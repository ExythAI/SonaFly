using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.BackgroundServices;

/// <summary>Settings under <c>SonaFly:Scan</c>.</summary>
public sealed class ScanScheduleOptions
{
    public const string SectionName = "SonaFly:Scan";

    /// <summary>
    /// How long after a library root was last scanned it is scanned again, so new music
    /// appears without anyone pressing Scan. Zero turns automatic scans off.
    /// </summary>
    public int AutoScanIntervalHours { get; set; } = 24;
}

/// <summary>
/// Queues an incremental scan for each library root once it is due.
/// </summary>
public sealed class ScheduledScanService(
    IServiceScopeFactory scopes,
    IScanQueue queue,
    IOptions<ScanScheduleOptions> options,
    ILogger<ScheduledScanService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = options.Value.AutoScanIntervalHours;
        if (hours <= 0) return;

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (stoppingToken.IsCancellationRequested == false)
            {
                try
                {
                    await QueueDueScansAsync(TimeSpan.FromHours(hours), DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Could not queue scheduled scans.");
                }

                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>Queues a scan for every root that is due. Returns how many were queued.</summary>
    public async Task<int> QueueDueScansAsync(TimeSpan interval, DateTime nowUtc, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();

        var roots = await db.LibraryRoots.Where(r => r.IsEnabled).ToListAsync(ct);
        var queued = 0;

        foreach (var root in roots)
        {
            // Only a root that has been scanned before is rescanned on a schedule. A new
            // root, or a library that was just purged, waits for someone to start the
            // first scan: a purge is often followed by changes that should come first.
            if (root.LastScanCompletedUtc is not { } last || nowUtc - last < interval) continue;

            var pending = await db.ScanJobs.AnyAsync(j => j.LibraryRootId == root.Id &&
                (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running), ct);
            if (pending) continue;

            var previousStatus = root.LastScanStatus;
            var job = new ScanJob { LibraryRootId = root.Id, Status = ScanStatus.Queued };
            db.ScanJobs.Add(job);
            root.LastScanStatus = ScanStatus.Queued;
            root.LastScanError = null;
            await db.SaveChangesAsync(ct);

            if (queue.TryEnqueue(new ScanRequest(root.Id, FullScan: false, job.Id)))
            {
                queued++;
                logger.LogInformation("Queued the scheduled scan of library root {Root}.", root.Name);
                continue;
            }

            // The worker is saturated. Withdraw the job rather than leave a Queued row
            // nothing will pick up; the next check tries again.
            db.ScanJobs.Remove(job);
            root.LastScanStatus = previousStatus;
            await db.SaveChangesAsync(ct);
        }

        return queued;
    }
}
