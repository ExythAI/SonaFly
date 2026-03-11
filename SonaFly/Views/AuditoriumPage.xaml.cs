using SonaFly.Services;
using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class AuditoriumPage : ContentPage
{
    private readonly AuditoriumViewModel _vm;
    private readonly AudioPlayerService _player;

    public AuditoriumPage(AuditoriumViewModel vm, AudioPlayerService player)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _player = player;
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
