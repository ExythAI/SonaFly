using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identification;

namespace SonaFlyUI.Server.Api.Controllers;

/// <summary>
/// Admin-only music identification endpoints (upgrade plan 13.1).
/// Current milestone (plan stage B): job lifecycle, fingerprint and AcoustID
/// candidates, and admin inspection; the background runner does the work.
/// Catalog apply arrives in milestone D; source-file writes stay behind
/// AllowSourceFileWrites and a writable mount (plan 14.3).
/// Absolute server paths, raw provider responses, and secrets are never
/// exposed here; work items identify tracks by ID only.
/// </summary>
[ApiController]
[Route("api/identification")]
[Authorize(Roles = "Admin")]
public class IdentificationController : ControllerBase
{
    private const int MaxPageSize = 100;

    /// <summary>
    /// Admits job creation and requeueing one at a time. SonaFly runs as a
    /// single process over SQLite, so an in-process lock is sufficient.
    /// </summary>
    private static readonly SemaphoreSlim JobAdmission = new(1, 1);

    private readonly SonaFlyDbContext _db;
    private readonly IdentificationOptions _options;
    private readonly IServerSettingsService _settings;
    private readonly ILogger<IdentificationController> _logger;

    public IdentificationController(
        SonaFlyDbContext db,
        IOptions<IdentificationOptions> options,
        IServerSettingsService settings,
        ILogger<IdentificationController> logger)
    {
        _db = db;
        _options = options.Value;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Safe configuration status for the Music folders page (plan 13.2).
    /// The AcoustID state reflects the effective key: a UI-stored key wins
    /// over configuration. No secret values are exposed.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<IdentificationStatusDto>> GetStatus(CancellationToken ct)
    {
        var key = await _settings.GetEffectiveAcoustIdKeyAsync(ct);
        var source = await _settings.GetAcoustIdKeySourceAsync(ct);
        var acoustIdConfigured = string.IsNullOrWhiteSpace(key) == false;

        var blockers = _options.GetBlockers(acoustIdConfigured);
        var localOnly = _options.Enabled && acoustIdConfigured == false;
        var message = _options.Enabled == false
            ? "Music identification is disabled. Enable SonaFly:Identification:Enabled to analyze music."
            : localOnly
                ? "Identification is enabled without an AcoustID key: local evidence collection only."
                : blockers.Count == 0
                    ? source == AcoustIdKeySource.Database
                        ? "Identification is configured and ready (key stored securely on the server)."
                        : "Identification is configured and ready."
                    : "Identification is enabled with warnings; see blockers.";

        return Ok(new IdentificationStatusDto(
            _options.Enabled,
            acoustIdConfigured,
            source.ToString(),
            _options.HasFpcalc,
            string.IsNullOrWhiteSpace(_options.FfmpegPath) == false,
            localOnly,
            blockers,
            message));
    }

    /// <summary>
    /// Creates an analysis job for one root, or for explicit track IDs in that
    /// root. The UI may queue one job per selected root (plan section 1).
    /// </summary>
    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJob([FromBody] CreateIdentificationJobRequest request, CancellationToken ct)
    {
        if (_options.Enabled == false)
        {
            return Conflict(new { message = "Music identification is disabled. Set SonaFly:Identification:Enabled to true first." });
        }

        var root = await _db.LibraryRoots.FirstOrDefaultAsync(lr => lr.Id == request.LibraryRootId, ct);
        if (root == null)
        {
            return NotFound(new { message = "Library root not found." });
        }

        if (root.IsEnabled == false)
        {
            return Conflict(new { message = "This library root is disabled. Enable it before analyzing." });
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();

        // Serialize admission: the idempotency and one-active-job checks below
        // are read-then-insert, so concurrent requests must not interleave.
        await JobAdmission.WaitAsync(ct);
        try
        {
            return await CreateJobAdmittedAsync(request, idempotencyKey, ct);
        }
        finally
        {
            JobAdmission.Release();
        }
    }

    private async Task<IActionResult> CreateJobAdmittedAsync(
        CreateIdentificationJobRequest request, string? idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey != null)
        {
            var existing = await _db.IdentificationJobs
                .FirstOrDefaultAsync(j => j.ClientRequestKey == idempotencyKey, ct);
            if (existing != null)
            {
                return Accepted(new { jobId = existing.Id, status = existing.Status.ToString(), message = "Duplicate request: returning the existing job." });
            }
        }

        var pending = await ActiveJobForRootAsync(request.LibraryRootId, excludeJobId: null, ct);
        if (pending != null)
        {
            return Accepted(new { jobId = pending.Id, status = pending.Status.ToString(), message = "An analysis job is already active for this library root." });
        }

        // A job lists its tracks when it is created. Queued during a scan, it
        // would miss tracks the scan adds and keep ones the scan removes.
        if (await IdentificationScanGuard.IsScanActiveAsync(_db, request.LibraryRootId, ct))
        {
            return Conflict(new { message = "A scan of this music folder is queued or running. Start identification after it finishes, so newly scanned tracks are included." });
        }

        var presentTracks = _db.Tracks.Where(t => t.LibraryRootId == request.LibraryRootId && t.IsMissing == false);

        List<Guid> trackIds;
        string selectionMode;
        if (request.TrackIds is { Length: > 0 })
        {
            selectionMode = IdentificationSelectionModes.TrackIds;
            trackIds = await presentTracks
                .Where(t => request.TrackIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);
            if (trackIds.Count == 0)
            {
                return BadRequest(new { message = "None of the requested tracks exist in this library root." });
            }
        }
        else if (string.Equals(request.Mode, IdentificationSelectionModes.Root, StringComparison.OrdinalIgnoreCase))
        {
            selectionMode = IdentificationSelectionModes.Root;
            trackIds = await presentTracks.Select(t => t.Id).ToListAsync(ct);
        }
        else if (string.IsNullOrWhiteSpace(request.Mode) ||
                 string.Equals(request.Mode, IdentificationSelectionModes.Unanalyzed, StringComparison.OrdinalIgnoreCase))
        {
            selectionMode = IdentificationSelectionModes.Unanalyzed;
            var capabilities = new IdentificationCapabilities(_options.HasFpcalc, await _settings.GetEffectiveAcoustIdKeyAsync(ct));
            var completed = capabilities.CompletedStatuses.ToList();

            // Analysed means a completed item for the file as it is now: same
            // path, size, and modification time. A changed file counts as new.
            trackIds = await presentTracks
                .Where(t => _db.IdentificationWorkItems.Any(w =>
                    w.TrackId == t.Id &&
                    completed.Contains(w.Status) &&
                    _db.TrackFileRevisions.Any(r =>
                        r.Id == w.FileRevisionId &&
                        r.NormalizedPath == t.FilePath &&
                        r.FileSizeBytes == t.FileSizeBytes &&
                        r.ModifiedUtcSource == t.ModifiedUtcSource)) == false)
                .Select(t => t.Id)
                .ToListAsync(ct);
            if (trackIds.Count == 0)
            {
                return Ok(new { jobId = (Guid?)null, status = "NothingToDo", message = "Every track in this music folder is already analysed." });
            }
        }
        else
        {
            return BadRequest(new { message = "Unknown selection mode. Use Unanalyzed or Root." });
        }

        var job = new IdentificationJob
        {
            LibraryRootId = request.LibraryRootId,
            ClientRequestKey = idempotencyKey,
            SelectionMode = selectionMode,
            Status = IdentificationJobStatus.Queued,
            Stage = "Queued",
            TotalItems = trackIds.Count
        };
        // One transaction: a failure part-way never leaves a queued job with
        // only some of its work items.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        _db.IdentificationJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        const int batchSize = 500;
        foreach (var batch in trackIds.Chunk(batchSize))
        {
            _db.IdentificationWorkItems.AddRange(batch.Select(trackId => new IdentificationWorkItem
            {
                JobId = job.Id,
                TrackId = trackId,
                Status = FileAnalysisStatus.Pending,
                Stage = "Pending"
            }));

            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(ct);
        _logger.LogInformation(
            "Identification job {JobId} queued for root {RootId} with {Count} items (mode {Mode}).",
            job.Id, request.LibraryRootId, trackIds.Count, selectionMode);

        return Accepted(new { jobId = job.Id, status = job.Status.ToString(), message = "Analysis job queued." });
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<IReadOnlyList<IdentificationJobDto>>> ListJobs(
        [FromQuery] Guid? rootId, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxPageSize);
        var query = _db.IdentificationJobs.AsNoTracking().Include(j => j.LibraryRoot).AsQueryable();
        if (rootId.HasValue)
        {
            query = query.Where(j => j.LibraryRootId == rootId.Value);
        }

        var jobs = await query
            .OrderByDescending(j => j.CreatedUtc)
            .Take(take)
            .ToListAsync(ct);

        return Ok(jobs.Select(j => MapJob(j, j.LibraryRoot?.Name)).ToList());
    }

    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<object>> GetJob(Guid id, CancellationToken ct)
    {
        var job = await _db.IdentificationJobs
            .AsNoTracking()
            .Include(j => j.LibraryRoot)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
        {
            return NotFound();
        }

        var errors = await _db.IdentificationErrors
            .AsNoTracking()
            .Where(e => e.JobId == id)
            .OrderByDescending(e => e.OccurredUtc)
            .Take(20)
            .Select(e => new { e.Subsystem, category = e.Category.ToString(), e.Retryable, e.Message, e.OccurredUtc })
            .ToListAsync(ct);

        return Ok(new { job = MapJob(job, job.LibraryRoot?.Name), errors });
    }

    [HttpPost("jobs/{id:guid}/pause")]
    public async Task<IActionResult> PauseJob(Guid id, CancellationToken ct)
    {
        var job = await _db.IdentificationJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
        {
            return NotFound();
        }

        if (job.Status != IdentificationJobStatus.Running && job.Status != IdentificationJobStatus.WaitingForNetwork)
        {
            return Conflict(new { message = "Only a running job can be paused." });
        }

        job.Status = IdentificationJobStatus.Paused;
        job.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { jobId = job.Id, status = job.Status.ToString() });
    }

    [HttpPost("jobs/{id:guid}/resume")]
    public async Task<IActionResult> ResumeJob(Guid id, CancellationToken ct)
    {
        var job = await _db.IdentificationJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
        {
            return NotFound();
        }

        if (job.Status != IdentificationJobStatus.Paused)
        {
            return Conflict(new { message = "Only a paused job can be resumed." });
        }

        job.Status = IdentificationJobStatus.Queued;
        job.CancelRequested = false;
        job.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { jobId = job.Id, status = job.Status.ToString() });
    }

