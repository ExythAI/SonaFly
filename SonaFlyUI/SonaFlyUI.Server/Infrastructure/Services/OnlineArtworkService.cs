using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>
/// Fetches album artwork from MusicBrainz Cover Art Archive.
/// Free API, no key required. Follows their rate limit (1 req/sec).
/// </summary>
public class OnlineArtworkService
{
    private readonly HttpClient _http;
    private readonly ILogger<OnlineArtworkService> _logger;
    private static readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;

    // Cache "not found" lookups so we don't retry the same album repeatedly
    private static readonly ConcurrentDictionary<string, DateTime> _notFoundCache = new();
    private static readonly TimeSpan _notFoundExpiry = TimeSpan.FromHours(24);

    public OnlineArtworkService(HttpClient http, ILogger<OnlineArtworkService> logger)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SonaFly/1.0 (personal-music-server)");
        _logger = logger;
    }

    /// <summary>
    /// Searches MusicBrainz for the release and downloads front cover art.
    /// Returns the image bytes and mime type, or null if not found.
    /// </summary>
    public async Task<(byte[] Data, string MimeType)?> FetchAlbumArtAsync(string? artistName, string? albumTitle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(albumTitle)) return null;

        // Check "not found" cache to avoid re-requesting
        var cacheKey = $"{artistName?.ToLowerInvariant()}|{albumTitle.ToLowerInvariant()}";
        if (_notFoundCache.TryGetValue(cacheKey, out var cachedAt) && DateTime.UtcNow - cachedAt < _notFoundExpiry)
        {
            _logger.LogDebug("Skipping artwork lookup for '{Artist} - {Album}' (cached not-found)", artistName, albumTitle);
            return null;
        }

        try
        {
            // Step 1: Search MusicBrainz for matching releases
            var mbids = await SearchReleaseMbidsAsync(artistName, albumTitle, ct);
            if (mbids == null || mbids.Count == 0)
            {
                _notFoundCache[cacheKey] = DateTime.UtcNow;
                return null;
            }

            // Step 2: Try each release until we find one with cover art
            foreach (var mbid in mbids)
            {
                var result = await DownloadCoverArtAsync(mbid, ct);
                if (result != null) return result;
            }

            // None of the releases had cover art — cache this
            _notFoundCache[cacheKey] = DateTime.UtcNow;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch online artwork for '{Artist} - {Album}'", artistName, albumTitle);
            return null;
        }
    }

    private async Task<List<string>?> SearchReleaseMbidsAsync(string? artist, string album, CancellationToken ct)
    {
        await RateLimitAsync(ct);

        var query = string.IsNullOrWhiteSpace(artist)
            ? $"release:\"{album}\""
            : $"release:\"{album}\" AND artist:\"{artist}\"";

        var url = $"https://musicbrainz.org/ws/2/release/?query={Uri.EscapeDataString(query)}&limit=5&fmt=json";

        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("MusicBrainz search returned {StatusCode}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<MbSearchResult>(ct);
        if (json?.Releases == null || json.Releases.Count == 0) return null;

        // Return all unique release IDs ordered by score
        var ids = json.Releases
            .Where(r => r.Id != null)
            .OrderByDescending(r => r.Score)
            .Select(r => r.Id!)
            .Distinct()
            .ToList();

        _logger.LogDebug("Found {Count} MusicBrainz releases for '{Album}'", ids.Count, album);
        return ids;
    }

    private async Task<(byte[] Data, string MimeType)?> DownloadCoverArtAsync(string mbid, CancellationToken ct)
    {
        await RateLimitAsync(ct);

        // Try the 500px thumbnail first
        var url = $"https://coverartarchive.org/release/{mbid}/front-500";
        var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            // Try full-size image
            await RateLimitAsync(ct);
            url = $"https://coverartarchive.org/release/{mbid}/front";
            response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("No cover art for release {Mbid} (tried front-500 and front)", mbid);
                return null;
            }
        }

        var data = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

        _logger.LogInformation("Downloaded cover art for release {Mbid} ({Bytes} bytes)", mbid, data.Length);
        return (data, contentType);
    }

    private static async Task RateLimitAsync(CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequest;
            if (elapsed.TotalMilliseconds < 1100)
            {
                await Task.Delay(1100 - (int)elapsed.TotalMilliseconds, ct);
            }
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    // MusicBrainz JSON response models
    private class MbSearchResult
    {
        [JsonPropertyName("releases")]
        public List<MbRelease>? Releases { get; set; }
    }

    private class MbRelease
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }
    }
}
