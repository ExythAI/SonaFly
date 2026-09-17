namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Hash-based move matching for one newly discovered file (review backlog U03).
/// The current scan keys tracks by path and mints a new Track for a moved
/// file. Before a new Track is committed, a new path may reclaim an old
/// TrackId only on strong evidence: the same SHA-256, exactly one claimant,
/// and a traversal report that verifies the old path is really gone.
/// A move is never inferred from title, artist, size, or filename alone, and
/// repeated scans stay idempotent because the decision depends only on hashes
/// and verified absence, not on how many times the scan ran.
/// </summary>
public sealed record MoveCandidate(
    Guid TrackId,
    string? Sha256,
    bool IsMissing,
    bool OldPathVerifiedAbsent);

public sealed record MoveMatchResult(bool IsMatch, Guid? TrackId, string Reason)
{
    public static MoveMatchResult NoMatch(string reason) => new(false, null, reason);

    public static MoveMatchResult Match(Guid trackId) =>
        new(true, trackId, "Unambiguous same-hash match with verified absence of the old path.");
}

public static class MoveMatcher
{
    public static MoveMatchResult TryMatch(
        string? newFileSha256,
        IReadOnlyList<MoveCandidate> sameRootCandidates)
    {
        if (string.IsNullOrWhiteSpace(newFileSha256))
        {
            return MoveMatchResult.NoMatch("The new file has no hash; moves are never inferred without one.");
        }

        var sameHash = sameRootCandidates
            .Where(c => string.Equals(c.Sha256, newFileSha256, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sameHash.Count == 0)
        {
            return MoveMatchResult.NoMatch("No same-hash track exists in this root.");
        }

        if (sameHash.Any(c => c.IsMissing == false))
        {
            return MoveMatchResult.NoMatch(
                "The hash is still present at another path; the new file is a duplicate copy, not a move.");
        }

        var verifiablyGone = sameHash.Where(c => c.OldPathVerifiedAbsent).ToList();
        if (verifiablyGone.Count == 0)
        {
            return MoveMatchResult.NoMatch(
                "No same-hash track is verifiably absent; a partial or offline scan never claims a move.");
        }

        if (verifiablyGone.Count > 1)
        {
            return MoveMatchResult.NoMatch(
                "Several same-hash tracks are gone; they are duplicates, and guessing which one moved is refused.");
        }

        return MoveMatchResult.Match(verifiablyGone[0].TrackId);
    }
}