    [HttpPost("jobs/{id:guid}/cancel")]
    public async Task<IActionResult> CancelJob(Guid id, CancellationToken ct)
    {
        var job = await _db.IdentificationJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
        {
            return NotFound();
        }

        if (job.Status is IdentificationJobStatus.Completed or IdentificationJobStatus.CompletedWithErrors
            or IdentificationJobStatus.Failed or IdentificationJobStatus.Cancelled)
        {
            return Conflict(new { message = "This job already finished and cannot be cancelled." });
        }

        job.Status = IdentificationJobStatus.Cancelled;
        job.CancelRequested = true;
        job.FinishedUtc = DateTime.UtcNow;
        job.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { jobId = job.Id, status = job.Status.ToString() });
    }

    [HttpPost("jobs/{id:guid}/retry-errors")]
    public async Task<IActionResult> RetryErrors(Guid id, CancellationToken ct)
    {
        // Requeueing a finished job is subject to the one-active-job rule.
        await JobAdmission.WaitAsync(ct);
        try
        {
            return await RetryErrorsAdmittedAsync(id, ct);
        }
        finally
        {
            JobAdmission.Release();
        }
    }

    private async Task<IActionResult> RetryErrorsAdmittedAsync(Guid id, CancellationToken ct)
    {
        var job = await _db.IdentificationJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null)
        {
            return NotFound();
        }

        if (job.Status == IdentificationJobStatus.Cancelled)
        {
            return Conflict(new { message = "A cancelled job cannot be retried. Queue a new analysis job instead." });
        }

        var requeue = job.Status is IdentificationJobStatus.Completed
            or IdentificationJobStatus.CompletedWithErrors or IdentificationJobStatus.Failed;
        if (requeue && await ActiveJobForRootAsync(job.LibraryRootId, excludeJobId: job.Id, ct) != null)
        {
            return Conflict(new { message = "Another analysis job is already active for this library root." });
        }

        var failed = await _db.IdentificationWorkItems
            .Where(w => w.JobId == id && w.Retryable &&
                        (w.Status == FileAnalysisStatus.RetryableError || w.Status == FileAnalysisStatus.PermanentError))
            .ToListAsync(ct);

        foreach (var item in failed)
        {
            item.Status = FileAnalysisStatus.Pending;
            item.Stage = "PendingRetry";
            item.Retryable = false;
            item.NextRetryUtc = null;
            item.UpdatedUtc = DateTime.UtcNow;
        }

        job.ErrorCount = Math.Max(0, job.ErrorCount - failed.Count);

        // A paused job keeps its state; the reset items run once it resumes.
        if (requeue && failed.Count > 0)
        {
            job.Status = IdentificationJobStatus.Queued;
            job.FinishedUtc = null;
        }

        job.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { jobId = job.Id, status = job.Status.ToString(), retried = failed.Count });
    }

    /// <summary>
    /// Pages analysis results by state, root, or group (plan 13.1).
    /// </summary>
    [HttpGet("results")]
    public async Task<ActionResult<IReadOnlyList<IdentificationWorkItemDto>>> ListResults(
        [FromQuery] Guid? jobId,
        [FromQuery] string? state,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxPageSize);
        var query = _db.IdentificationWorkItems.AsNoTracking().AsQueryable();
        if (jobId.HasValue)
        {
            query = query.Where(w => w.JobId == jobId.Value);
        }

        if (Enum.TryParse<FileAnalysisStatus>(state, ignoreCase: true, out var parsed))
        {
            query = query.Where(w => w.Status == parsed);
        }

        var items = await query
            .OrderByDescending(w => w.UpdatedUtc)
            .Take(take)
            .ToListAsync(ct);

        return Ok(items.Select(w => new IdentificationWorkItemDto(
            w.Id, w.JobId, w.TrackId, w.Status.ToString(), w.Stage,
            w.AttemptCount, w.LastError, w.NextRetryUtc)).ToList());
    }

    /// <summary>
    /// One work item with its scored candidates, for admin inspection (plan
    /// stage B). Paths are relative to the music folder; nothing secret or raw
    /// from a provider is returned.
    /// </summary>
    [HttpGet("items/{id:guid}")]
    public async Task<ActionResult<IdentificationItemDetailDto>> GetItem(Guid id, CancellationToken ct)
    {
        var item = await _db.IdentificationWorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (item == null)
        {
            return NotFound();
        }

        var track = await _db.Tracks.AsNoTracking()
            .Where(t => t.Id == item.TrackId)
            .Select(t => new { t.Title, Artist = t.PrimaryArtist != null ? t.PrimaryArtist.Name : null, Album = t.Album != null ? t.Album.Title : null })
            .FirstOrDefaultAsync(ct);
        var revision = item.FileRevisionId == null
            ? null
            : await _db.TrackFileRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == item.FileRevisionId, ct);
        var snapshot = revision == null
            ? null
            : await _db.OriginalTagSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.FileRevisionId == revision.Id, ct);
        var fingerprintDuration = revision == null
            ? null
            : await _db.AcousticFingerprints.AsNoTracking()
                .Where(f => f.FileRevisionId == revision.Id)
                .Select(f => f.DurationSeconds)
                .FirstOrDefaultAsync(ct);

        var candidates = await _db.RecordingCandidates.AsNoTracking()
            .Where(c => c.WorkItemId == id)
            .OrderByDescending(c => c.FinalScore)
            .Select(c => new RecordingCandidateDto(
                c.Provider, c.AcoustId, c.MusicBrainzRecordingId, c.Title, c.Artist,
                c.RawProviderScore, c.FinalScore, c.ConfidenceBand, c.ScoringVersion,
                c.ScoringBreakdownJson, c.Conflicts, c.Provenance))
            .ToListAsync(ct);

        return Ok(new IdentificationItemDetailDto(
            item.Id, item.JobId, item.TrackId, item.Status.ToString(), item.Stage, item.LastError,
            track?.Title, track?.Artist, track?.Album,
            revision?.RelativePath, revision?.Sha256, fingerprintDuration,
            snapshot?.Title, snapshot?.Artist, snapshot?.Album, snapshot?.MusicBrainzRecordingId,
            candidates));
    }

    /// <summary>
    /// Exact byte-duplicate report by SHA-256 (plan 10.1). Report only.
    /// </summary>
    [HttpGet("duplicates")]
    public async Task<ActionResult<IReadOnlyList<DuplicateGroupDto>>> ListDuplicates(
        [FromQuery] Guid? rootId, CancellationToken ct = default)
    {
        // Revisions are per-track history, so compare each track's latest
        // revision only, and count distinct tracks: several revisions of one
        // unchanged file are not duplicates of each other.
        var query = _db.TrackFileRevisions.AsNoTracking()
            .Where(r => r.Sha256 != null &&
                        r.ObservedUtc == _db.TrackFileRevisions
                            .Where(o => o.TrackId == r.TrackId)
                            .Max(o => o.ObservedUtc));
        if (rootId.HasValue)
        {
            query = query.Where(r => r.LibraryRootId == rootId.Value);
        }

        var groups = await query
            .Select(r => new { Sha256 = r.Sha256!, r.TrackId })
            .Distinct()
            .GroupBy(r => r.Sha256)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key)
            .Select(g => new { Sha256 = g.Key, TrackIds = g.Select(r => r.TrackId).ToList(), Count = g.Count() })
            .Take(MaxPageSize)
            .ToListAsync(ct);

        return Ok(groups.Select(g => new DuplicateGroupDto(g.Sha256, g.Count, g.TrackIds)).ToList());
    }

    /// <summary>
    /// Catalog apply placeholder. Returns 501 until milestone D lands with
    /// proposal revalidation, field overlays, and the audit journal.
    /// </summary>
    [HttpPost("proposals/apply-catalog")]
    public ActionResult ApplyCatalog()
    {
        return StatusCode(
            StatusCodes.Status501NotImplemented,
            new { message = "Catalog apply is not available in this build. Review and approval arrive in a later milestone." });
    }

    private Task<IdentificationJob?> ActiveJobForRootAsync(Guid rootId, Guid? excludeJobId, CancellationToken ct) =>
        _db.IdentificationJobs
            .Where(j => j.LibraryRootId == rootId &&
                        (excludeJobId == null || j.Id != excludeJobId) &&
                        (j.Status == IdentificationJobStatus.Queued ||
                         j.Status == IdentificationJobStatus.Running ||
                         j.Status == IdentificationJobStatus.WaitingForNetwork ||
                         j.Status == IdentificationJobStatus.Paused))
            .OrderByDescending(j => j.CreatedUtc)
            .FirstOrDefaultAsync(ct);

    private static IdentificationJobDto MapJob(IdentificationJob job, string? rootName) => new(
        job.Id, job.LibraryRootId, rootName, job.Status.ToString(), job.Stage,
        job.StartedUtc, job.FinishedUtc, job.TotalItems, job.HashedCount,
        job.FingerprintedCount, job.LookedUpCount, job.ResolvedCount,
        job.AmbiguousCount, job.ErrorCount, job.ProposalsReadyCount, job.ErrorSummary);
}

