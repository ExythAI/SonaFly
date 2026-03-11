using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/albums")]
[Authorize]
public class AlbumsController : ControllerBase
{
    private readonly SonaFlyDbContext _db;

    public AlbumsController(SonaFlyDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<AlbumDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] Guid? artistId = null, CancellationToken ct = default)
    {
        var query = _db.Albums.AsNoTracking().ApplyRestrictions(_db, CurrentUserId);
        if (artistId.HasValue)
            query = query.Where(a => a.AlbumArtistId == artistId.Value);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(a => a.SortTitle ?? a.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AlbumDto(
                a.Id, a.Title,
                a.AlbumArtist != null ? a.AlbumArtist.Name : null,
                a.Year, a.ArtworkId, a.Tracks.Count
            ))
            .ToListAsync(ct);

        return Ok(new PaginatedResult<AlbumDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlbumDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        // Check if this album is restricted for the user
        var isRestricted = await _db.Albums.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .AnyAsync(a => a.Id == id, ct);

        if (!isRestricted) return NotFound();

        var album = await _db.Albums.AsNoTracking()
            .Include(a => a.AlbumArtist)
            .Include(a => a.Tracks.OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber))
                .ThenInclude(t => t.PrimaryArtist)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (album == null) return NotFound();

        var dto = new AlbumDetailDto(
            album.Id, album.Title,
            album.AlbumArtist?.Name, album.AlbumArtistId,
            album.Year, album.DiscCount, album.GenreSummary, album.ArtworkId,
            album.Tracks.Select(t => new TrackListItemDto(
                t.Id, t.Title, t.PrimaryArtist?.Name, album.Title, album.Id,
                t.TrackNumber, t.DiscNumber, t.DurationSeconds, album.ArtworkId, t.Genre
            )).ToList()
        );

        return Ok(dto);
    }
}
