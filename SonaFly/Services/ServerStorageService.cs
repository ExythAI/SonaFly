using System.Text.Json;
using SonaFly.Models;

namespace SonaFly.Services;

public class ServerStorageService
{
    private const string StorageKey = "sonafly_servers";
    private List<ServerConfig>? _cache;

    public List<ServerConfig> GetAll()
    {
        if (_cache != null) return _cache;
        var json = Preferences.Get(StorageKey, "[]");
        _cache = JsonSerializer.Deserialize<List<ServerConfig>>(json) ?? [];
        return _cache;
    }

    public ServerConfig? GetActive() => GetAll().FirstOrDefault(s => s.IsActive);

    public void Add(ServerConfig config)
    {
        var list = GetAll();
        if (list.Count == 0) config.IsActive = true;
        list.Add(config);
        Save();
    }

    public void Remove(string id)
    {
        var list = GetAll();
        var item = list.FirstOrDefault(s => s.Id == id);
        if (item != null)
        {
            list.Remove(item);
            if (item.IsActive && list.Count > 0)
                list[0].IsActive = true;
            Save();
        }
    }

    public void SetActive(string id)
    {
        foreach (var s in GetAll())
            s.IsActive = s.Id == id;
        Save();
    }

    public void UpdateTokens(string id, string accessToken, string refreshToken, DateTime expiresUtc)
    {
        var server = GetAll().FirstOrDefault(s => s.Id == id);
        if (server != null)
        {
            server.AccessToken = accessToken;
            server.RefreshToken = refreshToken;
            server.TokenExpiresUtc = expiresUtc;
            Save();
        }
    }

    public void ClearTokens(string id)
    {
        var server = GetAll().FirstOrDefault(s => s.Id == id);
        if (server != null)
        {
            server.AccessToken = null;
            server.RefreshToken = null;
            server.TokenExpiresUtc = null;
            Save();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_cache);
        Preferences.Set(StorageKey, json);
    }
}
