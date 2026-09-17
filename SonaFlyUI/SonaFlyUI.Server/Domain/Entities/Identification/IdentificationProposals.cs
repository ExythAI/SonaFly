using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Domain.Entities.Identification;

/// <summary>
/// A catalog change that has not been applied yet (plan 11). Decisions are
/// stored apart from the mutable catalog so a rescoring job cannot erase an
/// approval. Old and new values plus the expected file revision travel with
/// the proposal so apply can revalidate everything in one transaction.
/// </summary>
public class MetadataProposal : EntityBase
{
    public Guid JobId { get; set; }
    public Guid? WorkItemId { get; set; }
    public Guid TrackId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? RecordingCandidateId { get; set; }
    public Guid? ReleaseCandidateId { get; set; }

    /// <summary>Preconditions: the file revision this proposal was built for.</summary>
    public long ExpectedFileSizeBytes { get; set; }

    public DateTime ExpectedModifiedUtcSource { get; set; }

    /// <summary>Comma-separated approved fields, subset of the allowed catalog fields.</summary>
    public string FieldMask { get; set; } = string.Empty;

    public string? OldTitle { get; set; }
    public string? NewTitle { get; set; }
    public string? OldArtist { get; set; }
    public string? NewArtist { get; set; }
    public string? OldAlbum { get; set; }
    public string? NewAlbum { get; set; }
    public int? OldTrackNumber { get; set; }
    public int? NewTrackNumber { get; set; }
    public int? OldDiscNumber { get; set; }
    public int? NewDiscNumber { get; set; }
    public int? OldYear { get; set; }
    public int? NewYear { get; set; }

    /// <summary>Evidence snapshot supporting the decision, as bounded JSON.</summary>
    public string? EvidenceSnapshotJson { get; set; }

    public ProposalStatus Status { get; set; } = ProposalStatus.Draft;
    public Guid? ReviewerUserId { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public DateTime? AppliedUtc { get; set; }

    /// <summary>Optimistic concurrency token, renewed on every state change.</summary>
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");

    public IdentificationJob Job { get; set; } = null!;
}

/// <summary>
/// Recovery journal for approved file work (plan 11). Required before any
/// source-file write ships; the catalog-only milestone writes rows here for
/// audit but never touches audio files.
/// </summary>
public class ChangeJournal : EntityBase
{
    public Guid TrackId { get; set; }
    public Guid? ProposalId { get; set; }
    public string? OldPath { get; set; }
    public string? NewPath { get; set; }
    public string? OldTagSnapshotJson { get; set; }
    public string? NewTagSnapshotJson { get; set; }
    public string? OldSha256 { get; set; }
    public string? NewSha256 { get; set; }
    public string? StepsAttemptedJson { get; set; }
    public string? StepsCompletedJson { get; set; }
    public string? Error { get; set; }
    public Guid? ReviewerUserId { get; set; }
}

