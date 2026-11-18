using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class BrowseViewModel : LibraryViewModelBase
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;

    /// <summary>Filled a page at a time as the list is scrolled.</summary>
    public PagedLoader<ArtistDto> Artists { get; } = new();

    /// <summary>All albums, or one artist's, a page at a time.</summary>
    public PagedLoader<AlbumDto> Albums { get; } = new();

    [ObservableProperty] private AlbumDetailDto? _selectedAlbum;

    [ObservableProperty] private bool _showArtists = true;
    [ObservableProperty] private bool _showAlbums;
    [ObservableProperty] private bool _showAlbumDetail;
    [ObservableProperty] private bool _canGoBack;

    public BrowseViewModel(SonaFlyApiClient api, AudioPlayerService player)
    {
        _api = api;
        _player = player;
    }

    private void SetView(string mode)
    {
        ShowArtists = mode == "artists";
        ShowAlbums = mode == "albums";
        ShowAlbumDetail = mode == "albumDetail";
        CanGoBack = mode != "artists";
    }

    [RelayCommand]
    private Task LoadArtistsAsync() => RunAsync("Couldn't load artists.", async () =>
    {
        if (await Artists.ReloadAsync((page, size) => _api.GetArtistsAsync(page, size)))
            SetView("artists");
    });

    [RelayCommand]
    private Task LoadMoreArtistsAsync() =>
        RunAsync("Couldn't load more artists.", Artists.LoadMoreAsync);

    [RelayCommand]
    private async Task SelectArtistAsync(ArtistDto? artist)
    {
        if (artist == null) return;
        await RunAsync($"Couldn't load albums by {artist.Name}.", async () =>
        {
            if (await Albums.ReloadAsync((page, size) => _api.GetAlbumsAsync(page, size, artist.Id)))
                SetView("albums");
        });
    }

    [RelayCommand]
    private Task LoadAllAlbumsAsync() => RunAsync("Couldn't load albums.", async () =>
    {
        if (await Albums.ReloadAsync((page, size) => _api.GetAlbumsAsync(page, size)))
            SetView("albums");
    });

    [RelayCommand]
    private Task LoadMoreAlbumsAsync() =>
        RunAsync("Couldn't load more albums.", Albums.LoadMoreAsync);

    [RelayCommand]
    private async Task SelectAlbumAsync(AlbumDto? album)
    {
        if (album == null) return;
        await RunAsync($"Couldn't open {album.Title}.", async () =>
        {
            SelectedAlbum = await _api.GetAlbumByIdAsync(album.Id.ToString());
            SetView("albumDetail");
        });
    }

    [RelayCommand]
    private void PlayTrack(TrackDto? track)
    {
        if (track == null) return;
        var queue = SelectedAlbum?.Tracks?.ToList() ?? [track];
        _player.Play(track, queue);
    }

    [RelayCommand]
    private void PlayAlbum()
    {
        if (SelectedAlbum?.Tracks?.Count > 0)
            _player.Play(SelectedAlbum.Tracks[0], SelectedAlbum.Tracks.ToList());
    }

    [RelayCommand]
    private void GoBack()
    {
        if (ShowAlbumDetail) SetView("albums");
        else if (ShowAlbums) SetView("artists");
    }

    public string ArtworkUrl(Guid? id) => _api.ArtworkUrl(id);
}
