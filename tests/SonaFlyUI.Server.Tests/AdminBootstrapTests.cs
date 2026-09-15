using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identity;
using Xunit;

namespace SonaFlyUI.Server.Tests;

/// <summary>
/// Covers the bootstrap administrator password: the published development credential
/// must never be usable in Production, and Production must never fall back to a
/// universal or blank one.
/// </summary>
public sealed class AdminBootstrapTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;

    public AdminBootstrapTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SonaFlyDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Mirrors the policy configured in Program.cs.
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<SonaFlyDbContext>();

        _services = services.BuildServiceProvider();
        _services.GetRequiredService<SonaFlyDbContext>().Database.EnsureCreated();
    }

    private UserManager<ApplicationUser> UserManager =>
        _services.GetRequiredService<UserManager<ApplicationUser>>();

    // ── Resolve: policy decisions ──

    [Fact]
    public void Resolve_ProductionWithDevelopmentDefault_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AdminBootstrap.Resolve(AdminBootstrap.DevelopmentDefaultPassword, isProduction: true));

        Assert.Contains("development password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankPasswordInProduction_GeneratesRatherThanFallingBack(string? configured)
    {
        // docker compose substitutes an unset ADMIN_DEFAULT_PASSWORD as "", so blank
        // has to behave exactly like absent.
        var resolved = AdminBootstrap.Resolve(configured, isProduction: true);

        Assert.Equal(AdminPasswordSource.Generated, resolved.Source);
        Assert.NotEqual(AdminBootstrap.DevelopmentDefaultPassword, resolved.Password);
        Assert.False(string.IsNullOrWhiteSpace(resolved.Password));
        Assert.True(resolved.MustChangeAtFirstLogin);
    }

    [Fact]
    public void Resolve_ProductionWithNoPassword_GeneratesOneTimePassword()
    {
        var first = AdminBootstrap.Resolve(null, isProduction: true);
        var second = AdminBootstrap.Resolve(null, isProduction: true);

        Assert.Equal(AdminPasswordSource.Generated, first.Source);
        Assert.True(first.MustChangeAtFirstLogin);
        Assert.NotEqual(AdminBootstrap.DevelopmentDefaultPassword, first.Password);
        Assert.NotEqual(first.Password, second.Password);
    }

    [Fact]
    public void Resolve_ProductionWithStrongPassword_IsUsedAsConfigured()
    {
        var resolved = AdminBootstrap.Resolve("Corrects-Horse-Battery-7", isProduction: true);

        Assert.Equal(AdminPasswordSource.Configured, resolved.Source);
        Assert.Equal("Corrects-Horse-Battery-7", resolved.Password);
        Assert.False(resolved.MustChangeAtFirstLogin);
    }

    [Fact]
    public void Resolve_DevelopmentWithNoPassword_UsesDocumentedDevelopmentCredential()
    {
        var resolved = AdminBootstrap.Resolve(null, isProduction: false);

        Assert.Equal(AdminPasswordSource.DevelopmentDefault, resolved.Source);
        Assert.Equal(AdminBootstrap.DevelopmentDefaultPassword, resolved.Password);
        // Development stays usable straight after startup; the loud log warning is the control.
        Assert.False(resolved.MustChangeAtFirstLogin);
    }

    [Fact]
    public async Task GeneratePassword_SatisfiesTheConfiguredIdentityPolicy()
    {
        var validators = UserManager.PasswordValidators;
        Assert.NotEmpty(validators);

        for (var i = 0; i < 50; i++)
        {
            var password = AdminBootstrap.GeneratePassword();
            Assert.Equal(24, password.Length);

            foreach (var validator in validators)
            {
                var result = await validator.ValidateAsync(UserManager, new ApplicationUser(), password);
                Assert.True(result.Succeeded,
                    $"Generated password was rejected: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }

    // ── SeedAdminAsync: end-to-end against a real UserManager ──

    [Fact]
    public async Task SeedAdmin_ProductionWithDevelopmentDefault_DoesNotCreateAnAccount()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => IdentitySeeder.SeedAdminAsync(
            UserManager, AdminBootstrap.DevelopmentDefaultPassword, isProduction: true, NullLogger.Instance));

        Assert.Null(await UserManager.FindByNameAsync(IdentitySeeder.AdminUserName));
    }

    [Fact]
    public async Task SeedAdmin_WeakPassword_FailsStartupWithoutCreatingAnAccount()
    {
        // "abc" fails the length and digit rules.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => IdentitySeeder.SeedAdminAsync(
            UserManager, "abc", isProduction: true, NullLogger.Instance));

        Assert.Contains("password policy", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await UserManager.FindByNameAsync(IdentitySeeder.AdminUserName));
    }

    [Fact]
    public async Task SeedAdmin_ProductionWithNoPassword_CreatesAccountThatMustChangeItsPassword()
    {
        await IdentitySeeder.SeedRolesAsync(_services.GetRequiredService<RoleManager<ApplicationRole>>());

        var bootstrap = await IdentitySeeder.SeedAdminAsync(
            UserManager, null, isProduction: true, NullLogger.Instance);

        Assert.NotNull(bootstrap);
        Assert.Equal(AdminPasswordSource.Generated, bootstrap.Source);

        var admin = await UserManager.FindByNameAsync(IdentitySeeder.AdminUserName);
        Assert.NotNull(admin);
        Assert.True(admin.MustChangePassword);
        Assert.Contains(IdentitySeeder.AdminRole, await UserManager.GetRolesAsync(admin));

        // The one-time password works, and the documented one does not.
        Assert.True(await UserManager.CheckPasswordAsync(admin, bootstrap.Password));
        Assert.False(await UserManager.CheckPasswordAsync(admin, AdminBootstrap.DevelopmentDefaultPassword));
    }

    [Fact]
    public async Task SeedAdmin_ValidConfiguredPassword_CreatesAccountWithoutForcingAChange()
    {
        await IdentitySeeder.SeedRolesAsync(_services.GetRequiredService<RoleManager<ApplicationRole>>());

        var bootstrap = await IdentitySeeder.SeedAdminAsync(
            UserManager, "Corrects-Horse-Battery-7", isProduction: true, NullLogger.Instance);

        Assert.NotNull(bootstrap);
        var admin = await UserManager.FindByNameAsync(IdentitySeeder.AdminUserName);
        Assert.NotNull(admin);
        Assert.False(admin.MustChangePassword);
        Assert.True(await UserManager.CheckPasswordAsync(admin, "Corrects-Horse-Battery-7"));
    }

    [Fact]
    public async Task SeedAdmin_WhenAdminAlreadyExists_LeavesThePasswordAlone()
    {
        await IdentitySeeder.SeedRolesAsync(_services.GetRequiredService<RoleManager<ApplicationRole>>());
        await IdentitySeeder.SeedAdminAsync(UserManager, "Corrects-Horse-Battery-7", isProduction: true, NullLogger.Instance);

        var second = await IdentitySeeder.SeedAdminAsync(
            UserManager, "Different-Password-9", isProduction: true, NullLogger.Instance);

        Assert.Null(second);
        var admin = await UserManager.FindByNameAsync(IdentitySeeder.AdminUserName);
        Assert.NotNull(admin);
        Assert.True(await UserManager.CheckPasswordAsync(admin, "Corrects-Horse-Battery-7"));
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }
}
