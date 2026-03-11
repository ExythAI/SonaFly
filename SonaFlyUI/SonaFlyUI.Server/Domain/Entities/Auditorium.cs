namespace SonaFlyUI.Server.Domain.Entities;

/// <summary>
/// A shared listening room where multiple users hear the same music simultaneously.
/// Only administrators can create auditoriums.
/// </summary>
public class Auditorium : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public bool IsActive { get; set; } = true;

    public ApplicationUser? CreatedByUser { get; set; }
    public ICollection<AuditoriumQueueItem> QueueItems { get; set; } = [];
}

/// <summary>
/// A track queued in an auditorium. Max 100 items per auditorium.
/// </summary>
public class AuditoriumQueueItem : EntityBase
{
    public Guid AuditoriumId { get; set; }
    public Guid TrackId { get; set; }
    public Guid QueuedByUserId { get; set; }
    public int Position { get; set; }

    public Auditorium? Auditorium { get; set; }
    public Track? Track { get; set; }
    public ApplicationUser? QueuedByUser { get; set; }
}
