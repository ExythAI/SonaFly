namespace SonaFlyUI.Server.Domain.Entities;

public class PlaylistItem : EntityBase
{
    public Guid PlaylistId { get; set; }
    public Guid TrackId { get; set; }
    public int SortOrder { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

    public Playlist Playlist { get; set; } = null!;
    public Track Track { get; set; } = null!;
}
