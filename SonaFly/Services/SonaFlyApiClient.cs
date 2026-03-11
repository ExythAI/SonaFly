using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SonaFly.Models;

namespace SonaFly.Services;

public class SonaFlyApiClient
{
    private readonly HttpClient _http;
    private readonly ServerStorageService _storage;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SonaFlyApiClient(HttpClient http, ServerStorageService storage)
    {
        _http = http;
        _storage = storage;
    }

    public string BaseUrl => _storage.GetActive()?.BaseUrl?.TrimEnd('/') ?? throw new InvalidOperationException("No active server.");
    public string? AccessToken => _storage.GetActive()?.AccessToken;

    private void ApplyAuth()
    {
        var server = _storage.GetActive();
        if (server?.AccessToken != null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.AccessToken);
    }

    // ── Auth ──
    public async Task<LoginResponse> LoginAsync(string baseUrl, string username, string password)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/auth/login";
        var resp = await _http.PostAsJsonAsync(url, new { username, password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts))!;
    }

    public async Task<bool> TryRefreshTokenAsync()
    {
        var server = _storage.GetActive();
        if (server?.RefreshToken == null) return false;

        try
        {
            var url = $"{BaseUrl}/api/auth/refresh";
            var resp = await _http.PostAsJsonAsync(url, new { refreshToken = server.RefreshToken });
            if (!resp.IsSuccessStatusCode) return false;

            var result = await resp.Content.ReadFromJsonAsync<RefreshResponse>(JsonOpts);
            if (result != null)
            {
                _storage.UpdateTokens(server.Id, result.AccessToken, result.RefreshToken, result.ExpiresUtc);
                return true;
            }
        }
        catch { /* swallow */ }
        return false;
    }

    private async Task<T?> GetAsync<T>(string path)
    {
        ApplyAuth();
        var resp = await _http.GetAsync($"{BaseUrl}/api/{path}");
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await TryRefreshTokenAsync())
            {
                ApplyAuth();
                resp = await _http.GetAsync($"{BaseUrl}/api/{path}");
            }
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    // ── Browse ──
    public Task<PaginatedResult<ArtistDto>?> GetArtistsAsync(int page = 1, int pageSize = 50) =>
        GetAsync<PaginatedResult<ArtistDto>>($"artists?page={page}&pageSize={pageSize}");

    public Task<PaginatedResult<AlbumDto>?> GetAlbumsAsync(int page = 1, int pageSize = 50, Guid? artistId = null) =>
        GetAsync<PaginatedResult<AlbumDto>>($"albums?page={page}&pageSize={pageSize}{(artistId.HasValue ? $"&artistId={artistId}" : "")}");

    public Task<AlbumDetailDto?> GetAlbumByIdAsync(string id) =>
        GetAsync<AlbumDetailDto>($"albums/{id}");

    public Task<PaginatedResult<TrackDto>?> GetTracksAsync(int page = 1, int pageSize = 50) =>
        GetAsync<PaginatedResult<TrackDto>>($"tracks?page={page}&pageSize={pageSize}");

    public Task<SearchResultDto?> SearchAsync(string query, int limit = 20) =>
        GetAsync<SearchResultDto>($"search?q={Uri.EscapeDataString(query)}&limit={limit}");

    public Task<List<GenreDto>?> GetGenresAsync() => GetAsync<List<GenreDto>>("genres");

    // ── Playlists ──
    public Task<List<PlaylistDto>?> GetPlaylistsAsync() => GetAsync<List<PlaylistDto>>("playlists");
    public Task<PlaylistDto?> GetPlaylistByIdAsync(string id) => GetAsync<PlaylistDto>($"playlists/{id}");

    public async Task<Guid?> CreatePlaylistAsync(string name, string? description)
    {
        var result = await PostAsync<object>("playlists", new { name, description, isPublic = false });
        if (result is JsonElement je && je.TryGetProperty("id", out var idProp))
            return Guid.Parse(idProp.GetString()!);
        return null;
    }

    public Task AddTrackToPlaylistAsync(Guid playlistId, Guid trackId) =>
        PostAsync<object>($"playlists/{playlistId}/items", new { trackId });

    public Task DeletePlaylistAsync(Guid playlistId) =>
        DeleteAsync($"playlists/{playlistId}");

    public Task RemovePlaylistItemAsync(Guid playlistId, Guid itemId) =>
        DeleteAsync($"playlists/{playlistId}/items/{itemId}");

    // ── Mixed Tapes ──
    public Task<List<MixedTapeDto>?> GetMixedTapesAsync() => GetAsync<List<MixedTapeDto>>("mixed-tapes");
    public Task<MixedTapeDto?> GetMixedTapeByIdAsync(string id) => GetAsync<MixedTapeDto>($"mixed-tapes/{id}");

    // ── Helpers ──
    public string ArtworkUrl(Guid? artworkId) =>
        artworkId.HasValue ? $"{BaseUrl}/api/artwork/{artworkId}" : string.Empty;

    public string StreamUrl(Guid trackId) => $"{BaseUrl}/api/stream/tracks/{trackId}";

    private async Task<T?> PostAsync<T>(string path, object body)
    {
        ApplyAuth();
        var resp = await _http.PostAsJsonAsync($"{BaseUrl}/api/{path}", body);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await TryRefreshTokenAsync())
            {
                ApplyAuth();
                resp = await _http.PostAsJsonAsync($"{BaseUrl}/api/{path}", body);
            }
        }
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength == 0) return default;
        return await resp.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    private async Task DeleteAsync(string path)
    {
        ApplyAuth();
        var resp = await _http.DeleteAsync($"{BaseUrl}/api/{path}");
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            if (await TryRefreshTokenAsync())
            {
                ApplyAuth();
                resp = await _http.DeleteAsync($"{BaseUrl}/api/{path}");
            }
        }
        resp.EnsureSuccessStatusCode();
    }

    // ── Auditoriums ──
    public async Task<List<AuditoriumDto>> GetAuditoriumsAsync()
    {
        ApplyAuth();
        var resp = await _http.GetAsync($"{BaseUrl}/api/auditoriums");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<AuditoriumDto>>(JsonOpts)) ?? [];
    }

    // ── Account ──
    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        ApplyAuth();
        var resp = await _http.PostAsJsonAsync($"{BaseUrl}/api/auth/change-password",
            new { currentPassword, newPassword });
        return resp.IsSuccessStatusCode;
    }
}
