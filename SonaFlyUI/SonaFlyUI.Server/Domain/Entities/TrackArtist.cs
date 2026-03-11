using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Domain.Entities;

public class TrackArtist
{
    public Guid TrackId { get; set; }
    public Guid ArtistId { get; set; }
    public TrackArtistRole Role { get; set; } = TrackArtistRole.Primary;

    public Track Track { get; set; } = null!;
    public Artist Artist { get; set; } = null!;
}
