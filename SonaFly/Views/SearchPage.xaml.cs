using SonaFly.Helpers;
using SonaFly.Models;
using SonaFly.Services;
using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class SearchPage : ContentPage
{
    private readonly AudioPlayerService _player;
    private readonly PlaylistPickerService _playlistPicker;

    public SearchPage(SearchViewModel vm, AudioPlayerService player, PlaylistPickerService playlistPicker)
    {
        InitializeComponent();
        BindingContext = vm;
        _player = player;
        _playlistPicker = playlistPicker;

        SearchTracksList.SelectionChanged += (_, _) => SelectionHelper.ClearAfterDelay(SearchTracksList);
    }

    private async void OnAddToPlaylistClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is TrackDto track)
            await _playlistPicker.ShowAndAddTrackAsync(this, track);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Initialize(_player);
        MiniPlayer.Activate();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MiniPlayer.Deactivate();
    }
}
