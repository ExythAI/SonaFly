using SonaFly.Helpers;
using SonaFly.Models;
using SonaFly.Services;
using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _vm;
    private readonly AudioPlayerService _player;
    private readonly PlaylistPickerService _playlistPicker;

    public HomePage(HomeViewModel vm, AudioPlayerService player, PlaylistPickerService playlistPicker)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _player = player;
        _playlistPicker = playlistPicker;

        AlbumsList.SelectionChanged += (_, _) => SelectionHelper.ClearAfterDelay(AlbumsList);
        AlbumTracksList.SelectionChanged += (_, _) => SelectionHelper.ClearAfterDelay(AlbumTracksList);
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
        await _vm.LoadCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MiniPlayer.Deactivate();
    }
}
