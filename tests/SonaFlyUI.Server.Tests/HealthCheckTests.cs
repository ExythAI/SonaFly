using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.HealthChecks;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// The container's health probe is only worth having if it can report unhealthy. The
/// previous endpoint answered "Healthy" whenever the process was alive, which a
/// container with an unreachable database also does.
/// </summary>
public sealed class HealthCheckTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SonaFlyDbContext _db;

    public HealthCheckTests()
    {
        _connection.Open();
        _db = new SonaFlyDbContext(new DbContextOptionsBuilder<SonaFlyDbContext>()
            .UseSqlite(_connection).Options);
    }

    private DatabaseHealthCheck Check() =>
        new(_db, NullLogger<DatabaseHealthCheck>.Instance);

    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration("database", _ => null!, HealthStatus.Unhealthy, tags: null)
    };

    [Fact]
    public async Task AReachableDatabase_IsHealthy()
    {
        await _db.Database.EnsureCreatedAsync();

        var result = await Check().CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task AMissingSchema_IsUnhealthy()
    {
        // The file opens but the tables are not there — the case a bare "can I connect?"
        // probe reports as healthy.
        var result = await Check().CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task AnUnreachableDatabase_IsUnhealthy()
    {
        await _db.Database.EnsureCreatedAsync();
        // Closing the in-memory connection discards the database entirely.
        await _connection.CloseAsync();

        var result = await Check().CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CancellationIsPropagated_NotReportedAsUnhealthy()
    {
        await _db.Database.EnsureCreatedAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // A probe that was cancelled says nothing about the database's health, so it must
        // not be recorded as a failure.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Check().CheckHealthAsync(Context(), cancelled.Token));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
