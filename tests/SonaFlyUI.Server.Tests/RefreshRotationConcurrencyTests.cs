using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Api.Controllers;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identity;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Refresh-token rotation has to be indivisible. Reading "this token is still active" and
/// then saving its successor as two separate steps lets two callers both consume one token,
/// and lets a logout or password reset revoke a family a moment before an unrevoked child of
/// that same family appears behind it.
///
/// Every test here runs the competing requests on genuinely separate connections to a
/// file-backed SQLite database, released together from a barrier: a shared in-memory
/// connection cannot contend with itself, and would prove nothing.
/// </summary>
public sealed class RefreshRotationConcurrencyTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"sonafly-refresh-{Guid.NewGuid():N}.db");

    private readonly List<ServiceProvider> _providers = [];

    private static readonly JwtSettings Jwt = new()
    {
        Secret = "SonaFly-Test-Secret-Key-Must-Be-At-Least-32-Chars!",
        Issuer = "SonaFly",
        Audience = "SonaFlyClients",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7
    };

    private readonly ServiceProvider _seedProvider;
    private readonly ApplicationUser _user;

    public RefreshRotationConcurrencyTests()
    {
        _seedProvider = NewProvider();
        _seedProvider.GetRequiredService<SonaFlyDbContext>().Database.EnsureCreated();

        IdentitySeeder.SeedRolesAsync(_seedProvider.GetRequiredService<RoleManager<ApplicationRole>>())
            .GetAwaiter().GetResult();

        _user = new ApplicationUser
        {
            UserName = "listener", Email = "listener@sonafly.local", DisplayName = "Listener", IsEnabled = true
        };
        var users = _seedProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(users.CreateAsync(_user, "Correct-Horse-9").GetAwaiter().GetResult().Succeeded);
        Assert.True(users.AddToRoleAsync(_user, IdentitySeeder.UserRole).GetAwaiter().GetResult().Succeeded);
    }

    private ServiceProvider NewProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddDbContext<SonaFlyDbContext>(o => o.UseSqlite($"Data Source={_databasePath}"));
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
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddScoped<IUserSecurityService, UserSecurityService>();
        services.AddSingleton<ITokenService>(new TokenService(Options.Create(Jwt)));
        services.Configure<JwtSettings>(o =>
        {
            o.Secret = Jwt.Secret;
            o.Issuer = Jwt.Issuer;
            o.Audience = Jwt.Audience;
            o.AccessTokenExpirationMinutes = Jwt.AccessTokenExpirationMinutes;
            o.RefreshTokenExpirationDays = Jwt.RefreshTokenExpirationDays;
        });

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    /// <summary>An AuthController with a connection of its own, as a separate request would have.</summary>
    private AuthController ControllerOnItsOwnConnection()
    {
        var provider = NewProvider();
        return new AuthController(
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            provider.GetRequiredService<SignInManager<ApplicationUser>>(),
            provider.GetRequiredService<ITokenService>(),
            provider.GetRequiredService<SonaFlyDbContext>(),
            provider.GetRequiredService<IOptions<JwtSettings>>(),
            provider.GetRequiredService<IUserSecurityService>(),
            provider.GetRequiredService<ILogger<AuthController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>Signs in and returns the refresh token, as a native client would hold it.</summary>
    private async Task<string> LoginAsync()
    {
        var result = await ControllerOnItsOwnConnection()
            .Login(new LoginRequest("listener", "Correct-Horse-9", UseCookie: false), default);

        var response = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        return Assert.IsType<string>(response.RefreshToken);
    }

    private async Task<ActionResult<RefreshResponse>> RefreshAsync(string token) =>
        await ControllerOnItsOwnConnection().Refresh(new RefreshRequest(token, UseCookie: false), default);

    private static string? SuccessfulRefreshTokenOrNull(ActionResult<RefreshResponse> result) =>
        result.Result is OkObjectResult { Value: RefreshResponse response } ? response.RefreshToken : null;

    private static bool Succeeded(ActionResult<RefreshResponse> result) => result.Result is OkObjectResult;

    /// <summary>Releases both operations from the same instant.</summary>
    private static async Task<(T First, T Second)> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        using var barrier = new Barrier(2);

        var a = Task.Run(async () => { barrier.SignalAndWait(); return await first(); });
        var b = Task.Run(async () => { barrier.SignalAndWait(); return await second(); });

        return (await a, await b);
    }

    /// <summary>
    /// Races two operations with the database write lock already held by somebody else, then
    /// hands it over.
    ///
    /// Starting two requests at the same instant is not enough to reproduce a read-then-write
    /// race: the first usually finishes before the second has read anything. Holding the lock
    /// externally parks both of them at the same point first. Correct code queues here,
    /// because it takes the lock before it reads; code that reads first lets both requests
    /// read the same token as active, which is exactly the interleaving being ruled out.
    /// </summary>
    private async Task<(T First, T Second)> RaceUnderWriteLockAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        await using var blocker = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        await blocker.OpenAsync();
        await using (var begin = blocker.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            await begin.ExecuteNonQueryAsync();
        }

        using var barrier = new Barrier(2);
        var a = Task.Run(async () => { barrier.SignalAndWait(); return await first(); });
        var b = Task.Run(async () => { barrier.SignalAndWait(); return await second(); });

        // Long enough for both requests to reach the database and stop, whichever point
        // they stop at.
        await Task.Delay(500);

        await using (var commit = blocker.CreateCommand())
        {
            commit.CommandText = "COMMIT";
            await commit.ExecuteNonQueryAsync();
        }

        return (await a, await b);
    }

    private int LiveTokenCount()
    {
        var db = NewProvider().GetRequiredService<SonaFlyDbContext>();
        var now = DateTime.UtcNow;
        return db.RefreshTokens.AsNoTracking().Count(rt => rt.RevokedUtc == null && rt.ExpiresUtc > now);
    }

    // ── Two refreshes of one token ──

    [Fact]
    public async Task TwoRefreshesOfTheSameToken_ProduceAtMostOneSuccessor()
    {
        var token = await LoginAsync();

        var (first, second) = await RaceUnderWriteLockAsync(() => RefreshAsync(token), () => RefreshAsync(token));

        // Exactly one caller consumed the token; the other is told the token is not valid.
        Assert.Single(new[] { first, second }, Succeeded);
        Assert.Single(new[] { first, second }, r => r.Result is UnauthorizedObjectResult);
        // And at most one descendant exists, whatever the callers were told.
        Assert.InRange(LiveTokenCount(), 0, 1);
    }

    [Fact]
    public async Task ASecondUseOfAConsumedToken_RevokesTheWholeChain()
    {
        var token = await LoginAsync();

        var (first, second) = await RaceUnderWriteLockAsync(() => RefreshAsync(token), () => RefreshAsync(token));
        var successor = SuccessfulRefreshTokenOrNull(first) ?? SuccessfulRefreshTokenOrNull(second);

        // One token presented twice means a copy is in circulation. The winner's successor
        // is not spared: the whole chain descended from that login is gone.
        Assert.NotNull(successor);
        Assert.False(Succeeded(await RefreshAsync(successor!)));
        Assert.Equal(0, LiveTokenCount());
    }

    [Fact]
    public async Task AnOrdinarySequentialRotation_KeepsWorking()
    {
        // The guard must not break the case it exists to protect.
        var token = await LoginAsync();

        for (var i = 0; i < 3; i++)
        {
            var result = await RefreshAsync(token);
            Assert.True(Succeeded(result));
            token = SuccessfulRefreshTokenOrNull(result)!;
        }

        Assert.Equal(1, LiveTokenCount());
    }

    // ── Refresh against revocation ──

    [Fact]
    public async Task RefreshRacingLogout_LeavesNoUsableDescendant()
    {
        var token = await LoginAsync();

        var (refreshResult, _) = await RaceUnderWriteLockAsync<object?>(
            async () => await RefreshAsync(token),
            async () => await ControllerOnItsOwnConnection().Logout(new LogoutRequest(token), default));

        // Either the refresh lost and was refused, or it won and its child was swept up by
        // the logout that followed it. What must never happen is a live descendant of a
        // session the user has ended.
        if (((ActionResult<RefreshResponse>)refreshResult!).Result is OkObjectResult)
        {
            var successor = SuccessfulRefreshTokenOrNull((ActionResult<RefreshResponse>)refreshResult!);
            Assert.False(Succeeded(await RefreshAsync(successor!)));
        }

        Assert.Equal(0, LiveTokenCount());
    }

    [Fact]
    public async Task RefreshRacingAPasswordReset_LeavesNoUsableDescendant()
    {
        var token = await LoginAsync();

        var (refreshResult, _) = await RaceUnderWriteLockAsync<object?>(
            async () => await RefreshAsync(token),
            async () =>
            {
                var provider = NewProvider();
                var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.FindByIdAsync(_user.Id.ToString());
                await provider.GetRequiredService<IUserSecurityService>()
                    .RevokeAllSessionsAsync(user!, SessionRevocationReason.PasswordReset);
                return null;
            });

        var successor = SuccessfulRefreshTokenOrNull((ActionResult<RefreshResponse>)refreshResult!);
        if (successor != null)
        {
            Assert.False(Succeeded(await RefreshAsync(successor)));
        }

        Assert.Equal(0, LiveTokenCount());
    }

    [Fact]
    public async Task RefreshRacingAnAccountDisable_LeavesNoUsableDescendant()
    {
        var token = await LoginAsync();

        var (refreshResult, _) = await RaceUnderWriteLockAsync<object?>(
            async () => await RefreshAsync(token),
            async () =>
            {
                var provider = NewProvider();
                var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.FindByIdAsync(_user.Id.ToString());
                user!.IsEnabled = false;
                await users.UpdateAsync(user);
                await provider.GetRequiredService<IUserSecurityService>()
                    .RevokeAllSessionsAsync(user, SessionRevocationReason.AccountDisabled);
                return null;
            });

        var successor = SuccessfulRefreshTokenOrNull((ActionResult<RefreshResponse>)refreshResult!);
        if (successor != null)
        {
            Assert.False(Succeeded(await RefreshAsync(successor)));
        }

        Assert.Equal(0, LiveTokenCount());
    }

    [Fact]
    public async Task ASuccessorOfARevokedSession_NeverRefreshesAfterwards()
    {
        // The plainly sequential statement of the invariant, independent of any race.
        var token = await LoginAsync();

        var rotated = await RefreshAsync(token);
        var successor = SuccessfulRefreshTokenOrNull(rotated)!;

        await ControllerOnItsOwnConnection().Logout(new LogoutRequest(successor), default);

        Assert.False(Succeeded(await RefreshAsync(successor)));
        Assert.Equal(0, LiveTokenCount());
    }

    [Fact]
    public async Task OtherLogins_SurviveOneSessionBeingRevoked()
    {
        // Families exist so that ending one session does not sign the user out everywhere.
        var phone = await LoginAsync();
        var laptop = await LoginAsync();

        await ControllerOnItsOwnConnection().Logout(new LogoutRequest(phone), default);

        Assert.False(Succeeded(await RefreshAsync(phone)));
        Assert.True(Succeeded(await RefreshAsync(laptop)));
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_databasePath); } catch (IOException) { /* temp file; the OS will reclaim it */ }
    }
}
