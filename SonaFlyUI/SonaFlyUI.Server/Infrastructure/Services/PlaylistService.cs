using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

public class PlaylistService : IPlaylistService
{
    private readonly SonaFlyDbContext _db;

    public PlaylistService(SonaFlyDbContext db)
    {
        _db = db;
    }

    // ── Access ──
    //
    // Reading is deliberately wider than writing: a playlist being public means anybody may
    // look at it, never that anybody may change it.

    private static Expression<Func<Playlist, bool>> ReadableBy(CollectionCaller caller) =>
        caller.IsAdmin
            ? _ => true
            : p => p.OwnerUserId == caller.UserId || p.IsPublic || p.IsSystemPlaylist;

    private static bool CanRead(Playlist p, CollectionCaller caller) =>
        caller.IsAdmin || p.OwnerUserId == caller.UserId || p.IsPublic || p.IsSystemPlaylist;

    private static bool CanWrite(Playlist p, CollectionCaller caller) =>
        caller.IsAdmin || p.OwnerUserId == caller.UserId;

    /// <summary>
    /// Loads a playlist the caller may modify. A playlist the caller cannot even see is
    /// reported as missing rather than forbidden, so private IDs stay unconfirmable.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such playlist, or none this caller can see.</exception>
    /// <exception cref="UnauthorizedAccessException">Visible to the caller, but not theirs to change.</exception>
    private async Task<Playlist> LoadForWriteAsync(Guid playlistId, CollectionCaller caller, CancellationToken ct)
    {
        var playlist = await _db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId, ct);

        if (playlist == null || !CanRead(playlist, caller))
        {
            throw new KeyNotFoundException($"Playlist {playlistId} not found.");
        }

        if (!CanWrite(playlist, caller))
        {
            throw new UnauthorizedAccessException($"Playlist {playlistId} belongs to another user.");
        }

