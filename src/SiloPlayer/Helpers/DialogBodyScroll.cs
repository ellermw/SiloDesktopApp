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

    private static void OnOpened(ContentDialog dialog, ContentDialogOpenedEventArgs args) => Apply(dialog);

    /// <summary>Applies the rule to an open dialog; true when its body now scrolls.</summary>
    internal static bool Apply(ContentDialog dialog)
    {
        var scroller = EditorDialogPresentation.Descendants<ScrollViewer>(dialog).FirstOrDefault(item => item.Name == "ContentScrollViewer");
        if (scroller is null) return false;
        // A TextBox carries its own inner scroller ("ContentElement"); only a scroller of the content's own counts.
        var ownScroll = dialog.Content is DependencyObject content
            && EditorDialogPresentation.Descendants<ScrollViewer>(content).Any(item => item.Name != "ContentElement");
        if (ownScroll) return false;
        scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; scroller.UseSystemFocusVisuals = true;
        void Reach() => scroller.IsTabStop = scroller.ScrollableHeight > 0;
        scroller.SizeChanged += (_, _) => Reach();
        scroller.LayoutUpdated += (_, _) => Reach();
        Reach();
        return true;
    }
}
