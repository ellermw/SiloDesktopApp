using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ContinuumPlayer.Helpers;

/// <summary>
/// Attaches to a ScrollViewer to provide smooth animated mouse wheel scrolling
/// instead of the default choppy discrete jumps.
/// </summary>
public static class SmoothScrollHelper
{
    private const double ScrollAmount = 300; // pixels per wheel tick

    public static void Attach(ScrollViewer scrollViewer)
    {
        scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
    }

    private static void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        var props = e.GetCurrentPoint(sv).Properties;
        int delta = props.MouseWheelDelta;

        if (delta == 0) return;

        // Calculate target offset (negative delta = scroll down)
        double direction = delta > 0 ? -1 : 1;
        double targetOffset = sv.VerticalOffset + (direction * ScrollAmount);

        // Clamp to valid range
        targetOffset = Math.Clamp(targetOffset, 0, sv.ScrollableHeight);

        // Animate to target (disableAnimation: false = smooth)
        sv.ChangeView(null, targetOffset, null, false);

        // Mark handled so the default chunky scroll doesn't also fire
        e.Handled = true;
    }
}
