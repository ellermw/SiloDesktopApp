using Microsoft.UI.Xaml.Controls;

namespace ContinuumPlayer.Helpers;

/// <summary>
/// Previously this helper intercepted PointerWheelChanged to animate 300 px
/// per-tick jumps via <c>ScrollViewer.ChangeView</c>. That forced every wheel
/// tick through the UI thread, queued animated transitions, and suppressed
/// the default composition-driven scroll — which WinUI 3's ScrollViewer
/// already handles smoothly at the GPU level. On large libraries (100k+
/// items) the behaviour presented as rubber-band chop during fast scroll.
///
/// Attach() is now a no-op so the default GPU-accelerated scroll takes over.
/// The method is kept as a shim so existing <c>SmoothScrollHelper.Attach(...)</c>
/// call sites don't need to change.
/// </summary>
public static class SmoothScrollHelper
{
    public static void Attach(ScrollViewer scrollViewer)
    {
        // Intentionally empty — default scroll is smoother than what we
        // replaced it with. See class-level comment for rationale.
    }
}
