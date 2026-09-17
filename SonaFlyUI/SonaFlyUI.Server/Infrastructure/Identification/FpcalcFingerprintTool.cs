using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Runs the Chromaprint fpcalc utility on one file (upgrade plan 6.1).
/// The file path is passed as a single argument, never through a shell, so
/// names containing spaces, quotes, or shell syntax are safe. Output is
/// bounded, each run has a timeout, and cancellation kills the process.
/// </summary>
public sealed class FpcalcFingerprintTool : IFingerprintTool
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);
    private const int MaxStdoutChars = 256 * 1024;
    private const int MaxStderrChars = 4 * 1024;

    private static string? _cachedVersion;
    private static string? _cachedVersionPath;

    private readonly IdentificationOptions _options;
    private readonly ILogger<FpcalcFingerprintTool> _logger;

    public FpcalcFingerprintTool(IOptions<IdentificationOptions> options, ILogger<FpcalcFingerprintTool> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FingerprintResult> ComputeAsync(string filePath, CancellationToken ct)
    {
        var executable = _options.FpcalcPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new IdentificationStepException(IdentificationErrorCategory.Configuration, retryable: false,
                "No fpcalc executable is configured (SonaFly:Identification:FpcalcPath).");
        }

        var run = await RunAsync(executable, ["-json", filePath], ct);

        // fpcalc can exit non-zero after a recoverable decode warning while still
        // printing a usable fingerprint, so trust valid output over the exit code.
        if (TryParse(run.Stdout, out var fingerprint, out var duration))
        {
            return new FingerprintResult(fingerprint, duration, await GetVersionAsync(executable, ct));
        }

        var detail = FirstLine(run.Stderr);
        throw new IdentificationStepException(IdentificationErrorCategory.Fingerprint, retryable: false,
            string.IsNullOrEmpty(detail)
                ? $"fpcalc could not fingerprint this file (exit code {run.ExitCode})."
                : $"fpcalc could not fingerprint this file (exit code {run.ExitCode}): {detail}");
    }

    /// <summary>Parses fpcalc's JSON output. Exposed for tests.</summary>
    public static bool TryParse(string? json, out string fingerprint, out double durationSeconds)
    {
        fingerprint = string.Empty;
        durationSeconds = 0;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty("fingerprint", out var fp) == false ||
                fp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(fp.GetString()) ||
                root.TryGetProperty("duration", out var d) == false ||
                d.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            fingerprint = fp.GetString()!;
            durationSeconds = d.GetDouble();
            return durationSeconds > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string?> GetVersionAsync(string executable, CancellationToken ct)
    {
        if (_cachedVersion != null && _cachedVersionPath == executable)
        {
            return _cachedVersion;
        }

        try
        {
            var run = await RunAsync(executable, ["-version"], ct);
            var version = FirstLine(run.Stdout);
            if (string.IsNullOrEmpty(version) == false)
            {
                _cachedVersion = version.Length > 128 ? version[..128] : version;
                _cachedVersionPath = executable;
            }

            return _cachedVersion;
        }
        catch (IdentificationStepException)
        {
            return null;
        }
    }

    private async Task<ProcessRun> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.Configuration, retryable: false,
                "The configured fpcalc executable could not be started. Check SonaFly:Identification:FpcalcPath.",
                inner: ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RunTimeout);

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, MaxStdoutChars, timeout.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, MaxStderrChars, timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessRun(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new IdentificationStepException(IdentificationErrorCategory.Fingerprint, retryable: true,
                $"fpcalc did not finish within {RunTimeout.TotalSeconds:0} seconds.");
        }
    }

    /// <summary>
    /// Reads a stream to its end but keeps only the first <paramref name="limit"/>
    /// characters, so a misbehaving tool cannot exhaust memory and never blocks
    /// on a full pipe.
    /// </summary>
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken ct)
    {
        var kept = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, ct)) > 0)
        {
            var room = limit - kept.Length;
            if (room > 0)
            {
                kept.Append(buffer, 0, Math.Min(room, read));
            }
        }

        return kept.ToString();
    }

    private void TryKill(Process process)
    {
        try
        {
            if (process.HasExited == false)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not stop an fpcalc process.");
        }
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var line = text.Trim().Split('\n', 2)[0].Trim();
        return line.Length > 256 ? line[..256] : line;
    }

    private sealed record ProcessRun(int ExitCode, string Stdout, string Stderr);
}
