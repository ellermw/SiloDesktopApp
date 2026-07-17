namespace SiloPlayer.Helpers;

/// <summary>
/// Normalizes a newly navigated page without animating its entire visual tree.
/// Frame navigation already swaps the route atomically; applying opacity or
/// transforms after that swap makes dense pages visibly flash and forces extra
/// compositor work across every card.
/// </summary>
public static class PageTransitionHelper
{
    public static void AnimateEntrance(FrameworkElement content)
    {
        content.RenderTransform = null;
        content.Opacity = 1;
    }
}
