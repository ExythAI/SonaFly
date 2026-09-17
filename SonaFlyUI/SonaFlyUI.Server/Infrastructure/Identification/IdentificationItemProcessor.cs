using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.Common;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Entities.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>The result of advancing one work item.</summary>
public sealed record WorkItemOutcome(FileAnalysisStatus Status, IdentificationStepException? Failure = null);

/// <summary>
/// Library maintenance (a purge or deleting this root) started while the item
/// was in flight. Nothing further is written; the runner stops its batch.
/// </summary>
public sealed class IdentificationPreemptedException : Exception
{
    public IdentificationPreemptedException()
        : base("Library maintenance is in progress; identification writes are paused.")
    {
    }
}

/// <summary>
/// Advances one work item through local evidence, fingerprint, lookup, and
/// scoring (upgrade plan 5 to 8). Every stage checks what is already
/// persisted first, so repeating an item after a crash, retry, or restart
/// reuses hashes, fingerprints, and cached lookups instead of redoing them.
/// Source files are only ever opened for reading.
/// </summary>
public sealed class IdentificationItemProcessor
{
    private const int MaxStoredCandidates = 25;
    private const int MaxMusicBrainzEnrichments = 3;
    private const string AcoustIdQueryShape = "lookup:meta=recordings";
    private const string MusicBrainzQueryShape = "recording:inc=artist-credits+isrcs";

    private readonly SonaFlyDbContext _db;
    private readonly IFileHashService _hashes;
    private readonly ITagEvidenceReader _tags;
    private readonly IFingerprintTool _fingerprints;
    private readonly IAcoustIdClient _acoustId;
    private readonly IMusicBrainzClient _musicBrainz;
    private readonly LibraryMaintenanceGate _gate;
    private readonly IdentificationOptions _options;
    private readonly ILogger<IdentificationItemProcessor> _logger;

