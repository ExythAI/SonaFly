using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace SonaFlyUI.Server.Infrastructure.Logging;

/// <summary>
/// Writes the log to a file per day, so it outlives the container that produced it.
/// <para>
/// The console log is all a container keeps, and it goes when the container is replaced,
/// which is exactly when it is wanted: after a deploy that went wrong. Files are named
/// <c>sonafly-yyyyMMdd.log</c> (UTC) and old ones are removed after a set number of days.
/// </para>
/// <para>
/// Writing happens on one background task fed by a queue. A request thread never waits
/// on the disk, and if the disk cannot keep up the oldest queued lines are dropped
/// rather than memory growing without limit.
/// </para>
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const string Prefix = "sonafly-";

    private readonly string _directory;
    private readonly int _retainDays;
    private readonly Func<DateTime> _utcNow;
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task _writer;

    public FileLoggerProvider(string directory, int retainDays = 14, Func<DateTime>? utcNow = null)
    {
        _directory = Path.GetFullPath(directory);
        _retainDays = Math.Max(1, retainDays);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Enqueue(string line) => _queue.Writer.TryWrite(line);

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        var day = string.Empty;
        try
        {
            while (await _queue.Reader.WaitToReadAsync())
            {
                try
                {
                    var today = _utcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    if (writer == null || today != day)
                    {
                        if (writer != null) await writer.DisposeAsync();
                        Directory.CreateDirectory(_directory);
                        writer = new StreamWriter(
                            new FileStream(Path.Combine(_directory, $"{Prefix}{today}.log"),
                                FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        day = today;
                        RemoveOldFiles();
                    }

                    while (_queue.Reader.TryRead(out var line))
                    {
                        await writer.WriteLineAsync(line);
                    }

                    await writer.FlushAsync();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The log directory is unavailable. Logging must never take the server
                    // down; drop what is queued and try again with the next line.
                    if (writer != null) await writer.DisposeAsync();
                    writer = null;
                    while (_queue.Reader.TryRead(out _)) { }
                }
            }
        }
        finally
        {
            if (writer != null) await writer.DisposeAsync();
        }
    }

    private void RemoveOldFiles()
    {
        var cutoff = _utcNow().Date.AddDays(-_retainDays);
        foreach (var file in Directory.EnumerateFiles(_directory, Prefix + "*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date) && date < cutoff)
            {
                try { File.Delete(file); }
                catch (IOException) { /* still open elsewhere; the next day tries again */ }
            }
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        // Give what is queued a moment to reach the disk; shutdown must not hang on it.
        _writer.Wait(TimeSpan.FromSeconds(3));
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) == false) return;

            var line = new StringBuilder()
                .Append(provider._utcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
                .Append(" [").Append(Abbreviate(logLevel)).Append("] ")
                .Append(category).Append(": ")
                .Append(formatter(state, exception));
            if (exception != null) line.AppendLine().Append(exception);

            provider.Enqueue(line.ToString());
        }

        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "---",
        };
    }
}
