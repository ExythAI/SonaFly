using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Models;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SonaFlyApiClient _api;
    private readonly ServerStorageService _storage;
    private readonly AudioPlayerService _player;
    private readonly AuditoriumService _auditorium;

    public SettingsViewModel(
        SonaFlyApiClient api,
        ServerStorageService storage,
        AudioPlayerService player,
        AuditoriumService auditorium)
    {
        _api = api;
        _storage = storage;
        _player = player;
        _auditorium = auditorium;
        LoadServers();

        if (_storage.GetActive()?.MustChangePassword == true)
        {
            // This page is where such an account lands; say why, rather than leaving them
            // to discover that nothing else works.
            StatusMessage = "This account is still using a temporary password. Choose a new one to continue.";
        }
    }

    // ── Server management ──
    [ObservableProperty] ObservableCollection<ServerConfig> servers = [];
    [ObservableProperty] ServerConfig? activeServer;

    private void LoadServers()
    {
        Servers = new(_storage.GetAll());
        ActiveServer = _storage.GetActive();
    }

    [RelayCommand]
    async Task LogoutAsync()
    {
        var active = _storage.GetActive();
        if (active != null)
        {
            // Stop using the session before ending it: audio is streaming with a ticket
            // minted from these credentials, and the hub connection is authenticated by them.
            _player.Stop();
            await _auditorium.LeaveAsync();

            // Tell the server first, while the refresh token is still at hand. Clearing it
            // locally only hides the credential; the refresh family stays usable for a week
            // to anyone holding a copy.
            var endedOnServer = await _api.LogoutAsync();
            var cleared = await _storage.ClearTokensAsync(active.Id);

            if (!cleared)
            {
                StatusMessage = endedOnServer
                    ? "Signed out. The saved credential could not be erased from this device, but the server has ended the session."
                    : "Signed out on this device only. The saved credential could not be erased and the server was not reachable.";
                IsSuccess = false;
            }
            else if (!endedOnServer)
            {
                StatusMessage = "Signed out on this device. The server could not be reached, so the session may stay open until it expires.";
                IsSuccess = false;
            }
        }

        // Navigate back to login
        if (Application.Current is App app)
        {
            var loginPage = Application.Current.Windows[0].Page?.Handler?.MauiContext?
                .Services.GetRequiredService<Views.LoginPage>();
            if (loginPage != null)
                Application.Current.Windows[0].Page = new NavigationPage(loginPage)
                {
                    BarBackgroundColor = Color.FromArgb("#0D0D1A"),
                    BarTextColor = Color.FromArgb("#FFE66D")
                };
        }
    }

    [RelayCommand]
    async Task SwitchServerAsync()
    {
        // Navigate to server setup
        if (Application.Current?.Windows.Count > 0)
        {
            var setupPage = Application.Current.Windows[0].Page?.Handler?.MauiContext?
                .Services.GetRequiredService<Views.ServerSetupPage>();
            if (setupPage != null)
                Application.Current.Windows[0].Page = new NavigationPage(setupPage)
                {
                    BarBackgroundColor = Color.FromArgb("#0D0D1A"),
                    BarTextColor = Color.FromArgb("#FFE66D")
                };
        }
    }

    [RelayCommand]
    async Task RemoveServerAsync(ServerConfig server)
    {
        if (server == null) return;

        bool confirm = await Application.Current!.Windows[0].Page!
            .DisplayAlert("Remove Server", $"Remove \"{server.Name}\"?", "Remove", "Cancel");
        if (!confirm) return;

        await _storage.RemoveAsync(server.Id);
        LoadServers();

        // If no servers left, go to server setup
        if (Servers.Count == 0)
            await SwitchServerAsync();
    }

    [RelayCommand]
    async Task SetActiveAsync(ServerConfig server)
    {
        if (server == null || server.IsActive) return;
        _storage.SetActive(server.Id);
        LoadServers();

        // Re-login needed for the new server
        await LogoutAsync();
    }

    // ── Password change ──
    [ObservableProperty] string currentPassword = "";
    [ObservableProperty] string newPassword = "";
    [ObservableProperty] string confirmPassword = "";
    [ObservableProperty] string? statusMessage;
    [ObservableProperty] bool isSuccess;
    [ObservableProperty] bool isBusy;

    [RelayCommand]
    async Task ChangePasswordAsync()
    {
        StatusMessage = null;

        if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(NewPassword))
        {
            StatusMessage = "Please fill in all fields.";
            IsSuccess = false;
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            StatusMessage = "New passwords do not match.";
            IsSuccess = false;
            return;
        }

        if (NewPassword.Length < 6)
        {
            StatusMessage = "Password must be at least 6 characters.";
            IsSuccess = false;
            return;
        }

        IsBusy = true;
        try
        {
            var wasTemporary = _storage.GetActive()?.MustChangePassword == true;
            var result = await _api.ChangePasswordAsync(CurrentPassword, NewPassword);

            CurrentPassword = "";
            NewPassword = "";
            ConfirmPassword = "";

            switch (result)
            {
                case ChangePasswordResult.Changed:
                    StatusMessage = "Password changed successfully!";
                    IsSuccess = true;
                    // The account is no longer held at this page; let it into the app.
                    if (wasTemporary && Application.Current is App app) app.NavigateToShell();
                    break;

                case ChangePasswordResult.ChangedButSignedOut:
                    // The password is the new one, but this device has nothing usable left.
                    StatusMessage = "Password changed. Sign in again with the new password.";
                    IsSuccess = true;
                    await LogoutAsync();
                    break;

                default:
                    StatusMessage = "Failed — check your current password.";
                    IsSuccess = false;
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            IsSuccess = false;
        }
        finally { IsBusy = false; }
    }
}
