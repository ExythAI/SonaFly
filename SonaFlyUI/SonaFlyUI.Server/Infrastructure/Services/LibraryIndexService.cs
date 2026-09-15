using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.Common;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

public class LibraryIndexService : ILibraryIndexService
{
    private readonly SonaFlyDbContext _db;
    private readonly IFileScanner _fileScanner;
    private readonly IMetadataReader _metadataReader;
    private readonly IArtworkService _artworkService;
    private readonly ILogger<LibraryIndexService> _logger;

    // Scan-session caches to avoid duplicate inserts between batch saves
    private Dictionary<string, Artist> _artistCache = null!;
    private Dictionary<string, Album> _albumCache = null!;
    private Dictionary<string, Genre> _genreCache = null!;

    // Albums whose artwork this scan has already resolved (or tried to resolve), so that a
    // 30-track album does not trigger 30 identical folder scans and remote lookups (N22).
    private HashSet<Guid> _albumArtworkResolved = null!;

    public LibraryIndexService(
        SonaFlyDbContext db,
        IFileScanner fileScanner,
        IMetadataReader metadataReader,
        IArtworkService artworkService,
        ILogger<LibraryIndexService> logger)
    {
        _db = db;
        _fileScanner = fileScanner;
        _metadataReader = metadataReader;
        _artworkService = artworkService;
        _logger = logger;
    }

