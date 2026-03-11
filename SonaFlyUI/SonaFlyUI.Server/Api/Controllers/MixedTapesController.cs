using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/mixed-tapes")]
[Authorize]
public class MixedTapesController : ControllerBase
{
    private readonly IMixedTapeService _mixedTapeService;

    public MixedTapesController(IMixedTapeService mixedTapeService) => _mixedTapeService = mixedTapeService;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MixedTapeDto>>> GetAll(CancellationToken ct)
    {
        var tapes = await _mixedTapeService.GetAllAsync(CurrentUserId, ct);
        return Ok(tapes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MixedTapeDto>> GetById(Guid id, CancellationToken ct)
    {
        var tape = await _mixedTapeService.GetByIdAsync(id, ct);
        return tape == null ? NotFound() : Ok(tape);
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreateMixedTapeRequest request, CancellationToken ct)
    {
        var id = await _mixedTapeService.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mixedTapeService.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddTrack(Guid id, [FromBody] AddTrackToMixedTapeRequest request, CancellationToken ct)
    {
        await _mixedTapeService.AddTrackAsync(id, request.TrackId, ct);
        return Ok();
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await _mixedTapeService.RemoveItemAsync(id, itemId, ct);
        return NoContent();
    }
}
