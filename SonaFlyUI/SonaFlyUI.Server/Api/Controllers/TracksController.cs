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
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? sortBy = "title", [FromQuery] string? sortDir = "asc",
        [FromQuery] string? filter = null, [FromQuery] Guid? artistId = null,
        CancellationToken ct = default)
    {
        var query = _db.Tracks.AsNoTracking()
            .Where(t => t.IsIndexed && !t.IsMissing)
            .ApplyRestrictions(_db, CurrentUserId);

        if (artistId.HasValue)
            query = query.Where(t => t.PrimaryArtistId == artistId.Value);

        // Filter
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.ToLower();
            query = query.Where(t =>
                t.Title.ToLower().Contains(f) ||
                (t.PrimaryArtist != null && t.PrimaryArtist.Name.ToLower().Contains(f)) ||
                (t.Album != null && t.Album.Title.ToLower().Contains(f)) ||
                (t.Genre != null && t.Genre.ToLower().Contains(f)));
        }

        var total = await query.CountAsync(ct);

        // Sort
        var desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLower()) switch
        {
            "artist" => desc ? query.OrderByDescending(t => t.PrimaryArtist != null ? t.PrimaryArtist.Name : "")
                             : query.OrderBy(t => t.PrimaryArtist != null ? t.PrimaryArtist.Name : ""),
            "album" => desc ? query.OrderByDescending(t => t.Album != null ? t.Album.Title : "")
                            : query.OrderBy(t => t.Album != null ? t.Album.Title : ""),
            "duration" => desc ? query.OrderByDescending(t => t.DurationSeconds)
                               : query.OrderBy(t => t.DurationSeconds),
            "genre" => desc ? query.OrderByDescending(t => t.Genre ?? "")
                            : query.OrderBy(t => t.Genre ?? ""),
            "tracknumber" => desc ? query.OrderByDescending(t => t.TrackNumber)
                                  : query.OrderBy(t => t.TrackNumber),
            _ => desc ? query.OrderByDescending(t => t.Title)
                      : query.OrderBy(t => t.Title),
        };

        var items = await query
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
