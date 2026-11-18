using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
/// Reviewing identification results: seeing which matches disagree with the catalog, and
/// applying, rejecting and undoing them without ever touching an audio file.
/// </summary>
public sealed class IdentificationReviewTests : IDisposable
{
    private static readonly DateTime FileDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly LibraryMaintenanceGate _gate = new();
    private readonly IdentificationReviewService _review;
    private readonly LibraryRoot _root = new() { Name = "Music", Path = "/music" };
    private readonly IdentificationJob _job;
    private readonly Guid _reviewer = Guid.NewGuid();

    public IdentificationReviewTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        _db = _services.GetRequiredService<SonaFlyDbContext>();
        _db.Database.EnsureCreated();
        _job = new IdentificationJob { LibraryRootId = _root.Id, Status = IdentificationJobStatus.Completed };
        _db.LibraryRoots.Add(_root);
        _db.IdentificationJobs.Add(_job);
        _db.SaveChanges();
        _review = new IdentificationReviewService(_db, _gate, Options.Create(new IdentificationOptions()),
            NullLogger<IdentificationReviewService>.Instance);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    /// <summary>A track as the scan left it, with one analysed match for it.</summary>
    private (Track Track, IdentificationWorkItem Item, RecordingCandidate Match) Analysed(
        string title, string? artist, string? matchTitle, string? matchArtist,
        double score = 0.95, string band = "High", string file = "a.mp3")
    {
        var track = new Track
        {
            LibraryRootId = _root.Id, FilePath = "/music/" + file, FileName = file, FileExtension = ".mp3",
            Title = title, MimeType = "audio/mpeg", IsIndexed = true,
            FileSizeBytes = 1000, ModifiedUtcSource = FileDate,
        };
        if (artist != null)
        {
            var existing = _db.Artists.Local.FirstOrDefault(a => a.Name == artist)
                           ?? _db.Artists.FirstOrDefault(a => a.Name == artist)
                           ?? new Artist { Name = artist };
            track.PrimaryArtist = existing;
            _db.TrackArtists.Add(new TrackArtist { Track = track, Artist = existing, Role = TrackArtistRole.Primary });
        }

        var revision = new TrackFileRevision
        {
            Track = track, LibraryRootId = _root.Id, NormalizedPath = track.FilePath, RelativePath = file,
            FileSizeBytes = 1000, ModifiedUtcSource = FileDate,
        };
        var item = new IdentificationWorkItem
        {
            JobId = _job.Id, TrackId = track.Id, FileRevisionId = revision.Id, Status = FileAnalysisStatus.Resolved,
        };
        var match = new RecordingCandidate
        {
            WorkItemId = item.Id, Provider = "AcoustID", Title = matchTitle, Artist = matchArtist,
            FinalScore = score, ConfidenceBand = band, MusicBrainzRecordingId = Guid.NewGuid().ToString(),
        };
        _db.AddRange(track, revision, item, match);
        _db.SaveChanges();
        return (track, item, match);
    }

    private static ReviewDecision Decide((Track Track, IdentificationWorkItem Item, RecordingCandidate Match) analysed,
        bool title = true, bool artist = true) => new(analysed.Item.Id, analysed.Match.Id, title, artist);

    [Fact]
    public async Task OnlyMatchesThatWouldChangeSomethingAreOfferedBestFirst()
    {
        Analysed("Yesterday", "The Beatles", "Yesterday", "The Beatles", file: "same.mp3");
        Analysed("yesterday", "beatles", "Yesterday", "The Beatles", score: 0.7, band: "Review", file: "sloppy.mp3");
        Analysed("Track 01", null, "Help!", "The Beatles", score: 0.97, file: "untagged.mp3");
        Analysed("Unknown", "Nobody", null, null, score: 0.9, file: "empty-match.mp3");

        var page = await _review.PendingAsync(_root.Id, null, 0, 50, CancellationToken.None);

        Assert.Equal(2, page.Total);
        Assert.Equal(1, page.HighConfidence);
        Assert.Equal(["Help!", "Yesterday"], page.Items.Select(i => i.ProposedTitle));
        var sloppy = page.Items[1];
        Assert.True(sloppy.TitleChanges);
        Assert.True(sloppy.ArtistChanges);
        Assert.Equal("sloppy.mp3", sloppy.RelativePath);

        var highOnly = await _review.PendingAsync(_root.Id, "High", 0, 50, CancellationToken.None);
        Assert.Equal("Help!", Assert.Single(highOnly.Items).ProposedTitle);
    }

