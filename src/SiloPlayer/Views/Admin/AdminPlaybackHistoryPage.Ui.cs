using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Small native primitives shared by the Playback History and Skip Markers
/// pages (WebUI Badge, Skeleton, surface-panel Card, text links). Theme brushes
/// are read from the app resources so light, dark and contrast themes apply;
/// nothing animates (pages swap in place, skeletons do not pulse).
/// </summary>
internal static class AdminPlaybackAuditUi
{
    internal static string T(string key, params (string, object?)[] values) => AdminPlaybackAuditText.T(key, values);

    internal enum BadgeKind { Default, Secondary, Outline }

    /// <summary>shadcn Badge: default (primary fill), secondary (muted fill) or outline.</summary>
    internal static Border Badge(string text, BadgeKind kind)
    {
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        label.Foreground = Brush(kind == BadgeKind.Default ? "AccentForegroundBrush" : "PrimaryTextBrush");
        return new Border
        {
            Child = label, CornerRadius = new(8), Padding = new(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left,
            Background = kind switch { BadgeKind.Default => Brush("AccentBrush"), BadgeKind.Secondary => Brush("MutedBrush"), _ => new SolidColorBrush(Microsoft.UI.Colors.Transparent) },
            BorderBrush = Brush("BorderBrush"), BorderThickness = new(kind == BadgeKind.Outline ? 1 : 0),
        };
    }

    internal static Border Skeleton(double height) => new()
    {
        Height = height, CornerRadius = new(8), Background = Tint("MutedBrush", 0.6), HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>A text link (underlines on hover via the HyperlinkButton template); invokes <paramref name="open"/>.</summary>
    internal static HyperlinkButton Link(UIElement content, string accessibleName, string automationId, Action open)
    {
        var link = new HyperlinkButton { Content = content, Padding = new(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(link, accessibleName);
        AutomationProperties.SetAutomationId(link, automationId);
        link.Click += (_, _) => open();
        return link;
    }

    internal static TextBlock Strong(string text, double size = 14)
    {
        var block = Text(text, size);
        block.FontWeight = FontWeights.Medium;
        return block;
    }

    /// <summary>The WebUI <c>surface-panel rounded-2xl</c> card.</summary>
    internal static Border Card(UIElement child, double padding = 24) => new()
    {
        Child = child, CornerRadius = new(16), Padding = new(padding), Background = Tint("SurfaceBrush", 0.9), BorderBrush = Tint("BorderBrush", 0.5), BorderThickness = new(1),
    };

    /// <summary>A polite live region for status and error text.</summary>
    internal static TextBlock Status(string automationId, double size = 12, bool muted = true)
    {
        var block = Text("", size, muted);
        AutomationProperties.SetAutomationId(block, automationId);
        AutomationProperties.SetLiveSetting(block, AutomationLiveSetting.Polite);
        return block;
    }

    /// <summary>A ComboBox with the given options; its accessible name is <paramref name="label"/>.</summary>
    internal static ComboBox Picker(string automationId, string label, double width)
    {
        var select = new ComboBox
        {
            Width = width, Height = 36, FontSize = 14, CornerRadius = new(10), Background = Brush("AppBackgroundBrush"), BorderBrush = Brush("BorderBrush"),
            Padding = new(12, 5, 12, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(select, automationId);
        AutomationProperties.SetName(select, label);
        return select;
    }

    internal static void SetOptions(ComboBox select, IEnumerable<(string Value, string Label)> options, string selected)
    {
        select.Items.Clear();
        foreach (var (value, label) in options) select.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        Select(select, selected);
    }

    internal static void Select(ComboBox select, string value)
    {
        var match = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
        select.SelectedItem = match ?? select.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    internal static string Selected(ComboBox select) => (select.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    internal static void SetHeading(TextBlock block) => AutomationProperties.SetHeadingLevel(block, AutomationHeadingLevel.Level2);

    /// <summary>The date and time display preference, or the server value when it is not an instant.</summary>
    internal static string FormatDateTime(string value) =>
        AdminPlaybackAuditText.TryParseInstant(value, out var instant) ? SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(instant) : value;
}
