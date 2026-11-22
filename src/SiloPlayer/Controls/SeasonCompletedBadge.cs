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

    public static Border Create() => new()
    {
        Width = 28,
        Height = 28,
        CornerRadius = new CornerRadius(14),
        Background = new SolidColorBrush(Fill),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 6, 6, 0),
        Child = new FontIcon { Glyph = "", FontSize = 14, Foreground = new SolidColorBrush(Check) },
    };
}
