using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Configuration;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Typed MusicBrainz recording client (upgrade plan 7.2). Requests go through
/// the process-wide MusicBrainz limiter shared with artwork search, carry the
/// configured User-Agent, and use HTTPS. A recording is not an album edition:
/// this client deliberately fetches recordings only.
/// </summary>
public sealed class MusicBrainzClient : IMusicBrainzClient
{
    public const string ProviderName = "MusicBrainz";

    private readonly HttpClient _http;
    private readonly MusicBrainzThrottle _throttle;
    private readonly string _userAgent;

    public MusicBrainzClient(HttpClient http, MusicBrainzThrottle throttle, IOptions<IdentificationOptions> options)
    {
        _http = http;
        _throttle = throttle;
        _userAgent = options.Value.MusicBrainzUserAgent;
    }

    public async Task<string?> GetRecordingJsonAsync(string recordingId, CancellationToken ct)
    {
        if (Guid.TryParse(recordingId, out var id) == false)
        {
            return null;
        }

        await _throttle.Limiter.WaitAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://musicbrainz.org/ws/2/recording/{id:D}?inc=artist-credits+isrcs&fmt=json");
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return null;
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable ||
                (int)response.StatusCode >= 500)
            {
                throw new IdentificationStepException(IdentificationErrorCategory.MusicBrainzLookup, retryable: true,
                    $"MusicBrainz is busy or unavailable (HTTP {(int)response.StatusCode}).",
                    response.Headers.RetryAfter?.Delta);
            }

            if (response.IsSuccessStatusCode == false)
            {
                throw new IdentificationStepException(IdentificationErrorCategory.MusicBrainzLookup, retryable: false,
                    $"MusicBrainz refused the recording lookup (HTTP {(int)response.StatusCode}).");
            }

            return body;
        }
        catch (TaskCanceledException ex) when (ct.IsCancellationRequested == false)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.MusicBrainzLookup, retryable: true,
                "The MusicBrainz lookup timed out.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.MusicBrainzLookup, retryable: true,
                "MusicBrainz could not be reached.", inner: ex);
        }
    }

    /// <summary>Parses a MusicBrainz recording response. Returns null when it is not one.</summary>
    public static MusicBrainzRecording? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty("id", out var id) == false ||
                id.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var artists = new List<string>();
            if (root.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
            {
                foreach (var credit in credits.EnumerateArray())
                {
                    if (credit.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    {
                        artists.Add(name.GetString()!);
                    }
                }
            }

            var isrcs = new List<string>();
            if (root.TryGetProperty("isrcs", out var isrcArray) && isrcArray.ValueKind == JsonValueKind.Array)
            {
                isrcs.AddRange(isrcArray.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!));
            }

            // MusicBrainz reports recording length in milliseconds.
            double? duration = root.TryGetProperty("length", out var length) && length.ValueKind == JsonValueKind.Number
                ? length.GetDouble() / 1000.0
                : null;
            string? title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

            return new MusicBrainzRecording(id.GetString()!, title, artists, duration, isrcs);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
