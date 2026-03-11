using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class BrowseViewModel : ObservableObject
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;

    [ObservableProperty] private List<ArtistDto> _artists = [];
    [ObservableProperty] private List<AlbumDto> _albums = [];
    [ObservableProperty] private AlbumDetailDto? _selectedAlbum;
    [ObservableProperty] private bool _isBusy;

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
    private async Task LoadArtistsAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _api.GetArtistsAsync(1, 100);
            Artists = result?.Items?.ToList() ?? [];
            SetView("artists");
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SelectArtistAsync(ArtistDto? artist)
    {
        if (artist == null) return;
        IsBusy = true;
        try
        {
            var result = await _api.GetAlbumsAsync(1, 100, artist.Id);
            Albums = result?.Items?.ToList() ?? [];
            SetView("albums");
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SelectAlbumAsync(AlbumDto? album)
    {
        if (album == null) return;
        IsBusy = true;
        try
        {
            SelectedAlbum = await _api.GetAlbumByIdAsync(album.Id.ToString());
            SetView("albumDetail");
        }
        catch { }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadAllAlbumsAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _api.GetAlbumsAsync(1, 100);
            Albums = result?.Items?.ToList() ?? [];
            SetView("albums");
        }
        catch { }
        finally { IsBusy = false; }
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
