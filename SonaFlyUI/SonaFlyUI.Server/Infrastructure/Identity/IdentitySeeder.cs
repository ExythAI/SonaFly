using Microsoft.AspNetCore.Identity;
using SonaFlyUI.Server.Domain.Entities;

namespace SonaFlyUI.Server.Infrastructure.Identity;

/// <summary>
/// Seeds the built-in roles and the initial administrator account.
/// </summary>
public static class IdentitySeeder
{
    public const string AdminRole = "Admin";
    public const string UserRole = "User";
    public const string AdminUserName = "admin";

    /// <summary>
    /// The only role names the API will assign. Anything else is rejected before any
    /// existing role is removed.
    /// </summary>
    public static readonly string[] AssignableRoles = [AdminRole, UserRole];

    public static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var role in AssignableRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role));
            }
        }
    }

    /// <summary>
    /// Creates the administrator account if it does not exist yet.
    /// </summary>
    /// <param name="configuredPassword">
    /// Raw <c>SonaFly:AdminDefaultPassword</c> value; <c>null</c> when the key is absent.
    /// </param>
    /// <returns>
    /// The password the account was created with when a new account was seeded, otherwise
    /// <c>null</c> because an administrator already existed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The bootstrap password is unusable. Startup must not continue.
    /// </exception>
    public static async Task<AdminBootstrapPassword?> SeedAdminAsync(
        UserManager<ApplicationUser> userManager,
        string? configuredPassword,
        bool isProduction,
        ILogger logger)
    {
        if (await userManager.FindByNameAsync(AdminUserName) is not null)
        {
            logger.LogInformation("Admin user already exists, skipping seed.");
            return null;
        }

        // Throws on a blank or development-default password in Production.
        var bootstrap = AdminBootstrap.Resolve(configuredPassword, isProduction);

        var admin = new ApplicationUser
        {
            UserName = AdminUserName,
            Email = "admin@sonafly.local",
            EmailConfirmed = true,
            DisplayName = "Administrator",
            IsEnabled = true,
            MustChangePassword = bootstrap.MustChangeAtFirstLogin
        };

        // Check the policy before creating, so a rejected password produces a startup
        // error naming the problem rather than a half-seeded database.
        var validation = await AdminBootstrap.ValidateAsync(userManager, admin, bootstrap.Password);
        if (!validation.Succeeded)
        {
            throw new InvalidOperationException(
                "The configured administrator bootstrap password does not satisfy the password policy: " +
                string.Join(" ", validation.Errors.Select(e => e.Description)));
        }

        var result = await userManager.CreateAsync(admin, bootstrap.Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to create the administrator account: " +
                string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        var roleResult = await userManager.AddToRoleAsync(admin, AdminRole);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to grant the Admin role to the administrator account: " +
                string.Join(" ", roleResult.Errors.Select(e => e.Description)));
        }

        LogBootstrapCredential(bootstrap, logger);
        return bootstrap;
    }

    private static void LogBootstrapCredential(AdminBootstrapPassword bootstrap, ILogger logger)
    {
        switch (bootstrap.Source)
        {
            case AdminPasswordSource.Generated:
                // Shown once, here only. It is not stored anywhere else and cannot be recovered.
                logger.LogWarning(
                    "Administrator account created with a one-time generated password.\n" +
                    "  username: {UserName}\n" +
                    "  password: {Password}\n" +
                    "This password is shown once and must be changed at first login.",
                    AdminUserName, bootstrap.Password);
                break;

            case AdminPasswordSource.DevelopmentDefault:
                logger.LogWarning(
                    "Administrator account created with the DEVELOPMENT-ONLY default password. " +
                    "This credential is published in the project README and is rejected in Production.");
                break;

            default:
                logger.LogInformation("Administrator account created with the configured password.");
                break;
        }
    }
}
