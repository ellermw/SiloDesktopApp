using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SiloPlayer.Helpers;

/// <summary>
/// Keeps a tall confirmation's body reachable on a short window (official #5070: an alert dialog taller than the screen scrolls, so its
/// actions stay reachable). The shared ContentDialog template keeps its command buttons outside the scrolled region, but its content
/// scroller is not allowed to scroll, so a body taller than the window was only clipped and its end could not be read. This turns the
/// scroller on for a dialog whose content does not scroll itself, and makes the scroller a keyboard stop while it has something to
/// scroll to, so the arrow and page keys reach the end of the body. A dialog whose content has its own scroller or star-sized layout
/// (the editor and picker dialogs) keeps the template's original behaviour.
/// </summary>
public static class DialogBodyScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DialogBodyScroll), new PropertyMetadata(false, EnabledChanged));

    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ContentDialog dialog) return;
        dialog.Opened -= OnOpened;
        if ((bool)args.NewValue) dialog.Opened += OnOpened;
    }

    /// <summary>
    /// Follows one showing of a dialog. LayoutUpdated is raised for every layout pass in the window, so the handler lives only while the
    /// dialog is open: it is removed when the dialog closes, and a dialog shown again gets a new one.
    /// </summary>
    private static void OnOpened(ContentDialog dialog, ContentDialogOpenedEventArgs args)
    {
        ScrollViewer? scroller = null; var settled = false;
        void Follow(object? sender, object e)
        {
            if (!settled)
            {
                // The template parts can still be arriving when Opened is raised: look again on each layout pass until the scroller is there.
                if (!TryFindBody(dialog, out scroller)) return;
                settled = true;
                if (scroller is null) { dialog.LayoutUpdated -= Follow; return; }
                scroller.UseSystemFocusVisuals = true;
            }
            Reach(scroller!);
        }
        void Stop(ContentDialog sender, ContentDialogClosedEventArgs e) { dialog.LayoutUpdated -= Follow; dialog.Closed -= Stop; }
        dialog.LayoutUpdated += Follow; dialog.Closed += Stop;
        Follow(null, new object());
    }

    /// <summary>
    /// True once the open dialog's template has its content scroller. <paramref name="scroller"/> is that scroller when the body should
    /// scroll in it, or null when the dialog's content scrolls itself and the template is left as it is.
    /// </summary>
    internal static bool TryFindBody(ContentDialog dialog, out ScrollViewer? scroller)
    {
        scroller = EditorDialogPresentation.Descendants<ScrollViewer>(dialog).FirstOrDefault(item => item.Name == "ContentScrollViewer");
        if (scroller is null) return false;
        // A TextBox carries its own inner scroller ("ContentElement"); only a scroller of the content's own counts.
        var ownScroll = dialog.Content is DependencyObject content
            && EditorDialogPresentation.Descendants<ScrollViewer>(content).Any(item => item.Name != "ContentElement");
        if (ownScroll) scroller = null;
        return true;
    }

    /// <summary>The dialog resets the scroller when it re-sizes itself to the window: keep it on, and a keyboard stop only while there is something to scroll to.</summary>
    private static void Reach(ScrollViewer scroller)
    {
        if (scroller.VerticalScrollBarVisibility != ScrollBarVisibility.Auto) scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stop = scroller.ScrollableHeight > 0;
        if (scroller.IsTabStop != stop) scroller.IsTabStop = stop;
    }
}
