using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
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
    private readonly ILogger<ServerSettingsController> _logger;

    public ServerSettingsController(
        IServerSettingsService settings,
        ILogger<ServerSettingsController> logger)
    {
        _settings = settings;
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
    /// Stores the AcoustID client key from the admin UI, encrypted at rest.
    /// A stored key wins over the configuration-file value until cleared.
    /// </summary>
    [HttpPut("identification/acoustid-key")]
    public async Task<ActionResult<IdentificationKeyStateDto>> SetAcoustIdKey(
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

        var userId = CurrentUserId();
        await _settings.SetSecretAsync(ServerSettingKeys.AcoustIdClientKey, key, userId, ct);
        _logger.LogWarning("AcoustID client key saved from the admin UI by user {UserId}.", userId);

        return Ok(new IdentificationKeyStateDto(true, AcoustIdKeySource.Database.ToString()));
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

    private Guid? CurrentUserId()
    {
        var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

