using SonaFly.Services;
using SonaFly.Views;

namespace SonaFly
{
    public partial class App : Application
    {
        private readonly ServerStorageService _storage;
        private readonly IServiceProvider _services;

        public App(ServerStorageService storage, IServiceProvider services)
        {
            InitializeComponent();
            _storage = storage;
            _services = services;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            Page startPage;
            var active = _storage.GetActive();

            if (active == null)
            {
                startPage = _services.GetRequiredService<ServerSetupPage>();
            }
            else if (string.IsNullOrEmpty(active.AccessToken))
            {
                startPage = _services.GetRequiredService<LoginPage>();
            }
            else if (active.MustChangePassword)
            {
                // The server refuses everything but the change itself, so opening the shell
                // would show a library of 403s.
                startPage = _services.GetRequiredService<SettingsPage>();
            }
            else
            {
                startPage = new AppShell();
            }

            return new Window(new NavigationPage(startPage)
            {
                BarBackgroundColor = Color.FromArgb("#0D0D1A"),
                BarTextColor = Color.FromArgb("#FFE66D")
            });
        }

        /// <summary>
        /// Called after successful login — switches MainPage to the Shell.
        /// </summary>
        public void NavigateToShell()
        {
            if (Windows.Count > 0)
            {
                Windows[0].Page = new AppShell();
            }
        }

        /// <summary>
        /// Sends the user to change a password the server will not let them keep. Nothing
        /// else works until they do, so this replaces the page rather than pushing onto it.
        /// </summary>
        public void NavigateToPasswordChange()
        {
            if (Windows.Count > 0)
            {
                Windows[0].Page = new NavigationPage(_services.GetRequiredService<SettingsPage>())
                {
                    BarBackgroundColor = Color.FromArgb("#0D0D1A"),
                    BarTextColor = Color.FromArgb("#FFE66D")
                };
            }
        }

        /// <summary>
        /// Called from server setup — pushes login page.
        /// </summary>
        public async Task NavigateToLogin()
        {
            if (Windows.Count > 0 && Windows[0].Page is NavigationPage nav)
            {
                var loginPage = _services.GetRequiredService<LoginPage>();
                await nav.PushAsync(loginPage);
            }
        }
    }
}