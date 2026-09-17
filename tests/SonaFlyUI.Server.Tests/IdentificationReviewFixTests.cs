using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Entities.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identification;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Regressions for the code review of the music identification foundation:
/// root deletion scope, job admission, job state transitions, the duplicate
/// report, options validation, and tag snapshot JSON.
/// </summary>
public sealed class IdentificationReviewFixTests : IDisposable
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("sonafly-review-fix-tests").FullName;
    private readonly string _connectionString;
    private readonly SonaFlyDbContext _db;

    public IdentificationReviewFixTests()
    {
        // A file database so concurrent requests can use separate connections.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_scratch, "test.db"),
            Pooling = false
        }.ToString();
        _db = NewContext();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ }
    }

    // ---------------------------------------------------------------- root deletion scope

    [Fact]
    public async Task DeletingOneRootLeavesAScanOfAnotherRootRunning()
    {
        var gate = new LibraryMaintenanceGate();
        using var scan = await gate.AcquireForScanAsync(Guid.NewGuid(), CancellationToken.None);

        using var deletion = await gate.AcquireForRootDeletionAsync(Guid.NewGuid(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(scan.Token.IsCancellationRequested);
        Assert.False(gate.MaintenancePending);
    }

    [Fact]
    public async Task DeletingARootCancelsAndDrainsThatRootsScan()
    {
        var gate = new LibraryMaintenanceGate();
        var rootId = Guid.NewGuid();
        var scan = await gate.AcquireForScanAsync(rootId, CancellationToken.None);

        var deletionTask = gate.AcquireForRootDeletionAsync(rootId, CancellationToken.None);
        await Task.Delay(150);

        Assert.True(scan.Token.IsCancellationRequested);
        Assert.False(deletionTask.IsCompleted);

        scan.Dispose();
        using var deletion = await deletionTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AScanOfARootBeingDeletedCancelsItself()
    {
        var gate = new LibraryMaintenanceGate();
        var rootId = Guid.NewGuid();
        var deletion = await gate.AcquireForRootDeletionAsync(rootId, CancellationToken.None);

        using (var scan = await gate.AcquireForScanAsync(rootId, CancellationToken.None))
        {
            Assert.True(gate.IsRootPendingDeletion(rootId));
            Assert.True(scan.Token.IsCancellationRequested);
        }

        deletion.Dispose();
        Assert.False(gate.IsRootPendingDeletion(rootId));
        using var later = await gate.AcquireForScanAsync(rootId, CancellationToken.None);
        Assert.False(later.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task DeletingAnUnknownRootDoesNotDisturbARunningScan()
    {
        var gate = new LibraryMaintenanceGate();
        var missingId = Guid.NewGuid();
        using var scan = await gate.AcquireForScanAsync(missingId, CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            new LibraryRootService(_db, gate).DeleteAsync(missingId, CancellationToken.None));

        Assert.False(scan.Token.IsCancellationRequested);
        Assert.False(gate.IsRootPendingDeletion(missingId));
    }

    [Fact]
    public async Task DeletingARootRemovesItsIdentificationErrors()
    {
        var root = await AddRootAsync();
        var otherRoot = await AddRootAsync();
        var job = await AddJobAsync(root.Id, IdentificationJobStatus.Running);
        var otherJob = await AddJobAsync(otherRoot.Id, IdentificationJobStatus.Running);
        var track = await AddTrackAsync(root.Id);
        var item = new IdentificationWorkItem { JobId = job.Id, TrackId = track.Id };
        _db.IdentificationWorkItems.Add(item);
        _db.IdentificationErrors.AddRange(
            new IdentificationError { JobId = job.Id, Message = "job" },
            new IdentificationError { WorkItemId = item.Id, Message = "item" },
            new IdentificationError { JobId = otherJob.Id, Message = "other root" });
        await _db.SaveChangesAsync();

        await new LibraryRootService(_db, new LibraryMaintenanceGate()).DeleteAsync(root.Id, CancellationToken.None);

        var remaining = await _db.IdentificationErrors.AsNoTracking().ToListAsync();
        Assert.Equal("other root", Assert.Single(remaining).Message);
    }

    // ---------------------------------------------------------------- job admission

    [Fact]
    public async Task ConcurrentCreatesForOneRootQueueASingleJob()
    {
        var root = await AddRootAsync();
        await AddTrackAsync(root.Id);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var db = NewContext();
            return await Controller(db).CreateJob(
                new CreateIdentificationJobRequest(root.Id, null, null), CancellationToken.None);
        })));

        Assert.All(results, r => Assert.IsType<AcceptedResult>(r));
        Assert.Equal(1, await _db.IdentificationJobs.CountAsync());
        Assert.Equal(1, await _db.IdentificationWorkItems.CountAsync());
    }

    [Fact]
    public async Task ConcurrentCreatesWithOneIdempotencyKeyReturnTheSameJob()
    {
        var root = await AddRootAsync();
        await AddTrackAsync(root.Id);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var db = NewContext();
            return await Controller(db).CreateJob(
                new CreateIdentificationJobRequest(root.Id, null, "click-1"), CancellationToken.None);
        })));

        var jobIds = results.Select(r => JobIdOf(Assert.IsType<AcceptedResult>(r))).Distinct().ToList();
        Assert.Single(jobIds);
        Assert.Equal(1, await _db.IdentificationJobs.CountAsync());
    }

    // ---------------------------------------------------------------- job transitions

    [Fact]
    public async Task AJobThatCompletedWithErrorsCannotBeCancelled()
    {
        var root = await AddRootAsync();
        var job = await AddJobAsync(root.Id, IdentificationJobStatus.CompletedWithErrors);

        var result = await Controller(_db).CancelJob(job.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        await _db.Entry(job).ReloadAsync();
        Assert.Equal(IdentificationJobStatus.CompletedWithErrors, job.Status);
    }

    [Fact]
    public async Task RetryingErrorsOnACancelledJobIsRefused()
    {
        var root = await AddRootAsync();
        var job = await AddJobAsync(root.Id, IdentificationJobStatus.Cancelled);
        var item = await AddFailedItemAsync(job.Id, root.Id);

        var result = await Controller(_db).RetryErrors(job.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        await _db.Entry(item).ReloadAsync();
        Assert.Equal(FileAnalysisStatus.RetryableError, item.Status);
    }

    [Fact]
    public async Task RetryingErrorsRequeuesTheJobAndAdjustsItsErrorCount()
    {
        var root = await AddRootAsync();
        var job = await AddJobAsync(root.Id, IdentificationJobStatus.CompletedWithErrors, errorCount: 3);
        await AddFailedItemAsync(job.Id, root.Id);
        await AddFailedItemAsync(job.Id, root.Id);

        var result = await Controller(_db).RetryErrors(job.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await _db.Entry(job).ReloadAsync();
        Assert.Equal(IdentificationJobStatus.Queued, job.Status);
        Assert.Equal(1, job.ErrorCount);
        Assert.Null(job.FinishedUtc);
    }

    [Fact]
    public async Task RetryingErrorsWillNotRequeueBesideAnotherActiveJob()
    {
        var root = await AddRootAsync();
        var finished = await AddJobAsync(root.Id, IdentificationJobStatus.CompletedWithErrors);
        await AddFailedItemAsync(finished.Id, root.Id);
        await AddJobAsync(root.Id, IdentificationJobStatus.Running);

        var result = await Controller(_db).RetryErrors(finished.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        await _db.Entry(finished).ReloadAsync();
        Assert.Equal(IdentificationJobStatus.CompletedWithErrors, finished.Status);
    }

    // ---------------------------------------------------------------- duplicate report

    [Fact]
    public async Task SeveralRevisionsOfOneTrackAreNotReportedAsDuplicates()
    {
        var root = await AddRootAsync();
        var track = await AddTrackAsync(root.Id);
        AddRevision(track, "aaa", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AddRevision(track, "aaa", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        var groups = await DuplicatesAsync();

        Assert.Empty(groups);
    }

    [Fact]
    public async Task DuplicatesCompareEachTracksLatestRevision()
    {
        var root = await AddRootAsync();
        var first = await AddTrackAsync(root.Id);
        var second = await AddTrackAsync(root.Id);
        var retagged = await AddTrackAsync(root.Id);
        var jan = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        AddRevision(first, "same", jan);
        AddRevision(second, "same", feb);
        // Matched once, but its current bytes differ, so it is no longer a copy.
        AddRevision(retagged, "same", jan);
        AddRevision(retagged, "edited", feb);
        await _db.SaveChangesAsync();

        var group = Assert.Single(await DuplicatesAsync());

        Assert.Equal(2, group.FileCount);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), group.TrackIds.Order());
    }

    // ---------------------------------------------------------------- options and snapshots

    [Theory]
    [InlineData("MusicBrainzRequestsPerSecond", "10")]
    [InlineData("MaxLocalWorkers", "500")]
    [InlineData("ReviewThreshold", "0.95")]
    public void OutOfRangeIdentificationOptionsFailValidation(string setting, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{IdentificationOptions.SectionName}:{setting}"] = value
            })
            .Build();
        using var services = new ServiceCollection()
            .AddIdentificationOptions(configuration)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            services.GetRequiredService<IOptions<IdentificationOptions>>().Value);
    }

    [Fact]
    public void DefaultIdentificationOptionsPassValidation()
    {
        using var services = new ServiceCollection()
            .AddIdentificationOptions(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        Assert.False(services.GetRequiredService<IOptions<IdentificationOptions>>().Value.Enabled);
    }

    [Fact]
    public void AnOversizedTagSnapshotStaysValidJson()
    {
        var metadata = new AudioMetadata
        {
            Title = new string('t', 20_000),
            Album = "Ålbum " + new string('é', 9_000),
            Artist = "Artist"
        };

        var snapshot = OriginalTagSnapshotBuilder.Build(metadata, Guid.NewGuid(), "test");

        Assert.True(snapshot.RawFieldsJson!.Length <= 8000);
        using var parsed = JsonDocument.Parse(snapshot.RawFieldsJson);
        Assert.Equal("Artist", parsed.RootElement.GetProperty("Artist").GetString());
        Assert.StartsWith("ttt", parsed.RootElement.GetProperty("Title").GetString());
        Assert.Equal(metadata.Title, snapshot.Title);
    }

    // ---------------------------------------------------------------- helpers

    private SonaFlyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SonaFlyDbContext>().UseSqlite(_connectionString).Options);

    private static IdentificationController Controller(SonaFlyDbContext db) => new(
        db,
        Options.Create(new IdentificationOptions { Enabled = true }),
        Mock.Of<IServerSettingsService>(),
        NullLogger<IdentificationController>.Instance);

    private async Task<IReadOnlyList<DuplicateGroupDto>> DuplicatesAsync()
    {
        var result = await Controller(_db).ListDuplicates(null, CancellationToken.None);
        return Assert.IsAssignableFrom<IReadOnlyList<DuplicateGroupDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    private static Guid JobIdOf(AcceptedResult result) =>
        (Guid)result.Value!.GetType().GetProperty("jobId")!.GetValue(result.Value)!;

    private async Task<LibraryRoot> AddRootAsync()
    {
        var root = new LibraryRoot { Name = "Music", Path = "/music/" + Guid.NewGuid() };
        _db.LibraryRoots.Add(root);
        await _db.SaveChangesAsync();
        return root;
    }

    private async Task<Track> AddTrackAsync(Guid rootId)
    {
        var path = $"/music/{Guid.NewGuid()}.mp3";
        var track = new Track
        {
            LibraryRootId = rootId,
            FilePath = path,
            FileName = Path.GetFileName(path),
            FileExtension = ".mp3",
            FileSizeBytes = 1000,
            Title = "Song",
            MimeType = "audio/mpeg",
            IsIndexed = true
        };
        _db.Tracks.Add(track);
        await _db.SaveChangesAsync();
        return track;
    }

    private async Task<IdentificationJob> AddJobAsync(Guid rootId, IdentificationJobStatus status, int errorCount = 0)
    {
        var job = new IdentificationJob { LibraryRootId = rootId, Status = status, ErrorCount = errorCount };
        _db.IdentificationJobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private async Task<IdentificationWorkItem> AddFailedItemAsync(Guid jobId, Guid rootId)
    {
        var track = await AddTrackAsync(rootId);
        var item = new IdentificationWorkItem
        {
            JobId = jobId,
            TrackId = track.Id,
            Status = FileAnalysisStatus.RetryableError,
            Retryable = true
        };
        _db.IdentificationWorkItems.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }

    private void AddRevision(Track track, string sha256, DateTime observedUtc) =>
        _db.TrackFileRevisions.Add(new TrackFileRevision
        {
            TrackId = track.Id,
            LibraryRootId = track.LibraryRootId,
            NormalizedPath = track.FilePath,
            Sha256 = sha256,
            ObservedUtc = observedUtc
        });
}
