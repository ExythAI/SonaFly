using SonaFly.Helpers;
using SonaFly.Services;
using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class MixedTapeDetailPage : ContentPage
{
    private readonly MixedTapeDetailViewModel _vm;
    private readonly AudioPlayerService _player;

    public MixedTapeDetailPage(MixedTapeDetailViewModel vm, AudioPlayerService player)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _player = player;

        TracksList.SelectionChanged += (_, _) => SelectionHelper.ClearAfterDelay(TracksList);
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
