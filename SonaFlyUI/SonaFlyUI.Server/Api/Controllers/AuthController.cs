using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly SonaFlyDbContext _db;
    private readonly JwtSettings _jwtSettings;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        SonaFlyDbContext db,
        IOptions<JwtSettings> jwtSettings)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _db = db;
        _jwtSettings = jwtSettings.Value;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByNameAsync(request.Username)
                   ?? await _userManager.FindByEmailAsync(request.Username);

        if (user == null)
            return Unauthorized(new { detail = "Invalid credentials." });

        if (!user.IsEnabled)
            return Unauthorized(new { detail = "Account is disabled." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
            return Unauthorized(new { detail = "Invalid credentials." });

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.CreateAccessToken(user, roles);
        var refreshToken = _tokenService.CreateRefreshToken();

        var refreshEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashRefreshToken(refreshToken),
            ExpiresUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedUtc = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(refreshEntity);

        user.LastLoginUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var expiresUtc = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        var userInfo = new UserInfoDto(user.Id, user.UserName!, user.Email!, user.DisplayName, user.IsEnabled, roles, user.LastLoginUtc, user.CreatedUtc);

        return Ok(new LoginResponse(accessToken, refreshToken, expiresUtc, userInfo));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var hash = _tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);

        if (stored == null || !stored.IsActive)
            return Unauthorized(new { detail = "Invalid or expired refresh token." });

        if (!stored.User.IsEnabled)
            return Unauthorized(new { detail = "Account is disabled." });

        // Rotate: revoke old, create new
        stored.RevokedUtc = DateTime.UtcNow;
        var newRefreshToken = _tokenService.CreateRefreshToken();
        var newHash = _tokenService.HashRefreshToken(newRefreshToken);
        stored.ReplacedByTokenHash = newHash;

        var newRefreshEntity = new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = newHash,
            ExpiresUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedUtc = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(newRefreshEntity);

        var roles = await _userManager.GetRolesAsync(stored.User);
        var accessToken = _tokenService.CreateAccessToken(stored.User, roles);
        await _db.SaveChangesAsync(ct);

        var expiresUtc = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        return Ok(new RefreshResponse(accessToken, newRefreshToken, expiresUtc));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        var hash = _tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);
        if (stored != null)
        {
            stored.RevokedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
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
        return Ok(new UserInfoDto(user.Id, user.UserName!, user.Email!, user.DisplayName, user.IsEnabled, roles, user.LastLoginUtc, user.CreatedUtc));
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

        return Ok(new { message = "Password changed successfully." });
    }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
