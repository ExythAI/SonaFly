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
        room.AddUser(Context.ConnectionId, UserId, UserName);

        lock (_connLock) { _connectionRooms[Context.ConnectionId] = auditoriumId; }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"aud-{auditoriumId}");

        // If room was paused (empty) and now has users, resume
        if (room.IsPaused && room.CurrentTrackId != null)
        {
            room.Resume();
            _logger.LogInformation("Auditorium {Id}: resuming playback, user {User} joined", auditoriumId, UserName);
        }

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
        if (room.CurrentTrackId != null)
            throw new HubException("A track is already playing. Only the starter or an admin can stop it first.");

        var track = await _db.Tracks.AsNoTracking()
            .Include(t => t.PrimaryArtist)
            .Include(t => t.Album)
            .FirstOrDefaultAsync(t => t.Id == trackId);

        if (track == null) throw new HubException("Track not found.");

        room.StartTrack(trackId, track.Title, track.PrimaryArtist?.Name,
            track.Album?.ArtworkId, track.DurationSeconds, UserId, UserName);

        _logger.LogInformation("Auditorium {Id}: {User} started playing '{Title}'", auditoriumId, UserName, track.Title);

        await Clients.Group($"aud-{auditoriumId}").SendAsync("OnTrackStarted", room.ToSnapshot());

        // Schedule auto-advance when track ends
        if (track.DurationSeconds.HasValue)
        {
            _scheduler.ScheduleTrackEnd(auditoriumId, trackId, track.DurationSeconds.Value);
        }
    }

    /// <summary>Stop the current track. Only allowed by the person who started it or an admin.</summary>
    public async Task StopTrack()
    {
        var (room, auditoriumId) = GetCurrentRoom();
        if (room.CurrentTrackId == null) return;

        if (room.StartedByUserId != UserId && !IsAdmin)
            throw new HubException("Only the person who started this track or an admin can stop it.");

        room.StopPlayback();
        _logger.LogInformation("Auditorium {Id}: {User} stopped playback", auditoriumId, UserName);

        // Try to auto-play next from queue
        await TryPlayNextFromQueue(room, auditoriumId);
    }

    /// <summary>Skip to the next track in the queue. Only starter or admin.</summary>
    public async Task SkipTrack()
    {
        var (room, auditoriumId) = GetCurrentRoom();
        if (room.CurrentTrackId == null) return;

        if (room.StartedByUserId != UserId && !IsAdmin)
            throw new HubException("Only the person who started this track or an admin can skip.");

        room.StopPlayback();
        await TryPlayNextFromQueue(room, auditoriumId);
    }

    /// <summary>Add a track to the queue. Max 100 items.</summary>
    public async Task QueueTrack(Guid trackId)
    {
        var (room, auditoriumId) = GetCurrentRoom();
        if (room.Queue.Count >= 100)
            throw new HubException("Queue is full (max 100 tracks).");

        var track = await _db.Tracks.AsNoTracking()
            .Include(t => t.PrimaryArtist)
            .Include(t => t.Album)
            .FirstOrDefaultAsync(t => t.Id == trackId);

        if (track == null) throw new HubException("Track not found.");

        // Add to in-memory queue
        var queueItem = new QueueItemInfo(
            Guid.NewGuid(), trackId, track.Title, track.PrimaryArtist?.Name,
            track.Album?.ArtworkId, UserId, UserName);
        room.Queue.Add(queueItem);

        // Persist to DB
        _db.AuditoriumQueueItems.Add(new AuditoriumQueueItem
        {
            Id = queueItem.Id,
            AuditoriumId = auditoriumId,
            TrackId = trackId,
            QueuedByUserId = UserId,
            Position = room.Queue.Count
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("Auditorium {Id}: {User} queued '{Title}'", auditoriumId, UserName, track.Title);

        await Clients.Group($"aud-{auditoriumId}").SendAsync("OnQueueUpdated", room.Queue);

        // If nothing is playing, auto-start this track
        if (room.CurrentTrackId == null)
        {
            await TryPlayNextFromQueue(room, auditoriumId);
        }
    }

    /// <summary>Remove a track from the queue. Only the queuer or admin can remove.</summary>
    public async Task RemoveFromQueue(Guid queueItemId)
    {
        var (room, auditoriumId) = GetCurrentRoom();
        var item = room.Queue.FirstOrDefault(q => q.Id == queueItemId);
        if (item == null) return;

        if (item.QueuedByUserId != UserId && !IsAdmin)
            throw new HubException("Only the person who queued this track or an admin can remove it.");

        room.Queue.Remove(item);

        // Remove from DB
        var dbItem = await _db.AuditoriumQueueItems.FindAsync(queueItemId);
        if (dbItem != null) { _db.AuditoriumQueueItems.Remove(dbItem); await _db.SaveChangesAsync(); }

        await Clients.Group($"aud-{auditoriumId}").SendAsync("OnQueueUpdated", room.Queue);
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

    private async Task TryPlayNextFromQueue(AuditoriumRoomState room, Guid auditoriumId)
    {
        if (room.Queue.Count == 0)
        {
            await Clients.Group($"aud-{auditoriumId}").SendAsync("OnTrackEnded", room.ToSnapshot());
            return;
        }

        var next = room.Queue[0];
        room.Queue.RemoveAt(0);

        // Remove from DB
        var dbItem = await _db.AuditoriumQueueItems.FindAsync(next.Id);
        if (dbItem != null) { _db.AuditoriumQueueItems.Remove(dbItem); await _db.SaveChangesAsync(); }

        // Load track details for duration
        var track = await _db.Tracks.AsNoTracking()
            .Include(t => t.PrimaryArtist)
            .Include(t => t.Album)
            .FirstOrDefaultAsync(t => t.Id == next.TrackId);

        if (track != null)
        {
            room.StartTrack(track.Id, track.Title, track.PrimaryArtist?.Name,
                track.Album?.ArtworkId, track.DurationSeconds, next.QueuedByUserId, next.QueuedByUserName);

            _logger.LogInformation("Auditorium {Id}: auto-advancing to '{Title}'", auditoriumId, track.Title);

            await Clients.Group($"aud-{auditoriumId}").SendAsync("OnTrackStarted", room.ToSnapshot());
            await Clients.Group($"aud-{auditoriumId}").SendAsync("OnQueueUpdated", room.Queue);

            if (track.DurationSeconds.HasValue)
                _scheduler.ScheduleTrackEnd(auditoriumId, track.Id, track.DurationSeconds.Value);
        }
        else
        {
            // Track was deleted, try next
            await TryPlayNextFromQueue(room, auditoriumId);
        }
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
