using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Services;

namespace SonaFly.Views.Controls;

public partial class MiniPlayerControl : ContentView
{
    private AudioPlayerService? _player;
    private MiniPlayerViewModel? _vm;
    private bool _initialized;
    private int _sourceRequest;
    private bool _mediaOpened;
    private double? _pendingSeek;

    public MiniPlayerControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// True while this control's media element holds the stream that is playing. That is
    /// not the same as being on screen: audio keeps coming from the owner after the user
    /// changes tab, so the owner is the one that must pause, seek, and advance.
    /// </summary>
    private bool OwnsPlayback => _player?.Surfaces.IsOwner(this) == true;

    public void Initialize(AudioPlayerService player)
    {
        if (_initialized) return;
        _initialized = true;

        _player = player;
        _vm = new MiniPlayerViewModel(player);
        BindingContext = _vm;

        _player.Surfaces.Register(this);
        _player.PropertyChanged += OnPlayerPropertyChanged;
        _player.SeekRequested += OnSeekRequested;
    }

    /// <summary>
    /// Call from OnAppearing. The next track to start loads here. Playback already
    /// running on another tab's media element is left alone and stays controllable.
    /// </summary>
    public void Activate() => _player?.Surfaces.MarkVisible(this);

    /// <summary>
    /// Call from OnDisappearing. Does not stop audio or give up ownership of it.
    /// </summary>
    public void Deactivate() => _player?.Surfaces.MarkHidden(this);

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_player == null) return;

        if (e.PropertyName == nameof(AudioPlayerService.StreamVersion))
        {
            var version = _player.StreamVersion;
            MainThread.BeginInvokeOnMainThread(() => OnStreamChanged(version));
        }
        else if (e.PropertyName == nameof(AudioPlayerService.IsPlaying))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_player == null || OwnsPlayback == false) return;
                if (_player.IsPlaying)
                    Player.Play();
                else
                    Player.Pause();
            });
        }
    }

    /// <summary>
    /// Every control hears about a new stream. Exactly one loads it and becomes the
    /// owner; the rest release whatever they were playing, so two never sound at once.
    /// </summary>
    private async void OnStreamChanged(int version)
    {
        if (_player == null) return;

        var request = ++_sourceRequest;
        _mediaOpened = false;
        _pendingSeek = null;

        try
        {
            Player.Stop();
            Player.Source = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to release the previous stream: {ex.Message}");
        }

        if (ReferenceEquals(_player.Surfaces.ResolveLoader(version), this) == false) return;

        try
        {
            var url = await _player.GetCurrentStreamUrlAsync();
            if (request != _sourceRequest || version != _player.StreamVersion || OwnsPlayback == false) return;
            if (!string.IsNullOrEmpty(url))
            {
                // What the lock screen and the media notification show. Set before the
                // source so the first notification already carries the right track.
                var track = _player.CurrentTrack;
                Player.MetadataTitle = track?.Title ?? string.Empty;
                Player.MetadataArtist = track?.ArtistName ?? string.Empty;
                Player.MetadataArtworkUrl = _player.CurrentArtworkUrl ?? string.Empty;
                Player.Source = MediaSource.FromUri(url);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to load stream: {ex.Message}");
            if (request == _sourceRequest) _player.Pause();
        }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (OwnsPlayback && _player?.HasNext == true)
            _player.Next();
    }

    private void OnPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        var seconds = e.Position.TotalSeconds;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (OwnsPlayback) _player?.ReportPosition(seconds);
        });
    }

    // ── Seek slider ──
    // While a finger is on the slider, playback progress must not move it; the seek
    // happens once, on release, rather than on every value the drag passes through.

    private void OnSeekDragStarted(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.IsScrubbing = true;
    }

    private void OnSeekValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_vm?.IsScrubbing == true) _vm.ScrubPosition = e.NewValue;
    }

    private void OnSeekDragCompleted(object? sender, EventArgs e)
    {
        if (_vm == null) return;
        var target = SeekSlider.Value;
        _vm.IsScrubbing = false;
        _player?.SeekTo(target);
    }

    private async void OnMediaOpened(object? sender, EventArgs e)
    {
        _mediaOpened = true;
        if (OwnsPlayback) _player?.ReportDuration(Player.Duration.TotalSeconds);
        if (_pendingSeek is double position)
        {
            _pendingSeek = null;
            try { await Player.SeekTo(TimeSpan.FromSeconds(position)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
        }
        if (_player?.IsPlaying != true) Player.Pause();
    }

    private void OnSeekRequested(double positionSeconds)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (OwnsPlayback == false) return;
            if (!_mediaOpened)
            {
                _pendingSeek = positionSeconds;
                return;
            }
            try { await Player.SeekTo(TimeSpan.FromSeconds(positionSeconds)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
        });
    }

}

