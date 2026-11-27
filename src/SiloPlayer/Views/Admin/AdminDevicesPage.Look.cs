using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>The small pieces of the official device detail that more than one part of it draws: its accent, its uppercase labels and its pills.</summary>
public sealed partial class AdminDevicesPage
{
    /// <summary>The official <c>profileAccent</c>: <c>hsl(hue 70% 62%)</c> for the hue the profile id hashes to.</summary>
    internal static Windows.UI.Color AccentColor(string profileId, byte alpha = 255)
    {
        double hue = AdminDevicePresentation.ProfileAccentHue(profileId), s = .70, l = .62;
        var c = (1 - Math.Abs(2 * l - 1)) * s; var h = hue / 60; var x = c * (1 - Math.Abs(h % 2 - 1)); var m = l - c / 2;
        var (r, g, b) = h switch { < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x), < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x) };
        byte Channel(double value) => (byte)Math.Round((value + m) * 255);
        return Windows.UI.Color.FromArgb(alpha, Channel(r), Channel(g), Channel(b));
    }
    private static SolidColorBrush AccentBrush(string profileId, double alpha = 1) => new(AccentColor(profileId, (byte)Math.Round(255 * alpha)));

    /// <summary>The official small-caps label: uppercase in the interface language, with tracking in thousandths of an em.</summary>
    private static TextBlock Caps(string text, double size, int tracking, Windows.UI.Text.FontWeight weight, bool muted = true, double lineHeight = 0)
    {
        var block = Text(AppStrings.Uppercase(text), size, muted); block.FontWeight = weight; block.CharacterSpacing = tracking;
        if (lineHeight > 0) block.LineHeight = lineHeight;
        return block;
    }

    /// <summary>The official rounded pill: <c>rounded-full border px-1.5 py-px text-[0.59375rem] font-medium tracking-[0.05em] uppercase</c>.</summary>
    private static Border DevicePill(string text, bool warning = false)
    {
        var label = Caps(text, 9.5, 50, FontWeights.Medium, lineHeight: 14);
        var badge = new Border
        {
            Child = warning ? DeviceSettingWarning(label, 10) : label,
            Padding = new(6, 1, 6, 1),
            BorderThickness = new(1),
            BorderBrush = warning ? AdminMonitorUi.Solid(AdminMonitorUi.Destructive, .3) : LogsNativeUi.Tint("BorderBrush", .5),
            Background = warning ? AdminMonitorUi.Solid(AdminMonitorUi.Destructive, .1) : null,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return SiloPlayer.Helpers.RoundedBadge.Apply(badge, 10);
    }

    /// <summary>The official override marker: a 6 px dot at 80% of the text colour; its title and accessible name say the setting is overridden.</summary>
    private static Ellipse OverrideDot(string title, string name)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Fill = Brush("PrimaryTextBrush"), Opacity = .8, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(dot, title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dot, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dot, "DeviceOverrideDot");
        return dot;
    }
}
