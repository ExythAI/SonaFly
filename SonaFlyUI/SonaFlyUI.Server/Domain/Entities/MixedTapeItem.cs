namespace SonaFlyUI.Server.Domain.Entities;

public class MixedTapeItem : EntityBase
{
    public Guid MixedTapeId { get; set; }
    public Guid TrackId { get; set; }
    public int SortOrder { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    public MixedTape MixedTape { get; set; } = null!;
    public Track Track { get; set; } = null!;
}
