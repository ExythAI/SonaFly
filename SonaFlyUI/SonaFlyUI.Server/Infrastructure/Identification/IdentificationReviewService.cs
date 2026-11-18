using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Entities.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>One track whose best identification match disagrees with the catalog.</summary>
public record ReviewItemDto(
    Guid WorkItemId,
    Guid TrackId,
    Guid CandidateId,
    string? RelativePath,
    string CurrentTitle,
    string? CurrentArtist,
    string? CurrentAlbum,
    string? ProposedTitle,
    string? ProposedArtist,
    bool TitleChanges,
    bool ArtistChanges,
    double? Score,
    string? ConfidenceBand,
    int CandidateCount,
    string? Conflicts);

/// <summary>A correction that was applied, for the history list and for undoing it.</summary>
public record AppliedChangeDto(
    Guid ProposalId,
    Guid TrackId,
    string? OldTitle,
    string? NewTitle,
    string? OldArtist,
    string? NewArtist,
    DateTime? AppliedUtc);

public record ReviewPageDto(int Total, int HighConfidence, IReadOnlyList<ReviewItemDto> Items);

/// <param name="Title">Take the match's title.</param>
/// <param name="Artist">Take the match's artist.</param>
public record ReviewDecision(Guid WorkItemId, Guid CandidateId, bool Title = true, bool Artist = true);

public record SkippedDecision(Guid WorkItemId, string Reason);

public record ReviewOutcomeDto(int Done, IReadOnlyList<SkippedDecision> Skipped);

