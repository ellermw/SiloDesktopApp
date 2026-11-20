using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services.Admin;
using Windows.UI;

namespace SiloPlayer.Controls;

/// <summary>
/// Shared drawing helpers for the Admin Monitor shell, indicator and Activity
/// page: theme brushes (read live so server themes apply), WebUI tint ratios,
/// Lucide 24-unit stroke icons and the small text/badge primitives the WebUI
/// composes with Tailwind classes. No animation helpers: pages swap in place.
/// </summary>
internal static class AdminMonitorUi
{
    public static Brush Brush(string key, Brush? fallback = null) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : fallback ?? new SolidColorBrush(Colors.Transparent);

    public static Color ColorOf(string key, Color fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) switch
        {
            true when value is Color color => color,
            true when value is SolidColorBrush brush => brush.Color,
            _ => fallback,
        };

    public static Color WithAlpha(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);

    public static SolidColorBrush Tint(string colorKey, double opacity, Color fallback) =>
        new(WithAlpha(ColorOf(colorKey, fallback), opacity));

    public static SolidColorBrush Solid(Color color, double opacity = 1) => new(WithAlpha(color, opacity));

    public static readonly Color Red500 = Color.FromArgb(255, 0xEF, 0x44, 0x44);
    public static readonly Color Emerald300 = Color.FromArgb(255, 0x6E, 0xE7, 0xB7);
    public static readonly Color Emerald400 = Color.FromArgb(255, 0x34, 0xD3, 0x99);
    public static readonly Color Amber300 = Color.FromArgb(255, 0xFC, 0xD3, 0x4D);
    public static readonly Color Amber400 = Color.FromArgb(255, 0xFB, 0xBF, 0x24);
    public static readonly Color Amber500 = Color.FromArgb(255, 0xF5, 0x9E, 0x0B);
    public static readonly Color Jellyfin = Color.FromArgb(255, 0xAA, 0x5C, 0xC3);
    public static readonly Color LiveRed = Color.FromArgb(255, 0xFF, 0x3C, 0x3C);
    public static readonly Color LiveText = Color.FromArgb(255, 0xF8, 0x71, 0x71);

    /// <summary>Tailwind tone: success/info/warning/destructive/muted-foreground.</summary>
    public static Color ToneColor(ActivityTone tone) => tone switch
    {
        ActivityTone.Success => ColorOf("SuccessBrush", Color.FromArgb(255, 0x22, 0xC5, 0x5E)),
        ActivityTone.Info => ColorOf("InfoBrush", Color.FromArgb(255, 0x60, 0xA5, 0xFA)),
        ActivityTone.Warning => ColorOf("WarningColor", Color.FromArgb(255, 0xD2, 0x99, 0x22)),
        ActivityTone.Destructive => ColorOf("ErrorColor", Color.FromArgb(255, 0xEF, 0x6B, 0x73)),
        _ => ColorOf("SecondaryTextColor", Color.FromArgb(255, 0x90, 0xA0, 0xB5)),
    };

    public static Color Primary => ColorOf("AccentColor", Color.FromArgb(255, 0x78, 0xAE, 0xFC));
    public static Color Muted => ColorOf("SecondaryTextColor", Color.FromArgb(255, 0x90, 0xA0, 0xB5));
    public static Color Foreground => ColorOf("PrimaryTextColor", Colors.White);
    public static Color Border => ColorOf("BorderColor", Color.FromArgb(255, 0x28, 0x38, 0x4D));
    public static Color Surface => ColorOf("SurfaceColor", Color.FromArgb(255, 0x15, 0x1E, 0x2B));
    public static Color Destructive => ColorOf("ErrorColor", Color.FromArgb(255, 0xEF, 0x6B, 0x73));
    public static Color Warning => ColorOf("WarningColor", Color.FromArgb(255, 0xD2, 0x99, 0x22));

