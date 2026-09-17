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
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Entities.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identification;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Executable acceptance for review backlog _backlog_upgrade.txt.
/// Each theory maps to a backlog item; none of them passes merely because a
/// matching class name exists.
/// </summary>
public sealed class BacklogResolutionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly string _scratch = Directory.CreateTempSubdirectory("sonafly-backlog-tests").FullName;

    public BacklogResolutionTests()
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

    // ---------------------------------------------------------------- U01

    [Fact]
    public async Task TwoEditionsStayDistinctAcrossAFullRescan()
    {
        var root = new LibraryRoot { Name = "Music", Path = "/music" };
        var artist = new Artist { Name = "Artist", SortName = "Artist" };
        var album = new Album { Title = "Album", SortTitle = "Album", AlbumArtist = artist };
        _db.AddRange(root, artist, album);
        await _db.SaveChangesAsync();

        var trackA = IndexedTrack(root.Id, "/music/a.mp3", album, artist);
        var trackB = IndexedTrack(root.Id, "/music/b.mp3", album, artist);
        _db.AddRange(trackA, trackB);
        await _db.SaveChangesAsync();

        var original = new CatalogRelease
        {
            LibraryRootId = root.Id, AlbumId = album.Id, Title = "Album",
            MusicBrainzReleaseId = "release-original", Country = "US"
        };
        var remaster = new CatalogRelease
        {
            LibraryRootId = root.Id, AlbumId = album.Id, Title = "Album",
            MusicBrainzReleaseId = "release-remaster", Country = "EU"
        };
        _db.AddRange(original, remaster);
        await _db.SaveChangesAsync();

        trackA.CatalogReleaseId = original.Id;
        trackB.CatalogReleaseId = remaster.Id;
        await _db.SaveChangesAsync();

        // A full metadata rescan that retags both files.
        var job = new ScanJob { LibraryRootId = root.Id };
        _db.ScanJobs.Add(job);
        await _db.SaveChangesAsync();

        var scanner = new StubScanner(
            [FileAt("/music/a.mp3", 2000), FileAt("/music/b.mp3", 2000)],
            ["/music"]);
        var service = new LibraryIndexService(
            _db, scanner, new StubMetadataReader(), new NoArtworkService(),
            NullLogger<LibraryIndexService>.Instance);
        await service.ScanLibraryRootAsync(new ScanRequest(root.Id, true, job.Id), CancellationToken.None);

        await _db.Entry(trackA).ReloadAsync();
        await _db.Entry(trackB).ReloadAsync();

        Assert.Equal(original.Id, trackA.CatalogReleaseId);
        Assert.Equal(remaster.Id, trackB.CatalogReleaseId);
        Assert.Equal(2, await _db.CatalogReleases.CountAsync());
        Assert.Equal(album.Id, trackA.AlbumId);
    }

    [Fact]
    public async Task AScanNeverInventsAReleaseLinkForAmbiguousMusic()
    {
        var root = new LibraryRoot { Name = "Music", Path = "/music" };
        _db.LibraryRoots.Add(root);
        var job = new ScanJob { LibraryRootId = root.Id };
        _db.ScanJobs.Add(job);
        await _db.SaveChangesAsync();

        var service = new LibraryIndexService(
            _db, new StubScanner([FileAt("/music/new.mp3")], ["/music"]),
            new StubMetadataReader(), new NoArtworkService(),
            NullLogger<LibraryIndexService>.Instance);
        await service.ScanLibraryRootAsync(new ScanRequest(root.Id, false, job.Id), CancellationToken.None);

        var track = await _db.Tracks.FirstAsync();
        Assert.Null(track.CatalogReleaseId);
        Assert.Equal(0, await _db.CatalogReleases.CountAsync());
    }

    // ---------------------------------------------------------------- U02

    [Fact]
    public async Task AnApplyWaitsForAScanInsteadOfCancellingIt()
    {
        var gate = new LibraryMaintenanceGate();
        using var scan = await gate.AcquireForScanAsync(CancellationToken.None);

        var applyTask = gate.AcquireForApplyAsync(CancellationToken.None);
        await Task.Delay(150);

        // The approval waits; the scan is untouched and keeps running.
        Assert.False(scan.Token.IsCancellationRequested);
        Assert.False(applyTask.IsCompleted);

        scan.Dispose();
        using var apply = await applyTask;
        Assert.False(apply.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task MaintenanceStillCancelsARunningScan()
    {
        var gate = new LibraryMaintenanceGate();
        var scan = await gate.AcquireForScanAsync(CancellationToken.None);

        // Maintenance asks the scan to stop and waits for it to drain; the
        // worker is what releases the slot, so the test plays that part.
        var maintenanceTask = gate.AcquireForMaintenanceAsync(CancellationToken.None);
        await Task.Delay(150);

        Assert.True(scan.Token.IsCancellationRequested);
        Assert.False(maintenanceTask.IsCompleted);

        scan.Dispose();
        using var maintenance = await maintenanceTask;
    }

    [Fact]
    public async Task AnApplyWaitsForMaintenanceInsteadOfInterleaving()
    {
        var gate = new LibraryMaintenanceGate();
        var maintenance = await gate.AcquireForMaintenanceAsync(CancellationToken.None);

        var applyTask = gate.AcquireForApplyAsync(CancellationToken.None);
        await Task.Delay(150);

        // The apply cannot slip in beside a purge or root deletion; it waits.
        Assert.False(applyTask.IsCompleted);

        maintenance.Dispose();
        using var apply = await applyTask;
        Assert.False(apply.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task RootDeletionCancelsJobsAndRemovesAnalysisRows()
    {
        var root = new LibraryRoot { Name = "Music", Path = "/music" };
        _db.LibraryRoots.Add(root);
        await _db.SaveChangesAsync();

        var track = IndexedTrack(root.Id, "/music/song.mp3", null, null);
        _db.Tracks.Add(track);
        var job = new IdentificationJob { LibraryRootId = root.Id, Status = IdentificationJobStatus.Running };
        _db.IdentificationJobs.Add(job);
        await _db.SaveChangesAsync();
        _db.IdentificationWorkItems.Add(new IdentificationWorkItem
        {
            JobId = job.Id, TrackId = track.Id, Status = FileAnalysisStatus.Fingerprinted
        });
        await _db.SaveChangesAsync();

        await new LibraryRootService(_db, new LibraryMaintenanceGate())
            .DeleteAsync(root.Id, CancellationToken.None);

        Assert.Equal(0, await _db.LibraryRoots.CountAsync());
        Assert.Equal(0, await _db.IdentificationJobs.CountAsync());
        Assert.Equal(0, await _db.IdentificationWorkItems.CountAsync());
        Assert.Equal(0, await _db.Tracks.CountAsync());
    }

    // ---------------------------------------------------------------- U03

    [Fact]
    public void AnUnambiguousVerifiedMoveMatches()
    {
        var trackId = Guid.NewGuid();
        var result = MoveMatcher.TryMatch(
            "ABCDEF",
            [new MoveCandidate(trackId, "abcdef", IsMissing: true, OldPathVerifiedAbsent: true)]);

        Assert.True(result.IsMatch);
        Assert.Equal(trackId, result.TrackId);
    }

    [Fact]
    public void SeveralGoneCopiesAreDuplicatesNotAMove()
    {
        var result = MoveMatcher.TryMatch(
            "ABCDEF",
            [
                new MoveCandidate(Guid.NewGuid(), "abcdef", IsMissing: true, OldPathVerifiedAbsent: true),
                new MoveCandidate(Guid.NewGuid(), "ABCDEF", IsMissing: true, OldPathVerifiedAbsent: true)
            ]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void AStillPresentCopyMeansDuplicateNotMove()
    {
        var result = MoveMatcher.TryMatch(
            "ABCDEF",
            [
                new MoveCandidate(Guid.NewGuid(), "abcdef", IsMissing: false, OldPathVerifiedAbsent: false),
                new MoveCandidate(Guid.NewGuid(), "abcdef", IsMissing: true, OldPathVerifiedAbsent: true)
            ]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void AnUnverifiedAbsenceNeverClaimsAMove()
    {
        var result = MoveMatcher.TryMatch(
            "ABCDEF",
            [new MoveCandidate(Guid.NewGuid(), "abcdef", IsMissing: true, OldPathVerifiedAbsent: false)]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void MovesAreNeverInferredWithoutAHash()
    {
        var result = MoveMatcher.TryMatch(
            null,
            [new MoveCandidate(Guid.NewGuid(), "abcdef", IsMissing: true, OldPathVerifiedAbsent: true)]);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void TitleArtistSizeAndFilenameAloneNeverMatch()
    {
        // Same title and artist live only in the catalog row; the matcher sees
        // hashes and absence, so a same-named file with a different hash moves nothing.
        var result = MoveMatcher.TryMatch(
            "DIFFERENT",
            [new MoveCandidate(Guid.NewGuid(), "abcdef", IsMissing: true, OldPathVerifiedAbsent: true)]);

        Assert.False(result.IsMatch);
    }

    // ---------------------------------------------------------------- U04

    [Fact]
    public void AnActiveOverrideWinsOverChangedTags()
    {
        var active = new CatalogOverride
        {
            TrackId = Guid.NewGuid(), Field = "Title", ValueText = "Approved Title",
            SourceFingerprintDigest = "audio-v1"
        };

        var resolved = OverridePrecedence.Resolve("Retagged Title", null, active, "audio-v1");

        Assert.Equal("Approved Title", resolved.Value);
        Assert.False(resolved.StaleForReview);
    }

    [Fact]
    public void ReplacedAudioMarksTheOverrideStaleForReview()
    {
        var active = new CatalogOverride
        {
            TrackId = Guid.NewGuid(), Field = "Artist", ValueText = "Approved Artist",
            SourceFingerprintDigest = "audio-v1"
        };

        var resolved = OverridePrecedence.Resolve("Someone Else", null, active, "audio-v2");

        Assert.Equal("Approved Artist", resolved.Value);
        Assert.True(resolved.StaleForReview);
    }

    [Fact]
    public void ATagOrArtworkEditKeepsTheOverrideWithoutReview()
    {
        var active = new CatalogOverride
        {
            TrackId = Guid.NewGuid(), Field = "Title", ValueText = "Approved Title",
            SourceFingerprintDigest = "audio-v1"
        };

        // Whole-file SHA changed (tag edit) but the audio fingerprint did not.
        var resolved = OverridePrecedence.Resolve("Edited Title", null, active, "audio-v1");

        Assert.Equal("Approved Title", resolved.Value);
        Assert.False(resolved.StaleForReview);
    }

    [Fact]
    public void WithoutAnOverrideTheTagValueStands()
    {
        var resolved = OverridePrecedence.Resolve("Tag Title", null, null, "audio-v1");

        Assert.Equal("Tag Title", resolved.Value);
        Assert.False(resolved.StaleForReview);
    }

    // ---------------------------------------------------------------- U05

    [Fact]
    public async Task PurgeRemovesAnalysisRowsButRetainsTheAuditJournal()
    {
        var music = Path.Combine(_scratch, "music");
        var cache = Path.Combine(_scratch, "artwork");
        Directory.CreateDirectory(music);
        Directory.CreateDirectory(cache);

        var root = new LibraryRoot { Name = "Music", Path = music };
        _db.LibraryRoots.Add(root);
        await _db.SaveChangesAsync();

        var track = IndexedTrack(root.Id, Path.Combine(music, "song.mp3"), null, null);
        _db.Tracks.Add(track);
        await _db.SaveChangesAsync();

        var revision = new TrackFileRevision
        {
            TrackId = track.Id, LibraryRootId = root.Id, NormalizedPath = track.FilePath,
            FileSizeBytes = 1000, ModifiedUtcSource = track.ModifiedUtcSource,
            Sha256 = Convert.ToHexString(new byte[32]).ToLowerInvariant()
        };
        _db.TrackFileRevisions.Add(revision);
        var job = new IdentificationJob { LibraryRootId = root.Id, Status = IdentificationJobStatus.Completed };
        _db.IdentificationJobs.Add(job);
        var journal = new ChangeJournal
        {
            TrackId = track.Id, OldPath = track.FilePath, NewPath = track.FilePath,
            OldSha256 = "old", NewSha256 = "new"
        };
        _db.ChangeJournals.Add(journal);
        var cached = new ProviderCacheEntry
        {
            Provider = "AcoustID", CacheKeyDigest = "digest", ResponseJson = "{}"
        };
        _db.ProviderCacheEntries.Add(cached);
        await _db.SaveChangesAsync();
        _db.IdentificationWorkItems.Add(new IdentificationWorkItem
        {
            JobId = job.Id, TrackId = track.Id, FileRevisionId = revision.Id,
            Status = FileAnalysisStatus.Resolved
        });
        await _db.SaveChangesAsync();

        var result = await PurgeController(cache).PurgeLibraryData(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        Assert.Equal(0, await _db.Tracks.CountAsync());
        Assert.Equal(0, await _db.TrackFileRevisions.CountAsync());
        Assert.Equal(0, await _db.IdentificationJobs.CountAsync());
        Assert.Equal(0, await _db.IdentificationWorkItems.CountAsync());

        // Audit and global cache survive with readable content.
        var kept = await _db.ChangeJournals.FirstAsync();
        Assert.Equal(track.Id, kept.TrackId);
        Assert.Equal("old", kept.OldSha256);
        Assert.Equal(1, await _db.ProviderCacheEntries.CountAsync());

        // The roots themselves survive a purge.
        Assert.Equal(1, await _db.LibraryRoots.CountAsync());
    }

    // ---------------------------------------------------------------- U07

    [Fact]
    public void ARecordingOnlyProposalPasses()
    {
        var proposal = new MetadataProposal
        {
            JobId = Guid.NewGuid(), TrackId = Guid.NewGuid(),
            FieldMask = "Title,Artist",
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };

        Assert.Empty(ProposalValidator.Validate(proposal, exactReleaseApplyEnabled: false));
    }

    [Fact]
    public void EditionFieldsNeedAReleaseDecisionAndTheFullMilestone()
    {
        var draft = new MetadataProposal
        {
            JobId = Guid.NewGuid(), TrackId = Guid.NewGuid(),
            FieldMask = "Title,Album",
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };

        var withoutRelease = ProposalValidator.Validate(draft, exactReleaseApplyEnabled: false);
        Assert.Contains(withoutRelease, e => e.Contains("Album"));

        draft.ReleaseCandidateId = Guid.NewGuid();
        var previewGate = ProposalValidator.Validate(draft, exactReleaseApplyEnabled: false);
        Assert.Contains(previewGate, e => e.Contains("full identification milestone"));

        Assert.Empty(ProposalValidator.Validate(draft, exactReleaseApplyEnabled: true));
    }

    [Fact]
    public void GenreAndFilePathsAreNeverProposalFields()
    {
        var proposal = new MetadataProposal
        {
            JobId = Guid.NewGuid(), TrackId = Guid.NewGuid(),
            FieldMask = "Title,Genre,FilePath",
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };

        var errors = ProposalValidator.Validate(proposal, exactReleaseApplyEnabled: true);
        Assert.Contains(errors, e => e.Contains("Genre"));
        Assert.Contains(errors, e => e.Contains("FilePath"));
    }

    // ---------------------------------------------------------------- helpers

    private static Track IndexedTrack(Guid rootId, string filePath, Album? album, Artist? artist) => new()
    {
        LibraryRootId = rootId,
        FilePath = filePath,
        FileName = Path.GetFileName(filePath),
        FileExtension = ".mp3",
        FileSizeBytes = 1000,
        ModifiedUtcSource = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Title = "Song",
        MimeType = "audio/mpeg",
        Album = album,
        PrimaryArtist = artist,
        IsIndexed = true
    };

    private static DiscoveredAudioFile FileAt(string path, long size = 1000) =>
        new(path, Path.GetFileName(path), ".mp3", size,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private SystemController PurgeController(string artworkRoot)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SonaFly:ArtworkRoot"] = artworkRoot,
                ["SonaFly:DataProtectionKeyRoot"] = Path.Combine(_scratch, "keys")
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
            NullLogger<SystemController>.Instance);
    }

    private sealed class StubScanner(
        IReadOnlyList<DiscoveredAudioFile>? files = null,
        IReadOnlyList<string>? traversedDirectories = null) : IFileScanner
    {
        public async IAsyncEnumerable<DiscoveredAudioFile> EnumerateAudioFilesAsync(
            string rootPath,
            ScanTraversalReport report,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            report.MarkRootAvailable(true);
            foreach (var directory in traversedDirectories ?? [])
                report.MarkDirectoryTraversed(directory);
            foreach (var file in files ?? [])
                yield return file;
        }
    }

    private sealed class StubMetadataReader : IMetadataReader
    {
        public Task<AudioMetadata> ReadAsync(string filePath, CancellationToken ct) =>
            Task.FromResult(new AudioMetadata
            {
                Title = "Retagged",
                Album = "Album",
                Artist = "Artist",
                Genre = "Jazz",
                MimeType = "audio/mpeg"
            });
    }

    private sealed class NoArtworkService : IArtworkService
    {
        public Task<ArtworkResult?> ExtractAndStoreAsync(AudioMetadata metadata, string filePath, CancellationToken ct) =>
            Task.FromResult<ArtworkResult?>(null);

        public Task<FileStreamResultModel?> OpenArtworkAsync(Guid artworkId, CancellationToken ct) =>
            Task.FromResult<FileStreamResultModel?>(null);
    }
}

