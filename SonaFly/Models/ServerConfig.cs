using System.Text.Json.Serialization;

namespace SonaFly.Models;

/// <summary>
/// A configured server. Everything except the tokens is ordinary metadata and is kept in
/// Preferences; the tokens are held in memory here and persisted separately to the
/// platform keychain by <see cref="Services.ServerStorageService"/>.
/// </summary>
public class ServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? Username { get; set; }
    public DateTime? TokenExpiresUtc { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether this account still has to replace its password before it can use the server.
    /// Ordinary metadata, not a secret, so it lives with the rest of it: a cold start has to
    /// route to the password change without first making a request that would only 403.
    /// </summary>
    public bool MustChangePassword { get; set; }

    // JsonIgnore is what keeps the credentials out of the Preferences blob, which is
    // plain text and readable by anything that can reach the app's preference store or
    // a device backup of it.

    [JsonIgnore]
    public string? AccessToken { get; set; }

    [JsonIgnore]
    public string? RefreshToken { get; set; }
}

/// <summary>
/// The shape written by versions that serialised tokens straight into Preferences. Read
/// once on startup so those credentials can be moved into the keychain and erased.
/// </summary>
internal sealed class LegacyServerConfig
{
    public string Id { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
}
