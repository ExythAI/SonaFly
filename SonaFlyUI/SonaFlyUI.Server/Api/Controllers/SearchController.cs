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
        if (!Pagination.TryValidateSearchLimit(limit, out limit, out var limitError))
            return BadRequest(new { message = limitError });
        if (!Pagination.TryValidateQueryText(q, "q", out var queryError))
            return BadRequest(new { message = queryError });

        if (string.IsNullOrWhiteSpace(q))
            return Ok(new SearchResultDto([], [], []));

        var term = q.Trim().ToLower();

        // Probes for "the term is a whole word here", by padding both the term and the
        // field with spaces: " vai " is in " steve vai " but not in " vain glory opera ".
        // Matching "roth" inside "Brothers", or "vai" inside "Vain", is technically a match
        // and almost never the one that was wanted.
        var wholeWord = " " + term + " ";

        var restrictions = _db.RestrictionsFor(CurrentUserId);

        // These counts were of every related row — including tracks that are unindexed,
        // missing, or restricted for this user. Count only what they can actually play.
        var artists = await _db.Artists.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(a => a.Name.ToLower().Contains(term))
            .OrderBy(a => a.Name).ThenBy(a => a.Id)
            .Take(limit)
            .Select(a => new ArtistDto(a.Id, a.Name, a.SortName, a.ArtworkId,
                a.Albums.Count(alb => !restrictions.AlbumIds.Contains(alb.Id) &&
                    alb.Tracks.Any(t => t.IsIndexed && !t.IsMissing &&
                        (t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value)) &&
                        !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))),
                a.PrimaryTracks.Count(t => t.IsIndexed && !t.IsMissing &&
                    (t.AlbumId == null || !restrictions.AlbumIds.Contains(t.AlbumId.Value)) &&
                    !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))))
            .ToListAsync(ct);

        var albums = await _db.Albums.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .WhereHasPlayableTracks(_db, CurrentUserId)
            .Where(a => a.Title.ToLower().Contains(term))
            .OrderBy(a => a.Title).ThenBy(a => a.Id)
            .Take(limit)
            .Select(a => new AlbumDto(a.Id, a.Title,
                a.AlbumArtist != null ? a.AlbumArtist.Name : null,
                a.Year, a.ArtworkId,
                a.Tracks.Count(t => t.IsIndexed && !t.IsMissing &&
                    (t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value)) &&
                    !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)))))
            .ToListAsync(ct);

        // Matching the title alone is not what anyone means by "search". Typing an artist
        // is the most common way to look for a track, and it previously returned nothing
        // unless the artist's name happened to appear in the song title.
        //
        // Ordering puts an exact title match first, then titles that begin with the term,
        // and groups everything after that by artist and album. Ordering by title alone
        // meant that searching a common word returned one song repeated across every
        // album that carries it, which reads as duplicates rather than as choices.
        var tracks = await _db.Tracks.AsNoTracking()
            .ApplyRestrictions(_db, CurrentUserId)
            .Where(t => t.IsIndexed && !t.IsMissing &&
                (t.Title.ToLower().Contains(term) ||
                 (t.PrimaryArtist != null && t.PrimaryArtist.Name.ToLower().Contains(term)) ||
                 (t.Album != null && t.Album.Title.ToLower().Contains(term)) ||
                 (t.Genre != null && t.Genre.ToLower().Contains(term))))
            // Relevance before alphabetics. A plain substring match is generous enough to
            // find things, but on its own it buries the answer: "roth" returned five
            // variations of "Brothers" and no David Lee Roth, and "vai" returned "Vain"
            // and no Steve Vai.
            //
            // The tiers below rank a whole-word match above a word-prefix match, and both
            // above a fragment stranded mid-word. That ordering is what separates the
            // artist named Vai from a song called "Vain Glory Opera": the first is the
            // whole word, the second merely starts with the letters. Nothing is filtered
            // out — the incidental hits still appear, lower down.
            .OrderBy(t =>
                // The song someone typed the exact name of.
                t.Title.ToLower() == term ? 0
                // The term is a whole word in the artist, album or title.
                : t.PrimaryArtist != null &&
                  (" " + t.PrimaryArtist.Name.ToLower() + " ").Contains(wholeWord) ? 1
                : t.Album != null &&
                  (" " + t.Album.Title.ToLower() + " ").Contains(wholeWord) ? 2
                : (" " + t.Title.ToLower() + " ").Contains(wholeWord) ? 3
                // The term begins a name, but is not the whole of it.
                : t.Title.ToLower().StartsWith(term) ? 4
                : t.PrimaryArtist != null && t.PrimaryArtist.Name.ToLower().StartsWith(term) ? 5
                : t.Album != null && t.Album.Title.ToLower().StartsWith(term) ? 6
                // Buried mid-word: "roth" in "Brothers".
                : 7)
            .ThenBy(t => t.PrimaryArtist != null ? t.PrimaryArtist.Name : string.Empty)
            .ThenBy(t => t.Album != null ? t.Album.Title : string.Empty)
            .ThenBy(t => t.DiscNumber)
            .ThenBy(t => t.TrackNumber)
            .ThenBy(t => t.Title)
            .ThenBy(t => t.Id)
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
