using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class HomeViewModel : LibraryViewModelBase
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;

    [ObservableProperty] private List<AlbumDto> _recentAlbums = [];
    [ObservableProperty] private AlbumDetailDto? _selectedAlbumDetail;
    [ObservableProperty] private bool _showAlbumDetail;

    public HomeViewModel(SonaFlyApiClient api, AudioPlayerService player)
    {
        _api = api;
        _player = player;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        await RunAsync("Couldn't load recent albums.", async () =>
        {
            var result = await _api.GetAlbumsAsync(1, 12);
            RecentAlbums = result?.Items?.ToList() ?? [];
        });
    }

    [RelayCommand]
    private async Task SelectAlbumAsync(AlbumDto? album)
    {
        if (album == null) return;
        await RunAsync($"Couldn't open {album.Title}.", async () =>
        {
            SelectedAlbumDetail = await _api.GetAlbumByIdAsync(album.Id.ToString());
            ShowAlbumDetail = true;
        });
    }

    [RelayCommand]
    private void GoBack()
    {
        ShowAlbumDetail = false;
        SelectedAlbumDetail = null;
    }

    [RelayCommand]
    private void PlayTrack(TrackDto? track)
    {
        if (track == null) return;
        var queue = SelectedAlbumDetail?.Tracks?.ToList() ?? [track];
        _player.Play(track, queue);
    }
}
