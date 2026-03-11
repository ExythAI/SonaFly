namespace SonaFlyUI.Server.Domain.Entities;

public class Track : EntityBase
{
    public Guid LibraryRootId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public double? DurationSeconds { get; set; }
    public int? BitRateKbps { get; set; }
    public int? SampleRateHz { get; set; }
    public int? TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public Guid? AlbumId { get; set; }
    public Guid? PrimaryArtistId { get; set; }
    public string? Genre { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public DateTime ModifiedUtcSource { get; set; }
    public bool IsIndexed { get; set; }
    public bool IsMissing { get; set; }

    public LibraryRoot LibraryRoot { get; set; } = null!;
    public Album? Album { get; set; }
    public Artist? PrimaryArtist { get; set; }
    public ICollection<TrackArtist> TrackArtists { get; set; } = new List<TrackArtist>();
    public ICollection<TrackGenre> TrackGenres { get; set; } = new List<TrackGenre>();
    public ICollection<PlaylistItem> PlaylistItems { get; set; } = new List<PlaylistItem>();
}