    public IdentificationItemProcessor(
        SonaFlyDbContext db,
        IFileHashService hashes,
        ITagEvidenceReader tags,
        IFingerprintTool fingerprints,
        IAcoustIdClient acoustId,
        IMusicBrainzClient musicBrainz,
        LibraryMaintenanceGate gate,
        IOptions<IdentificationOptions> options,
        ILogger<IdentificationItemProcessor> logger)
    {
        _db = db;
        _hashes = hashes;
        _tags = tags;
        _fingerprints = fingerprints;
        _acoustId = acoustId;
        _musicBrainz = musicBrainz;
        _gate = gate;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WorkItemOutcome> ProcessAsync(Guid workItemId, IdentificationCapabilities capabilities, CancellationToken ct)
    {
        var item = await _db.IdentificationWorkItems.Include(w => w.Job).FirstOrDefaultAsync(w => w.Id == workItemId, ct);
        if (item == null)
        {
            // Purged or its root was deleted after the batch was leased.
            return new WorkItemOutcome(FileAnalysisStatus.Skipped);
        }

        var rootId = item.Job.LibraryRootId;
        try
        {
            var track = await _db.Tracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == item.TrackId, ct);
            var root = await _db.LibraryRoots.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rootId, ct);
            if (track == null || root == null || track.IsMissing || track.LibraryRootId != rootId)
            {
                throw new SkipItem("The track left this music folder after the job was queued. A later \"not yet analysed\" job picks it up again if it returns.");
            }

            item.AttemptCount++;

            var revision = await EnsureLocalEvidenceAsync(item, track, root, ct);
            if (capabilities.HasFingerprintTool == false)
            {
                return await CompleteStageAsync(item, rootId, FileAnalysisStatus.LocalEvidenceReady, "Local evidence collected", ct);
            }

            var fingerprint = await EnsureFingerprintAsync(item, revision, rootId, ct);
            if (capabilities.CanLookUp == false)
            {
                return await CompleteStageAsync(item, rootId, FileAnalysisStatus.Fingerprinted, "Fingerprinted", ct);
            }

            var snapshot = await _db.OriginalTagSnapshots.AsNoTracking()
                .FirstOrDefaultAsync(s => s.FileRevisionId == revision.Id, ct);
            var candidates = await GatherCandidatesAsync(item, fingerprint, snapshot, capabilities.AcoustIdKey!, rootId, ct);

            var decision = RecordingScorer.Decide(
                new FileEvidence(snapshot?.Title, snapshot?.Artist, snapshot?.MusicBrainzRecordingId, fingerprint.DurationSeconds),
                candidates,
                _options.HighConfidenceThreshold,
                _options.ReviewThreshold);

            await StoreDecisionAsync(item, revision, decision, rootId, ct);
            return new WorkItemOutcome(decision.Outcome);
        }
        catch (SkipItem skip)
        {
            return await SkipAsync(item, rootId, skip.Message, ct);
        }
        catch (IdentificationStepException failure)
        {
            return await FailAsync(item, rootId, failure, ct);
        }
    }

    /// <summary>Records an unexpected exception against the item so the batch can continue.</summary>
    public async Task MarkUnexpectedFailureAsync(Guid workItemId, Exception exception, CancellationToken ct)
    {
        var item = await _db.IdentificationWorkItems.Include(w => w.Job).FirstOrDefaultAsync(w => w.Id == workItemId, ct);
        if (item == null)
        {
            return;
        }

        await FailAsync(item, item.Job.LibraryRootId, new IdentificationStepException(
            IdentificationErrorCategory.Unknown, retryable: false,
            $"Unexpected analysis error ({exception.GetType().Name}). See the server log for details.",
            inner: exception), ct);
    }

    // ---------------------------------------------------------------- local evidence

    private async Task<TrackFileRevision> EnsureLocalEvidenceAsync(
        IdentificationWorkItem item, Track track, LibraryRoot root, CancellationToken ct)
    {
        var path = track.FilePath;
        if (FileSystemPaths.IsSameOrUnder(path, root.Path) == false)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.LocalHash, retryable: false,
                "The track's file path is outside its music folder, so it is not analysed.");
        }

        var info = new FileInfo(path);
        if (info.Exists == false)
        {
            throw new SkipItem("The file is no longer on disk. Rescan the music folder to update the library.");
        }

        var size = info.Length;
        var modified = info.LastWriteTimeUtc;

        var existing = await _db.TrackFileRevisions
            .Where(r => r.TrackId == track.Id && r.NormalizedPath == path &&
                        r.FileSizeBytes == size && r.ModifiedUtcSource == modified && r.Sha256 != null)
            .OrderByDescending(r => r.ObservedUtc)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            if (await _db.OriginalTagSnapshots.AnyAsync(s => s.FileRevisionId == existing.Id, ct) == false)
            {
                _db.OriginalTagSnapshots.Add(TagEvidenceReader.BuildSnapshot(_tags.Read(path), existing.Id));
            }

            item.FileRevisionId = existing.Id;
            await AdvanceAsync(item, root.Id, FileAnalysisStatus.LocalEvidenceReady, "Local evidence collected", ct);
            return existing;
        }

        string sha256;
        try
        {
            sha256 = await _hashes.ComputeSha256Async(path, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new SkipItem("The file is no longer on disk. Rescan the music folder to update the library.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.LocalHash, retryable: true,
                "The file could not be read. It will be tried again.", inner: ex);
        }

        // A write racing with analysis must not produce evidence for a mix of
        // two file versions (plan 5.6).
        info.Refresh();
        if (info.Exists == false)
        {
            throw new SkipItem("The file is no longer on disk. Rescan the music folder to update the library.");
        }

        if (info.Length != size || info.LastWriteTimeUtc != modified)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.LocalHash, retryable: true,
                "The file changed while it was being read. It will be analysed again.");
        }

        var tags = _tags.Read(path);
        var revision = new TrackFileRevision
        {
            TrackId = track.Id,
            LibraryRootId = root.Id,
            NormalizedPath = path,
            RelativePath = Path.GetRelativePath(root.Path, path),
            FileSizeBytes = size,
            ModifiedUtcSource = modified,
            Sha256 = sha256,
            Sha256CalculatedUtc = DateTime.UtcNow,
            ParseStatus = tags == null ? "TagsUnreadable" : "Ok"
        };
        _db.TrackFileRevisions.Add(revision);
        _db.OriginalTagSnapshots.Add(TagEvidenceReader.BuildSnapshot(tags, revision.Id));

        item.FileRevisionId = revision.Id;
        await AdvanceAsync(item, root.Id, FileAnalysisStatus.LocalEvidenceReady, "Local evidence collected", ct);
        return revision;
    }

    // ---------------------------------------------------------------- fingerprint

    private async Task<AcousticFingerprint> EnsureFingerprintAsync(
        IdentificationWorkItem item, TrackFileRevision revision, Guid rootId, CancellationToken ct)
    {
        var fingerprint = await _db.AcousticFingerprints.AsNoTracking()
            .Where(f => f.FileRevisionId == revision.Id && f.Status == FileAnalysisStatus.Fingerprinted)
            .OrderByDescending(f => f.CreatedUtc)
            .FirstOrDefaultAsync(ct);
        if (fingerprint != null)
        {
            return fingerprint;
        }

        // Identical bytes have identical audio: reuse a fingerprint taken of
        // another copy or of this file before it moved (plan 6.2).
        var sameBytes = revision.Sha256 == null
            ? null
            : await (from f in _db.AcousticFingerprints.AsNoTracking()
                     join r in _db.TrackFileRevisions.AsNoTracking() on f.FileRevisionId equals r.Id
                     where r.Sha256 == revision.Sha256 && f.Status == FileAnalysisStatus.Fingerprinted
                     orderby f.CreatedUtc descending
                     select f).FirstOrDefaultAsync(ct);

        if (sameBytes != null)
        {
            fingerprint = new AcousticFingerprint
            {
                FileRevisionId = revision.Id,
                Algorithm = sameBytes.Algorithm,
                ToolVersion = sameBytes.ToolVersion,
                Fingerprint = sameBytes.Fingerprint,
                FingerprintDigest = sameBytes.FingerprintDigest,
                DurationSeconds = sameBytes.DurationSeconds
            };
        }
        else
        {
            if (File.Exists(revision.NormalizedPath) == false)
            {
                throw new SkipItem("The file is no longer on disk. Rescan the music folder to update the library.");
            }

            var result = await _fingerprints.ComputeAsync(revision.NormalizedPath, ct);
            fingerprint = new AcousticFingerprint
            {
                FileRevisionId = revision.Id,
                ToolVersion = result.ToolVersion,
                Fingerprint = result.Fingerprint,
                FingerprintDigest = FileHashService.DigestText(result.Fingerprint),
                DurationSeconds = result.DurationSeconds
            };
        }

        _db.AcousticFingerprints.Add(fingerprint);
        await AdvanceAsync(item, rootId, FileAnalysisStatus.Fingerprinted, "Fingerprinted", ct);
        _db.Entry(fingerprint).State = EntityState.Detached;
        return fingerprint;
    }

    // ---------------------------------------------------------------- lookups

    private async Task<List<RecordingEvidence>> GatherCandidatesAsync(
        IdentificationWorkItem item, AcousticFingerprint fingerprint, OriginalTagSnapshot? snapshot,
        string apiKey, Guid rootId, CancellationToken ct)
    {
        await AdvanceAsync(item, rootId, FileAnalysisStatus.LookupPending, "Looking up AcoustID", ct);

        var duration = (int)Math.Round(fingerprint.DurationSeconds ?? 0);
        var cacheKey = ProviderCache.Key("acoustid", "v2", fingerprint.FingerprintDigest, duration.ToString());

        var cached = await ProviderCache.TryGetAsync(_db, AcoustIdClient.ProviderName, cacheKey, AcoustIdQueryShape, ct);
        string json;
        if (cached?.Json != null)
        {
            json = cached.Json;
        }
        else
        {
            json = await _acoustId.LookupJsonAsync(apiKey, fingerprint.Fingerprint, duration, ct);
            GuardWrites(rootId);
            await ProviderCache.StoreAsync(_db, AcoustIdClient.ProviderName, cacheKey, AcoustIdQueryShape, json, isNotFound: false, ct);
        }

        var candidates = new List<RecordingEvidence>();
        foreach (var result in AcoustIdClient.Parse(json))
        {
            if (result.Recordings.Count == 0)
            {
                // Matched audio with no linked recording: reviewable, never a decision (plan 7.5).
                candidates.Add(new RecordingEvidence(AcoustIdClient.ProviderName, result.AcoustId, null, null, [], null,
                    result.Score, "AcoustID match without a linked MusicBrainz recording"));
                continue;
            }

            candidates.AddRange(result.Recordings.Select(r => new RecordingEvidence(
                AcoustIdClient.ProviderName, result.AcoustId, r.MusicBrainzRecordingId, r.Title, r.Artists,
                r.DurationSeconds, result.Score, "AcoustID")));
        }

        // Fill in recordings AcoustID returned without a title, strongest first,
        // bounded so one file cannot spend minutes of the shared MusicBrainz budget.
        var needDetails = candidates
            .Select((c, index) => (c, index))
            .Where(x => x.c.MusicBrainzRecordingId != null && string.IsNullOrWhiteSpace(x.c.Title) &&
                        (x.c.ProviderScore ?? 0) >= _options.ReviewThreshold)
            .OrderByDescending(x => x.c.ProviderScore)
            .Take(MaxMusicBrainzEnrichments)
            .ToList();
        foreach (var (candidate, index) in needDetails)
        {
            var recording = await GetMusicBrainzRecordingAsync(candidate.MusicBrainzRecordingId!, rootId, ct);
            if (recording != null)
            {
                candidates[index] = candidate with
                {
                    Title = recording.Title,
                    Artists = recording.Artists,
                    DurationSeconds = candidate.DurationSeconds ?? recording.DurationSeconds,
                    Provenance = "AcoustID, details from MusicBrainz"
                };
            }
        }

        // An embedded recording ID is a strong clue but may be a wrong copied
        // tag, so it becomes a candidate to compare, not an answer (plan 7.5).
        var embeddedId = snapshot?.MusicBrainzRecordingId;
        if (embeddedId != null &&
            candidates.Any(c => string.Equals(c.MusicBrainzRecordingId, embeddedId, StringComparison.OrdinalIgnoreCase)) == false)
        {
            var recording = await GetMusicBrainzRecordingAsync(embeddedId, rootId, ct);
            if (recording != null)
            {
                candidates.Add(new RecordingEvidence(MusicBrainzClient.ProviderName, null, recording.Id, recording.Title,
                    recording.Artists, recording.DurationSeconds, null, "Embedded MusicBrainz recording ID in the file's tags"));
            }
        }

        return candidates;
    }

    private async Task<MusicBrainzRecording?> GetMusicBrainzRecordingAsync(string recordingId, Guid rootId, CancellationToken ct)
    {
        var cacheKey = ProviderCache.Key("musicbrainz", "recording", recordingId.ToLowerInvariant());
        var cached = await ProviderCache.TryGetAsync(_db, MusicBrainzClient.ProviderName, cacheKey, MusicBrainzQueryShape, ct);
        if (cached != null)
        {
            return cached.IsNotFound || cached.Json == null ? null : MusicBrainzClient.Parse(cached.Json);
        }

        var json = await _musicBrainz.GetRecordingJsonAsync(recordingId, ct);
        GuardWrites(rootId);
        await ProviderCache.StoreAsync(_db, MusicBrainzClient.ProviderName, cacheKey, MusicBrainzQueryShape,
            json, isNotFound: json == null, ct);
        return json == null ? null : MusicBrainzClient.Parse(json);
    }

    private async Task StoreDecisionAsync(
        IdentificationWorkItem item, TrackFileRevision revision, RecordingDecision decision, Guid rootId, CancellationToken ct)
    {
        GuardWrites(rootId);
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Replace rather than append, so repeating the item never duplicates candidates.
        await _db.RecordingCandidates.Where(c => c.WorkItemId == item.Id).ExecuteDeleteAsync(ct);

        var fetched = DateTime.UtcNow;
        foreach (var scored in decision.Candidates.Take(MaxStoredCandidates))
        {
            var evidence = scored.Evidence;
            var artist = evidence.Artists.Count == 0 ? null : string.Join(" & ", evidence.Artists);
            _db.RecordingCandidates.Add(new RecordingCandidate
            {
                WorkItemId = item.Id,
                FileRevisionId = revision.Id,
                Provider = evidence.Provider,
                AcoustId = evidence.AcoustId,
                MusicBrainzRecordingId = evidence.MusicBrainzRecordingId,
                Title = Truncate(evidence.Title, 512),
                Artist = Truncate(artist, 512),
                RawProviderScore = evidence.ProviderScore,
                FinalScore = scored.Score,
                ConfidenceBand = scored.Band,
                ScoringVersion = RecordingScorer.Version,
                ScoringBreakdownJson = scored.BreakdownJson,
                Conflicts = Truncate(scored.Conflicts, 2048),
                Provenance = Truncate(evidence.Provenance, 1024),
                FetchedUtc = fetched
            });
        }

        item.Stage = decision.Outcome switch
        {
            FileAnalysisStatus.Resolved => "Recording identified",
            FileAnalysisStatus.Ambiguous => "Several possible recordings; needs review",
            _ => decision.Candidates.Count == 0 ? "No acoustic match found" : "No confident match"
        };
        MarkSucceeded(item, decision.Outcome);
        await SaveItemAsync(item, ct);
        await transaction.CommitAsync(ct);
    }

    // ---------------------------------------------------------------- state changes

    private async Task AdvanceAsync(IdentificationWorkItem item, Guid rootId, FileAnalysisStatus status, string stage, CancellationToken ct)
    {
        GuardWrites(rootId);
        item.Status = status;
        item.Stage = stage;
        item.UpdatedUtc = DateTime.UtcNow;
        await SaveItemAsync(item, ct);
    }

    private async Task<WorkItemOutcome> CompleteStageAsync(
        IdentificationWorkItem item, Guid rootId, FileAnalysisStatus status, string stage, CancellationToken ct)
    {
        GuardWrites(rootId);
        item.Stage = stage;
        MarkSucceeded(item, status);
        await SaveItemAsync(item, ct);
        return new WorkItemOutcome(status);
    }

    private static void MarkSucceeded(IdentificationWorkItem item, FileAnalysisStatus status)
    {
        item.Status = status;
        item.AttemptCount = 0;
        item.LastError = null;
        item.ErrorCategory = IdentificationErrorCategory.Unknown;
        item.Retryable = false;
        item.NextRetryUtc = null;
        item.UpdatedUtc = DateTime.UtcNow;
    }

    private async Task<WorkItemOutcome> SkipAsync(IdentificationWorkItem item, Guid rootId, string reason, CancellationToken ct)
    {
        DiscardPendingEvidence(item);
        GuardWrites(rootId);
        item.Status = FileAnalysisStatus.Skipped;
        item.Stage = "Skipped";
        item.LastError = reason;
        item.Retryable = false;
        item.NextRetryUtc = null;
        item.UpdatedUtc = DateTime.UtcNow;
        await SaveItemAsync(item, ct);
        return new WorkItemOutcome(FileAnalysisStatus.Skipped);
    }

    private async Task<WorkItemOutcome> FailAsync(
        IdentificationWorkItem item, Guid rootId, IdentificationStepException failure, CancellationToken ct)
    {
        DiscardPendingEvidence(item);
        GuardWrites(rootId);
        var now = DateTime.UtcNow;

        item.LastError = failure.Message;
        item.ErrorCategory = failure.Category;
        item.UpdatedUtc = now;

        if (failure.RequiresAdministrator)
        {
            // The job pauses; the item keeps its progress and retries on resume.
            item.AttemptCount = Math.Max(0, item.AttemptCount - 1);
            item.Status = item.FileRevisionId == null ? FileAnalysisStatus.Pending : FileAnalysisStatus.Fingerprinted;
            item.Stage = "Waiting for an administrator";
            item.Retryable = true;
            item.NextRetryUtc = null;
        }
        else if (failure.Retryable)
        {
            item.Status = FileAnalysisStatus.RetryableError;
            item.Stage = "Waiting to retry";
            item.Retryable = true;
            // Automatic retries are bounded; after that an admin uses "retry errors".
            item.NextRetryUtc = item.AttemptCount <= _options.MaxProviderRetries
                ? now + Backoff(item.AttemptCount, failure.RetryAfter)
                : null;
        }
        else
        {
            item.Status = FileAnalysisStatus.PermanentError;
            item.Stage = "Failed";
            // Configuration problems become retryable once an admin fixes them.
            item.Retryable = failure.Category == IdentificationErrorCategory.Configuration;
            item.NextRetryUtc = null;
        }

        _db.IdentificationErrors.Add(new IdentificationError
        {
            JobId = item.JobId,
            WorkItemId = item.Id,
            Subsystem = failure.Category.ToString(),
            Category = failure.Category,
            Retryable = item.Retryable,
            Message = failure.Message,
            OccurredUtc = now,
            NextRetryUtc = item.NextRetryUtc
        });

        if (failure.InnerException != null)
        {
            _logger.LogWarning(failure.InnerException, "Identification of work item {WorkItemId} failed: {Message}", item.Id, failure.Message);
        }
        else
        {
            _logger.LogInformation("Identification of work item {WorkItemId} failed: {Message}", item.Id, failure.Message);
        }

        await SaveItemAsync(item, ct);
        return new WorkItemOutcome(item.Status, failure);
    }

    /// <summary>
    /// Bounded exponential backoff with jitter: 30s, 1m, 2m... capped at 30 minutes,
    /// never sooner than the provider's own Retry-After.
    /// </summary>
    public static TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
    {
        var seconds = Math.Min(30 * Math.Pow(2, Math.Max(0, attempt - 1)), 1800);
        var jittered = TimeSpan.FromSeconds(seconds * (1 + Random.Shared.NextDouble() * 0.2));
        return retryAfter.HasValue && retryAfter.Value > jittered ? retryAfter.Value : jittered;
    }

    /// <summary>Drops evidence added in this attempt but not yet saved.</summary>
    private void DiscardPendingEvidence(IdentificationWorkItem item)
    {
        foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added && e.Entity != item).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Refuses writes while a purge or this root's deletion is pending, so a
    /// late provider response can never repopulate removed data (plan 12.3).
    /// </summary>
    private void GuardWrites(Guid rootId)
    {
        if (_gate.MaintenancePending || _gate.IsRootPendingDeletion(rootId))
        {
            throw new IdentificationPreemptedException();
        }
    }

    private async Task SaveItemAsync(IdentificationWorkItem item, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (await ItemWasRemovedAsync(item))
            {
                // A purge or root deletion removed the item or its track mid-write;
                // the foreign keys rejected the late write, which is the intent.
                throw new IdentificationPreemptedException();
            }

            throw;
        }
    }

    private async Task<bool> ItemWasRemovedAsync(IdentificationWorkItem item)
    {
        // Untracked queries read the database, not the failed change set.
        return await _db.IdentificationWorkItems.AsNoTracking().AnyAsync(w => w.Id == item.Id) == false ||
               await _db.Tracks.AsNoTracking().AnyAsync(t => t.Id == item.TrackId) == false;
    }

    private static string? Truncate(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..max];

    /// <summary>Internal signal: stop this item and record it as skipped.</summary>
    private sealed class SkipItem(string reason) : Exception(reason);
}
