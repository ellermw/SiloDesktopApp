using System.Globalization;
using Microsoft.UI;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services.Admin;
using Windows.UI;
using Windows.UI.Text;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SiloPlayer.Controls.Admin;

/// <summary>
/// Visual primitives for the Admin Monitor dashboard, mapped from the WebUI's
/// Tailwind tokens to the app's theme resources (which ThemeService fills
/// from the server theme). Status hues the WebUI defines globally (success,
/// warning, info) and the Tailwind palette colors widgets name directly are
/// fixed values taken from the pinned app.css and Tailwind defaults.
/// </summary>
internal static class DashboardUi
{
    // ---- Tokens --------------------------------------------------------

    public static Color Res(string key, string fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true)
        {
            if (value is Color color) return color;
            if (value is SolidColorBrush brush) return brush.Color;
        }
        return Hex(fallback);
    }

    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        byte P(int i) => byte.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length == 8 ? Color.FromArgb(P(0), P(2), P(4), P(6)) : Color.FromArgb(255, P(0), P(2), P(4));
    }

    public static Color Foreground => Res("PrimaryTextColor", "#E8E8EC");
    public static Color MutedForeground => Res("SecondaryTextColor", "#A4A4AE");
    public static Color Card => Res("CardBackgroundColor", "#1C1C20");
    public static Color BackgroundColor => Res("AppBackgroundColor", "#141417");
    public static Color BorderColor => Res("BorderColor", "#28282E");
    public static Color Surface => Res("SurfaceColor", "#1C1C20");
    public static Color SurfaceHover => Res("SurfaceHoverColor", "#232328");
    public static Color Primary => Res("AccentColor", "#E8E8EC");
    public static Color PrimaryForeground => Res("AccentForegroundColor", "#141417");
    public static Color Accent => Res("AccentBackgroundColor", "#232328");
    public static Color Muted => Res("MutedColor", "#1C1C20");
    public static Color Popover => Res("PopoverColor", "#18181C");
    public static Color Destructive => Res("ErrorColor", "#EF4444");
    public static readonly Color Success = Hex("#57CB60");
    public static readonly Color Warning = Hex("#EAB532");
    public static readonly Color Info = Hex("#4BA3F7");
    public static readonly Color Amber300 = Hex("#FCD34D");
    public static readonly Color Amber400 = Hex("#FBBF24");
    public static readonly Color Amber500 = Hex("#F59E0B");
    public static readonly Color Emerald500 = Hex("#10B981");
    public static readonly Color Green500 = Hex("#22C55E");
    public static readonly Color Sky500 = Hex("#0EA5E9");
    public static readonly Color Jellyfin = Hex("#AA5CC3");

    public static Color Chart(int slot) => DashboardChartMath.SeriesSlot(slot) switch
    {
        0 => Res("Chart1Color", "#5F74EE"),
        1 => Res("Chart2Color", "#169E88"),
        2 => Res("Chart3Color", "#BF7F22"),
        3 => Res("Chart4Color", "#C2578F"),
        _ => Res("Chart5Color", "#3F97CF"),
    };

    public static Color Alpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B);

    /// <summary>CSS color-mix(in srgb, a p%, b).</summary>
    public static Color Mix(Color a, double weight, Color b)
    {
        byte M(byte x, byte y) => (byte)Math.Round(x * weight + y * (1 - weight));
        return Color.FromArgb(M(a.A, b.A), M(a.R, b.R), M(a.G, b.G), M(a.B, b.B));
    }

    public static SolidColorBrush B(Color color) => new(color);
    public static SolidColorBrush B(Color color, double alpha) => new(Alpha(color, alpha));

    /// <summary>.surface-panel: surface 90%, border 72% (stat tiles drop the border with border-0).</summary>
    public static SolidColorBrush SurfacePanel => B(Surface, 0.9);
    /// <summary>.surface-panel-subtle: surface 72%, border 62%.</summary>
    public static SolidColorBrush SurfacePanelSubtle => B(Surface, 0.72);

    // ---- Text ----------------------------------------------------------

    public static readonly FontWeight Medium = new() { Weight = 500 };
    public static readonly FontWeight Semibold = new() { Weight = 600 };
    public static readonly FontWeight Bold = new() { Weight = 700 };
    public static readonly FontWeight ExtraBold = new() { Weight = 800 };

    public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, Color? color = null,
        bool trim = false, bool wrap = false, double? lineHeight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? new FontWeight { Weight = 400 },
            Foreground = B(color ?? Foreground),
            TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            TextWrapping = wrap ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
            IsTextScaleFactorEnabled = true,
        };
        if ((lineHeight ?? (size switch { 10 => 15, 11 => 16.5, 12 => 16, 14 => 20, _ => size * 1.5 })) is { } height)
        {
            block.LineHeight = height;
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }
        if (trim && text.Length > 0) ToolTipService.SetToolTip(block, text);
        return block;
    }

    /// <summary>A rich catalog message; tagged runs get their own weight/color.</summary>
    public static TextBlock Rich(IReadOnlyList<DashboardTextRun> runs, double size, Color baseColor,
        Func<string, (FontWeight? Weight, Color? Color)>? style = null, bool wrap = true)
    {
        var block = new TextBlock { FontSize = size, Foreground = B(baseColor), TextWrapping = wrap ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap };
        foreach (var run in runs)
        {
            var piece = new Microsoft.UI.Xaml.Documents.Run { Text = run.Text };
            if (run.Tag != null && style != null)
            {
                var (weight, color) = style(run.Tag);
                if (weight is { } w) piece.FontWeight = w;
                if (color is { } c) piece.Foreground = B(c);
            }
            block.Inlines.Add(piece);
        }
        return block;
    }

    // ---- Icons (Lucide 0.576 geometry) --------------------------------

    private static string Circle(double cx, double cy, double r)
        => FormattableString.Invariant($"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0");

    private static string Rect(double x, double y, double w, double h, double rx)
        => rx <= 0 ? FormattableString.Invariant($"M{x} {y}h{w}v{h}h{-w}z")
            : FormattableString.Invariant($"M{x + rx} {y}h{w - 2 * rx}a{rx} {rx} 0 0 1 {rx} {rx}v{h - 2 * rx}a{rx} {rx} 0 0 1 {-rx} {rx}h{-(w - 2 * rx)}a{rx} {rx} 0 0 1 {-rx} {-rx}v{-(h - 2 * rx)}a{rx} {rx} 0 0 1 {rx} {-rx}z");

    private static string Line(double x1, double y1, double x2, double y2) => FormattableString.Invariant($"M{x1} {y1}L{x2} {y2}");

    private static readonly Dictionary<string, string> IconPaths = new(StringComparer.Ordinal)
    {
        ["activity"] = "M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2",
        ["gauge"] = "m12 14 4-4M3.34 19a10 10 0 1 1 17.32 0",
        ["zap"] = "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z",
        ["user-check"] = "m16 11 2 2 4-4M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" + Circle(9, 7, 4),
        ["film"] = Rect(3, 3, 18, 18, 2) + "M7 3v18M3 7.5h4M3 12h18M3 16.5h4M17 3v18M17 7.5h4M17 16.5h4",
        ["tv"] = "m17 2-5 5-5-5" + Rect(2, 7, 20, 15, 2),
        ["shield-check"] = "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1zM9 12l2 2 4-4",
        ["users"] = "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M16 3.128a4 4 0 0 1 0 7.744M22 21v-2a4 4 0 0 0-3-3.87" + Circle(9, 7, 4),
        ["hard-drive"] = Line(22, 12, 2, 12) + "M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z" + Line(6, 16, 6.01, 16) + Line(10, 16, 10.01, 16),
        ["tag"] = "M12.586 2.586A2 2 0 0 0 11.172 2H4a2 2 0 0 0-2 2v7.172a2 2 0 0 0 .586 1.414l8.704 8.704a2.426 2.426 0 0 0 3.42 0l6.58-6.58a2.426 2.426 0 0 0 0-3.42z" + Circle(7.5, 7.5, .5),
        ["clock"] = Circle(12, 12, 10) + "M12 6v6l4 2",
        ["database"] = "M3 5a9 3 0 1 0 18 0a9 3 0 1 0 -18 0M3 5V19A9 3 0 0 0 21 19V5M3 12A9 3 0 0 0 21 12",
        ["server"] = Rect(2, 2, 20, 8, 2) + Rect(2, 14, 20, 8, 2) + Line(6, 6, 6.01, 6) + Line(6, 18, 6.01, 18),
        ["triangle-alert"] = "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3M12 9v4M12 17h.01",
        ["circle-x"] = Circle(12, 12, 10) + "M15 9l-6 6M9 9l6 6",
        ["circle-check"] = Circle(12, 12, 10) + "M9 12l2 2 4-4",
        ["circle-slash"] = Circle(12, 12, 10) + Line(9, 15, 15, 9),
        ["circle-pause"] = Circle(12, 12, 10) + Line(10, 15, 10, 9) + Line(14, 15, 14, 9),
        ["scan-line"] = "M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2M7 12h10",
        ["square"] = Rect(3, 3, 18, 18, 2),
        ["library"] = "m16 6 4 14M12 6v14M8 8v12M4 4v16",
        ["play"] = "M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z",
        ["pause"] = Rect(14, 3, 5, 18, 1) + Rect(5, 3, 5, 18, 1),
        ["chevron-right"] = "m9 18 6-6-6-6",
        ["grip-vertical"] = Circle(9, 12, 1) + Circle(9, 5, 1) + Circle(9, 19, 1) + Circle(15, 12, 1) + Circle(15, 5, 1) + Circle(15, 19, 1),
        ["x"] = "M18 6 6 18m0-12 12 12",
        ["plus"] = "M5 12h14M12 5v14",
        ["refresh-cw"] = "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8M21 3v5h-5M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16M8 16H3v5",
        ["layout-dashboard"] = Rect(3, 3, 7, 9, 1) + Rect(14, 3, 7, 5, 1) + Rect(14, 12, 7, 9, 1) + Rect(3, 16, 7, 5, 1),
        ["loader-circle"] = "M21 12a9 9 0 1 1-6.219-8.56",
        ["info"] = Circle(12, 12, 10) + "M12 16v-4M12 8h.01",
        ["arrow-right"] = "M5 12h14m-7-7 7 7-7 7",
    };

    /// <summary>A 24-unit stroke icon scaled to <paramref name="size"/>, decorative unless named.</summary>
    public static FrameworkElement Icon(string name, double size, Color color, bool fill = false)
    {
        var data = IconPaths.TryGetValue(name, out var path) ? path : IconPaths["info"];
        var shape = (Path)XamlReader.Load(
            $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        shape.Stroke = B(color);
        if (fill) shape.Fill = B(color);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(shape);
        var box = new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(box, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return box;
    }

    public static bool HasIcon(string name) => IconPaths.ContainsKey(name);

    // ---- Containers ----------------------------------------------------

    public static StackPanel Row(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Column(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// <summary>shadcn Card: bg-card, 1px border, rounded-lg (12px), vertical padding 24.</summary>
    public static Border CardShell(UIElement child, Thickness? padding = null)
        => new()
        {
            Background = B(Card),
            BorderBrush = B(BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = padding ?? new Thickness(0, 24, 0, 24),
            Child = child,
        };

    public static FrameworkElement Skeleton(double height, double radius = 6, double? width = null)
    {
        var box = new SkeletonBox { Height = height, CornerRadius = new CornerRadius(radius) };
        if (width is { } w) box.Width = w;
        return box;
    }

    /// <summary>The WebUI SectionError: destructive, centered, py-4, text-sm.</summary>
    public static FrameworkElement SectionError(string message)
        => new Border
        {
            Padding = new Thickness(0, 16, 0, 16),
            Child = new TextBlock { Text = message, FontSize = 14, Foreground = B(Destructive), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.WrapWholeWords, HorizontalAlignment = HorizontalAlignment.Stretch },
        };

    /// <summary>The muted centered empty line (py-4 text-sm).</summary>
    public static FrameworkElement EmptyLine(string message)
        => new Border
        {
            Padding = new Thickness(0, 16, 0, 16),
            Child = new TextBlock { Text = message, FontSize = 14, Foreground = B(MutedForeground), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.WrapWholeWords },
        };

    public static Border Pill(string text, Color background, Color foreground, Color border, double size = 9, double radius = 4, FontWeight? weight = null, Thickness? padding = null)
        => new()
        {
            Background = B(background),
            BorderBrush = B(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Padding = padding ?? new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? Semibold, Foreground = B(foreground), LineHeight = size * 1.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis },
        };

    // CSS rounded-full clamps to half the arranged short edge. WinUI's
    // oversized CornerRadius otherwise produces an ellipse on short pills.
    public static T Stadium<T>(T element) where T : FrameworkElement
    {
        element.SizeChanged += (_, e) =>
        {
            var radius = new CornerRadius(Math.Max(0, Math.Min(e.NewSize.Width, e.NewSize.Height) / 2));
            if (element is Border border) border.CornerRadius = radius;
            else if (element is Control control) control.CornerRadius = radius;
        };
        return element;
    }

    // ---- Buttons -------------------------------------------------------

    private static void Paint(Button button, Color background, Color hover, Color pressed, Color foreground, Color hoverForeground, Color border, Color hoverBorder)
    {
        var resources = button.Resources;
        resources["ButtonBackground"] = B(background);
        resources["ButtonBackgroundPointerOver"] = B(hover);
        resources["ButtonBackgroundPressed"] = B(pressed);
        resources["ButtonBackgroundDisabled"] = B(background);
        resources["ButtonForeground"] = B(foreground);
        resources["ButtonForegroundPointerOver"] = B(hoverForeground);
        resources["ButtonForegroundPressed"] = B(hoverForeground);
        resources["ButtonForegroundDisabled"] = B(foreground, 0.5);
        resources["ButtonBorderBrush"] = B(border);
        resources["ButtonBorderBrushPointerOver"] = B(hoverBorder);
        resources["ButtonBorderBrushPressed"] = B(hoverBorder);
        resources["ButtonBorderBrushDisabled"] = B(border, 0.5);
        button.Background = B(background);
        button.Foreground = B(foreground);
        button.BorderBrush = B(border);
    }

    private static object ButtonContent(string? icon, string? text, double iconSize, Color color, double gap = 6, double fontSize = 14, FontWeight? weight = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = gap, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) row.Children.Add(Icon(icon, iconSize, color));
        if (text != null) row.Children.Add(new TextBlock { Text = text, FontSize = fontSize, FontWeight = weight ?? Medium, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>shadcn Button size sm (h-8, px-3/2.5 with icon, rounded-md 10px).</summary>
    public static Button OutlineButton(string text, string? icon, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Height = 32,
            Padding = new Thickness(icon != null ? 10 : 12, 0, 12, 0),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Content = ButtonContent(icon, text, 14, Foreground),
        };
        Paint(button, Colors.Transparent, Accent, Accent, Foreground, Res("ThemeAccentForegroundColor", "#E8E8EC"), BorderColor, BorderColor);
        button.Click += onClick;
        return button;
    }

    public static Button PrimaryButton(string text, string? icon, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Height = 32,
            Padding = new Thickness(icon != null ? 10 : 12, 0, 12, 0),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(0),
            Content = ButtonContent(icon, text, 14, PrimaryForeground),
        };
        Paint(button, Primary, Alpha(Primary, 0.9), Alpha(Primary, 0.9), PrimaryForeground, PrimaryForeground, Colors.Transparent, Colors.Transparent);
        button.Click += onClick;
        return button;
    }

    /// <summary>A chrome-less text button (the WebUI's muted "Manage ›" links); hover tints toward primary.</summary>
    public static Button TextLink(string text, double size, RoutedEventHandler onClick, Color? color = null)
    {
        var button = new Button
        {
            Padding = new Thickness(0),
            MinHeight = 0,
            MinWidth = 0,
            BorderThickness = new Thickness(0),
            Content = new TextBlock { Text = text, FontSize = size },
            VerticalAlignment = VerticalAlignment.Center,
        };
        Paint(button, Colors.Transparent, Colors.Transparent, Colors.Transparent, color ?? MutedForeground, Primary, Colors.Transparent, Colors.Transparent);
        button.Click += onClick;
        AutomationProperties.SetName(button, text);
        return button;
    }

    /// <summary>
    /// A "›" destination. Within the Monitor scope it navigates; outside it the
    /// text is shown as plain muted text so no dead or fake link is offered.
    /// </summary>
    public static FrameworkElement Destination(string text, double size, Action? navigate)
        => navigate == null
            ? Text(text, size, color: MutedForeground)
            : TextLink(text, size, (_, _) => navigate());

    public static Button IconButton(string icon, double size, double iconSize, string label, Color foreground, Color hoverForeground,
        Color background, Color hoverBackground, RoutedEventHandler onClick, double radius = 999)
    {
        var button = new Button
        {
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(0),
            Content = Icon(icon, iconSize, foreground),
        };
        Paint(button, background, hoverBackground, hoverBackground, foreground, hoverForeground, Colors.Transparent, Colors.Transparent);
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += onClick;
        return button;
    }

    /// <summary>
    /// The compact segmented control the range picker and the egress view toggle
    /// share: rounded-full, border/60, bg-muted/20, 10px semibold options.
    /// </summary>
    public static FrameworkElement Segmented<T>(IReadOnlyList<(T Value, string Label, string AccessibleName)> options, T selected, Action<T> onChange)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (value, label, name) in options)
        {
            var active = EqualityComparer<T>.Default.Equals(value, selected);
            var option = new ToggleButton(active, label, name);
            option.Click += (_, _) => onChange(value);
            row.Children.Add(option.Button);
        }
        return Stadium(new Border
        {
            BorderBrush = B(BorderColor, 0.6),
            BorderThickness = new Thickness(1),
            Background = B(Muted, 0.2),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(2),
            Child = row,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private sealed class ToggleButton
    {
        public readonly Button Button;
        public event RoutedEventHandler? Click;
        public ToggleButton(bool active, string label, string accessibleName)
        {
            Button = new Button
            {
                Padding = new Thickness(6, 2, 6, 2),
                MinHeight = 0,
                MinWidth = 0,
                CornerRadius = new CornerRadius(999),
                BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = label, FontSize = 10, FontWeight = Semibold, LineHeight = 10, LineStackingStrategy = LineStackingStrategy.BlockLineHeight },
            };
            Stadium(Button);
            if (active) Paint(Button, Alpha(Primary, 0.15), Alpha(Primary, 0.15), Alpha(Primary, 0.15), Foreground, Foreground, Alpha(Primary, 0.4), Alpha(Primary, 0.4));
            else Paint(Button, Colors.Transparent, Alpha(Muted, 0.6), Alpha(Muted, 0.6), MutedForeground, Foreground, Colors.Transparent, Colors.Transparent);
            AutomationProperties.SetName(Button, accessibleName);
            AutomationProperties.SetItemStatus(Button, active ? "pressed" : "");
            Button.Click += (sender, e) => Click?.Invoke(sender, e);
        }
    }
}
