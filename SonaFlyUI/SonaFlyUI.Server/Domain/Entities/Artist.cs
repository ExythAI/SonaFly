namespace SonaFlyUI.Server.Domain.Entities;

public class Artist : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? SortName { get; set; }

    /// <summary>Name folded for searching; kept in step by the database context.</summary>
    public string SearchName { get; set; } = string.Empty;
    public string? MusicBrainzId { get; set; }
    public Guid? ArtworkId { get; set; }

    public ArtworkAsset? Artwork { get; set; }
    public ICollection<Album> Albums { get; set; } = new List<Album>();
    public ICollection<Track> PrimaryTracks { get; set; } = new List<Track>();
    public ICollection<TrackArtist> TrackArtists { get; set; } = new List<TrackArtist>();
}
