using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// The AcoustID key can be managed from the admin UI. The secret is encrypted
/// at rest with the Data Protection ring, a stored value wins over the
/// configuration file, and no API ever returns the plaintext.
/// </summary>
public sealed class ServerSettingsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly SonaFlyDbContext _db;
    private readonly IDataProtectionProvider _protection;

    public ServerSettingsTests()
    {
        _connection.Open();
        var collection = new ServiceCollection();
        collection.AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection));
        collection.AddDataProtection();
        _services = collection.BuildServiceProvider();
        _db = _services.GetRequiredService<SonaFlyDbContext>();
        _db.Database.EnsureCreated();
        _protection = _services.GetRequiredService<IDataProtectionProvider>();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private IServerSettingsService Service(IdentificationOptions? options = null) =>
        new ServerSettingsService(
            _db, _protection,
            Options.Create(options ?? new IdentificationOptions()),
            NullLogger<ServerSettingsService>.Instance);

    [Fact]
    public async Task AStoredSecretRoundTripsButIsEncryptedAtRest()
    {
        await Service().SetSecretAsync("k", "super-secret-value", Guid.NewGuid(), CancellationToken.None);

        var stored = await _db.ServerSettings.FirstAsync();
        Assert.NotNull(stored.EncryptedValue);
        Assert.DoesNotContain("super-secret-value", stored.EncryptedValue);

        Assert.Equal("super-secret-value", await Service().GetSecretAsync("k", CancellationToken.None));
    }

    [Fact]
    public async Task AStoredKeyWinsOverConfigurationUntilCleared()
    {
        var options = new IdentificationOptions { AcoustIdClientKey = "config-key" };
        var service = Service(options);

        Assert.Equal("config-key", await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));
        Assert.Equal(AcoustIdKeySource.Configuration, await service.GetAcoustIdKeySourceAsync(CancellationToken.None));

        await service.SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, "db-key", Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("db-key", await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));
        Assert.Equal(AcoustIdKeySource.Database, await service.GetAcoustIdKeySourceAsync(CancellationToken.None));

        await service.SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, null, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal("config-key", await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));
        Assert.Equal(AcoustIdKeySource.Configuration, await service.GetAcoustIdKeySourceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task NoKeyAnywhereMeansUnconfigured()
    {
        var service = Service();
        Assert.Null(await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));
        Assert.Equal(AcoustIdKeySource.None, await service.GetAcoustIdKeySourceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ClearingAnAbsentKeyIsHarmless()
    {
        await Service().SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, "   ", null, CancellationToken.None);
        Assert.Equal(0, await _db.ServerSettings.CountAsync());
    }

    [Fact]
    public async Task AnOversizedValueIsRejectedBeforeStorage()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().SetSecretAsync(
            "k", string.Concat(Enumerable.Repeat("x", ServerSettingsService.MaxSecretLength + 1)), null, CancellationToken.None));
        Assert.Equal(0, await _db.ServerSettings.CountAsync());
    }

    [Fact]
    public async Task TheSettingsSurfaceNeverReturnsPlaintext()
    {
        await Service().SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, "the-actual-key", null, CancellationToken.None);

        var controller = new ServerSettingsController(
            Service(), NullLogger<ServerSettingsController>.Instance);
        var result = await controller.GetIdentificationKeys(CancellationToken.None);

        var dto = Assert.IsType<OkObjectResult>(result.Result).Value as IdentificationKeyStateDto;
        Assert.NotNull(dto);
        Assert.True(dto.Configured);
        Assert.Equal("Database", dto.Source);
        Assert.DoesNotContain("the-actual-key", dto.ToString());
    }

    [Fact]
    public async Task SavingThenClearingFlowsThroughTheController()
    {
        var service = Service();
        var controller = new ServerSettingsController(service, NullLogger<ServerSettingsController>.Instance);

        var saved = await controller.SetAcoustIdKey(new SetAcoustIdKeyRequest("fresh-key"), CancellationToken.None);
        Assert.True((Assert.IsType<OkObjectResult>(saved.Result).Value as IdentificationKeyStateDto)!.Configured);
        Assert.Equal("fresh-key", await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));

        var rejected = await controller.SetAcoustIdKey(new SetAcoustIdKeyRequest("  "), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(rejected.Result);

        var cleared = await controller.ClearAcoustIdKey(CancellationToken.None);
        var state = Assert.IsType<OkObjectResult>(cleared.Result).Value as IdentificationKeyStateDto;
        Assert.NotNull(state);
        Assert.False(state.Configured);
        Assert.Null(await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));
    }

    [Fact]
    public void BothSettingsControllersStayAdminOnly()
    {
        foreach (var controller in new[] { typeof(ServerSettingsController), typeof(IdentificationController) })
        {
            var authorize = controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .FirstOrDefault();
            Assert.NotNull(authorize);
            Assert.Equal("Admin", authorize.Roles);
        }
    }
}

