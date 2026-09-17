using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Identification;
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
/// The identification runner: job selection, scan coordination, resumable
/// stages, provider clients, caching, retries, and recording scoring.
/// </summary>
public sealed class IdentificationRunnerTests : IDisposable
{
    private const string ApiKey = "test-acoustid-key";
    private const string RecordingA = "11111111-1111-1111-1111-111111111111";
    private const string RecordingB = "22222222-2222-2222-2222-222222222222";

    private readonly string _scratch = Directory.CreateTempSubdirectory("sonafly-runner-tests").FullName;
    private readonly string _music;
    private readonly string _connectionString;
    private readonly FakeFingerprintTool _fingerprints = new();
    private readonly FakeAcoustIdClient _acoustId = new();
    private readonly FakeMusicBrainzClient _musicBrainz = new();
    private readonly LibraryMaintenanceGate _gate = new();
    private IdentificationOptions _options = new() { Enabled = true, FpcalcPath = "fpcalc", MaxLocalWorkers = 2 };
    private string? _key = ApiKey;
    private ServiceProvider? _services;

    public IdentificationRunnerTests()
    {
        _music = Directory.CreateDirectory(Path.Combine(_scratch, "music")).FullName;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_scratch, "runner.db"),
            Pooling = false
        }.ToString();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services?.Dispose();
        try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ }
    }

    // ---------------------------------------------------------------- end to end

    [Fact]
    public async Task AJobResolvesAFileAndStoresExplainedCandidates()
    {
        var (root, track) = await AddTrackAsync("song.mp3");
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Real Title", "Real Artist", 200.0)])));

        var jobId = await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        await using var db = NewContext();
        var job = await db.IdentificationJobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal(IdentificationJobStatus.Completed, job.Status);
        Assert.Equal(1, job.HashedCount);
        Assert.Equal(1, job.FingerprintedCount);
        Assert.Equal(1, job.LookedUpCount);
        Assert.Equal(1, job.ResolvedCount);

        var item = await db.IdentificationWorkItems.SingleAsync();
        Assert.Equal(FileAnalysisStatus.Resolved, item.Status);
        Assert.Null(item.LeaseOwner);

        var candidate = await db.RecordingCandidates.SingleAsync();
        Assert.Equal(RecordingA, candidate.MusicBrainzRecordingId);
        Assert.Equal("High", candidate.ConfidenceBand);
        Assert.Equal(RecordingScorer.Version, candidate.ScoringVersion);
        // The file's own (wrong) tags are recorded as a conflict, not trusted.
        Assert.Contains("tag title differs", candidate.Conflicts);
        using var breakdown = JsonDocument.Parse(candidate.ScoringBreakdownJson!);
        Assert.True(breakdown.RootElement.TryGetProperty("provider", out _));

        var snapshot = await db.OriginalTagSnapshots.SingleAsync();
        Assert.Equal("Wrong Title", snapshot.Title);
        Assert.Equal(track.Id, (await db.TrackFileRevisions.SingleAsync()).TrackId);
    }

    [Fact]
    public async Task ReanalysingUnchangedFilesReusesFingerprintsAndCachedLookups()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Real Title", "Real Artist", 200.0)])));

        await CreateJobAsync(root.Id);
        await RunToIdleAsync();
        await CreateJobAsync(root.Id, IdentificationSelectionModes.Root);
        await RunToIdleAsync();

        Assert.Equal(1, _fingerprints.Calls);
        Assert.Equal(1, _acoustId.Calls);
        await using var db = NewContext();
        Assert.Equal(2, await db.IdentificationWorkItems.CountAsync(w => w.Status == FileAnalysisStatus.Resolved));
        Assert.Equal(1, await db.TrackFileRevisions.CountAsync());
    }

    [Fact]
    public async Task ByteIdenticalCopiesShareOneFingerprintRun()
    {
        var root = await AddRootAsync();
        var bytes = RandomBytes();
        await AddTrackAsync("copy-one.mp3", root, bytes);
        await AddTrackAsync("copy-two.mp3", root, bytes);
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));

        _options.MaxLocalWorkers = 1;
        await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        Assert.Equal(1, _fingerprints.Calls);
        Assert.Equal(1, _acoustId.Calls);
        await using var db = NewContext();
        Assert.Equal(2, await db.AcousticFingerprints.CountAsync());
    }

    [Fact]
    public async Task WithoutAKeyAnalysisStopsAtTheFingerprintAndALaterKeyedJobContinues()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        _key = null;

        var firstJob = await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        await using (var db = NewContext())
        {
            Assert.Equal(IdentificationJobStatus.Completed, (await db.IdentificationJobs.SingleAsync(j => j.Id == firstJob)).Status);
            Assert.Equal(FileAnalysisStatus.Fingerprinted, (await db.IdentificationWorkItems.SingleAsync()).Status);
            Assert.Equal(0, _acoustId.Calls);
        }

        // Adding the key makes the same file "not yet analysed" again.
        _key = ApiKey;
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));
        var secondJob = await CreateJobAsync(root.Id);
        Assert.NotNull(secondJob);
        await RunToIdleAsync();

        Assert.Equal(1, _fingerprints.Calls);
        await using var after = NewContext();
        Assert.Equal(FileAnalysisStatus.Resolved,
            (await after.IdentificationWorkItems.SingleAsync(w => w.JobId == secondJob)).Status);
    }

    [Fact]
    public async Task WithoutAFingerprintToolOnlyLocalEvidenceIsCollected()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        _options = new IdentificationOptions { Enabled = true };

        await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        await using var db = NewContext();
        Assert.Equal(FileAnalysisStatus.LocalEvidenceReady, (await db.IdentificationWorkItems.SingleAsync()).Status);
        Assert.Equal(IdentificationJobStatus.Completed, (await db.IdentificationJobs.SingleAsync()).Status);
        Assert.Equal(0, _fingerprints.Calls);
    }

    // ---------------------------------------------------------------- selection and scans

    [Fact]
    public async Task NotYetAnalysedSkipsFinishedFilesButIncludesChangedAndNewOnes()
    {
        var root = await AddRootAsync();
        var (_, unchanged) = await AddTrackAsync("unchanged.mp3", root);
        var (_, changed) = await AddTrackAsync("changed.mp3", root);
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));
        await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        // A rescan after the file was retagged records a new size and time.
        await File.WriteAllBytesAsync(changed.FilePath, RandomBytes());
        await using (var db = NewContext())
        {
            var info = new FileInfo(changed.FilePath);
            await db.Tracks.Where(t => t.Id == changed.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.FileSizeBytes, info.Length)
                .SetProperty(t => t.ModifiedUtcSource, info.LastWriteTimeUtc));
        }

        var (_, added) = await AddTrackAsync("added.mp3", root);

        var jobId = await CreateJobAsync(root.Id);

        await using var after = NewContext();
        var selected = await after.IdentificationWorkItems.Where(w => w.JobId == jobId).Select(w => w.TrackId).ToListAsync();
        Assert.Equal(new[] { changed.Id, added.Id }.Order(), selected.Order());
        Assert.DoesNotContain(unchanged.Id, selected);
    }

    [Fact]
    public async Task NotYetAnalysedReportsNothingToDoWhenEverythingIsAnalysed()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));
        await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        await using var db = NewContext();
        var result = await Controller(db).CreateJob(new CreateIdentificationJobRequest(root.Id, null, null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("NothingToDo", JsonSerializer.Serialize(ok.Value));
        Assert.Equal(1, await db.IdentificationJobs.CountAsync());
    }

    [Theory]
    [InlineData(ScanStatus.Queued)]
    [InlineData(ScanStatus.Running)]
    public async Task AJobCannotBeQueuedWhileItsFolderIsBeingScanned(ScanStatus status)
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        await AddScanAsync(root.Id, status);

        await using var db = NewContext();
        var result = await Controller(db).CreateJob(new CreateIdentificationJobRequest(root.Id, null, null), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(0, await db.IdentificationJobs.CountAsync());
    }

    [Fact]
    public async Task TheRunnerWaitsWhileAScanOfTheFolderStartsMidJob()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        await CreateJobAsync(root.Id);
        var scanId = await AddScanAsync(root.Id, ScanStatus.Running);

        Assert.False(await Runner().RunOnceAsync(CancellationToken.None));

        await using (var db = NewContext())
        {
            Assert.Equal(FileAnalysisStatus.Pending, (await db.IdentificationWorkItems.SingleAsync()).Status);
            Assert.Equal(IdentificationScanGuard.WaitingStage, (await db.IdentificationJobs.SingleAsync()).Stage);
            await db.ScanJobs.Where(s => s.Id == scanId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ScanStatus.Completed));
        }

        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));
        await RunToIdleAsync();
        await using var after = NewContext();
        Assert.Equal(FileAnalysisStatus.Resolved, (await after.IdentificationWorkItems.SingleAsync()).Status);
    }

    [Fact]
    public async Task TracksRemovedAfterQueueingAreSkippedNotFailed()
    {
        var root = await AddRootAsync();
        var (_, removed) = await AddTrackAsync("removed.mp3", root);
        var (_, deletedFile) = await AddTrackAsync("deleted-file.mp3", root);
        await CreateJobAsync(root.Id);

        await using (var db = NewContext())
        {
            await db.Tracks.Where(t => t.Id == removed.Id).ExecuteDeleteAsync();
        }
        File.Delete(deletedFile.FilePath);

        await RunToIdleAsync();

        await using var after = NewContext();
        Assert.All(await after.IdentificationWorkItems.ToListAsync(), w => Assert.Equal(FileAnalysisStatus.Skipped, w.Status));
        var job = await after.IdentificationJobs.SingleAsync();
        Assert.Equal(IdentificationJobStatus.Completed, job.Status);
        Assert.Contains("2 skipped", job.Stage);
        Assert.Equal(0, _fingerprints.Calls);
    }

    [Fact]
    public async Task NoWorkStartsWhileLibraryMaintenanceIsPending()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        await CreateJobAsync(root.Id);

        using (await _gate.AcquireForMaintenanceAsync(CancellationToken.None))
        {
            Assert.False(await Runner().RunOnceAsync(CancellationToken.None));
        }

        await using var db = NewContext();
        Assert.Equal(FileAnalysisStatus.Pending, (await db.IdentificationWorkItems.SingleAsync()).Status);
        Assert.Equal(0, _fingerprints.Calls);
    }

    [Fact]
    public async Task LeasesLeftByACrashedProcessAreReclaimed()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        await CreateJobAsync(root.Id);
        await using (var db = NewContext())
        {
            await db.IdentificationWorkItems.ExecuteUpdateAsync(s => s
                .SetProperty(w => w.LeaseOwner, "dead-process")
                .SetProperty(w => w.LeaseExpiryUtc, DateTime.UtcNow.AddHours(1)));
        }

        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));
        await Runner().ReclaimLeasesAsync(CancellationToken.None);
        await RunToIdleAsync();

        await using var after = NewContext();
        Assert.Equal(FileAnalysisStatus.Resolved, (await after.IdentificationWorkItems.SingleAsync()).Status);
    }

    // ---------------------------------------------------------------- failures

    [Fact]
    public async Task ANetworkFailureSchedulesARetryAndTheJobWaitsForIt()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        _acoustId.Fail(new IdentificationStepException(IdentificationErrorCategory.AcoustIdLookup, retryable: true, "AcoustID could not be reached."));

        var jobId = await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        await using (var db = NewContext())
        {
            var item = await db.IdentificationWorkItems.SingleAsync();
            Assert.Equal(FileAnalysisStatus.RetryableError, item.Status);
            Assert.NotNull(item.NextRetryUtc);
            var job = await db.IdentificationJobs.SingleAsync(j => j.Id == jobId);
            Assert.Equal(IdentificationJobStatus.WaitingForNetwork, job.Status);
            Assert.NotNull(job.NextRetryUtc);
            Assert.Equal(1, await db.IdentificationErrors.CountAsync());

            // Time passes.
            await db.IdentificationWorkItems.ExecuteUpdateAsync(s => s.SetProperty(w => w.NextRetryUtc, DateTime.UtcNow.AddMinutes(-1)));
            await db.IdentificationJobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.NextRetryUtc, DateTime.UtcNow.AddMinutes(-1)));
        }

        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));
        await RunToIdleAsync();

        await using var after = NewContext();
        Assert.Equal(FileAnalysisStatus.Resolved, (await after.IdentificationWorkItems.SingleAsync()).Status);
        Assert.Equal(IdentificationJobStatus.Completed, (await after.IdentificationJobs.SingleAsync()).Status);
        Assert.Equal(1, _fingerprints.Calls);
    }

    [Fact]
    public async Task ARejectedKeyPausesTheJobUntilAnAdministratorFixesIt()
    {
        var (root, _) = await AddTrackAsync("song.mp3");
        _acoustId.Fail(new IdentificationStepException(IdentificationErrorCategory.Configuration, retryable: false,
            "AcoustID rejected the client key.", requiresAdministrator: true));

        var jobId = await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        await using var db = NewContext();
        var job = await db.IdentificationJobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal(IdentificationJobStatus.Paused, job.Status);
        Assert.Contains("rejected", job.ErrorSummary);
        var item = await db.IdentificationWorkItems.SingleAsync();
        Assert.Equal(FileAnalysisStatus.Fingerprinted, item.Status);

        // Resuming after the fix finishes the job without refingerprinting.
        await Controller(db).ResumeJob(jobId!.Value, CancellationToken.None);
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));
        await RunToIdleAsync();

        await using var after = NewContext();
        Assert.Equal(IdentificationJobStatus.Completed, (await after.IdentificationJobs.SingleAsync()).Status);
        Assert.Equal(1, _fingerprints.Calls);
    }

    [Fact]
    public async Task ACorruptFileFailsAloneAndTheRestOfTheJobContinues()
    {
        var root = await AddRootAsync();
        var (_, corrupt) = await AddTrackAsync("corrupt.mp3", root);
        await AddTrackAsync("fine.mp3", root);
        _fingerprints.FailFor(corrupt.FilePath, new IdentificationStepException(IdentificationErrorCategory.Fingerprint, retryable: false, "fpcalc could not decode this file."));
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Song", "Artist", 200.0)])));

        await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        await using var db = NewContext();
        var items = await db.IdentificationWorkItems.ToListAsync();
        Assert.Equal(FileAnalysisStatus.PermanentError, items.Single(w => w.TrackId == corrupt.Id).Status);
        Assert.Equal(FileAnalysisStatus.Resolved, items.Single(w => w.TrackId != corrupt.Id).Status);
        var job = await db.IdentificationJobs.SingleAsync();
        Assert.Equal(IdentificationJobStatus.CompletedWithErrors, job.Status);
        Assert.Equal(1, job.ErrorCount);
    }

    [Fact]
    public async Task AnEmbeddedRecordingIdBecomesACandidateToCompareNotAnAnswer()
    {
        var (root, _) = await AddTrackAsync("song.mp3", embeddedRecordingId: RecordingB);
        _acoustId.Respond(AcoustIdJson(("acoustid-1", 0.97, [(RecordingA, "Real Title", "Real Artist", 200.0)])));
        _musicBrainz.Recordings[RecordingB] = MusicBrainzJson(RecordingB, "Tagged Title", "Tagged Artist", 200_000);

        await CreateJobAsync(root.Id);
        await RunToIdleAsync();

        await using var db = NewContext();
        var candidates = await db.RecordingCandidates.ToListAsync();
        Assert.Equal(2, candidates.Count);
        var embedded = candidates.Single(c => c.MusicBrainzRecordingId == RecordingB);
        Assert.Contains("Embedded", embedded.Provenance);
        var acoustic = candidates.Single(c => c.MusicBrainzRecordingId == RecordingA);
        Assert.Contains("embedded MusicBrainz recording ID points elsewhere", acoustic.Conflicts);
        Assert.True(acoustic.FinalScore > embedded.FinalScore);
    }

    // ---------------------------------------------------------------- scorer

    [Fact]
    public void AStrongAcousticMatchResolvesEvenWhenTheTagsAreWrong()
    {
        var decision = RecordingScorer.Decide(
            new FileEvidence("Track 01", "Unknown Artist", null, 200),
            [Evidence(RecordingA, "Real Song", "Real Artist", 0.92, "a1")],
            0.85, 0.6);

        Assert.Equal(FileAnalysisStatus.Resolved, decision.Outcome);
    }

    [Fact]
    public void TwoDifferentSongsWithCloseScoresAreAmbiguous()
    {
        var decision = RecordingScorer.Decide(
            new FileEvidence(null, null, null, 200),
            [Evidence(RecordingA, "Song One", "Artist", 0.95, "a1"), Evidence(RecordingB, "Song Two", "Artist", 0.93, "a2")],
            0.85, 0.6);

        Assert.Equal(FileAnalysisStatus.Ambiguous, decision.Outcome);
        Assert.NotNull(decision.WinningMargin);
    }

    [Fact]
    public void DuplicateMusicBrainzEntriesForOneSongStillResolve()
    {
        var decision = RecordingScorer.Decide(
            new FileEvidence(null, null, null, 200),
            [Evidence(RecordingA, "Same Song", "Artist", 0.95, "a1"), Evidence(RecordingB, "Same Song", "Artist", 0.95, "a1")],
            0.85, 0.6);

        Assert.Equal(FileAnalysisStatus.Resolved, decision.Outcome);
    }

    [Fact]
    public void ADurationContradictionPreventsAutomaticResolution()
    {
        var decision = RecordingScorer.Decide(
            new FileEvidence(null, null, null, 200),
            [Evidence(RecordingA, "Song", "Artist", 1.0, "a1", duration: 400)],
            0.85, 0.6);

        Assert.NotEqual(FileAnalysisStatus.Resolved, decision.Outcome);
        Assert.Contains("duration", decision.Candidates[0].Conflicts);
    }

    [Fact]
    public void AMatchWithoutARecordingIdIsNeverADecision()
    {
        var none = RecordingScorer.Decide(new FileEvidence(null, null, null, 200), [], 0.85, 0.6);
        var textOnly = RecordingScorer.Decide(
            new FileEvidence(null, null, null, 200),
            [new RecordingEvidence("AcoustID", "a1", null, null, [], null, 0.99, "AcoustID")],
            0.85, 0.6);

        Assert.Equal(FileAnalysisStatus.Unidentified, none.Outcome);
        Assert.Equal(FileAnalysisStatus.Unidentified, textOnly.Outcome);
        Assert.Single(textOnly.Candidates);
    }

    // ---------------------------------------------------------------- clients and tools

    [Fact]
    public void AcoustIdResponsesMapToCategorizedFailures()
    {
        var invalidKey = Assert.Throws<IdentificationStepException>(() => AcoustIdClient.Validate(
            HttpStatusCode.BadRequest, null, """{"status":"error","error":{"code":4,"message":"invalid API key"}}"""));
        Assert.True(invalidKey.RequiresAdministrator);
        Assert.DoesNotContain("invalid API key", invalidKey.Message);

        var limited = Assert.Throws<IdentificationStepException>(() => AcoustIdClient.Validate(
            HttpStatusCode.ServiceUnavailable, TimeSpan.FromSeconds(9), """{"status":"error","error":{"code":14}}"""));
        Assert.True(limited.Retryable);
        Assert.Equal(TimeSpan.FromSeconds(9), limited.RetryAfter);

        var serverError = Assert.Throws<IdentificationStepException>(() => AcoustIdClient.Validate(HttpStatusCode.BadGateway, null, "<html>"));
        Assert.True(serverError.Retryable);

        var malformed = Assert.Throws<IdentificationStepException>(() => AcoustIdClient.Validate(HttpStatusCode.OK, null, "not json"));
        Assert.False(malformed.Retryable);
    }

    [Fact]
    public void AcoustIdParsingKeepsEveryResultAndRecording()
    {
        var json = AcoustIdJson(
            ("a1", 0.9, [(RecordingA, "One", "Artist", 180.0), (RecordingB, null, null, null)]),
            ("a2", 0.4, []));

        var results = AcoustIdClient.Parse(AcoustIdClient.Validate(HttpStatusCode.OK, null, json));

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results[0].Recordings.Count);
        Assert.Equal(["Artist"], results[0].Recordings[0].Artists);
        Assert.Null(results[0].Recordings[1].Title);
        Assert.Empty(results[1].Recordings);
    }

    [Fact]
    public async Task TheAcoustIdKeyIsSentInTheBodyNotTheUrl()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, AcoustIdJson());
        var client = new AcoustIdClient(new HttpClient(handler), new AcoustIdThrottle(new ProviderRateLimiter(TimeSpan.Zero)));

        await client.LookupJsonAsync("secret-key", "FINGERPRINT", 200, CancellationToken.None);

        Assert.DoesNotContain("secret-key", handler.LastUri!.ToString());
        Assert.Contains("client=secret-key", handler.LastBody);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
    }

    [Fact]
    public async Task MusicBrainzTreatsAMissingRecordingAsNotFoundAndSendsTheUserAgent()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, """{"error":"Not Found"}""");
        var client = new MusicBrainzClient(new HttpClient(handler), new MusicBrainzThrottle(new ProviderRateLimiter(TimeSpan.Zero)),
            Options.Create(new IdentificationOptions { MusicBrainzUserAgent = "SonaFly/2.0 ( admin@example.com )" }));

        Assert.Null(await client.GetRecordingJsonAsync(RecordingA, CancellationToken.None));
        Assert.Contains("SonaFly/2.0", handler.LastUserAgent);
        Assert.StartsWith("https://musicbrainz.org/ws/2/recording/", handler.LastUri!.ToString());

        var parsed = MusicBrainzClient.Parse(MusicBrainzJson(RecordingA, "Title", "Artist", 215_000));
        Assert.Equal(215, parsed!.DurationSeconds);
        Assert.Equal(["Artist"], parsed.Artists);
    }

    [Fact]
    public void FpcalcOutputIsParsedStrictly()
    {
        Assert.True(FpcalcFingerprintTool.TryParse("""{"duration": 212.4, "fingerprint": "AQADtE"}""", out var fp, out var duration));
        Assert.Equal("AQADtE", fp);
        Assert.Equal(212.4, duration);

        Assert.False(FpcalcFingerprintTool.TryParse("ERROR: could not open file", out _, out _));
        Assert.False(FpcalcFingerprintTool.TryParse("""{"duration": 0, "fingerprint": "AQADtE"}""", out _, out _));
        Assert.False(FpcalcFingerprintTool.TryParse("""{"duration": 12}""", out _, out _));
    }

    [Fact]
    public async Task AMissingFpcalcExecutableIsAConfigurationError()
    {
        var tool = new FpcalcFingerprintTool(
            Options.Create(new IdentificationOptions { FpcalcPath = Path.Combine(_scratch, "no-such-fpcalc") }),
            NullLogger<FpcalcFingerprintTool>.Instance);

        var failure = await Assert.ThrowsAsync<IdentificationStepException>(() =>
            tool.ComputeAsync(Path.Combine(_scratch, "song with spaces.mp3"), CancellationToken.None));

        Assert.Equal(IdentificationErrorCategory.Configuration, failure.Category);
    }

    // ---------------------------------------------------------------- helpers

    private SonaFlyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SonaFlyDbContext>().UseSqlite(_connectionString).Options);

    private ServiceProvider Services()
    {
        _services?.Dispose();
        var settings = new Mock<IServerSettingsService>();
        settings.Setup(s => s.GetEffectiveAcoustIdKeyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _key);

        _services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connectionString))
            .AddSingleton(Options.Create(_options))
            .AddSingleton(_gate)
            .AddSingleton(settings.Object)
            .AddSingleton<IFileHashService, FileHashService>()
            .AddSingleton<ITagEvidenceReader>(new FakeTagReader(_embeddedIds))
            .AddSingleton<IFingerprintTool>(_fingerprints)
            .AddSingleton<IAcoustIdClient>(_acoustId)
            .AddSingleton<IMusicBrainzClient>(_musicBrainz)
            .AddScoped<IdentificationItemProcessor>()
            .AddSingleton<IdentificationJobRunner>()
            .BuildServiceProvider();
        return _services;
    }

    private readonly Dictionary<string, string> _embeddedIds = new(StringComparer.OrdinalIgnoreCase);

    private IdentificationJobRunner Runner() => Services().GetRequiredService<IdentificationJobRunner>();

    private async Task RunToIdleAsync()
    {
        var runner = Runner();
        for (var i = 0; i < 50; i++)
        {
            if (await runner.RunOnceAsync(CancellationToken.None) == false)
            {
                return;
            }
        }

        Assert.Fail("The runner did not settle.");
    }

    private IdentificationController Controller(SonaFlyDbContext db)
    {
        var settings = new Mock<IServerSettingsService>();
        settings.Setup(s => s.GetEffectiveAcoustIdKeyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _key);
        return new IdentificationController(db, Options.Create(_options), settings.Object, NullLogger<IdentificationController>.Instance);
    }

    private async Task<Guid?> CreateJobAsync(Guid rootId, string? mode = null)
    {
        await using var db = NewContext();
        var result = await Controller(db).CreateJob(new CreateIdentificationJobRequest(rootId, null, null, mode), CancellationToken.None);
        var value = result switch
        {
            AcceptedResult accepted => accepted.Value,
            OkObjectResult ok => ok.Value,
            _ => throw new InvalidOperationException($"Unexpected result {result.GetType().Name}.")
        };
        return (Guid?)value!.GetType().GetProperty("jobId")!.GetValue(value);
    }

    private async Task<LibraryRoot> AddRootAsync()
    {
        await using var db = NewContext();
        var root = new LibraryRoot { Name = "Music", Path = _music };
        db.LibraryRoots.Add(root);
        await db.SaveChangesAsync();
        return root;
    }

    private async Task<(LibraryRoot Root, Track Track)> AddTrackAsync(
        string fileName, LibraryRoot? root = null, byte[]? bytes = null, string? embeddedRecordingId = null)
    {
        root ??= await AddRootAsync();
        var path = Path.Combine(_music, fileName);
        await File.WriteAllBytesAsync(path, bytes ?? RandomBytes());
        if (embeddedRecordingId != null)
        {
            _embeddedIds[path] = embeddedRecordingId;
        }

        var info = new FileInfo(path);
        var track = new Track
        {
            LibraryRootId = root.Id,
            FilePath = path,
            FileName = fileName,
            FileExtension = ".mp3",
            FileSizeBytes = info.Length,
            ModifiedUtcSource = info.LastWriteTimeUtc,
            Title = "Wrong Title",
            MimeType = "audio/mpeg",
            IsIndexed = true
        };
        await using var db = NewContext();
        db.Tracks.Add(track);
        await db.SaveChangesAsync();
        return (root, track);
    }

    private async Task<Guid> AddScanAsync(Guid rootId, ScanStatus status)
    {
        await using var db = NewContext();
        var scan = new ScanJob { LibraryRootId = rootId, Status = status };
        db.ScanJobs.Add(scan);
        await db.SaveChangesAsync();
        return scan.Id;
    }

    private static byte[] RandomBytes()
    {
        var bytes = new byte[2048];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private static RecordingEvidence Evidence(string mbid, string title, string artist, double score, string acoustId, double duration = 200) =>
        new("AcoustID", acoustId, mbid, title, [artist], duration, score, "AcoustID");

    private static string AcoustIdJson(params (string Id, double Score, (string Mbid, string? Title, string? Artist, double? Duration)[] Recordings)[] results) =>
        JsonSerializer.Serialize(new
        {
            status = "ok",
            results = results.Select(r => new
            {
                id = r.Id,
                score = r.Score,
                recordings = r.Recordings.Select(x => new Dictionary<string, object?>
                {
                    ["id"] = x.Mbid,
                    ["title"] = x.Title,
                    ["duration"] = x.Duration,
                    ["artists"] = x.Artist == null ? null : new[] { new { id = "artist", name = x.Artist } }
                })
            })
        });

    private static string MusicBrainzJson(string id, string title, string artist, int lengthMs) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id,
            ["title"] = title,
            ["length"] = lengthMs,
            ["artist-credit"] = new[] { new { name = artist, joinphrase = "" } },
            ["isrcs"] = Array.Empty<string>()
        });

    private sealed class FakeFingerprintTool : IFingerprintTool
    {
        private readonly Dictionary<string, IdentificationStepException> _failures = new(StringComparer.OrdinalIgnoreCase);
        private int _calls;

        public int Calls => _calls;

        public void FailFor(string path, IdentificationStepException failure) => _failures[path] = failure;

        public async Task<FingerprintResult> ComputeAsync(string filePath, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            if (_failures.TryGetValue(filePath, out var failure))
            {
                throw failure;
            }

            // Same bytes give the same fingerprint, as with the real tool.
            var digest = FileHashService.DigestText(Convert.ToBase64String(await File.ReadAllBytesAsync(filePath, ct)));
            return new FingerprintResult("FP" + digest, 200.2, "fpcalc version 1.5.1");
        }
    }

    private sealed class FakeAcoustIdClient : IAcoustIdClient
    {
        private string _json = AcoustIdJson();
        private IdentificationStepException? _failure;
        private int _calls;

        public int Calls => _calls;

        public void Respond(string json)
        {
            _json = json;
            _failure = null;
        }

        public void Fail(IdentificationStepException failure) => _failure = failure;

        public Task<string> LookupJsonAsync(string apiKey, string fingerprint, int durationSeconds, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            Assert.Equal(ApiKey, apiKey);
            return _failure != null ? Task.FromException<string>(_failure) : Task.FromResult(_json);
        }
    }

    private sealed class FakeMusicBrainzClient : IMusicBrainzClient
    {
        public Dictionary<string, string> Recordings { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> GetRecordingJsonAsync(string recordingId, CancellationToken ct) =>
            Task.FromResult(Recordings.TryGetValue(recordingId, out var json) ? json : null);
    }

    private sealed class FakeTagReader(Dictionary<string, string> embeddedIds) : ITagEvidenceReader
    {
        public TagEvidence Read(string filePath) => new(
            "Wrong Title", "Wrong Artist", "Wrong Album", null, null, null, 1, null, null, null, null,
            embeddedIds.TryGetValue(filePath, out var id) ? id : null, null, null);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public string LastUserAgent { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            LastUserAgent = request.Headers.UserAgent.ToString();
            LastBody = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
