using SonaFlyUI.Server.Domain.Entities;

namespace SonaFlyUI.Server.Infrastructure.Services;

/// <summary>
/// In-memory state for all active auditoriums.
/// Tracks current playback, queue, and connected users.
/// </summary>
public class AuditoriumStateService
{
    private readonly Dictionary<Guid, AuditoriumRoomState> _rooms = new();
    private readonly object _lock = new();

    public AuditoriumRoomState GetOrCreateRoom(Guid auditoriumId)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(auditoriumId, out var room))
            {
                room = new AuditoriumRoomState { AuditoriumId = auditoriumId };
                _rooms[auditoriumId] = room;
            }
            return room;
        }
    }

    public AuditoriumRoomState? GetRoom(Guid auditoriumId)
    {
        lock (_lock)
        {
            return _rooms.GetValueOrDefault(auditoriumId);
        }
    }

    public void RemoveRoom(Guid auditoriumId)
    {
        lock (_lock) { _rooms.Remove(auditoriumId); }
    }

    public IReadOnlyList<AuditoriumRoomState> GetAllRooms()
    {
        lock (_lock) { return _rooms.Values.ToList(); }
    }
}

public class AuditoriumRoomState
{
    private readonly object _lock = new();

    public Guid AuditoriumId { get; set; }

    // Current playback
    public Guid? CurrentTrackId { get; set; }
    public string? CurrentTrackTitle { get; set; }
    public string? CurrentArtistName { get; set; }
    public Guid? CurrentArtworkId { get; set; }
    public double? CurrentTrackDuration { get; set; }
    public DateTime? PlaybackStartedAtUtc { get; set; }
    public Guid? StartedByUserId { get; set; }
    public string? StartedByUserName { get; set; }
    public bool IsPaused { get; set; }
    public double PausedAtSeconds { get; set; }

    // Queue (in-memory, synced from DB)
    public List<QueueItemInfo> Queue { get; set; } = [];

    // Active users
    public Dictionary<string, ActiveUserInfo> ActiveUsers { get; set; } = new();

    public double GetCurrentPositionSeconds()
    {
        if (CurrentTrackId == null || PlaybackStartedAtUtc == null) return 0;
        if (IsPaused) return PausedAtSeconds;
        return (DateTime.UtcNow - PlaybackStartedAtUtc.Value).TotalSeconds;
    }

    public void AddUser(string connectionId, Guid userId, string displayName)
    {
        lock (_lock)
        {
            ActiveUsers[connectionId] = new ActiveUserInfo(userId, displayName);
        }
    }

    public bool RemoveUser(string connectionId)
    {
        lock (_lock)
        {
            ActiveUsers.Remove(connectionId);
            return ActiveUsers.Count == 0;
        }
    }

    public void StartTrack(Guid trackId, string title, string? artistName, Guid? artworkId,
        double? duration, Guid userId, string userName)
    {
        CurrentTrackId = trackId;
        CurrentTrackTitle = title;
        CurrentArtistName = artistName;
        CurrentArtworkId = artworkId;
        CurrentTrackDuration = duration;
        PlaybackStartedAtUtc = DateTime.UtcNow;
        StartedByUserId = userId;
        StartedByUserName = userName;
        IsPaused = false;
        PausedAtSeconds = 0;
    }

    public void StopPlayback()
    {
        CurrentTrackId = null;
        CurrentTrackTitle = null;
        CurrentArtistName = null;
        CurrentArtworkId = null;
        CurrentTrackDuration = null;
        PlaybackStartedAtUtc = null;
        StartedByUserId = null;
        StartedByUserName = null;
        IsPaused = false;
        PausedAtSeconds = 0;
    }

    public void Pause()
    {
        if (CurrentTrackId != null && !IsPaused)
        {
            PausedAtSeconds = GetCurrentPositionSeconds();
            IsPaused = true;
        }
    }

    public void Resume()
    {
        if (CurrentTrackId != null && IsPaused)
        {
            PlaybackStartedAtUtc = DateTime.UtcNow.AddSeconds(-PausedAtSeconds);
            IsPaused = false;
        }
    }

    public AuditoriumStateSnapshot ToSnapshot() => new(
        AuditoriumId, CurrentTrackId, CurrentTrackTitle, CurrentArtistName, CurrentArtworkId,
        CurrentTrackDuration, GetCurrentPositionSeconds(), IsPaused,
        StartedByUserId, StartedByUserName,
        Queue.ToList(),
        ActiveUsers.Values.Distinct().ToList(),
        DateTime.UtcNow
    );
}

public record QueueItemInfo(Guid Id, Guid TrackId, string Title, string? ArtistName, Guid? ArtworkId, Guid QueuedByUserId, string QueuedByUserName);
public record ActiveUserInfo(Guid UserId, string DisplayName);

public record AuditoriumStateSnapshot(
    Guid AuditoriumId,
    Guid? CurrentTrackId, string? CurrentTrackTitle, string? CurrentArtistName, Guid? CurrentArtworkId,
    double? CurrentTrackDuration, double CurrentPositionSeconds, bool IsPaused,
    Guid? StartedByUserId, string? StartedByUserName,
    List<QueueItemInfo> Queue,
    List<ActiveUserInfo> ActiveUsers,
    DateTime ServerUtcNow
);
