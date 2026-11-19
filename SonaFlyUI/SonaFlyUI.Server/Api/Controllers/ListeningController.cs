using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Controllers;

public record ReportPlayRequest(Guid TrackId);

/// <summary>A track with when the caller last played it and how often.</summary>
public record PlayedTrackDto(TrackListItemDto Track, DateTime LastPlayedUtc, int PlayCount);

/// <summary>
/// The signed-in user's own listening: what they played, and what they marked as a
/// favourite. Everything here is per user, and every list respects that user's content
/// restrictions, so a track restricted after it was played stops appearing.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class ListeningController(SonaFlyDbContext db) : ControllerBase
{
    private const int MaxLimit = 200;

    /// <summary>A second report for the same track this soon is the same listen.</summary>
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(30);

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IQueryable<Track> PlayableTracks() => db.Tracks.AsNoTracking()
        .Where(t => t.IsIndexed && !t.IsMissing)
        .ApplyRestrictions(db, CurrentUserId);

    /// <summary>Records that the caller started playing a track.</summary>
    [HttpPost("plays")]
    public async Task<IActionResult> ReportPlay([FromBody] ReportPlayRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (await PlayableTracks().AnyAsync(t => t.Id == request.TrackId, ct) == false)
            return NotFound();

        var now = DateTime.UtcNow;
        var since = now - RepeatWindow;
        var repeated = await db.PlayEvents.AnyAsync(
            p => p.UserId == userId && p.TrackId == request.TrackId && p.PlayedUtc >= since, ct);
        if (repeated == false)
        {
            db.PlayEvents.Add(new PlayEvent { UserId = userId, TrackId = request.TrackId, PlayedUtc = now });
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>The tracks the caller played most recently, each once, newest first.</summary>
    [HttpGet("recently-played")]
    public Task<ActionResult<List<PlayedTrackDto>>> RecentlyPlayed(
        [FromQuery] int limit = 20, CancellationToken ct = default) =>
        PlayedAsync(limit, sinceUtc: null, mostPlayedFirst: false, ct);

    /// <summary>
    /// The tracks the caller played most often. <paramref name="days"/> limits it to
    /// recent listening; without it the whole history counts.
    /// </summary>
    [HttpGet("most-played")]
    public Task<ActionResult<List<PlayedTrackDto>>> MostPlayed(
        [FromQuery] int limit = 20, [FromQuery] int? days = null, CancellationToken ct = default) =>
        PlayedAsync(limit, days is > 0 ? DateTime.UtcNow.AddDays(-days.Value) : null, mostPlayedFirst: true, ct);

    private async Task<ActionResult<List<PlayedTrackDto>>> PlayedAsync(
        int limit, DateTime? sinceUtc, bool mostPlayedFirst, CancellationToken ct)
    {
        if (limit < 1 || limit > MaxLimit)
            return BadRequest(new { message = $"limit must be between 1 and {MaxLimit}." });

        var userId = CurrentUserId;
        var plays = db.PlayEvents.AsNoTracking().Where(p => p.UserId == userId);
        if (sinceUtc is { } since) plays = plays.Where(p => p.PlayedUtc >= since);

        var summary = plays
            .GroupBy(p => p.TrackId)
            .Select(g => new { TrackId = g.Key, Last = g.Max(p => p.PlayedUtc), Count = g.Count() });

        var joined = PlayableTracks().Join(summary, t => t.Id, s => s.TrackId, (t, s) => new { Track = t, s.Last, s.Count });
        var ordered = mostPlayedFirst
            ? joined.OrderByDescending(x => x.Count).ThenByDescending(x => x.Last).ThenBy(x => x.Track.Id)
            : joined.OrderByDescending(x => x.Last).ThenBy(x => x.Track.Id);

        var rows = await ordered.Take(limit)
            .Select(x => new
            {
                x.Track.Id, x.Track.Title,
                Artist = x.Track.PrimaryArtist != null ? x.Track.PrimaryArtist.Name : null,
                Album = x.Track.Album != null ? x.Track.Album.Title : null,
                x.Track.AlbumId, x.Track.TrackNumber, x.Track.DiscNumber, x.Track.DurationSeconds,
                ArtworkId = x.Track.Album != null ? x.Track.Album.ArtworkId : null,
                x.Track.Genre, ArtistId = x.Track.PrimaryArtistId, x.Last, x.Count,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new PlayedTrackDto(
            new TrackListItemDto(r.Id, r.Title, r.Artist, r.Album, r.AlbumId, r.TrackNumber, r.DiscNumber,
                r.DurationSeconds, r.ArtworkId, r.Genre, r.ArtistId),
            r.Last, r.Count)).ToList());
    }

    /// <summary>The caller's favourite tracks, most recently added first.</summary>
    [HttpGet("favorites")]
    public async Task<ActionResult<List<TrackListItemDto>>> Favorites(
        [FromQuery] int limit = MaxLimit, CancellationToken ct = default)
    {
        if (limit < 1 || limit > MaxLimit)
            return BadRequest(new { message = $"limit must be between 1 and {MaxLimit}." });

        var userId = CurrentUserId;
        var favorites = db.FavoriteTracks.AsNoTracking().Where(f => f.UserId == userId);

        return Ok(await PlayableTracks()
            .Join(favorites, t => t.Id, f => f.TrackId, (t, f) => new { Track = t, f.CreatedUtc })
            .OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Track.Id)
            .Take(limit)
            .Select(x => new TrackListItemDto(
                x.Track.Id, x.Track.Title,
                x.Track.PrimaryArtist != null ? x.Track.PrimaryArtist.Name : null,
                x.Track.Album != null ? x.Track.Album.Title : null,
                x.Track.AlbumId, x.Track.TrackNumber, x.Track.DiscNumber, x.Track.DurationSeconds,
                x.Track.Album != null ? x.Track.Album.ArtworkId : null,
                x.Track.Genre, x.Track.PrimaryArtistId))
            .ToListAsync(ct));
    }

    /// <summary>
    /// The IDs of every track the caller has marked as a favourite, so a client can show
    /// which rows of any list are favourites without fetching the tracks themselves.
    /// </summary>
    [HttpGet("favorites/ids")]
    public async Task<ActionResult<List<Guid>>> FavoriteIds(CancellationToken ct)
    {
        var userId = CurrentUserId;
        return Ok(await db.FavoriteTracks.AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => f.TrackId)
            .ToListAsync(ct));
    }

    /// <summary>Marks a track as a favourite. Doing it twice changes nothing.</summary>
    [HttpPut("favorites/{trackId:guid}")]
    public async Task<IActionResult> AddFavorite(Guid trackId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (await PlayableTracks().AnyAsync(t => t.Id == trackId, ct) == false)
            return NotFound();

        if (await db.FavoriteTracks.AnyAsync(f => f.UserId == userId && f.TrackId == trackId, ct) == false)
        {
            db.FavoriteTracks.Add(new FavoriteTrack { UserId = userId, TrackId = trackId });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two taps raced and the other one won; the track is a favourite either way.
            }
        }

        return NoContent();
    }

    /// <summary>Removes a track from the caller's favourites.</summary>
    [HttpDelete("favorites/{trackId:guid}")]
    public async Task<IActionResult> RemoveFavorite(Guid trackId, CancellationToken ct)
    {
        var userId = CurrentUserId;
        await db.FavoriteTracks.Where(f => f.UserId == userId && f.TrackId == trackId).ExecuteDeleteAsync(ct);
        return NoContent();
    }
}
