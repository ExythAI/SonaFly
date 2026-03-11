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

    public string? CurrentStreamUrl => CurrentTrack != null ? _api.StreamUrl(CurrentTrack.Id) : null;
    public string? CurrentArtworkUrl => CurrentTrack?.ArtworkId != null ? _api.ArtworkUrl(CurrentTrack.ArtworkId) : null;

    public event Action<double>? SeekRequested;

    public AudioPlayerService(SonaFlyApiClient api) => _api = api;

    public void SeekTo(double positionSeconds)
    {
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
        OnPropertyChanged(nameof(CurrentStreamUrl));
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
        OnPropertyChanged(nameof(CurrentStreamUrl));
        OnPropertyChanged(nameof(CurrentArtworkUrl));
    }

    public void Next()
    {
        if (Queue.Count == 0) return;
        CurrentIndex = (CurrentIndex + 1) % Queue.Count;
        CurrentTrack = Queue[CurrentIndex];
        IsPlaying = true;
        OnPropertyChanged(nameof(CurrentStreamUrl));
        OnPropertyChanged(nameof(CurrentArtworkUrl));
    }

    public void Previous()
    {
        if (Queue.Count == 0) return;
        CurrentIndex = CurrentIndex > 0 ? CurrentIndex - 1 : Queue.Count - 1;
        CurrentTrack = Queue[CurrentIndex];
        IsPlaying = true;
        OnPropertyChanged(nameof(CurrentStreamUrl));
        OnPropertyChanged(nameof(CurrentArtworkUrl));
    }

    public bool HasNext => Queue.Count > 0 && CurrentIndex < Queue.Count - 1;
    public bool HasPrevious => Queue.Count > 0 && CurrentIndex > 0;
}
