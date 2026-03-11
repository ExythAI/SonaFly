using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly SonaFlyApiClient _api;
    private readonly ServerStorageService _storage;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _serverName = string.Empty;

    public LoginViewModel(SonaFlyApiClient api, ServerStorageService storage)
    {
        _api = api;
        _storage = storage;
    }

    public void LoadServerInfo()
    {
        var server = _storage.GetActive();
        ServerName = server?.Name ?? "SonaFly Server";
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter username and password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var server = _storage.GetActive()
                ?? throw new InvalidOperationException("No active server.");

            var result = await _api.LoginAsync(server.BaseUrl, Username, Password);
            _storage.UpdateTokens(server.Id, result.AccessToken, result.RefreshToken, result.ExpiresUtc);
            server.Username = Username;

            Password = string.Empty;
            if (Application.Current is App app)
                app.NavigateToShell();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message.Contains("401") || ex.Message.Contains("Unauthorized")
                ? "Invalid username or password."
                : $"Connection error: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
