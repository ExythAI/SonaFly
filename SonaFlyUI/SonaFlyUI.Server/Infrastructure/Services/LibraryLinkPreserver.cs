using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>How much a purge set aside to be put back by the next scan.</summary>
public sealed record ParkedLinkCounts(int PlaylistItems, int MixedTapeItems, int Favorites, int Restrictions)
{
    public int Total => PlaylistItems + MixedTapeItems + Favorites + Restrictions;
}

/// <summary>
/// Carries what people made across a library purge.
/// <para>
/// <see cref="ParkAsync"/> runs inside the purge, before the library rows are deleted, and
/// records every playlist entry, mixed tape entry, favourite and restriction under an
/// identity that a rescan reproduces. <see cref="RestoreAsync"/> runs during scans and
/// re-attaches each one as soon as its track, album, artist or genre exists again.
/// </para>
/// </summary>
public sealed class LibraryLinkPreserver(SonaFlyDbContext db, ILogger<LibraryLinkPreserver> logger)
{
    /// <summary>
    /// Parks every reference to library data. Call inside the purge transaction, before
    /// the rows being referred to are deleted. Does not delete the originals.
    /// </summary>
    public async Task<ParkedLinkCounts> ParkAsync(CancellationToken ct)
    {
        var playlistItems = await db.PlaylistItems.AsNoTracking()
            .Select(i => new { i.PlaylistId, i.Track.FilePath, i.SortOrder, i.AddedUtc })
            .ToListAsync(ct);
        var tapeItems = await db.MixedTapeItems.AsNoTracking()
            .Select(i => new { i.MixedTapeId, i.Track.FilePath, i.SortOrder, i.AddedUtc })
            .ToListAsync(ct);
        var favorites = await db.FavoriteTracks.AsNoTracking()
            .Select(f => new { f.UserId, f.Track.FilePath, f.CreatedUtc })
            .ToListAsync(ct);

        db.ParkedLibraryLinks.AddRange(playlistItems.Select(i => new ParkedLibraryLink
        {
            Kind = ParkedLinkKind.PlaylistItem, OwnerId = i.PlaylistId,
            FilePath = i.FilePath, SortOrder = i.SortOrder, AddedUtc = i.AddedUtc,
        }));
        db.ParkedLibraryLinks.AddRange(tapeItems.Select(i => new ParkedLibraryLink
        {
            Kind = ParkedLinkKind.MixedTapeItem, OwnerId = i.MixedTapeId,
            FilePath = i.FilePath, SortOrder = i.SortOrder, AddedUtc = i.AddedUtc,
        }));
        db.ParkedLibraryLinks.AddRange(favorites.Select(f => new ParkedLibraryLink
        {
            Kind = ParkedLinkKind.Favorite, OwnerId = f.UserId,
            FilePath = f.FilePath, AddedUtc = f.CreatedUtc,
        }));

        var restrictions = await ParkRestrictionsAsync(ct);

        await db.SaveChangesAsync(ct);
        return new ParkedLinkCounts(playlistItems.Count, tapeItems.Count, favorites.Count, restrictions);
    }

    private async Task<int> ParkRestrictionsAsync(CancellationToken ct)
    {
        var restrictions = await db.UserRestrictions.AsNoTracking().ToListAsync(ct);
        if (restrictions.Count == 0) return 0;

        var albums = await db.Albums.AsNoTracking()
            .Select(a => new { a.Id, a.Title, Artist = a.AlbumArtist != null ? a.AlbumArtist.Name : null })
            .ToDictionaryAsync(a => a.Id, ct);
        var artists = await db.Artists.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var genres = await db.Genres.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        foreach (var restriction in restrictions)
        {
            // The live name is the one a rescan will reproduce. The cached display name is
            // a fallback for a restriction whose target had already gone.
            string? name = restriction.TargetName;
            string? artist = null;
            switch (restriction.RestrictionType)
            {
                case RestrictionType.Album when albums.TryGetValue(restriction.TargetId, out var album):
                    name = album.Title;
                    artist = album.Artist;
                    break;
                case RestrictionType.Artist when artists.TryGetValue(restriction.TargetId, out var artistName):
                    name = artistName;
                    break;
                case RestrictionType.Genre when genres.TryGetValue(restriction.TargetId, out var genreName):
                    name = genreName;
                    break;
            }

            if (string.IsNullOrWhiteSpace(name)) continue;

            db.ParkedLibraryLinks.Add(new ParkedLibraryLink
            {
                Kind = ParkedLinkKind.Restriction, OwnerId = restriction.UserId,
                RestrictionType = restriction.RestrictionType,
                TargetName = name, TargetArtistName = artist,
                AddedUtc = restriction.CreatedUtc,
            });
        }

        return restrictions.Count;
    }