    [Fact]
    public async Task ApplyingAMatchCorrectsTheCatalogAndRecordsWhatChanged()
    {
        var analysed = Analysed("Track 01", "Unknown Artist", "Help!", "The Beatles");

        var outcome = await _review.ApplyAsync([Decide(analysed)], _reviewer, CancellationToken.None);

        Assert.Equal(1, outcome.Done);
        Assert.Empty(outcome.Skipped);
        _db.ChangeTracker.Clear();

        var track = await _db.Tracks.Include(t => t.PrimaryArtist).SingleAsync();
        Assert.Equal("Help!", track.Title);
        Assert.Equal("help!", track.SearchTitle);
        Assert.Equal("The Beatles", track.PrimaryArtist!.Name);
        // Restrictions are enforced through the junction, so it has to move too.
        var link = await _db.TrackArtists.SingleAsync(ta => ta.TrackId == track.Id);
        Assert.Equal(track.PrimaryArtistId, link.ArtistId);

        var proposal = await _db.MetadataProposals.SingleAsync();
        Assert.Equal(ProposalStatus.Applied, proposal.Status);
        Assert.Equal("Track 01", proposal.OldTitle);
        Assert.Equal("Unknown Artist", proposal.OldArtist);
        Assert.Equal(_reviewer, proposal.ReviewerUserId);

        Assert.Equal(["Artist", "Title"], (await _db.CatalogOverrides.Select(o => o.Field).ToListAsync()).Order());
        var journal = await _db.ChangeJournals.SingleAsync();
        Assert.Contains("Track 01", journal.OldTagSnapshotJson);
        Assert.Contains("Help!", journal.NewTagSnapshotJson);

        // Once applied there is nothing left to review for it.
        Assert.Equal(0, (await _review.PendingAsync(null, null, 0, 50, CancellationToken.None)).Total);
        Assert.Equal("Help!", Assert.Single(await _review.AppliedAsync(10, CancellationToken.None)).NewTitle);
    }

    [Fact]
    public async Task OnlyTheChosenFieldIsTaken()
    {
        var analysed = Analysed("Track 01", "The Beatles (wrong)", "Help!", "The Beatles");

        await _review.ApplyAsync([Decide(analysed, artist: false)], _reviewer, CancellationToken.None);
        _db.ChangeTracker.Clear();

        var track = await _db.Tracks.Include(t => t.PrimaryArtist).SingleAsync();
        Assert.Equal("Help!", track.Title);
        Assert.Equal("The Beatles (wrong)", track.PrimaryArtist!.Name);
        Assert.Equal("Title", (await _db.MetadataProposals.SingleAsync()).FieldMask);
    }

    [Fact]
    public async Task TracksCorrectedToTheSameArtistShareOneArtistRow()
    {
        var first = Analysed("One", "beatles", "One", "The Beatles", file: "1.mp3");
        var second = Analysed("Two", "Beatles, The", "Two", "The Beatles", file: "2.mp3");

        var outcome = await _review.ApplyAsync([Decide(first), Decide(second)], _reviewer, CancellationToken.None);

        Assert.Equal(2, outcome.Done);
        Assert.Equal(1, await _db.Artists.CountAsync(a => a.Name == "The Beatles"));
        Assert.Single(await _db.Tracks.Select(t => t.PrimaryArtistId).Distinct().ToListAsync());
    }

    [Fact]
    public async Task AMatchIsNotAppliedToAFileThatChangedAfterItWasAnalysed()
    {
        var stale = Analysed("Track 01", null, "Help!", "The Beatles", file: "replaced.mp3");
        var fine = Analysed("Track 02", null, "Girl", "The Beatles", file: "fine.mp3");
        stale.Track.FileSizeBytes = 2222;
        await _db.SaveChangesAsync();

        var outcome = await _review.ApplyAsync(
            [Decide(stale), Decide(fine), new ReviewDecision(Guid.NewGuid(), Guid.NewGuid()),
             new ReviewDecision(fine.Item.Id, stale.Match.Id)],
            _reviewer, CancellationToken.None);

        // The good one goes through; the others say why they did not.
        Assert.Equal(1, outcome.Done);
        Assert.Equal(2, outcome.Skipped.Count);
        Assert.Contains(outcome.Skipped, s => s.WorkItemId == stale.Item.Id && s.Reason.Contains("changed since"));
        Assert.Equal("Track 01", (await _db.Tracks.AsNoTracking().SingleAsync(t => t.Id == stale.Track.Id)).Title);
        Assert.Equal("Girl", (await _db.Tracks.AsNoTracking().SingleAsync(t => t.Id == fine.Track.Id)).Title);
    }

    [Fact]
    public async Task ARejectedMatchIsNotOfferedAgainAndChangesNothing()
    {
        var analysed = Analysed("Live Version", "Band", "Studio Version", "Band");

        var outcome = await _review.RejectAsync([Decide(analysed)], _reviewer, CancellationToken.None);

        Assert.Equal(1, outcome.Done);
        Assert.Equal(0, (await _review.PendingAsync(null, null, 0, 50, CancellationToken.None)).Total);
        Assert.Equal("Live Version", (await _db.Tracks.AsNoTracking().SingleAsync()).Title);
        Assert.Equal(0, await _db.CatalogOverrides.CountAsync());
    }

