using Microsoft.EntityFrameworkCore;
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

    public async Task<ScanJobDto> ScanLibraryRootAsync(Guid libraryRootId, CancellationToken ct)
    {
        var libraryRoot = await _db.LibraryRoots.FindAsync([libraryRootId], ct)
            ?? throw new KeyNotFoundException($"Library root {libraryRootId} not found.");

        var scanJob = new ScanJob
        {
            LibraryRootId = libraryRootId,
            Status = ScanStatus.Running,
            StartedUtc = DateTime.UtcNow
        };
        _db.ScanJobs.Add(scanJob);

        libraryRoot.LastScanStartedUtc = DateTime.UtcNow;
        libraryRoot.LastScanStatus = ScanStatus.Running;
        libraryRoot.LastScanError = null;
        await _db.SaveChangesAsync(ct);

        // Pre-load existing entities into scan-session caches
        _artistCache = (await _db.Artists.ToListAsync(ct))
            .ToDictionary(a => a.Name.ToLowerInvariant(), a => a);
        _albumCache = (await _db.Albums.Include(a => a.AlbumArtist).ToListAsync(ct))
            .ToDictionary(a => AlbumKey(a.Title, a.AlbumArtistId), a => a);
        _genreCache = (await _db.Genres.ToListAsync(ct))
            .ToDictionary(g => g.Name.ToLowerInvariant(), g => g);

        var errors = new List<string>();

        try
        {
            // Get existing tracks for this library root for comparison
            var existingTracks = await _db.Tracks
                .Where(t => t.LibraryRootId == libraryRootId)
                .ToDictionaryAsync(t => t.FilePath, ct);

            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await foreach (var file in _fileScanner.EnumerateAudioFilesAsync(libraryRoot.Path, ct))
            {
                scanJob.FilesScanned++;
                scannedPaths.Add(file.FilePath);

                try
                {
                    if (existingTracks.TryGetValue(file.FilePath, out var existing))
                    {
                        // Incremental: skip if not changed
                        if (existing.FileSizeBytes == file.FileSizeBytes &&
                            existing.ModifiedUtcSource == file.LastModifiedUtc)
                        {
                            continue;
                        }

                        // File changed — re-read metadata
                        var metadata = await _metadataReader.ReadAsync(file.FilePath, ct);
                        UpdateTrack(existing, metadata, file);
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
                catch (Exception ex)
                {
                    scanJob.ErrorsCount++;
                    errors.Add($"{file.FileName}: {ex.Message}");
                    _logger.LogWarning(ex, "Error processing {FilePath}", file.FilePath);
                }
            }

            // Mark missing files
            foreach (var (path, track) in existingTracks)
            {
                if (!scannedPaths.Contains(path) && !track.IsMissing)
                {
                    track.IsMissing = true;
                    track.ModifiedUtc = DateTime.UtcNow;
                    scanJob.FilesMissing++;
                }
            }

            scanJob.Status = ScanStatus.Completed;
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

        await _db.SaveChangesAsync(ct);

        return MapScanJob(scanJob, libraryRoot.Name);
    }

    private async Task CreateTrackAsync(Guid libraryRootId, AudioMetadata metadata, DiscoveredAudioFile file, CancellationToken ct)
    {
        var artist = GetOrCreateArtist(metadata.Artist ?? metadata.AlbumArtist);
        var albumArtist = metadata.AlbumArtist != null && metadata.AlbumArtist != metadata.Artist
            ? GetOrCreateArtist(metadata.AlbumArtist)
            : artist;
        var album = GetOrCreateAlbum(metadata.Album, albumArtist?.Id, metadata.Year);
        var genre = GetOrCreateGenre(metadata.Genre);

        // Extract artwork
        Guid? artworkId = null;
        var artworkResult = await _artworkService.ExtractAndStoreAsync(metadata, file.FilePath, ct);
        if (artworkResult != null)
        {
            artworkId = artworkResult.ArtworkId;
            // Assign to album if album doesn't have artwork yet
            if (album != null && album.ArtworkId == null)
            {
                album.ArtworkId = artworkId;
            }
        }

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
    }

    private void UpdateTrack(Track track, AudioMetadata metadata, DiscoveredAudioFile file)
    {
        var artist = GetOrCreateArtist(metadata.Artist ?? metadata.AlbumArtist);
        var albumArtist = metadata.AlbumArtist != null && metadata.AlbumArtist != metadata.Artist
            ? GetOrCreateArtist(metadata.AlbumArtist)
            : artist;
        var album = GetOrCreateAlbum(metadata.Album, albumArtist?.Id, metadata.Year);

        track.FileSizeBytes = file.FileSizeBytes;
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
        track.ModifiedUtcSource = file.LastModifiedUtc;
        track.ModifiedUtc = DateTime.UtcNow;
        track.IsMissing = false;
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
        job.FilesMissing, job.ErrorsCount, job.ErrorSummary
    );
}
