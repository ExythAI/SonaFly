using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Domain.Enums;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identification;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

/// <summary>
/// Admin-managed server settings, including secrets. Secret values are
/// accepted on write but never returned: reads only report whether a value
/// is set and where it comes from. Every change is audit-logged with the
/// acting user and never with the value itself.
/// </summary>
[ApiController]
[Route("api/settings")]
[Authorize(Roles = "Admin")]
public class ServerSettingsController : ControllerBase
{
    private readonly IServerSettingsService _settings;
    private readonly IAcoustIdClient _acoustId;
    private readonly SonaFlyDbContext _db;
    private readonly ILogger<ServerSettingsController> _logger;

    public ServerSettingsController(
        IServerSettingsService settings,
        IAcoustIdClient acoustId,
        SonaFlyDbContext db,
        ILogger<ServerSettingsController> logger)
    {
        _settings = settings;
        _acoustId = acoustId;
        _db = db;
        _logger = logger;
    }

    /// <summary>Safe identification key state for the admin UI. No secrets.</summary>
    [HttpGet("identification")]
    public async Task<ActionResult<IdentificationKeyStateDto>> GetIdentificationKeys(CancellationToken ct)
    {
        var source = await _settings.GetAcoustIdKeySourceAsync(ct);
        return Ok(new IdentificationKeyStateDto(
            source != AcoustIdKeySource.None, source.ToString()));
    }

    /// <summary>
    /// Checks a key with AcoustID without saving it: the key in the request
    /// when one is given, otherwise the key the server currently uses.
    /// </summary>
    [HttpPost("identification/acoustid-key/test")]
    public async Task<ActionResult<AcoustIdKeyTestResultDto>> TestAcoustIdKey(
        [FromBody] TestAcoustIdKeyRequest? request, CancellationToken ct)
    {
        var entered = request?.Key?.Trim();
        string? key;
        string tested;
        if (string.IsNullOrEmpty(entered) == false)
        {
            key = entered;
            tested = "the entered key";
        }
        else
        {
            key = await _settings.GetEffectiveAcoustIdKeyAsync(ct);
            tested = (await _settings.GetAcoustIdKeySourceAsync(ct)) == AcoustIdKeySource.Configuration
                ? "the key from the server configuration"
                : "the saved key";
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Ok(new AcoustIdKeyTestResultDto(false, false, "No AcoustID key is saved yet. Enter one to test it."));
        }

        var check = await CheckKeyAsync(key, ct);
        return Ok(check.Checked
            ? new AcoustIdKeyTestResultDto(true, check.Accepted,
                check.Accepted ? $"AcoustID accepted {tested}." : check.Message)
            : new AcoustIdKeyTestResultDto(false, false, check.Message));
    }

    /// <summary>
    /// Stores the AcoustID client key from the admin UI, encrypted at rest,
    /// after AcoustID confirms it. A rejected key is not saved. If AcoustID
    /// cannot be reached the key is saved unverified, and the response says
    /// so. A stored key wins over the configuration-file value until cleared.
    /// </summary>
    [HttpPut("identification/acoustid-key")]
    public async Task<ActionResult<IdentificationKeySaveResultDto>> SetAcoustIdKey(
        [FromBody] SetAcoustIdKeyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return BadRequest(new { message = "A key value is required." });
        }

        var key = request.Key.Trim();
        if (key.Length > ServerSettingsService.MaxSecretLength)
        {
            return BadRequest(new { message = "The key value is too long." });
        }

        var check = await CheckKeyAsync(key, ct);
        if (check.Checked && check.Accepted == false)
        {
            return BadRequest(new { message = check.Message + " The key was not saved." });
        }

        var userId = CurrentUserId();
        await _settings.SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, key, userId, ct);
        _logger.LogWarning("AcoustID client key saved from the admin UI by user {UserId} (verified: {Verified}).", userId, check.Checked);

        if (check.Checked == false)
        {
            return Ok(new IdentificationKeySaveResultDto(true, AcoustIdKeySource.Database.ToString(), false,
                "Key saved, but it could not be checked: " + check.Message + " Use Test key to check it later."));
        }

        var resumed = await ResumeJobsPausedForAdministratorAsync(ct);
        var message = resumed == 0
            ? "AcoustID accepted the key and it was saved."
            : $"AcoustID accepted the key and it was saved. {resumed} paused identification job{(resumed == 1 ? " was" : "s were")} resumed.";
        return Ok(new IdentificationKeySaveResultDto(true, AcoustIdKeySource.Database.ToString(), true, message));
    }

    /// <summary>
    /// Clears the UI-stored key; the configuration-file value applies again.
    /// </summary>
    [HttpDelete("identification/acoustid-key")]
    public async Task<ActionResult<IdentificationKeyStateDto>> ClearAcoustIdKey(CancellationToken ct)
    {
        var userId = CurrentUserId();
        await _settings.SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, null, userId, ct);
        _logger.LogWarning("UI-stored AcoustID client key cleared by user {UserId}.", userId);

        var source = await _settings.GetAcoustIdKeySourceAsync(ct);
        return Ok(new IdentificationKeyStateDto(
            source != AcoustIdKeySource.None, source.ToString()));
    }

    private async Task<(bool Checked, bool Accepted, string Message)> CheckKeyAsync(string key, CancellationToken ct)
    {
        try
        {
            return await _acoustId.IsKeyAcceptedAsync(key, ct)
                ? (true, true, "AcoustID accepted the key.")
                : (true, false, AcoustIdClient.RejectedKeyMessage);
        }
        catch (IdentificationStepException ex)
        {
            return (false, false, ex.Message);
        }
    }

    /// <summary>A verified key unblocks jobs that paused because the key was rejected.</summary>
    private Task<int> ResumeJobsPausedForAdministratorAsync(CancellationToken ct) =>
        _db.IdentificationJobs
            .Where(j => j.Status == IdentificationJobStatus.Paused && j.Stage == IdentificationJobRunner.AdministratorPauseStage)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, IdentificationJobStatus.Queued)
                .SetProperty(j => j.Stage, "Queued")
                .SetProperty(j => j.ErrorSummary, (string?)null)
                .SetProperty(j => j.CancelRequested, false)
                .SetProperty(j => j.ModifiedUtc, DateTime.UtcNow), ct);

    private Guid? CurrentUserId()
    {
        var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
