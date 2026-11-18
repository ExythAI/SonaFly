using CommunityToolkit.Mvvm.ComponentModel;
using SonaFly.Models;

namespace SonaFly.Services;

public partial class AudioPlayerService : ObservableObject
{
    private readonly SonaFlyApiClient _api;

    [ObservableProperty] private TrackDto? _currentTrack;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private List<TrackDto> _queue = [];
    [ObservableProperty] private int _currentIndex;

    public Task<string?> GetCurrentStreamUrlAsync() => CurrentTrack is { } track
        ? GetStreamUrlAsync(track.Id) : Task.FromResult<string?>(null);

    private async Task<string?> GetStreamUrlAsync(Guid trackId) =>
        await _api.GetStreamUrlAsync(trackId, UseCompressedStreams());

    /// <summary>
    /// Asked each time a track starts: whether to request the smaller compressed stream.
    /// Supplied by the app from the listener's setting and the current connection.
    /// </summary>
    public Func<bool> UseCompressedStreams { get; set; } = () => false;

    // Changes signal that the player should request a fresh authorized URL.
    public int StreamVersion { get; private set; }

    private void NotifyStreamChanged()
    {
        // The new track starts from the top. Its length comes from the catalog until
        // the media element reports the real one.
        _seekTarget = null;
        Position = 0;
        Duration = PlaybackProgress.Sanitize(CurrentTrack?.DurationSeconds ?? 0);

        StreamVersion++;
        OnPropertyChanged(nameof(StreamVersion));
    }

    // ── Progress, reported by the surface that owns playback ──

    private static readonly TimeSpan SeekSettleWindow = TimeSpan.FromSeconds(5);
    private (double Position, DateTime RequestedUtc)? _seekTarget;

    /// <summary>
    /// Records where the media element says playback is. Small changes are dropped, and
    /// so are positions from before a seek the element has not carried out yet, which
    /// would otherwise make the progress bar jump back and then forward again.
    /// </summary>
    public void ReportPosition(double positionSeconds)
    {
        if (_seekTarget is { } target)
        {
            var settled = Math.Abs(positionSeconds - target.Position) <= 2;
            var expired = DateTime.UtcNow - target.RequestedUtc > SeekSettleWindow;
            if (settled == false && expired == false) return;
            _seekTarget = null;
        }

        if (PlaybackProgress.IsWorthReporting(Position, positionSeconds))
        {
            Position = PlaybackProgress.Clamp(positionSeconds, Duration);
        }
    }

    /// <summary>Records the track length the media element measured, when it has one.</summary>
    public void ReportDuration(double durationSeconds)
    {
        var duration = PlaybackProgress.Sanitize(durationSeconds);
        if (duration > 0) Duration = duration;
    }
    /// <summary>Reports the current track as played, once it has actually started.</summary>
    public Task ReportCurrentPlayAsync() =>
        CurrentTrack is { } track ? _api.ReportPlayAsync(track.Id) : Task.CompletedTask;

    public string? CurrentArtworkUrl => CurrentTrack?.ArtworkId != null ? _api.ArtworkUrl(CurrentTrack.ArtworkId) : null;

    public event Action<double>? SeekRequested;

    /// <summary>Which of the per-tab player surfaces owns playback.</summary>
    public PlaybackSurfaceCoordinator Surfaces { get; } = new();

    public AudioPlayerService(SonaFlyApiClient api) => _api = api;

    public void SeekTo(double positionSeconds)
    {
        positionSeconds = PlaybackProgress.Clamp(positionSeconds, Duration);
        _seekTarget = (positionSeconds, DateTime.UtcNow);
        Position = positionSeconds;
        SeekRequested?.Invoke(positionSeconds);
    }

    public void Play(TrackDto track, List<TrackDto>? queue = null)
    {
        if (queue != null)
        {
            Queue = queue;
            CurrentIndex = queue.IndexOf(track);
            if (CurrentIndex < 0) CurrentIndex = 0;
        }
        else
        {
            Queue = [track];
            CurrentIndex = 0;
        }

        CurrentTrack = track;
        IsPlaying = true;
        NotifyStreamChanged();
        OnPropertyChanged(nameof(CurrentArtworkUrl));
    }

    public void Pause() => IsPlaying = false;
    public void Resume() => IsPlaying = true;
    public void Stop()
    {
        IsPlaying = false;
        CurrentTrack = null;
        Position = 0;
        Duration = 0;
        NotifyStreamChanged();
        OnPropertyChanged(nameof(CurrentArtworkUrl));
    }

    public void Next()
    {
        if (Queue.Count == 0) return;
        CurrentIndex = (CurrentIndex + 1) % Queue.Count;
        CurrentTrack = Queue[CurrentIndex];
        IsPlaying = true;
        NotifyStreamChanged();
        OnPropertyChanged(nameof(CurrentArtworkUrl));
    }

    public void Previous()
    {
        if (Queue.Count == 0) return;
        CurrentIndex = CurrentIndex > 0 ? CurrentIndex - 1 : Queue.Count - 1;
        CurrentTrack = Queue[CurrentIndex];
        IsPlaying = true;
        NotifyStreamChanged();
        OnPropertyChanged(nameof(CurrentArtworkUrl));
    }

    public bool HasNext => Queue.Count > 0 && CurrentIndex < Queue.Count - 1;
    public bool HasPrevious => Queue.Count > 0 && CurrentIndex > 0;
}
