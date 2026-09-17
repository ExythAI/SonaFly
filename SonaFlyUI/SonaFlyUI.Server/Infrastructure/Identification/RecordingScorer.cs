using System.Globalization;
using System.Text.Json;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>One possible recording for a file, before scoring.</summary>
public sealed record RecordingEvidence(
    string Provider,
    string? AcoustId,
    string? MusicBrainzRecordingId,
    string? Title,
    IReadOnlyList<string> Artists,
    double? DurationSeconds,
    double? ProviderScore,
    string Provenance);

/// <summary>What the file itself says, used as supporting evidence only.</summary>
public sealed record FileEvidence(
    string? TagTitle,
    string? TagArtist,
    string? TagRecordingId,
    double? AudioDurationSeconds);

public sealed record ScoredRecording(
    RecordingEvidence Evidence,
    double Score,
    string Band,
    string BreakdownJson,
    string? Conflicts);

public sealed record RecordingDecision(
    FileAnalysisStatus Outcome,
    IReadOnlyList<ScoredRecording> Candidates,
    double? WinningMargin);

/// <summary>
/// Deterministic, versioned recording scorer (upgrade plan 8.1 to 8.5).
/// The acoustic match dominates because the library's tags are exactly what
/// may be wrong; tags only nudge a score and are recorded as conflicts rather
/// than punished. Every component is stored, and a score is shown with a band,
/// never as a calibrated probability. Unknown and ambiguous are real outcomes.
/// </summary>
public static class RecordingScorer
{
    public const string Version = "v0.2-recording";

    public const double ProviderWeight = 0.70;
    public const double DurationWeight = 0.15;
    public const double TitleWeight = 0.05;
    public const double ArtistWeight = 0.05;
    public const double EmbeddedIdWeight = 0.05;

    /// <summary>Required lead over the next distinct recording to resolve automatically.</summary>
    public const double MinimumMargin = 0.10;

    /// <summary>Seconds of disagreement tolerated for encoder padding and pregaps.</summary>
    public const double DurationTolerance = 3;

    /// <summary>Beyond this many seconds the durations contradict each other.</summary>
    public const double DurationContradiction = 15;

    public static RecordingDecision Decide(
        FileEvidence file,
        IReadOnlyList<RecordingEvidence> candidates,
        double highThreshold,
        double reviewThreshold)
    {
        // One entry per recording: the same MBID can arrive from several
        // AcoustID results or from the embedded tag; keep its strongest form.
        var distinct = candidates
            .GroupBy(c => c.MusicBrainzRecordingId ?? $"text:{c.AcoustId}:{c.Title}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.ProviderScore ?? -1).First())
            .ToList();

        var scored = distinct
            .Select(c => Score(file, c, highThreshold, reviewThreshold))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => string.IsNullOrWhiteSpace(s.Evidence.Title))
            .ThenBy(s => s.Evidence.MusicBrainzRecordingId, StringComparer.Ordinal)
            .ToList();

        // Only a candidate with a recording ID can become a decision (plan 7.5).
        var resolvable = scored.Where(s => s.Evidence.MusicBrainzRecordingId != null).ToList();
        if (resolvable.Count == 0)
        {
            return new RecordingDecision(FileAnalysisStatus.Unidentified, scored, null);
        }

        var best = resolvable[0];

        // MusicBrainz often holds one song as several recording entries, and
        // AcoustID returns them all with the same score. Title and artist
        // proposals do not depend on which entry wins, so the lead is measured
        // against the next *different* song, not against its duplicates.
        var competitor = resolvable.Skip(1).FirstOrDefault(s => IsSameSong(best.Evidence, s.Evidence) == false);
        double? margin = competitor == null ? null : best.Score - competitor.Score;
        var clearLead = margin == null || margin >= MinimumMargin;
        var contradicted = best.Conflicts?.Contains("duration", StringComparison.Ordinal) == true;

        var outcome = best.Score >= highThreshold && clearLead && contradicted == false
            ? FileAnalysisStatus.Resolved
            : best.Score >= reviewThreshold
                ? FileAnalysisStatus.Ambiguous
                : FileAnalysisStatus.Unidentified;

        return new RecordingDecision(outcome, scored, margin);
    }

