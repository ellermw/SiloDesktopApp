using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SiloPlayer.Helpers;

/// <summary>Normalizes uniform CSS corner radii before WinUI paints a badge or button.</summary>
public static class RoundedBadge
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundedBadge), new PropertyMetadata(0d, RadiusChanged));

    public static double GetRadius(DependencyObject target) => (double)target.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject target, double value) => target.SetValue(RadiusProperty, value);

    public static Border Apply(Border border, double radius)
    {
        SetRadius(border, radius);
        return border;
    }

    private static void RadiusChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element || target is not (Border or Control)) return;
        element.SizeChanged -= SizeChanged;
        if ((double)args.NewValue > 0)
        {
            element.SizeChanged += SizeChanged;
            UpdateRadius(element);
        }
    }

    private static void SizeChanged(object sender, SizeChangedEventArgs args) => UpdateRadius((FrameworkElement)sender);

    private static void UpdateRadius(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        // CSS reduces an oversized uniform radius equally in both axes.
        // Passing 999/9999 directly lets WinUI produce elliptical corners.
        var radius = Math.Min(GetRadius(element), Math.Min(element.ActualWidth, element.ActualHeight) / 2);
        var corners = new CornerRadius(radius);
        if (element is Border border && border.CornerRadius != corners) border.CornerRadius = corners;
        else if (element is Control control && control.CornerRadius != corners) control.CornerRadius = corners;
    }
}
