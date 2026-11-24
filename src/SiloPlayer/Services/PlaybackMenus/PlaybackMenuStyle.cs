using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Controls;

namespace SiloPlayer.Services.PlaybackMenus;

/// <summary>
/// The official player's panel tokens and parts (web/src/styles/player-hud.css):
/// fixed colours that sit on a picture, plus the accent ("lamp") for what is chosen.
/// Sizes are the stylesheet's rem values at 16px.
/// </summary>
internal static class PlaybackMenuStyle
{
    internal const double PanelRadius = 16;

    internal static SolidColorBrush Rgb(byte alpha, byte red, byte green, byte blue) => new(Windows.UI.Color.FromArgb(alpha, red, green, blue));
    internal static Brush Paper => Rgb(255, 0xF5, 0xF3, 0xEF);
    internal static Brush Ash => Rgb(255, 0xBD, 0xBA, 0xB3);
    internal static Brush Ink => Rgb(255, 0x0E, 0x0E, 0x11);
    internal static Brush Panel => Rgb(255, 16, 16, 19);
    internal static Brush Hairline => Rgb(31, 255, 255, 255);
    internal static Brush Hover => Rgb(26, 255, 255, 255);
    internal static Brush Clear => Rgb(0, 0, 0, 0);

    /// <summary>The server accent, lightened until it reads on the panel; <paramref name="ink"/> is the text colour on it.</summary>
    internal static Brush Lamp(bool ink = false)
    {
        var color = Application.Current.Resources.TryGetValue("AccentBrush", out var value) && value is SolidColorBrush accent
            ? accent.Color : Windows.UI.Color.FromArgb(255, 0xE8, 0xB8, 0x6E);
        var rgb = SiloPlayer.Core.Services.PlayerHudAccent.OnDark($"{color.R:X2}{color.G:X2}{color.B:X2}");
        if (ink) rgb = SiloPlayer.Core.Services.PlayerHudAccent.LabelOn(rgb);
        var number = Convert.ToInt32(rgb, 16);
        return Rgb(255, (byte)(number >> 16), (byte)(number >> 8), (byte)number);
    }

    /// <summary>.hud-item-secondary: the smaller grey line under or beside a row's title.</summary>
    internal static TextBlock Secondary(string text, bool wrap = false) => new()
    {
        Text = text, FontSize = 12, Foreground = Ash, TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxLines = wrap ? 3 : 1,
    };

