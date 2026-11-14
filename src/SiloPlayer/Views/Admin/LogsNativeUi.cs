using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using Windows.Foundation;
using Windows.System;

namespace SiloPlayer.Views.Admin;

internal static class LogsNativeUi
{
    internal static readonly Brush Violet = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 139, 250));
    internal static readonly Brush VioletLine = new SolidColorBrush(Windows.UI.Color.FromArgb(82, 167, 139, 250));
    internal static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    internal static string T(string key, params (string Name, object? Value)[] values) => LogsText.T(key, values);
    internal static TextBlock Text(string text, double size = 14, bool muted = false, bool mono = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Normal, Foreground = Brush(muted ? "SecondaryTextBrush" : "PrimaryTextBrush"), TextWrapping = TextWrapping.Wrap, LineHeight = size <= 12 ? 16 : size <= 14 ? 20 : size * 1.5, VerticalAlignment = VerticalAlignment.Center };
        if (mono) block.FontFamily = new FontFamily("Consolas");
        return block;
    }
    internal static StackPanel Stack(double spacing = 0) => new() { Spacing = spacing };
    internal static StackPanel Inline(double spacing = 8) => new() { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
    internal static Brush Tint(string key, double alpha)
    {
        if (Brush(key) is not SolidColorBrush source) return Brush(key);
        var color = source.Color; color.A = (byte)Math.Round(color.A * alpha); return new SolidColorBrush(color);
    }
    internal static Border Panel(UIElement child, double radius = 24, double padding = 12, bool border = false) => new()
    {
        Child = child, CornerRadius = new(radius), Padding = new(padding), Background = Tint("SurfaceBrush", border ? 0.72 : 0.90), BorderBrush = Tint("BorderBrush", border ? 0.62 : 0.72), BorderThickness = new(1)
    };
    internal static Button Button(string label, string key, string style = "OutlineButtonStyle", string? icon = null)
    {
        var button = new Button { Content = label, MinHeight = 36, Padding = icon == null ? new(16, 6, 16, 6) : new(12, 6, 12, 6), CornerRadius = new(10), FontSize = 14, FontWeight = FontWeights.Medium };
        button.Style = (Style)Application.Current.Resources[string.IsNullOrEmpty(style) ? "AccentButtonStyle" : style];
        if (icon != null) { var glyph = Icon(icon, 16, string.IsNullOrEmpty(style) ? Brush("AccentForegroundBrush") : null); if (label.Length == 0) button.Content = glyph; else { var content = Inline(8); content.Children.Add(glyph); content.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }); button.Content = content; } }
        AutomationProperties.SetName(button, label); AutomationProperties.SetAutomationId(button, key);
        return button;
    }
    internal static TextBox Input(string placeholder, string key, double width = 320, bool mono = false)
    {
        var input = new TextBox { PlaceholderText = placeholder, Width = width, MinWidth = 80, Height = 36, FontSize = 14, Padding = new(12, 6, 12, 6), CornerRadius = new(10), Background = Brush("AppBackgroundBrush"), BorderBrush = Brush("BorderBrush") };
        if (mono) input.FontFamily = new("Consolas");
        AutomationProperties.SetName(input, placeholder); AutomationProperties.SetAutomationId(input, key);
        return input;
    }
    internal static ComboBox Select(string key, IEnumerable<(string Value, string Label)> options, double width = 170)
    {
        var select = new ComboBox { Width = width, Height = 36, FontSize = 14, CornerRadius = new(10), Background = Brush("AppBackgroundBrush"), BorderBrush = Brush("BorderBrush"), Padding = new(12, 5, 12, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var (value, label) in options) select.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        select.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(select, key); AutomationProperties.SetName(select, T(key));
        return select;
    }
    internal static void SetSelected(ComboBox select, string? value)
    {
        var match = select.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == value);
        if (match == null && !string.IsNullOrEmpty(value)) { match = new() { Content = value, Tag = value }; select.Items.Add(match); }
        select.SelectedItem = match ?? select.Items[0];
    }
    internal static string Selected(ComboBox select) => (select.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    internal static FrameworkElement Header(string title, string subtitle, FrameworkElement? actions = null)
        => new LogsPageHeader(title, subtitle, actions);
    internal static StackPanel Metric(string label, string value, bool mono = false)
    {
        var metric = Stack(4); metric.Children.Add(Text(label, 12, true)); metric.Children.Add(Text(string.IsNullOrEmpty(value) ? "—" : value, mono ? 12 : 14, mono: mono)); return metric;
    }
    internal static TextBlock Cell(string text, bool muted = false, bool mono = false, double max = double.PositiveInfinity)
    {
        var cell = Text(text, mono ? 12 : 14, muted, mono); cell.TextWrapping = TextWrapping.NoWrap; cell.TextTrimming = TextTrimming.CharacterEllipsis; cell.MaxWidth = max; cell.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTipService.SetToolTip(cell, text); return cell;
    }
    internal static UIElement Icon(string name, double size = 16, Brush? foreground = null)
    {
        var data = name switch
        {
            "x" => "M18 6 6 18 M6 6l12 12",
            "download" => "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3",
            "external-link" => "M15 3h6v6 M10 14 21 3 M21 14v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5",
            "triangle-alert" => "m10.3 3.86-8.48 14.14A2 2 0 0 0 3.53 21h16.94a2 2 0 0 0 1.71-3L13.7 3.86a2 2 0 0 0-3.4 0 M12 9v4 M12 17h.01",
            "bug" => "m8 2 1.88 1.88 m4.24 0L16 2 M9 7.13V6a3 3 0 0 1 6 0v1.13 M12 20v-9 M4 5l3 3 M17 8l3-3 M2 12h6 M16 12h6 M3 19l4-4 M17 15l4 4 M8 7h8v9a4 4 0 0 1-8 0z",
            "filter-x" => "M22 3H2l8 9.46V19l4 2v-8.54 M16 16l5 5 M21 16l-5 5",
            "calendar" => "M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2",
            "trash-2" => "M3 6h18 M19 6l-1 14H6L5 6 M9 6V3h6v3 M10 10v6 M14 10v6",
            "circle-alert" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M12 8v4 M12 16h.01",
            "circle-check" => "M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M8 12l3 3 5-6",
            _ => null
        };
        if (data == null) return name == "download" ? WebUiIcon.Navigation(name, size) : WebUiIcon.Create(name, size);
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data + "' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = foreground ?? Brush("PrimaryTextBrush"); var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path); return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
    internal static Border Banner(string message, bool error = false)
    {
        var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var icon = Icon("triangle-alert"); if (icon is FrameworkElement glyph) { glyph.VerticalAlignment = VerticalAlignment.Top; glyph.Margin = new(0, 2, 0, 0); } row.Children.Add(icon);
        var label = Text(message, 14, !error); label.VerticalAlignment = VerticalAlignment.Top; if (error) label.Foreground = Brush("ErrorBrush"); Grid.SetColumn(label, 1); row.Children.Add(label);
        var panel = Panel(row, 12, 0, true); panel.Padding = new(16, 12, 16, 12); panel.Background = Tint("MutedBrush", 0.30); return panel;
    }
}

/// <summary>Flex-style header: actions wrap when the intrinsic title and action widths no longer fit.</summary>
internal sealed class LogsPageHeader : Panel
{
    private readonly StackPanel _titles = LogsNativeUi.Stack(8);
    private readonly TextBlock _heading;
    private readonly FrameworkElement? _actions;
    private bool _wrapped;
    internal LogsPageHeader(string title, string subtitle, FrameworkElement? actions)
    {
        Margin = new(0, 0, 0, 8);
        _heading = LogsNativeUi.Text(title, 32); _heading.FontWeight = FontWeights.Bold; _heading.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; _heading.CharacterSpacing = -35;
        AutomationProperties.SetHeadingLevel(_heading, AutomationHeadingLevel.Level1);
        _titles.Children.Add(_heading);
        if (subtitle.Length > 0) { var description = LogsNativeUi.Text(subtitle, 14, true); description.MaxWidth = 800; description.LineHeight = 22.75; _titles.Children.Add(description); }
        Children.Add(_titles); _actions = actions;
        if (actions != null) Children.Add(actions);
    }
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsFinite(available.Width) ? available.Width : 1560;
        var phone = (XamlRoot?.Size.Width ?? width) < 640;
        _heading.FontSize = phone ? 28 : 32; _heading.LineHeight = _heading.FontSize * 1.15;
        _titles.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        _actions?.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        _wrapped = _actions != null && _titles.DesiredSize.Width + 24 + _actions.DesiredSize.Width > width;
        _titles.Measure(new(width, double.PositiveInfinity));
        _actions?.Measure(new(width, double.PositiveInfinity));
        var titleHeight = _titles.DesiredSize.Height; var actionHeight = _actions?.DesiredSize.Height ?? 0;
        return new(width, _wrapped ? titleHeight + 12 + actionHeight : Math.Max(titleHeight, actionHeight));
    }
    protected override Size ArrangeOverride(Size final)
    {
        var titleHeight = _titles.DesiredSize.Height;
        _titles.Arrange(new(0, 0, Math.Min(final.Width, _titles.DesiredSize.Width), titleHeight));
        if (_actions != null) { var width = Math.Min(final.Width, _actions.DesiredSize.Width); _actions.Arrange(new(final.Width - width, _wrapped ? titleHeight + 12 : 0, width, _actions.DesiredSize.Height)); }
        return final;
    }
}

