using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using System.Runtime.InteropServices.WindowsRuntime;

internal static class SharedControlsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var surface = new StackPanel { Width = 720, Padding = new Thickness(24), Spacing = 24,
            Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var style in new[] { "AccentButtonStyle", "SecondaryButtonStyle", "GhostButtonStyle", "DestructiveButtonStyle", "OutlineButtonStyle" })
        {
            var button = new Button { Content = style.Replace("ButtonStyle", ""), Style = (Style)Application.Current.Resources[style] };
            buttons.Children.Add(button);
        }
        surface.Children.Add(buttons);
        var ordinaryButton = new Button { Content = "Ordinary action" };
        surface.Children.Add(ordinaryButton);
        var toggle = new ToggleSwitch { Header = "Switch", IsOn = false };
        var slider = new Slider { Header = "Slider", Minimum = 0, Maximum = 100, Value = 35, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var card = new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = new TextBlock { Text = "Card content" } };
        surface.Children.Add(toggle); surface.Children.Add(slider); surface.Children.Add(card);
        var input = new TextBox { Text = "Input", Style = (Style)Application.Current.Resources["DarkTextBoxStyle"] };
        var password = new PasswordBox { Password = "fixture", Style = (Style)Application.Current.Resources["DarkPasswordBoxStyle"] };
        var select = new ComboBox { SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        select.Items.Add("Selected option"); surface.Children.Add(input); surface.Children.Add(password); surface.Children.Add(select);
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var icon in new[] { "house", "folder", "bell", "hourglass", "copy", "settings" }) icons.Children.Add(WebUiIcon.Navigation(icon, 18));
        surface.Children.Add(icons); parent.Children.Add(surface);
        try
        {
            surface.Measure(new Windows.Foundation.Size(720, 600)); surface.Arrange(new Windows.Foundation.Rect(0, 0, 720, 600));
            await Task.Delay(100); surface.UpdateLayout();
            var expectedFont = ((FontFamily)Application.Current.Resources["ThemeFontFamily"]).Source;
            if (((TextBlock)card.Child).FontFamily.Source != expectedFont)
                throw new InvalidOperationException("Unstyled page text still uses the Windows font rather than the current WebUI theme font.");
            foreach (var button in buttons.Children.OfType<Button>())
            {
                Program.Log($"Shared button {button.Content}: {button.ActualWidth:0.##}x{button.ActualHeight:0.##}, padding={button.Padding}, font={button.FontSize:0.##}.");
                if (Math.Abs(button.ActualHeight - 36) > .5) throw new InvalidOperationException("Shared button default height differs from the official 36px contract.");
                if (button.CornerRadius != new CornerRadius(10)) throw new InvalidOperationException("Actual shared button corner differs from computed original10px (--radius .75rem), not generic Tailwind6px.");
            }
            if (ordinaryButton.CornerRadius != new CornerRadius(10))
                throw new InvalidOperationException("Unstyled actions override the computed original10px rounded-md corner.");
            toggle.ApplyTemplate(); slider.ApplyTemplate(); surface.UpdateLayout();
            var track = Find<Microsoft.UI.Xaml.Shapes.Rectangle>(toggle, "OuterBorder");
            var knob = Find<Microsoft.UI.Xaml.Shapes.Rectangle>(toggle, "SwitchKnobOff");
            Program.Log($"Shared switch track={track.ActualWidth:0.##}x{track.ActualHeight:0.##}, knob={knob.ActualWidth:0.##}x{knob.ActualHeight:0.##}.");
            if (Math.Abs(track.ActualWidth - 32) > .5 || Math.Abs(track.ActualHeight - 18.4) > .5 || Math.Abs(knob.ActualWidth - 16) > .5)
                throw new InvalidOperationException("Shared switch track/thumb geometry differs from current WebUI.");
            var thumb = Find<Microsoft.UI.Xaml.Controls.Primitives.Thumb>(slider, "HorizontalThumb");
            if (Math.Abs(thumb.ActualWidth - 16) > .5) throw new InvalidOperationException("Shared slider thumb geometry differs from current WebUI.");
            if (card.Padding != new Thickness(24) || card.CornerRadius != new CornerRadius(12)) throw new InvalidOperationException("Shared card spacing/corner differs from computed current WebUI.");
            if (input.CornerRadius != new CornerRadius(10) || password.CornerRadius != new CornerRadius(10) || select.CornerRadius != new CornerRadius(10))
                throw new InvalidOperationException("Rendered shared Input/Password/Select must follow computed original10px corners.");
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ToggleSwitchAutomationPeer(toggle);
            ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle();
            if (!toggle.IsOn) throw new InvalidOperationException("Styled switch lost its native toggle interaction.");
            await MediaParityNativeFixture.CaptureAsync(surface, "shared-controls-normal.png");
            foreach (var button in buttons.Children.OfType<Button>()) { button.ApplyTemplate(); VisualStateManager.GoToState(button, "PointerOver", false); }
            await MediaParityNativeFixture.CaptureAsync(surface, "shared-controls-hover.png");
            var hoverImage = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await hoverImage.RenderAsync(surface);
            var hoverPixels = (await hoverImage.GetPixelsAsync()).ToArray();
            var hoverOffset = (30 * hoverImage.PixelWidth + 30) * 4;
            if (hoverPixels[hoverOffset] < 190 || hoverPixels[hoverOffset + 1] < 130 || hoverPixels[hoverOffset + 2] < 80)
                throw new InvalidOperationException("Primary hover lost its theme primary fill and fell back to the generic Fluent surface.");
            foreach (var button in buttons.Children.OfType<Button>()) button.IsEnabled = false;
            await MediaParityNativeFixture.CaptureAsync(surface, "shared-controls-disabled.png");
            await CheckPageBackdropAsync(parent);
            Program.Log("PASS: shared control default sizes, native state matrix, card padding and published Lucide SVG rendering.");
        }
        finally { parent.Children.Remove(surface); }
    }
    private static async Task CheckPageBackdropAsync(StackPanel parent)
    {
        var resources = Application.Current.Resources;
        var previousBackground = resources["AppBackgroundBrush"];
        var hadAmbient = resources.ContainsKey("AmbientBrush");
        var previousAmbient = hadAmbient ? resources["AmbientBrush"] : null;
        var page = new Page { Width = 600, Height = 400 };
        try
        {
            resources["AppBackgroundBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 23, 34));
            resources["AmbientBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 64, 128, 192));
            page.Background = (Brush)resources["AppBackgroundBrush"];
            parent.Children.Add(page); await Task.Delay(80); page.UpdateLayout();
            if (page.Background is not RadialGradientBrush radial)
                throw new InvalidOperationException("Standard Page background has no current WebUI top ambient glow.");
            if (Math.Abs(radial.Center.X - 300) > .01 || Math.Abs(radial.Center.Y) > .01
                || Math.Abs(radial.RadiusX - 500) > .01 || Math.Abs(radial.RadiusY - 500) > .01)
                throw new InvalidOperationException("Shared Page glow does not use the farthest-corner circle.");
            // WinUI RenderTargetBitmap may return0x0 for Page itself; capture its realized content with the same actual Page brush.
            var backdropSurface = new Grid { Width = 600, Height = 400, Background = page.Background };
            page.Content = backdropSurface; await Task.Delay(60); page.UpdateLayout();
            var image = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await image.RenderAsync(backdropSurface);
            var pixels = (await image.GetPixelsAsync()).ToArray();
            if (image.PixelWidth == 0 || image.PixelHeight == 0 || pixels.Length != image.PixelWidth * image.PixelHeight * 4)
                throw new InvalidOperationException("Realized Page backdrop content could not be captured.");
            void Pixel(int x, int y, byte red, byte green, byte blue)
            {
                var offset = (y * image.PixelWidth + x) * 4;
                if (Math.Abs(pixels[offset] - blue) > 2 || Math.Abs(pixels[offset + 1] - green) > 2
                    || Math.Abs(pixels[offset + 2] - red) > 2 || pixels[offset + 3] != 255)
                    throw new InvalidOperationException($"Shared Page glow pixel at {x},{y} does not match the opaque current CSS blend.");
            }
            Pixel(image.PixelWidth / 2, 0, 21, 34, 50);
            Pixel(image.PixelWidth / 2, image.PixelHeight - 1, 16, 23, 34);
            await MediaParityNativeFixture.CaptureAsync(backdropSurface, "shared-page-backdrop.png");
            page.Width = 800; page.Height = 600; await Task.Delay(60); page.UpdateLayout();
            if (Math.Abs(radial.Center.X - 400) > .01 || Math.Abs(radial.RadiusX - 721.110255) > .01)
                throw new InvalidOperationException("Shared Page ambient circle did not follow a viewport resize.");
            var custom = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 12, 34, 56));
            page.Background = custom;
            var helper = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Helpers.PageBackdrop")!;
            helper.GetMethod("RefreshAll")!.Invoke(null, null);
            if (!ReferenceEquals(page.Background, custom))
                throw new InvalidOperationException("Shared Page glow replaced a custom page background.");
            page.Background = (Brush)resources["AppBackgroundBrush"];
            resources["AmbientBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 192, 64, 128));
            helper.GetMethod("RefreshAll")!.Invoke(null, null);
            if (page.Background is not RadialGradientBrush refreshed || refreshed.GradientStops[0].Color.R != 34
                || refreshed.GradientStops[0].Color.G != 27 || refreshed.GradientStops[0].Color.B != 43)
                throw new InvalidOperationException("Shared Page glow did not follow ambient theme changes.");
            Program.Log("PASS: shared Page actual radial geometry, opaque rendered glow, resize/theme refresh and custom background protection.");
        }
        finally
        {
            parent.Children.Remove(page);
            resources["AppBackgroundBrush"] = previousBackground;
            if (hadAmbient) resources["AmbientBrush"] = previousAmbient; else resources.Remove("AmbientBrush");
            typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Helpers.PageBackdrop")?.GetMethod("RefreshAll")?.Invoke(null, null);
        }
    }
    private static T Find<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name) return element;
            try { return Find<T>(child, name); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Native template element unavailable: " + name);
    }
}
