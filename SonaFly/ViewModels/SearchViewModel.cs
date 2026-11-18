using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class SearchViewModel : LibraryViewModelBase
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private List<ArtistDto> _artists = [];
    [ObservableProperty] private List<AlbumDto> _albums = [];
    [ObservableProperty] private List<TrackDto> _tracks = [];
    [ObservableProperty] private bool _hasResults;

    public SearchViewModel(SonaFlyApiClient api, AudioPlayerService player)
    {
        _api = api;
        _player = player;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query) || Query.Length < 2) return;
        var query = Query;
        await RunAsync("Search failed.", async () =>
        {
            var result = await _api.SearchAsync(query, 20);
            Artists = result?.Artists?.ToList() ?? [];
            Albums = result?.Albums?.ToList() ?? [];
            Tracks = result?.Tracks?.ToList() ?? [];
            HasResults = Artists.Count > 0 || Albums.Count > 0 || Tracks.Count > 0;
        });
    }

    [RelayCommand]
    private void PlayTrack(TrackDto? track)
    {
        if (track == null) return;
        _player.Play(track);
    }

    public string ArtworkUrl(Guid? id) => _api.ArtworkUrl(id);
}