internal sealed class LogsMetricGrid : Grid
{
    private readonly int _wideColumns, _phoneColumns;
    internal LogsMetricGrid(int wideColumns, int phoneColumns)
    {
        _wideColumns = wideColumns; _phoneColumns = phoneColumns; ColumnSpacing = 16; RowSpacing = 16;
        SizeChanged += (_, _) => Reflow(); Loaded += (_, _) => Reflow();
    }
    internal void Add(UIElement item) { Children.Add(item); Reflow(); }
    private void Reflow()
    {
        var columns = (XamlRoot?.Size.Width ?? ActualWidth) >= 640 ? _wideColumns : _phoneColumns;
        var rows = (Children.Count + columns - 1) / columns;
        if (ColumnDefinitions.Count != columns || RowDefinitions.Count != rows)
        {
            ColumnDefinitions.Clear(); RowDefinitions.Clear();
            for (var index = 0; index < columns; index++) ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var index = 0; index < rows; index++) RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        for (var index = 0; index < Children.Count; index++) { SetColumn((FrameworkElement)Children[index], index % columns); SetRow((FrameworkElement)Children[index], index / columns); }
    }
}

internal sealed class LogsWrapPanel : Panel
{
    internal double Spacing { get; set; } = 8;
    internal double RowSpacing { get; set; } = double.NaN;
    internal bool GrowFields { get; set; }
    internal UIElement? TrailingActions { get; set; }
    private readonly List<(UIElement Child, Rect Bounds)> _placements = [];
    protected override Size MeasureOverride(Size available)
    {
        _placements.Clear();
        var width = double.IsFinite(available.Width) ? Math.Max(0, available.Width) : 1560;
        var row = new List<(UIElement Child, double Basis, bool Grow)>();
        double used = 0, top = 0, occupied = 0;
        void FinishRow()
        {
            if (row.Count == 0) return;
            var remainder = Math.Max(0, width - used); var growing = row.Count(item => item.Grow);
            // flex: 1 uses a zero basis, then freezes fields at their minimum width.
            var flexible = row.Where(item => item.Grow).Select(item => item.Basis).OrderByDescending(value => value).ToArray();
            var budget = row.Where(item => item.Grow).Sum(item => item.Basis) + remainder;
            var remaining = growing; var growthWidth = remaining > 0 ? budget / remaining : 0;
            foreach (var minimum in flexible)
            {
                if (minimum <= growthWidth || remaining <= 1) break;
                budget -= minimum; remaining--; growthWidth = budget / remaining;
            }
            var sizes = new List<(UIElement Child, double Width, double Height)>();
            foreach (var item in row)
            {
                var allocated = item.Grow ? Math.Max(item.Basis, growthWidth) : item.Basis;
                item.Child.Measure(new(allocated, double.PositiveInfinity));
                sizes.Add((item.Child, allocated, item.Child.DesiredSize.Height));
            }
            var height = sizes.Max(item => item.Height); double left = 0;
            foreach (var item in sizes)
            {
                if (item.Child == TrailingActions && growing == 0) left += remainder;
                _placements.Add((item.Child, new(left, top + height - item.Height, item.Width, item.Height)));
                left += item.Width + Spacing;
            }
            occupied = Math.Max(occupied, left - Spacing); top += height + (double.IsNaN(RowSpacing) ? Spacing : RowSpacing); row.Clear(); used = 0;
        }
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new(width, double.PositiveInfinity));
            var element = child as FrameworkElement;
            var grow = GrowFields && child != TrailingActions && element != null && double.IsNaN(element.Width);
            var basis = Math.Min(width, grow && element!.MinWidth > 0 ? element.MinWidth : child.DesiredSize.Width);
            if (row.Count > 0 && used + Spacing + basis > width) FinishRow();
            if (row.Count > 0) used += Spacing;
            row.Add((child, basis, grow)); used += basis;
        }
        FinishRow();
        var gap = double.IsNaN(RowSpacing) ? Spacing : RowSpacing;
        return new(Math.Min(width, occupied), Math.Max(0, top - gap));
    }
    protected override Size ArrangeOverride(Size final)
    {
        foreach (var (child, bounds) in _placements) child.Arrange(bounds);
        return final;
    }
}

