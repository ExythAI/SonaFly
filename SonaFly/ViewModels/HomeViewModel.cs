using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class HomeViewModel : LibraryViewModelBase
{
    private readonly SonaFlyApiClient _api;

    [ObservableProperty] private List<AlbumDto> _recentAlbums = [];

    public HomeViewModel(SonaFlyApiClient api) => _api = api;

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
        if (album != null) await LibraryNavigator.OpenAlbumAsync(album.Id, album.Title);
    }
}
