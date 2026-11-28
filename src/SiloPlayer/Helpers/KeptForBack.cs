using Microsoft.UI.Xaml.Navigation;

namespace SiloPlayer.Helpers;

/// <summary>A page the frame keeps only for Back to the entry it shows.</summary>
public interface IKeptForBack
{
    /// <summary>Whether Back to <paramref name="parameter"/> may show this instance as it was left.</summary>
    bool HoldsEntry(object? parameter);
}

/// <summary>
/// The frame reuses a cached page for every navigation to its type. A page that shows one entry
/// (a collection, a person) is reused only by Back to that same entry while it still holds it;
/// any other navigation to its type gets a new page, as before. Kept pages count toward the
/// frame's CacheSize, and the frame keeps no strong reference to a page it has dropped.
/// </summary>
public static class KeptForBack
{
    public static void Attach(Page page)
    {
        if (page.Frame is not { } frame) return;
        var target = new WeakReference<Page>(page);
        NavigatingCancelEventHandler? handler = null;
        handler = (_, e) =>
        {
            if (!target.TryGetTarget(out var kept)) { frame.Navigating -= handler; return; }
            if (e.SourcePageType != kept.GetType()) return;
            if (e.NavigationMode == NavigationMode.Back && kept is IKeptForBack entry && entry.HoldsEntry(e.Parameter)) return;
            // Dropped before the frame resolves the page, so this navigation creates a new one.
            kept.NavigationCacheMode = NavigationCacheMode.Disabled;
            frame.Navigating -= handler;
        };
        frame.Navigating += handler;
    }
}
