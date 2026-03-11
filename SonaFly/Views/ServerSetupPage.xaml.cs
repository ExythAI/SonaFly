using SonaFly.ViewModels;

namespace SonaFly.Views;

public partial class ServerSetupPage : ContentPage
{
    public ServerSetupPage(ServerSetupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
