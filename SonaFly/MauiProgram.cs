using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using SonaFly.Services;
using SonaFly.ViewModels;
using SonaFly.Views;

namespace SonaFly
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Services
            builder.Services.AddSingleton<ServerStorageService>();
            builder.Services.AddSingleton<HttpClient>();
            builder.Services.AddSingleton<SonaFlyApiClient>();
            builder.Services.AddSingleton<AudioPlayerService>();
            builder.Services.AddSingleton<PlaylistPickerService>();
            builder.Services.AddSingleton<AuditoriumService>();

            // ViewModels
            builder.Services.AddTransient<ServerSetupViewModel>();
            builder.Services.AddTransient<LoginViewModel>();
            builder.Services.AddTransient<HomeViewModel>();
            builder.Services.AddTransient<BrowseViewModel>();
            builder.Services.AddTransient<PlaylistsViewModel>();
            builder.Services.AddTransient<SearchViewModel>();
            builder.Services.AddTransient<AuditoriumViewModel>();
            builder.Services.AddTransient<SettingsViewModel>();

            // Pages
            builder.Services.AddTransient<ServerSetupPage>();
            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<HomePage>();
            builder.Services.AddTransient<BrowsePage>();
            builder.Services.AddTransient<PlaylistsPage>();
            builder.Services.AddTransient<SearchPage>();
            builder.Services.AddTransient<AuditoriumPage>();
            builder.Services.AddTransient<SettingsPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
