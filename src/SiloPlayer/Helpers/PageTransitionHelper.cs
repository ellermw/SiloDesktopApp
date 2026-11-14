using Microsoft.UI.Xaml.Media.Animation;

namespace SiloPlayer.Helpers;

/// <summary>
/// Page changes swap in place: the incoming page is already at its final position, with no
/// rise, slide or drill-in. Retained pages come back with their state and scroll intact, and
/// fresh pages soften in through their own skeletons and artwork reveal. Never animate a page's
/// own Opacity/RenderTransform after the swap: that runs through every card and visibly flashes
/// dense pages.
/// </summary>
public static class PageTransitionHelper
{
    /// <summary>
    /// The transition for a page change to a new route or back to an earlier one. A Frame call
    /// without one falls back to the Frame's default theme transition, which slides the page up.
    /// </summary>
    public static NavigationTransitionInfo ForPageChange() => new SuppressNavigationTransitionInfo();

    /// <summary>Resets the incoming page root after an instant (suppressed) swap.</summary>
    public static void AnimateEntrance(FrameworkElement content)
    {
        content.RenderTransform = null;
        content.Opacity = 1;
    }
}
