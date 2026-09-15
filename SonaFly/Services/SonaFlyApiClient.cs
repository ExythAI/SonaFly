using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SonaFly.Models;

namespace SonaFly.Services;

/// <summary>
/// One server's identity and credentials, captured at the start of a logical request.
///
/// The app can hold several configured servers and the active one can change at any
/// moment, including while requests are in flight. Every request therefore works from
/// an immutable snapshot rather than re-reading the active server part by part: reading
/// the base URL and the token as two separate lookups is what allowed one server's
/// access token to be sent to another server's host.
/// </summary>
internal sealed record ServerSnapshot(string ServerId, string BaseUrl, string? AccessToken, string? RefreshToken)
{
    public string Url(string path) => $"{BaseUrl}/api/{path}";
}

public class SonaFlyApiClient
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
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

    public string BaseUrl => _storage.GetActive()?.BaseUrl?.TrimEnd('/')
                             ?? throw new InvalidOperationException("No active server.");

    public string? AccessToken => _storage.GetActive()?.AccessToken;

    // ── Snapshots ──

    private static ServerSnapshot Snapshot(ServerConfig server) =>
        new(server.Id, server.BaseUrl.TrimEnd('/'), server.AccessToken, server.RefreshToken);

    private ServerSnapshot ActiveServer() =>
        Snapshot(_storage.GetActive() ?? throw new InvalidOperationException("No active server."));

    /// <summary>
    /// The current state of <paramref name="serverId"/>, but only while it is still the
    /// active server. Returns null once the user has switched away, so an operation that
    /// began against one server is abandoned rather than retargeted at another.
    /// </summary>
    private ServerSnapshot? StillActive(string serverId)
    {
        var active = _storage.GetActive();
        return active is not null && active.Id == serverId ? Snapshot(active) : null;
    }

    // ── Transport ──

    /// <summary>
    /// Builds a request carrying its own Authorization header.
    /// </summary>
    /// <remarks>
    /// Never sets HttpClient.DefaultRequestHeaders: those are shared by every in-flight
    /// request, so with overlapping requests against different servers the header that
    /// arrives is whichever one was written last.
    /// </remarks>
    private static HttpRequestMessage BuildRequest(
        ServerSnapshot server, HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, server.Url(path));
        if (server.AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AccessToken);
        }
        if (body is not null)
        {
            // Re-created per attempt: content cannot be replayed on the retry.
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    /// <summary>
    /// Turns a redirect into an explanation. The API never legitimately redirects, so one
    /// means the server address is wrong — almost always http:// against a server that
    /// only serves https.
    /// </summary>
    /// <remarks>
    /// Worth its own message because the alternative is unreadable. Following the redirect
    /// rewrites a POST as a GET, the server answers an unmatched GET with its web page, and
    /// the app reports a JSON parsing error about an unexpected '&lt;' — which tells nobody
    /// that the real problem is a missing "s".
    /// </remarks>
    /// <exception cref="InvalidOperationException">The response was a redirect.</exception>
    private static void RejectRedirect(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        if (code is < 300 or > 399) return;

        var target = response.Headers.Location?.ToString();
        var scheme = target is not null && target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? " Use https:// in the server address."
            : string.Empty;

        throw new InvalidOperationException(
            target is not null
                ? $"The server redirected to {target}.{scheme}"
                : "The server redirected this request. Check the server address.");
    }

    /// <summary>
    /// Sends one request against <paramref name="server"/>, refreshing and retrying once
    /// on 401. The retry is bound to the same server; if the active server changed in the
    /// meantime the refresh is abandoned and the 401 stands.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        ServerSnapshot server, HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        using (var request = BuildRequest(server, method, path, body))
        {
            response = await _http.SendAsync(request, ct);
        }

        RejectRedirect(response);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var refreshed = await TryRefreshAsync(server, ct);
        if (refreshed is null)
        {
            return response;
        }

        response.Dispose();
        using (var retry = BuildRequest(refreshed, method, path, body))
        {
            return await _http.SendAsync(retry, ct);
        }
    }

    // ── Auth ──

    public async Task<LoginResponse> LoginAsync(string baseUrl, string username, string password)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/auth/login";
        using var resp = await _http.PostAsJsonAsync(url, new { username, password });

        // Before EnsureSuccessStatusCode, so the reason given is the real one.
        RejectRedirect(resp);
        resp.EnsureSuccessStatusCode();

        return (await resp.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts))!;
    }

    /// <summary>
    /// Rotates the active server's tokens. Exposed for callers outside a request.
    /// </summary>
    public async Task<bool> TryRefreshTokenAsync(string? failedAccessToken = null)
    {
        var active = _storage.GetActive();
        if (active is null) return false;

        // Attribute the attempt to the token that actually failed, so a rotation that
        // already happened elsewhere is recognised instead of repeated.
        var attempted = Snapshot(active) with { AccessToken = failedAccessToken ?? active.AccessToken };
        return await TryRefreshAsync(attempted, CancellationToken.None) is not null;
    }

    /// <summary>
    /// Single-flight token rotation for one server.
    /// </summary>
    /// <returns>
    /// A snapshot with usable credentials for that same server, or null when the rotation
    /// failed, the session was ended, or the user switched servers while it was running.
    /// </returns>
    private async Task<ServerSnapshot?> TryRefreshAsync(ServerSnapshot attempted, CancellationToken ct)
    {
        if (attempted.RefreshToken is null) return null;

        await _refreshGate.WaitAsync(ct);
        try
        {
            // Everything below is re-read after taking the gate: another caller may have
            // rotated, or the user may have logged out or switched servers, while waiting.
            var current = StillActive(attempted.ServerId);
            if (current is null) return null;

            // Someone else already rotated this server's tokens; reuse their result.
            if (current.AccessToken is not null && current.AccessToken != attempted.AccessToken)
            {
                return current;
            }

            // The refresh token we were given is no longer the stored one: the session was
            // replaced or cleared, and reviving it would resurrect a logged-out session.
            if (current.RefreshToken is null || current.RefreshToken != attempted.RefreshToken)
            {
                return null;
            }

            RefreshResponse? result;
            using (var request = new HttpRequestMessage(HttpMethod.Post, current.Url("auth/refresh")))
            {
                request.Content = JsonContent.Create(new { refreshToken = current.RefreshToken });
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return null;
                result = await response.Content.ReadFromJsonAsync<RefreshResponse>(JsonOpts, ct);
            }

            if (result is null) return null;

            // Re-check once more before writing: the rotation took time, and the result
            // must not be stored against a server the user has since left, nor overwrite
            // a session that was cleared while the request was out.
            var after = StillActive(attempted.ServerId);
            if (after is null || after.RefreshToken != current.RefreshToken) return null;

            await _storage.UpdateTokensAsync(
                attempted.ServerId, result.AccessToken, result.RefreshToken, result.ExpiresUtc);

            return current with { AccessToken = result.AccessToken, RefreshToken = result.RefreshToken };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    // ── Verbs ──

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct = default) =>
        await ReadAsync<T>(ActiveServer(), HttpMethod.Get, path, null, ct);

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct = default) =>
        await ReadAsync<T>(ActiveServer(), HttpMethod.Post, path, body, ct);

    private async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var resp = await SendAsync(ActiveServer(), HttpMethod.Delete, path, null, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task<T?> ReadAsync<T>(
        ServerSnapshot server, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var resp = await SendAsync(server, method, path, body, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength == 0) return default;
        return await resp.Content.ReadFromJsonAsync<T>(JsonOpts, ct);
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

    public async Task<string> GetStreamUrlAsync(Guid trackId, CancellationToken ct = default)
    {
        // One snapshot for both the request and the resolution of the relative URL it
        // returns, so the ticket is always played back against the server that issued it.
        var server = ActiveServer();
        var result = await ReadAsync<StreamUrlResponse>(
                server, HttpMethod.Get, $"stream/tracks/{trackId}/url", null, ct)
            ?? throw new InvalidOperationException("No streaming URL returned.");

        return new Uri(new Uri(server.BaseUrl + "/"), result.Url).AbsoluteUri;
    }

    private record StreamUrlResponse(string Url);

    // ── Auditoriums ──
    public async Task<List<AuditoriumDto>> GetAuditoriumsAsync() =>
        await GetAsync<List<AuditoriumDto>>("auditoriums") ?? [];

    // ── Account ──

    /// <summary>
    /// Changes the signed-in account's password and keeps the session alive.
    /// </summary>
    /// <remarks>
    /// The server revokes every token issued under the old password — including the one this
    /// request travelled with — and hands back a replacement pair. Reading only the status
    /// code and throwing the body away leaves the app holding credentials the server has
    /// already discarded, and the next request fails for no visible reason.
    ///
    /// The replacement is stored against the server the request was sent to, and only while
    /// that is still the session it belongs to: the user may switch servers or sign out while
    /// this is in flight, and one server's credentials must never be written over another's.
    /// </remarks>
    public async Task<ChangePasswordResult> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var server = ActiveServer();

        ChangePasswordResponse? replacement;
        using (var resp = await SendAsync(server, HttpMethod.Post, "auth/change-password",
                   new { currentPassword, newPassword }))
        {
            if (!resp.IsSuccessStatusCode) return ChangePasswordResult.Rejected;

            try
            {
                replacement = await resp.Content.ReadFromJsonAsync<ChangePasswordResponse>(JsonOpts);
            }
            catch (JsonException)
            {
                replacement = null;
            }
        }

        // Past this point the password HAS changed, whatever else goes wrong.
        if (replacement?.RefreshToken is null) return ChangePasswordResult.ChangedButSignedOut;

        var after = StillActive(server.ServerId);
        if (after is null || after.RefreshToken != server.RefreshToken)
        {
            return ChangePasswordResult.ChangedButSignedOut;
        }

        await _storage.UpdateTokensAsync(
            server.ServerId, replacement.AccessToken, replacement.RefreshToken, replacement.ExpiresUtc);
        _storage.SetMustChangePassword(server.ServerId, false);

        return ChangePasswordResult.Changed;
    }

    /// <summary>
    /// Ends the session on the server it was started on.
    /// </summary>
    /// <remarks>
    /// Clearing the tokens locally is not signing out: the refresh family stays usable, so
    /// anyone holding a copy of the refresh token keeps the account until it expires a week
    /// later. The server is told, bound to the snapshot captured here so a switch mid-request
    /// cannot send one server's token to another.
    ///
    /// Best effort by design — signing out must work on a plane. The caller clears the local
    /// credentials regardless of what this returns.
    /// </remarks>
    /// <returns>True when the server confirmed the session was ended.</returns>
    public async Task<bool> LogoutAsync()
    {
        var server = _storage.GetActive();
        if (server is null) return false;

        var snapshot = Snapshot(server);
        if (snapshot.RefreshToken is null) return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, snapshot.Url("auth/logout"));
            request.Content = JsonContent.Create(new { refreshToken = snapshot.RefreshToken });
            if (snapshot.AccessToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", snapshot.AccessToken);
            }

            using var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
