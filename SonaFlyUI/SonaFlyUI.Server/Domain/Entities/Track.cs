namespace SonaFlyUI.Server.Domain.Entities;

using SonaFlyUI.Server.Domain.Entities.Identification;

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

    /// <summary>
    /// Approved exact release (backlog U01). Set only by applying an approved
    /// release decision; the ordinary scan never writes or clears it, so a
    /// full metadata rescan preserves edition identity. Null means the track
    /// carries the shared local or unknown Album identity, including while
    /// its edition is still ambiguous.
    /// </summary>
    public Guid? CatalogReleaseId { get; set; }

    public string? Genre { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public DateTime ModifiedUtcSource { get; set; }
    public bool IsIndexed { get; set; }
    public bool IsMissing { get; set; }

    public LibraryRoot LibraryRoot { get; set; } = null!;
    public Album? Album { get; set; }
    public Artist? PrimaryArtist { get; set; }
    public CatalogRelease? CatalogRelease { get; set; }
    public ICollection<TrackArtist> TrackArtists { get; set; } = new List<TrackArtist>();
    public ICollection<TrackGenre> TrackGenres { get; set; } = new List<TrackGenre>();
    public ICollection<PlaylistItem> PlaylistItems { get; set; } = new List<PlaylistItem>();
}
