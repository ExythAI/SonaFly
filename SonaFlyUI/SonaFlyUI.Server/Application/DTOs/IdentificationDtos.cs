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

public record CreateIdentificationJobRequest(
    Guid LibraryRootId,
    Guid[]? TrackIds,
    string? IdempotencyKey
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

