using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Entities.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identification;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Phase 0 gates for the music identification upgrade:
/// with identification disabled nothing changes, and the new building blocks
/// (snapshot contract, hashing, normalization, additive schema) hold.
/// </summary>
public sealed class IdentificationFoundationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SonaFlyDbContext _db;

    public IdentificationFoundationTests()
    {
        _connection.Open();
        _db = new SonaFlyDbContext(
            new DbContextOptionsBuilder<SonaFlyDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void IdentificationIsDisabledByDefaultAndSourceWritesStayOff()
    {
        var options = new IdentificationOptions();
        Assert.False(options.Enabled);
        Assert.False(options.AllowSourceFileWrites);
        Assert.False(options.HasAcoustIdKey);
        Assert.Contains(options.GetBlockers(), b => b.Contains("disabled"));
    }

    [Fact]
    public void DisabledOptionsExplainLocalOnlyModeWhenEnabledWithoutKey()
    {
        var options = new IdentificationOptions { Enabled = true };
        Assert.Contains(options.GetBlockers(), b => b.Contains("AcoustID"));
    }

    [Fact]
    public void ProposalGuardMarksChangedFilesStale()
    {
        var key = new FileRevisionKey(
            Guid.NewGuid(), Guid.NewGuid(), "/music/song.mp3", 1000,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(ProposalGuard.IsStale(key, 1000, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.True(ProposalGuard.IsStale(key, 1001, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.True(ProposalGuard.IsStale(key, 1000, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void CatalogApprovalNeverCoversGenreOrFilePaths()
    {
        Assert.DoesNotContain("Genre", ProposalFields.AllowedCatalogFields);
        Assert.Contains("Title", ProposalFields.AllowedCatalogFields);
        Assert.Contains("Artist", ProposalFields.AllowedCatalogFields);
        Assert.Contains("Album", ProposalFields.AllowedCatalogFields);
    }

    [Fact]
    public void NormalizationKeepsVersionEvidenceInsteadOfStrippingIt()
    {
        Assert.True(TextNormalization.MatchesIgnoringCaseAndPunctuation("The Beatles", "beatles"));
        Assert.False(TextNormalization.MatchesIgnoringCaseAndPunctuation("Song (Live)", "Song"));

        var tokens = TextNormalization.ExtractVersionTokens("Song (Live, Remastered)");
        Assert.Contains("live", tokens);
        Assert.Contains("remastered", tokens);
    }

    [Fact]
    public async Task FileHashIsStableAndDistinguishesDifferentBytes()
    {
        IFileHashService hashes = new FileHashService();
        var dir = Path.Combine(Path.GetTempPath(), "sonafly-id-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var a = Path.Combine(dir, "a.mp3");
            var copy = Path.Combine(dir, "copy.mp3");
            var other = Path.Combine(dir, "other.mp3");
            await File.WriteAllBytesAsync(a, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(copy, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(other, [1, 2, 3, 5]);

            var hashA = await hashes.ComputeSha256Async(a, CancellationToken.None);
            var hashCopy = await hashes.ComputeSha256Async(copy, CancellationToken.None);
            var hashOther = await hashes.ComputeSha256Async(other, CancellationToken.None);

            Assert.Equal(64, hashA.Length);
            Assert.Equal(hashA, hashCopy);
            Assert.NotEqual(hashA, hashOther);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TagSnapshotPreservesOriginalStringsVerbatim()
    {
        var metadata = new AudioMetadata
        {
            Title = "  Song (Live)  ",
            Album = "Album",
            Artist = "Artist",
            Genre = "Rock",
            Year = 1971,
            TrackNumber = 3,
            DiscNumber = 1,
            MimeType = "audio/mpeg"
        };

        var snapshot = OriginalTagSnapshotBuilder.Build(metadata, Guid.NewGuid(), "TagLibSharp-Tests");
        Assert.Equal("  Song (Live)  ", snapshot.Title);
        Assert.Equal(1971, snapshot.Year);
        Assert.Equal("TagLibSharp-Tests", snapshot.ParserVersion);
    }

    [Fact]
    public async Task AdditiveSchemaPersistsJobsWorkItemsAndEvidence()
    {
        var root = new LibraryRoot { Name = "Music", Path = "/music" };
        _db.LibraryRoots.Add(root);
        await _db.SaveChangesAsync();

        var track = new Track
        {
            LibraryRootId = root.Id,
            FilePath = "/music/song.mp3",
            FileName = "song.mp3",
            FileExtension = ".mp3",
            FileSizeBytes = 1000,
            ModifiedUtcSource = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Title = "Song",
            MimeType = "audio/mpeg",
            IsIndexed = true
        };
        _db.Tracks.Add(track);
        await _db.SaveChangesAsync();

        var revision = new TrackFileRevision
        {
            TrackId = track.Id,
            LibraryRootId = root.Id,
            NormalizedPath = "/music/song.mp3",
            FileSizeBytes = 1000,
            ModifiedUtcSource = track.ModifiedUtcSource,
            Sha256 = Convert.ToHexString(new byte[32]).ToLowerInvariant()
        };
        _db.TrackFileRevisions.Add(revision);
        await _db.SaveChangesAsync();

        var job = new IdentificationJob { LibraryRootId = root.Id, TotalItems = 1 };
        _db.IdentificationJobs.Add(job);
        await _db.SaveChangesAsync();

        _db.IdentificationWorkItems.Add(new IdentificationWorkItem
        {
            JobId = job.Id,
            TrackId = track.Id,
            FileRevisionId = revision.Id,
            Status = FileAnalysisStatus.LocalEvidenceReady
        });
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _db.IdentificationJobs.CountAsync());
        Assert.Equal(1, await _db.IdentificationWorkItems.CountAsync());
        Assert.Equal(1, await _db.TrackFileRevisions.CountAsync());

        // Existing catalog rows are untouched by the additive tables.
        Assert.Equal("Song", (await _db.Tracks.FirstAsync()).Title);
        Assert.Null(await _db.Tracks.Select(t => t.ContentHash).FirstAsync());
    }
}

