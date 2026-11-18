using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.Common;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api")]
public class SystemController : ControllerBase
{
    private readonly SonaFlyDbContext _db;
    private readonly IConfiguration _config;
    private readonly LibraryMaintenanceGate _gate;
    private readonly TrackEndSchedulerService _auditoriums;
    private readonly LibraryLinkPreserver _links;
    private readonly DatabaseBackupService _backups;
    private readonly ILogger<SystemController> _logger;

    public SystemController(
        SonaFlyDbContext db,
        IConfiguration config,
        LibraryMaintenanceGate gate,
        TrackEndSchedulerService auditoriums,
        LibraryLinkPreserver links,
        DatabaseBackupService backups,
        ILogger<SystemController> logger)
    {
        _db = db;
        _config = config;
        _gate = gate;
        _auditoriums = auditoriums;
        _links = links;
        _backups = backups;
        _logger = logger;
    }

    // GET /api/health is served by the health-check middleware in Program.cs, which
    // also verifies database readiness rather than only that the process is running.

    /// <summary>
    /// Library totals as seen by the caller.
    /// <para>
    /// Administrative server-wide totals and user-visible library totals are deliberately
    /// separate. A restricted listener used to be told the global counts, which reveals exactly
    /// how much content is being withheld from them (backlog N21). Admins still get the whole
    /// picture because they can see the whole library anyway.
    /// </para>
    /// </summary>
    [HttpGet("system/status")]
    [Authorize]
    public async Task<ActionResult<SystemStatusDto>> Status(CancellationToken ct)
    {
        var lastScan = await _db.ScanJobs.AsNoTracking()
            .OrderByDescending(j => j.CompletedUtc)
            .FirstOrDefaultAsync(ct);

        var currentScan = await _db.ScanJobs.AsNoTracking()
            .Where(j => j.Status == ScanStatus.Running || j.Status == ScanStatus.Queued)
            .FirstOrDefaultAsync(ct);

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = User.IsInRole("Admin");
        var restrictions = _db.RestrictionsFor(userId);

        var tracks = _db.Tracks.AsNoTracking().Where(t => t.IsIndexed && !t.IsMissing);
        var albums = _db.Albums.AsNoTracking();
        var artists = _db.Artists.AsNoTracking();
        var genres = _db.Genres.AsNoTracking();

        if (!isAdmin)
        {
            tracks = tracks.ApplyRestrictions(_db, userId);
            albums = albums.ApplyRestrictions(_db, userId).WhereHasPlayableTracks(_db, userId);
            artists = artists.ApplyRestrictions(_db, userId);
            genres = genres.Where(g => !restrictions.GenreIds.Contains(g.Id));
        }

        return Ok(new SystemStatusDto(
            Version: "1.0.0-mvp",
            TotalTracks: await tracks.CountAsync(ct),
            TotalAlbums: await albums.CountAsync(ct),
            TotalArtists: await artists.CountAsync(ct),
            TotalGenres: await genres.CountAsync(ct),
            TotalPlaylists: await _db.Playlists.CountAsync(
                p => isAdmin || p.OwnerUserId == userId || p.IsPublic || p.IsSystemPlaylist, ct),
            // Deployment shape, not library content: admins only.
            LibraryRootCount: isAdmin ? await _db.LibraryRoots.CountAsync(lr => lr.IsEnabled, ct) : 0,
            CurrentScanStatus: isAdmin ? currentScan?.Status.ToString() : null,
            LastScanCompletedUtc: isAdmin ? lastScan?.CompletedUtc : null
        ));
    }

