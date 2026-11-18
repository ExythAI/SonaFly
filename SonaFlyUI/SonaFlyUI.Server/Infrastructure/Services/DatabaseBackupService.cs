using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>Settings under <c>SonaFly:Backup</c>.</summary>
public sealed class BackupOptions
{
    public const string SectionName = "SonaFly:Backup";

    /// <summary>Turns scheduled backups off. Backups before a purge or migration still run.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where snapshots are written. Empty means a "backups" folder beside the database
    /// file, which in the container is inside the database volume and so survives the
    /// container being replaced without any extra mount.
    /// </summary>
    public string Directory { get; set; } = string.Empty;

    public int IntervalHours { get; set; } = 24;

    /// <summary>How many scheduled snapshots to keep.</summary>
    public int Retain { get; set; } = 7;

    /// <summary>How many snapshots to keep of each other kind (before a purge, before a migration, manual).</summary>
    public int RetainPerEvent { get; set; } = 5;
}

public sealed record BackupFile(string FileName, long SizeBytes, DateTime CreatedUtc, string Reason);

/// <summary>
/// Takes consistent snapshots of the SQLite database while the server is running.
/// <para>
/// Uses <c>VACUUM INTO</c>, which writes a complete, defragmented copy as of one moment
/// without blocking readers. Copying the live file instead can capture a half-written
/// page or miss what is still in the write-ahead log.
/// </para>
/// </summary>
public sealed class DatabaseBackupService(
    IConfiguration config,
    IOptions<BackupOptions> options,
    ILogger<DatabaseBackupService> logger)
{
    public const string Scheduled = "scheduled";
    public const string BeforePurge = "pre-purge";
    public const string BeforeMigration = "pre-migration";
    public const string Manual = "manual";

    private const string Prefix = "sonafly-";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly BackupOptions _options = options.Value;

    private string? ConnectionString => config.GetConnectionString("DefaultConnection");

    /// <summary>The database file, or null when the database is in memory or not configured.</summary>
    private string? DatabasePath()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return null;
        try
        {
            var source = new SqliteConnectionStringBuilder(ConnectionString).DataSource;
            if (string.IsNullOrWhiteSpace(source) || source.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
                return null;
            return Path.GetFullPath(source);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Where snapshots live, or null when there is no database file to back up.</summary>
    public string? BackupDirectory()
    {
        if (DatabasePath() is not { } database) return null;
        return string.IsNullOrWhiteSpace(_options.Directory)
            ? Path.Combine(Path.GetDirectoryName(database)!, "backups")
            : Path.GetFullPath(_options.Directory);
    }

    /// <summary>
    /// Writes a snapshot now. Returns null when there is nothing to back up (no database
    /// file yet). Throws when the snapshot could not be written, so a caller about to do
    /// something destructive can decide whether to go ahead without one.
    /// </summary>
    public async Task<BackupFile?> CreateAsync(string reason, CancellationToken ct)
    {
        if (DatabasePath() is not { } database || BackupDirectory() is not { } directory) return null;
        if (File.Exists(database) == false) return null;

        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory);
            var now = DateTime.UtcNow;
            var name = $"{Prefix}{now.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{reason}.db";
            var target = Path.Combine(directory, name);
            var partial = target + ".partial";
            File.Delete(partial);

            // A connection of its own: the snapshot must not join a transaction the caller
            // has open, and must not be pooled with a schema the caller is about to change.
            var builder = new SqliteConnectionStringBuilder(ConnectionString) { Pooling = false };
            await using (var connection = new SqliteConnection(builder.ConnectionString))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $target";
                command.Parameters.AddWithValue("$target", partial);
                await command.ExecuteNonQueryAsync(ct);
            }

            // Only a finished snapshot gets the real name, so a crash part-way through
            // never leaves something that looks restorable and is not.
            File.Move(partial, target, overwrite: true);
            Prune(directory);

            var file = new BackupFile(name, new FileInfo(target).Length, now, reason);
            logger.LogInformation("Database backup written: {File} ({Bytes} bytes, {Reason}).", name, file.SizeBytes, reason);
            return file;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The snapshots on disk, newest first.</summary>
    public IReadOnlyList<BackupFile> List()
    {
        if (BackupDirectory() is not { } directory || Directory.Exists(directory) == false) return [];

        return Directory.EnumerateFiles(directory, Prefix + "*.db")
            .Select(Describe)
            .OfType<BackupFile>()
            .OrderByDescending(f => f.CreatedUtc)
            .ToList();
    }

    /// <summary>When the newest scheduled snapshot was taken, if there is one.</summary>
    public DateTime? LastScheduledUtc() =>
        List().Where(f => f.Reason == Scheduled).Select(f => (DateTime?)f.CreatedUtc).FirstOrDefault();

    private static BackupFile? Describe(string path)
    {
        // sonafly-20261118-031500-pre-purge.db
        var name = Path.GetFileName(path);
        var body = Path.GetFileNameWithoutExtension(name)[Prefix.Length..];
        if (body.Length <= TimestampFormat.Length + 1) return null;

        if (DateTime.TryParseExact(body[..TimestampFormat.Length], TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created) == false)
        {
            return null;
        }

        return new BackupFile(name, new FileInfo(path).Length, created, body[(TimestampFormat.Length + 1)..]);
    }

    /// <summary>Keeps the newest few snapshots of each kind and deletes the rest.</summary>
    private void Prune(string directory)
    {
        foreach (var group in List().GroupBy(f => f.Reason))
        {
            var keep = Math.Max(1, group.Key == Scheduled ? _options.Retain : _options.RetainPerEvent);
            foreach (var old in group.OrderByDescending(f => f.CreatedUtc).Skip(keep))
            {
                try
                {
                    File.Delete(Path.Combine(directory, old.FileName));
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Could not remove old backup {File}.", old.FileName);
                }
            }
        }
    }
}

/// <summary>Takes the scheduled snapshot once it is due.</summary>
public sealed class DatabaseBackupWorker(
    DatabaseBackupService backups,
    IOptions<BackupOptions> options,
    ILogger<DatabaseBackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.Enabled == false || settings.IntervalHours <= 0) return;

        var interval = TimeSpan.FromHours(settings.IntervalHours);
        try
        {
            // Let startup finish first; a restart loop must not turn into a backup loop.
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (stoppingToken.IsCancellationRequested == false)
            {
                // Due is judged from the files on disk, so restarting the server neither
                // skips a backup nor takes an extra one.
                var last = backups.LastScheduledUtc();
                if (last == null || DateTime.UtcNow - last.Value >= interval)
                {
                    try
                    {
                        await backups.CreateAsync(DatabaseBackupService.Scheduled, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Scheduled database backup failed.");
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
