using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Small building blocks of the Plugins pages. Text that comes from plugins, repositories or the server is always put
/// into <see cref="Run"/> and <see cref="TextBlock"/> text, never parsed as markup; a link opens only through
/// <see cref="AdminPluginPresentation.SafeLink"/> and the page's launcher.
/// </summary>
internal static class AdminPluginsUi
{
    internal static Brush ToneBrush(AdminPluginTone tone) => tone switch
    {
        AdminPluginTone.Success => AdminMonitorUi.Solid(AdminMonitorUi.Emerald400),
        AdminPluginTone.Warning => AdminMonitorUi.Solid(AdminMonitorUi.Warning),
        AdminPluginTone.Destructive => AdminMonitorUi.Solid(AdminMonitorUi.Destructive),
        _ => Brush("SecondaryTextBrush"),
    };

    /// <summary>The two-letter square that stands in for a plugin logo (manifests ship no icons).</summary>
    internal static FrameworkElement Monogram(string name, double size = 36)
    {
        var letters = new string(name.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
        var text = Text(letters.Length == 0 ? "??" : letters, size >= 48 ? 16 : 12.5);
        text.FontWeight = FontWeights.SemiBold; text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
        var square = new Border { Width = size, Height = size, CornerRadius = new(size >= 48 ? 12 : 8), Background = Tint("MutedBrush", 0.9), Child = text };
        AutomationProperties.SetAccessibilityView(square, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return square;
    }

    /// <summary>A status dot and label. The title (a runtime error, for instance) is a tooltip, shown redacted and bounded.</summary>
    internal static FrameworkElement StatusLabel(AdminPluginStatus status) => StatusLabel(status.Label, status.Tone, status.Title, status.Colored);

    internal static FrameworkElement StatusLabel(string label, AdminPluginTone tone, string? title = null, bool colored = false)
    {
        var row = Inline(6);
        row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = ToneBrush(tone), VerticalAlignment = VerticalAlignment.Center });
        var text = Text(label, 13); text.TextWrapping = TextWrapping.NoWrap;
        if (colored) text.Foreground = ToneBrush(tone);
        row.Children.Add(text);
        if (!string.IsNullOrEmpty(title)) ToolTipService.SetToolTip(row, AdminPluginPresentation.Redact(title, 400));
        AutomationProperties.SetName(row, label);
        return row;
    }

    /// <summary>A message with bold and link runs as one text block. The link runs call <paramref name="onLink"/>.</summary>
    internal static TextBlock Rich(IReadOnlyList<AdminPluginsTextRun> runs, Action? onLink = null, double size = 14, bool muted = false)
    {
        var block = Text("", size, muted); block.IsTextSelectionEnabled = true;
        foreach (var piece in runs)
        {
            var run = new Run { Text = piece.Text };
            if (piece.Bold) run.FontWeight = FontWeights.SemiBold;
            if (piece.Link && onLink != null)
            {
                var link = new Hyperlink(); link.Inlines.Add(run); link.Click += (_, _) => onLink(); block.Inlines.Add(link);
            }
            else block.Inlines.Add(run);
        }
        return block;
    }

    /// <summary>A button that opens one safe link through the launcher; nothing is shown at all for an unsafe URL.</summary>
    internal static FrameworkElement? Link(string label, string? url, Func<Uri, Task<bool>> launcher, string id)
    {
        if (AdminPluginPresentation.SafeLink(url) is not { } uri) return null;
        var link = new HyperlinkButton { Content = label, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 24 };
        link.Click += async (_, _) => { try { await launcher(uri); } catch (Exception) { /* opening a browser is best effort */ } };
        AutomationProperties.SetName(link, label); AutomationProperties.SetAutomationId(link, id);
        ToolTipService.SetToolTip(link, uri.GetLeftPart(UriPartial.Path));
        return link;
    }

