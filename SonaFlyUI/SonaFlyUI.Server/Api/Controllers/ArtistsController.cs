using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/artists")]
[Authorize]
public class ArtistsController : ControllerBase
{
    private readonly SonaFlyDbContext _db;

    public ArtistsController(SonaFlyDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<ArtistDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = _db.Artists.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(a =>
                a.PrimaryTracks.Any(t => t.IsIndexed && !t.IsMissing) ||
                a.Albums.Any(alb => alb.Tracks.Any(t => t.IsIndexed && !t.IsMissing)));
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(a => a.SortName ?? a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ArtistDto(
                a.Id, a.Name, a.SortName, a.ArtworkId,
                a.Albums.Count(alb => alb.Tracks.Any(t => t.IsIndexed && !t.IsMissing)),
                a.PrimaryTracks.Count(t => t.IsIndexed && !t.IsMissing)
            ))
            .ToListAsync(ct);

        return Ok(new PaginatedResult<ArtistDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArtistDto>> GetById(Guid id, CancellationToken ct)
    {
        var artist = await _db.Artists.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(a => a.Id == id)
            .Select(a => new ArtistDto(a.Id, a.Name, a.SortName, a.ArtworkId,
                a.Albums.Count(alb => alb.Tracks.Any(t => t.IsIndexed && !t.IsMissing)),
                a.PrimaryTracks.Count(t => t.IsIndexed && !t.IsMissing)))
            .FirstOrDefaultAsync(ct);

        return artist == null ? NotFound() : Ok(artist);
    }
}
