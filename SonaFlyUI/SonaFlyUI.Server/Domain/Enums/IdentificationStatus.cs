namespace SonaFlyUI.Server.Domain.Enums;

/// <summary>
/// Independent analysis states from upgrade plan section 4.2.
/// These enums are persisted as strings (see SonaFlyDbContext), so new values
/// may be appended without renumbering stored rows.
/// </summary>
public enum IdentificationJobStatus
{
    Queued,
    Running,
    WaitingForNetwork,
    Paused,
    Completed,
    CompletedWithErrors,
    Cancelled,
    Failed
}

/// <summary>
/// Per-file stage of an identification work item (plan 4.2).
/// </summary>
public enum FileAnalysisStatus
{
    Pending,
    LocalEvidenceReady,
    Fingerprinted,
    LookupPending,
    CandidatesReady,
    Resolved,
    Ambiguous,
    Unidentified,
    RetryableError,
    PermanentError
}

/// <summary>
/// Lifecycle of a metadata proposal (plan 4.2). A proposal never silently
/// overwrites catalog fields; it moves through review states explicitly.
/// </summary>
public enum ProposalStatus
{
    Draft,
    ReadyForReview,
    Approved,
    Rejected,
    Applied,
    Stale,
    ApplyFailed
}

/// <summary>
/// Diagnostic category for an identification error. Stored with the retryable
/// flag and an operator-safe message; never carries secrets or full audio.
/// </summary>
public enum IdentificationErrorCategory
{
    Configuration,
    LocalHash,
    Probe,
    Fingerprint,
    AcoustIdLookup,
    MusicBrainzLookup,
    Scoring,
    Grouping,
    Apply,
    Cancelled,
    Unknown
}

