using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
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
/// Exercises UsersController directly, rather than trusting the admin UI to only send
/// sensible requests. The invariants that matter: a role change never leaves an account
/// roleless, a failed Identity operation never reports success, and the server always
/// keeps at least one administrator who can sign in.
/// </summary>
public sealed class UserAdministrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly ApplicationUser _admin;

    public UserAdministrationTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection));
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

        _services = services.BuildServiceProvider();
        Db.Database.EnsureCreated();

        IdentitySeeder.SeedRolesAsync(Roles).GetAwaiter().GetResult();
        _admin = CreateUserAsync("admin", IdentitySeeder.AdminRole).GetAwaiter().GetResult();
    }

    private SonaFlyDbContext Db => _services.GetRequiredService<SonaFlyDbContext>();
    private UserManager<ApplicationUser> Users => _services.GetRequiredService<UserManager<ApplicationUser>>();
    private RoleManager<ApplicationRole> Roles => _services.GetRequiredService<RoleManager<ApplicationRole>>();

    private async Task<ApplicationUser> CreateUserAsync(string name, string role, bool enabled = true)
    {
        var user = new ApplicationUser
        {
            UserName = name,
            Email = $"{name}@sonafly.local",
            DisplayName = name,
            IsEnabled = enabled
        };
        Assert.True((await Users.CreateAsync(user, "Correct-Horse-9")).Succeeded);
        Assert.True((await Users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    /// <summary>A controller acting as <paramref name="actingAs"/> (the signed-in admin).</summary>
    private UsersController Controller(ApplicationUser? actingAs = null)
    {
        var controller = new UsersController(
            Users, Roles, Db, _services.GetRequiredService<IUserSecurityService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, (actingAs ?? _admin).Id.ToString())], "Test"))
                }
            }
        };
        return controller;
    }

    private static void AssertBadRequest(IActionResult result) => Assert.IsType<BadRequestObjectResult>(result);

    private static void AssertBadRequest<T>(ActionResult<T> result) =>
        Assert.IsType<BadRequestObjectResult>(result.Result);

    // ── Role validation ──

    [Fact]
    public async Task UpdateWithAnUnknownRole_IsRejectedAndLeavesRolesIntact()
    {
        var user = await CreateUserAsync("listener", IdentitySeeder.UserRole);

        var result = await Controller().Update(user.Id, new UpdateUserRequest(null, null, "Superuser"));

        AssertBadRequest(result);
        // The old behaviour removed every role first, leaving the account with none.
        Assert.Equal([IdentitySeeder.UserRole], await Users.GetRolesAsync(user));
    }

    [Fact]
    public async Task UpdateWithAnUnknownRole_DoesNotApplyTheProfileChangesEither()
    {
        var user = await CreateUserAsync("listener", IdentitySeeder.UserRole);

        await Controller().Update(user.Id, new UpdateUserRequest("new@sonafly.local", "New Name", "Superuser"));

        var reloaded = await Users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(reloaded);
        Assert.Equal("listener", reloaded.DisplayName);
        Assert.Equal("listener@sonafly.local", reloaded.Email);
    }

    [Fact]
    public async Task CreateWithAnUnknownRole_CreatesNoAccount()
    {
        var result = await Controller().Create(
            new CreateUserRequest("newbie", "newbie@sonafly.local", "Newbie", "Correct-Horse-9", "Superuser"));

        AssertBadRequest(result);
        Assert.Null(await Users.FindByNameAsync("newbie"));
    }

    [Fact]
    public async Task CreateWithAWeakPassword_ReportsTheFailureInsteadOfSucceeding()
    {
        var result = await Controller().Create(
            new CreateUserRequest("newbie", "newbie@sonafly.local", "Newbie", "abc", IdentitySeeder.UserRole));

        AssertBadRequest(result);
        Assert.Null(await Users.FindByNameAsync("newbie"));
    }

    [Fact]
    public async Task CreateWithAValidRole_GrantsItAndForcesAPasswordChange()
    {
        var result = await Controller().Create(
            new CreateUserRequest("newbie", "newbie@sonafly.local", "Newbie", "Correct-Horse-9", IdentitySeeder.UserRole));

        Assert.IsType<CreatedAtActionResult>(result.Result);
        var user = await Users.FindByNameAsync("newbie");
        Assert.NotNull(user);
        Assert.Equal([IdentitySeeder.UserRole], await Users.GetRolesAsync(user));
        // The admin picked this password, so the holder has to replace it.
        Assert.True(user.MustChangePassword);
    }

    [Fact]
    public async Task ARoleChangeToAValidRole_ReplacesTheOldRole()
    {
        var user = await CreateUserAsync("listener", IdentitySeeder.UserRole);

        var result = await Controller().Update(
            user.Id, new UpdateUserRequest(null, null, IdentitySeeder.AdminRole));

        Assert.IsType<NoContentResult>(result);
        Assert.Equal([IdentitySeeder.AdminRole], await Users.GetRolesAsync(user));
    }

    // ── Last administrator ──

    [Fact]
    public async Task TheLastEnabledAdmin_CannotBeDemoted()
    {
        var other = await CreateUserAsync("other-admin", IdentitySeeder.AdminRole);
        // Acting as "other-admin" so this is not blocked by the self-edit guard instead.
        var result = await Controller(actingAs: other).Update(
            _admin.Id, new UpdateUserRequest(null, null, IdentitySeeder.UserRole));

        // Two enabled admins exist, so this one is allowed.
        Assert.IsType<NoContentResult>(result);

        // Now only "other-admin" remains; demoting them must fail.
        var last = await Controller(actingAs: _admin).Update(
            other.Id, new UpdateUserRequest(null, null, IdentitySeeder.UserRole));

        AssertBadRequest(last);
        Assert.Contains(IdentitySeeder.AdminRole, await Users.GetRolesAsync(other));
    }

    [Fact]
    public async Task TheLastEnabledAdmin_CannotBeDisabled()
    {
        var other = await CreateUserAsync("other-admin", IdentitySeeder.AdminRole);

        // Two enabled admins: disabling one is allowed.
        Assert.IsType<NoContentResult>(await Controller(actingAs: other).Disable(_admin.Id));
        Assert.False((await Users.FindByIdAsync(_admin.Id.ToString()))!.IsEnabled);

        // "other" is now the only admin who can sign in, so nobody may disable them —
        // including a different admin account that is itself disabled.
        var disabledAdmin = await CreateUserAsync("disabled-admin", IdentitySeeder.AdminRole, enabled: false);
        AssertBadRequest(await Controller(actingAs: disabledAdmin).Disable(other.Id));
        Assert.True((await Users.FindByIdAsync(other.Id.ToString()))!.IsEnabled);
    }

    [Fact]
    public async Task TheLastEnabledAdmin_CannotBeDeleted()
    {
        var other = await CreateUserAsync("other-admin", IdentitySeeder.AdminRole);

        // Two enabled admins: deleting one is fine.
        Assert.IsType<NoContentResult>(await Controller(actingAs: other).Delete(_admin.Id));

        // "other" is the last one left, so they cannot be deleted by anyone.
        var disabledAdmin = await CreateUserAsync("disabled-admin", IdentitySeeder.AdminRole, enabled: false);
        AssertBadRequest(await Controller(actingAs: disabledAdmin).Delete(other.Id));
        Assert.NotNull(await Users.FindByIdAsync(other.Id.ToString()));
    }

    [Fact]
    public async Task ADisabledAdmin_DoesNotCountTowardsKeepingTheLastOne()
    {
        // _admin is the only admin who can sign in; this one cannot, so it is no
        // substitute and demoting _admin must still be refused.
        var disabled = await CreateUserAsync("disabled-admin", IdentitySeeder.AdminRole, enabled: false);

        AssertBadRequest(await Controller(actingAs: disabled).Update(
            _admin.Id, new UpdateUserRequest(null, null, IdentitySeeder.UserRole)));

        Assert.Contains(IdentitySeeder.AdminRole, await Users.GetRolesAsync(_admin));
        Assert.False((await Users.FindByIdAsync(disabled.Id.ToString()))!.IsEnabled);
    }

    // ── Self-protection ──

    [Fact]
    public async Task AnAdminCannotDisableTheirOwnAccount()
    {
        await CreateUserAsync("other-admin", IdentitySeeder.AdminRole);

        AssertBadRequest(await Controller(actingAs: _admin).Disable(_admin.Id));
        Assert.True((await Users.FindByIdAsync(_admin.Id.ToString()))!.IsEnabled);
    }

    [Fact]
    public async Task AnAdminCannotDeleteTheirOwnAccount()
    {
        await CreateUserAsync("other-admin", IdentitySeeder.AdminRole);

        AssertBadRequest(await Controller(actingAs: _admin).Delete(_admin.Id));
        Assert.NotNull(await Users.FindByIdAsync(_admin.Id.ToString()));
    }

    // ── Reset password ──

    [Fact]
    public async Task ResettingAPassword_ForcesTheHolderToChangeItAndEndsTheirSessions()
    {
        var user = await CreateUserAsync("listener", IdentitySeeder.UserRole);
        var stampBefore = user.SecurityStamp;
        Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            TokenHash = "live-session",
            ExpiresUtc = DateTime.UtcNow.AddDays(7),
            CreatedUtc = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();

        var result = await Controller().ResetPassword(user.Id, new ResetPasswordRequest("Reset-Horse-7"));

        Assert.IsType<NoContentResult>(result);
        var reloaded = await Users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(reloaded);
        Assert.True(reloaded.MustChangePassword);
        Assert.NotEqual(stampBefore, reloaded.SecurityStamp);
        Assert.All(await Db.RefreshTokens.AsNoTracking().Where(t => t.UserId == user.Id).ToListAsync(),
            t => Assert.False(t.IsActive));
    }

    [Fact]
    public async Task ResettingToAWeakPassword_IsRejected()
    {
        var user = await CreateUserAsync("listener", IdentitySeeder.UserRole);

        AssertBadRequest(await Controller().ResetPassword(user.Id, new ResetPasswordRequest("abc")));

        Assert.True(await Users.CheckPasswordAsync(
            (await Users.FindByIdAsync(user.Id.ToString()))!, "Correct-Horse-9"));
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }
}
