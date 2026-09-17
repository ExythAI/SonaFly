using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Domain.Entities.Identification;

/// <summary>
/// Stable file identity for one observed revision of a track (plan 5.3 and 11).
/// Whole-file SHA-256 distinguishes exact byte copies; it changes when tags or
/// cover art are rewritten, while the acoustic fingerprint serves a different
/// purpose and is stored separately. Never put a unique constraint on Sha256:
/// two files can legitimately hold identical bytes (plan 11).
/// </summary>
public class TrackFileRevision : EntityBase
{
    public Guid TrackId { get; set; }
    public Guid LibraryRootId { get; set; }
    public string NormalizedPath { get; set; } = string.Empty;
    public string? RelativePath { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime ModifiedUtcSource { get; set; }
    public string? Sha256 { get; set; }
    public DateTime? Sha256CalculatedUtc { get; set; }
    public string HashAlgorithm { get; set; } = "SHA-256";
    public string? Codec { get; set; }
    public int? Channels { get; set; }
    public int? BitDepth { get; set; }
    public DateTime ObservedUtc { get; set; } = DateTime.UtcNow;
    public string ParseStatus { get; set; } = "Ok";

    public Track Track { get; set; } = null!;
}

/// <summary>
/// The file tags exactly as read for one file revision (plan 5.1 and 11).
/// Original strings and their source are preserved; comparison code normalizes
/// copies elsewhere and never mutates this evidence.
/// </summary>
public class OriginalTagSnapshot : EntityBase
{
    public Guid FileRevisionId { get; set; }
    public string? Title { get; set; }
    public string? Album { get; set; }
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Genre { get; set; }
    public int? Year { get; set; }
    public int? TrackNumber { get; set; }
    public int? TrackTotal { get; set; }
    public int? DiscNumber { get; set; }
    public int? DiscTotal { get; set; }
    public string? FullDate { get; set; }
    public string? Isrc { get; set; }
    public string? Barcode { get; set; }
    public string? CatalogNumber { get; set; }
    public string? MusicBrainzRecordingId { get; set; }
    public string? MusicBrainzReleaseId { get; set; }
    public string? MusicBrainzReleaseGroupId { get; set; }

    /// <summary>
    /// Bounded format-specific raw fields as JSON, for evidence the mapped
    /// columns do not cover. Bounded so one exotic file cannot bloat the DB.
    /// </summary>
    public string? RawFieldsJson { get; set; }

    public string ParserVersion { get; set; } = "TagLibSharp";
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

    public TrackFileRevision FileRevision { get; set; } = null!;
}

/// <summary>
/// Chromaprint fingerprint for one file revision (plan 6.1 and 11).
/// FingerprintDigest is the SHA-256 of the fingerprint text, used for cache
/// keys and duplicate grouping without indexing the full text.
/// </summary>
public class AcousticFingerprint : EntityBase
{
    public Guid FileRevisionId { get; set; }
    public string Algorithm { get; set; } = "Chromaprint";
    public string? ToolVersion { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string FingerprintDigest { get; set; } = string.Empty;
    public double? DurationSeconds { get; set; }
    public FileAnalysisStatus Status { get; set; } = FileAnalysisStatus.Fingerprinted;
    public string? Error { get; set; }

    public TrackFileRevision FileRevision { get; set; } = null!;
}

