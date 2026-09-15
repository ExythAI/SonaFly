using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identity;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = IdentitySeeder.AdminRole)]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly SonaFlyDbContext _db;
    private readonly IUserSecurityService _security;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        SonaFlyDbContext db,
        IUserSecurityService security)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _security = security;
    }

    private Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    // ── Guards ──

    private static ActionResult Problem(params string[] errors) =>
        new BadRequestObjectResult(new { errors });

    private static ActionResult Problem(IEnumerable<IdentityError> errors) =>
        new BadRequestObjectResult(new { errors = errors.Select(e => e.Description) });

    /// <summary>
    /// Another administrator held the write lock for longer than this request was willing to
    /// wait. Nothing was changed, and the caller can simply try again.
    /// </summary>
    private static ActionResult WriteConflict() =>
        new ConflictObjectResult(new { errors = new[] { "Another administrative change was in progress. Try again." } });

    /// <summary>
    /// Rejects anything that is not one of the roles this application defines. Without
    /// this, an unrecognised role name would strip the user's existing roles and then
    /// fail to grant anything, leaving them with none.
    /// </summary>
    private async Task<string?> ValidateRoleAsync(string role)
    {
        if (!IdentitySeeder.AssignableRoles.Contains(role, StringComparer.Ordinal))
        {
            return $"'{role}' is not a valid role. Valid roles are: {string.Join(", ", IdentitySeeder.AssignableRoles)}.";
        }

        // The allowlist is the policy; this catches a database that was never seeded.
        return await _roleManager.RoleExistsAsync(role)
            ? null
            : $"The role '{role}' does not exist on this server.";
    }

    /// <summary>
    /// Whether this user is the only administrator who can still sign in. Losing them
    /// leaves nobody able to administer the server, which cannot be undone through the API.
    /// </summary>
    /// <remarks>
    /// Only meaningful inside <see cref="SerializedWrites.BeginExclusiveAsync"/>. On its own
    /// this is a read of a moment that has already passed: two administrators disabling each
    /// other would both see a second enabled administrator and both proceed.
    /// </remarks>
    private async Task<bool> IsLastEnabledAdminAsync(ApplicationUser user)
    {
        if (!await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole)) return false;
        if (!user.IsEnabled) return false;

        var admins = await _userManager.GetUsersInRoleAsync(IdentitySeeder.AdminRole);
        return admins.Count(a => a.IsEnabled) <= 1;
    }

    /// <summary>
    /// Runs a check-then-change against the last-administrator invariant as one indivisible
    /// step: the write lock is held from before the guard query until the mutation commits,
    /// so a competing request cannot slip between them. The body must load the user it acts
    /// on inside this scope, because a user read beforehand describes state the lock does
    /// not cover.
    ///
    /// Only a success status commits; any refusal rolls the whole thing back.
    /// </summary>
    private async Task<IActionResult> SerializedAsync(Func<Task<IActionResult>> body)
    {
        try
        {
            await using var transaction = await _db.BeginExclusiveAsync();

            var result = await body();

            var status = (result as IStatusCodeActionResult)?.StatusCode ?? StatusCodes.Status200OK;
            if (status is >= 200 and < 300)
            {
                await transaction.CommitAsync();
            }

            return result;
        }
        catch (Exception ex) when (SerializedWrites.IsWriteConflict(ex))
        {
            return WriteConflict();
        }
    }

    // ── Reads ──

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserInfoDto>>> GetAll()
    {
        var users = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        var result = new List<UserInfoDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            result.Add(ToDto(u, roles));
        }
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserInfoDto>> GetById(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();
        return Ok(ToDto(user, await _userManager.GetRolesAsync(user)));
    }

    private static UserInfoDto ToDto(ApplicationUser u, IEnumerable<string> roles) =>
        new(u.Id, u.UserName!, u.Email!, u.DisplayName, u.IsEnabled, roles,
            u.LastLoginUtc, u.CreatedUtc, u.MustChangePassword);

    // ── Writes ──

    [HttpPost]
    public async Task<ActionResult<UserInfoDto>> Create([FromBody] CreateUserRequest request)
    {
        var role = string.IsNullOrWhiteSpace(request.Role) ? IdentitySeeder.UserRole : request.Role;
        if (await ValidateRoleAsync(role) is { } roleError) return Problem(roleError);

        var user = new ApplicationUser
        {
            UserName = request.UserName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            IsEnabled = true,
            // The administrator chose this password, so the holder replaces it on first use.
            MustChangePassword = true
        };

        // Create and grant together: an account that exists with no role is not a
        // usable outcome, so roll the whole thing back if the grant fails.
        await using var transaction = await _db.Database.BeginTransactionAsync();

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded) return Problem(result.Errors);

        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded) return Problem(roleResult.Errors);

        await transaction.CommitAsync();

        var roles = await _userManager.GetRolesAsync(user);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ToDto(user, roles));
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request) => SerializedAsync(async () =>
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        var roleChanging = request.Role != null &&
                           (!currentRoles.Contains(request.Role) || currentRoles.Count > 1);

        // Validate before touching anything, so a bad role cannot leave the profile
        // half-updated.
        if (request.Role != null && await ValidateRoleAsync(request.Role) is { } roleError)
            return Problem(roleError);

        if (roleChanging && request.Role != IdentitySeeder.AdminRole && await IsLastEnabledAdminAsync(user))
            return Problem("This is the only enabled administrator. Promote another user before demoting this one.");

        if (request.Email != null) user.Email = request.Email;
        if (request.DisplayName != null) user.DisplayName = request.DisplayName;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded) return Problem(result.Errors);

        if (roleChanging)
        {
            // Grant first, then remove what is obsolete: if the grant fails the user
            // keeps the roles they had rather than ending up with none.
            var addResult = await _userManager.AddToRoleAsync(user, request.Role!);
            if (!addResult.Succeeded) return Problem(addResult.Errors);

            var obsolete = currentRoles.Where(r => r != request.Role).ToArray();
            if (obsolete.Length > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, obsolete);
                if (!removeResult.Succeeded) return Problem(removeResult.Errors);
            }

            // Roles live in the access token, so a demotion would otherwise keep its
            // old authority until the token expired.
            await _security.RevokeAllSessionsAsync(user, SessionRevocationReason.RolesChanged);
        }

        return NoContent();
    });

    [HttpPost("{id:guid}/disable")]
    public Task<IActionResult> Disable(Guid id) => SerializedAsync(async () =>
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();

        // Refusing self-disable is not enough on its own: two administrators disabling each
        // other both pass this check. The invariant below is what actually holds the line,
        // and it holds because the write lock is already ours.
        if (id == CurrentUserId)
            return Problem("You cannot disable your own account.");

        if (await IsLastEnabledAdminAsync(user))
            return Problem("This is the only enabled administrator. Enable another administrator first.");

        user.IsEnabled = false;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded) return Problem(result.Errors);

        await _security.RevokeAllSessionsAsync(user, SessionRevocationReason.AccountDisabled);
        return NoContent();
    });

    [HttpPost("{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();

        user.IsEnabled = true;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded) return Problem(result.Errors);

        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded) return Problem(result.Errors);

        // An administrator-chosen password is temporary: the holder replaces it at next
        // sign-in, and every session issued under the old password ends now.
        user.MustChangePassword = true;
        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded) return Problem(update.Errors);

        await _security.RevokeAllSessionsAsync(user, SessionRevocationReason.PasswordReset);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id) => SerializedAsync(async () =>
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound();

        if (id == CurrentUserId)
            return Problem("You cannot delete your own account.");

        if (await IsLastEnabledAdminAsync(user))
            return Problem("This is the only enabled administrator. Promote another user before deleting this one.");

        // Drop the refresh tokens first: once the row is gone the cascade may or may not
        // have run, and a live token must never outlive the account.
        await _security.RevokeRefreshTokensAsync(user.Id);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded) return Problem(result.Errors);

        return NoContent();
    });
}
