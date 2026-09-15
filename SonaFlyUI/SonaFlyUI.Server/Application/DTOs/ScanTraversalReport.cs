using SonaFlyUI.Server.Application.Common;

namespace SonaFlyUI.Server.Application.DTOs;

/// <summary>
/// Records how much of a library root a scan actually managed to walk.
/// <para>
/// Absence of a file is only evidence that the file is gone when the directory that would
/// contain it was successfully enumerated. An offline NAS or a permissions outage otherwise
/// looks exactly like "the user deleted their whole collection", and the index would mark
/// every track missing and garbage-collect its albums and artists (backlog N13).
/// </para>
/// </summary>
public sealed class ScanTraversalReport
{
    private const int MaxRecordedFailures = 50;

    private readonly HashSet<string> _traversedDirectories = new(FileSystemPaths.Comparer);
    private readonly HashSet<string> _unverifiedDirectories = new(FileSystemPaths.Comparer);
    private readonly HashSet<string> _unverifiedFiles = new(FileSystemPaths.Comparer);
    private readonly List<string> _failures = [];

    /// <summary>False when the root itself could not be opened at all.</summary>
    public bool RootAvailable { get; private set; }

    public int FailureCount { get; private set; }

    public IReadOnlyList<string> Failures => _failures;

    /// <summary>True only when the whole tree under the root was enumerated without error.</summary>
    public bool IsComplete => RootAvailable && FailureCount == 0;

    public int TraversedDirectoryCount => _traversedDirectories.Count;

    public void MarkRootAvailable(bool available) => RootAvailable = available;

    public void MarkDirectoryTraversed(string directory)
        => _traversedDirectories.Add(FileSystemPaths.NormalizeForComparison(directory));

    public void RecordFailure(string path, string message)
    {
        FailureCount++;
        if (_failures.Count < MaxRecordedFailures)
            _failures.Add($"{path}: {message}");
    }

    public void RecordFileFailure(string filePath, string message)
    {
        _unverifiedFiles.Add(FileSystemPaths.NormalizeForComparison(filePath));
        RecordFailure(filePath, message);
    }

    public void MarkDirectoryUnverified(string directory)
        => _unverifiedDirectories.Add(FileSystemPaths.NormalizeForComparison(directory));

    public void RecordDirectoryFailure(string directory, string message)
    {
        MarkDirectoryUnverified(directory);
        RecordFailure(directory, message);
    }

    /// <summary>
    /// True when this scan positively enumerated the directory holding
    /// <paramref name="filePath"/>, and can therefore be trusted about the file's absence.
    /// </summary>
    public bool CanVouchForAbsenceOf(string filePath)
    {
        if (_unverifiedFiles.Contains(FileSystemPaths.NormalizeForComparison(filePath)))
            return false;

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory)) return false;

        var normalizedDirectory = FileSystemPaths.NormalizeForComparison(directory);
        if (_unverifiedDirectories.Any(failed =>
                FileSystemPaths.IsSameOrUnder(normalizedDirectory, failed)))
            return false;

        // A successfully enumerated ancestor can vouch that a no-longer-present descendant
        // directory was deleted. Without this, removing an entire album directory leaves all
        // of its tracks "unverified" forever because that immediate directory cannot be walked.
        return _traversedDirectories.Any(traversed =>
            FileSystemPaths.IsSameOrUnder(normalizedDirectory, traversed));
    }
}
