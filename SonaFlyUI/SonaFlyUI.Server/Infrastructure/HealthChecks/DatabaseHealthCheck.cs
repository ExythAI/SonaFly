using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.HealthChecks;

/// <summary>
/// Reports whether the application can actually reach its database.
///
/// The previous health endpoint answered "Healthy" as long as the process could serve a
/// request, which is precisely the case a health check is least useful for: a container
/// whose database file is missing, locked, or on an unmounted volume answers that just
/// as cheerfully as a working one, and an orchestrator keeps sending it traffic.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly SonaFlyDbContext _db;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(SonaFlyDbContext db, ILogger<DatabaseHealthCheck> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // A real read, not just CanConnect: opening a SQLite file proves very little,
            // whereas reading a table proves the schema is present and readable.
            _ = await _db.Users.AsNoTracking().Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database is reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health check failed.");
            return HealthCheckResult.Unhealthy("Database is not reachable.", ex);
        }
    }
}