    /// <summary>Lucide icon path data (24-unit grid, stroke 2, round caps).</summary>
    private static string IconPath(string name) => name switch
    {
        // lucide-icons/lucide 0.576.0 icons/settings-2.svg, circles expressed as native arcs.
        "settings-2" => "M14 17H5 M19 7h-9 M20 17A3 3 0 1 1 14 17A3 3 0 1 1 20 17Z M10 7A3 3 0 1 1 4 7A3 3 0 1 1 10 7Z",
        // Lucide 0.576.0 SVG geometry; absolute starts keep concatenated paths independent.
        // After converting an initial 'm' to 'M', retain implicit relative lines with 'l'.
        "library" => "M16 6l4 14 M12 6v14 M8 8v12 M4 4v16",
        "radar" => "M19.07 4.93A10 10 0 0 0 6.99 3.34 M4 6h.01 M2.29 9.62A10 10 0 1 0 21.31 8.35 M16.24 7.76A6 6 0 1 0 8.23 16.67 M12 18h.01 M17.99 11.66A6 6 0 0 1 15.77 16.67 M14 12A2 2 0 1 1 10 12A2 2 0 1 1 14 12Z M13.41 10.59l5.66-5.66",
        "send" => "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z M21.854 2.147l-10.94 10.939",
        "users" => "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M16 3.128a4 4 0 0 1 0 7.744 M22 21v-2a4 4 0 0 0-3-3.87 M13 7A4 4 0 1 1 5 7A4 4 0 1 1 13 7Z",
        "play-circle" or "circle-play" => "M9 9.003a1 1 0 0 1 1.517-.859l4.997 2.997a1 1 0 0 1 0 1.718l-4.997 2.997A1 1 0 0 1 9 14.996z M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12Z",
        "hard-drive-download" => "M12 2v8 M16 6l-4 4-4-4 M4 14H20A2 2 0 0 1 22 16V20A2 2 0 0 1 20 22H4A2 2 0 0 1 2 20V16A2 2 0 0 1 4 14Z M6 18h.01 M10 18h.01",
        "layout-panel-top" => "M4 3H20A1 1 0 0 1 21 4V9A1 1 0 0 1 20 10H4A1 1 0 0 1 3 9V4A1 1 0 0 1 4 3Z M4 14H9A1 1 0 0 1 10 15V20A1 1 0 0 1 9 21H4A1 1 0 0 1 3 20V15A1 1 0 0 1 4 14Z M15 14H20A1 1 0 0 1 21 15V20A1 1 0 0 1 20 21H15A1 1 0 0 1 14 20V15A1 1 0 0 1 15 14Z",
        "rows-2" => "M5 3H19A2 2 0 0 1 21 5V19A2 2 0 0 1 19 21H5A2 2 0 0 1 3 19V5A2 2 0 0 1 5 3Z M3 12h18",
        "captions" => "M5 5H19A2 2 0 0 1 21 7V17A2 2 0 0 1 19 19H5A2 2 0 0 1 3 17V7A2 2 0 0 1 5 5Z M7 15h4M15 15h2M7 11h2M13 11h4",
        "compass" => "M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12Z M16.24 7.76l-1.804 5.411a2 2 0 0 1-1.265 1.265L7.76 16.24l1.804-5.411a2 2 0 0 1 1.265-1.265z",
        "users-round" => "M18 21a8 8 0 0 0-16 0 M15 8A5 5 0 1 1 5 8A5 5 0 1 1 15 8Z M22 20c0-3.37-2-6.5-4-8a5 5 0 0 0-.45-8.3",
        "monitor-smartphone" => "M18 8V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h8 M10 19v-3.96 3.15 M7 19h5 M18 12H20A2 2 0 0 1 22 14V20A2 2 0 0 1 20 22H18A2 2 0 0 1 16 20V14A2 2 0 0 1 18 12Z",
        "download" => "M12 15V3 M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5",
        "history" => "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M12 7v5l4 2",
        "skip-forward" => "M21 4v16 M6.029 4.285A2 2 0 0 0 3 6v12a2 2 0 0 0 3.029 1.715l9.997-5.998a2 2 0 0 0 .003-3.432z",
        "server" => "M4 2H20A2 2 0 0 1 22 4V8A2 2 0 0 1 20 10H4A2 2 0 0 1 2 8V4A2 2 0 0 1 4 2Z M4 14H20A2 2 0 0 1 22 16V20A2 2 0 0 1 20 22H4A2 2 0 0 1 2 20V16A2 2 0 0 1 4 14Z M6 6L6.01 6 M6 18L6.01 18",
        "activity" => "M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2",
        "shield" => "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
        "layout-dashboard" => "M4 3h5a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M15 3h5a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M15 12h5a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1z M4 16h5a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-3a1 1 0 0 1 1-1z",
        "radio" => "M4.9 19.1C1 15.2 1 8.8 4.9 4.9 M7.8 16.2c-2.3-2.3-2.3-6.1 0-8.5 M14 12a2 2 0 1 1-4 0 2 2 0 0 1 4 0 M16.2 7.8c2.3 2.3 2.3 6.1 0 8.5 M19.1 4.9C23 8.8 23 15.1 19.1 19",
        "scroll-text" => "M15 12h-5 M15 8h-5 M19 17V5a2 2 0 0 0-2-2H4 M8 21h12a2 2 0 0 0 2-2v-1a1 1 0 0 0-1-1H11a1 1 0 0 0-1 1v1a2 2 0 1 1-4 0V5a2 2 0 1 0-4 0v2a1 1 0 0 0 1 1h3",
        "chevron-right" => "m9 18 6-6-6-6",
        "chevron-down" => "m6 9 6 6 6-6",
        "chevron-up" => "m18 15-6-6-6 6",
        "arrow-left" => "m12 19-7-7 7-7 M19 12H5",
        "search" => "M21 21l-4.34-4.34 M19 11a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
        "menu" => "M4 12h16 M4 6h16 M4 18h16",
        "x" => "M18 6 6 18 M6 6l12 12",
        "rotate-ccw" => "M3 11a9 9 0 1 1 2.5 6.2 M3 3v8h8",
        "refresh-cw" => "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5",
        "pencil" => "M17 3a2.83 2.83 0 0 1 4 4L7.5 20.5 2 22l1.5-5.5z M15 5l4 4",
        "trash-2" => "M3 6h18 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M10 11v6 M14 11v6",
        "circle-pause" => "M22 12A10 10 0 1 1 2 12A10 10 0 1 1 22 12Z M10 9v6 M14 9v6",
        "scan-search" => "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M16 11A5 5 0 1 1 6 11A5 5 0 1 1 16 11Z M15 15l3 3",
        "terminal" => "M4 17l6-6-6-6 M12 19h8",
        "octagon-alert" => "M12 16h.01 M12 8v4 M15.312 2a2 2 0 0 1 1.414.586l4.688 4.688A2 2 0 0 1 22 8.688v6.624a2 2 0 0 1-.586 1.414l-4.688 4.688a2 2 0 0 1-1.414.586H8.688a2 2 0 0 1-1.414-.586l-4.688-4.688A2 2 0 0 1 2 15.312V8.688a2 2 0 0 1 .586-1.414l4.688-4.688A2 2 0 0 1 8.688 2z",
        "more-horizontal" => "M13 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M20 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0 M6 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0",
        "message-square" => "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z",
        "pause" => "M15 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z M7 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z",
        "play" => "M6 3l14 9-14 9z",
        "square" => "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z",
        "filter" => "M22 3H2l8 9.46V19l4 2v-8.54z",
        "file-text" => "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7z M14 2v4a2 2 0 0 0 2 2h4 M10 9H8 M16 13H8 M16 17H8",
        // Lucide Loader2 / LoaderCircle and Network (0.576.0; rects written as rounded paths).
        "loader-circle" => "M21 12a9 9 0 1 1-6.219-8.56",
        "network" => "M17 16h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z M3 16h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z M10 2h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3 M12 12V8",
        "loader" => "M12 2v4 M16.2 7.8l2.9-2.9 M18 12h4 M16.2 16.2l2.9 2.9 M12 18v4 M4.9 19.1l2.9-2.9 M2 12h4 M4.9 4.9l2.9 2.9",
        "scan-line" => "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10",
        "clock" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M12 6v6l4 2",
        "alert-triangle" => "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3 M12 9v4 M12 17h.01",
        _ => "M12 5v14 M5 12h14",
    };

