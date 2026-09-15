namespace SonaFlyUI.Server.Domain.Entities;

public class RefreshToken : EntityBase
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Identifies the chain of tokens descended from one login. Rotation preserves it, so
    /// replay of a superseded token can revoke every descendant in one query.
    /// </summary>
    public Guid FamilyId { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public bool IsExpired => DateTime.UtcNow >= ExpiresUtc;
    public bool IsRevoked => RevokedUtc != null;
    public bool IsActive => !IsRevoked && !IsExpired;
}
