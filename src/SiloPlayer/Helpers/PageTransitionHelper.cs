using Microsoft.UI.Xaml.Media.Animation;

namespace SiloPlayer.Helpers;

/// <summary>
/// Page changes use WinUI's own entrance transition: the incoming page eases up
/// and fades in on the compositor as one visual, so the shell feels soft instead
/// of cutting from one page to the next. Windows "Animation effects" off keeps
/// the instant swap. Never animate a page's own Opacity/RenderTransform after the
/// swap: that runs through every card and visibly flashes dense pages.
/// </summary>
public static class PageTransitionHelper
{
    /// <summary>The transition for a page change to a new route or back to an earlier one.</summary>
    public static NavigationTransitionInfo ForPageChange() => ArtworkReveal.AnimationsEnabled
        ? new EntranceNavigationTransitionInfo()
        : new SuppressNavigationTransitionInfo();

    /// <summary>
    /// Resets the incoming page root after an instant (suppressed) swap only; a page
    /// change that eases in leaves the root to its entrance transition.
    /// </summary>
    public static void AnimateEntrance(FrameworkElement content)
    {
        content.RenderTransform = null;
        content.Opacity = 1;
    }
}
