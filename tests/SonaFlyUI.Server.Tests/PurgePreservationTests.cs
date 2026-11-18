using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// A purge used to delete playlists, mixed tapes and restrictions along with the library.
/// They are now carried across it and re-attached when a rescan finds the same files,
/// and the database is snapshotted before anything is removed.
/// </summary>
public sealed class PurgePreservationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly LibraryLinkPreserver _links;
    private readonly string _scratch = Directory.CreateTempSubdirectory("sonafly-purge-tests").FullName;

    private readonly ApplicationUser _user = new()
    {
        Id = Guid.NewGuid(), UserName = "listener", Email = "listener@sonafly.local", IsEnabled = true
    };
    private readonly LibraryRoot _root = new() { Name = "Music", Path = "/music" };

    public PurgePreservationTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        _db = _services.GetRequiredService<SonaFlyDbContext>();
        _db.Database.EnsureCreated();
        _db.Users.Add(_user);
        _db.LibraryRoots.Add(_root);
        _db.SaveChanges();
        _links = new LibraryLinkPreserver(_db, NullLogger<LibraryLinkPreserver>.Instance);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ }
    }

    private Track AddTrack(string path, string title, Album? album = null, Artist? artist = null)
    {
        var track = new Track
        {
            LibraryRootId = _root.Id, FilePath = path, FileName = Path.GetFileName(path),
            FileExtension = ".mp3", Title = title, MimeType = "audio/mpeg", IsIndexed = true,
            Album = album, PrimaryArtist = artist,
        };
        _db.Tracks.Add(track);
        return track;
    }

    /// <summary>What the purge does to the library rows, without the controller around it.</summary>
    private async Task PurgeAsync()
    {
        await _links.ParkAsync(CancellationToken.None);
        await _db.PlaylistItems.ExecuteDeleteAsync();
        await _db.MixedTapeItems.ExecuteDeleteAsync();
        await _db.UserRestrictions.ExecuteDeleteAsync();
        await _db.FavoriteTracks.ExecuteDeleteAsync();
        await _db.TrackGenres.ExecuteDeleteAsync();
        await _db.Tracks.ExecuteDeleteAsync();
        await _db.Albums.ExecuteDeleteAsync();
        await _db.Artists.ExecuteDeleteAsync();
        await _db.Genres.ExecuteDeleteAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task PlaylistAndMixedTapeEntriesComeBackInOrderWhenTheSameFilesAreRescanned()
    {
        var first = AddTrack("/music/a.mp3", "A");
        var second = AddTrack("/music/b.mp3", "B");
        var playlist = new Playlist { Name = "Road trip", OwnerUserId = _user.Id };
        var tape = new MixedTape { Name = "Side A", OwnerUserId = _user.Id };
        _db.AddRange(playlist, tape);
        _db.PlaylistItems.AddRange(
            new PlaylistItem { Playlist = playlist, Track = second, SortOrder = 0 },
            new PlaylistItem { Playlist = playlist, Track = first, SortOrder = 1 });
        _db.MixedTapeItems.Add(new MixedTapeItem { MixedTape = tape, Track = first, SortOrder = 0 });
        _db.FavoriteTracks.Add(new FavoriteTrack { UserId = _user.Id, Track = second });
        await _db.SaveChangesAsync();

        await PurgeAsync();

        // The playlist and tape survive, empty, with their entries parked.
        Assert.Equal("Road trip", (await _db.Playlists.SingleAsync()).Name);
        Assert.Equal("Side A", (await _db.MixedTapes.SingleAsync()).Name);
        Assert.Equal(0, await _db.PlaylistItems.CountAsync());
        Assert.Equal(4, await _db.ParkedLibraryLinks.CountAsync());

        // A rescan issues new IDs for the same files.
        var rescannedA = AddTrack("/music/a.mp3", "A");
        var rescannedB = AddTrack("/music/b.mp3", "B");
        await _db.SaveChangesAsync();
        Assert.Equal(4, await _links.RestoreAsync(CancellationToken.None));

        var items = await _db.PlaylistItems.OrderBy(i => i.SortOrder).ToListAsync();
        Assert.Equal([rescannedB.Id, rescannedA.Id], items.Select(i => i.TrackId));
        Assert.Equal(rescannedA.Id, (await _db.MixedTapeItems.SingleAsync()).TrackId);
        Assert.Equal(rescannedB.Id, (await _db.FavoriteTracks.SingleAsync()).TrackId);
        Assert.Equal(0, await _db.ParkedLibraryLinks.CountAsync());
    }

    [Fact]
    public async Task AnEntryWhoseFileHasNotReturnedStaysParkedForALaterScan()
    {
        var kept = AddTrack("/music/kept.mp3", "Kept");
        var gone = AddTrack("/music/gone.mp3", "Gone");
        var playlist = new Playlist { Name = "Mix", OwnerUserId = _user.Id };
        _db.Add(playlist);
        _db.PlaylistItems.AddRange(
            new PlaylistItem { Playlist = playlist, Track = kept, SortOrder = 0 },
            new PlaylistItem { Playlist = playlist, Track = gone, SortOrder = 1 });
        await _db.SaveChangesAsync();
        await PurgeAsync();

        AddTrack("/music/kept.mp3", "Kept");
        await _db.SaveChangesAsync();
        Assert.Equal(1, await _links.RestoreAsync(CancellationToken.None));
        Assert.Equal("/music/gone.mp3", (await _db.ParkedLibraryLinks.SingleAsync()).FilePath);

        // The drive comes back; a later scan finishes the job, and nothing is duplicated.
        AddTrack("/music/gone.mp3", "Gone");
        await _db.SaveChangesAsync();
        Assert.Equal(1, await _links.RestoreAsync(CancellationToken.None));
        Assert.Equal(0, await _links.RestoreAsync(CancellationToken.None));
        Assert.Equal(2, await _db.PlaylistItems.CountAsync());
    }

    [Fact]
    public async Task AnEntryForAPlaylistDeletedMeanwhileIsDropped()
    {
        var track = AddTrack("/music/a.mp3", "A");
        var playlist = new Playlist { Name = "Short-lived", OwnerUserId = _user.Id };
        _db.Add(playlist);
        _db.PlaylistItems.Add(new PlaylistItem { Playlist = playlist, Track = track });
        await _db.SaveChangesAsync();
        await PurgeAsync();

        await _db.Playlists.ExecuteDeleteAsync();
        AddTrack("/music/a.mp3", "A");
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _links.RestoreAsync(CancellationToken.None));
        Assert.Equal(0, await _db.PlaylistItems.CountAsync());
        Assert.Equal(0, await _db.ParkedLibraryLinks.CountAsync());
    }

    [Fact]
    public async Task RestrictionsFollowTheAlbumArtistAndGenreByNameToTheirNewIds()
    {
        var artist = new Artist { Name = "Explicit Band" };
        var other = new Artist { Name = "Family Band" };
        var album = new Album { Title = "Greatest Hits", AlbumArtist = artist };
        var sameTitle = new Album { Title = "Greatest Hits", AlbumArtist = other };
        var genre = new Genre { Name = "Horrorcore" };
        _db.AddRange(artist, other, album, sameTitle, genre);
        await _db.SaveChangesAsync();
        _db.UserRestrictions.AddRange(
            new UserRestriction { UserId = _user.Id, RestrictionType = RestrictionType.Album, TargetId = album.Id, TargetName = "stale label" },
            new UserRestriction { UserId = _user.Id, RestrictionType = RestrictionType.Artist, TargetId = artist.Id },
            new UserRestriction { UserId = _user.Id, RestrictionType = RestrictionType.Genre, TargetId = genre.Id });
        await _db.SaveChangesAsync();

        await PurgeAsync();
        Assert.Equal(0, await _db.UserRestrictions.CountAsync());

        var newArtist = new Artist { Name = "Explicit Band" };
        var newOther = new Artist { Name = "Family Band" };
        var newAlbum = new Album { Title = "Greatest Hits", AlbumArtist = newArtist };
        var newSameTitle = new Album { Title = "Greatest Hits", AlbumArtist = newOther };
        var newGenre = new Genre { Name = "Horrorcore" };
        _db.AddRange(newArtist, newOther, newAlbum, newSameTitle, newGenre);
        await _db.SaveChangesAsync();

        Assert.Equal(3, await _links.RestoreAsync(CancellationToken.None));

        var restored = await _db.UserRestrictions.ToListAsync();
        Assert.Contains(restored, r => r.RestrictionType == RestrictionType.Album && r.TargetId == newAlbum.Id);
        Assert.Contains(restored, r => r.RestrictionType == RestrictionType.Artist && r.TargetId == newArtist.Id);
        Assert.Contains(restored, r => r.RestrictionType == RestrictionType.Genre && r.TargetId == newGenre.Id);
        // The other artist's album of the same name is not swept up with it.
        Assert.DoesNotContain(restored, r => r.TargetId == newSameTitle.Id);
    }

    [Fact]
    public async Task AScanRestoresEntriesInTheSameStepThatMakesItsTracksVisible()
    {
        var track = AddTrack("/music/a.mp3", "A");
        var playlist = new Playlist { Name = "Mix", OwnerUserId = _user.Id };
        _db.Add(playlist);
        _db.PlaylistItems.Add(new PlaylistItem { Playlist = playlist, Track = track });
        await _db.SaveChangesAsync();
        await PurgeAsync();

        var job = new ScanJob { LibraryRootId = _root.Id };
        _db.ScanJobs.Add(job);
        await _db.SaveChangesAsync();

        var service = new LibraryIndexService(
            _db, new OneFileScanner("/music/a.mp3"), new FixedMetadataReader(), new NoArtwork(),
            NullLogger<LibraryIndexService>.Instance, _links);
        await service.ScanLibraryRootAsync(new ScanRequest(_root.Id, false, job.Id), CancellationToken.None);

        var item = await _db.PlaylistItems.Include(i => i.Track).SingleAsync();
        Assert.Equal("/music/a.mp3", item.Track.FilePath);
        Assert.Equal(playlist.Id, item.PlaylistId);
    }

    // ── Backups ──

    private DatabaseBackupService BackupsFor(string databasePath, BackupOptions? options = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}"
            })
            .Build();
        return new DatabaseBackupService(config, Options.Create(options ?? new BackupOptions()),
            NullLogger<DatabaseBackupService>.Instance);
    }

    private static async Task<string> CreateDatabaseAsync(string path, string playlistName)
    {
        var options = new DbContextOptionsBuilder<SonaFlyDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString)
            .Options;
        await using var db = new SonaFlyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Playlists.Add(new Playlist { Name = playlistName });
        await db.SaveChangesAsync();
        return path;
    }

    [Fact]
    public async Task ABackupIsACompleteDatabaseThatOpensOnItsOwn()
    {
        var database = await CreateDatabaseAsync(Path.Combine(_scratch, "sonafly.db"), "Keep me");
        var backups = BackupsFor(database);

        var backup = await backups.CreateAsync(DatabaseBackupService.BeforePurge, CancellationToken.None);

        Assert.NotNull(backup);
        Assert.Equal(DatabaseBackupService.BeforePurge, backup.Reason);
        // Beside the database by default, so it lives in the same persistent volume.
        var file = Path.Combine(_scratch, "backups", backup.FileName);
        Assert.True(File.Exists(file));
        Assert.Empty(Directory.GetFiles(Path.Combine(_scratch, "backups"), "*.partial"));

        var options = new DbContextOptionsBuilder<SonaFlyDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ConnectionString)
            .Options;
        await using var restored = new SonaFlyDbContext(options);
        Assert.Equal("Keep me", (await restored.Playlists.SingleAsync()).Name);
        Assert.Equal(backup.FileName, Assert.Single(backups.List()).FileName);
    }

    [Fact]
    public async Task OnlyTheNewestBackupsOfEachKindAreKept()
    {
        var database = await CreateDatabaseAsync(Path.Combine(_scratch, "sonafly.db"), "x");
        var directory = Path.Combine(_scratch, "backups");
        Directory.CreateDirectory(directory);
        // Older snapshots, as if left by earlier nights.
        foreach (var day in Enumerable.Range(1, 5))
            await File.WriteAllTextAsync(Path.Combine(directory, $"sonafly-2026010{day}-030000-scheduled.db"), "old");
        await File.WriteAllTextAsync(Path.Combine(directory, "sonafly-20260101-030000-pre-purge.db"), "old");
        await File.WriteAllTextAsync(Path.Combine(directory, "notes.txt"), "not ours");

        var backups = BackupsFor(database, new BackupOptions { Retain = 3 });
        await backups.CreateAsync(DatabaseBackupService.Scheduled, CancellationToken.None);

        var scheduled = backups.List().Where(b => b.Reason == DatabaseBackupService.Scheduled).ToList();
        Assert.Equal(3, scheduled.Count);
        Assert.DoesNotContain(scheduled, b => b.FileName.Contains("20260101") || b.FileName.Contains("20260102") || b.FileName.Contains("20260103"));
        // Other kinds are counted separately, and files that are not backups are left alone.
        Assert.Contains(backups.List(), b => b.Reason == DatabaseBackupService.BeforePurge);
        Assert.True(File.Exists(Path.Combine(directory, "notes.txt")));
        Assert.NotNull(backups.LastScheduledUtc());
    }

    [Fact]
    public async Task AnInMemoryDatabaseHasNothingToBackUp()
    {
        var backups = BackupsFor(":memory:");

        Assert.Null(await backups.CreateAsync(DatabaseBackupService.Manual, CancellationToken.None));
        Assert.Null(backups.BackupDirectory());
        Assert.Empty(backups.List());
    }

    // ── Stubs ──

    private sealed class OneFileScanner(string path) : IFileScanner
    {
        public async IAsyncEnumerable<DiscoveredAudioFile> EnumerateAudioFilesAsync(
            string rootPath, ScanTraversalReport report,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            report.MarkRootAvailable(true);
            report.MarkDirectoryTraversed("/music");
            yield return new DiscoveredAudioFile(path, Path.GetFileName(path), ".mp3", 1000,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }

    private sealed class FixedMetadataReader : IMetadataReader
    {
        public Task<AudioMetadata> ReadAsync(string filePath, CancellationToken ct) =>
            Task.FromResult(new AudioMetadata { Title = "A", Album = "Album", Artist = "Artist", MimeType = "audio/mpeg" });
    }

    private sealed class NoArtwork : IArtworkService
    {
        public Task<ArtworkResult?> ExtractAndStoreAsync(AudioMetadata metadata, string filePath, CancellationToken ct) =>
            Task.FromResult<ArtworkResult?>(null);

        public Task<FileStreamResultModel?> OpenArtworkAsync(Guid artworkId, CancellationToken ct) =>
            Task.FromResult<FileStreamResultModel?>(null);
    }
}
