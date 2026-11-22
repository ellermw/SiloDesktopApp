using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;
using SiloPlayer.Helpers;
using Windows.Foundation;
using Windows.UI;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Presentation primitives for Manage › Users, after the official <c>pages/admin-users/detail/ui.tsx</c> (DetailCard, KeyValueRow, ListRow, StatStrip,
/// SourceTag, InitialAvatar) and the shadcn badges and tab strip. Text is plain; a tab or page change swaps in place (no animation).
/// </summary>
internal static class AdminUsersUi
{
    internal static string T(string key, params (string Name, object? Value)[] args) => AdminUsersText.T(key, args);

    /// <summary>The side padding of a card: <c>px-4 sm:px-5</c>. The page sets it before it draws.</summary>
    internal static double Gutter { get; set; } = 20;

    internal static Brush Brush(string key) => LogsNativeUi.Brush(key);
    internal static Brush Tint(string key, double alpha) => LogsNativeUi.Tint(key, alpha);
    internal static TextBlock Text(string text, double size = 14, bool muted = false) => LogsNativeUi.Text(text, size, muted);
    internal static TextBlock Medium(string text, double size = 14, bool muted = false)
    {
        var block = Text(text, size, muted);
        block.FontWeight = FontWeights.Medium;
        return block;
    }
    internal static TextBlock Mono(string text, double size = 13, bool muted = false)
    {
        var block = LogsNativeUi.Text(text, size, muted, mono: true);
        block.FlowDirection = FlowDirection.LeftToRight;
        return block;
    }
    /// <summary>One line cut with an ellipsis at its end (the official <c>truncate</c>).</summary>
    internal static TextBlock Truncated(string text, double size = 14, bool muted = false, bool medium = false)
    {
        var block = medium ? Medium(AdminNameText.SingleLine(text), size, muted) : Text(AdminNameText.SingleLine(text), size, muted);
        block.TextWrapping = TextWrapping.NoWrap; block.TextTrimming = TextTrimming.CharacterEllipsis; block.HorizontalAlignment = HorizontalAlignment.Left;
        block.TextReadingOrder = TextReadingOrder.DetectFromContent;
        return block;
    }
    internal static FrameworkElement Icon(string name, double size = 16, Brush? brush = null) => AdminMonitorUi.Icon(name, size, brush ?? Brush("PrimaryTextBrush"));
    internal static FrameworkElement MutedIcon(string name, double size = 16) => AdminMonitorUi.Icon(name, size, Brush("SecondaryTextBrush"));

    internal static Border Surface(UIElement child) => new()
    {
        Child = child, CornerRadius = new(AdminLibrariesUi.Radius2Xl), Background = Tint("SurfaceBrush", 0.9), BorderThickness = new(0),
    };

    /// <summary>A one-pixel hairline (<c>border-border/50</c>) as a standalone element.</summary>
    internal static Border Rule() => new() { Height = 1, Background = Tint("BorderBrush", 0.5), HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>The centered muted line a card or list shows when it has nothing to list (<c>px-5 py-6 text-center text-sm</c>).</summary>
    internal static Border Empty(string text)
    {
        var block = Text(text, 14, true); block.TextAlignment = TextAlignment.Center; block.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Child = block, Padding = new(Gutter, 24, Gutter, 24) };
    }

    /// <summary>A failed read inside a card: the red message with its retry button (<c>role="alert"</c>).</summary>
    internal static Border Failure(string message, Button? retry)
    {
        var row = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
        var text = Text(message, 14); text.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Assertive);
        row.Children.Add(text);
        if (retry != null) row.Children.Add(retry);
        return new Border { Child = row, Padding = new(Gutter, 16, Gutter, 16) };
    }

    /// <summary>A red-tinted alert (<c>role="alert"</c>): the triangle at the start, the content beside it.</summary>
    internal static Border AlertBox(UIElement content)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var icon = AdminMonitorUi.Icon("alert-triangle", 16, AdminMonitorUi.Red500); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 2, 0, 0);
        row.Children.Add(icon); Grid.SetColumn((FrameworkElement)content, 1); row.Children.Add(content);
        var box = new Border { Child = row, Padding = new(16), CornerRadius = new(16), BorderThickness = new(1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Red500, 0.2), Background = AdminMonitorUi.Solid(AdminMonitorUi.Red500, 0.05) };
        AutomationProperties.SetLiveSetting(box, AutomationLiveSetting.Assertive);
        return box;
    }

    // ── Tone helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>shadcn Badge <c>variant="default"</c>: the primary fill under its foreground.</summary>
    internal static Border PrimaryBadge(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Brush("AccentForegroundBrush"), TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        return new Border
        {
            Child = label, CornerRadius = new(AdminLibrariesUi.RadiusMd), Padding = new(8, 2, 8, 2), Background = Brush("AccentBrush"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>A badge tinted with a tone colour (<c>border-amber-500/30 bg-amber-500/10 text-amber-300</c> and its siblings).</summary>
    internal static Border TintedBadge(string text, Color tone, Color? foreground = null, bool pill = false, bool dot = false, double fontSize = 12)
    {
        var label = new TextBlock { Text = text, FontSize = fontSize, FontWeight = FontWeights.Medium, Foreground = AdminMonitorUi.Solid(foreground ?? tone), TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        UIElement child = label;
        if (dot)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var mark = new Border { Width = 6, Height = 6, Background = AdminMonitorUi.Solid(tone), VerticalAlignment = VerticalAlignment.Center };
            RoundedBadge.SetRadius(mark, 999);
            row.Children.Add(mark); row.Children.Add(label); child = row;
        }
        var badge = new Border
        {
            Child = child, CornerRadius = new(AdminLibrariesUi.RadiusMd), Padding = new(8, 2, 8, 2), BorderThickness = new(1),
            Background = AdminMonitorUi.Solid(tone, 0.10), BorderBrush = AdminMonitorUi.Solid(tone, 0.30),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
        if (pill) RoundedBadge.SetRadius(badge, 999);
        return badge;
    }

    /// <summary>The role badge: Owner, Admin or User (<c>default</c> for an admin, <c>secondary</c> otherwise).</summary>
    internal static FrameworkElement RoleBadge(System.Text.Json.Nodes.JsonObject user)
    {
        var role = user.Flag("is_owner") ? T("web.admin.users.detail.access.role.owner")
            : user.Text("role") == "admin" ? T("web.admin.users.detail.access.role.admin") : T("web.admin.users.detail.access.role.user");
        return user.Text("role") == "admin" ? PrimaryBadge(role) : AdminLibrariesUi.Badge(role, AdminLibrariesUi.BadgeTone.Secondary);
    }

    /// <summary>The account's status in the list: <c>outline</c> Active or <c>destructive</c> Disabled.</summary>
    internal static FrameworkElement StatusBadge(System.Text.Json.Nodes.JsonObject user) => user.Flag("enabled")
        ? AdminLibrariesUi.Badge(T("web.admin.users.list.status.active"), AdminLibrariesUi.BadgeTone.Outline)
        : AdminLibrariesUi.Badge(T("web.admin.users.list.status.disabled"), AdminLibrariesUi.BadgeTone.Destructive);

    /// <summary>The detail header's Active badge: outline with a success dot (<c>border-success/30 text-success</c>).</summary>
    internal static FrameworkElement ActiveBadge(string text) => TintedBadge(text, AdminMonitorUi.ToneColor(ActivityTone.Success), dot: true);

    /// <summary>Official SourceTag: DEFAULT, GROUP or CUSTOM (amber chip) in 10px semibold caps.</summary>
    internal static FrameworkElement SourceTag(string source)
    {
        var label = new TextBlock
        {
            Text = T("web.admin.users.detail.common.source." + source), FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 60,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap,
            Foreground = source == "custom" ? AdminMonitorUi.Solid(AdminMonitorUi.Amber300) : Brush("SecondaryTextBrush"),
        };
        if (source != "custom") { label.Opacity = 0.8; return label; }
        return new Border
        {
            Child = label, CornerRadius = new(8), Padding = new(6, 1, 6, 1), BorderThickness = new(1),
            Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.10), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.30),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ── Avatar ──────────────────────────────────────────────────────────────────────────────────────────

    private static readonly (Color Start, Color End)[] Gradients =
    [
        (Color.FromArgb(255, 0xF4, 0x3F, 0x5E), Color.FromArgb(255, 0xFB, 0x92, 0x3C)),   // rose-500 → orange-400
        (Color.FromArgb(255, 0x63, 0x66, 0xF1), Color.FromArgb(255, 0x8B, 0x5C, 0xF6)),   // indigo-500 → violet-500
        (Color.FromArgb(255, 0x10, 0xB9, 0x81), Color.FromArgb(255, 0x14, 0xB8, 0xA6)),   // emerald-500 → teal-500
        (Color.FromArgb(255, 0xF5, 0x9E, 0x0B), Color.FromArgb(255, 0xEA, 0x58, 0x0C)),   // amber-500 → orange-600
        (Color.FromArgb(255, 0x0E, 0xA5, 0xE9), Color.FromArgb(255, 0x25, 0x63, 0xEB)),   // sky-500 → blue-600
    ];

    /// <summary>Official InitialAvatar: the name's first character on one of five gradients picked by <paramref name="seed"/>; "?" when there is none.</summary>
    internal static Border Avatar(string name, long seed, bool large = false, double? size = null)
    {
        var diameter = size ?? (large ? 64 : 32);
        var (start, end) = Gradients[(int)(Math.Abs(seed) % Gradients.Length)];
        var initial = AdminNameText.Initial(name); if (initial.Length == 0) initial = "?";
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        brush.GradientStops.Add(new GradientStop { Color = start, Offset = 0 }); brush.GradientStops.Add(new GradientStop { Color = end, Offset = 1 });
        var glyph = new TextBlock
        {
            Text = initial, FontSize = Math.Round(diameter * (large ? 0.42 : 0.44)), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsTextScaleFactorEnabled = false,
        };
        var avatar = new Border { Width = diameter, Height = diameter, Background = brush, Child = glyph, VerticalAlignment = VerticalAlignment.Center };
        RoundedBadge.SetRadius(avatar, 999);
        AutomationProperties.SetAccessibilityView(avatar, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return avatar;
    }

    // ── Cards and rows ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Official DetailCard: a rounded surface with a header (title, description, actions), the rows, and an optional footer. The first row of the body
    /// should be created with <see cref="RowStack"/> so it carries no top rule.
    /// </summary>
    internal static Border DetailCard(string title, string? description, UIElement? actions, UIElement? body, UIElement? footer = null, string? id = null)
    {
        var stack = new StackPanel();
        var heading = new StackPanel { Spacing = 2 };
        var titleText = Medium(title, 14); titleText.FontWeight = FontWeights.SemiBold; titleText.LineHeight = 19.25;
        AutomationProperties.SetHeadingLevel(titleText, AutomationHeadingLevel.Level3);
        heading.Children.Add(titleText);
        if (!string.IsNullOrEmpty(description)) { var detail = Text(description, 13, true); detail.LineHeight = 21; heading.Children.Add(detail); }
        var header = new Border
        {
            Child = new SplitRow(heading, actions) { MinStart = 192, Gap = 12, RowGap = 8 },
            Padding = new(Gutter, 14, Gutter, 14), BorderThickness = new(0, 0, 0, 1), BorderBrush = Tint("BorderBrush", 0.7),
        };
        stack.Children.Add(header);
        if (body != null) stack.Children.Add(body);
        if (footer != null)
            stack.Children.Add(new Border { Child = footer, Padding = new(Gutter, 12, Gutter, 12), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.7) });
        var card = Surface(stack);
        AutomationProperties.SetName(card, title);
        if (id != null) AutomationProperties.SetAutomationId(card, id);
        return card;
    }

    /// <summary>The rows of a card body: each row rule except the first's (<c>first:border-t-0</c>).</summary>
    internal static StackPanel RowStack(IEnumerable<Border> rows)
    {
        var stack = new StackPanel();
        var first = true;
        foreach (var row in rows)
        {
            if (first) row.BorderThickness = new(0);
            first = false;
            stack.Children.Add(row);
        }
        return stack;
    }

    /// <summary>Official KeyValueRow: label (and description) at the start, the value (with a line under it and a source tag) at the end.</summary>
    internal static Border KeyValueRow(string label, string? description, UIElement value, string? below = null, UIElement? tag = null, bool changed = false)
    {
        var start = new StackPanel { Spacing = 0 };
        start.Children.Add(Text(label, 14, true));
        if (!string.IsNullOrEmpty(description)) { var hint = Text(description, 12, true); hint.Opacity = 0.8; hint.LineHeight = 19.5; start.Children.Add(hint); }
        var valueStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        if (value is TextBlock block) { block.TextAlignment = TextAlignment.Right; block.HorizontalAlignment = HorizontalAlignment.Right; }
        valueStack.Children.Add(value);
        if (!string.IsNullOrEmpty(below)) { var line = Text(below, 12, true); line.TextAlignment = TextAlignment.Right; line.HorizontalAlignment = HorizontalAlignment.Right; valueStack.Children.Add(line); }
        UIElement end = valueStack;
        if (tag != null)
        {
            var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
            pair.Children.Add(valueStack); pair.Children.Add(tag);
            end = pair;
        }
        return new Border
        {
            Child = new SplitRow(start, end) { MinStart = 112, Gap = 16, RowGap = 8, CenterVertically = true },
            Padding = new(Gutter, 12, Gutter, 12), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.5),
            Background = changed ? AdminMonitorUi.Solid(AdminMonitorUi.Amber500, 0.04) : null,
        };
    }
    internal static Border KeyValueRow(string label, string? description, string value, string? below = null, UIElement? tag = null, bool mono = false)
    {
        var text = mono ? Mono(value, 14) : Medium(value, 14);
        text.FontWeight = FontWeights.Medium; text.IsTextSelectionEnabled = true;
        return KeyValueRow(label, description, text, below, tag);
    }

    /// <summary>Official ListRow: a leading tile, the title (one line) with its meta line, and a trailing end-aligned note.</summary>
    internal static Border ListRow(UIElement? leading, string title, string? meta, UIElement? trailing, bool mono = false)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (leading != null) { grid.Children.Add(leading); }
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var heading = mono ? Mono(AdminNameText.SingleLine(title), 14) : Truncated(title, 14, medium: true);
        if (mono) { heading.TextWrapping = TextWrapping.Wrap; heading.FontWeight = FontWeights.Medium; }
        copy.Children.Add(heading);
        if (!string.IsNullOrEmpty(meta)) { var line = Text(meta, 12, true); line.LineHeight = 16; copy.Children.Add(line); }
        Grid.SetColumn(copy, 1); grid.Children.Add(copy);
        if (trailing != null)
        {
            if (trailing is FrameworkElement trailingElement) trailingElement.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn((FrameworkElement)trailing, 2); grid.Children.Add(trailing);
        }
        return new Border { Child = grid, Padding = new(Gutter, 12, Gutter, 12), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.5) };
    }

    /// <summary>The trailing note of a ListRow: 12px muted, end-aligned, never wrapped.</summary>
    internal static TextBlock Trailing(string text)
    {
        var block = Text(text, 12, true); block.TextAlignment = TextAlignment.Right; block.TextWrapping = TextWrapping.NoWrap; return block;
    }

    /// <summary>A thin progress bar (<c>h-1 rounded-full</c>, amber fill) for a fraction between 0 and 1.</summary>
    internal static FrameworkElement Bar(double fraction, double width = 96)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var track = new Grid { Width = width, Height = 4, HorizontalAlignment = HorizontalAlignment.Left };
        var back = new Border { Background = Tint("PrimaryTextBrush", 0.10) }; RoundedBadge.SetRadius(back, 999);
        track.Children.Add(back);
        if (fraction > 0)
        {
            var fill = new Border { Width = Math.Max(2, Math.Round(width * fraction)), Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500), HorizontalAlignment = HorizontalAlignment.Left };
            RoundedBadge.SetRadius(fill, 999);
            track.Children.Add(fill);
        }
        return track;
    }

    // ── Stat strip ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One StatTile: a muted label, a 20px semibold value (with its "of {total}") and an optional bar and detail.</summary>
    internal static Border StatTile(string label, string value, string? total = null, double? progress = null, string? detail = null)
    {
        var stack = new StackPanel { Spacing = 6 };
        var caption = Truncated(label, 12, true); stack.Children.Add(caption);
        var number = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush("PrimaryTextBrush"), TextWrapping = TextWrapping.NoWrap, LineHeight = 28, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        number.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = value });
        if (total != null)
            number.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " + T("web.admin.users.detail.common.statTotal", ("total", total)), FontSize = 14, FontWeight = FontWeights.Normal, Foreground = Brush("SecondaryTextBrush") });
        stack.Children.Add(number);
        if (progress is { } fraction) stack.Children.Add(StretchBar(fraction));
        if (!string.IsNullOrEmpty(detail)) stack.Children.Add(Text(detail, 12, true));
        var tile = new Border { Child = stack, Padding = new(16, 14, 16, 14), Background = Tint("CardBackgroundBrush", 0.6), BorderBrush = Tint("BorderBrush", 0.5) };
        AutomationProperties.SetName(tile, label + ": " + value + (total != null ? " " + T("web.admin.users.detail.common.statTotal", ("total", total)) : ""));
        return tile;
    }
    private static FrameworkElement StretchBar(double fraction)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var track = new Grid { Height = 4 };
        var back = new Border { Background = Tint("PrimaryTextBrush", 0.10) }; RoundedBadge.SetRadius(back, 999);
        track.Children.Add(back);
        var fill = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        fill.ColumnDefinitions.Add(new() { Width = new(Math.Max(fraction, 0.0001), GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new() { Width = new(Math.Max(1 - fraction, 0.0001), GridUnitType.Star) });
        var bar = new Border { Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500), Visibility = fraction > 0 ? Visibility.Visible : Visibility.Collapsed };
        RoundedBadge.SetRadius(bar, 999);
        fill.Children.Add(bar);
        track.Children.Add(fill);
        return track;
    }

    /// <summary>Official StatStrip: 2 columns on a phone; 5 (or 4 from 640 up when there are four tiles) from 1024 up. Rules sit between tiles.</summary>
    internal static Border StatStrip(IReadOnlyList<Border> tiles)
    {
        var panel = new TileGrid(tiles);
        return Surface(panel);
    }

    internal sealed class TileGrid : Panel
    {
        private readonly int _count;
        internal TileGrid(IReadOnlyList<Border> tiles) { _count = tiles.Count; foreach (var tile in tiles) Children.Add(tile); }
        private int _columns = 1;
        private double _cell;
        private double[] _rowHeights = [];
        protected override Size MeasureOverride(Size available)
        {
            var width = double.IsFinite(available.Width) ? available.Width : 1000;
            var root = XamlRoot?.Size.Width ?? width;
            _columns = _count >= 5 ? (root >= 1024 ? 5 : 2) : _count == 4 ? (root >= 640 ? 4 : 2) : Math.Max(1, Math.Min(_count, 2));
            _cell = width / _columns;
            var rows = (int)Math.Ceiling(_count / (double)_columns);
            _rowHeights = new double[rows];
            for (var index = 0; index < _count; index++)
            {
                var tile = (Border)Children[index];
                var column = index % _columns; var row = index / _columns;
                var last = index == _count - 1 && _count % _columns == 1 && _columns > 1;   // a last tile alone on its row spans it
                var span = last ? width : _cell;
                var thickness = new Thickness(0, 0, column == _columns - 1 || last ? 0 : 1, row == rows - 1 ? 0 : 1);
                if (tile.BorderThickness != thickness) tile.BorderThickness = thickness;
                tile.Measure(new(span, double.PositiveInfinity));
                _rowHeights[row] = Math.Max(_rowHeights[row], tile.DesiredSize.Height);
            }
            return new(width, _rowHeights.Sum());
        }
        protected override Size ArrangeOverride(Size final)
        {
            var rows = _rowHeights.Length;
            var top = 0.0;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < _columns; column++)
                {
                    var index = row * _columns + column;
                    if (index >= _count) break;
                    var last = index == _count - 1 && _count % _columns == 1 && _columns > 1;
                    Children[index].Arrange(new(last ? 0 : column * _cell, top, last ? final.Width : _cell, _rowHeights[row]));
                }
                top += _rowHeights[row];
            }
            return final;
        }
    }

    // ── Start/end row ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The official <c>flex flex-wrap items-center justify-between gap-x gap-y</c> row: the start content (basis <see cref="MinStart"/>) takes the
    /// free room, the end content sits at the end; when they cannot share a line the end content drops below, still at the end.
    /// </summary>
    internal sealed class SplitRow : Panel
    {
        private readonly UIElement _start;
        private readonly UIElement? _end;
        internal double MinStart { get; init; } = 112;
        internal double Gap { get; init; } = 12;
        internal double RowGap { get; init; } = 8;
        internal bool CenterVertically { get; init; }
        private bool _wrapped;
        private double _startWidth;

        internal SplitRow(UIElement start, UIElement? end)
        {
            _start = start; _end = end;
            Children.Add(start);
            if (end != null) Children.Add(end);
        }

        protected override Size MeasureOverride(Size available)
        {
            var width = double.IsFinite(available.Width) ? Math.Max(0, available.Width) : 1560;
            _start.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            if (_end == null)
            {
                if (_start.DesiredSize.Width > width) _start.Measure(new(width, double.PositiveInfinity));
                _wrapped = false; _startWidth = Math.Min(width, _start.DesiredSize.Width);
                return new(width, _start.DesiredSize.Height);
            }
            _end.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var s = _start.DesiredSize.Width; var e = _end.DesiredSize.Width;
            if (e > width) { _end.Measure(new(width, double.PositiveInfinity)); e = _end.DesiredSize.Width; }
            // A child is measured again only at a width below its desired width, never at exactly that width.
            if (s + Gap + e <= width + LayoutTolerance.Fit && e <= width)
            {
                _wrapped = false; _startWidth = s;
                return new(width, Math.Max(_start.DesiredSize.Height, _end.DesiredSize.Height));
            }
            if (Math.Min(s, MinStart) + Gap + e <= width + LayoutTolerance.Fit && e <= width)
            {
                _wrapped = false; _startWidth = Math.Max(0, width - e - Gap);
                if (_startWidth < s) _start.Measure(new(_startWidth, double.PositiveInfinity));
                else _startWidth = s;
                return new(width, Math.Max(_start.DesiredSize.Height, _end.DesiredSize.Height));
            }
            _wrapped = true; _startWidth = Math.Min(width, s);
            if (s > width) { _start.Measure(new(width, double.PositiveInfinity)); _startWidth = width; }
            return new(width, _start.DesiredSize.Height + RowGap + _end.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (_end == null) { _start.Arrange(new(0, 0, final.Width, _start.DesiredSize.Height)); return final; }
            var endWidth = Math.Min(final.Width, _end.DesiredSize.Width);
            if (!_wrapped)
            {
                var height = Math.Max(_start.DesiredSize.Height, _end.DesiredSize.Height);
                var startTop = CenterVertically ? (height - _start.DesiredSize.Height) / 2 : 0;
                var endTop = CenterVertically ? (height - _end.DesiredSize.Height) / 2 : 0;
                _start.Arrange(new(0, startTop, Math.Max(_startWidth, 0), _start.DesiredSize.Height));
                _end.Arrange(new(final.Width - endWidth, endTop, endWidth, _end.DesiredSize.Height));
            }
            else
            {
                _start.Arrange(new(0, 0, Math.Max(_startWidth, 0), _start.DesiredSize.Height));
                _end.Arrange(new(final.Width - endWidth, _start.DesiredSize.Height + RowGap, endWidth, _end.DesiredSize.Height));
            }
            return final;
        }
    }

    // ── Tabs ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The page's tab strip (<c>TabsList variant="line"</c>): a hairline under the row, the selected tab marked by a 2px bar. It scrolls sideways
    /// when the tabs do not fit and shows no scrollbar. <paramref name="extra"/> is the small count or dot after a label.
    /// </summary>
    internal static Border TabStrip(IReadOnlyList<(string Value, string Label, UIElement? Extra)> tabs, string selected, Action<string> choose, string automationPrefix)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, TabFocusNavigation = Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Once };
        for (var index = 0; index < tabs.Count; index++)
        {
            var (value, label, extra) = tabs[index];
            var active = value == selected;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap,
                Foreground = Brush(active ? "PrimaryTextBrush" : "SecondaryTextBrush") });
            if (extra != null) content.Children.Add(extra);
            var button = new Button
            {
                Content = content, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = 40, MinWidth = 0, Padding = new(12, 6, 12, 6),
                BorderThickness = new(0, 0, 0, 2), CornerRadius = new(0), IsTabStop = active,
                BorderBrush = active ? Brush("PrimaryTextBrush") : new SolidColorBrush(Colors.Transparent),
            };
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetAutomationId(button, automationPrefix + value);
            AutomationProperties.SetPositionInSet(button, index + 1);
            AutomationProperties.SetSizeOfSet(button, tabs.Count);
            var captured = value;
            button.Click += (_, _) => { if (captured != selected) choose(captured); };
            row.Children.Add(button);
        }
        return new Border
        {
            Child = new ScrollViewer
            {
                Content = row, HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1),
        };
    }

    /// <summary>A small muted count after a tab label (<c>text-muted-foreground font-normal tabular-nums</c>).</summary>
    internal static TextBlock TabCount(string text)
    {
        var block = Text(text, 14, true); block.VerticalAlignment = VerticalAlignment.Center; block.TextWrapping = TextWrapping.NoWrap;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    // ── Buttons ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>An outline button (<c>variant="outline" size="sm"</c>, 32px) or, with <paramref name="style"/>, a ghost, accent or destructive one.</summary>
    internal static Button ActionButton(string label, string? icon = null, string style = "OutlineButtonStyle", double height = 32, string? name = null)
    {
        var button = AdminLibrariesUi.Action(label, "Users_" + AdminNameKey(name ?? label), null, style, height: height);
        if (icon != null)
        {
            var tone = style == "AccentButtonStyle" ? Brush("AccentForegroundBrush") : style == "DestructiveButtonStyle" ? new SolidColorBrush(Colors.White) : Brush("PrimaryTextBrush");
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(AdminMonitorUi.Icon(icon, 14, tone));
            content.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            button.Content = content;
            button.Padding = new(10, 0, 10, 0);
        }
        AutomationProperties.SetName(button, name ?? label);
        return button;
    }

    /// <summary>A ghost icon button; the accessible name is required.</summary>
    internal static Button IconAction(string icon, string name, double size = 32, double iconSize = 16)
    {
        var button = new Button
        {
            Content = AdminMonitorUi.Icon(icon, iconSize, Brush("SecondaryTextBrush")), Width = size, Height = size, MinWidth = size, MinHeight = size, Padding = new(0),
            CornerRadius = new(AdminLibrariesUi.RadiusMd), Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, "Users_" + AdminNameKey(name));
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    private static string AdminNameKey(string name)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var ch in name) if (char.IsLetterOrDigit(ch)) builder.Append(ch);
        return builder.Length > 48 ? builder.ToString(0, 48) : builder.ToString();
    }

    /// <summary>
    /// One row of an action menu (<c>DropdownMenuItem</c>): a full-width, start-aligned ghost button of at least 36px with an optional end hint
    /// (<c>text-muted-foreground ms-auto text-xs</c>). Menu rows are real buttons so each stays reachable by name.
    /// </summary>
    internal static Button MenuButton(string label, bool destructive = false, string? hint = null)
    {
        var row = new Grid { ColumnSpacing = 24 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, FontSize = 14, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(destructive ? "ErrorBrush" : "PrimaryTextBrush") };
        row.Children.Add(text);
        if (!string.IsNullOrEmpty(hint)) { var note = Text(hint, 12, true); note.TextWrapping = TextWrapping.NoWrap; Grid.SetColumn(note, 1); row.Children.Add(note); }
        var button = new Button
        {
            Content = row, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = 36, MinWidth = 0, Padding = new(8, 4, 8, 4), CornerRadius = new(AdminLibrariesUi.RadiusSm),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetAutomationId(button, "UsersMenu_" + AdminNameKey(label));
        return button;
    }

    /// <summary>
    /// The button that opens an action menu: the ellipsis icon button (the accessible name is required), or, with <paramref name="label"/>, an outline
    /// text button with a chevron. Choosing a row closes the menu before the row's action runs.
    /// </summary>
    internal static Button MenuTrigger(IReadOnlyList<Button> items, string name, string? label = null, double size = 32)
    {
        var list = new StackPanel { Spacing = 0, MinWidth = 208 };
        foreach (var item in items) list.Children.Add(item);
        var flyout = new Flyout { Content = list, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
        presenter.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(AdminLibrariesUi.RadiusLg)));
        flyout.FlyoutPresenterStyle = presenter;
        foreach (var item in items) item.Click += (_, _) => flyout.Hide();
        Button button;
        if (label == null) button = IconAction("more-horizontal", name, size);
        else
        {
            button = ActionButton(label, null, "OutlineButtonStyle", size, name);
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(AdminMonitorUi.Icon("chevron-down", 14, Brush("PrimaryTextBrush")));
            button.Content = content; button.Padding = new(12, 0, 10, 0);
        }
        button.Flyout = flyout;
        return button;
    }

    // ── Fields ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A select (<c>h-9 rounded-md</c>) whose accessible name is its label; <paramref name="changed"/> gets the chosen value.</summary>
    internal static ComboBox Select(string name, IEnumerable<(string Value, string Label)> options, string selected, Action<string> changed, double width = double.NaN)
    {
        var box = new ComboBox
        {
            Height = 36, MinHeight = 36, MinWidth = 0, FontSize = 14, Padding = new(12, 4, 12, 4), CornerRadius = new(AdminLibrariesUi.RadiusMd),
            Background = Brush("AppBackgroundBrush"), BorderBrush = Brush("InputBorderBrush"), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left, UseSystemFocusVisuals = true,
        };
        if (!double.IsNaN(width)) box.Width = width;
        foreach (var (value, label) in options)
        {
            var item = new ComboBoxItem { Content = label, Tag = value, MinHeight = 32 };
            AutomationProperties.SetName(item, label);
            box.Items.Add(item);
        }
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? box.Items.OfType<ComboBoxItem>().FirstOrDefault();
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: string chosen }) changed(chosen); };
        AutomationProperties.SetName(box, name);
        return box;
    }

    /// <summary>The search field: an input with a search icon at its start and a clear button at its end while it holds text.</summary>
    internal static FrameworkElement SearchBox(string placeholder, string clearName, string text, Action<string> changed, out TextBox input)
    {
        var box = new TextBox
        {
            Text = text, PlaceholderText = placeholder, MaxLength = 320, Height = 36, MinHeight = 36, FontSize = 14, Padding = new(36, 4, 36, 4),
            CornerRadius = new(AdminLibrariesUi.RadiusMd), Background = Brush("AppBackgroundBrush"), BorderBrush = Brush("InputBorderBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(box, placeholder);
        var root = new Grid();
        root.Children.Add(box);
        var glyph = MutedIcon("search", 16); glyph.HorizontalAlignment = HorizontalAlignment.Left; glyph.VerticalAlignment = VerticalAlignment.Center; glyph.Margin = new(12, 0, 0, 0);
        root.Children.Add(glyph);
        var clear = IconAction("x", clearName, 28, 14);
        clear.HorizontalAlignment = HorizontalAlignment.Right; clear.VerticalAlignment = VerticalAlignment.Center; clear.Margin = new(0, 0, 4, 0);
        clear.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        clear.Click += (_, _) => { box.Text = ""; box.Focus(FocusState.Programmatic); };
        root.Children.Add(clear);
        box.TextChanged += (_, _) => { clear.Visibility = box.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; changed(box.Text); };
        input = box;
        return root;
    }

    /// <summary>
    /// A table in a sideways scroller: the table is as wide as its card, and wider only when its columns cannot fit. A scroller measures its content
    /// without a width limit, so the width is set from the scroller's viewport. The narrowest the table can be is what its columns need (a star column's
    /// minimum, an auto or fixed column's measured width). The star columns share the rest by weight, and are given as pixel widths: a Grid holds a star
    /// column at its minimum without taking that space back from the others, which made a table with minimums wider than the card.
    /// </summary>
    internal static ScrollViewer FitTable(Grid grid, double floor = 0)
    {
        var scroller = new ScrollViewer
        {
            Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
        };
        var columns = grid.ColumnDefinitions;
        var weights = columns.Select(column => column.Width.IsStar ? column.Width.Value : 0).ToArray();
        void Fit()
        {
            double measured = 0, minimums = 0;
            for (var index = 0; index < columns.Count; index++)
            {
                if (weights[index] > 0) minimums += columns[index].MinWidth;
                else measured += Math.Max(columns[index].ActualWidth, columns[index].MinWidth);
            }
            var total = Math.Max(Math.Max(scroller.ActualWidth, floor), measured + minimums);
            var widths = new double[columns.Count];
            var open = Enumerable.Range(0, columns.Count).Where(index => weights[index] > 0).ToList();
            // One pixel is left unassigned: the measured columns are layout-rounded, and the sum must never exceed the table's width.
            var room = total - measured - (total - measured - minimums >= LayoutTolerance.Fit ? LayoutTolerance.Fit : 0);
            while (open.Count > 0)
            {
                var share = room / open.Sum(index => weights[index]);
                var held = open.Where(index => weights[index] * share < columns[index].MinWidth).ToList();
                if (held.Count == 0) { foreach (var index in open) widths[index] = weights[index] * share; break; }
                foreach (var index in held) { widths[index] = columns[index].MinWidth; room -= widths[index]; open.Remove(index); }
            }
            for (var index = 0; index < columns.Count; index++)
                if (weights[index] > 0 && (columns[index].Width.IsStar || Math.Abs(columns[index].Width.Value - widths[index]) >= 0.5)) columns[index].Width = new GridLength(widths[index]);
            if (total > 0 && (double.IsNaN(grid.Width) || Math.Abs(grid.Width - total) >= 0.5)) grid.Width = total;
        }
        EventHandler<object> refresh = (_, _) => Fit();
        scroller.SizeChanged += (_, _) => Fit();
        grid.Loaded += (_, _) => { Fit(); grid.LayoutUpdated -= refresh; grid.LayoutUpdated += refresh; };
        grid.Unloaded += (_, _) => grid.LayoutUpdated -= refresh;
        return scroller;
    }

    /// <summary>Horizontal padding of a table cell: <c>px-2 sm:px-3</c>, tighter while the table has little room. The page sets it before it draws.</summary>
    internal static double CellPad { get; set; } = 12;

    /// <summary>The platform as the Devices pages name it: "Android", "iOS", "TV".</summary>
    internal static string PlatformLabel(string? platform) => (platform ?? "").Trim().ToLowerInvariant() switch
    {
        "" => "",
        "tv" or "androidtv" or "android_tv" or "tvos" or "appletv" => "TV",
        "ios" => "iOS", "macos" => "macOS", "android" => "Android", "windows" => "Windows", "linux" => "Linux", "web" => "Web", "mobile" => "Mobile",
        var other => char.ToUpperInvariant(other[0]) + other[1..],
    };

    // ── Dates, durations and sizes (official format.ts and adminUserList.ts) ─────────────────────────────

    internal static DateTimeOffset? Parse(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    /// <summary>A medium date and the time without seconds: "Nov 14, 2026, 9:02 AM"; the fallback when there is no date.</summary>
    internal static string DateTimeShort(string? value, string fallback = "-") =>
        Parse(value) is { } at ? DateTimeDisplay.FormatDate(at, medium: true) + ", " + DateTimeDisplay.FormatTime(at) : fallback;
    internal static string FullDateTime(string? value) => Parse(value) is { } at ? DateTimeDisplay.FormatDateTime(at, seconds: true) : "";
    /// <summary>A calendar date with the month named: "Sep 28, 2026".</summary>
    internal static string AccountDate(string? value) => Parse(value) is { } at ? DateTimeDisplay.FormatDate(at, medium: true) : value ?? "";
    /// <summary>A month and day: "Sep 26".</summary>
    internal static string ShortDate(string? value) =>
        Parse(value) is { } at ? at.ToLocalTime().ToString("MMM d", SiloPlayer.Core.Localization.AppStrings.FormatCulture) : "";

    /// <summary>Intl.RelativeTimeFormat (numeric always) as the users list uses it: "3 days ago".</summary>
    internal static string Relative(string? value, string fallback)
    {
        if (Parse(value) is not { } at) return fallback;
        var seconds = (long)Math.Round((at - DateTimeOffset.UtcNow).TotalSeconds);
        var ranges = new (string Unit, long Seconds)[] { ("year", 31_536_000), ("month", 2_592_000), ("week", 604_800), ("day", 86_400), ("hour", 3_600), ("minute", 60), ("second", 1) };
        foreach (var (unit, per) in ranges)
        {
            if (Math.Abs(seconds) < per && unit != "second") continue;
            var amount = (long)Math.Round(seconds / (double)per);
            var count = Math.Abs(amount);
            var noun = count == 1 ? unit : unit + "s";
            return amount > 0 ? $"in {count} {noun}" : $"{count} {noun} ago";
        }
        return fallback;
    }

    /// <summary>"2h ago" within a month, the date after it, "—" when never (official formatLastSeen).</summary>
    internal static string LastSeen(string? value)
    {
        if (Parse(value) is not { } at) return "—";
        if ((DateTimeOffset.UtcNow - at).TotalDays > 30) return DateTimeDisplay.FormatDate(at, medium: true);
        return AdminPlaybackAuditText.Relative(value, DateTimeOffset.UtcNow) ?? "—";
    }

    /// <summary>"Today, 09:02", "Yesterday, 21:14" or "Sep 26, 18:02".</summary>
    internal static string DayTime(string? value)
    {
        if (Parse(value) is not { } at) return "";
        var local = at.ToLocalTime(); var now = DateTimeOffset.Now;
        var time = DateTimeDisplay.FormatTime(at);
        if (local.Date == now.Date) return T("web.admin.users.detail.common.dayTime.today", ("time", time));
        if (local.Date == now.Date.AddDays(-1)) return T("web.admin.users.detail.common.dayTime.yesterday", ("time", time));
        return T("web.admin.users.detail.common.dayTime.date", ("date", ShortDate(value)), ("time", time));
    }

    internal static string WatchTime(double seconds)
    {
        var total = double.IsFinite(seconds) ? Math.Max(0, (long)Math.Floor(seconds)) : 0;
        var hours = total / 3600; var minutes = total % 3600 / 60;
        return hours > 0 ? T("web.admin.users.detail.common.watchTime.hoursMinutes", ("hours", hours), ("minutes", minutes)) : T("web.admin.users.detail.common.watchTime.minutes", ("minutes", minutes));
    }

    /// <summary>Decimal units: "14.2 GB", "900 MB", "512 B".</summary>
    internal static string Bytes(double bytes)
    {
        var value = double.IsFinite(bytes) ? Math.Max(0, bytes) : 0;
        var units = new (string Key, double Size, int Digits)[] { ("b", 1, 0), ("kb", 1e3, 0), ("mb", 1e6, 0), ("gb", 1e9, 1), ("tb", 1e12, 1) };
        var index = 0;
        while (index < units.Length - 1 && value >= units[index + 1].Size) index++;
        var (key, size, digits) = units[index];
        if (Math.Round(value / size, digits) >= 1000 && index < units.Length - 1) (key, size, digits) = units[index + 1];
        return T("web.admin.users.detail.common.bytes." + key, ("value", Math.Round(value / size, digits).ToString("0.#", SiloPlayer.Core.Localization.AppStrings.FormatCulture)));
    }

    /// <summary>"S02E04" for an episode; "" when either number is unknown.</summary>
    internal static string EpisodeCode(long? season, long? episode) => season is null || episode is null ? ""
        : T("web.admin.users.detail.common.episodeCode", ("season", season.Value.ToString("00")), ("episode", episode.Value.ToString("00")));
}
