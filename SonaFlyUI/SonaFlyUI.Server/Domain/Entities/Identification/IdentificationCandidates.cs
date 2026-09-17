namespace SonaFlyUI.Server.Domain.Entities.Identification;

/// <summary>
/// Cached provider response (plan 7.4 and 11). Keyed by a digest of the
/// normalized query so repeat runs hit the cache and offline review keeps
/// working. Never stores API keys in cache keys, raw URLs, or logs.
/// </summary>
public class ProviderCacheEntry : EntityBase
{
    /// <summary>For example AcoustID or MusicBrainz.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>SHA-256 digest of the normalized query plus request options.</summary>
    public string CacheKeyDigest { get; set; } = string.Empty;

    /// <summary>Describes the query and include shape, without secrets.</summary>
    public string? QueryShape { get; set; }

    public int? HttpStatus { get; set; }

    /// <summary>Bounded response JSON needed for analysis and offline review.</summary>
    public string? ResponseJson { get; set; }

    public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresUtc { get; set; }
    public string SchemaVersion { get; set; } = "v1";
    public bool IsNotFound { get; set; }
}

/// <summary>
/// One recording candidate for one analyzed file (plan 7.1 and 11).
/// All useful results are kept; the first result is never taken as final.
/// </summary>
public class RecordingCandidate : EntityBase
{
    public Guid WorkItemId { get; set; }
    public Guid? FileRevisionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? AcoustId { get; set; }
    public string? MusicBrainzRecordingId { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public double? RawProviderScore { get; set; }
    public double? FinalScore { get; set; }
    public string? ConfidenceBand { get; set; }
    public string ScoringVersion { get; set; } = "v0.1-recording-only";

    /// <summary>Per-component score breakdown plus winning margin, as JSON.</summary>
    public string? ScoringBreakdownJson { get; set; }

    public string? Conflicts { get; set; }
    public string? Provenance { get; set; }
    public DateTime FetchedUtc { get; set; } = DateTime.UtcNow;

    public IdentificationWorkItem WorkItem { get; set; } = null!;
}

/// <summary>
/// Candidate album group inside one library root (plan 9.1 and 11).
/// Groups never merge files from different roots merely because their
/// relative paths match.
/// </summary>
public class AlbumGroup : EntityBase
{
    public Guid JobId { get; set; }
    public Guid LibraryRootId { get; set; }
    public string GroupKey { get; set; } = string.Empty;

    /// <summary>Directory, disc-folder, and tag evidence for membership, as JSON.</summary>
    public string? EvidenceJson { get; set; }

    public string Status { get; set; } = "Draft";
    public int Version { get; set; } = 1;

    public IdentificationJob Job { get; set; } = null!;
    public ICollection<AlbumGroupMember> Members { get; set; } = new List<AlbumGroupMember>();
}

public class AlbumGroupMember : EntityBase
{
    public Guid GroupId { get; set; }
    public Guid TrackId { get; set; }
    public Guid? FileRevisionId { get; set; }
    public int? ProposedDisc { get; set; }
    public int? ProposedPosition { get; set; }

    public AlbumGroup Group { get; set; } = null!;
}

/// <summary>
/// One candidate release for an album group (plan 9.2 and 11). Recording and
/// exact-release decisions stay separate with independent scores.
/// </summary>
public class ReleaseCandidate : EntityBase
{
    public Guid GroupId { get; set; }
    public string MusicBrainzReleaseId { get; set; } = string.Empty;
    public string? MusicBrainzReleaseGroupId { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Date { get; set; }
    public string? Country { get; set; }
    public string? Label { get; set; }
    public string? CatalogNumber { get; set; }
    public string? Barcode { get; set; }
    public string? Status { get; set; }
    public string? Format { get; set; }
    public double? Score { get; set; }
    public string? ScoringBreakdownJson { get; set; }
    public bool IsAmbiguityMember { get; set; }
    public string? AmbiguitySetId { get; set; }
    public string ScoringVersion { get; set; } = "v0.1-recording-only";
    public string? Provenance { get; set; }

    public AlbumGroup Group { get; set; } = null!;
}

