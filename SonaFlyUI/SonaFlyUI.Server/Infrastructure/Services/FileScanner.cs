using System.Runtime.CompilerServices;
using SonaFlyUI.Server.Application.Common;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Infrastructure.Services;

public class FileScanner : IFileScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav"
    };

    private static readonly Dictionary<string, string> MimeTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".wav"] = "audio/wav"
    };

    /// <summary>
    /// Walks the tree one directory at a time rather than using a recursive
    /// <c>EnumerateFiles</c> with <c>IgnoreInaccessible</c>, so that a directory we were
    /// not allowed to read is reported as an unverified subtree instead of silently
    /// looking like an empty one (backlog N13).
    /// </summary>
    public async IAsyncEnumerable<DiscoveredAudioFile> EnumerateAudioFilesAsync(
        string rootPath,
        ScanTraversalReport report,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask; // keeps method async-compatible

        if (!Directory.Exists(rootPath))
        {
            report.MarkRootAvailable(false);
            report.RecordFailure(rootPath, "Library root is not reachable (offline, unmounted or removed).");
            yield break;
        }

        report.MarkRootAvailable(true);

        // Visited set guards against symlink/junction cycles, which the recursive
        // enumeration APIs do not protect against either.
        var visited = new HashSet<string>(FileSystemPaths.Comparer);
        var pending = new Stack<string>();
        pending.Push(FileSystemPaths.NormalizeForComparison(rootPath));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var directory = pending.Pop();
            if (!visited.Add(directory))
                continue;

            string[] subdirectories;
            string[] files;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Unverified subtree: we do not know what is in here, so nothing below it
                // may be reconciled as deleted.
                report.RecordDirectoryFailure(directory, ex.Message);
                continue;
            }

            report.MarkDirectoryTraversed(directory);

            foreach (var subdirectory in subdirectories)
            {
                if (IsReparsePoint(subdirectory))
                {
                    // This scanner deliberately does not traverse links/junctions. Treat their
                    // contents as unknown so ancestor reconciliation cannot declare them gone.
                    report.MarkDirectoryUnverified(subdirectory);
                    continue;
                }
                pending.Push(FileSystemPaths.NormalizeForComparison(subdirectory));
            }

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(filePath);
                if (!SupportedExtensions.Contains(ext))
                    continue;

                string fileName;
                long length;
                DateTime lastWriteUtc;
                try
                {
                    var info = new FileInfo(filePath);
                    fileName = info.Name;
                    length = info.Length;
                    lastWriteUtc = info.LastWriteTimeUtc;
                }
                catch (Exception ex)
                {
                    // The file exists but we cannot stat it — that is not evidence of deletion.
                    report.RecordFileFailure(filePath, ex.Message);
                    continue;
                }

                yield return new DiscoveredAudioFile(
                    FilePath: filePath,
                    FileName: fileName,
                    Extension: ext.ToLowerInvariant(),
                    FileSizeBytes: length,
                    LastModifiedUtc: lastWriteUtc
                );
            }
        }
    }

    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    public static string GetMimeType(string extension)
    {
        return MimeTypeMap.GetValueOrDefault(extension.ToLowerInvariant(), "application/octet-stream");
    }
}
