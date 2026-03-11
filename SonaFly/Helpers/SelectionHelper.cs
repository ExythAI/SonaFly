namespace SonaFly.Helpers;

/// <summary>
/// Clears a CollectionView's selection after a delay, creating a fade-out effect.
/// </summary>
public static class SelectionHelper
{
    public static async void ClearAfterDelay(CollectionView? collectionView, int delayMs = 2000)
    {
        if (collectionView == null) return;
        await Task.Delay(delayMs);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            collectionView.SelectedItem = null;
        });
    }
}
