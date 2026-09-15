using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SonaFlyUI.Server.Api.Hubs;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.Data;

namespace SonaFlyUI.Server.Infrastructure.Services;

// All methods changing a room are called while holding room.EnterAsync().
public class TrackEndSchedulerService(
    IHubContext<AuditoriumHub> hubContext,
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    AuditoriumStateService auditoriums,
    ILogger<TrackEndSchedulerService> logger)
{
    /// <summary>
    /// Ceiling used when a track's duration is unknown or nonsensical. Shared web playback waits
    /// for the server to advance the room, so without a bounded fallback such a track parks the
    /// room forever until its starter or an admin intervenes (backlog N19). The value is longer
    /// than almost any single track, so it never truncates a normal one.
    /// </summary>
    public static readonly TimeSpan UnknownDurationCeiling = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A track is only admissible to shared playback if it is indexed, present in the index and
    /// actually readable on disk. DB flags alone can admit a file that was deleted since the
    /// last scan, which then cannot stream at all.
    /// </summary>
    public static bool IsPlayable([NotNullWhen(true)] Track? track)
        => track != null
           && track.IsIndexed
           && !track.IsMissing
           && !string.IsNullOrWhiteSpace(track.FilePath)
           && File.Exists(track.FilePath);

    public void ScheduleTrackEnd(AuditoriumRoomState room)
    {
        room.CancelTrackEnd();
        if (room.IsPaused || room.IsDeleted || room.CurrentTrackId == null) return;

        var source = room.CreateTrackEnd(lifetime.ApplicationStopping);
        var generation = room.PlaybackGeneration;

        double remaining;
        if (room.CurrentTrackDuration is double duration && double.IsFinite(duration) && duration >= 0)
        {
            remaining = Math.Max(0, duration - room.GetCurrentPositionSeconds());
        }
        else
        {
            // Unknown duration: still guarantee a completion path rather than returning and
            // leaving the room with no scheduled advance at all.
            remaining = Math.Max(0, UnknownDurationCeiling.TotalSeconds - room.GetCurrentPositionSeconds());
            logger.LogWarning(
                "Auditorium {Id}: track {TrackId} has no usable duration; advancing after at most {Ceiling}.",
                room.AuditoriumId, room.CurrentTrackId, UnknownDurationCeiling);
        }

        _ = CompleteTrackAsync(room, source, generation, remaining);
    }

    private async Task CompleteTrackAsync(AuditoriumRoomState room, CancellationTokenSource source,
        long generation, double remaining)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(remaining + 1), source.Token);
            using var lease = await room.EnterAsync(source.Token);
            if (room.IsDeleted || room.IsPaused || room.PlaybackGeneration != generation) return;
            room.StopPlayback();
            await TryPlayNextFromQueue(room);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error advancing auditorium {Id}", room.AuditoriumId);
        }
        finally
        {
            using var lease = await room.EnterAsync();
            room.ClearTrackEnd(source);
            source.Dispose();
        }
    }

    /// <summary>
    /// Stops playback and empties the in-memory queue of every live room, telling listeners why.
    /// Called before a library purge: the rooms hold track IDs and queue entries that are about
    /// to stop existing, and leaving them in place means clients keep playing or requesting
    /// deleted tracks (backlog N16).
    /// </summary>
    /// <returns>How many rooms were reset.</returns>
    public async Task<int> ResetAllRoomsAsync(string reason)
    {
        var rooms = auditoriums.GetAllRooms();
        var reset = 0;

        foreach (var room in rooms)
        {
            using var lease = await room.EnterAsync();

            room.CancelTrackEnd();
            room.StopPlayback();
            room.Queue.Clear();
            room.QueueLoaded = false;
            reset++;

            await hubContext.Clients.Group($"aud-{room.AuditoriumId}")
                .SendAsync("OnLibraryReset", new { reason });
            await hubContext.Clients.Group($"aud-{room.AuditoriumId}")
                .SendAsync("OnQueueUpdated", room.Queue.ToList());
            await hubContext.Clients.Group($"aud-{room.AuditoriumId}")
                .SendAsync("OnTrackEnded", room.ToSnapshot());
        }

        if (reset > 0)
            logger.LogWarning("Reset {Count} auditorium room(s): {Reason}", reset, reason);

        return reset;
    }

    public async Task LoadQueueAsync(AuditoriumRoomState room, SonaFlyDbContext db)
    {
        if (room.QueueLoaded) return;
        room.Queue = await db.AuditoriumQueueItems.AsNoTracking()
            .Where(q => q.AuditoriumId == room.AuditoriumId)
            .OrderBy(q => q.Position).ThenBy(q => q.Id)
            .Select(q => new QueueItemInfo(q.Id, q.TrackId, q.Track!.Title,
                q.Track.PrimaryArtist != null ? q.Track.PrimaryArtist.Name : null,
                q.Track.Album != null ? q.Track.Album.ArtworkId : null,
                q.QueuedByUserId, q.QueuedByUser != null ? q.QueuedByUser.UserName! : "Unknown"))
            .ToListAsync();
        room.QueueLoaded = true;
    }

    public async Task TryPlayNextFromQueue(AuditoriumRoomState room)
    {
        if (room.IsDeleted) return;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();
        while (room.Queue.Count > 0)
        {
            var next = room.Queue[0];
            var track = await db.Tracks.AsNoTracking()
                .Where(t => t.IsIndexed && !t.IsMissing)
                .ApplyRestrictions(db, next.QueuedByUserId)
                .Include(t => t.PrimaryArtist).Include(t => t.Album)
                .FirstOrDefaultAsync(t => t.Id == next.TrackId);
            var dbItem = await db.AuditoriumQueueItems.FindAsync(next.Id);
            if (dbItem != null)
            {
                db.AuditoriumQueueItems.Remove(dbItem);
                await db.SaveChangesAsync();
            }
            room.Queue.RemoveAt(0);

            if (!IsPlayable(track))
            {
                // Tell the room why the item vanished instead of silently dropping it.
                var reason = track == null
                    ? "it is no longer available to the person who queued it"
                    : "its file could not be found on the server";
                logger.LogWarning("Auditorium {Id}: skipping queued track {TrackId} because {Reason}.",
                    room.AuditoriumId, next.TrackId, reason);
                await hubContext.Clients.Group($"aud-{room.AuditoriumId}")
                    .SendAsync("OnQueueItemSkipped", new { trackId = next.TrackId, title = next.Title, reason });
                continue;
            }

            room.StartTrack(track.Id, track.Title, track.PrimaryArtist?.Name,
                track.Album?.ArtworkId, track.DurationSeconds, next.QueuedByUserId, next.QueuedByUserName);
            if (room.ActiveUsers.Count == 0) room.Pause();
            ScheduleTrackEnd(room);
            await hubContext.Clients.Group($"aud-{room.AuditoriumId}")
                .SendAsync("OnTrackStarted", room.ToSnapshot());
            await hubContext.Clients.Group($"aud-{room.AuditoriumId}")
                .SendAsync("OnQueueUpdated", room.Queue.ToList());
            return;
        }
        await hubContext.Clients.Group($"aud-{room.AuditoriumId}")
            .SendAsync("OnQueueUpdated", room.Queue.ToList());
        await hubContext.Clients.Group($"aud-{room.AuditoriumId}")
            .SendAsync("OnTrackEnded", room.ToSnapshot());
    }
}
