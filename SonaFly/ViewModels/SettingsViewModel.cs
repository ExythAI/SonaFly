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

    public SettingsViewModel(SonaFlyApiClient api, ServerStorageService storage)
    {
        _api = api;
        _storage = storage;
        LoadServers();
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
            _storage.ClearTokens(active.Id);

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

        _storage.Remove(server.Id);
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
            var success = await _api.ChangePasswordAsync(CurrentPassword, NewPassword);
            if (success)
            {
                StatusMessage = "Password changed successfully!";
                IsSuccess = true;
                CurrentPassword = "";
                NewPassword = "";
                ConfirmPassword = "";
            }
            else
            {
                StatusMessage = "Failed — check your current password.";
                IsSuccess = false;
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
