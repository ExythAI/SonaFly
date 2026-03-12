using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.BackgroundServices;

public class LibraryScanBackgroundService : BackgroundService
{
    private readonly IScanQueue _scanQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LibraryScanBackgroundService> _logger;

    public LibraryScanBackgroundService(
        IScanQueue scanQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<LibraryScanBackgroundService> logger)
    {
        _scanQueue = scanQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Clean up stale scan jobs from previous server runs
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();
            var staleJobs = await db.ScanJobs
                .Where(j => j.Status == ScanStatus.Running || j.Status == ScanStatus.Queued)
                .ToListAsync(stoppingToken);
            if (staleJobs.Count > 0)
            {
                foreach (var job in staleJobs)
                {
                    job.Status = ScanStatus.Failed;
                    job.CompletedUtc = DateTime.UtcNow;
                }
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogWarning("Reset {Count} stale scan job(s) from previous server run.", staleJobs.Count);
            }
        }

        _logger.LogInformation("Library scan background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = await _scanQueue.DequeueAsync(stoppingToken);
                _logger.LogInformation("Starting {ScanType} scan for library root {LibraryRootId}",
                    request.FullScan ? "full" : "incremental", request.LibraryRootId);

                using var scope = _scopeFactory.CreateScope();
                var indexService = scope.ServiceProvider.GetRequiredService<ILibraryIndexService>();
                var result = await indexService.ScanLibraryRootAsync(request.LibraryRootId, request.FullScan, stoppingToken);

                _logger.LogInformation(
                    "Scan completed for library root {LibraryRootId}: {Status} — {FilesScanned} scanned, {FilesAdded} added, {FilesUpdated} updated, {FilesMissing} missing, {Errors} errors",
                    request.LibraryRootId, result.Status, result.FilesScanned, result.FilesAdded,
                    result.FilesUpdated, result.FilesMissing, result.ErrorsCount);
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
}
