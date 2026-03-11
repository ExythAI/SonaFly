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

    public async Task UpdateAsync(Guid playlistId, UpdatePlaylistRequest request, CancellationToken ct)
    {
        var playlist = await _db.Playlists.FindAsync([playlistId], ct)
            ?? throw new KeyNotFoundException($"Playlist {playlistId} not found.");

        if (request.Name != null) playlist.Name = request.Name.Trim();
        if (request.Description != null) playlist.Description = request.Description.Trim();
        if (request.IsPublic.HasValue) playlist.IsPublic = request.IsPublic.Value;
        playlist.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid playlistId, CancellationToken ct)
    {
        var playlist = await _db.Playlists.FindAsync([playlistId], ct)
            ?? throw new KeyNotFoundException($"Playlist {playlistId} not found.");

        _db.Playlists.Remove(playlist);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PlaylistDto?> GetByIdAsync(Guid playlistId, CancellationToken ct)
    {
        var playlist = await _db.Playlists
            .AsNoTracking()
            .Include(p => p.Owner)
            .Include(p => p.Items.OrderBy(i => i.SortOrder))
                .ThenInclude(i => i.Track)
                    .ThenInclude(t => t.PrimaryArtist)
            .Include(p => p.Items)
                .ThenInclude(i => i.Track)
                    .ThenInclude(t => t.Album)
            .FirstOrDefaultAsync(p => p.Id == playlistId, ct);

        return playlist == null ? null : MapToDto(playlist);
    }

    public async Task<IReadOnlyList<PlaylistDto>> GetAllAsync(Guid? ownerUserId, CancellationToken ct)
    {
        var query = _db.Playlists.AsNoTracking()
            .Include(p => p.Owner)
            .Include(p => p.Items)
            .AsQueryable();

        if (ownerUserId.HasValue)
            query = query.Where(p => p.OwnerUserId == ownerUserId || p.IsPublic || p.IsSystemPlaylist);

        return await query
            .OrderBy(p => p.Name)
            .Select(p => new PlaylistDto(
                p.Id, p.Name, p.Description, p.OwnerUserId,
                p.Owner != null ? p.Owner.DisplayName : null,
                p.IsPublic, p.IsSystemPlaylist,
                p.Items.Count,
                new List<PlaylistItemDto>()
            ))
            .ToListAsync(ct);
    }

    public async Task AddTrackAsync(Guid playlistId, Guid trackId, CancellationToken ct)
    {
        var playlist = await _db.Playlists.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == playlistId, ct)
            ?? throw new KeyNotFoundException($"Playlist {playlistId} not found.");

        var maxOrder = playlist.Items.Any() ? playlist.Items.Max(i => i.SortOrder) : 0;

        var item = new PlaylistItem
        {
            PlaylistId = playlistId,
            TrackId = trackId,
            SortOrder = maxOrder + 1,
            AddedUtc = DateTime.UtcNow
        };
        _db.PlaylistItems.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveItemAsync(Guid playlistId, Guid itemId, CancellationToken ct)
    {
        var item = await _db.PlaylistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.PlaylistId == playlistId, ct)
            ?? throw new KeyNotFoundException($"Playlist item {itemId} not found.");

        _db.PlaylistItems.Remove(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(Guid playlistId, ReorderPlaylistItemsRequest request, CancellationToken ct)
    {
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

    private static PlaylistDto MapToDto(Playlist p) => new(
        p.Id, p.Name, p.Description, p.OwnerUserId,
        p.Owner?.DisplayName,
        p.IsPublic, p.IsSystemPlaylist,
        p.Items.Count,
        p.Items.OrderBy(i => i.SortOrder).Select(i => new PlaylistItemDto(
            i.Id, i.TrackId,
            i.Track.Title,
            i.Track.PrimaryArtist?.Name,
            i.Track.Album?.Title,
            i.Track.DurationSeconds,
            i.SortOrder
        )).ToList()
    );
}