public partial class MiniPlayerViewModel : ObservableObject
{
    private readonly AudioPlayerService _player;

    public MiniPlayerViewModel(AudioPlayerService player)
    {
        _player = player;
        _player.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                // Position arrives several times a second; refresh only what it affects.
                case nameof(AudioPlayerService.Position):
                    if (IsScrubbing == false)
                    {
                        OnPropertyChanged(nameof(SliderPosition));
                        OnPropertyChanged(nameof(PositionText));
                    }
                    break;
                case nameof(AudioPlayerService.Duration):
                    OnPropertyChanged(nameof(HasDuration));
                    OnPropertyChanged(nameof(SliderMaximum));
                    OnPropertyChanged(nameof(DurationText));
                    break;
                default:
                    OnPropertyChanged(nameof(HasTrack));
                    OnPropertyChanged(nameof(TrackTitle));
                    OnPropertyChanged(nameof(ArtistName));
                    OnPropertyChanged(nameof(ArtworkUrl));
                    OnPropertyChanged(nameof(HasArtwork));
                    OnPropertyChanged(nameof(PlayPauseIcon));
                    break;
            }
        };
    }

    public bool HasTrack => _player.CurrentTrack != null;
    public string TrackTitle => _player.CurrentTrack?.Title ?? "";
    public string ArtistName => _player.CurrentTrack?.ArtistName ?? "";
    public string PlayPauseIcon => _player.IsPlaying ? "⏸" : "▶";
    public string? ArtworkUrl => _player.CurrentArtworkUrl;
    public bool HasArtwork => string.IsNullOrEmpty(_player.CurrentArtworkUrl) == false;

    // ── Progress ──

    public bool HasDuration => _player.Duration > 0;

    /// <summary>A slider needs a maximum above its minimum even before the length is known.</summary>
    public double SliderMaximum => _player.Duration > 0 ? _player.Duration : 1;

    public double SliderPosition => PlaybackProgress.Clamp(_player.Position, SliderMaximum);

    /// <summary>Shows where the finger is while dragging, and the playback position otherwise.</summary>
    public string PositionText => PlaybackProgress.Format(IsScrubbing ? ScrubPosition : _player.Position);

    public string DurationText => HasDuration ? PlaybackProgress.Format(_player.Duration) : "–:––";

    private bool _isScrubbing;
    public bool IsScrubbing
    {
        get => _isScrubbing;
        set
        {
            if (_isScrubbing == value) return;
            _isScrubbing = value;
            if (value) _scrubPosition = _player.Position;
            OnPropertyChanged(nameof(PositionText));
        }
    }

    private double _scrubPosition;
    public double ScrubPosition
    {
        get => _scrubPosition;
        set
        {
            _scrubPosition = value;
            OnPropertyChanged(nameof(PositionText));
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_player.IsPlaying) _player.Pause();
        else _player.Resume();
    }

    [RelayCommand]
    private void Next() => _player.Next();

    [RelayCommand]
    private void Previous() => _player.Previous();
}
