using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

/// <summary>One playlist and its tracks.</summary>
public partial class PlaylistDetailViewModel : LibraryViewModelBase, IQueryAttributable
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;
    private Guid? _playlistId;

    [ObservableProperty] private string _title = "Playlist";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaylist))]
    private PlaylistDto? _playlist;

    public bool HasPlaylist => Playlist != null;

    public PlaylistDetailViewModel(SonaFlyApiClient api, AudioPlayerService player)
    {
        _api = api;
        _player = player;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (LibraryNavigator.TryRead(query, out var id, out var name) == false) return;
        _playlistId = id;
        if (name != null) Title = name;
    }

    /// <summary>Loads the playlist once. Called again on return, it retries a failed load.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_playlistId is not { } id || Playlist != null || IsBusy) return;
        await RunAsync($"Couldn't open {Title}.", async () =>
        {
            Playlist = await _api.GetPlaylistByIdAsync(id.ToString())
                       ?? throw new InvalidOperationException("The server sent no playlist.");
            Title = Playlist.Name;
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Playlist is not { } playlist) return;

        var deleted = await RunAsync($"Couldn't delete {playlist.Name}.", () =>
            _api.DeletePlaylistAsync(playlist.Id));

        // The list underneath reloads when it comes back on screen.
        if (deleted) await LibraryNavigator.CloseAsync();
    }

    [RelayCommand]
    private void PlayTrack(PlaylistItemDto? item)
    {
        if (item == null || Playlist?.Items == null) return;
        var queue = Playlist.Items.Select(i => new TrackDto(
            i.TrackId, i.TrackTitle, i.ArtistName, i.AlbumTitle,
            null, null, null, i.DurationSeconds, null, null
        )).ToList();
        var track = queue.FirstOrDefault(t => t.Id == item.TrackId);
        if (track != null) _player.Play(track, queue);
    }
}