    /// <summary>
    /// Purges all music library data (tracks, albums, artists, genres, artwork, scan jobs)
    /// while preserving users, roles, library root configs, and what people made:
    /// playlists and mixed tapes keep their names, and their entries, favourites and
    /// content restrictions are parked and put back as a rescan finds the same files.
    /// Play history is not kept.
    /// </summary>
    [HttpPost("system/purge")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PurgeLibraryData(CancellationToken ct)
    {
        _logger.LogWarning("Admin initiated library data purge.");

        // Stop the scan worker and wait for it to drain. Without this, a concurrent scan writes
        // rows back in behind the delete, and the purge deletes rows the scan is mid-write on.
        using var maintenance = await _gate.AcquireForMaintenanceAsync(ct);

        // In-memory auditorium queues and "now playing" entries point at tracks that are about
        // to stop existing; reset and broadcast before deleting them.
        var clearedRooms = await _auditoriums.ResetAllRoomsAsync("The library was purged by an administrator.");

        // Capture the exact files owned by artwork rows before the transaction removes those
        // rows. The configured directory may contain operator-managed files or another app's
        // data, neither of which this purge owns.
        var ownedArtworkPaths = await _db.ArtworkAssets.AsNoTracking()
            .Select(a => a.StoragePath)
            .ToListAsync(ct);

        // A snapshot first: this is the one operation that cannot be redone by rescanning.
        // If it cannot be written the purge does not go ahead.
        BackupFile? backup;
        try
        {
            backup = await _backups.CreateAsync(DatabaseBackupService.BeforePurge, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Purge refused: the database backup that precedes it could not be written.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "The library was not purged, because the backup taken first could not be written: " +
                          ex.Message
            });
        }

        ParkedLinkCounts parked;
        await using (var transaction = await _db.Database.BeginTransactionAsync(ct))
        {
            // Before anything is deleted: record what each playlist entry, mixed tape entry,
            // favourite and restriction pointed at, in a form the next scan can match.
            parked = await _links.ParkAsync(ct);

            // Delete in dependency order to avoid FK issues. All of it commits together, so a
            // failure or cancellation part-way through leaves the library as it was rather than
            // half-erased.
            await _db.AuditoriumQueueItems.ExecuteDeleteAsync(ct);
            await _db.MixedTapeItems.ExecuteDeleteAsync(ct);
            await _db.PlaylistItems.ExecuteDeleteAsync(ct);

            // The entries and restrictions go, because the rows they name are about to stop
            // existing and a rescan reissues IDs to unrelated entities. They were parked
            // above. The playlists and mixed tapes themselves stay, empty until the rescan.
            await _db.UserRestrictions.ExecuteDeleteAsync(ct);
            await _db.FavoriteTracks.ExecuteDeleteAsync(ct);
            await _db.PlayEvents.ExecuteDeleteAsync(ct);

            await _db.TrackGenres.ExecuteDeleteAsync(ct);
            await _db.TrackArtists.ExecuteDeleteAsync(ct);

            // Music identification analysis rows belong to the purged library
            // and go with it (review backlog U05). Children first so direct
            // deletes never trip a foreign key. The change journal is
            // deliberately kept: it is self-contained audit with plain IDs,
            // and the provider cache is global rather than library-scoped.
            await _db.RecordingCandidates.ExecuteDeleteAsync(ct);
            await _db.ReleaseCandidates.ExecuteDeleteAsync(ct);
            await _db.AlbumGroupMembers.ExecuteDeleteAsync(ct);
            await _db.MetadataProposals.ExecuteDeleteAsync(ct);
            await _db.IdentificationWorkItems.ExecuteDeleteAsync(ct);
            await _db.AlbumGroups.ExecuteDeleteAsync(ct);
            await _db.IdentificationJobs.ExecuteDeleteAsync(ct);
            await _db.IdentificationErrors.ExecuteDeleteAsync(ct);
            await _db.AcousticFingerprints.ExecuteDeleteAsync(ct);
            await _db.OriginalTagSnapshots.ExecuteDeleteAsync(ct);
            await _db.CatalogOverrides.ExecuteDeleteAsync(ct);
            await _db.TrackFileRevisions.ExecuteDeleteAsync(ct);

            await _db.Tracks.ExecuteDeleteAsync(ct);
            await _db.CatalogReleases.ExecuteDeleteAsync(ct);
            await _db.Albums.ExecuteDeleteAsync(ct);
            await _db.Artists.ExecuteDeleteAsync(ct);
            await _db.Genres.ExecuteDeleteAsync(ct);
            await _db.ArtworkAssets.ExecuteDeleteAsync(ct);
            await _db.ScanJobs.ExecuteDeleteAsync(ct);

            // Reset scan status on library roots (keep the roots themselves)
            await _db.LibraryRoots.ExecuteUpdateAsync(lr => lr
                .SetProperty(x => x.LastScanStartedUtc, (DateTime?)null)
                .SetProperty(x => x.LastScanCompletedUtc, (DateTime?)null)
                .SetProperty(x => x.LastScanStatus, (ScanStatus?)null)
                .SetProperty(x => x.LastScanError, (string?)null), ct);

            await transaction.CommitAsync(ct);
        }

        var cacheCleanup = await ClearArtworkCacheAsync(ownedArtworkPaths, ct);

        _logger.LogWarning("Library data purge complete. {Cache}", cacheCleanup.Message);

        return Ok(new
        {
            message = "Library data purged. Library roots, playlists and mixed tapes were kept; " +
                      $"{parked.Total} playlist, mixed tape, favourite and restriction link(s) will be " +
                      "restored as a rescan finds the same files.",
            backup = backup?.FileName,
            preserved = parked,
            auditoriumsReset = clearedRooms,
            artworkCacheCleared = cacheCleanup.Complete,
            artworkCacheDetail = cacheCleanup.Message
        });
    }

    /// <summary>The database snapshots on disk, newest first.</summary>
    [HttpGet("system/backups")]
    [Authorize(Roles = "Admin")]
    public IActionResult ListBackups() => Ok(new
    {
        directory = _backups.BackupDirectory(),
        backups = _backups.List(),
    });

