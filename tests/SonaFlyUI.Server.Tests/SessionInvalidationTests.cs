using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identity;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Covers the rule that a signed, unexpired access token is not on its own enough:
/// disabling, deleting, demoting or re-crediting an account has to take effect on the
/// next request rather than when the token happens to expire.
/// </summary>
public sealed class SessionInvalidationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;
    private readonly TokenService _tokens;

    public SessionInvalidationTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
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
            // Password reset needs the Default token provider, as it does in Program.cs.
            .AddDefaultTokenProviders();
        services.AddDataProtection();
        services.AddScoped<IUserSecurityService, UserSecurityService>();

        _services = services.BuildServiceProvider();
        _services.GetRequiredService<SonaFlyDbContext>().Database.EnsureCreated();

        _tokens = new TokenService(Options.Create(new JwtSettings
        {
            Secret = "SonaFly-Test-Secret-Key-Must-Be-At-Least-32-Chars!",
            Issuer = "SonaFly",
            Audience = "SonaFlyClients"
        }));
    }

    private SonaFlyDbContext Db => _services.GetRequiredService<SonaFlyDbContext>();
    private UserManager<ApplicationUser> UserManager => _services.GetRequiredService<UserManager<ApplicationUser>>();
    private IUserSecurityService Security => _services.GetRequiredService<IUserSecurityService>();

    private async Task<ApplicationUser> CreateUserAsync(string name = "listener")
    {
        var user = new ApplicationUser
        {
            UserName = name,
            Email = $"{name}@sonafly.local",
            DisplayName = name,
            IsEnabled = true
        };
        var result = await UserManager.CreateAsync(user, "Correct-Horse-9");
        Assert.True(result.Succeeded);
        return user;
    }

    /// <summary>
    /// Builds the principal the JWT middleware would hand to OnTokenValidated, by
    /// round-tripping a real issued token rather than hand-assembling claims.
    /// </summary>
    private ClaimsPrincipal PrincipalFor(ApplicationUser user, params string[] roles)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_tokens.CreateAccessToken(user, roles));
        return new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "Test"));
    }

    // ── Access tokens ──

    [Fact]
    public void AccessToken_CarriesTheSecurityStamp()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "x", SecurityStamp = "STAMP-1" };

        var principal = PrincipalFor(user);

        Assert.Equal("STAMP-1", principal.FindFirstValue(TokenService.SecurityStampClaim));
    }

    [Fact]
    public async Task ValidToken_ForAnUntouchedAccount_IsAccepted()
    {
        var user = await CreateUserAsync();
        var principal = PrincipalFor(user);

        Assert.NotNull(await Security.ResolveValidUserAsync(principal));
    }

    [Fact]
    public async Task TokenIssuedBeforeDisable_IsRejected()
    {
        var user = await CreateUserAsync();
        var principal = PrincipalFor(user);

        user.IsEnabled = false;
        Assert.True((await UserManager.UpdateAsync(user)).Succeeded);

        Assert.Null(await Security.ResolveValidUserAsync(principal));
    }

    [Fact]
    public async Task TokenIssuedBeforeDelete_IsRejected()
    {
        var user = await CreateUserAsync();
        var principal = PrincipalFor(user);

        Assert.True((await UserManager.DeleteAsync(user)).Succeeded);

        Assert.Null(await Security.ResolveValidUserAsync(principal));
    }

    [Fact]
    public async Task TokenIssuedBeforePasswordChange_IsRejected()
    {
        var user = await CreateUserAsync();
        var principal = PrincipalFor(user);

        // ChangePasswordAsync rotates the security stamp, which is what invalidates
        // tokens already handed out.
        Assert.True((await UserManager.ChangePasswordAsync(user, "Correct-Horse-9", "Different-Horse-8")).Succeeded);

        Assert.Null(await Security.ResolveValidUserAsync(principal));
    }

    [Fact]
    public async Task TokenIssuedBeforePasswordReset_IsRejected()
    {
        var user = await CreateUserAsync();
        var principal = PrincipalFor(user);

        var token = await UserManager.GeneratePasswordResetTokenAsync(user);
        Assert.True((await UserManager.ResetPasswordAsync(user, token, "Reset-Horse-7")).Succeeded);

        Assert.Null(await Security.ResolveValidUserAsync(principal));
    }

    [Fact]
    public async Task TokenIssuedBeforeRevokeAllSessions_IsRejected_AndANewTokenWorks()
    {
        var user = await CreateUserAsync();
        var oldPrincipal = PrincipalFor(user, "Admin");

        await Security.RevokeAllSessionsAsync(user, SessionRevocationReason.RolesChanged);

        // The demoted admin's existing token is dead ...
        Assert.Null(await Security.ResolveValidUserAsync(oldPrincipal));

        // ... while a token minted after the rotation is accepted.
        var refreshed = await UserManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(refreshed);
        Assert.NotNull(await Security.ResolveValidUserAsync(PrincipalFor(refreshed, "User")));
    }

    [Fact]
    public async Task TokenWithoutASecurityStampClaim_IsRejected()
    {
        var user = await CreateUserAsync();

        // A token minted before this change carried no stamp at all.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "Test"));

        Assert.Null(await Security.ResolveValidUserAsync(principal));
    }

    [Fact]
    public async Task PrincipalWithoutAUsableSubject_IsRejected()
    {
        await CreateUserAsync();

        Assert.Null(await Security.ResolveValidUserAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(await Security.ResolveValidUserAsync(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")], "Test"))));
    }

    // ── Refresh tokens ──

    private async Task<RefreshToken> AddRefreshTokenAsync(Guid userId, Guid familyId, string hash)
    {
        var token = new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = hash,
            ExpiresUtc = DateTime.UtcNow.AddDays(7),
            CreatedUtc = DateTime.UtcNow
        };
        Db.RefreshTokens.Add(token);
        await Db.SaveChangesAsync();
        return token;
    }

    [Fact]
    public async Task RevokeAllSessions_RevokesEveryLiveRefreshToken()
    {
        var user = await CreateUserAsync();
        var other = await CreateUserAsync("other");

        await AddRefreshTokenAsync(user.Id, Guid.NewGuid(), "phone");
        await AddRefreshTokenAsync(user.Id, Guid.NewGuid(), "laptop");
        await AddRefreshTokenAsync(other.Id, Guid.NewGuid(), "untouched");

        await Security.RevokeAllSessionsAsync(user, SessionRevocationReason.PasswordChanged);

        var tokens = await Db.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.All(tokens.Where(t => t.UserId == user.Id), t => Assert.False(t.IsActive));
        // Another user's sessions are untouched.
        Assert.True(tokens.Single(t => t.UserId == other.Id).IsActive);
    }

    [Fact]
    public async Task ReplayingASupersededToken_RevokesItsWholeFamilyButNotOtherLogins()
    {
        var user = await CreateUserAsync();

        // One login, rotated twice: three tokens in one family, the newest still live.
        var family = Guid.NewGuid();
        var first = await AddRefreshTokenAsync(user.Id, family, "gen-1");
        var second = await AddRefreshTokenAsync(user.Id, family, "gen-2");
        var third = await AddRefreshTokenAsync(user.Id, family, "gen-3");
        first.RevokedUtc = DateTime.UtcNow;
        second.RevokedUtc = DateTime.UtcNow;
        await Db.SaveChangesAsync();

        // A separate login on another device.
        var otherFamily = await AddRefreshTokenAsync(user.Id, Guid.NewGuid(), "tablet");

        // Someone presents "gen-1" again — it was rotated away, so a copy is loose.
        await Security.RevokeRefreshTokenFamilyAsync(first.FamilyId);

        var tokens = await Db.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.False(tokens.Single(t => t.Id == third.Id).IsActive);
        // The unrelated login survives: one stolen chain does not sign out every device.
        Assert.True(tokens.Single(t => t.Id == otherFamily.Id).IsActive);
    }

    [Fact]
    public async Task RevokingAFamilyTwice_IsHarmless()
    {
        var user = await CreateUserAsync();
        var family = Guid.NewGuid();
        await AddRefreshTokenAsync(user.Id, family, "gen-1");

        Assert.Equal(1, await Security.RevokeRefreshTokenFamilyAsync(family));
        Assert.Equal(0, await Security.RevokeRefreshTokenFamilyAsync(family));
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }
}
