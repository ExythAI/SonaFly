using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Services;

namespace SonaFlyUI.Server.Api.Hubs;

[Authorize]
public class AuditoriumHub : Hub
{
    private readonly AuditoriumStateService _state;
    private readonly SonaFlyDbContext _db;
    private readonly ILogger<AuditoriumHub> _logger;
    private readonly TrackEndSchedulerService _scheduler;

    // Tracks which auditorium each connection is in
    private static readonly Dictionary<string, Guid> _connectionRooms = new();
    private static readonly object _connLock = new();

    public AuditoriumHub(AuditoriumStateService state, SonaFlyDbContext db, ILogger<AuditoriumHub> logger, TrackEndSchedulerService scheduler)
    {
        _state = state;
        _db = db;
        _logger = logger;
        _scheduler = scheduler;
    }

    private Guid UserId => Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string UserName => Context.User!.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
    private bool IsAdmin => Context.User!.IsInRole("Admin");

    /// <summary>Join an auditorium room. Returns the current state.</summary>
    public async Task<AuditoriumStateSnapshot> JoinAuditorium(Guid auditoriumId)
    {
        var exists = await _db.Auditoriums.AnyAsync(a => a.Id == auditoriumId && a.IsActive);
        if (!exists) throw new HubException("Auditorium not found.");

        // Leave any current room first
        await LeaveCurrentRoom();

        var room = _state.GetOrCreateRoom(auditoriumId);
        using var lease = await room.EnterAsync();
        if (room.IsDeleted) throw new HubException("Auditorium not found.");
        await _scheduler.LoadQueueAsync(room, _db);
        room.AddUser(Context.ConnectionId, UserId, UserName);

        lock (_connLock) { _connectionRooms[Context.ConnectionId] = auditoriumId; }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"aud-{auditoriumId}");

        // If room was paused (empty) and now has users, resume
        if (room.IsPaused && room.CurrentTrackId != null)
        {
            room.Resume();
            _scheduler.ScheduleTrackEnd(room);
            _logger.LogInformation("Auditorium {Id}: resuming playback, user {User} joined", auditoriumId, UserName);
        }

        if (room.CurrentTrackId == null && room.Queue.Count > 0)
            await _scheduler.TryPlayNextFromQueue(room);

        var snapshot = room.ToSnapshot();
        await Clients.Group($"aud-{auditoriumId}").SendAsync("OnUserJoined", UserName, snapshot.ActiveUsers);

