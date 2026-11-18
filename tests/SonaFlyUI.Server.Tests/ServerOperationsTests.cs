using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.BackgroundServices;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Logging;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// The server looking after itself: scanning on a schedule, serving covers at the size
/// they are shown, and keeping its log somewhere that outlives the container.
/// </summary>
public sealed class ServerOperationsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly string _scratch = Directory.CreateTempSubdirectory("sonafly-ops-tests").FullName;
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    public ServerOperationsTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        _db = _services.GetRequiredService<SonaFlyDbContext>();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ }
    }

    // ── Scheduled scans ──

    private sealed class RecordingQueue(bool accept = true) : IScanQueue
    {
        public List<ScanRequest> Requests { get; } = [];

        public bool TryEnqueue(ScanRequest request)
        {
            if (accept) Requests.Add(request);
            return accept;
        }

        public ValueTask<ScanRequest> DequeueAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private ScheduledScanService Scheduler(IScanQueue queue) => new(
        _services.GetRequiredService<IServiceScopeFactory>(), queue,
        Options.Create(new ScanScheduleOptions()), NullLogger<ScheduledScanService>.Instance);

    private LibraryRoot AddRoot(string name, DateTime? lastScan, bool enabled = true)
    {
        var root = new LibraryRoot
        {
            Name = name, Path = "/music/" + name, IsEnabled = enabled,
            LastScanCompletedUtc = lastScan, LastScanStatus = lastScan == null ? null : ScanStatus.Completed,
        };
        _db.LibraryRoots.Add(root);
        _db.SaveChanges();
        return root;
    }

    [Fact]
    public async Task ARootScannedLongerAgoThanTheIntervalIsScannedAgainIncrementally()
    {
        var due = AddRoot("due", Now.AddHours(-25));
        AddRoot("fresh", Now.AddHours(-2));
        AddRoot("disabled", Now.AddDays(-9), enabled: false);
        var queue = new RecordingQueue();

        var queued = await Scheduler(queue).QueueDueScansAsync(TimeSpan.FromHours(24), Now, CancellationToken.None);

        Assert.Equal(1, queued);
        var request = Assert.Single(queue.Requests);
        Assert.Equal(due.Id, request.LibraryRootId);
        Assert.False(request.FullScan);
        // The job is in scan history like one started by hand.
        var job = await _db.ScanJobs.AsNoTracking().SingleAsync();
        Assert.Equal(request.ScanJobId, job.Id);
        Assert.Equal(ScanStatus.Queued, job.Status);
    }

    [Fact]
    public async Task ARootThatHasNeverBeenScannedWaitsForSomeoneToStartIt()
    {
        // Also the state every root is left in by a purge.
        AddRoot("new", lastScan: null);
        var queue = new RecordingQueue();

        Assert.Equal(0, await Scheduler(queue).QueueDueScansAsync(TimeSpan.FromHours(24), Now, CancellationToken.None));
        Assert.Empty(queue.Requests);
    }

    [Fact]
    public async Task AScanAlreadyWaitingOrRunningIsNotQueuedTwice()
    {
        var root = AddRoot("busy", Now.AddDays(-3));
        _db.ScanJobs.Add(new ScanJob { LibraryRootId = root.Id, Status = ScanStatus.Running });
        await _db.SaveChangesAsync();
        var queue = new RecordingQueue();

        Assert.Equal(0, await Scheduler(queue).QueueDueScansAsync(TimeSpan.FromHours(24), Now, CancellationToken.None));
        Assert.Equal(1, await _db.ScanJobs.CountAsync());
    }

    [Fact]
    public async Task WhenTheWorkerIsSaturatedNoOrphanJobIsLeftBehind()
    {
        var root = AddRoot("due", Now.AddDays(-3));
        var queue = new RecordingQueue(accept: false);

        Assert.Equal(0, await Scheduler(queue).QueueDueScansAsync(TimeSpan.FromHours(24), Now, CancellationToken.None));

        Assert.Equal(0, await _db.ScanJobs.CountAsync());
        Assert.Equal(ScanStatus.Completed,
            (await _db.LibraryRoots.AsNoTracking().SingleAsync(r => r.Id == root.Id)).LastScanStatus);
    }

    // ── Artwork thumbnails ──

    private static byte[] Picture(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Crimson);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(1, 160)]
    [InlineData(160, 160)]
    [InlineData(161, 320)]
    [InlineData(300, 320)]
    [InlineData(640, 640)]
    [InlineData(641, null)]
    public void ARequestedSizeIsServedByTheSmallestThumbnailThatCoversIt(int? requested, int? expected)
    {
        Assert.Equal(expected, ArtworkThumbnails.Snap(requested));
    }

    [Fact]
    public void AThumbnailKeepsTheShapeOfTheOriginal()
    {
        var thumbnail = ArtworkThumbnails.Create(Picture(1000, 500), 320);

        Assert.NotNull(thumbnail);
        using var decoded = SKBitmap.Decode(thumbnail);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(160, decoded.Height);
    }

    [Fact]
    public void AnImageAlreadySmallEnoughOrUnreadableIsLeftToTheOriginal()
    {
        Assert.Null(ArtworkThumbnails.Create(Picture(200, 200), 320));
        Assert.Null(ArtworkThumbnails.Create([1, 2, 3, 4], 320));
    }

    [Fact]
    public async Task AThumbnailIsMadeOnceKeptBesideTheOriginalsAndCacheableForGood()
    {
        var root = Path.Combine(_scratch, "artwork");
        Directory.CreateDirectory(Path.Combine(root, "ab"));
        var storagePath = Path.Combine("ab", "abcdef.png");
        await File.WriteAllBytesAsync(Path.Combine(root, storagePath), Picture(1200, 1200));
        var asset = new ArtworkAsset { StoragePath = storagePath, MimeType = "image/png", Hash = "abcdef" };
        _db.ArtworkAssets.Add(asset);
        await _db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SonaFly:ArtworkRoot"] = root })
            .Build();
        var service = new ArtworkService(_db, config, NullLogger<ArtworkService>.Instance,
            new OnlineArtworkService(new HttpClient(), NullLogger<OnlineArtworkService>.Instance));
        var controller = new ArtworkController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        var result = Assert.IsType<FileStreamResult>(await controller.GetArtwork(asset.Id, 300, CancellationToken.None));
        await result.FileStream.DisposeAsync();

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.True(File.Exists(Path.Combine(root, "thumbs", "320", "abcdef.jpg")));
        Assert.Contains("immutable", controller.Response.Headers.CacheControl.ToString());
        Assert.NotNull(result.EntityTag);

        // No size: the original, untouched.
        var original = Assert.IsType<FileStreamResult>(await controller.GetArtwork(asset.Id, null, CancellationToken.None));
        await original.FileStream.DisposeAsync();
        Assert.Equal("image/png", original.ContentType);
        Assert.NotEqual(result.EntityTag, original.EntityTag);

        // Every thumbnail an original can have is known from its name, so a purge can remove them.
        Assert.Contains(Path.Combine("thumbs", "320", "abcdef.jpg"), ArtworkThumbnails.AllRelativePaths(storagePath));
    }

    // ── Log files ──

    [Fact]
    public async Task TheLogIsWrittenToADailyFileAndOldDaysAreRemoved()
    {
        var directory = Path.Combine(_scratch, "logs");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "sonafly-20261001.log"), "ancient");
        await File.WriteAllTextAsync(Path.Combine(directory, "sonafly-20261117.log"), "yesterday");
        await File.WriteAllTextAsync(Path.Combine(directory, "unrelated.txt"), "not ours");

        var provider = new FileLoggerProvider(directory, retainDays: 14, utcNow: () => Now);
        var logger = provider.CreateLogger("SonaFly.Test");
        logger.LogWarning("Scan of {Root} failed", "Music");
        logger.LogError(new InvalidOperationException("disk full"), "Could not save");
        provider.Dispose();

        var text = await File.ReadAllTextAsync(Path.Combine(directory, "sonafly-20261118.log"));
        Assert.Contains("[WRN] SonaFly.Test: Scan of Music failed", text);
        Assert.Contains("[ERR] SonaFly.Test: Could not save", text);
        Assert.Contains("disk full", text);

        Assert.False(File.Exists(Path.Combine(directory, "sonafly-20261001.log")));
        Assert.True(File.Exists(Path.Combine(directory, "sonafly-20261117.log")));
        Assert.True(File.Exists(Path.Combine(directory, "unrelated.txt")));
    }
}
