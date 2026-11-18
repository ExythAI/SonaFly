using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

/// <summary>One album and its tracks, opened from Home, Browse or an artist.</summary>
public partial class AlbumDetailViewModel : LibraryViewModelBase, IQueryAttributable
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;
    private Guid? _albumId;

    [ObservableProperty] private string _title = "Album";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAlbum))]
    private AlbumDetailDto? _album;

    public bool HasAlbum => Album != null;

    public AlbumDetailViewModel(SonaFlyApiClient api, AudioPlayerService player)
    {
        _api = api;
        _player = player;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (LibraryNavigator.TryRead(query, out var id, out var title) == false) return;
        _albumId = id;
        if (title != null) Title = title;
    }

    /// <summary>Loads the album once. Called again on return, it retries a failed load.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_albumId is not { } id || Album != null || IsBusy) return;
        await RunAsync($"Couldn't open {Title}.", async () =>
        {
            Album = await _api.GetAlbumByIdAsync(id.ToString())
                    ?? throw new InvalidOperationException("The server sent no album.");
            Title = Album.Title;
        });
    }

    [RelayCommand]
    private void PlayTrack(TrackDto? track)
    {
        if (track == null) return;
        _player.Play(track, Album?.Tracks?.ToList() ?? [track]);
    }

    [RelayCommand]
    private void PlayAlbum()
    {
        if (Album?.Tracks?.Count > 0)
            _player.Play(Album.Tracks[0], Album.Tracks.ToList());
    }
}
