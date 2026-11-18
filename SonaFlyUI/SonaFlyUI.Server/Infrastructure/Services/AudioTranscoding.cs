using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>Settings under <c>SonaFly:Transcoding</c>.</summary>
public sealed class TranscodingOptions
{
    public const string SectionName = "SonaFly:Transcoding";

    public bool Enabled { get; set; } = true;

    /// <summary>The ffmpeg executable. Empty means "ffmpeg" on the PATH.</summary>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>Where compressed copies are kept. A cache: anything in it can be rebuilt.</summary>
    public string CacheRoot { get; set; } = "./data/transcodes";

    /// <summary>MP3 bitrate of a compressed stream.</summary>
    public int BitrateKbps { get; set; } = 192;

    /// <summary>The cache is trimmed, least recently played first, once it passes this.</summary>
    public int MaxCacheMegabytes { get; set; } = 2048;

    /// <summary>Longest one file may take to encode before it is given up on.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>Turns one audio file into an MP3. Separate from the cache so the cache can be tested without ffmpeg.</summary>
public interface IAudioEncoder
{
    /// <summary>Whether encoding can work at all on this machine.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    /// <summary>Encodes <paramref name="source"/> to <paramref name="target"/>. False when it failed.</summary>
    Task<bool> EncodeAsync(string source, string target, int bitrateKbps, CancellationToken ct);
}

/// <summary>Encodes with the ffmpeg command-line tool.</summary>
public sealed class FfmpegAudioEncoder(IOptions<TranscodingOptions> options, ILogger<FfmpegAudioEncoder> logger)
    : IAudioEncoder
{
    private readonly TranscodingOptions _options = options.Value;
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private bool? _available;

    private string Executable => string.IsNullOrWhiteSpace(_options.FfmpegPath) ? "ffmpeg" : _options.FfmpegPath;

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (_available is { } known) return known;

        await _probeGate.WaitAsync(ct);
        try
        {
            if (_available is { } already) return already;

            var available = await RunAsync(["-version"], TimeSpan.FromSeconds(10), ct) == 0;
            if (available == false)
            {
                logger.LogWarning(
                    "ffmpeg was not found ({Executable}); compressed streams are off and originals are served.",
                    Executable);
            }

            _available = available;
            return available;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    public async Task<bool> EncodeAsync(string source, string target, int bitrateKbps, CancellationToken ct)
    {
        // Passed as separate arguments, never through a shell: a file name is data.
        string[] arguments =
        [
            "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-i", source,
            // The first audio stream only; embedded cover art is a video stream to ffmpeg.
            "-vn", "-map", "0:a:0",
            "-c:a", "libmp3lame", "-b:a", $"{bitrateKbps}k",
            "-f", "mp3", target,
        ];

        var exitCode = await RunAsync(arguments, TimeSpan.FromSeconds(_options.TimeoutSeconds), ct);
        if (exitCode != 0)
        {
            logger.LogWarning("ffmpeg could not encode {Source} (exit code {ExitCode}).", source, exitCode);
        }

        return exitCode == 0;
    }

    /// <summary>Runs ffmpeg. Returns its exit code, or -1 when it could not be started or timed out.</summary>
    private async Task<int> RunAsync(string[] arguments, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            if (process.Start() == false) return -1;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return -1;
        }

        // Drained so a chatty ffmpeg cannot fill the pipe and stall.
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var errors = process.StandardError.ReadToEndAsync(ct);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            await Task.WhenAll(output, errors);
            if (process.ExitCode != 0 && errors.Result.Length > 0)
            {
                logger.LogDebug("ffmpeg: {Errors}", errors.Result.Trim());
            }

            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already gone */ }
            ct.ThrowIfCancellationRequested();
            return -1; // timed out
        }
    }
}

