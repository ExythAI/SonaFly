using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/auditoriums")]
[Authorize]
public class AuditoriumsController : ControllerBase
{
    private readonly SonaFlyDbContext _db;
    private readonly AuditoriumStateService _state;

    public AuditoriumsController(SonaFlyDbContext db, AuditoriumStateService state)
    {
        _db = db;
        _state = state;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>List all active auditoriums with live user counts.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AuditoriumListDto>>> GetAll(CancellationToken ct)
    {
        var auditoriums = await _db.Auditoriums.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new AuditoriumListDto(a.Id, a.Name, a.CreatedByUserId, 0, null))
            .ToListAsync(ct);

        // Enrich with live state
        for (int i = 0; i < auditoriums.Count; i++)
        {
            var room = _state.GetRoom(auditoriums[i].Id);
            if (room != null)
            {
                auditoriums[i] = auditoriums[i] with
                {
                    ActiveUserCount = room.ActiveUsers.Count,
                    NowPlaying = room.CurrentTrackTitle
                };
            }
        }

        return Ok(auditoriums);
    }

    /// <summary>Get current state of an auditorium (REST fallback for non-SignalR).</summary>
    [HttpGet("{id:guid}/state")]
    public async Task<ActionResult<AuditoriumStateSnapshot>> GetState(Guid id, CancellationToken ct)
    {
        var exists = await _db.Auditoriums.AnyAsync(a => a.Id == id && a.IsActive, ct);
        if (!exists) return NotFound();

        var room = _state.GetRoom(id);
        return room == null ? Ok(new AuditoriumStateSnapshot(id, null, null, null, null, null, 0, false, null, null, [], [], DateTime.UtcNow)) : Ok(room.ToSnapshot());
    }

    /// <summary>Create a new auditorium. Admin only.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Create([FromBody] CreateAuditoriumRequest request, CancellationToken ct)
    {
        var auditorium = new Auditorium
        {
            Name = request.Name.Trim(),
            CreatedByUserId = CurrentUserId,
            IsActive = true
        };
        _db.Auditoriums.Add(auditorium);
        await _db.SaveChangesAsync(ct);
        return Ok(new { auditorium.Id, auditorium.Name });
    }

    /// <summary>Delete (deactivate) an auditorium. Admin only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var auditorium = await _db.Auditoriums.FindAsync([id], ct);
        if (auditorium == null) return NotFound();
        auditorium.IsActive = false;
        await _db.SaveChangesAsync(ct);
        _state.RemoveRoom(id);
        return NoContent();
    }
}

public record AuditoriumListDto(Guid Id, string Name, Guid CreatedByUserId, int ActiveUserCount, string? NowPlaying);
public record CreateAuditoriumRequest(string Name);