internal sealed class LogsSummaryLayout : Panel
{
    private readonly Button _action;
    private readonly List<(UIElement Child, Rect Bounds)> _placements = [];
    internal LogsSummaryLayout(Button action) { _action = action; Children.Add(action); }
    protected override Size MeasureOverride(Size available)
    {
        _placements.Clear();
        var width = double.IsFinite(available.Width) ? Math.Max(0, available.Width) : 1000;
        _action.Measure(new(width, double.PositiveInfinity));
        var metrics = Children.Where(child => child != _action).ToArray();
        foreach (var metric in metrics) metric.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var intrinsic = metrics.Sum(metric => metric.DesiredSize.Width) + Math.Max(0, metrics.Length - 1) * 12;
        var wide = (XamlRoot?.Size.Width ?? width) >= 768;
        var beside = wide || intrinsic + 12 + _action.DesiredSize.Width <= width;
        var metricsWidth = beside ? Math.Max(0, width - _action.DesiredSize.Width - 12) : width;
        double left = 0, top = 0, rowHeight = 0;
        var columnWidth = Math.Max(0, (metricsWidth - 60) / 6);
        for (var index = 0; index < metrics.Length; index++)
        {
            var metric = metrics[index];
            var itemWidth = wide ? columnWidth : Math.Min(metricsWidth, metric.DesiredSize.Width);
            if (index > 0 && (wide ? index % 6 == 0 : left + itemWidth > metricsWidth + 0.1)) { top += rowHeight + 12; left = 0; rowHeight = 0; }
            metric.Measure(new(itemWidth, double.PositiveInfinity));
            _placements.Add((metric, new(left, top, itemWidth, metric.DesiredSize.Height)));
            left += itemWidth + 12; rowHeight = Math.Max(rowHeight, metric.DesiredSize.Height);
        }
        var metricsHeight = top + rowHeight;
        _placements.Add((_action, new(beside ? width - _action.DesiredSize.Width : 0, beside ? 0 : metricsHeight + 12, _action.DesiredSize.Width, _action.DesiredSize.Height)));
        return new(width, beside ? Math.Max(metricsHeight, _action.DesiredSize.Height) : metricsHeight + 12 + _action.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        foreach (var (child, bounds) in _placements) child.Arrange(bounds);
        return final;
    }
}

internal sealed class LogsActionFeedback : ContentControl
{
    private readonly FrameworkElement _owner;
    private readonly TextBlock _message = LogsNativeUi.Text("", 12);
    private readonly Border _symbol = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private Panel? _home;
    private string? _last;
    internal LogsActionFeedback(FrameworkElement owner)
    {
        _owner = owner; Width = 356; HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Bottom;
        var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.Children.Add(_symbol); Grid.SetColumn(_message, 1); row.Children.Add(_message); Content = row; _message.FontWeight = FontWeights.SemiBold;
        owner.SizeChanged += (_, args) => Width = Math.Min(356, Math.Max(0, args.NewSize.Width - 48));
        Padding = new(16); Margin = new(24); CornerRadius = new(12); BorderThickness = new(1); Background = LogsNativeUi.Brush("SurfaceRaisedBrush"); BorderBrush = LogsNativeUi.Brush("BorderBrush"); Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(this, "LogsActionFeedback"); AutomationProperties.SetLiveSetting(this, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _timer.Tick += (_, _) => { _timer.Stop(); Visibility = Visibility.Collapsed; };
    }
    internal void Present(string? error, string? notice)
    {
        var message = error ?? notice;
        if (message == _last) return;
        _last = message;
        if (message == null) return;
        _home ??= Parent as Panel;
        for (DependencyObject? ancestor = _owner; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is AdminShellPage shell && Parent is Panel host && !ReferenceEquals(host, shell.MonitorOverlayRoot)) { host.Children.Remove(this); Grid.SetRowSpan(this, Math.Max(1, shell.MonitorOverlayRoot.RowDefinitions.Count)); shell.MonitorOverlayRoot.Children.Add(this); break; }
        Canvas.SetZIndex(this, 1001); _message.Text = message; _symbol.Child = LogsNativeUi.Icon(error == null ? "circle-check" : "circle-alert", 16); Visibility = Visibility.Visible; _timer.Stop(); _timer.Start();
    }
    internal void Clear()
    {
        _timer.Stop(); _last = null; _message.Text = ""; Visibility = Visibility.Collapsed;
        if (_home != null && Parent is Panel host && !ReferenceEquals(host, _home)) { host.Children.Remove(this); Grid.SetRowSpan(this, 1); _home.Children.Add(this); }
    }
}

internal sealed class LogsNativeTable : Grid
{
    private readonly StackPanel _rows = new();
    private readonly List<Grid> _grids = [];
    private readonly HashSet<int> _endAligned = [];
    private readonly int _columns;
    private Border? _lastRule;
    internal LogsNativeTable(IReadOnlyList<string> headers, double[] widths)
    {
        _columns = widths.Length;
        Children.Add(new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled });
        var heading = Row(headers.Select(label => (UIElement)LogsNativeUi.Cell(label)).ToArray());
        foreach (var label in heading.Children.OfType<Border>().Select(border => border.Child).OfType<TextBlock>()) label.FontWeight = FontWeights.Medium;
        heading.Padding = new(0, 12, 0, 12); _rows.Children.Add(Rule(heading));
    }
    internal void AlignEnd(int column)
    {
        _endAligned.Add(column);
        foreach (var row in _grids) ApplyAlignment(row, column);
    }
    private static void ApplyAlignment(Grid row, int column)
    {
        if (column >= row.Children.Count || row.Children[column] is not Border { Child: FrameworkElement child }) return;
        child.HorizontalAlignment = HorizontalAlignment.Right;
        if (child is TextBlock text) text.TextAlignment = TextAlignment.Right;
    }
    internal void Add(IReadOnlyList<UIElement> cells, Action? select = null, string? name = null, bool highlight = false)
    {
        var grid = Row(cells);
        if (highlight) grid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(13, 167, 139, 250));
        FrameworkElement content = grid;
        if (select != null)
        {
            var button = LogsNativeUi.Button("", name ?? "LogsRow", "GhostButtonStyle"); button.Content = grid; button.Padding = new(0); button.MinHeight = 0; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Stretch; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.BorderThickness = new(0); button.CornerRadius = new(0);
            if (name != null) AutomationProperties.SetName(button, name); button.Click += (_, _) => select(); content = button;
        }
        if (_lastRule != null) _lastRule.BorderThickness = new(0, 0, 0, 1);
        _lastRule = Rule(content); _lastRule.BorderThickness = new(0); _rows.Children.Add(_lastRule);
        InvalidateMeasure();
    }
    internal void AddFull(UIElement content, double height = 112)
    {
        _rows.Children.Add(new Border { Child = content, MinHeight = height, Padding = new(16), HorizontalAlignment = HorizontalAlignment.Stretch });
    }
    protected override Size MeasureOverride(Size available)
    {
        // A CSS auto table shares each column's intrinsic width across every row.
        // Excess viewport width grows columns proportionally; narrow tables scroll
        // instead of assigning a fixed, clipped remainder to the message column.
        var widths = new double[_columns];
        foreach (var row in _grids)
            for (var i = 0; i < row.Children.Count; i++)
            {
                var cell = (FrameworkElement)row.Children[i];
                cell.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                widths[i] = Math.Max(widths[i], cell.DesiredSize.Width);
            }
        var intrinsic = widths.Sum();
        var width = Math.Max(intrinsic, double.IsFinite(available.Width) ? available.Width : intrinsic);
        var ratio = intrinsic > 0 ? width / intrinsic : 1;
        _rows.Width = width;
        foreach (var row in _grids)
            for (var i = 0; i < _columns; i++)
            {
                var next = Math.Max(1, widths[i] * ratio);
                if (Math.Abs(row.ColumnDefinitions[i].Width.Value - next) > 0.01) row.ColumnDefinitions[i].Width = new(next);
            }
        return base.MeasureOverride(available);
    }
    private Grid Row(IReadOnlyList<UIElement> cells)
    {
        var grid = new Grid { Padding = new(0, 8, 0, 8) };
        for (var index = 0; index < _columns; index++) grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        for (var index = 0; index < cells.Count; index++) { var cell = new Border { Child = cells[index], Padding = new(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(cell, index); grid.Children.Add(cell); }
        _grids.Add(grid); foreach (var column in _endAligned) ApplyAlignment(grid, column);
        return grid;
    }
    private static Border Rule(UIElement child) => new() { Child = child, BorderBrush = LogsNativeUi.Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
}

internal sealed class LogsDetailSheet : Grid
{
    private Control? _previousFocus;
    private readonly Border _panel;
    private readonly TextBlock _title = LogsNativeUi.Text("", 16);
    private Panel? _homeParent;
    private int _homeIndex;
    internal bool MonospaceTitle { set => _title.FontFamily = value ? new FontFamily("Consolas") : (FontFamily)Application.Current.Resources["ThemeFontFamily"]; }
    private readonly TextBlock _description = LogsNativeUi.Text("", 14, true);
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Button _close;
    private readonly Control _pageBody;
    internal event Action? Closed;
    internal LogsDetailSheet(Control pageBody, double width)
    {
        _pageBody = pageBody; Visibility = Visibility.Collapsed; Background = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 0, 0, 0));
        var dismiss = new Button { Background = new SolidColorBrush(Colors.Transparent), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderThickness = new(0) };
        AutomationProperties.SetName(dismiss, LogsNativeUi.T("logs.native.closeDetails")); dismiss.Click += (_, _) => Close(); Children.Add(dismiss);
        var layout = new Grid { RowSpacing = 16, TabFocusNavigation = KeyboardNavigationMode.Cycle }; layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var heading = LogsNativeUi.Stack(6); _title.LineHeight = 24; _description.LineHeight = 20; _title.FontWeight = FontWeights.SemiBold; heading.Children.Add(_title); heading.Children.Add(_description); heading.Margin = new(16, 16, 48, 16);
        _close = LogsNativeUi.Button("", "LogsDetailClose", "GhostButtonStyle", "x"); _close.Width = 32; _close.MinHeight = 32; _close.Padding = new(0); _close.UseSystemFocusVisuals = false; _close.GotFocus += (_, _) => _close.UseSystemFocusVisuals = _close.FocusState == FocusState.Keyboard; _close.HorizontalAlignment = HorizontalAlignment.Right; _close.VerticalAlignment = VerticalAlignment.Top; _close.Margin = new(0, 8, 8, 0); AutomationProperties.SetName(_close, LogsNativeUi.T("logs.native.closeDetails")); _close.Click += (_, _) => Close();
        layout.Children.Add(heading); layout.Children.Add(_close); _scroll.Padding = new(16, 0, 16, 24); Grid.SetRow(_scroll, 1); layout.Children.Add(_scroll);
        _panel = new() { Width = width, Background = LogsNativeUi.Brush("AppBackgroundBrush"), BorderBrush = LogsNativeUi.Brush("BorderBrush"), BorderThickness = new(1, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Child = layout };
        Children.Add(_panel); SizeChanged += (_, args) => _panel.Width = Math.Min(width, args.NewSize.Width);
        KeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { Close(); args.Handled = true; } };
    }
    internal void Show(string title, string description, UIElement content)
    {
        _previousFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        if (_homeParent == null && Parent is Panel home)
        {
            for (DependencyObject? ancestor = _pageBody; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
                if (ancestor is AdminShellPage shell && !ReferenceEquals(home, shell.MonitorOverlayRoot))
                {
                    _homeParent = home; _homeIndex = home.Children.IndexOf(this); home.Children.Remove(this);
                    Grid.SetRowSpan(this, Math.Max(1, shell.MonitorOverlayRoot.RowDefinitions.Count)); Canvas.SetZIndex(this, 1000);
                    shell.MonitorOverlayRoot.Children.Add(this); break;
                }
        }
        _title.Text = title; _description.Text = description; _scroll.Content = content; Visibility = Visibility.Visible; _pageBody.IsEnabled = false; _close.Focus(FocusState.Programmatic);
    }
    internal void Update(string title, string description, UIElement content)
    {
        var focus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        var focusId = focus == null ? "" : AutomationProperties.GetAutomationId(focus);
        var offset = _scroll.VerticalOffset;
        _title.Text = title; _description.Text = description; _scroll.Content = content;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Visibility != Visibility.Visible || !ReferenceEquals(_scroll.Content, content)) return;
            _scroll.ChangeView(null, offset, null, true);
            if (focusId.Length > 0 && focusId != "LogsDetailClose")
            {
                Control? Find(DependencyObject parent)
                {
                    if (parent is Control control && control.IsEnabled && AutomationProperties.GetAutomationId(control) == focusId) return control;
                    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) if (Find(VisualTreeHelper.GetChild(parent, index)) is { } match) return match;
                    return null;
                }
                (Find(content) ?? _close).Focus(FocusState.Programmatic);
            }
        });
    }
    internal void Close()
    {
        if (Visibility == Visibility.Collapsed) return;
        Visibility = Visibility.Collapsed; _scroll.Content = null; _title.Text = ""; _description.Text = ""; _pageBody.IsEnabled = true;
        if (_homeParent != null && Parent is Panel host) { host.Children.Remove(this); Grid.SetRowSpan(this, 1); Canvas.SetZIndex(this, 0); _homeParent.Children.Insert(Math.Min(_homeIndex, _homeParent.Children.Count), this); _homeParent = null; } _previousFocus?.Focus(FocusState.Programmatic); _previousFocus = null; Closed?.Invoke();
    }
}

