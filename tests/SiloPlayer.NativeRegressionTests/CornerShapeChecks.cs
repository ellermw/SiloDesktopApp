using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// WinUI limits an oversized corner radius on each axis by itself (CSS reduces it equally on both), so a "rounded-full" element given a
/// radius such as 999 is drawn as an oval with pointed ends instead of a pill. A pill's radius must follow its size
/// (<c>RoundedBadge.SetRadius</c>, <c>DashboardUi.Stadium</c>). This finds elements that are drawn with such oval corners.
/// </summary>
internal static class CornerShapeChecks
{
    /// <summary>Visible, laid-out elements under <paramref name="root"/> whose corner would be drawn more than two pixels longer on one axis than the other.</summary>
    internal static IReadOnlyList<string> OvalCorners(DependencyObject root)
    {
        var found = new List<string>();
        Walk(root);
        return found;

        static bool Thick(Thickness thickness) => thickness.Left > 0 || thickness.Top > 0 || thickness.Right > 0 || thickness.Bottom > 0;
        static bool Paints(Brush? brush) => brush switch
        {
            null => false,
            SolidColorBrush solid => solid.Color.A > 0 && solid.Opacity > 0,
            _ => brush.Opacity > 0,
        };

        void Walk(DependencyObject node)
        {
            if (node is FrameworkElement { Visibility: Visibility.Visible, ActualWidth: > 0, ActualHeight: > 0 } element)
            {
                // Only what is painted at rest counts: a transparent text button has corners nobody sees until it is hovered.
                var (corners, painted) = element switch
                {
                    Border border => ((CornerRadius?)border.CornerRadius, Paints(border.Background) || (Paints(border.BorderBrush) && Thick(border.BorderThickness))),
                    Control control => (control.CornerRadius, Paints(control.Background) || (Paints(control.BorderBrush) && Thick(control.BorderThickness))),
                    Grid grid => (grid.CornerRadius, Paints(grid.Background) || (Paints(grid.BorderBrush) && Thick(grid.BorderThickness))),
                    StackPanel stack => (stack.CornerRadius, Paints(stack.Background) || (Paints(stack.BorderBrush) && Thick(stack.BorderThickness))),
                    _ => ((CornerRadius?)null, false),
                };
                if (painted && corners is { } radius)
                {
                    var largest = Math.Max(Math.Max(radius.TopLeft, radius.TopRight), Math.Max(radius.BottomLeft, radius.BottomRight));
                    var across = Math.Min(largest, element.ActualWidth / 2);
                    var down = Math.Min(largest, element.ActualHeight / 2);
                    if (Math.Abs(across - down) > 2)
                    {
                        var id = AutomationProperties.GetAutomationId(element);
                        var name = AutomationProperties.GetName(element);
                        found.Add($"{element.GetType().Name}{(id.Length > 0 ? " #" + id : "")}{(name.Length > 0 ? " \"" + name + "\"" : "")} {element.ActualWidth:0.#}x{element.ActualHeight:0.#} radius {largest:0.#}");
                    }
                }
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++) Walk(VisualTreeHelper.GetChild(node, index));
        }
    }
}
