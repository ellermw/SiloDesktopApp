using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Helpers;

internal static class AppUpdateAppearance
{
    internal static SolidColorBrush Green(string backgroundKey = "AppBackgroundBrush")
    {
        var color = Application.Current.Resources.TryGetValue(backgroundKey, out var value) && value is SolidColorBrush brush
            ? brush.Color : Windows.UI.Color.FromArgb(255, 0, 0, 0);
        var bright = .2126 * color.R + .7152 * color.G + .0722 * color.B > 140;
        return new SolidColorBrush(bright ? Windows.UI.Color.FromArgb(255, 15, 123, 15) : Windows.UI.Color.FromArgb(255, 74, 222, 128));
    }
}
