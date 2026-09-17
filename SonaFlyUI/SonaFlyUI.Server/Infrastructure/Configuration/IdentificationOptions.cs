using System.ComponentModel.DataAnnotations;

namespace SonaFlyUI.Server.Infrastructure.Configuration;

/// <summary>
/// Settings for the music identification subsystem (upgrade plan, section 15.1).
/// Bound from the <c>SonaFly:Identification</c> configuration section.
/// Secrets (AcoustID client key) come from environment variables, user secrets,
/// or an OS secret store. They are never written to logs or status APIs.
/// </summary>
public sealed class IdentificationOptions
{
    public const string SectionName = "SonaFly:Identification";

    /// <summary>
    /// Master switch. When false, the ordinary scan and playback paths behave
    /// exactly as before and the identification endpoints refuse to queue work.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Path to the fpcalc executable (Chromaprint). Empty means not configured.</summary>
    public string? FpcalcPath { get; set; }

    /// <summary>Path to the ffprobe executable. Empty means not configured.</summary>
    public string? FfprobePath { get; set; }

    /// <summary>Path to the ffmpeg executable. Empty means not configured.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>
    /// AcoustID application client key. This is a secret: never include it in
    /// logs, error messages, cache keys, or API responses.
    /// </summary>
    public string? AcoustIdClientKey { get; set; }

    /// <summary>
    /// User-Agent sent to the MusicBrainz API. Configure a meaningful value
    /// with contact details before making live requests.
    /// </summary>
    public string MusicBrainzUserAgent { get; set; } = "SonaFly/1.0";

    /// <summary>Bounded worker pool for local hash, probe, and fingerprint work.</summary>
    [Range(1, 32)]
    public int MaxLocalWorkers { get; set; } = 2;

    /// <summary>Bounded retries for transient provider failures (429, 503, timeouts).</summary>
    [Range(0, 10)]
    public int MaxProviderRetries { get; set; } = 3;

    /// <summary>Per-request timeout for provider calls, in seconds.</summary>
    [Range(5, 300)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Ceiling for AcoustID lookups per second. The cited service rule asks for
    /// no more than three; recheck the current terms before release.
    /// </summary>
    [Range(1, 3)]
    public double AcoustIdRequestsPerSecond { get; set; } = 3;

    /// <summary>
    /// Ceiling for MusicBrainz API requests per second. The cited service rule
    /// asks for no more than one; recheck the current terms before release.
    /// </summary>
    [Range(1, 1)]
    public double MusicBrainzRequestsPerSecond { get; set; } = 1;

    /// <summary>Fallback providers are opt-in and arrive in a later milestone.</summary>
    public bool EnableAudDFallback { get; set; }

    /// <summary>Fallback providers are opt-in and arrive in a later milestone.</summary>
    public bool EnableAcrCloudFallback { get; set; }

    /// <summary>
    /// Source-file tag writing and renames. Default false. A database flag alone
    /// cannot make a read-only Docker mount writable; enabling this also needs a
    /// writable mount and OS permission for the container user (plan 14.3).
    /// </summary>
    public bool AllowSourceFileWrites { get; set; }

    /// <summary>Identifier of the active scoring rules, stored with every score.</summary>
    public string ScoringVersion { get; set; } = "v0.1-recording-only";

    /// <summary>
    /// Full identification milestone gate (review backlog U07). False means the
    /// server offers the recording-only preview: title and artist proposals
    /// with release identity labeled unresolved. Exact-edition fields need
    /// this flag plus an approved release decision.
    /// </summary>
    public bool EnableExactReleaseApply { get; set; }

    /// <summary>Score at or above which a candidate is shown in the High band.</summary>
    [Range(0, 1)]
    public double HighConfidenceThreshold { get; set; } = 0.85;

    /// <summary>Score at or above which a candidate is shown in the Review band.</summary>
    [Range(0, 1)]
    public double ReviewThreshold { get; set; } = 0.6;

    public bool HasAcoustIdKey => string.IsNullOrWhiteSpace(AcoustIdClientKey) == false;

    public bool HasFpcalc => string.IsNullOrWhiteSpace(FpcalcPath) == false;

    /// <summary>
    /// Operator-safe configuration blockers shown in the UI before a job is
    /// queued (plan 13.2). Contains no secret values. The key state comes
    /// from the caller so UI-stored keys count as configured.
    /// </summary>
    public IReadOnlyList<string> GetBlockers() => GetBlockers(HasAcoustIdKey);

    public IReadOnlyList<string> GetBlockers(bool acoustIdConfigured)
    {
        var blockers = new List<string>();
        if (Enabled == false)
        {
            blockers.Add("Music identification is disabled (SonaFly:Identification:Enabled is false).");
            return blockers;
        }

        if (acoustIdConfigured == false)
        {
            blockers.Add("No AcoustID client key is configured, so online lookups are unavailable. Local evidence collection can still run.");
        }

        if (HasFpcalc == false)
        {
            blockers.Add("No fpcalc executable is configured, so acoustic fingerprinting will be skipped until one is installed.");
        }

        return blockers;
    }
}

public static class IdentificationOptionsRegistration
{
    /// <summary>
    /// Binds and validates <see cref="IdentificationOptions"/> at startup, so
    /// out-of-range values (for example provider rate limits above the
    /// documented service ceilings) fail fast instead of binding silently.
    /// </summary>
    public static IServiceCollection AddIdentificationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentificationOptions>()
            .Bind(configuration.GetSection(IdentificationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.ReviewThreshold <= o.HighConfidenceThreshold,
                "SonaFly:Identification:ReviewThreshold must not exceed HighConfidenceThreshold.")
            .ValidateOnStart();
        return services;
    }
}

