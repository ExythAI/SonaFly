using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/playlists")]
[Authorize]
public class PlaylistsController : ControllerBase
{
    private readonly IPlaylistService _playlistService;

    public PlaylistsController(IPlaylistService playlistService) => _playlistService = playlistService;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlaylistDto>>> GetAll(CancellationToken ct)
    {
        var playlists = await _playlistService.GetAllAsync(CurrentUserId, ct);
        return Ok(playlists);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlaylistDto>> GetById(Guid id, CancellationToken ct)
    {
        var playlist = await _playlistService.GetByIdAsync(id, ct);
        return playlist == null ? NotFound() : Ok(playlist);
    }

    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CreatePlaylistRequest request, CancellationToken ct)
    {
        var id = await _playlistService.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlaylistRequest request, CancellationToken ct)
    {
        await _playlistService.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _playlistService.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddTrack(Guid id, [FromBody] AddTrackToPlaylistRequest request, CancellationToken ct)
    {
        await _playlistService.AddTrackAsync(id, request.TrackId, ct);
        return Ok();
    }

    [HttpPut("{id:guid}/items/reorder")]
    public async Task<IActionResult> Reorder(Guid id, [FromBody] ReorderPlaylistItemsRequest request, CancellationToken ct)
    {
        await _playlistService.ReorderAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await _playlistService.RemoveItemAsync(id, itemId, ct);
        return NoContent();
    }
}
