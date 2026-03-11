using SonaFly.Helpers;
using SonaFly.Services;
using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class PlaylistsPage : ContentPage
{
    private readonly PlaylistsViewModel _vm;
    private readonly AudioPlayerService _player;

    public PlaylistsPage(PlaylistsViewModel vm, AudioPlayerService player)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _player = player;

        PlaylistTracksList.SelectionChanged += (_, _) => SelectionHelper.ClearAfterDelay(PlaylistTracksList);
        MixedTapeTracksList.SelectionChanged += (_, _) => SelectionHelper.ClearAfterDelay(MixedTapeTracksList);
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
