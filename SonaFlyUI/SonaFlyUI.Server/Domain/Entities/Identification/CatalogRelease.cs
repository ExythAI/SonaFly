namespace SonaFlyUI.Server.Domain.Entities.Identification;

/// <summary>
/// An approved exact release in SonaFly catalog terms (review backlog U01).
/// The ordinary <see cref="Album"/> row stays keyed by title plus album artist
/// and keeps serving unmatched music under a local or unknown identity; it is
/// never split or rewritten to fake an edition. When the whole-release
/// resolver distinguishes an exact edition and an administrator approves it,
/// one row here records that decision and the affected tracks link to it.
/// Two editions with the same title and artist therefore stay distinct, while
/// ambiguous editions simply get no row. No MBID is ever invented to separate
/// rows: unmatched music keeps a null release link.
/// </summary>
public class CatalogRelease : EntityBase
{
    public Guid LibraryRootId { get; set; }
    public Guid AlbumId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ArtistName { get; set; }
    public string? MusicBrainzReleaseId { get; set; }
    public string? MusicBrainzReleaseGroupId { get; set; }
    public string? Date { get; set; }
    public string? Country { get; set; }
    public string? Label { get; set; }
    public string? CatalogNumber { get; set; }
    public string? Barcode { get; set; }
    public string? Status { get; set; }
    public string? Format { get; set; }
    public Guid? SourceProposalId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedUtc { get; set; }

    public Album Album { get; set; } = null!;
}

