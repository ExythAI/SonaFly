namespace SonaFlyUI.Server.Domain.Entities;

/// <summary>
/// Deny-list entry: hides a specific album, artist, or genre from a user.
/// By default users see everything; restrictions remove visibility.
/// </summary>
public class UserRestriction : EntityBase
{
    public Guid UserId { get; set; }
    public RestrictionType RestrictionType { get; set; }
    public Guid TargetId { get; set; }

    /// <summary>Cached display name for admin UI (e.g., "Dark Side of the Moon")</summary>
    public string? TargetName { get; set; }

    public ApplicationUser? User { get; set; }
}

public enum RestrictionType
{
    Album = 0,
    Artist = 1,
    Genre = 2
}
