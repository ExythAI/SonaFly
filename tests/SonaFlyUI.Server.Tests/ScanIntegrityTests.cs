using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// A scan is the only thing that decides a file is gone, so it has to be right about what it
/// actually saw. These cover the three ways it used to be wrong:
/// <list type="bullet">
/// <item>an unreachable root or an unreadable folder read as "the user deleted everything",
/// which marked the whole collection missing and garbage-collected its albums and artists
/// (backlog N13);</item>
/// <item>a file that came back byte-identical after being marked missing stayed hidden
/// forever, because the unchanged-file shortcut ran before presence was reconciled (N14);</item>
/// <item>retagging moved <c>Track.Genre</c> but not <c>TrackGenres</c>, so a track retagged
/// into a blocked genre stayed streamable, and an unreadable file replaced good metadata with
/// empty placeholder values (N15).</item>
/// </list>
/// </summary>
public sealed class ScanIntegrityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SonaFlyDbContext _db;
    private readonly LibraryRoot _root;

    public ScanIntegrityTests()
    {
        _connection.Open();
        _db = NewDb();
        _db.Database.EnsureCreated();

        _root = new LibraryRoot { Name = "Music", Path = Path.Combine(Path.GetTempPath(), "sonafly-scan-tests") };
        _db.LibraryRoots.Add(_root);
        _db.SaveChanges();
    }

    private SonaFlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<SonaFlyDbContext>().UseSqlite(_connection).Options);

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------- helpers

    private LibraryIndexService Service(IFileScanner scanner, IMetadataReader reader) =>
        new(_db, scanner, reader, new NoArtworkService(), NullLogger<LibraryIndexService>.Instance);

    private Task<ScanJobDto> ScanAsync(IFileScanner scanner, IMetadataReader reader, bool fullScan = false) =>
        Service(scanner, reader).ScanLibraryRootAsync(
            new ScanRequest(_root.Id, fullScan, Guid.NewGuid()), CancellationToken.None);

    private Track SeedTrack(string filePath, string genreName, string artistName, bool isMissing = false)
    {
        var artist = new Artist { Name = artistName, SortName = artistName };
        var genre = new Genre { Name = genreName };
        var album = new Album { Title = "Album", SortTitle = "Album", AlbumArtist = artist };

        var track = new Track
        {
            LibraryRootId = _root.Id,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileExtension = ".mp3",
            FileSizeBytes = 1000,
            ModifiedUtcSource = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Title = "Song",
            MimeType = "audio/mpeg",
            Album = album,
            PrimaryArtist = artist,
            Genre = genreName,
            IsIndexed = true,
            IsMissing = isMissing
        };

        _db.AddRange(artist, genre, album, track);
        _db.TrackGenres.Add(new TrackGenre { Track = track, Genre = genre });
        _db.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, Role = TrackArtistRole.Primary });
        _db.SaveChanges();
        return track;
    }

    private static DiscoveredAudioFile FileAt(string path, long size = 1000, DateTime? modified = null) =>
        new(path, Path.GetFileName(path), ".mp3", size,
            modified ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    // ---------------------------------------------------------------- N13

    [Fact]
    public async Task AnUnreachableRootFailsTheScanAndErasesNothing()
    {
        var track = SeedTrack("/music/song.mp3", "Jazz", "Coltrane");

        var job = await ScanAsync(new StubScanner(rootAvailable: false), new StubMetadataReader());

        Assert.Equal(nameof(ScanStatus.Failed), job.Status);
        Assert.Equal(0, job.FilesMissing);
        Assert.Equal(1, job.FilesUnverified);

        await _db.Entry(track).ReloadAsync();
        Assert.False(track.IsMissing);
        Assert.Equal(1, await _db.Albums.CountAsync());
        Assert.Equal(1, await _db.Artists.CountAsync());
    }

    [Fact]
    public async Task AnUnreadableFolderLeavesItsOwnTracksAloneButStillReconcilesTheRest()
    {
        var readable = SeedTrack("/music/ok/kept.mp3", "Jazz", "Coltrane");
        var unreadable = SeedTrack("/music/denied/hidden.mp3", "Rock", "Zeppelin");

        // The scan enumerated /music/ok and found nothing there; /music/denied threw.
        var scanner = new StubScanner(
            files: [],
            traversedDirectories: ["/music/ok"],
            failedPaths: ["/music/denied"]);

        var job = await ScanAsync(scanner, new StubMetadataReader());

        Assert.Equal(nameof(ScanStatus.Partial), job.Status);

        await _db.Entry(readable).ReloadAsync();
        await _db.Entry(unreadable).ReloadAsync();

        // We looked in /music/ok and the file was not there — that is real evidence.
        Assert.True(readable.IsMissing);
        // We never got into /music/denied, so its absence proves nothing.
        Assert.False(unreadable.IsMissing);
        Assert.Equal(1, job.FilesMissing);
        Assert.Equal(1, job.FilesUnverified);
    }

    [Fact]
    public async Task APartialScanDoesNotGarbageCollectMetadata()
    {
        SeedTrack("/music/denied/hidden.mp3", "Rock", "Zeppelin");

        await ScanAsync(new StubScanner(files: [], traversedDirectories: [], failedPaths: ["/music/denied"]),
            new StubMetadataReader());

        Assert.Equal(1, await _db.Albums.CountAsync());
        Assert.Equal(1, await _db.Artists.CountAsync());
        Assert.Equal(1, await _db.Genres.CountAsync());
    }

    [Fact]
    public async Task AMissingTrackKeepsItsAlbumAndArtistIdentity()
    {
        var track = SeedTrack("/music/song.mp3", "Jazz", "Coltrane");
        var albumId = track.AlbumId;
        var artistId = track.PrimaryArtistId;

        // A clean scan of an empty (but readable) folder: the file really is gone.
        var job = await ScanAsync(
            new StubScanner(files: [], traversedDirectories: ["/music"]),
            new StubMetadataReader());

        Assert.Equal(nameof(ScanStatus.Completed), job.Status);
        await _db.Entry(track).ReloadAsync();
        Assert.True(track.IsMissing);

        // The track row still exists, so its album and artist must survive orphan cleanup —
        // playlists, mixed tapes and ID-based restrictions all point at these IDs.
        Assert.True(await _db.Albums.AnyAsync(a => a.Id == albumId));
        Assert.True(await _db.Artists.AnyAsync(a => a.Id == artistId));
    }

    // ---------------------------------------------------------------- N14

    [Fact]
    public async Task AnIdenticalFileComingBackBecomesVisibleAgainOnAnIncrementalScan()
    {
        var track = SeedTrack("/music/song.mp3", "Jazz", "Coltrane", isMissing: true);

        // Same size, same modification time — the unchanged-file shortcut would skip this.
        var job = await ScanAsync(
            new StubScanner(files: [FileAt("/music/song.mp3")], traversedDirectories: ["/music"]),
            new StubMetadataReader());

        Assert.Equal(nameof(ScanStatus.Completed), job.Status);
        await _db.Entry(track).ReloadAsync();
        Assert.False(track.IsMissing);
        Assert.True(track.IsIndexed);
    }

    [Fact]
    public async Task AnUnchangedPresentFileIsStillSkipped()
    {
        var track = SeedTrack("/music/song.mp3", "Jazz", "Coltrane");

        var job = await ScanAsync(
            new StubScanner(files: [FileAt("/music/song.mp3")], traversedDirectories: ["/music"]),
            new StubMetadataReader());

        Assert.Equal(0, job.FilesUpdated);
        await _db.Entry(track).ReloadAsync();
        Assert.False(track.IsMissing);
    }

    // ---------------------------------------------------------------- N15

    [Fact]
    public async Task RetaggingMovesTheGenreAndArtistJunctionsAndNotJustTheScalars()
    {
        var track = SeedTrack("/music/song.mp3", "Jazz", "Coltrane");

        var reader = new StubMetadataReader(new AudioMetadata
        {
            Title = "Song",
            Album = "Album",
            Artist = "Zeppelin",
            Genre = "Rock",
            MimeType = "audio/mpeg"
        });

        // A changed file (different size) so the update path runs.
        await ScanAsync(new StubScanner(
            files: [FileAt("/music/song.mp3", size: 2000)],
            traversedDirectories: ["/music"]), reader);

        using var verify = NewDb();

        var genreLinks = await verify.TrackGenres
            .Where(tg => tg.TrackId == track.Id)
            .Select(tg => tg.Genre!.Name)
            .ToListAsync();
        Assert.Equal(["Rock"], genreLinks);

        var artistLinks = await verify.TrackArtists
            .Where(ta => ta.TrackId == track.Id && ta.Role == TrackArtistRole.Primary)
            .Select(ta => ta.Artist!.Name)
            .ToListAsync();
        Assert.Equal(["Zeppelin"], artistLinks);

        var stored = await verify.Tracks.FirstAsync(t => t.Id == track.Id);
        Assert.Equal("Rock", stored.Genre);
    }

    [Fact]
    public async Task RemovingATagRemovesTheJunctionRatherThanLeavingTheOldOne()
    {
        var track = SeedTrack("/music/song.mp3", "Jazz", "Coltrane");

        var reader = new StubMetadataReader(new AudioMetadata
        {
            Title = "Song",
            Album = "Album",
            Artist = "Coltrane",
            Genre = null,
            MimeType = "audio/mpeg"
        });

        await ScanAsync(new StubScanner(
            files: [FileAt("/music/song.mp3", size: 2000)],
            traversedDirectories: ["/music"]), reader);

        using var verify = NewDb();
        Assert.False(await verify.TrackGenres.AnyAsync(tg => tg.TrackId == track.Id));
    }

    [Fact]
    public async Task AnUnparseableFileKeepsItsLastKnownGoodMetadata()
    {
        var track = SeedTrack("/music/song.mp3", "Jazz", "Coltrane");

        // What MetadataReader returns when TagLib throws: a filename placeholder, flagged.
        var reader = new StubMetadataReader(new AudioMetadata
        {
            Title = "song",
            MimeType = "audio/mpeg",
            ReadFailed = true
        });

        await ScanAsync(new StubScanner(
            files: [FileAt("/music/song.mp3", size: 2000)],
            traversedDirectories: ["/music"]), reader);

        using var verify = NewDb();
        var stored = await verify.Tracks.FirstAsync(t => t.Id == track.Id);

        Assert.Equal("Song", stored.Title);
        Assert.Equal("Jazz", stored.Genre);
        Assert.NotNull(stored.PrimaryArtistId);
        // The junction restrictions are enforced through must survive too.
        Assert.True(await verify.TrackGenres.AnyAsync(tg => tg.TrackId == track.Id));

        // The file facts we do know are still updated.
        Assert.Equal(2000, stored.FileSizeBytes);
        Assert.False(stored.IsMissing);
    }

    // ---------------------------------------------------------------- stubs

    private sealed class StubScanner(
        IReadOnlyList<DiscoveredAudioFile>? files = null,
        IReadOnlyList<string>? traversedDirectories = null,
        IReadOnlyList<string>? failedPaths = null,
        bool rootAvailable = true) : IFileScanner
    {
        public async IAsyncEnumerable<DiscoveredAudioFile> EnumerateAudioFilesAsync(
            string rootPath,
            ScanTraversalReport report,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;

            report.MarkRootAvailable(rootAvailable);
            if (!rootAvailable)
            {
                report.RecordFailure(rootPath, "offline");
                yield break;
            }

            foreach (var directory in traversedDirectories ?? [])
                report.MarkDirectoryTraversed(directory);

            foreach (var failure in failedPaths ?? [])
                report.RecordFailure(failure, "access denied");

            foreach (var file in files ?? [])
                yield return file;
        }
    }

    private sealed class StubMetadataReader(AudioMetadata? metadata = null) : IMetadataReader
    {
        public Task<AudioMetadata> ReadAsync(string filePath, CancellationToken ct) =>
            Task.FromResult(metadata ?? new AudioMetadata
            {
                Title = "Song",
                Album = "Album",
                Artist = "Coltrane",
                Genre = "Jazz",
                MimeType = "audio/mpeg"
            });
    }

    /// <summary>Artwork is out of scope here; these tests are about index correctness.</summary>
    private sealed class NoArtworkService : IArtworkService
    {
        public Task<ArtworkResult?> ExtractAndStoreAsync(AudioMetadata metadata, string filePath, CancellationToken ct) =>
            Task.FromResult<ArtworkResult?>(null);

        public Task<FileStreamResultModel?> OpenArtworkAsync(Guid artworkId, CancellationToken ct) =>
            Task.FromResult<FileStreamResultModel?>(null);
    }
}
