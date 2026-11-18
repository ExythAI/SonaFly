using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

/// <summary>
/// The Browse tab: every artist or every album. An artist's albums and an album's
/// tracks open as pages of their own.
/// </summary>
public partial class BrowseViewModel : LibraryViewModelBase
{
    private readonly SonaFlyApiClient _api;

    /// <summary>Filled a page at a time as the list is scrolled.</summary>
    public PagedLoader<ArtistDto> Artists { get; } = new();

    /// <summary>Filled a page at a time as the list is scrolled.</summary>
    public PagedLoader<AlbumDto> Albums { get; } = new();

    [ObservableProperty] private bool _showArtists = true;
    [ObservableProperty] private bool _showAlbums;

    public BrowseViewModel(SonaFlyApiClient api) => _api = api;

    private void ShowArtistList(bool artists)
    {
        ShowArtists = artists;
        ShowAlbums = artists == false;
    }

    [RelayCommand]
    private Task LoadArtistsAsync() => RunAsync("Couldn't load artists.", async () =>
    {
        if (await Artists.ReloadAsync((page, size) => _api.GetArtistsAsync(page, size)))
            ShowArtistList(true);
    });

    [RelayCommand]
    private Task LoadMoreArtistsAsync() =>
        RunAsync("Couldn't load more artists.", Artists.LoadMoreAsync);

    [RelayCommand]
    private Task LoadAllAlbumsAsync() => RunAsync("Couldn't load albums.", async () =>
    {
        if (await Albums.ReloadAsync((page, size) => _api.GetAlbumsAsync(page, size)))
            ShowArtistList(false);
    });

    [RelayCommand]
    private Task LoadMoreAlbumsAsync() =>
        RunAsync("Couldn't load more albums.", Albums.LoadMoreAsync);

    [RelayCommand]
    private async Task SelectArtistAsync(ArtistDto? artist)
    {
        if (artist != null) await LibraryNavigator.OpenArtistAsync(artist.Id, artist.Name);
    }

    [RelayCommand]
    private async Task SelectAlbumAsync(AlbumDto? album)
    {
        if (album != null) await LibraryNavigator.OpenAlbumAsync(album.Id, album.Title);
    }
}
