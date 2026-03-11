using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<DiscoveredAudioFile> EnumerateAudioFilesAsync(
        string rootPath,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        await Task.CompletedTask; // keeps method async-compatible

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*.*", options))
        {
            ct.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(filePath);
            if (!SupportedExtensions.Contains(ext))
                continue;

            FileInfo info;
            try
            {
                info = new FileInfo(filePath);
            }
            catch
            {
                continue; // Skip inaccessible files
            }

            yield return new DiscoveredAudioFile(
                FilePath: filePath,
                FileName: info.Name,
                Extension: ext.ToLowerInvariant(),
                FileSizeBytes: info.Length,
                LastModifiedUtc: info.LastWriteTimeUtc
            );
        }
    }

    public static string GetMimeType(string extension)
    {
        return MimeTypeMap.GetValueOrDefault(extension.ToLowerInvariant(), "application/octet-stream");
    }
}
