using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Application.DTOs;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

public class MixedTapeService : IMixedTapeService
{
    private readonly SonaFlyDbContext _db;

    public MixedTapeService(SonaFlyDbContext db) => _db = db;

    public async Task<Guid> CreateAsync(CreateMixedTapeRequest request, Guid ownerUserId, CancellationToken ct)
    {
        var tape = new MixedTape
        {
            Name = request.Name.Trim(),
            OwnerUserId = ownerUserId,
            TargetDurationSeconds = 3600
        };
        _db.MixedTapes.Add(tape);
        await _db.SaveChangesAsync(ct);
        return tape.Id;
    }

    public async Task<IReadOnlyList<MixedTapeDto>> GetAllAsync(Guid ownerUserId, CancellationToken ct)
    {
        return await _db.MixedTapes.AsNoTracking()
            .Include(m => m.Owner)
            .Include(m => m.Items).ThenInclude(i => i.Track)
            .Where(m => m.OwnerUserId == ownerUserId)
            .OrderByDescending(m => m.CreatedUtc)
            .Select(m => MapToDto(m))
            .ToListAsync(ct);
    }

    public async Task<MixedTapeDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var tape = await _db.MixedTapes.AsNoTracking()
            .Include(m => m.Owner)
            .Include(m => m.Items.OrderBy(i => i.SortOrder))
                .ThenInclude(i => i.Track)
                    .ThenInclude(t => t.PrimaryArtist)
            .Include(m => m.Items)
                .ThenInclude(i => i.Track)
                    .ThenInclude(t => t.Album)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return tape == null ? null : MapToDto(tape);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var tape = await _db.MixedTapes.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Mixed tape {id} not found.");
        _db.MixedTapes.Remove(tape);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddTrackAsync(Guid mixedTapeId, Guid trackId, CancellationToken ct)
    {
        var tape = await _db.MixedTapes
            .Include(m => m.Items).ThenInclude(i => i.Track)
            .FirstOrDefaultAsync(m => m.Id == mixedTapeId, ct)
            ?? throw new KeyNotFoundException($"Mixed tape {mixedTapeId} not found.");

        var track = await _db.Tracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == trackId, ct)
            ?? throw new KeyNotFoundException($"Track {trackId} not found.");

        // Enforce 60-minute limit
        var currentDuration = tape.Items.Sum(i => i.Track.DurationSeconds ?? 0);
        var newDuration = currentDuration + (track.DurationSeconds ?? 0);
        if (newDuration > tape.TargetDurationSeconds)
            throw new InvalidOperationException(
                $"Adding this track ({track.DurationSeconds:F0}s) would exceed the {tape.TargetDurationSeconds}s limit. " +
                $"Current: {currentDuration:F0}s, Would be: {newDuration:F0}s.");

        // Prevent duplicates
        if (tape.Items.Any(i => i.TrackId == trackId))
            throw new InvalidOperationException("This track is already on the mixed tape.");

        var maxOrder = tape.Items.Any() ? tape.Items.Max(i => i.SortOrder) : 0;

        _db.MixedTapeItems.Add(new MixedTapeItem
        {
            MixedTapeId = mixedTapeId,
            TrackId = trackId,
            SortOrder = maxOrder + 1,
            AddedUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveItemAsync(Guid mixedTapeId, Guid itemId, CancellationToken ct)
    {
        var item = await _db.MixedTapeItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.MixedTapeId == mixedTapeId, ct)
            ?? throw new KeyNotFoundException($"Mixed tape item {itemId} not found.");

        _db.MixedTapeItems.Remove(item);
        await _db.SaveChangesAsync(ct);
    }

    private static MixedTapeDto MapToDto(MixedTape m)
    {
        var totalDuration = m.Items.Sum(i => i.Track.DurationSeconds ?? 0);
        return new MixedTapeDto(
            m.Id, m.Name, m.OwnerUserId,
            m.Owner?.DisplayName,
            m.TargetDurationSeconds,
            totalDuration,
            Math.Max(0, m.TargetDurationSeconds - totalDuration),
            m.Items.Count,
            m.Items.OrderBy(i => i.SortOrder).Select(i => new MixedTapeItemDto(
                i.Id, i.TrackId,
                i.Track.Title,
                i.Track.PrimaryArtist?.Name,
                i.Track.Album?.Title,
                i.Track.AlbumId,
                i.Track.DurationSeconds,
                i.Track.Album?.ArtworkId,
                i.SortOrder
            )).ToList()
        );
    }
}
