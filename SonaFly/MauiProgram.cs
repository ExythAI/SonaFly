using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection;
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
                .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: true)
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Services
            builder.Services.AddSingleton<ServerStorageService>();
            builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler
            {
                // The API never legitimately redirects. Following one silently is what
                // turned a plain-HTTP server URL into an unreadable error: a 301 to https
                // rewrites POST as GET, so the sign-in request arrived as a GET and the
                // reply was a web page. Surfacing the redirect instead lets the app say
                // something true about it.
                AllowAutoRedirect = false,
            }));
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

            var app = builder.Build();

            // Tokens live in the platform keychain, which is async to read. Load them
            // before App.CreateWindow decides which page to open. Task.Run keeps this off
            // the UI synchronization context so the blocking wait cannot deadlock.
            Task.Run(() => app.Services.GetRequiredService<ServerStorageService>().LoadAsync())
                .GetAwaiter().GetResult();

            return app;
        }
    }
}
