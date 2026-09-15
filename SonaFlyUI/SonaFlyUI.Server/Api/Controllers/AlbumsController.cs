using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.Common;
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
        if (!Pagination.TryValidate(page, pageSize, out page, out pageSize, out var pagingError))
            return BadRequest(new { message = pagingError });

        var restrictions = _db.RestrictionsFor(CurrentUserId);

        // An album is only listed when this user can play something on it, and the count
        // shown is of those tracks — not of every indexed track, which would advertise
        // content the stream endpoint then refuses.
        var query = _db.Albums.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .WhereHasPlayableTracks(_db, CurrentUserId);
        if (artistId.HasValue)
            query = query.Where(a => a.AlbumArtistId == artistId.Value);
        var total = await query.CountAsync(ct);
        var items = await query
            // Year and title are both non-unique; Id makes the page boundary stable (N20).
            .OrderBy(a => a.Year ?? int.MaxValue).ThenBy(a => a.SortTitle ?? a.Title).ThenBy(a => a.Id)
            .Skip(Pagination.Offset(page, pageSize))
            .Take(pageSize)
            .Select(a => new AlbumDto(
                a.Id, a.Title,
                a.AlbumArtist != null ? a.AlbumArtist.Name : null,
                a.Year, a.ArtworkId,
                a.Tracks.Count(t => t.IsIndexed && !t.IsMissing &&
                    (t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value)) &&
                    !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))
            ))
            .ToListAsync(ct);

        return Ok(new PaginatedResult<AlbumDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AlbumDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var album = await _db.Albums.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Include(a => a.AlbumArtist)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (album == null) return NotFound();

        // Built from a separately restricted Track query rather than a filtered Include,
        // so track-level artist and genre restrictions apply here exactly as they do at
        // the stream endpoint. Previously every indexed track was listed, which exposed
        // titles the user could not play and left dead rows in the UI.
        var tracks = await _db.Tracks.AsNoTracking()
            .Where(t => t.AlbumId == id && t.IsIndexed && !t.IsMissing)
            .ApplyRestrictions(_db, CurrentUserId)
            .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber)
            .Select(t => new TrackListItemDto(
                t.Id, t.Title,
                t.PrimaryArtist != null ? t.PrimaryArtist.Name : null,
                album.Title, album.Id,
                t.TrackNumber, t.DiscNumber, t.DurationSeconds, album.ArtworkId, t.Genre
            ))
            .ToListAsync(ct);

        // Nothing left to play: treat the album as absent rather than showing an empty
        // shell that reveals it exists.
        if (tracks.Count == 0) return NotFound();

        var dto = new AlbumDetailDto(
            album.Id, album.Title,
            album.AlbumArtist?.Name, album.AlbumArtistId,
            album.Year, album.DiscCount, album.GenreSummary, album.ArtworkId,
            tracks
        );

        return Ok(dto);
    }
}
