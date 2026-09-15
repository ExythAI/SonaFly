using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identity;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// The server must always keep at least one administrator who can sign in. Checking that
/// before the mutation is not enough on its own: two administrators acting at the same
/// moment can each see the other still enabled and each decide they are safe to proceed.
///
/// These tests run two requests against the same database through genuinely independent
/// connections — a file-backed SQLite database rather than the shared in-memory one used
/// elsewhere, because a shared connection cannot contend for a lock with itself.
/// </summary>
public sealed class UserAdministrationConcurrencyTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"sonafly-admin-{Guid.NewGuid():N}.db");

    private readonly List<ServiceProvider> _providers = [];

    private ServiceProvider NewProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<SonaFlyDbContext>(o =>
            o.UseSqlite($"Data Source={_databasePath}"));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<SonaFlyDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IUserSecurityService, UserSecurityService>();

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private readonly ServiceProvider _seedProvider;
    private readonly ApplicationUser _adminA;
    private readonly ApplicationUser _adminB;

    public UserAdministrationConcurrencyTests()
    {
        _seedProvider = NewProvider();
        var db = _seedProvider.GetRequiredService<SonaFlyDbContext>();
        db.Database.EnsureCreated();

        IdentitySeeder.SeedRolesAsync(_seedProvider.GetRequiredService<RoleManager<ApplicationRole>>())
            .GetAwaiter().GetResult();

        _adminA = CreateAdmin("admin-a");
        _adminB = CreateAdmin("admin-b");
    }

    private ApplicationUser CreateAdmin(string name)
    {
        var users = _seedProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = name, Email = $"{name}@sonafly.local", DisplayName = name, IsEnabled = true
        };
        Assert.True(users.CreateAsync(user, "Correct-Horse-9").GetAwaiter().GetResult().Succeeded);
        Assert.True(users.AddToRoleAsync(user, IdentitySeeder.AdminRole).GetAwaiter().GetResult().Succeeded);
        return user;
    }

    /// <summary>A controller on its own connection, signed in as <paramref name="actingAs"/>.</summary>
    private UsersController ControllerOnItsOwnConnection(ApplicationUser actingAs)
    {
        var provider = NewProvider();
        return new UsersController(
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            provider.GetRequiredService<RoleManager<ApplicationRole>>(),
            provider.GetRequiredService<SonaFlyDbContext>(),
            provider.GetRequiredService<IUserSecurityService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, actingAs.Id.ToString())], "Test"))
                }
            }
        };
    }

    /// <summary>
    /// Runs both operations from the same instant. The barrier releases them before either
    /// has taken a lock, so whichever arrives second must observe the first one's result.
    /// </summary>
    private static async Task<(IActionResult First, IActionResult Second)> RaceAsync(
        Func<Task<IActionResult>> first, Func<Task<IActionResult>> second)
    {
        using var barrier = new Barrier(2);

        var a = Task.Run(async () => { barrier.SignalAndWait(); return await first(); });
        var b = Task.Run(async () => { barrier.SignalAndWait(); return await second(); });

        return (await a, await b);
    }

    private int EnabledAdminCount()
    {
        var provider = NewProvider();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        return users.GetUsersInRoleAsync(IdentitySeeder.AdminRole).GetAwaiter().GetResult()
            .Count(u => u.IsEnabled);
    }

    private static void AssertOneSucceededAndOneWasRefused(IActionResult first, IActionResult second)
    {
        IActionResult[] results = [first, second];

        Assert.Single(results, r => r is NoContentResult);
        // A refusal is either the invariant answering, or the loser of the lock being told
        // to try again. Both are clean: neither changed anything.
        Assert.Single(results, r => r is BadRequestObjectResult or ConflictObjectResult);
    }

    [Fact]
    public async Task TwoAdministratorsDisablingEachOther_CannotBothSucceed()
    {
        var byA = ControllerOnItsOwnConnection(_adminA);
        var byB = ControllerOnItsOwnConnection(_adminB);

        var (first, second) = await RaceAsync(
            () => byA.Disable(_adminB.Id),
            () => byB.Disable(_adminA.Id));

        AssertOneSucceededAndOneWasRefused(first, second);
        Assert.Equal(1, EnabledAdminCount());
    }

    [Fact]
    public async Task TwoConcurrentDemotions_CannotBothSucceed()
    {
        var byA = ControllerOnItsOwnConnection(_adminA);
        var byB = ControllerOnItsOwnConnection(_adminB);

        var (first, second) = await RaceAsync(
            () => byA.Update(_adminB.Id, new UpdateUserRequest(null, null, IdentitySeeder.UserRole)),
            () => byB.Update(_adminA.Id, new UpdateUserRequest(null, null, IdentitySeeder.UserRole)));

        AssertOneSucceededAndOneWasRefused(first, second);
        Assert.Equal(1, EnabledAdminCount());
    }

    [Fact]
    public async Task ConcurrentDeleteAndDemotion_CannotBothSucceed()
    {
        // Different routes, same invariant: the guard has to be shared across all of them.
        var byA = ControllerOnItsOwnConnection(_adminA);
        var byB = ControllerOnItsOwnConnection(_adminB);

        var (first, second) = await RaceAsync(
            () => byA.Delete(_adminB.Id),
            () => byB.Update(_adminA.Id, new UpdateUserRequest(null, null, IdentitySeeder.UserRole)));

        AssertOneSucceededAndOneWasRefused(first, second);
        Assert.Equal(1, EnabledAdminCount());
    }

    [Fact]
    public async Task ARefusedOperation_LeavesNoPartialChange()
    {
        var byA = ControllerOnItsOwnConnection(_adminA);
        var byB = ControllerOnItsOwnConnection(_adminB);

        // The refused Update also carries a profile edit; rolling back must undo that too,
        // rather than renaming an account it then declined to demote.
        var (_, second) = await RaceAsync(
            () => byA.Disable(_adminB.Id),
            () => byB.Update(_adminA.Id, new UpdateUserRequest(null, "Renamed", IdentitySeeder.UserRole)));

        if (second is not NoContentResult)
        {
            var provider = NewProvider();
            var reread = await provider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(_adminA.Id.ToString());
            Assert.Equal("admin-a", reread!.DisplayName);
        }
    }

    [Fact]
    public async Task WithASpareAdministrator_BothOperationsStillSucceed()
    {
        // The guard must not be so eager that ordinary concurrent administration fails.
        CreateAdmin("admin-c");
        CreateAdmin("admin-d");

        var byA = ControllerOnItsOwnConnection(_adminA);
        var byB = ControllerOnItsOwnConnection(_adminB);

        var (first, second) = await RaceAsync(
            () => byA.Disable(_adminB.Id),
            () => byB.Disable(_adminA.Id));

        Assert.IsType<NoContentResult>(first);
        Assert.IsType<NoContentResult>(second);
        Assert.Equal(2, EnabledAdminCount());
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_databasePath); } catch (IOException) { /* the OS still holds it; it is a temp file */ }
    }
}
