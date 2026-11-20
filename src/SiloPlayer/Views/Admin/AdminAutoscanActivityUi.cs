using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using A = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityText;
using D = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityDisplay;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Controls shared by the Autoscan Activity page and its inspector sheet: the official badges (<c>PollActivityBadges.tsx</c>
/// at 88b825d57), the <c>TablePagination</c> bar and the table frame, built from the Admin Monitor native primitives.
/// </summary>
internal static class AutoscanUi
{
    // Tailwind 500 tones the official badges use (emerald, sky, amber).
    internal static readonly Windows.UI.Color Emerald = Windows.UI.Color.FromArgb(255, 16, 185, 129);
    internal static readonly Windows.UI.Color Sky = Windows.UI.Color.FromArgb(255, 14, 165, 233);
    internal static readonly Windows.UI.Color Amber = Windows.UI.Color.FromArgb(255, 245, 158, 11);
    internal static Windows.UI.Color Destructive => Brush("ErrorBrush") is SolidColorBrush error ? error.Color : Windows.UI.Color.FromArgb(255, 239, 107, 115);
    internal static Windows.UI.Color Primary => Brush("AccentBrush") is SolidColorBrush accent ? accent.Color : Sky;
    internal static Windows.UI.Color Muted => Brush("SecondaryTextBrush") is SolidColorBrush text ? text.Color : Windows.UI.Color.FromArgb(255, 144, 160, 181);

    internal static SolidColorBrush Solid(Windows.UI.Color color, double alpha = 1) =>
        new(Windows.UI.Color.FromArgb((byte)Math.Round(color.A * alpha), color.R, color.G, color.B));

    internal static Windows.UI.Color Of(AdminAutoscanTone tone) => tone switch
    {
        AdminAutoscanTone.Warning => Amber, AdminAutoscanTone.Danger => Destructive, _ => Muted
    };

    internal static TextBlock Toned(TextBlock text, AdminAutoscanTone tone)
    {
        if (tone == AdminAutoscanTone.Normal) text.Foreground = Brush("PrimaryTextBrush");
        else if (tone != AdminAutoscanTone.Muted) text.Foreground = Solid(Of(tone));
        return text;
    }

    /// <summary>Text through the credential redactor, as every dynamic string on this page is shown.</summary>
    internal static TextBlock SafeText(string? text, double size = 14, bool muted = false, bool mono = false, bool selectable = false)
    {
        var block = Text(A.Safe(text), size, muted, mono);
        if (selectable) block.IsTextSelectionEnabled = true;
        return block;
    }

    internal static UIElement Glyph(string name, double size = 16, Brush? foreground = null)
    {
        var data = name switch
        {
            "chevron-left" => "m15 18-6-6 6-6",
            "chevron-right" => "m9 18 6-6-6-6",
            "chevrons-left" => "m11 17-5-5 5-5 m18 17-5-5 5-5",
            "chevrons-right" => "m6 17 5-5-5-5 m13 17 5-5-5-5",
            "square" => "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z",
            "scan-line" => "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10",
            "clock" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M12 6v6l4 2",
            "history" => "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5 M12 7v5l4 2",
            "list-checks" => "m3 17 2 2 4-4 m3 7 2 2 4-4 M13 6h8 M13 12h8 M13 18h8",
            "refresh-cw" => "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5",
            "search" => "M21 21l-4.34-4.34 M19 11a8 8 0 1 1-16 0 8 8 0 0 1 16 0",
            "x" => "M18 6 6 18 M6 6l12 12",
            "triangle-alert" => "m10.3 3.86-8.48 14.14A2 2 0 0 0 3.53 21h16.94a2 2 0 0 0 1.71-3L13.7 3.86a2 2 0 0 0-3.4 0 M12 9v4 M12 17h.01",
            "circle-check" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M8 12l3 3 5-6",
            _ => "M12 5v14 M5 12h14"
        };
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data +
            "' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = foreground ?? Brush("PrimaryTextBrush");
        var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>The official <c>formatTimestamp</c>: short date and time in the saved date and time preferences; a dash when unknown.</summary>
    internal static string Timestamp(DateTimeOffset? value) => value == null ? D.NoValue : SiloPlayer.Helpers.DateTimeDisplay.FormatShortDateTime(value.Value);

    /// <summary>The official <c>formatTime</c>: the time of day in the saved time preference; a dash when unknown.</summary>
    internal static string ClockTime(DateTimeOffset? value) => value == null ? D.NoValue : SiloPlayer.Helpers.DateTimeDisplay.FormatTime(value.Value);

    internal static Border Card(UIElement content, double padding = 16) => new()
    {
        Child = content, Padding = new(padding), CornerRadius = new(8), BorderBrush = Brush("BorderBrush"), BorderThickness = new(1), Background = Brush("SurfaceBrush")
    };

    internal static Border Dashed(UIElement content, double padding = 24) => new()
    {
        Child = content, Padding = new(padding), CornerRadius = new(8), BorderBrush = Brush("BorderBrush"), BorderThickness = new(1), Background = Tint("MutedBrush", 0.20)
    };

    /// <summary>The official Badge: rounded, tinted border and background, optional leading icon.</summary>
    internal static Border ToneBadge(string label, Windows.UI.Color color, string? icon = null, bool secondary = false)
    {
        var row = Inline(4); row.VerticalAlignment = VerticalAlignment.Center;
        if (icon != null) row.Children.Add(Glyph(icon, 14, Solid(color)));
        var text = Text(A.Safe(label), 12); text.Foreground = secondary ? Brush("PrimaryTextBrush") : Solid(color); text.FontWeight = FontWeights.Medium; text.LineHeight = 16;
        row.Children.Add(text);
        return new Border
        {
            Child = row, BorderThickness = new(1), CornerRadius = new(6), Padding = new(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = secondary ? Brush("BorderBrush") : Solid(color, 0.30), Background = secondary ? Brush("SecondaryBackgroundBrush") : Solid(color, 0.10)
        };
    }

    internal static Border PollStatusBadge(string status)
    {
        var (color, icon) = status switch
        {
            "success" => (Emerald, "circle-check"), "running" => (Primary, "refresh-cw"), "unresolved" => (Amber, "triangle-alert"), _ => (Destructive, "triangle-alert")
        };
        var label = status is "running" or "success" or "unresolved" or "error" ? A.T("pollStatus." + status) : status;
        var badge = ToneBadge(label, color, icon);
        AutomationProperties.SetName(badge, label);
        return badge;
    }

    internal static Border ScanStatusBadge(string status)
    {
        var color = status switch
        {
            "completed" => Emerald, "failed" => Destructive, "running" => Sky, "cancelled" => Amber, _ => Muted
        };
        var badge = ToneBadge(A.ScanStatus(status), color);
        AutomationProperties.SetName(badge, A.ScanStatus(status));
        return badge;
    }

    internal static UIElement? DeliveryBadge(Core.Models.Admin.AdminAutoscanActivityEvent poll)
    {
        if (poll.DeliveryMode != "webhook") return null;
        return ToneBadge(string.IsNullOrEmpty(poll.ProviderEventType) ? A.T("delivery.webhook") : A.T("delivery.webhookEvent", new { event_type = poll.ProviderEventType }), Muted, secondary: true);
    }

    internal static Border OutcomeBadge(string outcome)
    {
        var known = D.IsKnownOutcome(outcome);
        var color = outcome switch { "queued" => Emerald, "joined" => Sky, "unresolved" => Amber, "error" => Destructive, _ => Muted };
        return ToneBadge(known ? A.T("outcome." + outcome) : outcome, color);
    }

    internal static TextBlock ResultText(Core.Models.Admin.AdminAutoscanActivityResult? result, double size = 12)
    {
        var summary = D.SummarizeResult(result);
        var text = SafeText(summary?.Text ?? "", size);
        return Toned(text, summary?.Tone ?? AdminAutoscanTone.Muted);
    }

    /// <summary>A path with its parent prefix muted and its last segment emphasized, as one selectable text.</summary>
    internal static TextBlock PathDisplay(string? path, double size = 12)
    {
        var (dir, leaf) = D.SplitDisplayPath(path);
        var block = new TextBlock { FontSize = size, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, LineHeight = size + 6, IsTextSelectionEnabled = true };
        if (dir.Length > 0) block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = A.Safe(dir), Foreground = Brush("SecondaryTextBrush") });
        block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = A.Safe(leaf), Foreground = Brush("PrimaryTextBrush"), FontWeight = FontWeights.Medium });
        return block;
    }

    /// <summary>The poll metric strip: inline text, or bordered pills inside the inspector.</summary>
    internal static FrameworkElement MetricStrip(Core.Models.Admin.AdminAutoscanActivityEvent poll, bool pills)
    {
        var items = new (string Key, long Count, Windows.UI.Color? Tone, bool Strong)[]
        {
            ("changes", poll.ChangesReturned, null, true), ("targets", poll.TargetsClaimed, null, false), ("created", poll.ScansCreated, Emerald, true),
            ("reused", poll.ScansReused, Sky, false), ("suppressed", poll.ScansSuppressed, Amber, false),
        };
        var wrap = new WrapPanel { HorizontalSpacing = pills ? 6 : 12, VerticalSpacing = pills ? 6 : 2 };
        foreach (var (key, count, tone, strong) in items)
        {
            var label = Text(A.RawCount("metrics." + key, count), 12); label.LineHeight = 16;
            label.Foreground = count == 0 ? Tint("SecondaryTextBrush", 0.6) : tone is { } color ? Solid(color) : strong ? Brush("PrimaryTextBrush") : Brush("PrimaryTextBrush");
            if (count > 0 && strong) label.FontWeight = FontWeights.Medium;
            if (!pills) { wrap.Children.Add(label); continue; }
            wrap.Children.Add(new Border
            {
                Child = label, BorderThickness = new(1), CornerRadius = new(6), Padding = new(8, 2, 8, 2),
                BorderBrush = count > 0 && tone is { } border ? Solid(border, 0.30) : Tint("BorderBrush", count > 0 ? 1 : 0.5),
                Background = count > 0 ? tone is { } fill ? Solid(fill, 0.10) : Tint("MutedBrush", 0.4) : null
            });
        }
        return wrap;
    }

    internal static Button IconButton(string icon, string name, string id, Action click, bool enabled = true, bool ghost = false, double size = 32, Windows.UI.Color? tint = null)
    {
        var button = Button("", id, ghost ? "GhostButtonStyle" : "OutlineButtonStyle");
        button.Content = Glyph(icon, 14, tint is { } color ? Solid(color) : null);
        button.Width = size; button.Height = size; button.MinWidth = size; button.MinHeight = size; button.Padding = new(0); button.IsEnabled = enabled;
        button.CornerRadius = new(6);
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The official <c>TablePagination</c>: summary, optional rows selector and first/previous/numbered/next/last buttons (hidden at one page).</summary>
    internal static FrameworkElement Pagination(string id, long total, int page, int pageSize, Action<int> goTo, Action<int>? setSize, IReadOnlyList<int> sizes,
        string? summaryKey, bool fetching = false, bool phone = false, int maxPages = int.MaxValue)
    {
        var pageCount = (int)Math.Min(maxPages, Math.Max(1, (total + pageSize - 1) / pageSize));
        var safe = Math.Min(Math.Max(page, 0), pageCount - 1);
        var from = (long)safe * pageSize + 1; var to = Math.Min((long)(safe + 1) * pageSize, total);
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var left = new WrapPanel { HorizontalSpacing = 16, VerticalSpacing = 8 };
        var summary = Text(A.T(summaryKey ?? "web.ui.pagination.summary", new { from, to, total, count = total }), 12, true);
        summary.Opacity = fetching ? 0.6 : 1; AutomationProperties.SetAutomationId(summary, id + "Summary"); left.Children.Add(summary);
        if (setSize != null)
        {
            var rows = Inline(8); rows.Children.Add(Text(A.T("web.ui.pagination.rows"), 12, true));
            var select = Select(id + "Size", sizes.Select(size => (size.ToString(), size.ToString())), 80); select.Height = 32; select.FontSize = 12; select.Padding = new(10, 3, 10, 3);
            AutomationProperties.SetName(select, A.T("web.ui.pagination.rows"));
            SetSelected(select, pageSize.ToString());
            // The selection above is set before the handler exists, so only an admin choice changes the page size.
            select.SelectionChanged += (_, _) => { if (int.TryParse(Selected(select), out var size) && size != pageSize) setSize(size); };
            rows.Children.Add(select); left.Children.Add(rows);
        }
        grid.Children.Add(left);
        if (pageCount > 1)
        {
            var nav = Inline(4); AutomationProperties.SetName(nav, A.T("web.ui.pagination.label"));
            nav.Children.Add(IconButton("chevrons-left", A.T("web.ui.pagination.first"), id + "First", () => goTo(0), safe > 0, size: 32));
            nav.Children.Add(IconButton("chevron-left", A.T("web.ui.pagination.previous"), id + "Previous", () => goTo(safe - 1), safe > 0, size: 32));
            if (phone) nav.Children.Add(Text(A.T("web.ui.pagination.compact", new { page = safe + 1, pages = pageCount }), 12, true));
            else
                foreach (var entry in D.PageWindow(safe, pageCount))
                {
                    if (entry is not { } index) { var gap = Text("\u2026", 14, true); gap.Opacity = 0.6; AutomationProperties.SetAccessibilityView(gap, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw); nav.Children.Add(gap); continue; }
                    var button = Button(AdminText.FormatNumber(index + 1), id + "Page" + (index + 1), index == safe ? "AccentButtonStyle" : "GhostButtonStyle");
                    button.MinWidth = 32; button.Height = 32; button.MinHeight = 32; button.Padding = new(8, 0, 8, 0); button.CornerRadius = new(6);
                    AutomationProperties.SetName(button, A.T("web.ui.pagination.page", new { page = index + 1 }));
                    if (index == safe) AutomationProperties.SetItemStatus(button, "current");
                    button.Click += (_, _) => goTo(index);
                    nav.Children.Add(button);
                }
            nav.Children.Add(IconButton("chevron-right", A.T("web.ui.pagination.next"), id + "Next", () => goTo(safe + 1), safe < pageCount - 1, size: 32));
            nav.Children.Add(IconButton("chevrons-right", A.T("web.ui.pagination.last"), id + "Last", () => goTo(pageCount - 1), safe < pageCount - 1, size: 32));
            if (phone) { Grid.SetRow(nav, 1); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetColumnSpan(left, 2); Grid.SetColumnSpan(nav, 2); }
            else Grid.SetColumn(nav, 1);
            grid.Children.Add(nav);
        }
        return grid;
    }

    /// <summary>The shared table frame: clipped rounded border, muted header band, hairline row dividers.</summary>
    internal sealed class DataTable
    {
        private readonly StackPanel _rows = new();
        internal Border Root { get; } = new();
        private readonly double[] _widths;
        internal DataTable(IReadOnlyList<string> headers, double[] widths)
        {
            _widths = widths; Root.CornerRadius = new(8); Root.BorderBrush = Brush("BorderBrush"); Root.BorderThickness = new(1); Root.Child = _rows;
            var head = MakeRow(headers.Select(header => { var label = Text(header, 12, true); label.FontWeight = FontWeights.Medium; return (UIElement)label; }).ToArray());
            head.Background = Tint("MutedBrush", 0.5); head.Padding = new(12, 10, 12, 10);
            _rows.Children.Add(head);
        }
        internal Grid MakeRow(IReadOnlyList<UIElement> cells)
        {
            var grid = new Grid { ColumnSpacing = 12, Padding = new(12, 12, 12, 12), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            foreach (var width in _widths) grid.ColumnDefinitions.Add(new() { Width = new(width, GridUnitType.Star) });
            for (var index = 0; index < cells.Count; index++)
            {
                if (cells[index] is FrameworkElement element) element.VerticalAlignment = VerticalAlignment.Top;
                Microsoft.UI.Xaml.Controls.Grid.SetColumn((FrameworkElement)cells[index], index); grid.Children.Add(cells[index]);
            }
            return grid;
        }
        internal Grid AddRow(IReadOnlyList<UIElement> cells)
        {
            var row = MakeRow(cells);
            _rows.Children.Add(new Border { BorderThickness = new(0, 1, 0, 0), BorderBrush = Brush("BorderBrush"), Child = row });
            return row;
        }
    }

    internal static StackPanel Stacked(IEnumerable<UIElement> children, double spacing = 8)
    {
        var stack = Stack(spacing); foreach (var child in children) if (child != null) stack.Children.Add(child); return stack;
    }

    internal static Border Skeleton() => new() { Height = 64, CornerRadius = new(8), Background = Tint("MutedBrush", 0.6), Opacity = 0.7 };

    /// <summary>Wrapping horizontal flow with fixed spacing: badges, chips and metric items break onto a new line when narrow.</summary>
    internal sealed class WrapPanel : Panel
    {
        internal double HorizontalSpacing { get; init; }
        internal double VerticalSpacing { get; init; }
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
        {
            double x = 0, y = 0, rowHeight = 0, width = 0;
            foreach (var child in Children)
            {
                child.Measure(new(available.Width, double.PositiveInfinity));
                var size = child.DesiredSize;
                if (x > 0 && x + size.Width > available.Width) { y += rowHeight + VerticalSpacing; x = 0; rowHeight = 0; }
                x += size.Width + HorizontalSpacing; rowHeight = Math.Max(rowHeight, size.Height); width = Math.Max(width, x - HorizontalSpacing);
            }
            return new(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), y + rowHeight);
        }
        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
        {
            double x = 0, y = 0, rowHeight = 0;
            foreach (var child in Children)
            {
                var size = child.DesiredSize;
                if (x > 0 && x + size.Width > finalSize.Width) { y += rowHeight + VerticalSpacing; x = 0; rowHeight = 0; }
                child.Arrange(new(x, y, Math.Min(size.Width, finalSize.Width), size.Height));
                x += size.Width + HorizontalSpacing; rowHeight = Math.Max(rowHeight, size.Height);
            }
            return finalSize;
        }
    }

    /// <summary>The control with <paramref name="id"/> inside <paramref name="root"/>, or null.</summary>
    internal static Control? FindControl(DependencyObject root, string id)
    {
        if (root is Control control && AutomationProperties.GetAutomationId(control) == id) return control;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindControl(VisualTreeHelper.GetChild(root, index), id) is { } match) return match;
        return null;
    }

    internal static bool IsInsideRegion(DependencyObject? element, DependencyObject region)
    {
        for (; element != null; element = VisualTreeHelper.GetParent(element)) if (ReferenceEquals(element, region)) return true;
        return false;
    }

    /// <summary>The automation id of the control that has keyboard focus inside <paramref name="region"/>, or null when focus is elsewhere.</summary>
    internal static string? FocusedIdIn(XamlRoot? root, DependencyObject region)
    {
        if (root == null || Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) is not Control control || !IsInsideRegion(control, region)) return null;
        var id = AutomationProperties.GetAutomationId(control);
        return id.Length == 0 ? null : id;
    }

    /// <summary>
    /// The pager button to use when the one the admin pressed is now disabled (Next or Last on the last page, First or Previous on the first):
    /// the opposite direction, which is enabled whenever the pager has more than one page.
    /// </summary>
    internal static string? FallbackFocusId(string id)
    {
        foreach (var (suffix, other) in new[] { ("First", "Next"), ("Previous", "Next"), ("Last", "Previous"), ("Next", "Previous") })
            if (id.EndsWith(suffix, StringComparison.Ordinal)) return id[..^suffix.Length] + other;
        return null;
    }

    /// <summary>
    /// Puts keyboard focus back on the control with <paramref name="id"/> after its region was rebuilt (a rebuilt control is a new object, so
    /// the one that had focus is gone). It waits for the rebuilt tree, does nothing when the control already has focus, and falls back
    /// to the neighbouring pager button when the pressed one is now disabled.
    /// </summary>
    internal static void RestoreFocusById(Microsoft.UI.Dispatching.DispatcherQueue? queue, DependencyObject region, string? id, Func<bool>? alive = null)
    {
        if (id == null || queue == null) return;
        queue.TryEnqueue(() =>
        {
            if (alive != null && !alive()) return;
            var target = FindControl(region, id) is { IsEnabled: true } exact ? exact
                : FallbackFocusId(id) is { } other && FindControl(region, other) is { IsEnabled: true } fallback ? fallback : null;
            if (target?.XamlRoot == null || ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(target.XamlRoot), target)) return;
            target.Focus(FocusState.Programmatic);
        });
    }

    internal static bool HasAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        for (; element != null; element = VisualTreeHelper.GetParent(element)) if (element is T) return true;
        return false;
    }
}