    public async Task<ScanJobDto> ScanLibraryRootAsync(ScanRequest request, CancellationToken ct)
    {
        var libraryRootId = request.LibraryRootId;
        var fullScan = request.FullScan;

        var libraryRoot = await _db.LibraryRoots.FindAsync([libraryRootId], ct)
            ?? throw new KeyNotFoundException($"Library root {libraryRootId} not found.");

        // Adopt the job the API persisted when it accepted the request, so scan history shows
        // one row per requested scan rather than a queued row plus an unrelated running one.
        var scanJob = await _db.ScanJobs.FirstOrDefaultAsync(j => j.Id == request.ScanJobId, ct)
            ?? throw new KeyNotFoundException(
                $"Scan job {request.ScanJobId} no longer exists; it may have been invalidated by library maintenance.");

        if (scanJob.LibraryRootId != libraryRootId)
            throw new InvalidOperationException(
                $"Scan job {request.ScanJobId} does not belong to library root {libraryRootId}.");

        scanJob.Status = ScanStatus.Running;
        scanJob.StartedUtc = DateTime.UtcNow;

        libraryRoot.LastScanStartedUtc = DateTime.UtcNow;
        libraryRoot.LastScanStatus = ScanStatus.Running;
        libraryRoot.LastScanError = null;
        await _db.SaveChangesAsync(ct);

        var errors = new List<string>();
        var traversal = new ScanTraversalReport();

        try
        {
            // Pre-load existing entities into scan-session caches. This sits inside the try so a
            // failure here is recorded on the job and the root, rather than escaping and leaving
            // the root stuck at Running forever (backlog N17).
            _artistCache = (await _db.Artists.ToListAsync(ct))
                .ToDictionary(a => a.Name.ToLowerInvariant(), a => a);
            _albumCache = (await _db.Albums.Include(a => a.AlbumArtist).ToListAsync(ct))
                .ToDictionary(a => AlbumKey(a.Title, a.AlbumArtistId), a => a);
            _genreCache = (await _db.Genres.ToListAsync(ct))
                .ToDictionary(g => g.Name.ToLowerInvariant(), g => g);
            _albumArtworkResolved = [];

            // Get existing tracks for this library root for comparison
            var existingTracks = (await _db.Tracks
                    .Where(t => t.LibraryRootId == libraryRootId)
                    .ToListAsync(ct))
                .GroupBy(t => FileSystemPaths.NormalizeForComparison(t.FilePath), FileSystemPaths.Comparer)
                .ToDictionary(g => g.Key, g => g.First(), FileSystemPaths.Comparer);

            var scannedPaths = new HashSet<string>(FileSystemPaths.Comparer);

            await foreach (var file in _fileScanner.EnumerateAudioFilesAsync(libraryRoot.Path, traversal, ct))
            {
                scanJob.FilesScanned++;
                var normalizedPath = FileSystemPaths.NormalizeForComparison(file.FilePath);
                scannedPaths.Add(normalizedPath);

                try
                {
                    if (existingTracks.TryGetValue(normalizedPath, out var existing))
                    {
                        // Incremental: skip only if the file is unchanged AND the index already
                        // agrees that it is present. A file that comes back byte-identical after
                        // being marked missing would otherwise stay hidden forever (N14).
                        if (!fullScan &&
                            !existing.IsMissing &&
                            existing.FileSizeBytes == file.FileSizeBytes &&
                            existing.ModifiedUtcSource == file.LastModifiedUtc)
                        {
                            continue;
                        }

                        var metadata = await _metadataReader.ReadAsync(file.FilePath, ct);
                        await UpdateTrackAsync(existing, metadata, file, ct);
                        scanJob.FilesUpdated++;
                    }
                    else
                    {
                        // New file
                        var metadata = await _metadataReader.ReadAsync(file.FilePath, ct);
                        await CreateTrackAsync(libraryRootId, metadata, file, ct);
                        scanJob.FilesAdded++;
                    }

                    // Batch save every 100 files
                    if (scanJob.FilesScanned % 100 == 0)
                    {
                        await _db.SaveChangesAsync(ct);
                        _logger.LogInformation("Scan progress: {Scanned} files scanned, {Added} added",
                            scanJob.FilesScanned, scanJob.FilesAdded);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    scanJob.ErrorsCount++;
                    errors.Add($"{file.FileName}: {ex.Message}");
                    _logger.LogWarning(ex, "Error processing {FilePath}", file.FilePath);
                }
            }

            // Reconcile absence, but only where this scan actually looked. An offline root or a
            // denied subtree is not evidence that its files were deleted (N13).
            foreach (var (path, track) in existingTracks)
            {
                if (scannedPaths.Contains(path) || track.IsMissing)
                    continue;

                if (!traversal.CanVouchForAbsenceOf(track.FilePath))
                {
                    scanJob.FilesUnverified++;
                    continue;
                }

                track.IsMissing = true;
                track.ModifiedUtc = DateTime.UtcNow;
                scanJob.FilesMissing++;
            }

            if (!traversal.RootAvailable)
            {
                // The root itself was unreachable. That is a failed scan, not an empty library.
                scanJob.Status = ScanStatus.Failed;
                errors.Insert(0,
                    $"Library root '{libraryRoot.Path}' could not be reached. No tracks were marked " +
                    $"missing and no metadata was removed; {scanJob.FilesUnverified} indexed track(s) " +
                    "were left as they were.");
                errors.AddRange(traversal.Failures.Select(f => $"Unreadable: {f}"));
            }
            else if (traversal.FailureCount > 0)
            {
                scanJob.Status = ScanStatus.Partial;
                errors.Insert(0,
                    $"Partial scan: {traversal.FailureCount} path(s) could not be read; " +
                    $"{scanJob.FilesUnverified} existing track(s) were left untouched because their " +
                    "location could not be verified.");
                errors.AddRange(traversal.Failures.Select(f => $"Unreadable: {f}"));
            }
            else
            {
                scanJob.Status = ScanStatus.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            scanJob.Status = ScanStatus.Cancelled;
        }
        catch (Exception ex)
        {
            scanJob.Status = ScanStatus.Failed;
            errors.Add($"Fatal: {ex.Message}");
            _logger.LogError(ex, "Scan failed for library root {LibraryRootId}", libraryRootId);
        }

        scanJob.CompletedUtc = DateTime.UtcNow;
        if (errors.Count > 0)
            scanJob.ErrorSummary = string.Join("\n", errors.Take(50)); // Cap error summary

        libraryRoot.LastScanCompletedUtc = DateTime.UtcNow;
        libraryRoot.LastScanStatus = scanJob.Status;
        libraryRoot.LastScanError = scanJob.ErrorSummary;

        // The scan may have been cancelled; its final state still has to be persisted, so this
        // save must not use the (possibly already-cancelled) scan token.
        await _db.SaveChangesAsync(CancellationToken.None);

        // Only a scan that saw the whole tree may garbage-collect metadata.
        if (scanJob.Status == ScanStatus.Completed)
        {
            await CleanupOrphansAsync(ct);
        }
        else if (scanJob.Status == ScanStatus.Partial)
        {
            _logger.LogWarning(
                "Skipping orphan cleanup for library root {LibraryRootId}: {Failures} path(s) were unreadable.",
                libraryRootId, traversal.FailureCount);
        }

        return MapScanJob(scanJob, libraryRoot.Name);
    }

    /// <summary>
    /// Removes metadata entities that no longer have any track at all.
    /// <para>
    /// Deliberately keyed on the existence of a track row rather than a <em>present</em> track
    /// row: a track that is merely missing (an unplugged drive, a file temporarily moved) must
    /// keep its album and artist identity so that playlist membership, mixed tapes and
    /// ID-based content restrictions still point at the same entities when it returns (N13).
    /// </para>
    /// </summary>
    private async Task CleanupOrphansAsync(CancellationToken ct)
    {
        var orphanAlbums = await _db.Albums
            .Where(a => !_db.Tracks.Any(t => t.AlbumId == a.Id))
            .ToListAsync(ct);
        if (orphanAlbums.Count > 0)
        {
            _logger.LogInformation("Removing {Count} orphaned albums", orphanAlbums.Count);
            _db.Albums.RemoveRange(orphanAlbums);
            await _db.SaveChangesAsync(ct);
        }

        var orphanArtists = await _db.Artists
            .Where(a =>
                !_db.Tracks.Any(t => t.PrimaryArtistId == a.Id) &&
                !_db.TrackArtists.Any(ta => ta.ArtistId == a.Id) &&
                !_db.Albums.Any(alb => alb.AlbumArtistId == a.Id))
            .ToListAsync(ct);
        if (orphanArtists.Count > 0)
        {
            _logger.LogInformation("Removing {Count} orphaned artists", orphanArtists.Count);
            _db.Artists.RemoveRange(orphanArtists);
            await _db.SaveChangesAsync(ct);
        }

        var orphanGenres = await _db.Genres
            .Where(g => !_db.TrackGenres.Any(tg => tg.GenreId == g.Id))
            .ToListAsync(ct);
        if (orphanGenres.Count > 0)
        {
            _logger.LogInformation("Removing {Count} orphaned genres", orphanGenres.Count);
            _db.Genres.RemoveRange(orphanGenres);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task CreateTrackAsync(Guid libraryRootId, AudioMetadata metadata, DiscoveredAudioFile file, CancellationToken ct)
    {
        var artist = GetOrCreateArtist(metadata.Artist ?? metadata.AlbumArtist);
        var albumArtist = metadata.AlbumArtist != null && metadata.AlbumArtist != metadata.Artist
            ? GetOrCreateArtist(metadata.AlbumArtist)
            : artist;
        var album = GetOrCreateAlbum(metadata.Album, albumArtist?.Id, metadata.Year);
        var genre = GetOrCreateGenre(metadata.Genre);

        var track = new Track
        {
            LibraryRootId = libraryRootId,
            FilePath = file.FilePath,
            FileName = file.FileName,
            FileExtension = file.Extension,
            FileSizeBytes = file.FileSizeBytes,
            DurationSeconds = metadata.DurationSeconds,
            BitRateKbps = metadata.BitRateKbps,
            SampleRateHz = metadata.SampleRateHz,
            TrackNumber = metadata.TrackNumber,
            DiscNumber = metadata.DiscNumber,
            Title = metadata.Title ?? file.FileName,
            AlbumId = album?.Id,
            PrimaryArtistId = artist?.Id,
            Genre = metadata.Genre,
            MimeType = metadata.MimeType,
            ModifiedUtcSource = file.LastModifiedUtc,
            IsIndexed = true,
            IsMissing = false
        };

        _db.Tracks.Add(track);

        // Add genre junction
        if (genre != null)
        {
            _db.TrackGenres.Add(new TrackGenre { TrackId = track.Id, GenreId = genre.Id });
        }

        // Add track artist junction
        if (artist != null)
        {
            _db.TrackArtists.Add(new TrackArtist { TrackId = track.Id, ArtistId = artist.Id, Role = TrackArtistRole.Primary });
        }

        await ResolveArtworkAsync(album, metadata, file.FilePath, forceRefresh: false, ct);
    }

    private async Task UpdateTrackAsync(Track track, AudioMetadata metadata, DiscoveredAudioFile file, CancellationToken ct)
    {
        // Presence is known independently of whether its tags can be parsed.
        track.IsMissing = false;
        track.IsIndexed = true;

        if (metadata.ReadFailed)
        {
            // The placeholder metadata says nothing about this file's tags. Overwriting the
            // last-known-good title/artist/genre — and dropping the junctions restrictions are
            // enforced through — would be a regression, not an update (N15). Keep the previous
            // size/timestamp fingerprint too, so an incremental scan retries this file instead
            // of treating the failed attempt as a successful update.
            _logger.LogWarning(
                "Keeping last-known-good metadata for {FilePath}: tags could not be parsed.", file.FilePath);
            return;
        }

        track.FileSizeBytes = file.FileSizeBytes;
        track.ModifiedUtcSource = file.LastModifiedUtc;
        track.ModifiedUtc = DateTime.UtcNow;

        var artist = GetOrCreateArtist(metadata.Artist ?? metadata.AlbumArtist);
        var albumArtist = metadata.AlbumArtist != null && metadata.AlbumArtist != metadata.Artist
            ? GetOrCreateArtist(metadata.AlbumArtist)
            : artist;
        var album = GetOrCreateAlbum(metadata.Album, albumArtist?.Id, metadata.Year);
        var genre = GetOrCreateGenre(metadata.Genre);

        track.DurationSeconds = metadata.DurationSeconds;
        track.BitRateKbps = metadata.BitRateKbps;
        track.SampleRateHz = metadata.SampleRateHz;
        track.TrackNumber = metadata.TrackNumber;
        track.DiscNumber = metadata.DiscNumber;
        track.Title = metadata.Title ?? file.FileName;
        track.AlbumId = album?.Id;
        track.PrimaryArtistId = artist?.Id;
        track.Genre = metadata.Genre;
        track.MimeType = metadata.MimeType;

        // Scalar metadata and junctions have to move together: content restrictions are
        // evaluated through TrackGenres/TrackArtists, so a track retagged into a blocked genre
        // stays streamable if only Track.Genre is updated (N15).
        await SyncGenreJunctionAsync(track, genre, ct);
        await SyncPrimaryArtistJunctionAsync(track, artist, ct);

        // A changed file may carry changed embedded artwork.
        await ResolveArtworkAsync(album, metadata, file.FilePath, forceRefresh: true, ct);
    }

    private async Task SyncGenreJunctionAsync(Track track, Genre? genre, CancellationToken ct)
    {
        var current = await LoadJunctionsAsync(_db.TrackGenres, track.Id, ct);

        var keep = current.FirstOrDefault(l => genre != null && l.GenreId == genre.Id);

        foreach (var link in current)
        {
            if (!ReferenceEquals(link, keep))
                _db.TrackGenres.Remove(link);
        }

        if (genre != null && keep == null)
        {
            _db.TrackGenres.Add(new TrackGenre { TrackId = track.Id, GenreId = genre.Id });
        }
    }

    private async Task SyncPrimaryArtistJunctionAsync(Track track, Artist? artist, CancellationToken ct)
    {
        var current = (await LoadJunctionsAsync(_db.TrackArtists, track.Id, ct))
            .Where(ta => ta.Role == TrackArtistRole.Primary)
            .ToList();

        var keep = current.FirstOrDefault(l => artist != null && l.ArtistId == artist.Id);

        foreach (var link in current)
        {
            if (!ReferenceEquals(link, keep))
                _db.TrackArtists.Remove(link);
        }

        if (artist != null && keep == null)
        {
            _db.TrackArtists.Add(new TrackArtist { TrackId = track.Id, ArtistId = artist.Id, Role = TrackArtistRole.Primary });
        }
    }

    /// <summary>
    /// Reads the junction rows for one track: those already persisted plus any this batch has
    /// added but not yet saved, so the two sources cannot fight each other mid-scan.
    /// </summary>
    private async Task<List<TrackGenre>> LoadJunctionsAsync(DbSet<TrackGenre> set, Guid trackId, CancellationToken ct)
    {
        var rows = await set.Where(tg => tg.TrackId == trackId).ToListAsync(ct);

        foreach (var entry in _db.ChangeTracker.Entries<TrackGenre>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TrackId == trackId && !rows.Contains(entry.Entity))
                rows.Add(entry.Entity);
        }

        return rows;
    }

    private async Task<List<TrackArtist>> LoadJunctionsAsync(DbSet<TrackArtist> set, Guid trackId, CancellationToken ct)
    {
        var rows = await set.Where(ta => ta.TrackId == trackId).ToListAsync(ct);

        foreach (var entry in _db.ChangeTracker.Entries<TrackArtist>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TrackId == trackId && !rows.Contains(entry.Entity))
                rows.Add(entry.Entity);
        }

        return rows;
    }

    /// <summary>
    /// Resolves artwork for a track's album at most once per scan. Embedded art is cheap, but
    /// the folder-image and MusicBrainz fallbacks are not, and running them per track means one
    /// album can issue dozens of identical remote lookups (N22).
    /// </summary>
    private async Task ResolveArtworkAsync(Album? album, AudioMetadata metadata, string filePath, bool forceRefresh, CancellationToken ct)
    {
        var hasEmbeddedArt = metadata.ArtworkData is { Length: > 0 };

        if (album != null && !forceRefresh && album.ArtworkId != null)
        {
            // Album is already illustrated; nothing to look up for this track.
            _albumArtworkResolved.Add(album.Id);
            return;
        }

        if (album != null && !_albumArtworkResolved.Add(album.Id) && !hasEmbeddedArt)
        {
            // Already attempted for this album during this scan, and this track brings no new
            // embedded image of its own — do not repeat the expensive fallbacks.
            return;
        }

        var artworkResult = await _artworkService.ExtractAndStoreAsync(metadata, filePath, ct);
        if (artworkResult == null || album == null) return;

        // Embedded art from the track itself wins on a refresh; otherwise only fill a gap.
        if (album.ArtworkId == null || (forceRefresh && hasEmbeddedArt))
        {
            album.ArtworkId = artworkResult.ArtworkId;
        }
    }

    private Artist? GetOrCreateArtist(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = name.Trim().ToLowerInvariant();
        if (_artistCache.TryGetValue(key, out var existing)) return existing;

        var artist = new Artist { Name = name.Trim(), SortName = name.Trim() };
        _db.Artists.Add(artist);
        _artistCache[key] = artist;
        return artist;
    }

    private Album? GetOrCreateAlbum(string? title, Guid? artistId, int? year)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var key = AlbumKey(title.Trim(), artistId);
        if (_albumCache.TryGetValue(key, out var existing)) return existing;

        var album = new Album
        {
            Title = title.Trim(),
            SortTitle = title.Trim(),
            AlbumArtistId = artistId,
            Year = year
        };
        _db.Albums.Add(album);
        _albumCache[key] = album;
        return album;
    }

    private Genre? GetOrCreateGenre(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = name.Trim().ToLowerInvariant();
        if (_genreCache.TryGetValue(key, out var existing)) return existing;

        var genre = new Genre { Name = name.Trim() };
        _db.Genres.Add(genre);
        _genreCache[key] = genre;
        return genre;
    }

    private static string AlbumKey(string title, Guid? artistId) =>
        $"{title.ToLowerInvariant()}|{artistId}";

    internal static ScanJobDto MapScanJob(ScanJob job, string? libraryRootName) => new(
        job.Id, job.LibraryRootId, libraryRootName,
        job.Status.ToString(), job.StartedUtc, job.CompletedUtc,
        job.FilesScanned, job.FilesAdded, job.FilesUpdated,
        job.FilesMissing, job.FilesUnverified, job.ErrorsCount, job.ErrorSummary
    );
}
