using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SiloPlayer.Helpers;

/// <summary>Normalizes uniform CSS corner radii before WinUI paints a badge.</summary>
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
        if (target is not Border border) return;
        border.SizeChanged -= SizeChanged;
        if ((double)args.NewValue > 0)
        {
            border.SizeChanged += SizeChanged;
            UpdateRadius(border);
        }
    }

    private static void SizeChanged(object sender, SizeChangedEventArgs args) => UpdateRadius((Border)sender);

    private static void UpdateRadius(Border border)
    {
        if (border.ActualWidth <= 0 || border.ActualHeight <= 0) return;
        // CSS reduces an oversized uniform radius equally in both axes.
        // Passing 999/9999 directly lets WinUI produce elliptical corners.
        var radius = Math.Min(GetRadius(border), Math.Min(border.ActualWidth, border.ActualHeight) / 2);
        var corners = new CornerRadius(radius);
        if (border.CornerRadius != corners) border.CornerRadius = corners;
    }
}
