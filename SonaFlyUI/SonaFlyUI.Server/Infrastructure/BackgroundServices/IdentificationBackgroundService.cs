using Microsoft.Extensions.Options;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Identification;

namespace SonaFlyUI.Server.Infrastructure.BackgroundServices;

/// <summary>
/// Hosts the identification runner. Idle and silent when identification is
/// disabled, so the scan and playback paths behave exactly as before.
/// </summary>
public sealed class IdentificationBackgroundService : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

    private readonly IdentificationJobRunner _runner;
    private readonly IdentificationOptions _options;
    private readonly ILogger<IdentificationBackgroundService> _logger;

    public IdentificationBackgroundService(
        IdentificationJobRunner runner,
        IOptions<IdentificationOptions> options,
        ILogger<IdentificationBackgroundService> logger)
    {
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Enabled == false)
        {
            _logger.LogInformation("Music identification is disabled; the identification worker is not running.");
            return;
        }

        _logger.LogInformation(
            "Music identification worker started (fingerprinting {Fingerprinting}, {Workers} local worker(s)).",
            _options.HasFpcalc ? "configured" : "not configured", _options.MaxLocalWorkers);

        try
        {
            await _runner.ReclaimLeasesAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not release identification leases from the previous run.");
        }

        while (stoppingToken.IsCancellationRequested == false)
        {
            try
            {
                if (await _runner.RunOnceAsync(stoppingToken) == false)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The identification worker hit an unexpected error; retrying shortly.");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }
}
