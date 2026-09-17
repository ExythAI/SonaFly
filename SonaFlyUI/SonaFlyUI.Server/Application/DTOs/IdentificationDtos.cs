namespace SonaFlyUI.Server.Application.DTOs;

/// <summary>
/// Safe configuration status for the review UI (plan 13.2). Shows blockers
/// before a job is queued. Never contains secret values.
/// </summary>
public record IdentificationStatusDto(
    bool Enabled,
    bool AcoustIdConfigured,
    string AcoustIdSource,
    bool FpcalcConfigured,
    bool FfmpegConfigured,
    bool LocalOnlyMode,
    IReadOnlyList<string> Blockers,
    string Message
);

/// <summary>
/// Safe AcoustID key state for the admin UI. The key value itself is never
/// exposed; Source is None, Configuration, or Database.
/// </summary>
public record IdentificationKeyStateDto(bool Configured, string Source);

public record SetAcoustIdKeyRequest(string Key);

/// <param name="Mode">
/// "Unanalyzed" (the default) picks tracks whose current file has no completed
/// analysis; "Root" re-analyses every present track, reusing stored hashes,
/// fingerprints, and cached lookups. Ignored when TrackIds is given.
/// </param>
public record CreateIdentificationJobRequest(
    Guid LibraryRootId,
    Guid[]? TrackIds,
    string? IdempotencyKey,
    string? Mode = null
);

public record RecordingCandidateDto(
    string Provider,
    string? AcoustId,
    string? MusicBrainzRecordingId,
    string? Title,
    string? Artist,
    double? ProviderScore,
    double? Score,
    string? ConfidenceBand,
    string ScoringVersion,
    string? ScoringBreakdownJson,
    string? Conflicts,
    string? Provenance
);

public record IdentificationItemDetailDto(
    Guid Id,
    Guid JobId,
    Guid TrackId,
    string Status,
    string? Stage,
    string? LastError,
    string? CatalogTitle,
    string? CatalogArtist,
    string? CatalogAlbum,
    string? RelativePath,
    string? Sha256,
    double? FingerprintDurationSeconds,
    string? TagTitle,
    string? TagArtist,
    string? TagAlbum,
    string? TagRecordingId,
    IReadOnlyList<RecordingCandidateDto> Candidates
);

public record IdentificationJobDto(
    Guid Id,
    Guid LibraryRootId,
    string? LibraryRootName,
    string Status,
    string? Stage,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    int TotalItems,
    int HashedCount,
    int FingerprintedCount,
    int LookedUpCount,
    int ResolvedCount,
    int AmbiguousCount,
    int ErrorCount,
    int ProposalsReadyCount,
    string? ErrorSummary
);

public record IdentificationWorkItemDto(
    Guid Id,
    Guid JobId,
    Guid TrackId,
    string Status,
    string? Stage,
    int AttemptCount,
    string? LastError,
    DateTime? NextRetryUtc
);

/// <summary>
/// Exact byte-duplicate report by SHA-256 (plan 10.1). Same bytes are one
/// category; same recording across encodings is a later, separate category.
/// Nothing is ever deleted automatically.
/// </summary>
public record DuplicateGroupDto(
    string Sha256,
    int FileCount,
    IReadOnlyList<Guid> TrackIds
);

