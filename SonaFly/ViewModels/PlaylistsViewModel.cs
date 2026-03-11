using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class PlaylistsViewModel : ObservableObject
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;

    [ObservableProperty] private List<PlaylistDto> _playlists = [];
    [ObservableProperty] private List<MixedTapeDto> _mixedTapes = [];
    [ObservableProperty] private PlaylistDto? _selectedPlaylist;
    [ObservableProperty] private MixedTapeDto? _selectedMixedTape;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _showList = true;
    [ObservableProperty] private bool _showPlaylistDetail;
    [ObservableProperty] private bool _showMixedTapeDetail;
    [ObservableProperty] private bool _showCreateForm;

    // Create form fields
    [ObservableProperty] private string _newPlaylistName = string.Empty;
    [ObservableProperty] private string _newPlaylistDescription = string.Empty;

    public PlaylistsViewModel(SonaFlyApiClient api, AudioPlayerService player)
    {
        _api = api;
        _player = player;
    }

    private void SetView(string mode)
    {
        ShowList = mode == "list";
        ShowPlaylistDetail = mode == "playlistDetail";
        ShowMixedTapeDetail = mode == "mixedTapeDetail";
        ShowCreateForm = mode == "create";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var playlistsTask = _api.GetPlaylistsAsync();
            var tapesTask = _api.GetMixedTapesAsync();
            Playlists = (await playlistsTask) ?? [];
            MixedTapes = (await tapesTask) ?? [];
            SetView("list");
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SelectPlaylistAsync(PlaylistDto? playlist)
    {
        if (playlist == null) return;
        IsBusy = true;
        try
        {
            SelectedPlaylist = await _api.GetPlaylistByIdAsync(playlist.Id.ToString());
            SetView("playlistDetail");
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SelectMixedTapeAsync(MixedTapeDto? tape)
    {
        if (tape == null) return;
        IsBusy = true;
        try
        {
            SelectedMixedTape = await _api.GetMixedTapeByIdAsync(tape.Id.ToString());
            SetView("mixedTapeDetail");
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ShowCreate()
    {
        NewPlaylistName = string.Empty;
        NewPlaylistDescription = string.Empty;
        SetView("create");
    }

    [RelayCommand]
    private async Task CreatePlaylistAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistName)) return;
        IsBusy = true;
        try
        {
            await _api.CreatePlaylistAsync(NewPlaylistName.Trim(), NewPlaylistDescription.Trim());
            NewPlaylistName = string.Empty;
            NewPlaylistDescription = string.Empty;
            await LoadAsync(); // refresh list
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeletePlaylistAsync()
    {
        if (SelectedPlaylist == null) return;
        IsBusy = true;
        try
        {
            await _api.DeletePlaylistAsync(SelectedPlaylist.Id);
            await LoadAsync();
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void PlayPlaylistTrack(PlaylistItemDto? item)
    {
        if (item == null || SelectedPlaylist?.Items == null) return;
        var queue = SelectedPlaylist.Items.Select(i => new TrackDto(
            i.TrackId, i.TrackTitle, i.ArtistName, i.AlbumTitle,
            null, null, null, i.DurationSeconds, null, null
        )).ToList();
        var track = queue.FirstOrDefault(t => t.Id == item.TrackId);
        if (track != null) _player.Play(track, queue);
    }

    [RelayCommand]
    private void PlayMixedTapeTrack(MixedTapeItemDto? item)
    {
        if (item == null || SelectedMixedTape?.Items == null) return;
        var queue = SelectedMixedTape.Items.Select(i => new TrackDto(
            i.TrackId, i.TrackTitle, i.ArtistName, i.AlbumTitle,
            i.AlbumId, null, null, i.DurationSeconds, i.ArtworkId, null
        )).ToList();
        var track = queue.FirstOrDefault(t => t.Id == item.TrackId);
        if (track != null) _player.Play(track, queue);
    }

    [RelayCommand]
    private void GoBack() => SetView("list");

    [RelayCommand]
    private void CancelCreate() => SetView("list");
}
