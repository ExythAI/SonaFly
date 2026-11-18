using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonaFly.Services;

namespace SonaFly.ViewModels;

/// <summary>
/// Shared by the library screens: a busy flag and an error message the page shows in a
/// banner, so a failed request is reported instead of leaving an empty screen.
/// </summary>
public abstract partial class LibraryViewModelBase : ObservableObject
{
    private int _running;

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => string.IsNullOrEmpty(ErrorMessage) == false;

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    /// <summary>
    /// Runs <paramref name="work"/> with the busy flag up. A failure becomes the error
    /// message, starting with <paramref name="whatFailed"/>; a success clears the
    /// previous one. Returns whether the work succeeded.
    /// </summary>
    protected async Task<bool> RunAsync(string whatFailed, Func<Task> work)
    {
        _running++;
        IsBusy = true;
        try
        {
            await work();
            ErrorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{whatFailed} {ex}");
            ErrorMessage = ApiErrorMessage.Describe(whatFailed, ex);
            return false;
        }
        finally
        {
            // Overlapping operations: stay busy until the last one finishes.
            if (--_running == 0) IsBusy = false;
        }
    }
}
