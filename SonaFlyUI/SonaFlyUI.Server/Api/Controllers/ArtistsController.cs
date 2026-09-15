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
        if (!Pagination.TryValidate(page, pageSize, out page, out pageSize, out var pagingError))
            return BadRequest(new { message = pagingError });

        var restrictions = _db.RestrictionsFor(CurrentUserId);

        // Counts and visibility use only content this user can play, so the artist page
        // does not promise albums or tracks that browsing them will not show.
        var query = _db.Artists.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(a =>
                a.PrimaryTracks.Any(t => t.IsIndexed && !t.IsMissing &&
                    !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId))) ||
                a.Albums.Any(alb => !restrictions.AlbumIds.Contains(alb.Id) &&
                    alb.Tracks.Any(t => t.IsIndexed && !t.IsMissing &&
                        (t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value)) &&
                        !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))));
        var total = await query.CountAsync(ct);
        var items = await query
            // Artist names are not unique; Id makes the page boundary stable (N20).
            .OrderBy(a => a.SortName ?? a.Name).ThenBy(a => a.Id)
            .Skip(Pagination.Offset(page, pageSize))
            .Take(pageSize)
            .Select(a => new ArtistDto(
                a.Id, a.Name, a.SortName, a.ArtworkId,
                a.Albums.Count(alb => !restrictions.AlbumIds.Contains(alb.Id) &&
                    alb.Tracks.Any(t => t.IsIndexed && !t.IsMissing &&
                        (t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value)) &&
                        !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))),
                a.PrimaryTracks.Count(t => t.IsIndexed && !t.IsMissing &&
                    (t.AlbumId == null || !restrictions.AlbumIds.Contains(t.AlbumId.Value)) &&
                    !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))
            ))
            .ToListAsync(ct);

        return Ok(new PaginatedResult<ArtistDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArtistDto>> GetById(Guid id, CancellationToken ct)
    {
        var restrictions = _db.RestrictionsFor(CurrentUserId);

        var artist = await _db.Artists.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(a => a.Id == id)
            .Select(a => new ArtistDto(a.Id, a.Name, a.SortName, a.ArtworkId,
                a.Albums.Count(alb => !restrictions.AlbumIds.Contains(alb.Id) &&
                    alb.Tracks.Any(t => t.IsIndexed && !t.IsMissing &&
                        (t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value)) &&
                        !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))),
                a.PrimaryTracks.Count(t => t.IsIndexed && !t.IsMissing &&
                    (t.AlbumId == null || !restrictions.AlbumIds.Contains(t.AlbumId.Value)) &&
                    !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))))
            .FirstOrDefaultAsync(ct);

        return artist == null ? NotFound() : Ok(artist);
    }
}
