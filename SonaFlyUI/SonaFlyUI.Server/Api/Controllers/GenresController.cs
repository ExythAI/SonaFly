using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/genres")]
[Authorize]
public class GenresController : ControllerBase
{
    private readonly SonaFlyDbContext _db;

    public GenresController(SonaFlyDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Genres the caller can actually browse, counted over the tracks they can actually play.
    /// <para>
    /// This used to return every genre with its unfiltered track count, so a restricted listener
    /// could see that a blocked genre exists and how much of it the server holds — the
    /// restriction leaked the very metadata it was meant to hide — and counts included missing
    /// and unindexed tracks (backlog N21).
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> GetAll(CancellationToken ct)
    {
        var restrictions = _db.RestrictionsFor(CurrentUserId);

        var genres = await _db.Genres.AsNoTracking()
            .Where(g => !restrictions.GenreIds.Contains(g.Id))
            // A genre whose every track is restricted, missing or unindexed is not a genre this
            // user has; filtering before the projection keeps the whole query translatable.
            .Where(g => g.TrackGenres.Any(tg =>
                tg.Track.IsIndexed && !tg.Track.IsMissing &&
                (tg.Track.AlbumId == null || !restrictions.AlbumIds.Contains(tg.Track.AlbumId.Value)) &&
                (tg.Track.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(tg.Track.PrimaryArtistId.Value))))
            .OrderBy(g => g.Name)
            .Select(g => new GenreDto(
                g.Id,
                g.Name,
                g.TrackGenres.Count(tg =>
                    tg.Track.IsIndexed && !tg.Track.IsMissing &&
                    (tg.Track.AlbumId == null || !restrictions.AlbumIds.Contains(tg.Track.AlbumId.Value)) &&
                    (tg.Track.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(tg.Track.PrimaryArtistId.Value)))))
            .ToListAsync(ct);

        return Ok(genres);
    }
}