/// <summary>
/// Turns identification results into catalog corrections that an administrator approves.
/// <para>
/// This is the recording-only milestone: a match can correct a track's title and artist.
/// Album, track number and year need an exact-release decision and stay out of reach
/// here (see <see cref="ProposalValidator"/>). Audio files are never written; a
/// correction changes what the library shows, is recorded in the change journal, and is
/// held as a catalog override so that a later rescan of the unchanged tags does not put
/// the old text back.
/// </para>
/// </summary>
public sealed class IdentificationReviewService(
    SonaFlyDbContext db,
    LibraryMaintenanceGate gate,
    IOptions<IdentificationOptions> options,
    ILogger<IdentificationReviewService> logger)
{
    public const string TitleField = "Title";
    public const string ArtistField = "Artist";

    private static readonly FileAnalysisStatus[] ReviewableStates =
    [
        FileAnalysisStatus.Resolved, FileAnalysisStatus.Ambiguous, FileAnalysisStatus.CandidatesReady,
    ];

    // ── What needs a decision ──

    /// <summary>
    /// Tracks whose best match would change their title or artist and has not been
    /// decided yet. <paramref name="band"/> narrows it to one confidence band.
    /// </summary>
    public async Task<ReviewPageDto> PendingAsync(Guid? rootId, string? band, int skip, int take, CancellationToken ct)
    {
        var workItems = db.IdentificationWorkItems.AsNoTracking()
            .Where(w => ReviewableStates.Contains(w.Status));
        if (rootId is { } root) workItems = workItems.Where(w => w.Job.LibraryRootId == root);

        // A track analysed by several jobs is reviewed once, on its newest analysis.
        var newest = (await workItems
                .Select(w => new { w.Id, w.TrackId, w.FileRevisionId, w.UpdatedUtc })
                .ToListAsync(ct))
            .GroupBy(w => w.TrackId)
            .Select(g => g.OrderByDescending(w => w.UpdatedUtc).First())
            .ToList();
        if (newest.Count == 0) return new ReviewPageDto(0, 0, []);

        var workItemIds = newest.Select(w => w.Id).ToHashSet();
        var best = (await db.RecordingCandidates.AsNoTracking()
                .Where(c => c.FinalScore != null && ReviewableStates.Contains(c.WorkItem.Status))
                .Select(c => new
                {
                    c.Id, c.WorkItemId, c.Title, c.Artist, c.FinalScore, c.ConfidenceBand, c.Conflicts,
                })
                .ToListAsync(ct))
            .Where(c => workItemIds.Contains(c.WorkItemId))
            .GroupBy(c => c.WorkItemId)
            .ToDictionary(g => g.Key, g => new { Top = g.OrderByDescending(c => c.FinalScore).First(), Count = g.Count() });

        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => t.IsIndexed && !t.IsMissing)
            .Select(t => new
            {
                t.Id, t.Title,
                Artist = t.PrimaryArtist != null ? t.PrimaryArtist.Name : null,
                Album = t.Album != null ? t.Album.Title : null,
            })
            .ToDictionaryAsync(t => t.Id, ct);

        // A candidate that was applied or rejected is not offered again.
        var decided = (await db.MetadataProposals.AsNoTracking()
                .Where(p => p.RecordingCandidateId != null &&
                            (p.Status == ProposalStatus.Applied || p.Status == ProposalStatus.Rejected))
                .Select(p => p.RecordingCandidateId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var revisionIds = newest.Where(w => w.FileRevisionId != null).Select(w => w.FileRevisionId!.Value).ToHashSet();
        var paths = (await db.TrackFileRevisions.AsNoTracking()
                .Select(r => new { r.Id, r.RelativePath })
                .ToListAsync(ct))
            .Where(r => revisionIds.Contains(r.Id))
            .ToDictionary(r => r.Id, r => r.RelativePath);

        var pending = new List<ReviewItemDto>();
        foreach (var item in newest)
        {
            if (best.TryGetValue(item.Id, out var match) == false) continue;
            if (tracks.TryGetValue(item.TrackId, out var track) == false) continue;
            if (decided.Contains(match.Top.Id)) continue;

            var titleChanges = Differs(track.Title, match.Top.Title);
            var artistChanges = Differs(track.Artist, match.Top.Artist);
            if (titleChanges == false && artistChanges == false) continue;

            pending.Add(new ReviewItemDto(
                item.Id, item.TrackId, match.Top.Id,
                item.FileRevisionId is { } revision ? paths.GetValueOrDefault(revision) : null,
                track.Title, track.Artist, track.Album,
                match.Top.Title, match.Top.Artist, titleChanges, artistChanges,
                match.Top.FinalScore, match.Top.ConfidenceBand, match.Count, match.Top.Conflicts));
        }

        var highConfidence = pending.Count(p => p.ConfidenceBand == "High");
        if (string.IsNullOrWhiteSpace(band) == false)
        {
            pending = pending.Where(p => string.Equals(p.ConfidenceBand, band, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var page = pending
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.CurrentArtist).ThenBy(p => p.CurrentAlbum).ThenBy(p => p.CurrentTitle).ThenBy(p => p.TrackId)
            .Skip(skip).Take(take)
            .ToList();
        return new ReviewPageDto(pending.Count, highConfidence, page);
    }

    /// <summary>
    /// A match only counts as a change when it says something and says something
    /// different. Case and spacing count: correcting "the beatles" is the point.
    /// </summary>
    private static bool Differs(string? current, string? proposed) =>
        string.IsNullOrWhiteSpace(proposed) == false &&
        string.Equals(current?.Trim(), proposed.Trim(), StringComparison.Ordinal) == false;

    /// <summary>The most recently applied corrections, newest first.</summary>
    public async Task<IReadOnlyList<AppliedChangeDto>> AppliedAsync(int take, CancellationToken ct) =>
        await db.MetadataProposals.AsNoTracking()
            .Where(p => p.Status == ProposalStatus.Applied)
            .OrderByDescending(p => p.AppliedUtc)
            .Take(take)
            .Select(p => new AppliedChangeDto(
                p.Id, p.TrackId, p.OldTitle, p.NewTitle, p.OldArtist, p.NewArtist, p.AppliedUtc))
            .ToListAsync(ct);

    // ── Deciding ──

    /// <summary>
    /// Applies the chosen matches to the catalog. Each one is rechecked first; any that no
    /// longer holds is skipped with the reason, and the rest go ahead. Everything that is
    /// applied commits together.
    /// </summary>
    public async Task<ReviewOutcomeDto> ApplyAsync(
        IReadOnlyList<ReviewDecision> decisions, Guid? reviewerId, CancellationToken ct)
    {
        // Waits for a running scan rather than cancelling it, and refuses during a purge.
        using var lease = await gate.AcquireForApplyAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var skipped = new List<SkippedDecision>();
        var done = 0;
        var now = DateTime.UtcNow;

        foreach (var decision in decisions.DistinctBy(d => d.WorkItemId))
        {
            var prepared = await PrepareAsync(decision, ct);
            if (prepared.Refusal is { } refusal)
            {
                skipped.Add(new SkippedDecision(decision.WorkItemId, refusal));
                continue;
            }

            var (workItem, candidate, track, revision) = (prepared.WorkItem!, prepared.Candidate!, prepared.Track!, prepared.Revision);
            var oldArtist = track.PrimaryArtist?.Name;
            var takeTitle = decision.Title && Differs(track.Title, candidate.Title);
            var takeArtist = decision.Artist && Differs(oldArtist, candidate.Artist);
            if (takeTitle == false && takeArtist == false)
            {
                skipped.Add(new SkippedDecision(decision.WorkItemId, "The catalog already matches."));
                continue;
            }

            var proposal = new MetadataProposal
            {
                JobId = workItem.JobId, WorkItemId = workItem.Id, TrackId = track.Id,
                RecordingCandidateId = candidate.Id,
                ExpectedFileSizeBytes = track.FileSizeBytes,
                ExpectedModifiedUtcSource = track.ModifiedUtcSource,
                FieldMask = string.Join(",", new[] { takeTitle ? TitleField : null, takeArtist ? ArtistField : null }
                    .Where(f => f != null)),
                OldTitle = track.Title, NewTitle = takeTitle ? candidate.Title!.Trim() : track.Title,
                OldArtist = oldArtist, NewArtist = takeArtist ? candidate.Artist!.Trim() : oldArtist,
                EvidenceSnapshotJson = JsonSerializer.Serialize(new
                {
                    candidate.Provider, candidate.MusicBrainzRecordingId, candidate.FinalScore,
                    candidate.ConfidenceBand, candidate.ScoringVersion,
                }),
                Status = ProposalStatus.Applied, ReviewerUserId = reviewerId, ReviewedUtc = now, AppliedUtc = now,
            };

            var problems = ProposalValidator.Validate(proposal, options.Value.EnableExactReleaseApply);
            if (problems.Count > 0)
            {
                skipped.Add(new SkippedDecision(decision.WorkItemId, string.Join(" ", problems)));
                continue;
            }

            var digest = revision == null
                ? null
                : await db.AcousticFingerprints.AsNoTracking()
                    .Where(f => f.FileRevisionId == revision.Id)
                    .Select(f => f.FingerprintDigest)
                    .FirstOrDefaultAsync(ct);

            db.MetadataProposals.Add(proposal);
            if (takeTitle)
            {
                track.Title = proposal.NewTitle!;
                await SetOverrideAsync(track, TitleField, proposal.NewTitle, null, proposal.Id, digest, reviewerId, now, ct);
            }

            if (takeArtist)
            {
                var artist = await FindOrCreateArtistAsync(proposal.NewArtist!, ct);
                await SetPrimaryArtistAsync(track, artist, ct);
                await SetOverrideAsync(track, ArtistField, artist.Name, artist.Id, proposal.Id, digest, reviewerId, now, ct);
            }

            track.ModifiedUtc = now;
            db.ChangeJournals.Add(JournalEntry(track.Id, proposal.Id, reviewerId,
                new { title = proposal.OldTitle, artist = proposal.OldArtist },
                new { title = proposal.NewTitle, artist = proposal.NewArtist },
                "catalog-apply"));

            // Saved per track so the next decision in the batch sees an artist this one created.
            await db.SaveChangesAsync(ct);
            done++;
        }

        await transaction.CommitAsync(ct);
        if (done > 0) logger.LogInformation("Applied {Count} identification correction(s) to the catalog.", done);
        return new ReviewOutcomeDto(done, skipped);
    }

    /// <summary>
    /// Records that the chosen matches are wrong, so they stop being offered. Nothing in
    /// the catalog changes.
    /// </summary>
    public async Task<ReviewOutcomeDto> RejectAsync(
        IReadOnlyList<ReviewDecision> decisions, Guid? reviewerId, CancellationToken ct)
    {
        var skipped = new List<SkippedDecision>();
        var done = 0;
        var now = DateTime.UtcNow;

        foreach (var decision in decisions.DistinctBy(d => d.WorkItemId))
        {
            var prepared = await PrepareAsync(decision, ct, requireUnchangedFile: false);
            if (prepared.Refusal is { } refusal)
            {
                skipped.Add(new SkippedDecision(decision.WorkItemId, refusal));
                continue;
            }

            var (workItem, candidate, track) = (prepared.WorkItem!, prepared.Candidate!, prepared.Track!);
            db.MetadataProposals.Add(new MetadataProposal
            {
                JobId = workItem.JobId, WorkItemId = workItem.Id, TrackId = track.Id,
                RecordingCandidateId = candidate.Id,
                ExpectedFileSizeBytes = track.FileSizeBytes, ExpectedModifiedUtcSource = track.ModifiedUtcSource,
                FieldMask = $"{TitleField},{ArtistField}",
                OldTitle = track.Title, NewTitle = candidate.Title,
                OldArtist = track.PrimaryArtist?.Name, NewArtist = candidate.Artist,
                Status = ProposalStatus.Rejected, ReviewerUserId = reviewerId, ReviewedUtc = now,
            });
            done++;
        }

        await db.SaveChangesAsync(ct);
        return new ReviewOutcomeDto(done, skipped);
    }

    /// <summary>
    /// Undoes an applied correction: the track gets its previous title and artist back and
    /// the overrides that held the correction are removed. Refused when the track has
    /// been changed again since, because the "previous" values would then be the wrong ones.
    /// </summary>
    /// <returns>Null on success, or why it could not be undone.</returns>
    public async Task<string?> RevertAsync(Guid proposalId, Guid? reviewerId, CancellationToken ct)
    {
        using var lease = await gate.AcquireForApplyAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var proposal = await db.MetadataProposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
        if (proposal == null) return "That change no longer exists.";
        if (proposal.Status != ProposalStatus.Applied) return "That change is not currently applied.";

        var track = await db.Tracks.Include(t => t.PrimaryArtist).FirstOrDefaultAsync(t => t.Id == proposal.TrackId, ct);
        if (track == null) return "The track is no longer in the library.";

        var fields = proposal.FieldMask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var undoTitle = fields.Contains(TitleField);
        var undoArtist = fields.Contains(ArtistField);

        if ((undoTitle && track.Title != proposal.NewTitle) ||
            (undoArtist && track.PrimaryArtist?.Name != proposal.NewArtist))
        {
            return "The track has been changed again since this correction, so it cannot be undone automatically.";
        }

        if (undoTitle) track.Title = proposal.OldTitle ?? track.Title;
        if (undoArtist)
        {
            var artist = string.IsNullOrWhiteSpace(proposal.OldArtist)
                ? null
                : await FindOrCreateArtistAsync(proposal.OldArtist, ct);
            await SetPrimaryArtistAsync(track, artist, ct);
        }

        var overrides = await db.CatalogOverrides
            .Where(o => o.TrackId == track.Id && o.ProposalId == proposal.Id)
            .ToListAsync(ct);
        db.CatalogOverrides.RemoveRange(overrides);

        var now = DateTime.UtcNow;
        track.ModifiedUtc = now;
        // Rejected, not deleted: the same match must not be offered again straight away.
        proposal.Status = ProposalStatus.Rejected;
        proposal.ReviewedUtc = now;
        proposal.ReviewerUserId = reviewerId;
        proposal.ConcurrencyToken = Guid.NewGuid().ToString("N");

        db.ChangeJournals.Add(JournalEntry(track.Id, proposal.Id, reviewerId,
            new { title = proposal.NewTitle, artist = proposal.NewArtist },
            new { title = proposal.OldTitle, artist = proposal.OldArtist },
            "catalog-revert"));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }

    // ── Helpers ──

    private sealed record Prepared(
        IdentificationWorkItem? WorkItem, RecordingCandidate? Candidate, Track? Track,
        TrackFileRevision? Revision, string? Refusal);

    /// <summary>Loads what a decision refers to and checks it still stands.</summary>
    private async Task<Prepared> PrepareAsync(ReviewDecision decision, CancellationToken ct, bool requireUnchangedFile = true)
    {
        static Prepared Refuse(string reason) => new(null, null, null, null, reason);

        var workItem = await db.IdentificationWorkItems.FirstOrDefaultAsync(w => w.Id == decision.WorkItemId, ct);
        if (workItem == null) return Refuse("That analysis result no longer exists.");

        var candidate = await db.RecordingCandidates.FirstOrDefaultAsync(
            c => c.Id == decision.CandidateId && c.WorkItemId == workItem.Id, ct);
        if (candidate == null) return Refuse("That match does not belong to this track's analysis.");

        var track = await db.Tracks.Include(t => t.PrimaryArtist).FirstOrDefaultAsync(t => t.Id == workItem.TrackId, ct);
        if (track == null || track.IsMissing) return Refuse("The track is no longer in the library.");

        var revision = workItem.FileRevisionId == null
            ? null
            : await db.TrackFileRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == workItem.FileRevisionId, ct);

        // The match was made from one particular file. If the file has been replaced
        // since, the match may describe audio that is no longer there.
        if (requireUnchangedFile && revision != null &&
            (revision.FileSizeBytes != track.FileSizeBytes || revision.ModifiedUtcSource != track.ModifiedUtcSource))
        {
            return Refuse("The file has changed since it was analysed. Run identification again first.");
        }

        return new Prepared(workItem, candidate, track, revision, null);
    }

    private async Task<Artist> FindOrCreateArtistAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        // Exact spelling first, so applying "The Beatles" over "the beatles" can settle
        // on the correctly cased artist when both exist.
        var folded = Application.Common.SearchText.Normalize(name);
        var artist = await db.Artists.FirstOrDefaultAsync(a => a.Name == name, ct)
                     ?? (await db.Artists.Where(a => a.SearchName == folded).ToListAsync(ct))
                         .FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (artist != null) return artist;

        artist = new Artist { Name = name, SortName = name };
        db.Artists.Add(artist);
        return artist;
    }

    /// <summary>
    /// Moves the track to an artist, including the junction row: content restrictions are
    /// evaluated through TrackArtists, so the two must change together.
    /// </summary>
    private async Task SetPrimaryArtistAsync(Track track, Artist? artist, CancellationToken ct)
    {
        track.PrimaryArtist = artist;
        track.PrimaryArtistId = artist?.Id;

        var links = await db.TrackArtists
            .Where(ta => ta.TrackId == track.Id && ta.Role == TrackArtistRole.Primary)
            .ToListAsync(ct);
        var keep = artist == null ? null : links.FirstOrDefault(l => l.ArtistId == artist.Id);
        db.TrackArtists.RemoveRange(links.Where(l => ReferenceEquals(l, keep) == false));
        if (artist != null && keep == null)
        {
            db.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, Role = TrackArtistRole.Primary });
        }
    }

    private async Task SetOverrideAsync(
        Track track, string field, string? text, Guid? valueId, Guid proposalId, string? fingerprintDigest,
        Guid? reviewerId, DateTime now, CancellationToken ct)
    {
        var existing = await db.CatalogOverrides.FirstOrDefaultAsync(o => o.TrackId == track.Id && o.Field == field, ct);
        if (existing == null)
        {
            existing = new CatalogOverride { TrackId = track.Id, Field = field };
            db.CatalogOverrides.Add(existing);
        }

        existing.ValueText = text;
        existing.ValueId = valueId;
        existing.ProposalId = proposalId;
        existing.SourceFingerprintDigest = fingerprintDigest;
        existing.SourceFileSizeBytes = track.FileSizeBytes;
        existing.SourceModifiedUtc = track.ModifiedUtcSource;
        existing.ApprovedByUserId = reviewerId;
        existing.ApprovedUtc = now;
        existing.ModifiedUtc = now;
    }

    private static ChangeJournal JournalEntry(
        Guid trackId, Guid proposalId, Guid? reviewerId, object before, object after, string step) => new()
    {
        TrackId = trackId, ProposalId = proposalId, ReviewerUserId = reviewerId,
        OldTagSnapshotJson = JsonSerializer.Serialize(before),
        NewTagSnapshotJson = JsonSerializer.Serialize(after),
        StepsAttemptedJson = JsonSerializer.Serialize(new[] { step }),
        StepsCompletedJson = JsonSerializer.Serialize(new[] { step }),
    };
}
