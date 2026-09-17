using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Drives identification jobs from persisted state (upgrade plan 12). Work is
/// leased in small batches and every stage is checkpointed in SQLite, so a
/// restart resumes where it stopped. The runner never holds the library gate
/// across lookups; it simply declines to start work while maintenance is
/// pending and waits while a scan of the job's folder is queued or running.
/// </summary>
public sealed class IdentificationJobRunner
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LibraryMaintenanceGate _gate;
    private readonly IdentificationOptions _options;
    private readonly ILogger<IdentificationJobRunner> _logger;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public IdentificationJobRunner(
        IServiceScopeFactory scopeFactory,
        LibraryMaintenanceGate gate,
        IOptions<IdentificationOptions> options,
        ILogger<IdentificationJobRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _gate = gate;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Releases leases left by a previous process. SonaFly runs one server
    /// process, so any lease at startup belongs to work that died with it.
    /// </summary>
    public async Task ReclaimLeasesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();

        var items = await db.IdentificationWorkItems
            .Where(w => w.LeaseOwner != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.LeaseOwner, (string?)null)
                .SetProperty(w => w.LeaseExpiryUtc, (DateTime?)null), ct);
        if (items > 0)
        {
            _logger.LogInformation("Resuming identification: released {Count} work item lease(s) from the previous run.", items);
        }
    }

    /// <summary>
    /// Advances the oldest runnable job by one batch. Returns true when it made
    /// progress or changed a job's state, so the caller can loop immediately.
    /// </summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        if (_options.Enabled == false)
        {
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<IServerSettingsService>();
        var now = DateTime.UtcNow;

        var job = await db.IdentificationJobs.AsNoTracking()
            .Where(j => (j.Status == IdentificationJobStatus.Queued ||
                         j.Status == IdentificationJobStatus.Running ||
                         j.Status == IdentificationJobStatus.WaitingForNetwork) &&
                        j.CancelRequested == false &&
                        (j.NextRetryUtc == null || j.NextRetryUtc <= now))
            .OrderBy(j => j.CreatedUtc)
            .FirstOrDefaultAsync(ct);
        if (job == null)
        {
            return false;
        }

        if (_gate.MaintenancePending || _gate.IsRootPendingDeletion(job.LibraryRootId))
        {
            return false;
        }

        if (await IdentificationScanGuard.IsScanActiveAsync(db, job.LibraryRootId, ct))
        {
            // Tracks the scan adds or removes would otherwise be missed or dangle.
            await db.IdentificationJobs
                .Where(j => j.Id == job.Id && j.Stage != IdentificationScanGuard.WaitingStage)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Stage, IdentificationScanGuard.WaitingStage), ct);
            return false;
        }

        var capabilities = new IdentificationCapabilities(_options.HasFpcalc, await settings.GetEffectiveAcoustIdKeyAsync(ct));

        var started = await db.IdentificationJobs
            .Where(j => j.Id == job.Id && j.CancelRequested == false &&
                        (j.Status == IdentificationJobStatus.Queued ||
                         j.Status == IdentificationJobStatus.Running ||
                         j.Status == IdentificationJobStatus.WaitingForNetwork))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, IdentificationJobStatus.Running)
                .SetProperty(j => j.StartedUtc, j => j.StartedUtc ?? now)
                .SetProperty(j => j.NextRetryUtc, (DateTime?)null)
                .SetProperty(j => j.Stage, "Analysing")
                .SetProperty(j => j.ModifiedUtc, now), ct);
        if (started == 0)
        {
            return false;
        }

        var actionable = capabilities.ActionableStatuses.ToList();
        var batch = await db.IdentificationWorkItems
            .Where(w => w.JobId == job.Id &&
                        (actionable.Contains(w.Status) ||
                         (w.Status == FileAnalysisStatus.RetryableError && w.NextRetryUtc != null && w.NextRetryUtc <= now)) &&
                        (w.LeaseExpiryUtc == null || w.LeaseExpiryUtc < now))
            .OrderBy(w => w.CreatedUtc)
            .Select(w => w.Id)
            .Take(Math.Max(1, _options.MaxLocalWorkers) * 4)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            await SettleAsync(db, job.Id, ct);
            return true;
        }

        await db.IdentificationWorkItems
            .Where(w => batch.Contains(w.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.LeaseOwner, _owner)
                .SetProperty(w => w.LeaseExpiryUtc, now + LeaseDuration), ct);

        var preempted = 0;
        IdentificationStepException? administratorFailure = null;
        try
        {
            await Parallel.ForEachAsync(batch,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.MaxLocalWorkers), CancellationToken = ct },
                async (workItemId, token) =>
                {
                    if (Volatile.Read(ref preempted) != 0 || Volatile.Read(ref administratorFailure) != null)
                    {
                        return;
                    }

                    using var itemScope = _scopeFactory.CreateScope();
                    var processor = itemScope.ServiceProvider.GetRequiredService<IdentificationItemProcessor>();
                    try
                    {
                        var outcome = await processor.ProcessAsync(workItemId, capabilities, token);
                        if (outcome.Failure?.RequiresAdministrator == true)
                        {
                            Interlocked.CompareExchange(ref administratorFailure, outcome.Failure, null);
                        }
                    }
                    catch (IdentificationPreemptedException)
                    {
                        Interlocked.Exchange(ref preempted, 1);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Identification work item {WorkItemId} failed unexpectedly.", workItemId);
                        using var failureScope = _scopeFactory.CreateScope();
                        await failureScope.ServiceProvider.GetRequiredService<IdentificationItemProcessor>()
                            .MarkUnexpectedFailureAsync(workItemId, ex, CancellationToken.None);
                    }
                });
        }
        finally
        {
            await db.IdentificationWorkItems
                .Where(w => batch.Contains(w.Id) && w.LeaseOwner == _owner)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.LeaseOwner, (string?)null)
                    .SetProperty(w => w.LeaseExpiryUtc, (DateTime?)null), CancellationToken.None);
        }

        if (preempted != 0)
        {
            return false;
        }

        if (administratorFailure != null)
        {
            await db.IdentificationJobs
                .Where(j => j.Id == job.Id && j.Status == IdentificationJobStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, IdentificationJobStatus.Paused)
                    .SetProperty(j => j.Stage, "Paused: administrator action needed")
                    .SetProperty(j => j.ErrorSummary, administratorFailure.Message)
                    .SetProperty(j => j.ModifiedUtc, DateTime.UtcNow), CancellationToken.None);
            _logger.LogWarning("Identification job {JobId} paused: {Reason}", job.Id, administratorFailure.Message);
        }

        await RefreshCountersAsync(db, job.Id, CancellationToken.None);
        return true;
    }

    /// <summary>
    /// Closes out a job with nothing runnable right now: it waits for scheduled
    /// retries, or finishes as completed or completed-with-errors.
    /// </summary>
    private async Task SettleAsync(SonaFlyDbContext db, Guid jobId, CancellationToken ct)
    {
        await RefreshCountersAsync(db, jobId, ct);
        var now = DateTime.UtcNow;

        var nextRetry = await db.IdentificationWorkItems
            .Where(w => w.JobId == jobId && w.Status == FileAnalysisStatus.RetryableError && w.NextRetryUtc != null)
            .MinAsync(w => w.NextRetryUtc, ct);
        if (nextRetry != null)
        {
            await db.IdentificationJobs
                .Where(j => j.Id == jobId && j.Status == IdentificationJobStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, IdentificationJobStatus.WaitingForNetwork)
                    .SetProperty(j => j.NextRetryUtc, nextRetry)
                    .SetProperty(j => j.Stage, "Waiting to retry failed lookups")
                    .SetProperty(j => j.ModifiedUtc, now), ct);
            return;
        }

        var errors = await db.IdentificationWorkItems.CountAsync(w => w.JobId == jobId &&
            (w.Status == FileAnalysisStatus.RetryableError || w.Status == FileAnalysisStatus.PermanentError), ct);
        var skipped = await db.IdentificationWorkItems.CountAsync(w => w.JobId == jobId && w.Status == FileAnalysisStatus.Skipped, ct);

        var stage = skipped == 0 ? "Finished" : $"Finished ({skipped} skipped: no longer in the library)";
        var finished = await db.IdentificationJobs
            .Where(j => j.Id == jobId && j.Status == IdentificationJobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, errors == 0 ? IdentificationJobStatus.Completed : IdentificationJobStatus.CompletedWithErrors)
                .SetProperty(j => j.FinishedUtc, now)
                .SetProperty(j => j.Stage, stage)
                .SetProperty(j => j.ModifiedUtc, now), ct);
        if (finished > 0)
        {
            _logger.LogInformation("Identification job {JobId} finished with {Errors} error(s) and {Skipped} skipped item(s).", jobId, errors, skipped);
        }
    }

    /// <summary>Progress counts come from persisted work state, so they are right after a restart (plan 12.5).</summary>
    private static async Task RefreshCountersAsync(SonaFlyDbContext db, Guid jobId, CancellationToken ct)
    {
        var items = db.IdentificationWorkItems.Where(w => w.JobId == jobId);

        var byStatus = await items
            .GroupBy(w => w.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Count(params FileAnalysisStatus[] statuses) => byStatus.Where(x => statuses.Contains(x.Status)).Sum(x => x.Count);

        var hashed = await items.CountAsync(w => w.FileRevisionId != null, ct);
        var fingerprinted = await items.CountAsync(w => w.FileRevisionId != null &&
            db.AcousticFingerprints.Any(f => f.FileRevisionId == w.FileRevisionId && f.Status == FileAnalysisStatus.Fingerprinted), ct);

        var lookedUp = Count(FileAnalysisStatus.Resolved, FileAnalysisStatus.Ambiguous, FileAnalysisStatus.Unidentified);
        var resolved = Count(FileAnalysisStatus.Resolved);
        var ambiguous = Count(FileAnalysisStatus.Ambiguous);
        var errors = Count(FileAnalysisStatus.RetryableError, FileAnalysisStatus.PermanentError);

        await db.IdentificationJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.HashedCount, hashed)
                .SetProperty(j => j.FingerprintedCount, fingerprinted)
                .SetProperty(j => j.LookedUpCount, lookedUp)
                .SetProperty(j => j.ResolvedCount, resolved)
                .SetProperty(j => j.AmbiguousCount, ambiguous)
                .SetProperty(j => j.ErrorCount, errors), ct);
    }
}

/// <summary>Coordination between identification and library scans.</summary>
public static class IdentificationScanGuard
{
    public const string WaitingStage = "Waiting for the folder scan to finish";

    public static Task<bool> IsScanActiveAsync(SonaFlyDbContext db, Guid libraryRootId, CancellationToken ct) =>
        db.ScanJobs.AnyAsync(s => s.LibraryRootId == libraryRootId &&
                                  (s.Status == ScanStatus.Queued || s.Status == ScanStatus.Running), ct);
}
