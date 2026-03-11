using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>
/// Extension methods to apply user restriction filters to IQueryable queries.
/// These filter OUT any content that appears in the user's deny list.
/// </summary>
public static class RestrictionQueryExtensions
{
    /// <summary>
    /// Filters out albums that the user has restricted (by album ID or artist ID).
    /// </summary>
    public static IQueryable<Album> ApplyRestrictions(this IQueryable<Album> query, SonaFlyDbContext db, Guid userId)
    {
        var blockedAlbumIds = db.UserRestrictions
            .Where(r => r.UserId == userId && r.RestrictionType == RestrictionType.Album)
            .Select(r => r.TargetId);

        var blockedArtistIds = db.UserRestrictions
            .Where(r => r.UserId == userId && r.RestrictionType == RestrictionType.Artist)
            .Select(r => r.TargetId);

        return query
            .Where(a => !blockedAlbumIds.Contains(a.Id))
            .Where(a => a.AlbumArtistId == null || !blockedArtistIds.Contains(a.AlbumArtistId.Value));
    }

    /// <summary>
    /// Filters out artists that the user has restricted.
    /// </summary>
    public static IQueryable<Artist> ApplyRestrictions(this IQueryable<Artist> query, SonaFlyDbContext db, Guid userId)
    {
        var blockedArtistIds = db.UserRestrictions
            .Where(r => r.UserId == userId && r.RestrictionType == RestrictionType.Artist)
            .Select(r => r.TargetId);

        return query.Where(a => !blockedArtistIds.Contains(a.Id));
    }

    /// <summary>
    /// Filters out tracks whose album or artist is restricted.
    /// Also filters tracks whose genre is restricted.
    /// </summary>
    public static IQueryable<Track> ApplyRestrictions(this IQueryable<Track> query, SonaFlyDbContext db, Guid userId)
    {
        var blockedAlbumIds = db.UserRestrictions
            .Where(r => r.UserId == userId && r.RestrictionType == RestrictionType.Album)
            .Select(r => r.TargetId);

        var blockedArtistIds = db.UserRestrictions
            .Where(r => r.UserId == userId && r.RestrictionType == RestrictionType.Artist)
            .Select(r => r.TargetId);

        var blockedGenreIds = db.UserRestrictions
            .Where(r => r.UserId == userId && r.RestrictionType == RestrictionType.Genre)
            .Select(r => r.TargetId);

        return query
            .Where(t => t.AlbumId == null || !blockedAlbumIds.Contains(t.AlbumId.Value))
            .Where(t => t.PrimaryArtistId == null || !blockedArtistIds.Contains(t.PrimaryArtistId.Value))
            .Where(t => !t.TrackGenres.Any(tg => blockedGenreIds.Contains(tg.GenreId)));
    }
}
