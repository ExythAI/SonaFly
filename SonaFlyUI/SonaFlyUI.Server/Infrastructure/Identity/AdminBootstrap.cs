using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using SonaFlyUI.Server.Domain.Entities;

namespace SonaFlyUI.Server.Infrastructure.Identity;

/// <summary>
/// How the initial administrator password was obtained.
/// </summary>
public enum AdminPasswordSource
{
    /// <summary>Read from SonaFly:AdminDefaultPassword.</summary>
    Configured,

    /// <summary>Generated on this boot because nothing was configured.</summary>
    Generated,

    /// <summary>The published development-only credential. Never valid in Production.</summary>
    DevelopmentDefault
}

public sealed record AdminBootstrapPassword(string Password, AdminPasswordSource Source)
{
    /// <summary>
    /// A generated password is shown once in the logs and is not a credential anyone chose,
    /// so it only gets the account as far as the change-password endpoint. A development
    /// instance keeps its documented credential usable and relies on the startup warning.
    /// </summary>
    public bool MustChangeAtFirstLogin => Source == AdminPasswordSource.Generated;
}

/// <summary>
/// Decides which password the seeded administrator account is created with.
/// Production never accepts the published development credential, and never
/// falls back to a universal one.
/// </summary>
public static class AdminBootstrap
{
    /// <summary>
    /// The credential documented in README.md and docker/.env.example. It exists so a
    /// local development instance is usable immediately; Production rejects it outright.
    /// </summary>
    public const string DevelopmentDefaultPassword = "Admin123!";

    private const string ConfigurationKey = "SonaFly:AdminDefaultPassword";

    // Excludes characters that are easy to confuse when transcribed from a container log.
    private const string LowerChars = "abcdefghijkmnopqrstuvwxyz";
    private const string UpperChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string DigitChars = "23456789";
    private const string SymbolChars = "!@#$%^&*-_=+?";

    /// <summary>
    /// Resolves the bootstrap password for the given environment.
    /// </summary>
    /// <param name="configuredPassword">
    /// The raw configuration value. <c>null</c>, empty and whitespace are all treated as
    /// "not configured".
    /// </param>
    /// <param name="isProduction">Whether the host is running in the Production environment.</param>
    /// <exception cref="InvalidOperationException">
    /// The configured value is the published development credential and the host is in
    /// Production. Startup must not continue.
    /// </exception>
    public static AdminBootstrapPassword Resolve(string? configuredPassword, bool isProduction)
    {
        // Absent and blank are the same case: docker compose substitutes an unset
        // variable as an empty string, so a commented-out ADMIN_DEFAULT_PASSWORD
        // arrives here as "". Neither is ever allowed to mean "use a known password".
        if (string.IsNullOrWhiteSpace(configuredPassword))
        {
            // Development gets the documented credential for convenience;
            // Production gets a one-time password instead of a universal one.
            return isProduction
                ? new AdminBootstrapPassword(GeneratePassword(), AdminPasswordSource.Generated)
                : new AdminBootstrapPassword(DevelopmentDefaultPassword, AdminPasswordSource.DevelopmentDefault);
        }

        if (isProduction && configuredPassword == DevelopmentDefaultPassword)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is set to the published development password. " +
                "It is documented in this project's README and must never be used in Production. " +
                $"Set {ConfigurationKey} (ADMIN_DEFAULT_PASSWORD) to a unique strong password, " +
                "or remove it to have a one-time password generated on first boot.");
        }

        if (!isProduction && configuredPassword == DevelopmentDefaultPassword)
        {
            return new AdminBootstrapPassword(configuredPassword, AdminPasswordSource.DevelopmentDefault);
        }

        return new AdminBootstrapPassword(configuredPassword, AdminPasswordSource.Configured);
    }

    /// <summary>
    /// Generates a password that satisfies any reasonable Identity policy: 24 characters
    /// drawn from four character classes, with at least one of each.
    /// </summary>
    public static string GeneratePassword(int length = 24)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 8);

        const string all = LowerChars + UpperChars + DigitChars + SymbolChars;
        var chars = new char[length];

        // Guarantee one character from each class, then fill the remainder.
        chars[0] = Pick(LowerChars);
        chars[1] = Pick(UpperChars);
        chars[2] = Pick(DigitChars);
        chars[3] = Pick(SymbolChars);
        for (var i = 4; i < length; i++)
        {
            chars[i] = Pick(all);
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);

        static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }

    /// <summary>
    /// Runs the configured Identity password validators against a candidate password.
    /// </summary>
    public static async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string password)
    {
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, password);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        return IdentityResult.Success;
    }
}
