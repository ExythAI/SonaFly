using System.Collections.Concurrent;
using System.Text.Json;
using SonaFly.Models;

namespace SonaFly.Services;

/// <summary>
/// Stores configured servers.
///
/// Server metadata goes to Preferences, which is plain text. Access and refresh tokens
/// go to the platform keychain (SecureStorage) under a per-server key, so an attacker
/// with access to the preference store or a device backup does not get the seven-day
/// refresh credential. Tokens are cached in memory after <see cref="LoadAsync"/> so the
/// hot path stays synchronous.
/// </summary>
public class ServerStorageService
{
    private const string StorageKey = "sonafly_servers";
    private List<ServerConfig>? _cache;

    /// <summary>
    /// One writer at a time per server, so a write and the removal that follows it cannot
    /// reach the keychain out of order.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keychainGates = new();

    /// <summary>
    /// Bumped every time a server's credentials are replaced or cleared. A keychain write
    /// carries the generation it was issued for and does nothing if that has moved on —
    /// otherwise a write still queued when the user signs out would put the credentials
    /// back on disk after the removal, and the next start would reload a dead session.
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _sessionGenerations = new();

    private static string TokenKey(string serverId) => $"sonafly_tokens_{serverId}";

    private sealed record StoredTokens(string? AccessToken, string? RefreshToken);

    private int NextGeneration(string serverId) =>
        _sessionGenerations.AddOrUpdate(serverId, 1, (_, generation) => generation + 1);

    private SemaphoreSlim GateFor(string serverId) =>
        _keychainGates.GetOrAdd(serverId, _ => new SemaphoreSlim(1, 1));

    public List<ServerConfig> GetAll()
    {
        if (_cache != null) return _cache;
        var json = Preferences.Get(StorageKey, "[]");
        _cache = JsonSerializer.Deserialize<List<ServerConfig>>(json) ?? [];
        return _cache;
    }

    /// <summary>
    /// Loads tokens from the keychain into memory, migrating any that an earlier version
    /// left sitting in Preferences. Call once during startup, before the first request.
    /// </summary>
    public async Task LoadAsync()
    {
        var servers = GetAll();
        var migrated = await MigrateLegacyTokensAsync(servers);

        foreach (var server in servers)
        {
            if (server.AccessToken != null || server.RefreshToken != null)
            {
                // Just migrated; already in memory and written to the keychain.
                continue;
            }

            var stored = await ReadTokensAsync(server.Id);
            if (stored != null)
            {
                server.AccessToken = stored.AccessToken;
                server.RefreshToken = stored.RefreshToken;
            }
        }

        if (migrated)
        {
            // Rewrite Preferences so the plaintext copies are gone. ServerConfig no
            // longer serialises its token properties, so this drops them.
            Save();
        }
    }

    public ServerConfig? GetActive() => GetAll().FirstOrDefault(s => s.IsActive);

    public void Add(ServerConfig config)
    {
        var list = GetAll();
        if (list.Count == 0) config.IsActive = true;
        list.Add(config);
        Save();
    }

    public async Task<bool> RemoveAsync(string id)
    {
        var list = GetAll();
        var item = list.FirstOrDefault(s => s.Id == id);
        if (item == null) return true;

        var generation = NextGeneration(id);
        list.Remove(item);
        if (item.IsActive && list.Count > 0)
            list[0].IsActive = true;
        Save();

        return await PersistAsync(id, generation, null);
    }

    public void SetActive(string id)
    {
        foreach (var s in GetAll())
            s.IsActive = s.Id == id;
        Save();
    }

    public async Task UpdateTokensAsync(string id, string accessToken, string? refreshToken, DateTime expiresUtc)
    {
        var server = GetAll().FirstOrDefault(s => s.Id == id);
        if (server == null) return;

        var generation = NextGeneration(id);
        server.AccessToken = accessToken;
        server.RefreshToken = refreshToken;
        server.TokenExpiresUtc = expiresUtc;
        Save();
        await PersistAsync(id, generation, new StoredTokens(accessToken, refreshToken));
    }

