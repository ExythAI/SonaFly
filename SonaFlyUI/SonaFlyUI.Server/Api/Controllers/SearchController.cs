using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly SonaFlyDbContext _db;

    public SearchController(SonaFlyDbContext db) => _db = db;

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string q, [FromQuery] int limit = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(new SearchResultDto([], [], []));

        var term = q.Trim().ToLower();

        var artists = await _db.Artists.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(a => a.Name.ToLower().Contains(term))
            .OrderBy(a => a.Name)
            .Take(limit)
            .Select(a => new ArtistDto(a.Id, a.Name, a.SortName, a.ArtworkId, a.Albums.Count, a.PrimaryTracks.Count))
            .ToListAsync(ct);

        var albums = await _db.Albums.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(a => a.Title.ToLower().Contains(term))
            .OrderBy(a => a.Title)
            .Take(limit)
            .Select(a => new AlbumDto(a.Id, a.Title,
                a.AlbumArtist != null ? a.AlbumArtist.Name : null,
                a.Year, a.ArtworkId, a.Tracks.Count))
            .ToListAsync(ct);

        var tracks = await _db.Tracks.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(t => t.IsIndexed && !t.IsMissing &&
                (t.Title.ToLower().Contains(term) || (t.Genre != null && t.Genre.ToLower().Contains(term))))
            .OrderBy(t => t.Title)
            .Take(limit)
            .Select(t => new TrackListItemDto(
                t.Id, t.Title,
                t.PrimaryArtist != null ? t.PrimaryArtist.Name : null,
                t.Album != null ? t.Album.Title : null,
                t.AlbumId, t.TrackNumber, t.DiscNumber, t.DurationSeconds,
                t.Album != null ? t.Album.ArtworkId : null,
                t.Genre))
            .ToListAsync(ct);

        return Ok(new SearchResultDto(artists, albums, tracks));
    }
}
