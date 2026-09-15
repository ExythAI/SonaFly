using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Per-address rate limit applied to the unauthenticated credential endpoints.
    /// Configured in Program.cs.
    /// </summary>
    public const string AuthRateLimitPolicy = "auth";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly SonaFlyDbContext _db;
    private readonly JwtSettings _jwtSettings;
    private readonly IUserSecurityService _security;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        SonaFlyDbContext db,
        IOptions<JwtSettings> jwtSettings,
        IUserSecurityService security,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _db = db;
        _jwtSettings = jwtSettings.Value;
        _security = security;
        _logger = logger;
    }

    [HttpPost("login")]
    [EnableRateLimiting(AuthRateLimitPolicy)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByNameAsync(request.Username)
                   ?? await _userManager.FindByEmailAsync(request.Username);

        if (user == null)
            return Unauthorized(new { detail = "Invalid credentials." });

        if (!user.IsEnabled)
            return Unauthorized(new { detail = "Account is disabled." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked,
                new { detail = "Account is temporarily locked after too many failed sign-in attempts." });

        if (!result.Succeeded)
            return Unauthorized(new { detail = "Invalid credentials." });

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles);
        var refreshToken = _tokenService.CreateRefreshToken();

        var refreshEntity = new RefreshToken
        {
            UserId = user.Id,
            // Each login starts its own family, so revoking one compromised chain does
            // not sign the user out of their other devices.
            FamilyId = Guid.NewGuid(),
            TokenHash = _tokenService.HashRefreshToken(refreshToken),
            ExpiresUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedUtc = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(refreshEntity);

        user.LastLoginUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var expiresUtc = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        var userInfo = new UserInfoDto(user.Id, user.UserName!, user.Email!, user.DisplayName, user.IsEnabled, roles, user.LastLoginUtc, user.CreatedUtc, user.MustChangePassword);

        // Browser clients get the refresh token only as an HttpOnly cookie, so no
        // script-readable copy of the seven-day credential ever exists.
        if (request.UseCookie)
        {
            RefreshTokenCookie.Append(Response, refreshToken, refreshEntity.ExpiresUtc);
            return Ok(new LoginResponse(accessToken, null, expiresUtc, userInfo));
        }

        return Ok(new LoginResponse(accessToken, refreshToken, expiresUtc, userInfo));
    }

    [HttpPost("refresh")]
    [EnableRateLimiting(AuthRateLimitPolicy)]
    public async Task<ActionResult<RefreshResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        // A browser presents the cookie; a native client puts the token in the body.
        var cookieToken = RefreshTokenCookie.Read(Request);
        var fromCookie = string.IsNullOrEmpty(request.RefreshToken) && cookieToken is not null;
        var presented = fromCookie ? cookieToken : request.RefreshToken;

        // Answer with a cookie when one was presented, and also when a browser is
        // trading in a token it used to keep in localStorage.
        var usingCookie = fromCookie || request.UseCookie;

        if (string.IsNullOrEmpty(presented))
            return Unauthorized(new { detail = "Invalid or expired refresh token." });

        // The cookie alone must not be enough to rotate a session from another origin.
        // SameSite=Strict already blocks that; this header is the second lock.
        if (fromCookie && !RefreshTokenCookie.HasClientHeader(Request))
            return Unauthorized(new { detail = "Invalid or expired refresh token." });

        var hash = _tokenService.HashRefreshToken(presented);

        // Everything from here to the commit is one indivisible step, holding the database
        // write lock from before the token is examined.
        //
        // Reading RevokedUtc and then saving a successor as two separate steps is not safe
        // in either direction: two refreshes racing on the same token could both see it
        // active and both mint a descendant, and a logout or password reset landing between
        // the read and the insert would revoke the family and then watch an unrevoked child
        // appear behind it — the family sweep only touches rows that exist when it runs.
        await using var transaction = await _db.BeginExclusiveAsync(ct);

        var stored = await _db.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);

        if (stored == null)
            return Unauthorized(new { detail = "Invalid or expired refresh token." });

        if (stored.IsRevoked)
        {
            // This token was already rotated away or revoked. Presenting it again means
            // a copy is in circulation, so the whole chain descended from that login goes.
            var replayRevoked = await _security.RevokeRefreshTokenFamilyAsync(stored.FamilyId, ct);
            _logger.LogWarning(
                "Refresh token replay detected for user {UserId}; revoked {Count} live token(s) in family {FamilyId}.",
                stored.UserId, replayRevoked, stored.FamilyId);
            await transaction.CommitAsync(ct);
            if (fromCookie) RefreshTokenCookie.Delete(Response);
            return Unauthorized(new { detail = "Invalid or expired refresh token." });
        }

        if (stored.IsExpired)
            return Unauthorized(new { detail = "Invalid or expired refresh token." });

        // Read the account inside the lock rather than through the token's navigation
        // property: the successor and the access token about to be minted must reflect the
        // account as it stands now, stamp included, not as it was when something else
        // loaded it.
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == stored.UserId, ct);

        if (user == null)
            return Unauthorized(new { detail = "Invalid or expired refresh token." });

        if (!user.IsEnabled)
            return Unauthorized(new { detail = "Account is disabled." });

        var newRefreshToken = _tokenService.CreateRefreshToken();
        var newHash = _tokenService.HashRefreshToken(newRefreshToken);
        var rotatedUtc = DateTime.UtcNow;

        // Claim the token by moving it from active to revoked in one conditional write. A
        // caller that updates no rows did not hold a live token, whatever it read a moment
        // ago, and must not be given a descendant.
        var claimed = await _db.RefreshTokens
            .Where(rt => rt.Id == stored.Id && rt.RevokedUtc == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(rt => rt.RevokedUtc, rotatedUtc)
                .SetProperty(rt => rt.ReplacedByTokenHash, newHash), ct);

        if (claimed == 0)
        {
            var lostRaceRevoked = await _security.RevokeRefreshTokenFamilyAsync(stored.FamilyId, ct);
            _logger.LogWarning(
                "Refresh token was consumed or revoked concurrently for user {UserId}; " +
                "revoked {Count} live token(s) in family {FamilyId}.",
                stored.UserId, lostRaceRevoked, stored.FamilyId);
            await transaction.CommitAsync(ct);
            if (fromCookie) RefreshTokenCookie.Delete(Response);
            return Unauthorized(new { detail = "Invalid or expired refresh token." });
        }

        var newRefreshEntity = new RefreshToken
        {
            UserId = stored.UserId,
            // Rotation stays inside the family it came from.
            FamilyId = stored.FamilyId,
            TokenHash = newHash,
            ExpiresUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedUtc = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(newRefreshEntity);

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var expiresUtc = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        if (usingCookie)
        {
            RefreshTokenCookie.Append(Response, newRefreshToken, newRefreshEntity.ExpiresUtc);
            return Ok(new RefreshResponse(accessToken, null, expiresUtc));
        }

        return Ok(new RefreshResponse(accessToken, newRefreshToken, expiresUtc));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        var presented = string.IsNullOrEmpty(request.RefreshToken)
            ? RefreshTokenCookie.Read(Request)
            : request.RefreshToken;

        // Always clear the cookie, even if the token is already unknown to us, so a
        // browser cannot be left holding a credential it will keep presenting.
        RefreshTokenCookie.Delete(Response);

        if (string.IsNullOrEmpty(presented))
            return Ok();

        var hash = _tokenService.HashRefreshToken(presented);

        // Same lock as rotation, for the same reason: a refresh that is mid-flight has
        // already taken it, so its descendant exists by the time the sweep below runs and is
        // revoked with the rest. Without this the sweep could run first and miss the child.
        await using var transaction = await _db.BeginExclusiveAsync(ct);

        var stored = await _db.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);
        if (stored != null)
        {
            // Revoking the family rather than the single token means a rotation that was
            // in flight during logout cannot leave a usable descendant behind.
            await _security.RevokeRefreshTokenFamilyAsync(stored.FamilyId, ct);
        }

        await transaction.CommitAsync(ct);
        return Ok();
    }

    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<ActionResult<UserInfoDto>> Me()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new UserInfoDto(user.Id, user.UserName!, user.Email!, user.DisplayName, user.IsEnabled, roles, user.LastLoginUtc, user.CreatedUtc, user.MustChangePassword));
    }

    [HttpPost("change-password")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { detail = string.Join(", ", result.Errors.Select(e => e.Description)) });

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded)
                return BadRequest(new { detail = string.Join(", ", update.Errors.Select(e => e.Description)) });
        }

        // A changed password ends every session. Rotating the security stamp is what
        // kills the access tokens and stream tickets already in the wild; revoking the
        // refresh tokens stops them being renewed. Both happen before the replacement
        // pair is minted, so the new tokens carry the new stamp.
        await _security.RevokeAllSessionsAsync(user, SessionRevocationReason.PasswordChanged);

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles);
        var refreshToken = _tokenService.CreateRefreshToken();
        var refreshExpiresUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            TokenHash = _tokenService.HashRefreshToken(refreshToken),
            ExpiresUtc = refreshExpiresUtc,
            CreatedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var expiresUtc = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        // A caller that arrived with the cookie keeps the cookie; the old one was just
        // revoked, so it has to be replaced rather than left to go stale.
        if (RefreshTokenCookie.Read(Request) is not null)
        {
            RefreshTokenCookie.Append(Response, refreshToken, refreshExpiresUtc);
            return Ok(new ChangePasswordResponse(accessToken, null, expiresUtc));
        }

        return Ok(new ChangePasswordResponse(accessToken, refreshToken, expiresUtc));
    }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// Replacement credentials issued because changing the password revoked the caller's
/// existing tokens.
/// </summary>
public record ChangePasswordResponse(string AccessToken, string? RefreshToken, DateTime ExpiresUtc);