    public static Viewbox Icon(string name, double size, Brush foreground, double strokeThickness = 2)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load(
            $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{IconPath(name)}' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = foreground;
        path.StrokeThickness = strokeThickness;
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    public static Viewbox Icon(string name, double size, Color color) => Icon(name, size, Solid(color));

    /// <summary>A spinning Lucide Loader (WebUI animate-spin): rotation only, no entrance motion.</summary>
    public static FrameworkElement Spinner(double size, Color color, string iconName = "loader")
    {
        var icon = Icon(iconName, size, color);
        var transform = new RotateTransform { CenterX = size / 2, CenterY = size / 2 };
        icon.RenderTransform = transform;
        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0, To = 360, Duration = new Duration(TimeSpan.FromSeconds(1)),
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, transform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Angle");
        story.Children.Add(animation);
        icon.Loaded += (_, _) => story.Begin();
        icon.Unloaded += (_, _) => story.Stop();
        return icon;
    }

    public static TextBlock Text(string text, double size, Color color, Windows.UI.Text.FontWeight? weight = null, bool trim = true) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Solid(color),
        FontWeight = weight ?? FontWeights.Normal,
        TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        TextWrapping = trim ? TextWrapping.NoWrap : TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Uppercase tracked label (10px semibold, tracking 0.12-0.2em).</summary>
    public static TextBlock Eyebrow(string text, Color color, int characterSpacing = 120, double size = 10) => new()
    {
        Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        CharacterSpacing = characterSpacing,
        Foreground = Solid(color),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Tinted rounded badge (WebUI "rounded border px-1.5 py-0.5 text-[9px] font-semibold" family).</summary>
    public static Border Badge(string text, Color tone, double fontSize = 9, double backgroundAlpha = 0.10, double borderAlpha = 0.15,
        Color? foreground = null, double radius = 4)
        => new()
        {
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            BorderBrush = Solid(tone, borderAlpha),
            Background = Solid(tone, backgroundAlpha),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text, FontSize = fontSize, FontWeight = FontWeights.SemiBold, Foreground = Solid(foreground ?? tone),
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };

    /// <summary>The WebUI <c>.live-badge</c>: 10px bold, red tint, radius 6.</summary>
    public static Border LiveBadge(string text, bool withRadioIcon)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (withRadioIcon) content.Children.Add(Icon("radio", 12, LiveText));
        content.Children.Add(new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Solid(LiveText) });
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 2),
            Background = Solid(LiveRed, 0.12),
            BorderBrush = Solid(LiveRed, 0.20),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = content,
        };
    }

    /// <summary>A transparent, chrome-less button around arbitrary content (links, chips, rows).</summary>
    public static Button Plain(UIElement content, string? automationName = null, Thickness? padding = null)
    {
        var button = new Button
        {
            Content = content,
            Padding = padding ?? new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (Application.Current.Resources.TryGetValue("GhostButtonStyle", out var style) && style is Style ghost)
        {
            button.Style = ghost;
            button.Padding = padding ?? new Thickness(0);
            button.MinHeight = 0;
        }
        if (automationName != null) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationName);
        return button;
    }

    /// <summary>The WebUI outline small button (h-8, 12px, rounded-md) with an optional icon.</summary>
    public static Button Outline(string label, string? icon = null, Color? tone = null, double height = 32, double fontSize = 12)
    {
        var color = tone ?? Foreground;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) content.Children.Add(Icon(icon, 14, color));
        content.Children.Add(new TextBlock { Text = label, FontSize = fontSize, FontWeight = FontWeights.Medium, Foreground = Solid(color), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Content = content,
            Height = height,
            MinHeight = 0,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
        };
        if (Application.Current.Resources.TryGetValue("OutlineButtonStyle", out var style) && style is Style outline)
        {
            button.Style = outline;
            button.Height = height;
            button.MinHeight = 0;
            button.Padding = new Thickness(10, 0, 10, 0);
            button.CornerRadius = new CornerRadius(6);
        }
        if (tone is { } t) button.BorderBrush = Solid(t, 0.40);
        return button;
    }

    /// <summary>Monospace family for build numbers, IPs and console rows.</summary>
    public static FontFamily Mono { get; } = new("Cascadia Mono, Consolas");

    /// <summary>The surface-panel look: rounded-2xl, bg-surface, hairline border.</summary>
    public static Border Panel(UIElement child, Thickness padding, double radius = 16) => new()
    {
        CornerRadius = new CornerRadius(radius),
        Background = Solid(Surface, 0.70),
        BorderBrush = Solid(Border, 0.60),
        BorderThickness = new Thickness(1),
        Padding = padding,
        Child = child,
    };
}
