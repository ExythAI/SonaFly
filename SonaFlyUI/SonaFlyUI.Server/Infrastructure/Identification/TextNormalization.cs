using System.Text;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Deterministic text comparison for recording scoring (upgrade plan 8.2).
/// Comparison normalizes case, whitespace, punctuation, leading articles, and
/// common featuring forms while preserving the original text elsewhere.
/// Version markers (Live, Remix, Radio Edit, and friends) are treated as
/// evidence and extracted explicitly instead of being stripped silently.
/// </summary>
public static class TextNormalization
{
    private static readonly string[] LeadingArticles = ["the ", "a ", "an "];

    private static readonly string[] FeaturingForms = [" featuring ", " feat ", " feat. ", " ft ", " ft. "];

    /// <summary>
    /// Version labels that distinguish recordings and must never be stripped
    /// before comparing candidates (plan 8.2).
    /// </summary>
    public static readonly IReadOnlyList<string> VersionMarkers =
    [
        "live",
        "remix",
        "radio edit",
        "acoustic",
        "instrumental",
        "demo",
        "remaster",
        "remastered",
        "extended",
        "unplugged",
        "reprise"
    ];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var folded = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();

        var builder = new StringBuilder(folded.Length);
        var pendingSpace = false;
        foreach (var c in folded)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (char.IsPunctuation(c) || char.IsSymbol(c))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(" ");
            }

            pendingSpace = false;
            builder.Append(c);
        }

        var collapsed = builder.ToString();
        var withoutFeaturing = collapsed;
        foreach (var form in FeaturingForms)
        {
            withoutFeaturing = withoutFeaturing.Replace(form, " feat ", StringComparison.Ordinal);
        }

        return WithoutLeadingArticle(withoutFeaturing);
    }

    public static string WithoutLeadingArticle(string normalized)
    {
        foreach (var article in LeadingArticles)
        {
            if (normalized.StartsWith(article, StringComparison.Ordinal))
            {
                return normalized.Substring(article.Length);
            }
        }

        return normalized;
    }

    /// <summary>
    /// Returns the version markers present in a title, for scoring evidence.
    /// </summary>
    public static IReadOnlyList<string> ExtractVersionTokens(string? title)
    {
        var normalized = Normalize(title);
        if (normalized.Length == 0)
        {
            return [];
        }

        var padded = " " + normalized + " ";
        var found = new List<string>();
        foreach (var marker in VersionMarkers)
        {
            if (padded.Contains(" " + marker + " ", StringComparison.Ordinal) ||
                padded.Contains(" " + marker + ".", StringComparison.Ordinal) ||
                padded.Contains("(" + marker, StringComparison.Ordinal) ||
                padded.Contains("[" + marker, StringComparison.Ordinal))
            {
                found.Add(marker);
            }
        }

        return found;
    }

    public static bool MatchesIgnoringCaseAndPunctuation(string? left, string? right)
    {
        return Normalize(left).Equals(Normalize(right), StringComparison.Ordinal);
    }
}

