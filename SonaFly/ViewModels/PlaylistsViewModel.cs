using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

/// <summary>
/// The Playlists tab: the lists of playlists and mixed tapes, and the form that
/// creates a playlist. Each playlist and tape opens as a page of its own.
/// </summary>
public partial class PlaylistsViewModel : LibraryViewModelBase
{
    private readonly SonaFlyApiClient _api;

    [ObservableProperty] private List<PlaylistDto> _playlists = [];
    [ObservableProperty] private List<MixedTapeDto> _mixedTapes = [];

    [ObservableProperty] private bool _showList = true;
    [ObservableProperty] private bool _showCreateForm;

    // Create form fields
    [ObservableProperty] private string _newPlaylistName = string.Empty;
    [ObservableProperty] private string _newPlaylistDescription = string.Empty;

    public PlaylistsViewModel(SonaFlyApiClient api) => _api = api;

    private void ShowForm(bool form)
    {
        ShowCreateForm = form;
        ShowList = form == false;
    }

    /// <summary>
    /// Refreshes the lists, as on every return to this tab. A half-filled create form
    /// stays open.
    /// </summary>
    [RelayCommand]
    private Task LoadAsync() => RunAsync("Couldn't load playlists.", RefreshAsync);

    /// <summary>Fetches both lists. Throws when either request fails.</summary>
    private async Task RefreshAsync()
    {
        var playlistsTask = _api.GetPlaylistsAsync();
        var tapesTask = _api.GetMixedTapesAsync();
        await Task.WhenAll(playlistsTask, tapesTask);
        Playlists = (await playlistsTask) ?? [];
        MixedTapes = (await tapesTask) ?? [];
    }

    [RelayCommand]
    private async Task SelectPlaylistAsync(PlaylistDto? playlist)
    {
        if (playlist != null) await LibraryNavigator.OpenPlaylistAsync(playlist.Id, playlist.Name);
    }

    [RelayCommand]
    private async Task SelectMixedTapeAsync(MixedTapeDto? tape)
    {
        if (tape != null) await LibraryNavigator.OpenMixedTapeAsync(tape.Id, tape.Name);
    }

    [RelayCommand]
    private void ShowCreate()
    {
        NewPlaylistName = string.Empty;
        NewPlaylistDescription = string.Empty;
        ShowForm(true);
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
        ShowForm(false);
        await RunAsync("The playlist was created, but the list couldn't be refreshed.", RefreshAsync);
    }

    [RelayCommand]
    private void CancelCreate() => ShowForm(false);
}
