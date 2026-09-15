namespace SonaFlyUI.Server.Application.Common;

/// <summary>
/// One place for filesystem path semantics so validation, duplicate detection, persistence
/// and scan reconciliation all agree (backlog N18). Case sensitivity follows the host
/// platform: two paths differing only in case are the same file on Windows and different
/// files on Linux, and using a single hard-coded rule silently merges or duplicates tracks.
/// </summary>
public static class FileSystemPaths
{
    public static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Canonicalizes an absolute path. Unlike trimming separators by hand this keeps
    /// filesystem-root semantics intact: "/" stays "/" and "C:\" stays "C:\" rather than
    /// collapsing to an empty or drive-relative path.
    /// </summary>
    /// <returns>True when <paramref name="input"/> is a usable absolute path.</returns>
    public static bool TryNormalize(string? input, out string normalized, out string? error)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Path is required.";
            return false;
        }

        var trimmed = input.Trim();

        if (!Path.IsPathFullyQualified(trimmed))
        {
            error = "Path must be absolute (a drive-qualified or rooted path).";
            return false;
        }

        try
        {
            // GetFullPath resolves ".", ".." and redundant separators, but it keeps a trailing
            // one ("C:\music\" stays "C:\music\"), which would make two spellings of the same
            // directory compare unequal. Trim it — except when the whole path IS the root, where
            // the separator is part of the path rather than decoration.
            normalized = Path.GetFullPath(trimmed);

            var pathRoot = Path.GetPathRoot(normalized);
            if (!string.Equals(normalized, pathRoot, Comparison))
            {
                normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Path is not valid: {ex.Message}";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Normalizes for comparison only, falling back to the trimmed input when the value
    /// cannot be canonicalized (so already-persisted odd values still compare stably).
    /// </summary>
    public static string NormalizeForComparison(string? input)
    {
        if (TryNormalize(input, out var normalized, out _)) return normalized;

        // Not canonicalizable (a rooted-but-not-drive-qualified path, say). Still unify the
        // separator so two spellings of the same path compare equal.
        return (input ?? string.Empty).Trim()
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    public static bool AreSame(string? a, string? b)
        => string.Equals(NormalizeForComparison(a), NormalizeForComparison(b), Comparison);

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="ancestor"/> itself or lives
    /// beneath it. Used to keep destructive cache cleanup from escaping into music, database
    /// or key storage (backlog N16).
    /// </summary>
    public static bool IsSameOrUnder(string? candidate, string? ancestor)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(ancestor)) return false;

        var normalizedCandidate = NormalizeForComparison(candidate);
        var normalizedAncestor = NormalizeForComparison(ancestor);
        if (normalizedCandidate.Length == 0 || normalizedAncestor.Length == 0) return false;

        if (string.Equals(normalizedCandidate, normalizedAncestor, Comparison)) return true;

        var prefix = normalizedAncestor.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedAncestor
            : normalizedAncestor + Path.DirectorySeparatorChar;

        return normalizedCandidate.StartsWith(prefix, Comparison);
    }
}