    /// <summary>Plugin Markdown as native text: paragraphs, lists, strong, emphasis, code and safe links only.</summary>
    internal static FrameworkElement Markdown(string? source, Func<Uri, Task<bool>> launcher)
    {
        var body = Stack(10);
        foreach (var block in AdminPluginMarkdown.Parse(source))
        {
            if (!block.IsList) { body.Children.Add(Inlines(block.Items[0], launcher)); continue; }
            var list = Stack(4);
            for (var index = 0; index < block.Items.Count; index++)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                var marker = Text(block.Ordered ? (index + 1) + "." : "•", 13, true); marker.HorizontalAlignment = HorizontalAlignment.Right;
                var content = Inlines(block.Items[index], launcher); Grid.SetColumn(content, 1);
                row.Children.Add(marker); row.Children.Add(content); list.Children.Add(row);
            }
            body.Children.Add(list);
        }
        return body;
    }

    private static TextBlock Inlines(IReadOnlyList<AdminPluginInline> inlines, Func<Uri, Task<bool>> launcher)
    {
        var block = Text("", 13.5); block.IsTextSelectionEnabled = true;
        foreach (var inline in inlines)
        {
            var run = new Run { Text = inline.Text };
            switch (inline.Kind)
            {
                case AdminPluginInlineKind.Strong: run.FontWeight = FontWeights.SemiBold; block.Inlines.Add(run); break;
                case AdminPluginInlineKind.Emphasis: run.FontStyle = Windows.UI.Text.FontStyle.Italic; block.Inlines.Add(run); break;
                case AdminPluginInlineKind.Code: run.FontFamily = new FontFamily("Consolas"); block.Inlines.Add(run); break;
                case AdminPluginInlineKind.Link when AdminPluginPresentation.SafeLink(inline.Href) is { } uri:
                    var link = new Hyperlink(); link.Inlines.Add(run);
                    link.Click += async (_, _) => { try { await launcher(uri); } catch (Exception) { /* best effort */ } };
                    block.Inlines.Add(link); break;
                default: block.Inlines.Add(run); break;
            }
        }
        return block;
    }

    /// <summary>A bordered banner: warnings assert themselves to a screen reader, notes are polite.</summary>
    internal static Border Banner(UIElement content, bool warning)
    {
        var border = new Border
        {
            Child = content, CornerRadius = new(12), Padding = new(16, 12, 16, 12), BorderThickness = new(1),
            Background = warning ? AdminMonitorUi.Solid(AdminMonitorUi.Warning, 0.10) : Tint("SurfaceBrush", 0.9),
            BorderBrush = warning ? AdminMonitorUi.Solid(AdminMonitorUi.Warning, 0.30) : Tint("BorderBrush", 0.72),
        };
        AutomationProperties.SetLiveSetting(border, warning ? Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive : Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        return border;
    }

    internal static TextBlock Heading(string title, int level = 2, double size = 16)
    {
        var heading = Text(title, level == 1 ? 30 : size); heading.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(heading, level == 1 ? Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1 : Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        return heading;
    }

    /// <summary>A titled card (official DetailPanel): title, optional description, optional badge text, body.</summary>
    internal static StackPanel Card(StackPanel parent, string title, string? description = null, string? badge = null, string? id = null)
    {
        var body = Stack(12);
        var header = new LogsWrapPanel { Spacing = 8 };
        header.Children.Add(Heading(title));
        if (!string.IsNullOrEmpty(badge)) header.Children.Add(Panel(Text(badge, 11), 6, 4, border: true));
        body.Children.Add(header);
        if (!string.IsNullOrEmpty(description)) body.Children.Add(Text(description, 13, true));
        var card = Panel(body, 16, 20, border: true);
        if (id != null) AutomationProperties.SetAutomationId(card, id);
        parent.Children.Add(card);
        return body;
    }

    internal static Button Command(string text, string id, Action action, bool primary = false)
    {
        var button = AdminSettingsUi.Action(text, id, primary); button.Click += (_, _) => action(); return button;
    }
}