    /// <summary>A row label of a control line inside a panel ("Delay", "Timing", "Speed").</summary>
    internal static TextBlock Label(string text) => new() { Text = text, FontSize = 14, LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = Paper, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>.hud-chip: an outlined status tag ("Playing", "Default").</summary>
    internal static Border Chip(string text) => new()
    {
        CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 1), BorderThickness = new Thickness(1),
        BorderBrush = Rgb(46, 255, 255, 255), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, LineHeight = 17.6, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold, Foreground = Ash, CharacterSpacing = 20 },
    };

    /// <summary>.hud-panel-divider.</summary>
    internal static Border Divider() => new() { Height = 1, Margin = new Thickness(16, 6, 16, 6), Background = Hairline };

    /// <summary>.hud-panel-section: the small upper-case group label.</summary>
    internal static TextBlock Section(string text) => new()
    {
        Text = text.ToUpper(SiloPlayer.Core.Localization.AppStrings.FormatCulture), FontSize = 11, LineHeight = 16.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.Bold,
        CharacterSpacing = 100, Foreground = Ash, Margin = new Thickness(16, 12, 16, 4),
    };

    /// <summary>.hud-switch, drawn (the row is the control; this only shows its state).</summary>
    internal static FrameworkElement Switch(bool on)
    {
        var track = new Grid { Width = 34, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Rectangle { RadiusX = 10, RadiusY = 10, Fill = on ? Lamp() : Rgb(56, 255, 255, 255) });
        track.Children.Add(new Ellipse
        {
            Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(on ? 17 : 3, 0, 0, 0), Fill = on ? Lamp(ink: true) : Paper,
        });
        return track;
    }

    /// <summary>Gives a button the panel's own fills in every pointer state instead of the system button chrome.</summary>
    internal static void Paint(Button button, Brush rest, Brush hover, Brush text, Brush? hoverText = null)
    {
        button.Background = rest; button.Foreground = text; button.BorderThickness = new Thickness(0);
        button.Resources["ButtonBackground"] = rest;
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = hover;
        button.Resources["ButtonBackgroundDisabled"] = Rgb(10, 255, 255, 255);
        button.Resources["ButtonForeground"] = text;
        button.Resources["ButtonForegroundPointerOver"] = hoverText ?? text;
        button.Resources["ButtonForegroundPressed"] = hoverText ?? text;
        button.Resources["ButtonForegroundDisabled"] = Rgb(102, 0xF5, 0xF3, 0xEF);
        foreach (var key in new[] { "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled" })
            button.Resources[key] = Clear;
        // Keyboard focus: the accent ring with a dark halo, as on the web HUD.
        button.FocusVisualPrimaryBrush = Lamp(); button.FocusVisualSecondaryBrush = Rgb(153, 0, 0, 0);
    }

    /// <summary>.hud-mini-btn: a small inline action (delay −/+, Reset, Sync to audio).</summary>
    internal static Button Mini(Button button)
    {
        button.MinWidth = 36; button.Height = 36; button.Padding = new Thickness(8, 0, 8, 0); button.CornerRadius = new CornerRadius(8);
        button.FontSize = 13; button.FontWeight = FontWeights.SemiBold; button.VerticalAlignment = VerticalAlignment.Center;
        Paint(button, Rgb(20, 255, 255, 255), Rgb(41, 255, 255, 255), Paper);
        return button;
    }

    /// <summary>
    /// A tool row under a panel's choices, like .hud-item with .hud-item-icon. The
    /// button keeps its plain text label; the icon is drawn over its leading edge.
    /// </summary>
    internal static Grid ToolRow(string icon, Button button)
    {
        button.MinHeight = 40; button.Padding = new Thickness(42, 8, 10, 8); button.CornerRadius = new CornerRadius(10);
        button.FontSize = 15; button.FontWeight = FontWeights.Normal;
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
        Paint(button, Clear, Hover, Paper);
        var host = new Grid { Margin = new Thickness(6, 0, 6, 0) };
        host.Children.Add(button);
        host.Children.Add(new Border
        {
            Width = 20, Margin = new Thickness(10, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Child = WebUiIcon.Create(icon, 20, Ash, 1.75),
        });
        return host;
    }

    /// <summary>.hud-segmented: equal segments on a faint track, the chosen one filled.</summary>
    internal static Grid Segmented(IReadOnlyList<PlaybackMenuSegment> segments, Action<string> invoked, bool touchSheet)
    {
        var track = new Grid { ColumnSpacing = 2, Padding = new Thickness(3), CornerRadius = new CornerRadius(10), Background = Rgb(18, 255, 255, 255) };
        int columns = touchSheet ? 5 : segments.Count;
        for (var column = 0; column < columns; column++)
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        track.RowSpacing = 2;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (index % columns == 0) track.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var button = new Button
            {
                Content = segment.Label, Height = touchSheet ? 44 : 36, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 13, FontWeight = FontWeights.SemiBold,
                IsEnabled = segment.IsEnabled || segment.IsCurrent, IsTabStop = false,
            };
            if (segment.IsCurrent) Paint(button, Paper, Paper, Ink);
            else Paint(button, Clear, Hover, Ash, Paper);
            if (segment.IsCurrent) { button.Resources["ButtonBackgroundDisabled"] = Paper; button.Resources["ButtonForegroundDisabled"] = Ink; }
            AutomationProperties.SetName(button, segment.Name);
            var id = segment.Id; var enabled = segment.IsEnabled;
            button.Click += (_, _) => { if (enabled) invoked(id); };
            Grid.SetRow(button, index / columns); Grid.SetColumn(button, index % columns); track.Children.Add(button);
        }
        return track;
    }
}
