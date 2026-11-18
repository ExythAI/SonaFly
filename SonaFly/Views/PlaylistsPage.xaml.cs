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

        SelectionHelper.OpenOnTap(PlaylistsList, vm.SelectPlaylistCommand);
        SelectionHelper.OpenOnTap(MixedTapesList, vm.SelectMixedTapeCommand);
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

    /// <summary>
    /// The create form is part of this page, so the system back button closes the form
    /// rather than leaving the tab.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (_vm.ShowCreateForm)
        {
            _vm.CancelCreateCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