    private static ScoredRecording Score(FileEvidence file, RecordingEvidence candidate, double high, double review)
    {
        var conflicts = new List<string>();

        var provider = Math.Clamp(candidate.ProviderScore ?? 0, 0, 1);

        double duration = 0.5;
        if (file.AudioDurationSeconds is { } audio && candidate.DurationSeconds is { } known)
        {
            var delta = Math.Abs(audio - known);
            duration = delta <= DurationTolerance ? 1
                : delta <= DurationTolerance * 2 ? 0.6
                : delta <= DurationContradiction ? 0.2
                : 0;
            if (delta > DurationContradiction)
            {
                conflicts.Add($"duration differs by {delta.ToString("0", CultureInfo.InvariantCulture)}s");
            }
        }

        var title = CompareText(file.TagTitle, candidate.Title, "title", conflicts);
        if (VersionsDiffer(file.TagTitle, candidate.Title))
        {
            conflicts.Add("version markers differ (for example live or remix)");
        }

        var artist = CompareText(file.TagArtist, candidate.Artists.Count > 0 ? string.Join(" & ", candidate.Artists) : null, "artist", conflicts);

        double embedded = 0.5;
        if (file.TagRecordingId != null && candidate.MusicBrainzRecordingId != null)
        {
            if (string.Equals(file.TagRecordingId, candidate.MusicBrainzRecordingId, StringComparison.OrdinalIgnoreCase))
            {
                embedded = 1;
            }
            else
            {
                embedded = 0;
                conflicts.Add("embedded MusicBrainz recording ID points elsewhere");
            }
        }

        var total = Math.Clamp(
            provider * ProviderWeight +
            duration * DurationWeight +
            title * TitleWeight +
            artist * ArtistWeight +
            embedded * EmbeddedIdWeight, 0, 1);

        var breakdown = JsonSerializer.Serialize(new
        {
            version = Version,
            provider = new { value = Round(provider), weight = ProviderWeight },
            duration = new { value = Round(duration), weight = DurationWeight },
            title = new { value = Round(title), weight = TitleWeight },
            artist = new { value = Round(artist), weight = ArtistWeight },
            embeddedId = new { value = Round(embedded), weight = EmbeddedIdWeight },
            total = Round(total)
        });

        return new ScoredRecording(
            candidate,
            Round(total),
            ProposalFields.ConfidenceBandFor(total, high, review),
            breakdown,
            conflicts.Count == 0 ? null : string.Join("; ", conflicts));
    }

    /// <summary>
    /// 1 for an exact normalized match, otherwise neutral 0.5. A mismatch is
    /// recorded as a conflict but not penalized: the tags are the evidence
    /// under suspicion, so agreement may raise confidence while disagreement
    /// must not drown out a strong acoustic match.
    /// </summary>
    private static double CompareText(string? tag, string? candidate, string field, List<string> conflicts)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(candidate))
        {
            return 0.5;
        }

        if (TextNormalization.MatchesIgnoringCaseAndPunctuation(tag, candidate))
        {
            return 1;
        }

        conflicts.Add($"tag {field} differs");
        return 0.5;
    }

    private static bool VersionsDiffer(string? tagTitle, string? candidateTitle)
    {
        if (string.IsNullOrWhiteSpace(tagTitle) || string.IsNullOrWhiteSpace(candidateTitle))
        {
            return false;
        }

        var tagMarkers = TextNormalization.ExtractVersionTokens(tagTitle);
        var candidateMarkers = TextNormalization.ExtractVersionTokens(candidateTitle);
        return tagMarkers.Order().SequenceEqual(candidateMarkers.Order()) == false;
    }

    /// <summary>
    /// Two candidates name the same song when their normalized title and artists
    /// agree. Without a title, only another entry from the same AcoustID result
    /// counts as the same song, since it was matched to identical audio.
    /// </summary>
    private static bool IsSameSong(RecordingEvidence a, RecordingEvidence b)
    {
        if (string.IsNullOrWhiteSpace(a.Title) || string.IsNullOrWhiteSpace(b.Title))
        {
            return a.AcoustId != null && string.Equals(a.AcoustId, b.AcoustId, StringComparison.OrdinalIgnoreCase);
        }

        return TextNormalization.MatchesIgnoringCaseAndPunctuation(a.Title, b.Title) &&
               TextNormalization.MatchesIgnoringCaseAndPunctuation(string.Join(" & ", a.Artists), string.Join(" & ", b.Artists));
    }

    private static double Round(double value) => Math.Round(value, 4);
}
