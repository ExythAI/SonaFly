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
    private bool _isActive;
    private int _sourceRequest;
    private bool _mediaOpened;
    private double? _pendingSeek;

    // Track ALL instances so we can stop old ones when a new track starts
    private static readonly List<MiniPlayerControl> _allPlayers = [];
    private static MiniPlayerControl? _activePlayer;

    public MiniPlayerControl()
    {
        InitializeComponent();
    }

    public void Initialize(AudioPlayerService player)
    {
        if (_initialized) return;
        _initialized = true;

        _player = player;
        _vm = new MiniPlayerViewModel(player);
        BindingContext = _vm;

        _player.PropertyChanged += OnPlayerPropertyChanged;
        _player.SeekRequested += OnSeekRequested;

        if (!_allPlayers.Contains(this))
            _allPlayers.Add(this);
    }

    /// <summary>
    /// Call from OnAppearing. Marks this as the active player.
    /// Does NOT restart audio — existing playback continues from the previous tab's MediaElement.
    /// </summary>
    public void Activate()
    {
        if (_activePlayer != null && _activePlayer != this)
            _activePlayer._isActive = false;

        _activePlayer = this;
        _isActive = true;
        // Don't touch the MediaElement — let audio keep playing from wherever it was
    }

    /// <summary>
    /// Call from OnDisappearing. Just marks inactive — does NOT stop audio.
    /// </summary>
    public void Deactivate()
    {
        _isActive = false;
        // Don't stop — audio continues seamlessly
    }

    /// <summary>
    /// Stop all MediaElements except the active one. Called before playing a new track.
    /// </summary>
    private static void StopAllExceptActive()
    {
        foreach (var p in _allPlayers)
        {
            if (p != _activePlayer)
            {
                try { p.Player.Stop(); p.Player.Source = null; } catch { }
            }
        }
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_isActive) return;

        if (e.PropertyName == nameof(AudioPlayerService.StreamVersion))
        {
            _mediaOpened = false;
            _pendingSeek = null;
            var request = ++_sourceRequest;
            var version = _player?.StreamVersion;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                StopAllExceptActive();
                Player.Stop();
                Player.Source = null;
                try
                {
                    var url = _player == null ? null : await _player.GetCurrentStreamUrlAsync();
                    if (request != _sourceRequest || !_isActive || version != _player?.StreamVersion) return;
                    if (!string.IsNullOrEmpty(url)) Player.Source = MediaSource.FromUri(url);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Unable to load stream: {ex.Message}");
                    if (request == _sourceRequest) _player?.Pause();
                }
            });
        }
        else if (e.PropertyName == nameof(AudioPlayerService.IsPlaying))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_player == null) return;
                if (_player.IsPlaying)
                    Player.Play();
                else
                    Player.Pause();
            });
        }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (_isActive && _player?.HasNext == true)
            _player.Next();
    }

    private async void OnMediaOpened(object? sender, EventArgs e)
    {
        _mediaOpened = true;
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
        if (!_isActive) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
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
        _player.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(TrackTitle));
            OnPropertyChanged(nameof(ArtistName));
            OnPropertyChanged(nameof(PlayPauseIcon));
        };
    }

    public bool HasTrack => _player.CurrentTrack != null;
    public string TrackTitle => _player.CurrentTrack?.Title ?? "";
    public string ArtistName => _player.CurrentTrack?.ArtistName ?? "";
    public string PlayPauseIcon => _player.IsPlaying ? "⏸" : "▶";

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
