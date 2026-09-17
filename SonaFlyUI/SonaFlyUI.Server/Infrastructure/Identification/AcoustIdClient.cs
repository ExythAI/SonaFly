using System.Globalization;
using System.Net;
using System.Text.Json;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Domain.Enums;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Typed AcoustID lookup client (upgrade plan 7.1). The client key travels in
/// the POST body, never the URL, so it cannot leak through request logging.
/// Every result is kept: the caller scores them, and nothing here picks a
/// winner. Fingerprints are only looked up, never submitted.
/// </summary>
public sealed class AcoustIdClient : IAcoustIdClient
{
    public const string ProviderName = "AcoustID";

    private static readonly Uri LookupUri = new("https://api.acoustid.org/v2/lookup");

    /// <summary>AcoustID error code for an unknown or revoked application key.</summary>
    private const int InvalidApiKeyCode = 4;

    /// <summary>AcoustID error code for exceeding the request rate.</summary>
    private const int RateLimitedCode = 14;

    private readonly HttpClient _http;
    private readonly AcoustIdThrottle _throttle;

    public AcoustIdClient(HttpClient http, AcoustIdThrottle throttle)
    {
        _http = http;
        _throttle = throttle;
    }

    public async Task<string> LookupJsonAsync(string apiKey, string fingerprint, int durationSeconds, CancellationToken ct)
    {
        await _throttle.Limiter.WaitAsync(ct);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client"] = apiKey,
            ["meta"] = "recordings",
            ["duration"] = durationSeconds.ToString(CultureInfo.InvariantCulture),
            ["fingerprint"] = fingerprint
        });

        HttpResponseMessage response;
        string body;
        try
        {
            response = await _http.PostAsync(LookupUri, content, ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (TaskCanceledException ex) when (ct.IsCancellationRequested == false)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.AcoustIdLookup, retryable: true,
                "The AcoustID lookup timed out.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.AcoustIdLookup, retryable: true,
                "AcoustID could not be reached.", inner: ex);
        }

        using (response)
        {
            return Validate(response.StatusCode, response.Headers.RetryAfter?.Delta, body);
        }
    }

    /// <summary>
    /// Maps an AcoustID response to its JSON or a categorized failure. Exposed
    /// for tests. Error messages from the service are not echoed back, since
    /// they can repeat request parameters.
    /// </summary>
    public static string Validate(HttpStatusCode status, TimeSpan? retryAfter, string body)
    {
        var (errorCode, isOk) = ReadStatus(body);

        if (errorCode == InvalidApiKeyCode)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.Configuration, retryable: false,
                "AcoustID rejected the client key. Save a valid key on the Music folders page, then resume the job.",
                requiresAdministrator: true);
        }

        if (status == HttpStatusCode.TooManyRequests || errorCode == RateLimitedCode)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.AcoustIdLookup, retryable: true,
                "AcoustID asked SonaFly to slow down.", retryAfter);
        }

        if ((int)status >= 500)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.AcoustIdLookup, retryable: true,
                $"AcoustID is temporarily unavailable (HTTP {(int)status}).", retryAfter);
        }

        if (isOk == false)
        {
            throw new IdentificationStepException(IdentificationErrorCategory.AcoustIdLookup, retryable: false,
                errorCode.HasValue
                    ? $"AcoustID refused the lookup (error {errorCode})."
                    : $"AcoustID returned an unexpected response (HTTP {(int)status}).");
        }

        return body;
    }

    /// <summary>Parses a validated lookup response into every result and recording.</summary>
    public static IReadOnlyList<AcoustIdResult> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("results", out var results) == false ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parsed = new List<AcoustIdResult>();
        foreach (var result in results.EnumerateArray())
        {
            var id = GetString(result, "id");
            if (id == null)
            {
                continue;
            }

            var score = result.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0;
            var recordings = new List<AcoustIdRecording>();
            if (result.TryGetProperty("recordings", out var recs) && recs.ValueKind == JsonValueKind.Array)
            {
                foreach (var recording in recs.EnumerateArray())
                {
                    var mbid = GetString(recording, "id");
                    if (mbid == null)
                    {
                        continue;
                    }

                    var artists = new List<string>();
                    if (recording.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array)
                    {
                        artists.AddRange(a.EnumerateArray().Select(x => GetString(x, "name")).OfType<string>());
                    }

                    double? duration = recording.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                        ? d.GetDouble()
                        : null;
                    recordings.Add(new AcoustIdRecording(mbid, GetString(recording, "title"), artists, duration));
                }
            }

            parsed.Add(new AcoustIdResult(id, Math.Clamp(score, 0, 1), recordings));
        }

        return parsed;
    }

    private static (int? ErrorCode, bool IsOk) ReadStatus(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, false);
            }

            int? code = root.TryGetProperty("error", out var error) &&
                        error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("code", out var c) &&
                        c.ValueKind == JsonValueKind.Number
                ? c.GetInt32()
                : null;
            return (code, GetString(root, "status") == "ok");
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.IsNullOrWhiteSpace(value.GetString()) == false
            ? value.GetString()
            : null;
}
