using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Api.Hubs;
using SonaFlyUI.Server.Application.Common;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Administrative surfaces that used to be too trusting: root paths were normalized by trimming
/// separators (N18), list endpoints accepted any page size and ordered by non-unique columns
/// (N20), scan diagnostics and genre counts leaked to restricted listeners (N21), and the purge
/// would recursively delete whatever the artwork-cache setting happened to point at (N16).
/// </summary>
public sealed class LibraryAdministrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly string _scratch = Directory.CreateTempSubdirectory("sonafly-admin-tests").FullName;

    private readonly ApplicationUser _listener = new()
    {
        Id = Guid.NewGuid(), UserName = "listener", Email = "listener@sonafly.local", IsEnabled = true
    };

    public LibraryAdministrationTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        _db = _services.GetRequiredService<SonaFlyDbContext>();
        _db.Database.EnsureCreated();
        _db.Users.Add(_listener);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ }
    }

    private string NewFolder(string name)
    {
        var path = Path.Combine(_scratch, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private ControllerContext ContextFor(bool admin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _listener.Id.ToString()),
            new(ClaimTypes.Name, _listener.UserName!)
        };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
    }

    // ---------------------------------------------------------------- N18

    [Fact]
    public async Task ATrailingSeparatorIsCanonicalizedRatherThanTrimmedBlindly()
    {
        var folder = NewFolder("library");
        var service = new LibraryRootService(_db);

        var id = await service.CreateAsync(
            new CreateLibraryRootRequest("Music", folder + Path.DirectorySeparatorChar), CancellationToken.None);

        var stored = await _db.LibraryRoots.FirstAsync(r => r.Id == id);
        Assert.Equal(Path.GetFullPath(folder), stored.Path);
    }

    [Fact]
    public async Task DuplicateDetectionComparesTheCanonicalizedPathNotTheRawInput()
    {
        var folder = NewFolder("library");
        var service = new LibraryRootService(_db);

        await service.CreateAsync(new CreateLibraryRootRequest("Music", folder), CancellationToken.None);

        // Same directory, differently spelled: previously the raw-string comparison missed it
        // and a second root was created for the same tree.
        var spelled = Path.Combine(folder, "sub", "..") + Path.DirectorySeparatorChar;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateLibraryRootRequest("Music again", spelled), CancellationToken.None));
    }

    [Fact]
    public async Task ARelativePathIsRejectedRatherThanStoredAsIs()
    {
        var service = new LibraryRootService(_db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(new CreateLibraryRootRequest("Music", "music/library"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAppliesTheSameAccessibilityCheckAsCreate()
    {
        var folder = NewFolder("library");
        var service = new LibraryRootService(_db);
        var id = await service.CreateAsync(new CreateLibraryRootRequest("Music", folder), CancellationToken.None);

        var gone = Path.Combine(_scratch, "not-there");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(id, new UpdateLibraryRootRequest(null, gone, null, null), CancellationToken.None));

        var stored = await _db.LibraryRoots.FirstAsync(r => r.Id == id);
        Assert.Equal(Path.GetFullPath(folder), stored.Path);
    }

    [Fact]
    public void AFilesystemRootSurvivesNormalization()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\" : "/";

        Assert.True(FileSystemPaths.TryNormalize(root, out var normalized, out _));
        Assert.Equal(Path.GetFullPath(root), normalized);
        // The old TrimEnd turned these into "" and "C:", a drive-relative path.
        Assert.NotEqual(string.Empty, normalized);
        Assert.NotEqual("C:", normalized);
    }

    // ---------------------------------------------------------------- N16

    [Fact]
    public void CachePathContainmentIsDetectedInBothDirections()
    {
        var parent = NewFolder("data");
        var child = NewFolder(Path.Combine("data", "artwork"));

        Assert.True(FileSystemPaths.IsSameOrUnder(child, parent));
        Assert.True(FileSystemPaths.IsSameOrUnder(parent, parent));
        Assert.False(FileSystemPaths.IsSameOrUnder(parent, child));

        // A sibling whose name merely starts with the same characters is not inside it.
        Assert.False(FileSystemPaths.IsSameOrUnder(parent + "-backup", parent));
    }

    [Fact]
    public async Task ThePurgeRefusesToClearAnArtworkCacheThatOverlapsTheMusicLibrary()
    {
        var music = NewFolder("music");
        var decoy = Path.Combine(music, "precious.flac");
        await File.WriteAllTextAsync(decoy, "not really audio");

        _db.LibraryRoots.Add(new LibraryRoot { Name = "Music", Path = music });
        await _db.SaveChangesAsync();

        // Misconfiguration: the artwork cache points at the music library.
        var controller = PurgeController(artworkRoot: music);

        var result = await controller.PurgeLibraryData(CancellationToken.None);

        var payload = Assert.IsType<OkObjectResult>(result).Value!;
        Assert.False((bool)payload.GetType().GetProperty("artworkCacheCleared")!.GetValue(payload)!);

        // The decisive assertion: the music file is untouched.
        Assert.True(File.Exists(decoy));
    }

    [Fact]
    public async Task ThePurgeClearsOnlyTheContentsOfADedicatedCacheAndKeepsTheDirectory()
    {
        var music = NewFolder("music");
        var cache = NewFolder("artwork");
        var cached = Path.Combine(cache, "ab");
        Directory.CreateDirectory(cached);
        await File.WriteAllTextAsync(Path.Combine(cached, "abcd.jpg"), "image");

        _db.LibraryRoots.Add(new LibraryRoot { Name = "Music", Path = music });
        await _db.SaveChangesAsync();

        var controller = PurgeController(artworkRoot: cache);
        var result = await controller.PurgeLibraryData(CancellationToken.None);

        var payload = Assert.IsType<OkObjectResult>(result).Value!;
        Assert.True((bool)payload.GetType().GetProperty("artworkCacheCleared")!.GetValue(payload)!);

        Assert.True(Directory.Exists(cache));           // the mount point stays
        Assert.False(Directory.Exists(cached));         // its contents do not
    }

    private SystemController PurgeController(string artworkRoot)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SonaFly:ArtworkRoot"] = artworkRoot,
                ["SonaFly:DataProtectionKeyRoot"] = NewFolder("keys")
            })
            .Build();

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hubContext = new Mock<IHubContext<AuditoriumHub>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

        var state = new AuditoriumStateService();
        var scheduler = new TrackEndSchedulerService(
            hubContext.Object,
            _services.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<IHostApplicationLifetime>(),
            state,
            NullLogger<TrackEndSchedulerService>.Instance);

        return new SystemController(_db, config, new LibraryMaintenanceGate(), scheduler,
            NullLogger<SystemController>.Instance)
        {
            ControllerContext = ContextFor(admin: true)
        };
    }

    // ---------------------------------------------------------------- N20

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, -10)]
    [InlineData(1, 100_000)]
    [InlineData(int.MaxValue, 50)]
    public async Task OutOfRangePagingIsRejectedRatherThanProducingABadOffset(int page, int pageSize)
    {
        var controller = new TracksController(_db) { ControllerContext = ContextFor(admin: false) };

        var result = await controller.GetAll(page, pageSize, ct: CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AnOverlongFilterIsRejected()
    {
        var controller = new TracksController(_db) { ControllerContext = ContextFor(admin: false) };

        var result = await controller.GetAll(1, 50, filter: new string('x', Pagination.MaxQueryLength + 1),
            ct: CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task DuplicateTitlesDoNotRepeatOrVanishAcrossPageBoundaries()
    {
        var root = new LibraryRoot { Name = "Music", Path = NewFolder("library") };
        _db.LibraryRoots.Add(root);

        // Ten tracks sharing one title: without a unique tiebreaker their relative order is
        // undefined, so a row can appear on both pages or on neither.
        for (var i = 0; i < 10; i++)
        {
            _db.Tracks.Add(new Track
            {
                LibraryRoot = root,
                Title = "Same Title",
                FilePath = $"/music/{i}.mp3",
                FileName = $"{i}.mp3",
                MimeType = "audio/mpeg",
                IsIndexed = true
            });
        }
        await _db.SaveChangesAsync();

        var controller = new TracksController(_db) { ControllerContext = ContextFor(admin: false) };

        var first = Assert.IsType<OkObjectResult>(
            (await controller.GetAll(1, 5, ct: CancellationToken.None)).Result).Value;
        var second = Assert.IsType<OkObjectResult>(
            (await controller.GetAll(2, 5, ct: CancellationToken.None)).Result).Value;

        var firstIds = ((PaginatedResult<TrackListItemDto>)first!).Items.Select(t => t.Id).ToList();
        var secondIds = ((PaginatedResult<TrackListItemDto>)second!).Items.Select(t => t.Id).ToList();

        Assert.Equal(10, firstIds.Concat(secondIds).Distinct().Count());
    }

    // ---------------------------------------------------------------- N21

    [Fact]
    public async Task ARestrictedListenerSeesNeitherTheBlockedGenreNorItsCount()
    {
        var root = new LibraryRoot { Name = "Music", Path = NewFolder("library") };
        var blocked = new Genre { Name = "Blocked" };
        var allowed = new Genre { Name = "Allowed" };

        var blockedTrack = NewTrack(root, "Hidden");
        var allowedTrack = NewTrack(root, "Visible");
        var missingTrack = NewTrack(root, "Gone");
        missingTrack.IsMissing = true;

        _db.AddRange(root, blocked, allowed, blockedTrack, allowedTrack, missingTrack);
        _db.TrackGenres.Add(new TrackGenre { Track = blockedTrack, Genre = blocked });
        _db.TrackGenres.Add(new TrackGenre { Track = allowedTrack, Genre = allowed });
        _db.TrackGenres.Add(new TrackGenre { Track = missingTrack, Genre = allowed });
        _db.UserRestrictions.Add(new UserRestriction
        {
            UserId = _listener.Id, RestrictionType = RestrictionType.Genre, TargetId = blocked.Id
        });
        await _db.SaveChangesAsync();

        var controller = new GenresController(_db) { ControllerContext = ContextFor(admin: false) };
        var genres = Assert.IsType<OkObjectResult>(
            (await controller.GetAll(CancellationToken.None)).Result).Value as IReadOnlyList<GenreDto>;

        Assert.NotNull(genres);
        Assert.DoesNotContain(genres, g => g.Name == "Blocked");

        // The count for a visible genre excludes the missing track.
        var visible = Assert.Single(genres);
        Assert.Equal("Allowed", visible.Name);
        Assert.Equal(1, visible.TrackCount);
    }

    [Fact]
    public async Task RawScanDiagnosticsAreAdminOnly()
    {
        var root = new LibraryRoot { Name = "Music", Path = NewFolder("library") };
        _db.LibraryRoots.Add(root);
        _db.ScanJobs.Add(new ScanJob
        {
            LibraryRoot = root,
            Status = ScanStatus.Completed,
            StartedUtc = DateTime.UtcNow,
            CompletedUtc = DateTime.UtcNow,
            ErrorsCount = 2,
            ErrorSummary = @"D:\Music\private\secret.flac: TagLib.CorruptFileException"
        });
        await _db.SaveChangesAsync();

        var asListener = new ScansController(_db) { ControllerContext = ContextFor(admin: false) };
        var listenerJobs = Assert.IsType<OkObjectResult>(
            (await asListener.GetAll(CancellationToken.None)).Result).Value as IReadOnlyList<ScanJobDto>;

        var seen = Assert.Single(listenerJobs!);
        Assert.DoesNotContain("secret.flac", seen.ErrorSummary ?? string.Empty);
        Assert.Contains("2 file(s)", seen.ErrorSummary);

        var asAdmin = new ScansController(_db) { ControllerContext = ContextFor(admin: true) };
        var adminJobs = Assert.IsType<OkObjectResult>(
            (await asAdmin.GetAll(CancellationToken.None)).Result).Value as IReadOnlyList<ScanJobDto>;

        Assert.Contains("secret.flac", Assert.Single(adminJobs!).ErrorSummary);
    }

    private static Track NewTrack(LibraryRoot root, string title) => new()
    {
        LibraryRoot = root,
        Title = title,
        FilePath = $"/music/{title}.mp3",
        FileName = $"{title}.mp3",
        MimeType = "audio/mpeg",
        IsIndexed = true
    };
}
