namespace SonaFlyUI.Server.Application.Interfaces;

/// <summary>
/// Encrypted server settings (AcoustID key and friends). A stored value wins
/// over the matching configuration-file value; an absent or cleared value
/// falls back to configuration. Plaintext secrets never reach logs or APIs.
/// </summary>
public interface IServerSettingsService
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct);
    Task SetSecretAsync(string key, string? value, Guid? userId, CancellationToken ct);

    /// <summary>
    /// The AcoustID key the provider clients must use: the admin-UI value
    /// when one is stored, otherwise the configured value, otherwise null.
    /// </summary>
    Task<string?> GetEffectiveAcoustIdKeyAsync(CancellationToken ct);

    /// <summary>Where the effective AcoustID key comes from, for safe display.</summary>
    Task<AcoustIdKeySource> GetAcoustIdKeySourceAsync(CancellationToken ct);
}

public enum AcoustIdKeySource
{
    None,
    Configuration,
    Database
}