        return playlist;
    }

    // ── Commands ──

    public async Task<Guid> CreateAsync(CreatePlaylistRequest request, Guid ownerUserId, CancellationToken ct)
    {
        var playlist = new Playlist
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            OwnerUserId = ownerUserId,
            IsPublic = request.IsPublic
        };
        _db.Playlists.Add(playlist);
        await _db.SaveChangesAsync(ct);
        return playlist.Id;
    }

    public async Task UpdateAsync(Guid playlistId, UpdatePlaylistRequest request, CollectionCaller caller, CancellationToken ct)
    {
        var playlist = await LoadForWriteAsync(playlistId, caller, ct);

        if (request.Name != null) playlist.Name = request.Name.Trim();
        if (request.Description != null) playlist.Description = request.Description.Trim();
        if (request.IsPublic.HasValue) playlist.IsPublic = request.IsPublic.Value;
        playlist.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid playlistId, CollectionCaller caller, CancellationToken ct)
    {
        var playlist = await LoadForWriteAsync(playlistId, caller, ct);

        _db.Playlists.Remove(playlist);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddTrackAsync(Guid playlistId, Guid trackId, CollectionCaller caller, CancellationToken ct)
    {
        await LoadForWriteAsync(playlistId, caller, ct);

        // Existence is not enough: a track the caller may not listen to must not become an
        // entry they can then stream out of the playlist.
        var trackIsVisible = await _db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .ApplyRestrictions(_db, caller.UserId)
            .AnyAsync(ct);
        if (!trackIsVisible)
        {
            throw new KeyNotFoundException($"Track {trackId} not found.");
        }

        var maxOrder = await _db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync(ct) ?? 0;

        _db.PlaylistItems.Add(new PlaylistItem
        {
            PlaylistId = playlistId,
            TrackId = trackId,
            SortOrder = maxOrder + 1,
            AddedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveItemAsync(Guid playlistId, Guid itemId, CollectionCaller caller, CancellationToken ct)
    {
        await LoadForWriteAsync(playlistId, caller, ct);

        var item = await _db.PlaylistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.PlaylistId == playlistId, ct)
            ?? throw new KeyNotFoundException($"Playlist item {itemId} not found.");

        _db.PlaylistItems.Remove(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(Guid playlistId, ReorderPlaylistItemsRequest request, CollectionCaller caller, CancellationToken ct)
    {
        await LoadForWriteAsync(playlistId, caller, ct);

        var items = await _db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .ToListAsync(ct);

        for (int i = 0; i < request.ItemIdsInOrder.Count; i++)
        {
            var item = items.FirstOrDefault(x => x.Id == request.ItemIdsInOrder[i]);
            if (item != null)
                item.SortOrder = i + 1;
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── Queries ──

    public async Task<PlaylistDto?> GetByIdAsync(Guid playlistId, CollectionCaller caller, CancellationToken ct)
    {
        // Captured into locals so EF composes the three deny-list subqueries into the
        // projection; the item predicate is repeated because it has to be inline for EF to
        // translate it, once for the count and once for the list.
        var restrictions = _db.RestrictionsFor(caller.UserId);
        var albumIds = restrictions.AlbumIds;
        var artistIds = restrictions.ArtistIds;
        var genreIds = restrictions.GenreIds;

        return await _db.Playlists
            .AsNoTracking()
            .Where(p => p.Id == playlistId)
            .Where(ReadableBy(caller))
            .Select(p => new PlaylistDto(
                p.Id, p.Name, p.Description, p.OwnerUserId,
                p.Owner != null ? p.Owner.DisplayName : null,
                p.IsPublic, p.IsSystemPlaylist,
                // Counts what the caller can see, so the number agrees with the list below it.
                p.Items.Count(i =>
                    (i.Track.AlbumId == null || !albumIds.Contains(i.Track.AlbumId.Value)) &&
                    (i.Track.PrimaryArtistId == null || !artistIds.Contains(i.Track.PrimaryArtistId.Value)) &&
                    !i.Track.TrackGenres.Any(tg => genreIds.Contains(tg.GenreId))),
                p.Items
                    .Where(i =>
                        (i.Track.AlbumId == null || !albumIds.Contains(i.Track.AlbumId.Value)) &&
                        (i.Track.PrimaryArtistId == null || !artistIds.Contains(i.Track.PrimaryArtistId.Value)) &&
                        !i.Track.TrackGenres.Any(tg => genreIds.Contains(tg.GenreId)))
                    .OrderBy(i => i.SortOrder)
                    .Select(i => new PlaylistItemDto(
                        i.Id, i.TrackId,
                        i.Track.Title,
                        i.Track.PrimaryArtist != null ? i.Track.PrimaryArtist.Name : null,
                        i.Track.Album != null ? i.Track.Album.Title : null,
                        i.Track.DurationSeconds,
                        i.SortOrder))
                    .ToList()))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<PlaylistDto>> GetAllAsync(CollectionCaller caller, CancellationToken ct)
    {
        var restrictions = _db.RestrictionsFor(caller.UserId);
        var albumIds = restrictions.AlbumIds;
        var artistIds = restrictions.ArtistIds;
        var genreIds = restrictions.GenreIds;

        return await _db.Playlists.AsNoTracking()
            .Where(ReadableBy(caller))
            .OrderBy(p => p.Name)
            .Select(p => new PlaylistDto(
                p.Id, p.Name, p.Description, p.OwnerUserId,
                p.Owner != null ? p.Owner.DisplayName : null,
                p.IsPublic, p.IsSystemPlaylist,
                p.Items.Count(i =>
                    (i.Track.AlbumId == null || !albumIds.Contains(i.Track.AlbumId.Value)) &&
                    (i.Track.PrimaryArtistId == null || !artistIds.Contains(i.Track.PrimaryArtistId.Value)) &&
                    !i.Track.TrackGenres.Any(tg => genreIds.Contains(tg.GenreId))),
                new List<PlaylistItemDto>()
            ))
            .ToListAsync(ct);
    }
}
