namespace SonaFlyUI.Server.Domain.Entities;

/// <summary>One listen: a user started a track. The basis of "recently played" and "most played".</summary>
public class PlayEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TrackId { get; set; }
    public DateTime PlayedUtc { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
    public Track Track { get; set; } = null!;
}

/// <summary>A track a user marked as a favourite.</summary>
public class FavoriteTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TrackId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
    public Track Track { get; set; } = null!;
}
