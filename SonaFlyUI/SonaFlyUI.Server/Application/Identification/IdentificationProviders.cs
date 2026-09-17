using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Application.Identification;

/// <summary>
/// Job selection modes (upgrade plan 12.1). <see cref="Unanalyzed"/> is the
/// default: it picks only tracks whose current file has no completed analysis,
/// so re-running it after a scan catches new or changed files and skips work
/// that is already done.
/// </summary>
public static class IdentificationSelectionModes
{
    public const string Unanalyzed = "Unanalyzed";
    public const string Root = "Root";
    public const string TrackIds = "TrackIds";
}

/// <summary>What the runner can do for this pass, resolved once per batch.</summary>
public sealed record IdentificationCapabilities(bool HasFingerprintTool, string? AcoustIdKey)
{
    public bool CanLookUp => HasFingerprintTool && string.IsNullOrWhiteSpace(AcoustIdKey) == false;

    /// <summary>
    /// Work-item states that count as fully analysed with these capabilities.
    /// Without a fingerprint tool local evidence is as far as analysis goes;
    /// without an AcoustID key a fingerprint is.
    /// </summary>
    public IReadOnlyList<FileAnalysisStatus> CompletedStatuses => CanLookUp
        ? [FileAnalysisStatus.Resolved, FileAnalysisStatus.Ambiguous, FileAnalysisStatus.Unidentified]
        : HasFingerprintTool
            ? [FileAnalysisStatus.Fingerprinted, FileAnalysisStatus.Resolved, FileAnalysisStatus.Ambiguous, FileAnalysisStatus.Unidentified]
            : [FileAnalysisStatus.LocalEvidenceReady, FileAnalysisStatus.Fingerprinted, FileAnalysisStatus.Resolved, FileAnalysisStatus.Ambiguous, FileAnalysisStatus.Unidentified];

    /// <summary>Work-item states the runner should still advance.</summary>
    public IReadOnlyList<FileAnalysisStatus> ActionableStatuses => CanLookUp
        ? [FileAnalysisStatus.Pending, FileAnalysisStatus.LocalEvidenceReady, FileAnalysisStatus.Fingerprinted, FileAnalysisStatus.LookupPending, FileAnalysisStatus.CandidatesReady]
        : HasFingerprintTool
            ? [FileAnalysisStatus.Pending, FileAnalysisStatus.LocalEvidenceReady]
            : [FileAnalysisStatus.Pending];
}

/// <summary>
/// A stage failure with its diagnostic category and retry policy (plan 6.3
/// and 7.3). Messages are operator-safe: no secrets and no full audio.
/// </summary>
public sealed class IdentificationStepException : Exception
{
    public IdentificationStepException(
        IdentificationErrorCategory category,
        bool retryable,
        string message,
        TimeSpan? retryAfter = null,
        bool requiresAdministrator = false,
        Exception? inner = null)
        : base(message, inner)
    {
        Category = category;
        Retryable = retryable;
        RetryAfter = retryAfter;
        RequiresAdministrator = requiresAdministrator;
    }

    public IdentificationErrorCategory Category { get; }

    /// <summary>True when trying again later may succeed (network, rate limit, timeout).</summary>
    public bool Retryable { get; }

    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// The whole job cannot progress until an administrator fixes something,
    /// for example a rejected AcoustID key. The runner pauses the job.
    /// </summary>
    public bool RequiresAdministrator { get; }
}

public sealed record FingerprintResult(string Fingerprint, double DurationSeconds, string? ToolVersion);

/// <summary>Chromaprint fingerprinting of one file (plan 6.1).</summary>
public interface IFingerprintTool
{
    Task<FingerprintResult> ComputeAsync(string filePath, CancellationToken ct);
}

public sealed record AcoustIdRecording(
    string MusicBrainzRecordingId,
    string? Title,
    IReadOnlyList<string> Artists,
    double? DurationSeconds);

public sealed record AcoustIdResult(string AcoustId, double Score, IReadOnlyList<AcoustIdRecording> Recordings);

/// <summary>
/// AcoustID lookups (plan 7.1). Returns the validated raw response so the
/// caller can cache it; parse it with <c>AcoustIdClient.Parse</c>.
/// </summary>
public interface IAcoustIdClient
{
    Task<string> LookupJsonAsync(string apiKey, string fingerprint, int durationSeconds, CancellationToken ct);
}

public sealed record MusicBrainzRecording(
    string Id,
    string? Title,
    IReadOnlyList<string> Artists,
    double? DurationSeconds,
    IReadOnlyList<string> Isrcs);

/// <summary>
/// MusicBrainz recording lookups (plan 7.2). Returns the raw response for
/// caching, or null when MusicBrainz confirms the recording does not exist.
/// </summary>
public interface IMusicBrainzClient
{
    Task<string?> GetRecordingJsonAsync(string recordingId, CancellationToken ct);
}
