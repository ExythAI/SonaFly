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
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Entities.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identification;
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

        var controller = Controller(Service(), new KeyChecker(accepts: true));
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
        var controller = Controller(service, new KeyChecker(accepts: true));

        var saved = await controller.SetAcoustIdKey(new SetAcoustIdKeyRequest("fresh-key"), CancellationToken.None);
        Assert.True((Assert.IsType<OkObjectResult>(saved.Result).Value as IdentificationKeySaveResultDto)!.Configured);
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
    public async Task AKeyAcoustIdRejectsIsNotSavedAndTheMessageNamesTheRightKind()
    {
        var service = Service();
        await service.SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, "previous-good-key", null, CancellationToken.None);

        var result = await Controller(service, new KeyChecker(accepts: false))
            .SetAcoustIdKey(new SetAcoustIdKeyRequest("user-api-key"), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var message = System.Text.Json.JsonSerializer.Serialize(bad.Value);
        Assert.Contains("new-application", message);
        Assert.Contains("not saved", message);
        Assert.Equal("previous-good-key", await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AKeyIsSavedUnverifiedWhenAcoustIdCannotBeReached()
    {
        var service = Service();

        var result = await Controller(service, new KeyChecker(unreachable: true))
            .SetAcoustIdKey(new SetAcoustIdKeyRequest("some-key"), CancellationToken.None);

        var dto = Assert.IsType<OkObjectResult>(result.Result).Value as IdentificationKeySaveResultDto;
        Assert.NotNull(dto);
        Assert.False(dto.Verified);
        Assert.Contains("could not be checked", dto.Message);
        Assert.Equal("some-key", await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AnAcceptedKeyResumesOnlyJobsPausedForTheKey()
    {
        var root = new LibraryRoot { Name = "Music", Path = "/music" };
        _db.LibraryRoots.Add(root);
        var pausedForKey = new IdentificationJob
        {
            LibraryRootId = root.Id,
            Status = IdentificationJobStatus.Paused,
            Stage = IdentificationJobRunner.AdministratorPauseStage,
            ErrorSummary = "AcoustID rejected the key."
        };
        var pausedByAdmin = new IdentificationJob { LibraryRootId = root.Id, Status = IdentificationJobStatus.Paused, Stage = "Analysing" };
        _db.IdentificationJobs.AddRange(pausedForKey, pausedByAdmin);
        await _db.SaveChangesAsync();

        var result = await Controller(Service(), new KeyChecker(accepts: true))
            .SetAcoustIdKey(new SetAcoustIdKeyRequest("application-key"), CancellationToken.None);

        var dto = Assert.IsType<OkObjectResult>(result.Result).Value as IdentificationKeySaveResultDto;
        Assert.True(dto!.Verified);
        Assert.Contains("1 paused identification job was resumed", dto.Message);

        var jobs = await _db.IdentificationJobs.AsNoTracking().ToDictionaryAsync(j => j.Id);
        Assert.Equal(IdentificationJobStatus.Queued, jobs[pausedForKey.Id].Status);
        Assert.Null(jobs[pausedForKey.Id].ErrorSummary);
        Assert.Equal(IdentificationJobStatus.Paused, jobs[pausedByAdmin.Id].Status);
    }

    [Fact]
    public async Task TestingChecksTheEnteredKeyOrElseTheSavedOne()
    {
        var service = Service();
        var checker = new KeyChecker(accepts: true);
        var controller = Controller(service, checker);

        var nothingSaved = await controller.TestAcoustIdKey(new TestAcoustIdKeyRequest(null), CancellationToken.None);
        Assert.False((Assert.IsType<OkObjectResult>(nothingSaved.Result).Value as AcoustIdKeyTestResultDto)!.Checked);
        Assert.Empty(checker.Tested);

        await service.SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, "saved-key", null, CancellationToken.None);
        var saved = await controller.TestAcoustIdKey(null, CancellationToken.None);
        var savedResult = (Assert.IsType<OkObjectResult>(saved.Result).Value as AcoustIdKeyTestResultDto)!;
        Assert.True(savedResult.Accepted);
        Assert.Contains("saved key", savedResult.Message);

        await controller.TestAcoustIdKey(new TestAcoustIdKeyRequest("  typed-key "), CancellationToken.None);
        Assert.Equal(["saved-key", "typed-key"], checker.Tested);
        // Testing never saves.
        Assert.Equal("saved-key", await service.GetEffectiveAcoustIdKeyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResumingAJobClearsItsPauseReason()
    {
        var root = new LibraryRoot { Name = "Music", Path = "/music" };
        _db.LibraryRoots.Add(root);
        var job = new IdentificationJob
        {
            LibraryRootId = root.Id,
            Status = IdentificationJobStatus.Paused,
            Stage = IdentificationJobRunner.AdministratorPauseStage,
            ErrorSummary = "AcoustID rejected the key."
        };
        _db.IdentificationJobs.Add(job);
        await _db.SaveChangesAsync();

        var controller = new IdentificationController(_db, Options.Create(new IdentificationOptions { Enabled = true }),
            Service(), NullLogger<IdentificationController>.Instance);
        await controller.ResumeJob(job.Id, CancellationToken.None);

        var resumed = await _db.IdentificationJobs.AsNoTracking().SingleAsync();
        Assert.Equal(IdentificationJobStatus.Queued, resumed.Status);
        Assert.Null(resumed.ErrorSummary);
        Assert.Equal("Queued", resumed.Stage);
    }

    private ServerSettingsController Controller(IServerSettingsService service, IAcoustIdClient checker) =>
        new(service, checker, _db, NullLogger<ServerSettingsController>.Instance);

    private sealed class KeyChecker(bool accepts = true, bool unreachable = false) : IAcoustIdClient
    {
        public List<string> Tested { get; } = [];

        public Task<string> LookupJsonAsync(string apiKey, string fingerprint, int durationSeconds, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> IsKeyAcceptedAsync(string apiKey, CancellationToken ct)
        {
            Tested.Add(apiKey);
            return unreachable
                ? Task.FromException<bool>(new IdentificationStepException(IdentificationErrorCategory.AcoustIdLookup, retryable: true, "AcoustID could not be reached."))
                : Task.FromResult(accepts);
        }
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

