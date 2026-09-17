namespace SonaFlyUI.Server.Application.Identification;

/// <summary>
/// Analysis snapshot contract (upgrade plan 4.1). Every candidate and proposal
/// refers to a specific track revision: track, library root, normalized path,
/// file size, and source modification time. A proposal becomes stale when the
/// file changes or the track is reassigned before approval.
/// </summary>
public sealed record FileRevisionKey(
    Guid TrackId,
    Guid LibraryRootId,
    string NormalizedPath,
    long FileSizeBytes,
    DateTime ModifiedUtcSource);

/// <summary>
/// Guards for proposal preconditions (plan 4.1 and 14.1). A changed file gets
/// a new analysis revision; old proposals are marked stale, never applied.
/// </summary>
public static class ProposalGuard
{
    public static bool IsStale(
        long expectedSize,
        DateTime expectedModifiedUtc,
        long currentSize,
        DateTime currentModifiedUtc)
    {
        return expectedSize != currentSize || expectedModifiedUtc != currentModifiedUtc;
    }

    public static bool IsStale(FileRevisionKey expected, long currentSize, DateTime currentModifiedUtc)
    {
        return IsStale(expected.FileSizeBytes, expected.ModifiedUtcSource, currentSize, currentModifiedUtc);
    }
}

/// <summary>
/// What an approval may change in the first shipped milestone (plan 4.3).
/// Genre stays out of automatic proposals because it is subjective, and file
/// paths change only in the later source-file phase.
/// </summary>
public static class ProposalFields
{
    public static readonly IReadOnlyList<string> AllowedCatalogFields =
    [
        "Title",
        "Artist",
        "Album",
        "TrackNumber",
        "DiscNumber",
        "Year"
    ];

    /// <summary>
    /// Confidence wording before empirical calibration (plan 8.3 and 18).
    /// Scores are shown with a band, never as a calibrated probability.
    /// </summary>
    public static string ConfidenceBandFor(double score, double highThreshold, double reviewThreshold)
    {
        if (score >= highThreshold)
        {
            return "High";
        }

        if (score >= reviewThreshold)
        {
            return "Review";
        }

        return "Unresolved";
    }
}

