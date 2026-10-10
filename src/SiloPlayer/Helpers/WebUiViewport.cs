using Microsoft.UI.Xaml;
namespace SiloPlayer.Helpers;

internal static class WebUiViewport
{
    // CSS breakpoints use the whole client viewport, including its sidebar.
    // Detached previews/fixtures instead own the width passed by their host.
    internal static double Width(FrameworkElement element, double localWidth)
    {
        if (App.MainWindowInstance?.Content is FrameworkElement root
            && root.XamlRoot != null && ReferenceEquals(root.XamlRoot, element.XamlRoot)
            && root.ActualWidth > 0)
            return root.ActualWidth;
        return localWidth;
    }
}