    /// <summary>Records whether this account still has to replace its password.</summary>
    public void SetMustChangePassword(string id, bool mustChange)
    {
        var server = GetAll().FirstOrDefault(s => s.Id == id);
        if (server == null || server.MustChangePassword == mustChange) return;

        server.MustChangePassword = mustChange;
        Save();
    }

    /// <summary>
    /// Forgets one server's credentials, in memory first and then on disk.
    /// </summary>
    /// <returns>
    /// False when the keychain entry could not actually be removed. The caller decides what
    /// to say about it: the session is over either way, but a credential left on the device
    /// is not something to discover silently at the next start.
    /// </returns>
    public async Task<bool> ClearTokensAsync(string id)
    {
        var server = GetAll().FirstOrDefault(s => s.Id == id);
        if (server == null) return true;

        // Bump first: any keychain write already queued for this server belongs to the
        // session being ended and must not land after the removal.
        var generation = NextGeneration(id);

        server.AccessToken = null;
        server.RefreshToken = null;
        server.TokenExpiresUtc = null;
        server.MustChangePassword = false;
        Save();

        return await PersistAsync(id, generation, null);
    }

    // ── Persistence ──

    private void Save()
    {
        var json = JsonSerializer.Serialize(_cache);
        Preferences.Set(StorageKey, json);
    }

    /// <summary>
    /// Applies one keychain change, unless a later one has already superseded it.
    /// </summary>
    /// <param name="tokens">What to store, or null to remove the entry.</param>
    /// <returns>False when the change was attempted and the keychain refused it.</returns>
    private async Task<bool> PersistAsync(string serverId, int generation, StoredTokens? tokens)
    {
        var gate = GateFor(serverId);
        await gate.WaitAsync();
        try
        {
            if (_sessionGenerations.GetValueOrDefault(serverId) != generation)
            {
                // Superseded while this was queued. The winning change is the current one.
                return true;
            }

            return tokens is null
                ? RemoveTokens(serverId)
                : await WriteTokensAsync(serverId, tokens);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<StoredTokens?> ReadTokensAsync(string serverId)
    {
        try
        {
            var json = await SecureStorage.GetAsync(TokenKey(serverId));
            return json is null ? null : JsonSerializer.Deserialize<StoredTokens>(json);
        }
        catch (Exception)
        {
            // The keychain can be unavailable (locked device, unsupported platform, or a
            // key written by a previous install). Treat it as "no session" rather than
            // failing startup; the user signs in again.
            return null;
        }
    }

    private static async Task<bool> WriteTokensAsync(string serverId, StoredTokens tokens)
    {
        try
        {
            await SecureStorage.SetAsync(TokenKey(serverId), JsonSerializer.Serialize(tokens));
            return true;
        }
        catch (Exception)
        {
            // The session still works for this run, it just will not survive a restart.
            return false;
        }
    }

    private static bool RemoveTokens(string serverId)
    {
        try
        {
            SecureStorage.Remove(TokenKey(serverId));
            return true;
        }
        catch (Exception)
        {
            // Reported to the caller rather than swallowed: a refresh token still sitting in
            // the keychain after a sign-out is worth saying out loud.
            return false;
        }
    }

    /// <summary>
    /// Moves tokens written by an earlier version out of the Preferences blob.
    /// </summary>
    /// <returns>True when something was migrated and Preferences needs rewriting.</returns>
    private static async Task<bool> MigrateLegacyTokensAsync(List<ServerConfig> servers)
    {
        List<LegacyServerConfig>? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<List<LegacyServerConfig>>(
                Preferences.Get(StorageKey, "[]"));
        }
        catch (JsonException)
        {
            return false;
        }

        if (legacy is null) return false;

        var migrated = false;
        foreach (var old in legacy)
        {
            if (old.AccessToken is null && old.RefreshToken is null) continue;

            var server = servers.FirstOrDefault(s => s.Id == old.Id);
            if (server is null) continue;

            server.AccessToken = old.AccessToken;
            server.RefreshToken = old.RefreshToken;
            await WriteTokensAsync(server.Id, new StoredTokens(old.AccessToken, old.RefreshToken));
            migrated = true;
        }

        return migrated;
    }
}
