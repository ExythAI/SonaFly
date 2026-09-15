using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using SonaFlyUI.Server.Api.Hubs;
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
    private readonly TrackEndSchedulerService _scheduler;
    private readonly IHubContext<AuditoriumHub> _hub;

    public AuditoriumsController(SonaFlyDbContext db, AuditoriumStateService state, TrackEndSchedulerService scheduler, IHubContext<AuditoriumHub> hub)
    {
        _db = db;
        _state = state;
        _scheduler = scheduler;
        _hub = hub;
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
                using var lease = await room.EnterAsync(ct);
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

        var room = _state.GetOrCreateRoom(id);
        using var lease = await room.EnterAsync(ct);
        if (room.IsDeleted) return NotFound();
        await _scheduler.LoadQueueAsync(room, _db);
        return Ok(room.ToSnapshot());
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
        var room = _state.GetOrCreateRoom(id);
        using var lease = await room.EnterAsync(ct);
        auditorium.IsActive = false;
        await _db.SaveChangesAsync(ct);
        room.IsDeleted = true;
        room.StopPlayback();
        await _hub.Clients.Group($"aud-{id}").SendAsync("OnRoomClosed", cancellationToken: ct);
        // Retain the tombstone so a join already in progress cannot recreate this room.
        return NoContent();
    }
}

public record AuditoriumListDto(Guid Id, string Name, Guid CreatedByUserId, int ActiveUserCount, string? NowPlaying);
public record CreateAuditoriumRequest(string Name);
