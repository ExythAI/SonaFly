using SonaFly.Helpers;
using SonaFly.Models;
using SonaFly.Services;
using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class BrowsePage : ContentPage
{
    private readonly BrowseViewModel _vm;
    private readonly AudioPlayerService _player;
    private readonly PlaylistPickerService _playlistPicker;

    public BrowsePage(BrowseViewModel vm, AudioPlayerService player, PlaylistPickerService playlistPicker)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _player = player;
        _playlistPicker = playlistPicker;

        TracksList.SelectionChanged += (_, _) => SelectionHelper.ClearAfterDelay(TracksList);
    }

    private async void OnAddToPlaylistClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is TrackDto track)
            await _playlistPicker.ShowAndAddTrackAsync(this, track);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Initialize(_player);
        MiniPlayer.Activate();
        if (_vm.Artists.Count == 0)
            await _vm.LoadArtistsCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MiniPlayer.Deactivate();
    }
}