/// <summary>
/// Compressed copies of tracks, for listening over a slow or metered connection.
/// <para>
/// A FLAC album is several hundred megabytes; the same music as MP3 is a tenth of that.
/// The copy is made in full the first time it is asked for and kept, rather than encoded
/// on the fly, because a finished file can be seeked in and reports its length, and a
/// stream that is still being produced can do neither.
/// </para>
/// </summary>
public sealed class TranscodeCache(
    IAudioEncoder encoder,
    IOptions<TranscodingOptions> options,
    ILogger<TranscodeCache> logger)
{
    public const string MimeType = "audio/mpeg";

    /// <summary>
    /// Formats phones and browsers play as they are and that are already small. Anything
    /// else, lossless or exotic, is what a compressed stream is for.
    /// </summary>
    private static readonly HashSet<string> AlreadyCompact =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".aac", ".ogg", ".opus" };

    private readonly TranscodingOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly SemaphoreSlim _trimGate = new(1, 1);

    public static bool WouldBenefit(string filePath) =>
        AlreadyCompact.Contains(Path.GetExtension(filePath)) == false;

    /// <summary>
    /// The compressed copy of a track, made now if it does not exist yet. Null means "serve
    /// the original": transcoding is off or unavailable, the file is already compact, or
    /// encoding failed.
    /// </summary>
    public async Task<string?> GetOrCreateAsync(string sourcePath, CancellationToken ct)
    {
        if (_options.Enabled == false || WouldBenefit(sourcePath) == false) return null;
        if (await encoder.IsAvailableAsync(ct) == false) return null;

        var source = new FileInfo(sourcePath);
        if (source.Exists == false) return null;

        var root = Path.GetFullPath(_options.CacheRoot);
        var target = Path.Combine(root, CacheKey(source) + ".mp3");
        if (File.Exists(target)) return Touch(target);

        // One encode per file: a player often asks twice, once to probe and once to play.
        var gate = _locks.GetOrAdd(target, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(target)) return Touch(target);

            Directory.CreateDirectory(root);
            var partial = $"{target}.{Guid.NewGuid():N}.partial";
            try
            {
                if (await encoder.EncodeAsync(sourcePath, partial, _options.BitrateKbps, ct) == false ||
                    File.Exists(partial) == false || new FileInfo(partial).Length == 0)
                {
                    return null;
                }

                File.Move(partial, target, overwrite: true);
            }
            finally
            {
                try { File.Delete(partial); } catch (IOException) { /* left for the next trim */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not cache a compressed copy of {Source}.", sourcePath);
            return null;
        }
        finally
        {
            gate.Release();
            _locks.TryRemove(target, out _);
        }

        _ = TrimAsync(root);
        return target;
    }

    /// <summary>
    /// Named for the file as it is now: a retagged or replaced file has a different size or
    /// date, so it gets a new copy and the stale one ages out of the cache.
    /// </summary>
    private string CacheKey(FileInfo source)
    {
        var identity = $"{source.FullName}|{source.Length}|{source.LastWriteTimeUtc.Ticks}|{_options.BitrateKbps}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    /// <summary>Marks a copy as just used, which is what the trim orders by.</summary>
    private static string Touch(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
        catch (IOException) { /* being read by another request; it is in use either way */ }
        return path;
    }

    /// <summary>Deletes the least recently played copies until the cache fits its limit.</summary>
    public async Task TrimAsync(string root)
    {
        if (await _trimGate.WaitAsync(0) == false) return;
        try
        {
            var limit = Math.Max(0, (long)_options.MaxCacheMegabytes) * 1024 * 1024;
            var files = new DirectoryInfo(root).EnumerateFiles()
                .OrderByDescending(f => f.LastAccessTimeUtc)
                .ToList();

            long kept = 0;
            foreach (var file in files)
            {
                // A leftover from an encode that was interrupted, once it is clearly dead.
                var abandoned = file.Extension == ".partial" &&
                                DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromHours(1);
                if (file.Extension == ".partial" && abandoned == false) continue;

                kept += file.Length;
                if (kept <= limit && abandoned == false) continue;

                try { file.Delete(); }
                catch (IOException) { /* being streamed right now; next time */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not trim the transcode cache.");
        }
        finally
        {
            _trimGate.Release();
        }
    }
}
