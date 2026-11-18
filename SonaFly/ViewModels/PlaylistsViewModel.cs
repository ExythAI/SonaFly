using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class PlaylistsViewModel : LibraryViewModelBase
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;

    [ObservableProperty] private List<PlaylistDto> _playlists = [];
    [ObservableProperty] private List<MixedTapeDto> _mixedTapes = [];
    [ObservableProperty] private PlaylistDto? _selectedPlaylist;
    [ObservableProperty] private MixedTapeDto? _selectedMixedTape;

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
    private Task LoadAsync() => RunAsync("Couldn't load playlists.", RefreshAsync);

    /// <summary>Fetches both lists and shows them. Throws when either request fails.</summary>
    private async Task RefreshAsync()
    {
        var playlistsTask = _api.GetPlaylistsAsync();
        var tapesTask = _api.GetMixedTapesAsync();
        await Task.WhenAll(playlistsTask, tapesTask);
        Playlists = (await playlistsTask) ?? [];
        MixedTapes = (await tapesTask) ?? [];
        SetView("list");
    }

    [RelayCommand]
    private async Task SelectPlaylistAsync(PlaylistDto? playlist)
    {
        if (playlist == null) return;
        await RunAsync($"Couldn't open {playlist.Name}.", async () =>
        {
            SelectedPlaylist = await _api.GetPlaylistByIdAsync(playlist.Id.ToString());
            SetView("playlistDetail");
        });
    }

    [RelayCommand]
    private async Task SelectMixedTapeAsync(MixedTapeDto? tape)
    {
        if (tape == null) return;
        await RunAsync($"Couldn't open {tape.Name}.", async () =>
        {
            SelectedMixedTape = await _api.GetMixedTapeByIdAsync(tape.Id.ToString());
            SetView("mixedTapeDetail");
        });
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

        // The typed name is kept if this fails, so it can be tried again.
        var created = await RunAsync("Couldn't create the playlist.", () =>
            _api.CreatePlaylistAsync(NewPlaylistName.Trim(), NewPlaylistDescription.Trim()));
        if (created == false) return;

        NewPlaylistName = string.Empty;
        NewPlaylistDescription = string.Empty;
        await RunAsync("The playlist was created, but the list couldn't be refreshed.", RefreshAsync);
    }

    [RelayCommand]
    private async Task DeletePlaylistAsync()
    {
        if (SelectedPlaylist is not { } playlist) return;

        var deleted = await RunAsync($"Couldn't delete {playlist.Name}.", () =>
            _api.DeletePlaylistAsync(playlist.Id));
        if (deleted == false) return;

        await RunAsync("The playlist was deleted, but the list couldn't be refreshed.", RefreshAsync);
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
