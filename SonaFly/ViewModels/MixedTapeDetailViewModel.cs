using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

/// <summary>One mixed tape and its tracks.</summary>
public partial class MixedTapeDetailViewModel : LibraryViewModelBase, IQueryAttributable
{
    private readonly SonaFlyApiClient _api;
    private readonly AudioPlayerService _player;
    private Guid? _tapeId;

    [ObservableProperty] private string _title = "Mixed Tape";
    [ObservableProperty] private MixedTapeDto? _mixedTape;

    public MixedTapeDetailViewModel(SonaFlyApiClient api, AudioPlayerService player)
    {
        _api = api;
        _player = player;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (LibraryNavigator.TryRead(query, out var id, out var name) == false) return;
        _tapeId = id;
        if (name != null) Title = name;
    }

    /// <summary>Loads the tape once. Called again on return, it retries a failed load.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_tapeId is not { } id || MixedTape != null || IsBusy) return;
        await RunAsync($"Couldn't open {Title}.", async () =>
        {
            MixedTape = await _api.GetMixedTapeByIdAsync(id.ToString())
                        ?? throw new InvalidOperationException("The server sent no mixed tape.");
            Title = MixedTape.Name;
        });
    }

    [RelayCommand]
    private void PlayTrack(MixedTapeItemDto? item)
    {
        if (item == null || MixedTape?.Items == null) return;
        var queue = MixedTape.Items.Select(i => new TrackDto(
            i.TrackId, i.TrackTitle, i.ArtistName, i.AlbumTitle,
            i.AlbumId, null, null, i.DurationSeconds, i.ArtworkId, null
        )).ToList();
        var track = queue.FirstOrDefault(t => t.Id == item.TrackId);
        if (track != null) _player.Play(track, queue);
    }
}
