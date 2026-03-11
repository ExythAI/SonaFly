using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class AuditoriumViewModel : ObservableObject
{
    private readonly SonaFlyApiClient _api;
    private readonly AuditoriumService _auditorium;
    private readonly AudioPlayerService _player;

    public AuditoriumViewModel(SonaFlyApiClient api, AuditoriumService auditorium, AudioPlayerService player)
    {
        _api = api;
        _auditorium = auditorium;
        _player = player;

        _auditorium.TrackStarted += OnTrackStarted;
        _auditorium.TrackEnded += OnTrackEnded;
        _auditorium.QueueUpdated += OnQueueUpdated;
        _auditorium.UserJoined += OnPresenceChanged;
        _auditorium.UserLeft += OnPresenceChanged;
        _auditorium.ErrorOccurred += msg => StatusMessage = $"Error: {msg}";

        // Auto-leave auditorium if user plays a personal track
        _player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AudioPlayerService.CurrentTrack) && IsInRoom)
            {
                if (_auditoriumInitiatedPlay)
                {
                    _auditoriumInitiatedPlay = false;
                    return;
                }
                // User played something outside the auditorium — auto-leave
                MainThread.BeginInvokeOnMainThread(async () => await LeaveRoomAsync(stopAudio: false));
            }
        };
    }

    private bool _auditoriumInitiatedPlay;

    // ── State ──
    [ObservableProperty] ObservableCollection<AuditoriumDto> auditoriums = [];
    [ObservableProperty] ObservableCollection<QueueItemDto> queue = [];
    [ObservableProperty] ObservableCollection<ActiveUserDto> activeUsers = [];

    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool isInRoom;
    [ObservableProperty] string? roomName;
    [ObservableProperty] string? statusMessage;

    // Now playing
    [ObservableProperty] string? nowPlayingTitle;
    [ObservableProperty] string? nowPlayingArtist;
    [ObservableProperty] Guid? nowPlayingArtworkId;
    [ObservableProperty] double nowPlayingDuration;
    [ObservableProperty] double nowPlayingPosition;
    [ObservableProperty] bool isPlaying;
    [ObservableProperty] string? startedByUserName;
    [ObservableProperty] Guid? startedByUserId;
    [ObservableProperty] bool canStopSkip;
    [ObservableProperty] int activeUserCount;

    // Track search for queuing
    [ObservableProperty] string? searchQuery;
    [ObservableProperty] ObservableCollection<TrackDto> searchResults = [];
    [ObservableProperty] bool showSearch;

    // Browse albums for queuing
    [ObservableProperty] ObservableCollection<AlbumDto> browseAlbums = [];
    [ObservableProperty] ObservableCollection<TrackDto> browseTracks = [];
    [ObservableProperty] AlbumDetailDto? selectedBrowseAlbum;
    [ObservableProperty] bool showBrowse;
    [ObservableProperty] bool showBrowseTracks;

    private IDispatcherTimer? _positionTimer;

    [RelayCommand]
    async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var list = await _api.GetAuditoriumsAsync();
            Auditoriums = new(list);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    async Task JoinRoomAsync(AuditoriumDto room)
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            // Stop any personal audio
            _player.Stop();

            var state = await _auditorium.JoinAsync(room.Id);
            if (state == null) { StatusMessage = "Failed to join."; return; }

            IsInRoom = true;
            RoomName = room.Name;
            ApplyState(state);
            StartPositionTimer();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    async Task LeaveRoomCommandAsync() => await LeaveRoomAsync(stopAudio: true);

    async Task LeaveRoomAsync(bool stopAudio = true)
    {
        if (!IsInRoom) return;
        StopPositionTimer();
        await _auditorium.LeaveAsync();
        _lastPlayingTrackId = null;
        IsInRoom = false;
        RoomName = null;
        IsPlaying = false;
        NowPlayingTitle = null;
        Queue.Clear();
        ActiveUsers.Clear();
        StatusMessage = null;
        ActiveUserCount = 0;
        ShowBrowse = false;
        ShowBrowseTracks = false;
        ShowSearch = false;
        if (stopAudio) _player.Stop();
    }

    [RelayCommand]
    async Task StopAsync() => await _auditorium.StopTrackAsync();

    [RelayCommand]
    async Task SkipAsync() => await _auditorium.SkipTrackAsync();

    [RelayCommand]
    void ToggleSearch()
    {
        ShowSearch = !ShowSearch;
        if (ShowSearch) { ShowBrowse = false; ShowBrowseTracks = false; }
    }

    [RelayCommand]
    async Task ToggleBrowseAsync()
    {
        ShowBrowse = !ShowBrowse;
        if (ShowBrowse)
        {
            ShowSearch = false;
            ShowBrowseTracks = false;
            if (BrowseAlbums.Count == 0)
            {
                try
                {
                    var result = await _api.GetAlbumsAsync(1, 100);
                    if (result != null)
                        BrowseAlbums = new(result.Items);
                }
                catch (Exception ex) { StatusMessage = ex.Message; }
            }
        }
    }

    [RelayCommand]
    async Task SelectBrowseAlbumAsync(AlbumDto album)
    {
        if (album == null) return;
        try
        {
            var detail = await _api.GetAlbumByIdAsync(album.Id.ToString());
            if (detail != null)
            {
                SelectedBrowseAlbum = detail;
                BrowseTracks = new(detail.Tracks);
                ShowBrowseTracks = true;
            }
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    void BackToAlbums()
    {
        ShowBrowseTracks = false;
        SelectedBrowseAlbum = null;
        BrowseTracks.Clear();
    }

    [RelayCommand]
    async Task QueueBrowseTrackAsync(TrackDto track)
    {
        if (track == null) return;
        await _auditorium.QueueTrackAsync(track.Id);
    }

    [RelayCommand]
    async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        try
        {
            var results = await _api.SearchAsync(SearchQuery, 20);
            SearchResults = new(results.Tracks);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    async Task QueueTrackAsync(TrackDto track)
    {
        await _auditorium.QueueTrackAsync(track.Id);
    }

    [RelayCommand]
    async Task RemoveQueueItemAsync(QueueItemDto item)
    {
        await _auditorium.RemoveFromQueueAsync(item.Id);
    }

    // ── Event handlers ──

    private void OnTrackStarted(AuditoriumStateDto state) => ApplyState(state);
    private void OnTrackEnded(AuditoriumStateDto state) => ApplyState(state);
    private void OnQueueUpdated(List<QueueItemDto> q) => Queue = new(q);
    private void OnPresenceChanged(string _, List<ActiveUserDto> users)
    {
        ActiveUsers = new(users);
        ActiveUserCount = users.Count;
    }

    private void ApplyState(AuditoriumStateDto state)
    {
        var wasPlaying = IsPlaying;
        var previousTrackId = _lastPlayingTrackId;

        IsPlaying = state.CurrentTrackId != null && !state.IsPaused;
        NowPlayingTitle = state.CurrentTrackTitle;
        NowPlayingArtist = state.CurrentArtistName;
        NowPlayingArtworkId = state.CurrentArtworkId;
        NowPlayingDuration = state.CurrentTrackDuration ?? 0;
        NowPlayingPosition = state.CurrentPositionSeconds;
        StartedByUserId = state.StartedByUserId;
        StartedByUserName = state.StartedByUserName;
        Queue = new(state.Queue);
        ActiveUsers = new(state.ActiveUsers);
        ActiveUserCount = state.ActiveUsers.Count;
        CanStopSkip = IsPlaying;

        // Actually play audio!
        if (state.CurrentTrackId != null && IsPlaying && state.CurrentTrackId != previousTrackId)
        {
            _lastPlayingTrackId = state.CurrentTrackId;
            _auditoriumInitiatedPlay = true;
            var track = new TrackDto(
                state.CurrentTrackId.Value,
                state.CurrentTrackTitle ?? "Unknown",
                state.CurrentArtistName, null, null,
                null, null, state.CurrentTrackDuration,
                state.CurrentArtworkId, null
            );
            _player.Play(track);

            // Seek to the current server position for mid-song sync
            if (state.CurrentPositionSeconds > 1)
                _player.SeekTo(state.CurrentPositionSeconds);
        }
        else if (state.CurrentTrackId == null && wasPlaying)
        {
            _lastPlayingTrackId = null;
            _player.Stop();
        }
    }

    private Guid? _lastPlayingTrackId;

    private void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_positionTimer == null) return;
        _positionTimer.Interval = TimeSpan.FromSeconds(1);
        _positionTimer.Tick += (_, _) =>
        {
            if (IsPlaying && NowPlayingDuration > 0)
            {
                NowPlayingPosition += 1;
                if (NowPlayingPosition > NowPlayingDuration)
                    NowPlayingPosition = NowPlayingDuration;
            }
        };
        _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = null;
    }
}