        _logger.LogInformation("User {User} joined auditorium {Id}", UserName, auditoriumId);
        return snapshot;
    }

    /// <summary>Leave the current auditorium.</summary>
    public async Task LeaveAuditorium()
    {
        await LeaveCurrentRoom();
    }

    /// <summary>Play a track. Only allowed if nothing is currently playing.</summary>
    public async Task PlayTrack(Guid trackId)
    {
        var (room, auditoriumId) = GetCurrentRoom();
        using var lease = await room.EnterAsync();
        if (room.IsDeleted) throw new HubException("Auditorium not found.");
        if (room.CurrentTrackId != null)
            throw new HubException("A track is already playing. Only the starter or an admin can stop it first.");

        var track = await _db.Tracks.AsNoTracking()
            .Where(t => t.IsIndexed && !t.IsMissing)
            .ApplyRestrictions(_db, UserId)
            .Include(t => t.PrimaryArtist)
            .Include(t => t.Album)
            .FirstOrDefaultAsync(t => t.Id == trackId);

        if (track == null) throw new HubException("Track not found.");
        if (!TrackEndSchedulerService.IsPlayable(track))
            throw new HubException("That track's file is not available on the server right now.");

        room.StartTrack(trackId, track.Title, track.PrimaryArtist?.Name,
            track.Album?.ArtworkId, track.DurationSeconds, UserId, UserName);

        _logger.LogInformation("Auditorium {Id}: {User} started playing '{Title}'", auditoriumId, UserName, track.Title);

        _scheduler.ScheduleTrackEnd(room);
        await Clients.Group($"aud-{auditoriumId}").SendAsync("OnTrackStarted", room.ToSnapshot());
    }

    /// <summary>Stop the current track. Only allowed by the person who started it or an admin.</summary>
    public async Task StopTrack()
    {
        var (room, auditoriumId) = GetCurrentRoom();
        using var lease = await room.EnterAsync();
        if (room.IsDeleted) throw new HubException("Auditorium not found.");
        if (room.CurrentTrackId == null) return;

        if (room.StartedByUserId != UserId && !IsAdmin)
            throw new HubException("Only the person who started this track or an admin can stop it.");

        room.StopPlayback();
        _logger.LogInformation("Auditorium {Id}: {User} stopped playback", auditoriumId, UserName);

        // Try to auto-play next from queue
        await _scheduler.TryPlayNextFromQueue(room);
    }

    /// <summary>Skip to the next track in the queue. Only starter or admin.</summary>
    public async Task SkipTrack()
    {
        var (room, auditoriumId) = GetCurrentRoom();
        using var lease = await room.EnterAsync();
        if (room.IsDeleted) throw new HubException("Auditorium not found.");
        if (room.CurrentTrackId == null) return;

        if (room.StartedByUserId != UserId && !IsAdmin)
            throw new HubException("Only the person who started this track or an admin can skip.");

        room.StopPlayback();
        await _scheduler.TryPlayNextFromQueue(room);
    }

    /// <summary>Add a track to the queue. Max 100 items.</summary>
    public async Task QueueTrack(Guid trackId)
    {
        var (room, auditoriumId) = GetCurrentRoom();
        using var lease = await room.EnterAsync();
        if (room.IsDeleted) throw new HubException("Auditorium not found.");
        if (room.Queue.Count >= 100)
            throw new HubException("Queue is full (max 100 tracks).");

        var track = await _db.Tracks.AsNoTracking()
            .Where(t => t.IsIndexed && !t.IsMissing)
            .ApplyRestrictions(_db, UserId)
            .Include(t => t.PrimaryArtist)
            .Include(t => t.Album)
            .FirstOrDefaultAsync(t => t.Id == trackId);

        if (track == null) throw new HubException("Track not found.");

        // Add to in-memory queue
        var queueItem = new QueueItemInfo(
            Guid.NewGuid(), trackId, track.Title, track.PrimaryArtist?.Name,
            track.Album?.ArtworkId, UserId, UserName);

        // Persist to DB
        _db.AuditoriumQueueItems.Add(new AuditoriumQueueItem
        {
            Id = queueItem.Id,
            AuditoriumId = auditoriumId,
            TrackId = trackId,
            QueuedByUserId = UserId,
            Position = (await _db.AuditoriumQueueItems
                .Where(q => q.AuditoriumId == auditoriumId)
                .MaxAsync(q => (int?)q.Position) ?? 0) + 1
        });
        await _db.SaveChangesAsync();

        room.Queue.Add(queueItem);
        _logger.LogInformation("Auditorium {Id}: {User} queued '{Title}'", auditoriumId, UserName, track.Title);

        await Clients.Group($"aud-{auditoriumId}").SendAsync("OnQueueUpdated", room.Queue.ToList());

        // If nothing is playing, auto-start this track
        if (room.CurrentTrackId == null)
        {
            await _scheduler.TryPlayNextFromQueue(room);
        }
    }

    /// <summary>Remove a track from the queue. Only the queuer or admin can remove.</summary>
    public async Task RemoveFromQueue(Guid queueItemId)
    {
        var (room, auditoriumId) = GetCurrentRoom();
        using var lease = await room.EnterAsync();
        if (room.IsDeleted) throw new HubException("Auditorium not found.");
        var item = room.Queue.FirstOrDefault(q => q.Id == queueItemId);
        if (item == null) return;

        if (item.QueuedByUserId != UserId && !IsAdmin)
            throw new HubException("Only the person who queued this track or an admin can remove it.");

        // Remove from DB
        var dbItem = await _db.AuditoriumQueueItems.FindAsync(queueItemId);
        if (dbItem != null) { _db.AuditoriumQueueItems.Remove(dbItem); await _db.SaveChangesAsync(); }
        room.Queue.Remove(item);

        await Clients.Group($"aud-{auditoriumId}").SendAsync("OnQueueUpdated", room.Queue.ToList());
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCurrentRoom();
        await base.OnDisconnectedAsync(exception);
    }

    // ── Private helpers ──

    private async Task LeaveCurrentRoom()
    {
        Guid auditoriumId;
        lock (_connLock)
        {
            if (!_connectionRooms.Remove(Context.ConnectionId, out auditoriumId)) return;
        }

        var room = _state.GetRoom(auditoriumId);
        if (room == null) return;

        using var lease = await room.EnterAsync();
        var isEmpty = room.RemoveUser(Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"aud-{auditoriumId}");

        if (isEmpty && room.CurrentTrackId != null)
        {
            room.Pause();
            _logger.LogInformation("Auditorium {Id}: paused — no active users", auditoriumId);
        }

        await Clients.Group($"aud-{auditoriumId}").SendAsync("OnUserLeft", UserName,
            room.ActiveUsers.Values.Distinct().ToList());

        _logger.LogInformation("User {User} left auditorium {Id}", UserName, auditoriumId);
    }

    /// <summary>
    /// Whether the room's current track is playable by the calling listener, together with
    /// the track the answer is about.
    /// </summary>
    /// <remarks>
    /// A room admits a track based on the restrictions of whoever queued it, but each
    /// listener's own restrictions are re-checked when they request the stream. Those two
    /// can disagree, and the product rule is that the room plays on while the affected
    /// listener is told plainly — not shown a generic playback failure.
    ///
    /// The answer names its subject because the room does not stand still while the question
    /// is being asked: by the time this returns, the track it examined may no longer be the
    /// one the caller was asking about. A bare yes/no gives the caller no way to notice, and
    /// a late "yes" about a finished track would start it playing again.
    ///
    /// This reveals nothing new: the caller is already in the room and already has the
    /// track's title and artist in the snapshot. It only answers for the track currently
    /// playing in the room the caller has joined, never for an arbitrary track id.
    /// </remarks>
    public async Task<CurrentTrackPlayability> CanPlayCurrentTrack()
    {
        var (room, _) = GetCurrentRoom();

        var trackId = room.CurrentTrackId;
        if (trackId is null) return new CurrentTrackPlayability(null, true);

        var playable = await _db.Tracks.AsNoTracking()
            .Where(t => t.IsIndexed && !t.IsMissing)
            .ApplyRestrictions(_db, UserId)
            .AnyAsync(t => t.Id == trackId.Value);

        return new CurrentTrackPlayability(trackId, playable);
    }

    private (AuditoriumRoomState room, Guid auditoriumId) GetCurrentRoom()
    {
        Guid auditoriumId;
        lock (_connLock)
        {
            if (!_connectionRooms.TryGetValue(Context.ConnectionId, out auditoriumId))
                throw new HubException("You are not in an auditorium.");
        }
        var room = _state.GetRoom(auditoriumId) ?? throw new HubException("Auditorium state not found.");
        return (room, auditoriumId);
    }
}

/// <summary>
/// The answer to "may this listener play what the room is playing", and the track it is
/// about. The caller compares <paramref name="TrackId"/> with the snapshot it was acting on
/// and discards anything that has since been overtaken.
/// </summary>
/// <param name="TrackId">The track examined; null when the room is playing nothing.</param>
/// <param name="Playable">Whether this listener may stream that track.</param>
public record CurrentTrackPlayability(Guid? TrackId, bool Playable);
