using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Controls;

/// <summary>
/// The completed-season check on a season poster of the series page: a green circle at the poster's top-right with a black check. White on
/// the 90% green is 2.1:1 to 2.7:1 over the artwork, below the 3:1 a graphic needs; black is 7.7:1 or more (official SeasonCarousel, #5080,
/// which draws it black like the poster card's watched check).
/// </summary>
public static class SeasonCompletedBadge
{
    internal static readonly Windows.UI.Color Fill = Windows.UI.Color.FromArgb(0xE6, 0x22, 0xC5, 0x5E);
    internal static readonly Windows.UI.Color Check = Microsoft.UI.Colors.Black;

    /// <summary>Named Watched for assistive technology, as the official badge is (<c>role="img"</c> with the Watched label, #5123); its check is decoration.</summary>
    public static Border Create() => MediaItemCardActions.BindText(new Border()
    {
        Width = 28,
        Height = 28,
        CornerRadius = new CornerRadius(14),
        Background = new SolidColorBrush(Fill),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 6, 6, 0),
        Child = Icon(),
    }, "Name", badge => AutomationProperties.SetName(badge, SiloPlayer.Core.Localization.AppStrings.Get("web.common.overlays.watched")));

    private static FontIcon Icon()
    {
        var icon = new FontIcon { Glyph = "\uE73E", FontSize = 14, Foreground = new SolidColorBrush(Check) };
        AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return icon;
    }
}
