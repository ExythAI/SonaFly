using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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

    /// <summary>Hard ceiling on a downloaded cover image. Cover art is tens to hundreds of KB.</summary>
    private const int MaxImageBytes = 8 * 1024 * 1024;

    /// <summary>Per-request timeout, so one stalled response cannot hold up a whole scan.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    // Cache confirmed "no artwork exists" lookups so we do not retry the same album repeatedly.
    // Transient failures (network down, 5xx, rate limiting) are deliberately NOT cached: caching
    // them would hide artwork for a whole day because of a momentary outage (backlog N22).
    private static readonly ConcurrentDictionary<string, DateTime> _notFoundCache = new();
    private static readonly TimeSpan _notFoundExpiry = TimeSpan.FromHours(24);
    private const int MaxNotFoundEntries = 10_000;

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

        var cacheKey = $"{artistName?.ToLowerInvariant()}|{albumTitle.ToLowerInvariant()}";
        if (_notFoundCache.TryGetValue(cacheKey, out var cachedAt))
        {
            if (DateTime.UtcNow - cachedAt < _notFoundExpiry)
            {
                _logger.LogDebug("Skipping artwork lookup for '{Artist} - {Album}' (cached not-found)", artistName, albumTitle);
                return null;
            }

            _notFoundCache.TryRemove(cacheKey, out _);
        }

        try
        {
            // Step 1: Search MusicBrainz for matching releases
            var mbids = await SearchReleaseMbidsAsync(artistName, albumTitle, ct);
            if (mbids == null || mbids.Count == 0)
            {
                CacheNotFound(cacheKey);
                return null;
            }

            // Step 2: Try each release until we find one with cover art
            foreach (var mbid in mbids)
            {
                var result = await DownloadCoverArtAsync(mbid, ct);
                if (result != null) return result;
            }

            // None of the releases had cover art — that is a confirmed negative.
            CacheNotFound(cacheKey);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A cancelled scan must stop promptly rather than being absorbed as "no artwork".
            throw;
        }
        catch (TransientArtworkLookupException ex)
        {
            _logger.LogWarning("Artwork lookup for '{Artist} - {Album}' failed transiently: {Reason}",
                artistName, albumTitle, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch online artwork for '{Artist} - {Album}'", artistName, albumTitle);
            return null;
        }
    }

    private static void CacheNotFound(string cacheKey)
    {
        if (_notFoundCache.Count >= MaxNotFoundEntries)
        {
            // Bounded cache: drop the oldest half rather than growing without limit.
            foreach (var stale in _notFoundCache.OrderBy(kv => kv.Value).Take(MaxNotFoundEntries / 2).ToList())
                _notFoundCache.TryRemove(stale.Key, out _);
        }

        _notFoundCache[cacheKey] = DateTime.UtcNow;
    }

    private async Task<List<string>?> SearchReleaseMbidsAsync(string? artist, string album, CancellationToken ct)
    {
        await RateLimitAsync(ct);

        var query = string.IsNullOrWhiteSpace(artist)
            ? $"release:\"{album}\""
            : $"release:\"{album}\" AND artist:\"{artist}\"";

        var url = $"https://musicbrainz.org/ws/2/release/?query={Uri.EscapeDataString(query)}&limit=5&fmt=json";

        using var timeout = CreateTimeoutScope(ct);
        using var response = await SendAsync(url, timeout.Token, ct);

        if (!response.IsSuccessStatusCode)
        {
            if (IsTransient(response.StatusCode))
                throw new TransientArtworkLookupException($"MusicBrainz search returned {(int)response.StatusCode}.");

            _logger.LogDebug("MusicBrainz search returned {StatusCode}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<MbSearchResult>(timeout.Token);
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

        using var timeout = CreateTimeoutScope(ct);

        // Try the 500px thumbnail first
        var url = $"https://coverartarchive.org/release/{mbid}/front-500";
        using var thumbnailResponse = await SendAsync(url, timeout.Token, ct);

        HttpResponseMessage response = thumbnailResponse;
        HttpResponseMessage? fullSizeResponse = null;

        try
        {
            if (!thumbnailResponse.IsSuccessStatusCode)
            {
                if (IsTransient(thumbnailResponse.StatusCode))
                    throw new TransientArtworkLookupException($"Cover Art Archive returned {(int)thumbnailResponse.StatusCode}.");

                // Try full-size image
                await RateLimitAsync(ct);
                url = $"https://coverartarchive.org/release/{mbid}/front";
                fullSizeResponse = await SendAsync(url, timeout.Token, ct);
                response = fullSizeResponse;

                if (!response.IsSuccessStatusCode)
                {
                    if (IsTransient(response.StatusCode))
                        throw new TransientArtworkLookupException($"Cover Art Archive returned {(int)response.StatusCode}.");

                    _logger.LogDebug("No cover art for release {Mbid} (tried front-500 and front)", mbid);
                    return null;
                }
            }

            var data = await ReadBoundedAsync(response, mbid, timeout.Token);
            if (data == null) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

            _logger.LogInformation("Downloaded cover art for release {Mbid} ({Bytes} bytes)", mbid, data.Length);
            return (data, contentType);
        }
        finally
        {
            fullSizeResponse?.Dispose();
        }
    }

    /// <summary>
    /// Streams the body with an explicit byte ceiling instead of buffering whatever the remote
    /// server decides to send.
    /// </summary>
    private async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage response, string mbid, CancellationToken ct)
    {
        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > MaxImageBytes)
        {
            _logger.LogWarning("Rejecting cover art for release {Mbid}: {Bytes} bytes exceeds the {Limit} byte limit.",
                mbid, declaredLength, MaxImageBytes);
            return null;
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(capacity: (int)Math.Min(declaredLength ?? 64 * 1024, MaxImageBytes));

        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes)
            {
                _logger.LogWarning("Rejecting cover art for release {Mbid}: body exceeded the {Limit} byte limit.",
                    mbid, MaxImageBytes);
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken effectiveToken, CancellationToken scanToken)
    {
        try
        {
            return await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, effectiveToken);
        }
        catch (OperationCanceledException) when (!scanToken.IsCancellationRequested)
        {
            // Our own timeout fired, not the scan being cancelled.
            throw new TransientArtworkLookupException($"Request to {url} timed out after {RequestTimeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new TransientArtworkLookupException(ex.Message);
        }
    }

    /// <summary>
    /// MusicBrainz asks for at most one request per second. The limiter is shared with
    /// identification lookups so their combined traffic stays within that rule.
    /// </summary>
    private static Task RateLimitAsync(CancellationToken ct) =>
        Identification.ProviderRateLimiter.MusicBrainz.WaitAsync(ct);

    private static CancellationTokenSource CreateTimeoutScope(CancellationToken ct)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(RequestTimeout);
        return linked;
    }

    private static bool IsTransient(HttpStatusCode status)
        => (int)status >= 500 || status == HttpStatusCode.RequestTimeout || status == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// A failure that says nothing about whether artwork exists, and therefore must not be
    /// recorded in the not-found cache.
    /// </summary>
    private sealed class TransientArtworkLookupException(string message) : Exception(message);

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
