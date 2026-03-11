namespace SonaFlyUI.Server.Domain.Entities;

public class Album : EntityBase
{
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public Guid? AlbumArtistId { get; set; }
    public int? Year { get; set; }
    public int? DiscCount { get; set; }
    public int? TrackCount { get; set; }
    public string? GenreSummary { get; set; }
    public Guid? ArtworkId { get; set; }

    public Artist? AlbumArtist { get; set; }
    public ArtworkAsset? Artwork { get; set; }
    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}
