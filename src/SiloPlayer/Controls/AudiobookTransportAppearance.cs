using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace SiloPlayer.Controls;

internal static class AudiobookTransportAppearance
{
    // Keep the existing player's glyph/state and automation owner, but paint
    // filled pause bars like the audiobook transport in the WebUI.
    internal static void Apply(Button button, FontIcon icon, double size)
    {
        button.Content = null;
        var surface = new Grid();
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = size * .18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var index = 0; index < 2; index++) bars.Children.Add(new Rectangle { Width = size * .18, Height = size * .70, Fill = icon.Foreground });
        surface.Children.Add(icon); surface.Children.Add(bars); button.Content = surface;
        void Paint() { var pausedGlyph = icon.Glyph == "\uE769"; icon.Visibility = pausedGlyph ? Visibility.Collapsed : Visibility.Visible; bars.Visibility = pausedGlyph ? Visibility.Visible : Visibility.Collapsed; }
        icon.RegisterPropertyChangedCallback(FontIcon.GlyphProperty, (_, _) => Paint()); Paint();
    }
}
