using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

/// <summary>One artist's albums, a page at a time.</summary>
public partial class ArtistAlbumsViewModel : LibraryViewModelBase, IQueryAttributable
{
    private readonly SonaFlyApiClient _api;
    private Guid? _artistId;
    private bool _loaded;

    [ObservableProperty] private string _title = "Artist";

    public PagedLoader<AlbumDto> Albums { get; } = new();

    public ArtistAlbumsViewModel(SonaFlyApiClient api) => _api = api;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (LibraryNavigator.TryRead(query, out var id, out var name) == false) return;
        _artistId = id;
        if (name != null) Title = name;
    }

    /// <summary>Loads the first page once. Called again on return, it retries a failed load.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_artistId is not { } id || _loaded || IsBusy) return;
        _loaded = await RunAsync($"Couldn't load albums by {Title}.", () =>
            Albums.ReloadAsync((page, size) => _api.GetAlbumsAsync(page, size, id)));
    }

    [RelayCommand]
    private Task LoadMoreAsync() => RunAsync("Couldn't load more albums.", Albums.LoadMoreAsync);

    [RelayCommand]
    private async Task OpenAlbumAsync(AlbumDto? album)
    {
        if (album != null) await LibraryNavigator.OpenAlbumAsync(album.Id, album.Title);
    }
}
