using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Api.Hubs;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>
/// Background-safe service that schedules track-end auto-advance for auditoriums.
/// Uses IHubContext (not the Hub instance) so it works after the Hub method returns.
/// </summary>
public class TrackEndSchedulerService
{
    private readonly AuditoriumStateService _state;
    private readonly IHubContext<AuditoriumHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrackEndSchedulerService> _logger;

    public TrackEndSchedulerService(
        AuditoriumStateService state,
        IHubContext<AuditoriumHub> hubContext,
        IServiceScopeFactory scopeFactory,
        ILogger<TrackEndSchedulerService> logger)
    {
        _state = state;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Schedule auto-advance after the given track finishes playing.
    /// This runs outside the Hub scope using IHubContext, so Clients calls work properly.
    /// </summary>
    public void ScheduleTrackEnd(Guid auditoriumId, Guid trackId, double durationSeconds)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds + 1)); // small buffer

                var room = _state.GetRoom(auditoriumId);
                if (room == null || room.CurrentTrackId != trackId) return; // already changed
                if (room.IsPaused) return; // paused because empty — don't advance

                room.StopPlayback();
                _logger.LogInformation("Auditorium {Id}: track ended, advancing queue", auditoriumId);

                await TryPlayNextFromQueue(room, auditoriumId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ScheduleTrackEnd for auditorium {Id}", auditoriumId);
            }
        });
    }

    private async Task TryPlayNextFromQueue(AuditoriumRoomState room, Guid auditoriumId)
    {
        if (room.Queue.Count == 0)
        {
            _logger.LogInformation("Auditorium {Id}: queue empty, sending TrackEnded", auditoriumId);
            await _hubContext.Clients.Group($"aud-{auditoriumId}")
                .SendAsync("OnTrackEnded", room.ToSnapshot());
            return;
        }

        var next = room.Queue[0];
        room.Queue.RemoveAt(0);

        // Remove from DB using a new scope (we're outside the Hub's DI scope)
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();

        var dbItem = await db.AuditoriumQueueItems.FindAsync(next.Id);
        if (dbItem != null) { db.AuditoriumQueueItems.Remove(dbItem); await db.SaveChangesAsync(); }

        // Load track details for duration
        var track = await db.Tracks.AsNoTracking()
            .Include(t => t.PrimaryArtist)
            .Include(t => t.Album)
            .FirstOrDefaultAsync(t => t.Id == next.TrackId);

        if (track != null)
        {
            room.StartTrack(track.Id, track.Title, track.PrimaryArtist?.Name,
                track.Album?.ArtworkId, track.DurationSeconds, next.QueuedByUserId, next.QueuedByUserName);

            _logger.LogInformation("Auditorium {Id}: auto-advancing to '{Title}'", auditoriumId, track.Title);

            await _hubContext.Clients.Group($"aud-{auditoriumId}")
                .SendAsync("OnTrackStarted", room.ToSnapshot());

            // Also notify queue updated
            await _hubContext.Clients.Group($"aud-{auditoriumId}")
                .SendAsync("OnQueueUpdated", room.Queue);

            if (track.DurationSeconds.HasValue)
                ScheduleTrackEnd(auditoriumId, track.Id, track.DurationSeconds.Value);
        }
        else
        {
            // Track was deleted, try next
            await TryPlayNextFromQueue(room, auditoriumId);
        }
    }
}
