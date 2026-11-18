using SonaFly.Helpers;
using SonaFly.Services;
using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class BrowsePage : ContentPage
{
    private readonly BrowseViewModel _vm;
    private readonly AudioPlayerService _player;

    public BrowsePage(BrowseViewModel vm, AudioPlayerService player)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _player = player;

        SelectionHelper.OpenOnTap(ArtistsList, vm.SelectArtistCommand);
        SelectionHelper.OpenOnTap(AlbumsList, vm.SelectAlbumCommand);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Initialize(_player);
        MiniPlayer.Activate();

        // Also runs on the way back from an artist or album, where it retries a load
        // that failed but leaves a list the user was looking at alone.
        if (_vm.ShowArtists && _vm.Artists.Items.Count == 0)
            await _vm.LoadArtistsCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MiniPlayer.Deactivate();
    }
}
