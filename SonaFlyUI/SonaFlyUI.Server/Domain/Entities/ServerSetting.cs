namespace SonaFlyUI.Server.Domain.Entities;

/// <summary>
/// A server-managed setting, including secrets. Secret values are stored
/// encrypted with the Data Protection key ring (see ServerSettingsService),
/// so a database backup never contains them in plaintext. The API only ever
/// reports whether a secret is set, never its value.
/// </summary>
public class ServerSetting : EntityBase
{
    public string Key { get; set; } = string.Empty;
    public string? EncryptedValue { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Well-known server setting keys.</summary>
public static class ServerSettingKeys
{
    /// <summary>
    /// AcoustID application client key saved from the admin UI. When present
    /// it wins over the SonaFly:Identification:AcoustIdClientKey configuration
    /// value; deleting it falls back to configuration.
    /// </summary>
    public const string AcoustIdClientKey = "Identification.AcoustIdClientKey";
}

