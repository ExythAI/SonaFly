namespace SonaFlyUI.Server.Domain.Entities.Identification;

/// <summary>
/// Field-level approved catalog override for one track (review backlog U04).
/// A later full scan must preserve approved values instead of restoring old
/// tag strings, so approvals live here rather than only in display strings.
/// One active row exists per track and field; clearing an override deletes
/// the row, with history retained in the proposal and change journal.
/// Associations (approved album or artist) are stored as IDs in
/// <see cref="ValueId"/>, not as strings, so the scan can reuse the exact
/// approved rows instead of re-creating them by name.
/// </summary>
public class CatalogOverride : EntityBase
{
    public Guid TrackId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string? ValueText { get; set; }
    public Guid? ValueId { get; set; }
    public Guid? ProposalId { get; set; }

    /// <summary>
    /// Audio identity the approval was built on. A changed fingerprint means
    /// the recording may have been replaced and the override needs re-review;
    /// a changed whole-file hash alone (tag or artwork edit) does not.
    /// </summary>
    public string? SourceFingerprintDigest { get; set; }

    public long SourceFileSizeBytes { get; set; }
    public DateTime SourceModifiedUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime ApprovedUtc { get; set; } = DateTime.UtcNow;

    public Track Track { get; set; } = null!;
}

