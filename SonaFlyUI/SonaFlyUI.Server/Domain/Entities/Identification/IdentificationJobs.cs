using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Domain.Entities.Identification;

/// <summary>
/// Durable analysis job for one library root (plan 11 and 12.1). Unlike the
/// in-memory scan queue, identification work is checkpointed per file in
/// SQLite so a restart resumes from the last valid checkpoint instead of
/// failing the whole run. One job belongs to exactly one root.
/// </summary>
public class IdentificationJob : EntityBase
{
    public Guid LibraryRootId { get; set; }

    /// <summary>Optional idempotency key supplied by the caller at creation.</summary>
    public string? ClientRequestKey { get; set; }

    public Guid? RequestedByUserId { get; set; }

    /// <summary>Root for a whole-root run, TrackIds for a targeted rerun.</summary>
    public string SelectionMode { get; set; } = "Root";

    /// <summary>Bounded JSON list of track IDs when SelectionMode is TrackIds.</summary>
    public string? SelectionJson { get; set; }

    public IdentificationJobStatus Status { get; set; } = IdentificationJobStatus.Queued;
    public string? Stage { get; set; }
    public int TotalItems { get; set; }
    public int HashedCount { get; set; }
    public int FingerprintedCount { get; set; }
    public int LookedUpCount { get; set; }
    public int ResolvedCount { get; set; }
    public int AmbiguousCount { get; set; }
    public int ErrorCount { get; set; }
    public int ProposalsReadyCount { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextRetryUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiryUtc { get; set; }
    public bool CancelRequested { get; set; }
    public string? LastCheckpoint { get; set; }

    /// <summary>Operator-safe summary. Never carries credentials or raw audio.</summary>
    public string? ErrorSummary { get; set; }

    public LibraryRoot LibraryRoot { get; set; } = null!;
    public ICollection<IdentificationWorkItem> WorkItems { get; set; } = new List<IdentificationWorkItem>();
}

/// <summary>
/// Bounded per-file unit of analysis work (plan 11). Each stage is idempotent
/// so repeats after a restart never create duplicate decisions or proposals.
/// </summary>
public class IdentificationWorkItem : EntityBase
{
    public Guid JobId { get; set; }
    public Guid TrackId { get; set; }
    public Guid? FileRevisionId { get; set; }
    public FileAnalysisStatus Status { get; set; } = FileAnalysisStatus.Pending;
    public string? Stage { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextRetryUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiryUtc { get; set; }

    /// <summary>Operator-safe explanation of the current state.</summary>
    public string? LastError { get; set; }

    public IdentificationErrorCategory ErrorCategory { get; set; } = IdentificationErrorCategory.Unknown;
    public bool Retryable { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public IdentificationJob Job { get; set; } = null!;
}

/// <summary>
/// Bounded, operator-safe error record (plan 11). No unbounded exception text
/// and no secret leakage.
/// </summary>
public class IdentificationError : EntityBase
{
    public Guid? JobId { get; set; }
    public Guid? WorkItemId { get; set; }
    public string Subsystem { get; set; } = string.Empty;
    public IdentificationErrorCategory Category { get; set; } = IdentificationErrorCategory.Unknown;
    public bool Retryable { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    public DateTime? NextRetryUtc { get; set; }
}