    [Fact]
    public async Task AnAppliedCorrectionCanBeUndone()
    {
        var analysed = Analysed("Track 01", "Unknown Artist", "Help!", "The Beatles");
        await _review.ApplyAsync([Decide(analysed)], _reviewer, CancellationToken.None);
        var proposalId = (await _db.MetadataProposals.SingleAsync()).Id;

        Assert.Null(await _review.RevertAsync(proposalId, _reviewer, CancellationToken.None));
        _db.ChangeTracker.Clear();

        var track = await _db.Tracks.Include(t => t.PrimaryArtist).SingleAsync();
        Assert.Equal("Track 01", track.Title);
        Assert.Equal("Unknown Artist", track.PrimaryArtist!.Name);
        Assert.Equal(0, await _db.CatalogOverrides.CountAsync());
        Assert.Equal(2, await _db.ChangeJournals.CountAsync());
        // Undone, and not put straight back in front of the reviewer.
        Assert.Equal(0, (await _review.PendingAsync(null, null, 0, 50, CancellationToken.None)).Total);
        // A second undo has nothing to do.
        Assert.NotNull(await _review.RevertAsync(proposalId, _reviewer, CancellationToken.None));
    }

    [Fact]
    public async Task ACorrectionChangedAgainSinceIsNotUndoneBlindly()
    {
        var analysed = Analysed("Track 01", null, "Help!", null);
        await _review.ApplyAsync([Decide(analysed)], _reviewer, CancellationToken.None);
        var proposalId = (await _db.MetadataProposals.SingleAsync()).Id;
        analysed.Track.Title = "Help! (2009 Remaster)";
        await _db.SaveChangesAsync();

        var refusal = await _review.RevertAsync(proposalId, _reviewer, CancellationToken.None);

        Assert.Contains("changed again", refusal);
        Assert.Equal("Help! (2009 Remaster)", (await _db.Tracks.AsNoTracking().SingleAsync()).Title);
    }

    [Fact]
    public async Task ApplyingIsRefusedWhileThePurgeHoldsTheLibrary()
    {
        var analysed = Analysed("Track 01", null, "Help!", null);
        using var maintenance = await _gate.AcquireForMaintenanceAsync(CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<Exception>(() => _review.ApplyAsync([Decide(analysed)], _reviewer, timeout.Token));
        Assert.Equal("Track 01", (await _db.Tracks.AsNoTracking().SingleAsync()).Title);
    }

    [Fact]
    public async Task ARescanOfTheUnchangedTagsKeepsTheApprovedCorrection()
    {
        var analysed = Analysed("Wrong Title", "Wrong Artist", "Help!", "The Beatles");
        await _review.ApplyAsync([Decide(analysed)], _reviewer, CancellationToken.None);

        var job = new ScanJob { LibraryRootId = _root.Id };
        _db.ScanJobs.Add(job);
        await _db.SaveChangesAsync();
        // A full rescan re-reads every file; this one still carries its old, wrong tags.
        var scan = new LibraryIndexService(
            _db, new OneFileScanner("/music/a.mp3"), new WrongTags(), new NoArtwork(),
            NullLogger<LibraryIndexService>.Instance);
        await scan.ScanLibraryRootAsync(new ScanRequest(_root.Id, true, job.Id), CancellationToken.None);
        _db.ChangeTracker.Clear();

        var track = await _db.Tracks.Include(t => t.PrimaryArtist).Include(t => t.Album).SingleAsync();
        Assert.Equal("Help!", track.Title);
        Assert.Equal("The Beatles", track.PrimaryArtist!.Name);
        // Fields that were not corrected still follow the file.
        Assert.Equal("Album From Tags", track.Album!.Title);
        Assert.Equal(track.PrimaryArtistId, (await _db.TrackArtists.SingleAsync()).ArtistId);
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
            yield return new DiscoveredAudioFile(path, Path.GetFileName(path), ".mp3", 1000, FileDate);
        }
    }

    private sealed class WrongTags : IMetadataReader
    {
        public Task<AudioMetadata> ReadAsync(string filePath, CancellationToken ct) =>
            Task.FromResult(new AudioMetadata
            {
                Title = "Wrong Title", Artist = "Wrong Artist", Album = "Album From Tags", MimeType = "audio/mpeg"
            });
    }

    private sealed class NoArtwork : IArtworkService
    {
        public Task<ArtworkResult?> ExtractAndStoreAsync(AudioMetadata metadata, string filePath, CancellationToken ct) =>
            Task.FromResult<ArtworkResult?>(null);

        public Task<FileStreamResultModel?> OpenArtworkAsync(Guid artworkId, CancellationToken ct) =>
            Task.FromResult<FileStreamResultModel?>(null);
    }
}
