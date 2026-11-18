using System.Windows.Input;

namespace SonaFly.Helpers;

public static class SelectionHelper
{
    /// <summary>
    /// Clears a CollectionView's selection after a delay, creating a fade-out effect.
    /// </summary>
    public static async void ClearAfterDelay(CollectionView? collectionView, int delayMs = 2000)
    {
        if (collectionView == null) return;
        await Task.Delay(delayMs);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            collectionView.SelectedItem = null;
        });
    }

    /// <summary>
    /// Treats a tap on a row as "open this", for lists that lead to another page.
    /// The selection is cleared at once: a row left selected does not report a second
    /// tap, so after coming back the same item could not be opened again.
    /// </summary>
    public static void OpenOnTap(CollectionView list, ICommand command)
    {
        list.SelectionChanged += (_, e) =>
        {
            var item = e.CurrentSelection.Count > 0 ? e.CurrentSelection[0] : null;
            if (item == null) return;

            list.SelectedItem = null;
            if (command.CanExecute(item)) command.Execute(item);
        };
    }
}
