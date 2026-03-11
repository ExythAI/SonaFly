using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Services;

namespace SonaFly.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SonaFlyApiClient _api;

    public SettingsViewModel(SonaFlyApiClient api) => _api = api;

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
