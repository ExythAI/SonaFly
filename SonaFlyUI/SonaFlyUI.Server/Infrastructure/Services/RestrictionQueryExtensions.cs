using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>
/// The three deny lists that apply to one user, as composable subqueries.
///
/// Exposed rather than hidden behind <c>ApplyRestrictions</c> because restrictions also
/// have to be expressed inside projections — an album's track count, an artist's album
/// count — where a query-level filter cannot reach. Keeping the sets in one place stops
/// those call sites drifting apart from the filters.
/// </summary>
public sealed class UserRestrictionSets
{
    internal UserRestrictionSets(SonaFlyDbContext db, Guid userId)
    {
        AlbumIds = Targets(db, userId, RestrictionType.Album);
        ArtistIds = Targets(db, userId, RestrictionType.Artist);
        GenreIds = Targets(db, userId, RestrictionType.Genre);
    }

    public IQueryable<Guid> AlbumIds { get; }
    public IQueryable<Guid> ArtistIds { get; }
    public IQueryable<Guid> GenreIds { get; }

    private static IQueryable<Guid> Targets(SonaFlyDbContext db, Guid userId, RestrictionType type) =>
        db.UserRestrictions
            .Where(r => r.UserId == userId && r.RestrictionType == type)
            .Select(r => r.TargetId);
}

/// <summary>
/// Extension methods to apply user restriction filters to IQueryable queries.
/// These filter OUT any content that appears in the user's deny list.
/// </summary>
public static class RestrictionQueryExtensions
{
    /// <summary>
    /// The deny lists for one user, for use inside a projection.
    /// </summary>
    public static UserRestrictionSets RestrictionsFor(this SonaFlyDbContext db, Guid userId) => new(db, userId);

    /// <summary>
    /// Filters out albums that the user has restricted (by album ID or artist ID).
    /// </summary>
    /// <remarks>
    /// This says nothing about the album's <em>tracks</em>: an album can survive this
    /// filter while every track in it is restricted by genre or track artist. Use
    /// <see cref="WhereHasPlayableTracks"/> when the album is only worth showing if
    /// something in it can actually be played.
    /// </remarks>
    public static IQueryable<Album> ApplyRestrictions(this IQueryable<Album> query, SonaFlyDbContext db, Guid userId)
    {
        var restrictions = db.RestrictionsFor(userId);

        return query
            .Where(a => !restrictions.AlbumIds.Contains(a.Id))
            .Where(a => a.AlbumArtistId == null || !restrictions.ArtistIds.Contains(a.AlbumArtistId.Value));
    }

    /// <summary>
    /// Keeps only albums holding at least one track this user can actually stream.
    /// Apply after <see cref="ApplyRestrictions(IQueryable{Album}, SonaFlyDbContext, Guid)"/>.
    /// </summary>
    public static IQueryable<Album> WhereHasPlayableTracks(this IQueryable<Album> query, SonaFlyDbContext db, Guid userId)
    {
        var restrictions = db.RestrictionsFor(userId);

        return query.Where(a => a.Tracks.Any(t =>
            t.IsIndexed && !t.IsMissing &&
            (t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value)) &&
            !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId))));
    }

    /// <summary>
    /// Filters out artists that the user has restricted.
    /// </summary>
    public static IQueryable<Artist> ApplyRestrictions(this IQueryable<Artist> query, SonaFlyDbContext db, Guid userId)
    {
        var restrictions = db.RestrictionsFor(userId);

        return query.Where(a => !restrictions.ArtistIds.Contains(a.Id));
    }

    /// <summary>
    /// Filters out tracks whose album or artist is restricted.
    /// Also filters tracks whose genre is restricted.
    /// </summary>
    public static IQueryable<Track> ApplyRestrictions(this IQueryable<Track> query, SonaFlyDbContext db, Guid userId)
    {
        var restrictions = db.RestrictionsFor(userId);

        return query
            .Where(t => t.AlbumId == null || !restrictions.AlbumIds.Contains(t.AlbumId.Value))
            .Where(t => t.PrimaryArtistId == null || !restrictions.ArtistIds.Contains(t.PrimaryArtistId.Value))
            .Where(t => !t.TrackGenres.Any(tg => restrictions.GenreIds.Contains(tg.GenreId)));
    }
}
