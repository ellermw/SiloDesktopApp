using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using Windows.UI;

/// <summary>
/// The shared destructive (red) theme tokens against the official WebUI roles: the destructive button fill
/// and its darker hover and pressed states, white foreground, error text and tint following a theme switch,
/// the high contrast lightening, a server theme, and a simulated Windows contrast theme. Captures are written
/// first so the same states can be compared before and after the change; the checks run after.
/// Self-contained on purpose: it resolves every token by key, so it also runs against a build that predates them.
/// Program.cs dispatch: SILO_NATIVE_TEST_ONLY=shared-destructive-theme.
/// </summary>
internal static class SharedDestructiveThemeNativeFixture
{
    private static readonly List<string> Failures = [];
    private static ResourceDictionary Res => Application.Current.Resources;

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    private static SolidColorBrush? Brush(string key)
    {
        try { return Res[key] as SolidColorBrush; } catch (Exception) { return null; }
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static string Hex(SolidColorBrush? b) => b is null ? "(missing)" : Hex(b.Color);

    private static Color Blend(Color top, double opacity, Color behind) => Color.FromArgb(255,
        (byte)Math.Round(top.R * opacity + behind.R * (1 - opacity), MidpointRounding.AwayFromZero),
        (byte)Math.Round(top.G * opacity + behind.G * (1 - opacity), MidpointRounding.AwayFromZero),
        (byte)Math.Round(top.B * opacity + behind.B * (1 - opacity), MidpointRounding.AwayFromZero));

    private static bool Near(Color actual, Color expected, int tolerance = 2)
        => Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;

    private static double Luminance(Color c)
    {
        static double Channel(byte v) { var s = v / 255d; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = Luminance(a) >= Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static Color Parse(string hex) => Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }

    private static Border Root(Button button) => Descendants<Border>(button).First(border => border.Name == "WebUiRoot");

    private static async Task SettleAsync(FrameworkElement element) { element.UpdateLayout(); await Task.Delay(120); element.UpdateLayout(); }

    internal static async Task RunAsync(StackPanel parent)
    {
        var settingsDirectory = Path.Combine(Program.ResultDirectory, "destructive-theme-settings");
        var settings = new SettingsService(settingsDirectory);
        var theme = new ThemeService(settings, new SettingsApi(new SiloApiClient(new HttpClient())));
        theme.ApplyTheme("midnight-cinema");

        var background = (SolidColorBrush)Res["AppBackgroundBrush"];
        var surface = new StackPanel { Width = 720, Padding = new Thickness(24), Spacing = 16, Background = background };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        Button Make(string content)
        {
            var button = new Button { Content = content, Style = (Style)Res["DestructiveButtonStyle"] };
            buttons.Children.Add(button);
            return button;
        }
        var rest = Make("Delete"); var hover = Make("Delete"); var pressed = Make("Delete"); var disabled = Make("Delete");
        surface.Children.Add(buttons);

        var errorLine = new TextBlock { Text = "Could not save the library. Try again.", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Res["ErrorBrush"] };
        surface.Children.Add(errorLine);
        var card = new Border { Style = (Style)Res["CardStyle"], Child = new TextBlock { Text = "Could not reach the server.", Foreground = (Brush)Res["ErrorBrush"] } };
        surface.Children.Add(card);
        // The sign-in style error panel: beta paints a literal #2DEF6B73; the shared tint replaces it.
        var tintBrush = Brush("ErrorTintBrush") ?? new SolidColorBrush(Color.FromArgb(0x2D, 0xEF, 0x6B, 0x73));
        var panelText = new TextBlock { Text = "That password is not right.", Foreground = (Brush)Res["ErrorBrush"], TextWrapping = TextWrapping.Wrap };
        var panel = new Border { Background = tintBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Child = panelText };
        surface.Children.Add(panel);
        // A red label on its own tint (the failed pill) and the shared red badge.
        var labelBrush = Brush("ErrorLabelBrush") ?? Brush("ErrorBrush")!;
        var pillTint = Brush("ErrorTintBrush") ?? new SolidColorBrush(Brush("ErrorBrush")!.Color) { Opacity = 0.12 };
        var pill = new Border { Background = pillTint, CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = "Failed", FontSize = 12, Foreground = labelBrush } };
        var badge = ExternalTitleCard.StatusBadge("Failed");
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        badges.Children.Add(pill); badges.Children.Add(badge);
        surface.Children.Add(badges);
        parent.Children.Add(surface);

        try
        {
            surface.Measure(new Windows.Foundation.Size(720, 600)); surface.Arrange(new Windows.Foundation.Rect(0, 0, 720, 600));
            await SettleAsync(surface);
            foreach (var button in new[] { rest, hover, pressed, disabled }) button.ApplyTemplate();
            await SettleAsync(surface);

            // ── Captures first (the same states before and after) ──
            await MediaParityNativeFixture.CaptureAsync(surface, "destructive-theme-rest.png");
            VisualStateManager.GoToState(hover, "PointerOver", false);
            VisualStateManager.GoToState(pressed, "Pressed", false);
            disabled.IsEnabled = false;
            await SettleAsync(surface);
            await MediaParityNativeFixture.CaptureAsync(surface, "destructive-theme-states.png");

            // ── Resources: the official roles ──
            Check(Hex(Brush("ErrorBrush")) == "#EF4444", $"error text/icon/border is --destructive #EF4444 (was {Hex(Brush("ErrorBrush"))})");
            Check(Hex(Brush("DestructiveFillBrush")) == "#DC2626", $"destructive fill is --destructive-fill #DC2626 (was {Hex(Brush("DestructiveFillBrush"))})");
            Check(Hex(Brush("DestructiveForegroundBrush")) == "#FFFFFF", "text on the fill is --destructive-foreground #FFFFFF");
            Check(Hex(Brush("ErrorLabelBrush")) == "#F37373", $"label is --destructive-label #F37373 (was {Hex(Brush("ErrorLabelBrush"))})");
            Check(Brush("ErrorTintBrush") is { } tint && Hex(tint) == "#EF4444" && Math.Abs(tint.Opacity - 0.1) < 1e-9, "tint is --destructive at 10%");
            Check(Brush("WebUiDestructiveHoverBrush") is { } hoverBrush && Hex(hoverBrush) == "#DC2626" && Math.Abs(hoverBrush.Opacity - 0.9) < 1e-9, "hover is the fill at 90%, not the text red");
            Check(Brush("WebUiDestructivePressedBrush") is { } pressedBrush && Hex(pressedBrush) == "#DC2626" && Math.Abs(pressedBrush.Opacity - 0.8) < 1e-9, "pressed is the fill at 80%");

            // ── The button: fill at rest, darker fill in PointerOver and Pressed, white foreground, official disabled opacity ──
            var fill = Parse("#DC2626"); var surfaceColor = background.Color; var white = Parse("#FFFFFF");
            Check(Root(rest).Background is SolidColorBrush restFill && Hex(restFill) == "#DC2626" && Math.Abs(restFill.Opacity - 1) < 1e-9, "button rests on the destructive fill");
            Check(Root(hover).Background is SolidColorBrush hoverFill && Hex(hoverFill) == "#DC2626" && Math.Abs(hoverFill.Opacity - 0.9) < 1e-9, "PointerOver paints the fill at 90%");
            Check(Root(pressed).Background is SolidColorBrush pressFill && Hex(pressFill) == "#DC2626" && Math.Abs(pressFill.Opacity - 0.8) < 1e-9, "Pressed paints the fill at 80%");
            Check(Math.Abs(Root(disabled).Opacity - 0.5) < 1e-9, "Disabled keeps the official 50% opacity");
            foreach (var (name, button) in new[] { ("rest", rest), ("hover", hover), ("pressed", pressed) })
                Check(Descendants<ContentPresenter>(button).First(p => p.Name == "WebUiContent").Foreground is SolidColorBrush text && Hex(text) == "#FFFFFF", $"{name} label is white");
            Check(Contrast(white, fill) >= 4.5, $"white on the fill is {Contrast(white, fill):0.00}:1");
            Check(Contrast(white, Blend(fill, 0.9, surfaceColor)) > Contrast(white, fill) && Contrast(white, Blend(fill, 0.8, surfaceColor)) > Contrast(white, Blend(fill, 0.9, surfaceColor)),
                "hover and pressed are darker than the fill, so white contrast only rises");

            // Rendered pixels, in the padding left of each label: the blend over the surface behind.
            var image = new RenderTargetBitmap(); await image.RenderAsync(surface);
            var pixels = (await image.GetPixelsAsync()).ToArray();
            var scale = image.PixelWidth / surface.ActualWidth;
            Color Sample(Button button)
            {
                var point = button.TransformToVisual(surface).TransformPoint(new Windows.Foundation.Point(8, button.ActualHeight / 2));
                var offset = ((int)(point.Y * scale) * image.PixelWidth + (int)(point.X * scale)) * 4;
                return Color.FromArgb(255, pixels[offset + 2], pixels[offset + 1], pixels[offset]);
            }
            Check(Near(Sample(rest), fill), $"rendered rest fill {Hex(Sample(rest))} is {Hex(fill)}");
            Check(Near(Sample(hover), Blend(fill, 0.9, surfaceColor)), $"rendered hover {Hex(Sample(hover))} is the fill at 90% over the surface {Hex(Blend(fill, 0.9, surfaceColor))}");
            Check(Near(Sample(pressed), Blend(fill, 0.8, surfaceColor)), $"rendered pressed {Hex(Sample(pressed))} is the fill at 80% {Hex(Blend(fill, 0.8, surfaceColor))}");
            Check(Luminance(Sample(hover)) < Luminance(Sample(rest)), "hover is darker than rest");

            // ── Error text, tint, shared badge ──
            Check(ReferenceEquals(errorLine.Foreground, Res["ErrorBrush"]) && ReferenceEquals(panelText.Foreground, Res["ErrorBrush"]), "error text resolves the shared error brush");
            Check(badge.Background is SolidColorBrush badgeFill && Hex(badgeFill) == "#DC2626" && Math.Abs(badgeFill.Opacity - 1) < 1e-9
                && badge.Child is TextBlock { Foreground: SolidColorBrush badgeText } && Hex(badgeText) == "#FFFFFF", "the red badge is the fill under white");

            await CheckViewerDialogAsync(parent);

            // ── Live theme switch: the same elements follow, nothing is replaced ──
            var errorBrush = Brush("ErrorBrush"); var fillBrush = Brush("DestructiveFillBrush");
            var local = settings.Load(); local.UiHighContrast = true; settings.Save(local);
            theme.ApplyTheme("midnight-cinema");
            await SettleAsync(surface);
            Check(ReferenceEquals(errorBrush, Brush("ErrorBrush")) && ReferenceEquals(fillBrush, Brush("DestructiveFillBrush")), "a theme switch updates the brushes in place");
            Check(Hex(((SolidColorBrush)errorLine.Foreground)) == "#F9BEBE" && Hex(((SolidColorBrush)panelText.Foreground)) == "#F9BEBE", "high contrast lightens red text (mix 65% toward the boost) on live elements");
            Check(Hex(Brush("ErrorLabelBrush")) == "#F9BEBE", "high contrast label is the lightened red");
            Check(Hex(Brush("DestructiveFillBrush")) == "#DC2626" && Hex(Brush("WebUiDestructiveHoverBrush")) == "#DC2626" && Hex(Brush("WebUiDestructivePressedBrush")) == "#DC2626", "high contrast leaves the fill and its hover and pressed states");
            Check(Root(rest).Background is SolidColorBrush && Hex((SolidColorBrush)Root(rest).Background) == "#DC2626" && Contrast(white, fill) >= 4.5, "the live button keeps its fill and 4.5:1 label");
            await MediaParityNativeFixture.CaptureAsync(surface, "destructive-theme-highcontrast.png");
            local = settings.Load(); local.UiHighContrast = false; settings.Save(local);

            // A server theme that redefines the text and the fill; a token it omits inherits the default.
            var shared = typeof(ThemeService).GetField("_sharedAppearance", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(theme)!;
            var colors = shared.GetType().GetProperty("Colors")!;
            colors.SetValue(shared, new Dictionary<string, string> { ["destructive"] = "#F87171", ["destructive-fill"] = "#B91C1C" });
            theme.ApplyTheme("midnight-cinema");
            Check(Hex(Brush("ErrorBrush")) == "#F87171" && Hex(Brush("DestructiveFillBrush")) == "#B91C1C" && Hex(Brush("DestructiveForegroundBrush")) == "#FFFFFF", "a server theme sets text and fill; the omitted foreground inherits white");
            Check(Hex(Brush("ErrorLabelBrush")) == "#FA9595" && Hex(Brush("WebUiDestructiveHoverBrush")) == "#B91C1C", "label and hover follow the server theme");
            Check(Hex(((SolidColorBrush)errorLine.Foreground)) == "#F87171" && Root(rest).Background is SolidColorBrush serverFill && Hex(serverFill) == "#B91C1C", "live elements follow the server theme");
            colors.SetValue(shared, new Dictionary<string, string> { ["destructive-fill"] = "#B91C1C" });
            theme.ApplyTheme("midnight-cinema");
            Check(Hex(Brush("ErrorBrush")) == "#EF4444" && Hex(Brush("DestructiveFillBrush")) == "#B91C1C", "a server theme that omits the text inherits the default text");
            colors.SetValue(shared, new Dictionary<string, string> { ["primary"] = "#4F46E5", ["ring"] = "#4F46E5" });
            theme.ApplyTheme("midnight-cinema");
            Check(Hex(Brush("AccentBrush")) == "#4F46E5" && Hex(Brush("ErrorBrush")) == "#EF4444" && Hex(Brush("DestructiveFillBrush")) == "#DC2626", "the branding accent leaves every red token on the default");
            colors.SetValue(shared, new Dictionary<string, string>());
            theme.ApplyTheme("midnight-cinema");
            Check(Hex(Brush("ErrorBrush")) == "#EF4444" && Hex(Brush("DestructiveFillBrush")) == "#DC2626", "clearing the server theme restores the default");

            // ── A simulated Windows contrast theme: system colors win as before, the red roles are not repainted ──
            var palette = typeof(ThemeService).GetField("_simulatedSystemPalette", BindingFlags.NonPublic | BindingFlags.Instance);
            if (palette is null) Check(false, "the contrast simulation hook exists");
            else
            {
                var window = Parse("#000000"); var windowText = Parse("#FFFFFF"); var highlight = Parse("#1AEBFF"); var highlightText = Parse("#000000"); var gray = Parse("#3FF23F");
                palette.SetValue(theme, (window, windowText, highlight, highlightText, gray));
                theme.ApplyTheme("midnight-cinema");
                await SettleAsync(surface);
                Check(Hex(Brush("AppBackgroundBrush")) == "#000000" && Hex(Brush("PrimaryTextBrush")) == "#FFFFFF" && Hex(Brush("AccentBrush")) == "#1AEBFF", "a contrast theme repaints surfaces, text and accent with the system colors");
                Check(Hex(Brush("ErrorBrush")) == "#EF4444" && Hex(Brush("DestructiveFillBrush")) == "#DC2626" && Hex(Brush("DestructiveForegroundBrush")) == "#FFFFFF", "the red roles are unchanged in a contrast theme, as before");
                Check(Contrast(white, fill) >= 4.5 && Hex(Brush("WebUiDestructiveHoverBrush")) == "#DC2626", "a destructive button keeps 4.5:1 white on its fill in a contrast theme");
                await MediaParityNativeFixture.CaptureAsync(surface, "destructive-theme-contrast.png");
                palette.SetValue(theme, null);
                theme.ApplyTheme("midnight-cinema");
                Check(Hex(Brush("AppBackgroundBrush")) == "#141417" && Hex(Brush("ErrorBrush")) == "#EF4444", "leaving the contrast theme restores the palette");
            }

            if (Failures.Count > 0) throw new InvalidOperationException("Shared destructive theme checks failed: " + string.Join("; ", Failures));
            Program.Log("PASS: shared destructive theme roles, button states, live theme switch, server theme, high contrast and contrast-theme behaviour.");
        }
        finally { parent.Children.Remove(surface); }
    }

    private static async Task CheckViewerDialogAsync(StackPanel parent)
    {
        var factory = typeof(SiloPlayer.Views.CollectionEditorPage).GetMethod("CreateDepartureDialog", BindingFlags.Static | BindingFlags.NonPublic)!;
        var dialog = (ContentDialog)factory.Invoke(null, [parent.XamlRoot])!;
        var shown = dialog.ShowAsync();
        try
        {
            await Task.Delay(220); dialog.UpdateLayout();
            var primary = Descendants<Button>(dialog).First(button => button.Name == "PrimaryButton");
            await MediaParityNativeFixture.CaptureAsync(dialog, "destructive-theme-viewer-dialog.png");
            Check(primary.Background is SolidColorBrush fill && Hex(fill) == "#DC2626", $"the viewer discard confirmation fills with the destructive fill (was {Hex(primary.Background as SolidColorBrush)})");
            VisualStateManager.GoToState(primary, "PointerOver", false);
            await Task.Delay(100);
            await MediaParityNativeFixture.CaptureAsync(dialog, "destructive-theme-viewer-dialog-hover.png");
        }
        finally { dialog.Hide(); await shown; }
    }
}
