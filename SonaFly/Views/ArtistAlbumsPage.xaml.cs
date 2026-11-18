using SonaFly.Helpers;
using SonaFly.Services;
using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class ArtistAlbumsPage : ContentPage
{
    private readonly ArtistAlbumsViewModel _vm;
    private readonly AudioPlayerService _player;

    public ArtistAlbumsPage(ArtistAlbumsViewModel vm, AudioPlayerService player)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _player = player;

        SelectionHelper.OpenOnTap(AlbumsList, vm.OpenAlbumCommand);
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
