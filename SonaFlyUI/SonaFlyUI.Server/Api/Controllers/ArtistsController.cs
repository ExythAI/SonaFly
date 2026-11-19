using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.Common;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities;
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

    /// <summary>
    /// Artists the caller can play something by. <paramref name="filter"/> narrows by
    /// name, ignoring case and accents; <paramref name="sort"/> is "name" (the default)
    /// or "tracks" for the artists with the most music first.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<ArtistDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default,
        [FromQuery] string? filter = null, [FromQuery] string? sort = null)
    {
        if (!Pagination.TryValidate(page, pageSize, out page, out pageSize, out var pagingError))
            return BadRequest(new { message = pagingError });
        if (!Pagination.TryValidateQueryText(filter, "filter", out var filterError))
            return BadRequest(new { message = filterError });

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

        var folded = SearchText.Normalize(filter);
        if (folded.Length > 0) query = query.Where(a => a.SearchName.Contains(folded));

        var total = await query.CountAsync(ct);

        // Artist names are not unique; Id makes the page boundary stable (N20).
        var ordered = string.Equals(sort, "tracks", StringComparison.OrdinalIgnoreCase)
            ? query.OrderByDescending(a => a.PrimaryTracks.Count(t => t.IsIndexed && !t.IsMissing))
                .ThenBy(a => a.SortName ?? a.Name).ThenBy(a => a.Id)
            : query.OrderBy(a => a.SortName ?? a.Name).ThenBy(a => a.Id);

        var items = await Project(ordered
                .Skip(Pagination.Offset(page, pageSize))
                .Take(pageSize), restrictions)
            .ToListAsync(ct);

        return Ok(new PaginatedResult<ArtistDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArtistDto>> GetById(Guid id, CancellationToken ct)
    {
        var restrictions = _db.RestrictionsFor(CurrentUserId);

        var artist = await Project(_db.Artists.AsNoTracking()
                .ApplyRestrictions(_db, CurrentUserId)
                .Where(a => a.Id == id), restrictions)
            .FirstOrDefaultAsync(ct);

        return artist == null ? NotFound() : Ok(artist);
    }

    /// <summary>
    /// An artist as the caller sees them: counts of what they can play, and a picture.
    /// <para>
    /// There are no artist photographs in the library, so an artist without one is
    /// shown by the cover of one of their albums: their earliest, or failing that an
    /// album one of their tracks is on. Never one the caller is restricted from.
    /// </para>
    /// </summary>
    private static IQueryable<ArtistDto> Project(IQueryable<Artist> artists, UserRestrictionSets restrictions) =>
        artists.Select(a => new ArtistDto(
            a.Id, a.Name, a.SortName,
            a.ArtworkId
                ?? a.Albums
                    .Where(alb => alb.ArtworkId != null && !restrictions.AlbumIds.Contains(alb.Id))
                    .OrderBy(alb => alb.Year).ThenBy(alb => alb.Id)
                    .Select(alb => alb.ArtworkId)
                    .FirstOrDefault()
                ?? a.PrimaryTracks
                    .Where(t => t.Album != null && t.Album.ArtworkId != null &&
                                !restrictions.AlbumIds.Contains(t.AlbumId!.Value))
                    .OrderBy(t => t.Album!.Year).ThenBy(t => t.Id)
                    .Select(t => t.Album!.ArtworkId)
                    .FirstOrDefault(),
            a.Albums.Count(alb => !restrictions.AlbumIds.Contains(alb.Id) &&
                alb.Tracks.Any(t => t.IsIndexed && !t.IsMissing &&
                    (t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value)) &&
                    !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))),
            a.PrimaryTracks.Count(t => t.IsIndexed && !t.IsMissing &&
                (t.AlbumId == null || !restrictions.AlbumIds.Contains(t.AlbumId.Value)) &&
                !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))));
}