    /// <summary>Takes a database snapshot now.</summary>
    [HttpPost("system/backups")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateBackup(CancellationToken ct)
    {
        try
        {
            var backup = await _backups.CreateAsync(DatabaseBackupService.Manual, ct);
            return backup == null
                ? Conflict(new { message = "There is no database file to back up." })
                : Ok(backup);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Manual database backup failed.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "The backup could not be written: " + ex.Message });
        }
    }

    /// <summary>
    /// Removes the artwork files this server owns, and only those.
    /// <para>
    /// The previous implementation recursively deleted whatever <c>SonaFly:ArtworkRoot</c>
    /// pointed at. A misconfigured value — the music folder, the data directory, the Data
    /// Protection key ring — therefore destroyed unrelated files. This refuses to run when the
    /// configured cache overlaps anything that matters, deletes only files represented by
    /// ArtworkAsset rows, and leaves the directory itself in place so a mounted volume is not
    /// unmounted from under the process (backlog N16).
    /// </para>
    /// </summary>
    private async Task<(bool Complete, string Message)> ClearArtworkCacheAsync(
        IReadOnlyCollection<string> ownedStoragePaths,
        CancellationToken ct)
    {
        var artworkRoot = _config["SonaFly:ArtworkRoot"] ?? "./data/artwork";
        if (!FileSystemPaths.TryNormalize(Path.GetFullPath(artworkRoot), out var cacheRoot, out var pathError))
            return (false, $"Artwork cache path is not usable ({pathError}); nothing was deleted.");

        if (!Directory.Exists(cacheRoot))
            return (true, "Artwork cache directory does not exist; nothing to clear.");

        var protectedPaths = await CollectProtectedPathsAsync(ct);
        foreach (var (label, path) in protectedPaths)
        {
            if (FileSystemPaths.IsSameOrUnder(cacheRoot, path) || FileSystemPaths.IsSameOrUnder(path, cacheRoot))
            {
                _logger.LogError(
                    "Refusing to clear artwork cache '{CacheRoot}': it overlaps {Label} at '{Path}'.",
                    cacheRoot, label, path);
                return (false,
                    $"Artwork cache '{cacheRoot}' overlaps {label} ('{path}'); no files were deleted. " +
                    "Point SonaFly:ArtworkRoot at a dedicated directory and retry.");
            }
        }

        var failures = 0;
        var removed = 0;

        foreach (var storagePath in ownedStoragePaths.Distinct(FileSystemPaths.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (Path.IsPathRooted(storagePath))
                    throw new InvalidOperationException("Artwork storage path must be relative.");

                var entry = Path.GetFullPath(Path.Combine(cacheRoot, storagePath));
                if (!FileSystemPaths.IsSameOrUnder(entry, cacheRoot) || FileSystemPaths.AreSame(entry, cacheRoot))
                    throw new InvalidOperationException("Artwork storage path escapes the cache root.");

                if (!System.IO.File.Exists(entry))
                    continue;

                System.IO.File.Delete(entry);
                removed++;

                // Thumbnails made from this image are the server's too, and go with it.
                foreach (var thumbnail in ArtworkThumbnails.AllRelativePaths(storagePath))
                {
                    var thumbnailPath = Path.GetFullPath(Path.Combine(cacheRoot, thumbnail));
                    if (FileSystemPaths.IsSameOrUnder(thumbnailPath, cacheRoot) && System.IO.File.Exists(thumbnailPath))
                        System.IO.File.Delete(thumbnailPath);
                }

                var parent = Path.GetDirectoryName(entry);
                if (parent != null && !FileSystemPaths.AreSame(parent, cacheRoot) &&
                    !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    Directory.Delete(parent);
                }
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogWarning(ex, "Could not remove owned artwork cache entry {StoragePath}", storagePath);
            }
        }

        return failures == 0
            ? (true, $"Artwork cache cleared ({removed} entries removed).")
            : (false, $"Artwork cache partially cleared: {removed} removed, {failures} could not be deleted. Retry the purge to finish.");
    }

    /// <summary>Paths the artwork cache must never overlap.</summary>
    private async Task<List<(string Label, string Path)>> CollectProtectedPathsAsync(CancellationToken ct)
    {
        var paths = new List<(string, string)>();

        void Add(string label, string? raw)
        {
            if (!string.IsNullOrWhiteSpace(raw) && FileSystemPaths.TryNormalize(Path.GetFullPath(raw), out var p, out _))
                paths.Add((label, p));
        }

        foreach (var root in await _db.LibraryRoots.AsNoTracking().Select(lr => lr.Path).ToListAsync(ct))
            Add("a music library root", root);

        Add("the Data Protection key ring", Infrastructure.Configuration.DataProtectionSetup.ResolveKeyRingPath(_config));

        var dbPath = SonaFlyDbContext.TryGetDatabaseFilePath(_db);
        if (dbPath != null)
        {
            Add("the database file", dbPath);
            Add("the database directory", Path.GetDirectoryName(dbPath));
        }

        return paths;
    }
}

