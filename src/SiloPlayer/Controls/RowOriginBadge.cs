using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>
/// The tag that tells a profile whether a Home row is managed by the server or was added by this profile (official RowOriginBadge, #4702):
/// "Yours" in the info colour, "Server" in a muted one. Only the profile surface shows it; every row on the Sections admin page is the server's.
/// </summary>
public static class RowOriginBadge
{
    public static string Text(RowOrigin origin) => origin == RowOrigin.Yours
        ? SiloPlayer.Core.Localization.AppStrings.Get("web.home.rows.line.yours", null, "Yours")
        : HomeRowText.T("web.home.rows.line.server");

    public static Border Create(RowOrigin origin)
    {
        var yours = origin == RowOrigin.Yours;
        var info = Application.Current.Resources.TryGetValue("InfoBrush", out var found) && found is SolidColorBrush brush
            ? brush.Color : ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;
        var muted = ((SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]).Color;
        var border = ((SolidColorBrush)Application.Current.Resources["BorderBrush"]).Color;
        Windows.UI.Color Alpha(Windows.UI.Color color, byte a) => Windows.UI.Color.FromArgb(a, color.R, color.G, color.B);
        var badge = new Border
        {
            Name = yours ? "HomeRowOriginYours" : "HomeRowOriginServer",
            Height = 22, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(yours ? Alpha(info, 38) : Alpha(border, 90)),
            BorderBrush = new SolidColorBrush(yours ? Alpha(info, 77) : border),
            Child = new TextBlock
            {
                Text = Text(origin), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(yours ? info : muted),
            },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge, Text(origin));
        return badge;
    }
}
