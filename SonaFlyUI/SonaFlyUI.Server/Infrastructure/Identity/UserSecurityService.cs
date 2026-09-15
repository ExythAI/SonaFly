using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Identity;

/// <summary>
/// Why a user's sessions were ended. Recorded in the log only.
/// </summary>
public enum SessionRevocationReason
{
    PasswordChanged,
    PasswordReset,
    AccountDisabled,
    AccountDeleted,
    RolesChanged,
    TokenReplayDetected
}

public interface IUserSecurityService
{
    /// <summary>
    /// Re-checks a bearer token's subject against current database state.
    /// </summary>
    /// <returns>
    /// The user when the principal is still valid; <c>null</c> when the account is
    /// missing, disabled, or the security stamp has moved on since the token was issued.
    /// </returns>
    Task<ApplicationUser?> ResolveValidUserAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// Ends every session the user holds: rotates the security stamp so outstanding
    /// access tokens and stream tickets stop validating, and revokes their refresh tokens.
    /// </summary>
    Task RevokeAllSessionsAsync(ApplicationUser user, SessionRevocationReason reason, CancellationToken ct = default);

    /// <summary>Revokes every unrevoked refresh token belonging to the user.</summary>
    Task<int> RevokeRefreshTokensAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Revokes every token descended from one login — on logout, and on replay of a
    /// superseded token, which means the chain is in someone else's hands.
    /// </summary>
    /// <returns>How many live tokens were revoked.</returns>
    Task<int> RevokeRefreshTokenFamilyAsync(Guid familyId, CancellationToken ct = default);
}

public sealed class UserSecurityService : IUserSecurityService
{
    private readonly SonaFlyDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<UserSecurityService> _logger;

    public UserSecurityService(
        SonaFlyDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<UserSecurityService> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<ApplicationUser?> ResolveValidUserAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var rawId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawId, out var userId))
        {
            return null;
        }

        // AsNoTracking: this runs on every authenticated request and never writes.
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || !user.IsEnabled)
        {
            return null;
        }

        // A token issued before a password change, reset or role change carries a stale
        // stamp. Reject it rather than waiting for it to expire.
        var tokenStamp = principal.FindFirstValue(TokenService.SecurityStampClaim);
        if (!string.Equals(tokenStamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            return null;
        }

        return user;
    }

    public async Task RevokeAllSessionsAsync(
        ApplicationUser user, SessionRevocationReason reason, CancellationToken ct = default)
    {
        // Some Identity operations (ChangePassword, ResetPassword) rotate the stamp
        // themselves; doing it again is harmless and keeps every caller uniform.
        //
        // The stamp is what invalidates access tokens and stream tickets already issued, so
        // a failure here means the revocation did not actually happen. Reporting success
        // anyway would tell an administrator the sessions are gone when they are not.
        var stampResult = await _userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            var detail = string.Join("; ", stampResult.Errors.Select(e => e.Description));
            _logger.LogError(
                "Could not rotate the security stamp for user {UserId} ({Reason}): {Errors}",
                user.Id, reason, detail);
            throw new InvalidOperationException(
                $"Could not end the sessions for user {user.Id}: the security stamp was not rotated. {detail}");
        }

        var revoked = await RevokeRefreshTokensAsync(user.Id, ct);

        _logger.LogInformation(
            "Revoked all sessions for user {UserId} ({Reason}); {Count} refresh token(s) revoked.",
            user.Id, reason, revoked);
    }

    public async Task<int> RevokeRefreshTokensAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(rt => rt.RevokedUtc, now), ct);
    }

    public async Task<int> RevokeRefreshTokenFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var revoked = await _db.RefreshTokens
            .Where(rt => rt.FamilyId == familyId && rt.RevokedUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(rt => rt.RevokedUtc, now), ct);

        _logger.LogInformation(
            "Revoked {Count} live refresh token(s) in family {FamilyId}.", revoked, familyId);

        return revoked;
    }
}
