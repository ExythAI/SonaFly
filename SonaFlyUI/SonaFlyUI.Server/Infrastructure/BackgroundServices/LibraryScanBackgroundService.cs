using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Infrastructure.BackgroundServices;

public class LibraryScanBackgroundService : BackgroundService
{
    private readonly IScanQueue _scanQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LibraryMaintenanceGate _gate;
    private readonly ILogger<LibraryScanBackgroundService> _logger;

    public LibraryScanBackgroundService(
        IScanQueue scanQueue,
        IServiceScopeFactory scopeFactory,
        LibraryMaintenanceGate gate,
        ILogger<LibraryScanBackgroundService> logger)
    {
        _scanQueue = scanQueue;
        _scopeFactory = scopeFactory;
        _gate = gate;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStaleStateAsync(stoppingToken);

        _logger.LogInformation("Library scan background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = await _scanQueue.DequeueAsync(stoppingToken);
                await RunScanAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in library scan background service");
                await Task.Delay(5000, stoppingToken); // Prevent tight error loops
            }
        }

        _logger.LogInformation("Library scan background service stopped.");
    }

    private async Task RunScanAsync(ScanRequest request, CancellationToken stoppingToken)
    {
        // Take the library slot. Maintenance (a purge) cancels the lease token, so a scan can
        // never repopulate data an admin is in the middle of deleting (backlog N16).
        using var lease = await _gate.AcquireForScanAsync(stoppingToken);

        if (_gate.MaintenancePending || lease.Token.IsCancellationRequested)
        {
            await AbandonJobAsync(request.ScanJobId,
                "Scan abandoned: library maintenance is in progress.", stoppingToken);
            return;
        }

        _logger.LogInformation("Starting {ScanType} scan for library root {LibraryRootId}",
            request.FullScan ? "full" : "incremental", request.LibraryRootId);

        using var scope = _scopeFactory.CreateScope();
        var indexService = scope.ServiceProvider.GetRequiredService<ILibraryIndexService>();

        ScanJobDto result;
        try
        {
            result = await indexService.ScanLibraryRootAsync(request, lease.Token);
        }
        catch (Exception ex)
        {
            // The index service records its own failures, but anything thrown before it gets
            // that far — a root deleted between queueing and running, say — would otherwise
            // leave the job and its root stuck at Queued/Running (backlog N17).
            _logger.LogError(ex, "Scan for library root {LibraryRootId} failed before it could record its own outcome.",
                request.LibraryRootId);
            await FailJobAsync(request.ScanJobId, $"Scan could not start: {ex.Message}");
            return;
        }

        _logger.LogInformation(
            "Scan completed for library root {LibraryRootId}: {Status} — {FilesScanned} scanned, {FilesAdded} added, {FilesUpdated} updated, {FilesMissing} missing, {FilesUnverified} unverified, {Errors} errors",
            request.LibraryRootId, result.Status, result.FilesScanned, result.FilesAdded,
            result.FilesUpdated, result.FilesMissing, result.FilesUnverified, result.ErrorsCount);
    }

    /// <summary>
    /// Marks a persisted job as not-run without pretending a scan happened.
    /// </summary>
    private Task AbandonJobAsync(Guid scanJobId, string reason, CancellationToken ct) =>
        SettleJobAsync(scanJobId, ScanStatus.Cancelled, reason, ct);

    private Task FailJobAsync(Guid scanJobId, string reason) =>
        SettleJobAsync(scanJobId, ScanStatus.Failed, reason, CancellationToken.None);

    private async Task SettleJobAsync(Guid scanJobId, ScanStatus status, string reason, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();

        var job = await db.ScanJobs.Include(j => j.LibraryRoot).FirstOrDefaultAsync(j => j.Id == scanJobId, ct);
        if (job == null) return;

        job.Status = status;
        job.CompletedUtc = DateTime.UtcNow;
        job.ErrorSummary = reason;

        if (job.LibraryRoot != null)
        {
            job.LibraryRoot.LastScanStatus = status;
            job.LibraryRoot.LastScanError = reason;
            job.LibraryRoot.LastScanCompletedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogWarning("Scan job {ScanJobId} settled as {Status}: {Reason}", scanJobId, status, reason);
    }

    /// <summary>
    /// Reconciles state left behind by a previous process.
    /// <para>
    /// Queued and running jobs do not resume: the in-memory queue did not survive the restart,
    /// so they are explicitly failed rather than left pending forever. Crucially the owning
    /// LibraryRoot is reconciled too — leaving <c>LastScanStatus = Running</c> behind is what
    /// leaves the web Scan button permanently disabled (backlog N17).
    /// </para>
    /// </summary>
    private async Task RecoverStaleStateAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();

        const string reason = "Interrupted by a server restart; queued scans are not resumed. Start a new scan.";

        var staleJobs = await db.ScanJobs
            .Where(j => j.Status == ScanStatus.Running || j.Status == ScanStatus.Queued)
            .ToListAsync(stoppingToken);

        foreach (var job in staleJobs)
        {
            job.Status = ScanStatus.Failed;
            job.CompletedUtc = DateTime.UtcNow;
            job.ErrorSummary = reason;
        }

        var staleRoots = await db.LibraryRoots
            .Where(lr => lr.LastScanStatus == ScanStatus.Running || lr.LastScanStatus == ScanStatus.Queued)
            .ToListAsync(stoppingToken);

        foreach (var root in staleRoots)
        {
            root.LastScanStatus = ScanStatus.Failed;
            root.LastScanCompletedUtc = DateTime.UtcNow;
            root.LastScanError = reason;
        }

        if (staleJobs.Count > 0 || staleRoots.Count > 0)
        {
            await db.SaveChangesAsync(stoppingToken);
            _logger.LogWarning(
                "Reset {Jobs} stale scan job(s) and {Roots} stuck library root status(es) from a previous server run.",
                staleJobs.Count, staleRoots.Count);
        }
    }
}
