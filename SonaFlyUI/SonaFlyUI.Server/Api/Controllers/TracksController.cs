using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/tracks")]
[Authorize]
public class TracksController : ControllerBase
{
    private readonly SonaFlyDbContext _db;

    public TracksController(SonaFlyDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<TrackListItemDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = _db.Tracks.AsNoTracking()
            .Where(t => t.IsIndexed && !t.IsMissing)
            .ApplyRestrictions(_db, CurrentUserId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(t => t.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TrackListItemDto(
                t.Id, t.Title,
                t.PrimaryArtist != null ? t.PrimaryArtist.Name : null,
                t.Album != null ? t.Album.Title : null,
                t.AlbumId, t.TrackNumber, t.DiscNumber, t.DurationSeconds,
                t.Album != null ? t.Album.ArtworkId : null,
                t.Genre
            ))
            .ToListAsync(ct);

        return Ok(new PaginatedResult<TrackListItemDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TrackListItemDto>> GetById(Guid id, CancellationToken ct)
    {
        var track = await _db.Tracks.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(t => t.Id == id)
            .Select(t => new TrackListItemDto(
                t.Id, t.Title,
                t.PrimaryArtist != null ? t.PrimaryArtist.Name : null,
                t.Album != null ? t.Album.Title : null,
                t.AlbumId, t.TrackNumber, t.DiscNumber, t.DurationSeconds,
                t.Album != null ? t.Album.ArtworkId : null,
                t.Genre
            ))
            .FirstOrDefaultAsync(ct);

        return track == null ? NotFound() : Ok(track);
    }
}