    /// <summary>
    /// Re-attaches every parked reference whose target exists again, and saves. Cheap when
    /// nothing is parked, so the scan can call it after every batch.
    /// </summary>
    /// <returns>How many references were put back.</returns>
    public async Task<int> RestoreAsync(CancellationToken ct)
    {
        if (await db.ParkedLibraryLinks.AnyAsync(ct) == false) return 0;

        var restored = await RestoreTrackLinksAsync(ct) + await RestoreRestrictionsAsync(ct);
        if (restored > 0 || db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }

        if (restored > 0)
        {
            logger.LogInformation("Restored {Count} playlist, mixed tape, favourite or restriction link(s) after a purge.", restored);
        }

        return restored;
    }

    private async Task<int> RestoreTrackLinksAsync(CancellationToken ct)
    {
        var matches = await (
            from link in db.ParkedLibraryLinks
            where link.Kind != ParkedLinkKind.Restriction && link.FilePath != null
            join track in db.Tracks on link.FilePath equals track.FilePath
            select new { Link = link, TrackId = track.Id }).ToListAsync(ct);
        if (matches.Count == 0) return 0;

        var playlists = (await db.Playlists.Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var tapes = (await db.MixedTapes.Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var users = (await db.Users.Select(u => u.Id).ToListAsync(ct)).ToHashSet();
        var favorites = (await db.FavoriteTracks.Select(f => new { f.UserId, f.TrackId }).ToListAsync(ct))
            .Select(f => (f.UserId, f.TrackId)).ToHashSet();

        var restored = 0;
        // Two track rows can share a path; the link goes to one of them, once.
        foreach (var match in matches.DistinctBy(m => m.Link.Id))
        {
            var link = match.Link;
            switch (link.Kind)
            {
                case ParkedLinkKind.PlaylistItem when playlists.Contains(link.OwnerId):
                    db.PlaylistItems.Add(new PlaylistItem
                    {
                        PlaylistId = link.OwnerId, TrackId = match.TrackId,
                        SortOrder = link.SortOrder, AddedUtc = link.AddedUtc,
                    });
                    restored++;
                    break;
                case ParkedLinkKind.MixedTapeItem when tapes.Contains(link.OwnerId):
                    db.MixedTapeItems.Add(new MixedTapeItem
                    {
                        MixedTapeId = link.OwnerId, TrackId = match.TrackId,
                        SortOrder = link.SortOrder, AddedUtc = link.AddedUtc,
                    });
                    restored++;
                    break;
                case ParkedLinkKind.Favorite when users.Contains(link.OwnerId):
                    if (favorites.Add((link.OwnerId, match.TrackId)))
                    {
                        db.FavoriteTracks.Add(new FavoriteTrack
                        {
                            UserId = link.OwnerId, TrackId = match.TrackId, CreatedUtc = link.AddedUtc,
                        });
                    }
                    restored++;
                    break;
            }

            // Restored, or its playlist, tape or user was deleted meanwhile: done either way.
            db.ParkedLibraryLinks.Remove(link);
        }

        return restored;
    }

    private IQueryable<Album> AlbumsNamed(string title, string? artistName) => artistName == null
        ? db.Albums.Where(a => a.Title == title && a.AlbumArtist == null)
        : db.Albums.Where(a => a.Title == title && a.AlbumArtist != null && a.AlbumArtist.Name == artistName);

    private async Task<int> RestoreRestrictionsAsync(CancellationToken ct)
    {
        var parked = await db.ParkedLibraryLinks
            .Where(l => l.Kind == ParkedLinkKind.Restriction)
            .ToListAsync(ct);
        if (parked.Count == 0) return 0;

        var restored = 0;
        foreach (var link in parked)
        {
            if (link.RestrictionType is not { } type || link.TargetName is not { } name) continue;

            Guid? targetId = type switch
            {
                RestrictionType.Genre => await db.Genres.Where(g => g.Name == name)
                    .Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct),
                RestrictionType.Artist => await db.Artists.Where(a => a.Name == name)
                    .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct),
                RestrictionType.Album => await AlbumsNamed(name, link.TargetArtistName)
                    .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct),
                _ => null,
            };
            if (targetId is not { } id) continue;

            if (await db.Users.AnyAsync(u => u.Id == link.OwnerId, ct) &&
                await db.UserRestrictions.AnyAsync(
                    r => r.UserId == link.OwnerId && r.RestrictionType == type && r.TargetId == id, ct) == false)
            {
                db.UserRestrictions.Add(new UserRestriction
                {
                    UserId = link.OwnerId, RestrictionType = type, TargetId = id, TargetName = name,
                });
                restored++;
            }

            db.ParkedLibraryLinks.Remove(link);
        }

        return restored;
    }
}
