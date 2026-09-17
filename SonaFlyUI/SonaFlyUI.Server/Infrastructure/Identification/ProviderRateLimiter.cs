using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Infrastructure.Configuration;

namespace SonaFlyUI.Server.Infrastructure.Identification;

/// <summary>
/// Spaces requests to one external service at least a fixed interval apart,
/// process-wide (upgrade plan 7.3). Callers wait their turn in order.
/// </summary>
public sealed class ProviderRateLimiter
{
    /// <summary>
    /// The one MusicBrainz limiter for the whole process. Artwork search and
    /// identification lookups share it so their combined traffic stays under
    /// the service's one-request-per-second rule.
    /// </summary>
    public static readonly ProviderRateLimiter MusicBrainz = new(TimeSpan.FromMilliseconds(1100));

    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly TimeSpan _minInterval;
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public ProviderRateLimiter(TimeSpan minInterval)
    {
        _minInterval = minInterval;
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        await _turn.WaitAsync(ct);
        try
        {
            var wait = _lastRequestUtc + _minInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _turn.Release();
        }
    }
}

/// <summary>The AcoustID limiter, sized from configuration. Registered as a singleton.</summary>
public sealed class AcoustIdThrottle
{
    public AcoustIdThrottle(IOptions<IdentificationOptions> options)
        : this(new ProviderRateLimiter(TimeSpan.FromSeconds(1.0 / options.Value.AcoustIdRequestsPerSecond)))
    {
    }

    public AcoustIdThrottle(ProviderRateLimiter limiter)
    {
        Limiter = limiter;
    }

    public ProviderRateLimiter Limiter { get; }
}

/// <summary>The MusicBrainz limiter handed to clients. Registered as a singleton.</summary>
public sealed class MusicBrainzThrottle
{
    public MusicBrainzThrottle() : this(ProviderRateLimiter.MusicBrainz)
    {
    }

    public MusicBrainzThrottle(ProviderRateLimiter limiter)
    {
        Limiter = limiter;
    }

    public ProviderRateLimiter Limiter { get; }
}