internal sealed class LogsDateTimeInput : Button
{
    private readonly CalendarView _calendar = new() { SelectionMode = CalendarViewSelectionMode.Single };
    private readonly TimePicker _time = new() { ClockIdentifier = "24HourClock" };
    private readonly TextBlock _label = LogsNativeUi.Cell("", true);
    private readonly Button _clear;
    private bool _syncing;
    private DateTimeOffset? _value;
    internal string Value
    {
        get => _value?.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        set { _value = DateTimeOffset.TryParse(value, out var parsed) ? parsed.ToLocalTime() : null; Update(); }
    }
    internal LogsDateTimeInput(string name, string automationId)
    {
        Width = 176; Height = 36; Style = (Style)Application.Current.Resources["OutlineButtonStyle"]; Padding = new(12, 6, 12, 6); HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, name); AutomationProperties.SetAutomationId(this, automationId);
        var content = new Grid { ColumnSpacing = 8 }; content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); content.Children.Add(LogsNativeUi.Icon("calendar")); Grid.SetColumn(_label, 1); content.Children.Add(_label); Content = content;
        var flyoutContent = LogsNativeUi.Stack(12); flyoutContent.Children.Add(_calendar); var timeRow = LogsNativeUi.Inline(8); timeRow.Children.Add(_time); _clear = LogsNativeUi.Button(LogsNativeUi.T("web.common.ui.dateTimePicker.clear"), automationId + "Clear", "GhostButtonStyle"); _clear.Click += (_, _) => { _value = null; Update(); }; timeRow.Children.Add(_clear); flyoutContent.Children.Add(timeRow);
        AutomationProperties.SetName(_time, LogsNativeUi.T("web.common.ui.dateTimePicker.timeOfDay")); Flyout = new Flyout { Content = flyoutContent };
        _calendar.SelectedDatesChanged += (_, _) => { if (_syncing) return; var selected = _calendar.SelectedDates.FirstOrDefault(); _value = selected == default ? null : new DateTimeOffset(selected.Date + (_value?.TimeOfDay ?? TimeSpan.Zero), selected.Offset); Update(); };
        _time.TimeChanged += (_, _) => { if (_syncing) return; var day = _value ?? DateTimeOffset.Now; _value = new DateTimeOffset(day.Date + _time.Time, day.Offset); Update(); };
        Update();
    }
    private void Update()
    {
        _syncing = true;
        try { _calendar.SelectedDates.Clear(); if (_value != null) { _calendar.SelectedDates.Add(_value.Value); _calendar.SetDisplayDate(_value.Value); } _time.Time = _value?.TimeOfDay ?? TimeSpan.Zero; _label.Text = _value == null ? LogsNativeUi.T("web.common.ui.dateTimePicker.anyTime") : $"{SiloPlayer.Helpers.DateTimeDisplay.FormatDate(_value.Value)}, {SiloPlayer.Helpers.DateTimeDisplay.FormatTime(_value.Value, seconds: false)}"; _clear.IsEnabled = _value != null; }
        finally { _syncing = false; }
    }
}
