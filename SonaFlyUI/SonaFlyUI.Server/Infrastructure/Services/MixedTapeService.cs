using System.Linq.Expressions;
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

    // ── Access ──
    //
    // Mixed tapes have no public flag, so reading and writing coincide: the owner, or an
    // administrator.

    private static Expression<Func<MixedTape, bool>> AccessibleBy(CollectionCaller caller) =>
        caller.IsAdmin
            ? _ => true
            : m => m.OwnerUserId == caller.UserId;

    /// <summary>
    /// Loads a mixed tape the caller may modify. Someone else's tape is reported as missing,
    /// not forbidden, so guessing IDs confirms nothing.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such tape, or it belongs to another user.</exception>
    private async Task<MixedTape> LoadForWriteAsync(Guid mixedTapeId, CollectionCaller caller, CancellationToken ct)
    {
        var tape = await _db.MixedTapes.FirstOrDefaultAsync(m => m.Id == mixedTapeId, ct);

        if (tape == null || !(caller.IsAdmin || tape.OwnerUserId == caller.UserId))
        {
            throw new KeyNotFoundException($"Mixed tape {mixedTapeId} not found.");
        }

        return tape;
    }

    // ── Commands ──

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

    public async Task DeleteAsync(Guid id, CollectionCaller caller, CancellationToken ct)
    {
        var tape = await LoadForWriteAsync(id, caller, ct);
        _db.MixedTapes.Remove(tape);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddTrackAsync(Guid mixedTapeId, Guid trackId, CollectionCaller caller, CancellationToken ct)
    {
        var tape = await _db.MixedTapes
            .Include(m => m.Items).ThenInclude(i => i.Track)
            .Where(AccessibleBy(caller))
            .FirstOrDefaultAsync(m => m.Id == mixedTapeId, ct)
            ?? throw new KeyNotFoundException($"Mixed tape {mixedTapeId} not found.");

        // Restricted tracks are treated as absent, so they cannot be smuggled onto a tape
        // and streamed from there.
        var track = await _db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .ApplyRestrictions(_db, caller.UserId)
            .FirstOrDefaultAsync(ct)
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

    public async Task RemoveItemAsync(Guid mixedTapeId, Guid itemId, CollectionCaller caller, CancellationToken ct)
    {
        await LoadForWriteAsync(mixedTapeId, caller, ct);

        var item = await _db.MixedTapeItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.MixedTapeId == mixedTapeId, ct)
            ?? throw new KeyNotFoundException($"Mixed tape item {itemId} not found.");

        _db.MixedTapeItems.Remove(item);
        await _db.SaveChangesAsync(ct);
    }

    // ── Queries ──

    public async Task<IReadOnlyList<MixedTapeDto>> GetAllAsync(CollectionCaller caller, CancellationToken ct)
    {
        var tapes = await _db.MixedTapes.AsNoTracking()
            .Include(m => m.Owner)
            .Include(m => m.Items).ThenInclude(i => i.Track).ThenInclude(t => t.TrackGenres)
            .Where(AccessibleBy(caller))
            .OrderByDescending(m => m.CreatedUtc)
            .ToListAsync(ct);

        var restrictions = await LoadRestrictionsAsync(caller.UserId, ct);
        return tapes.Select(m => MapToDto(m, restrictions)).ToList();
    }

    public async Task<MixedTapeDto?> GetByIdAsync(Guid id, CollectionCaller caller, CancellationToken ct)
    {
        var tape = await _db.MixedTapes.AsNoTracking()
            .Include(m => m.Owner)
            .Include(m => m.Items.OrderBy(i => i.SortOrder))
                .ThenInclude(i => i.Track)
                    .ThenInclude(t => t.PrimaryArtist)
            .Include(m => m.Items)
                .ThenInclude(i => i.Track)
                    .ThenInclude(t => t.Album)
            .Include(m => m.Items)
                .ThenInclude(i => i.Track)
                    .ThenInclude(t => t.TrackGenres)
            .Where(AccessibleBy(caller))
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (tape == null) return null;

        return MapToDto(tape, await LoadRestrictionsAsync(caller.UserId, ct));
    }

    /// <summary>
    /// The caller's deny lists, materialised once. Mixed tapes are mapped in memory (the
    /// duration arithmetic does not translate cleanly), so the filter needs real sets rather
    /// than the composable subqueries used for query-side filtering.
    /// </summary>
    private async Task<DenyLists> LoadRestrictionsAsync(Guid userId, CancellationToken ct)
    {
        var sets = _db.RestrictionsFor(userId);
        return new DenyLists(
            (await sets.AlbumIds.ToListAsync(ct)).ToHashSet(),
            (await sets.ArtistIds.ToListAsync(ct)).ToHashSet(),
            (await sets.GenreIds.ToListAsync(ct)).ToHashSet());
    }

    private sealed record DenyLists(HashSet<Guid> Albums, HashSet<Guid> Artists, HashSet<Guid> Genres)
    {
        public bool Allows(Track t) =>
            (t.AlbumId == null || !Albums.Contains(t.AlbumId.Value)) &&
            (t.PrimaryArtistId == null || !Artists.Contains(t.PrimaryArtistId.Value)) &&
            !t.TrackGenres.Any(tg => Genres.Contains(tg.GenreId));
    }

    private static MixedTapeDto MapToDto(MixedTape m, DenyLists restrictions)
    {
        // Restricted entries are dropped from the listing, and from the totals with them, so
        // the remaining-time figure still describes what the caller is looking at.
        var items = m.Items.Where(i => restrictions.Allows(i.Track)).OrderBy(i => i.SortOrder).ToList();
        var totalDuration = items.Sum(i => i.Track.DurationSeconds ?? 0);

        return new MixedTapeDto(
            m.Id, m.Name, m.OwnerUserId,
            m.Owner?.DisplayName,
            m.TargetDurationSeconds,
            totalDuration,
            Math.Max(0, m.TargetDurationSeconds - totalDuration),
            items.Count,
            items.Select(i => new MixedTapeItemDto(
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
