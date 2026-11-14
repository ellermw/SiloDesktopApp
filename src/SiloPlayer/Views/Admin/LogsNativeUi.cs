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
        var block = new TextBlock { Text = text, FontSize = size, Foreground = Brush(muted ? "SecondaryTextBrush" : "PrimaryTextBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (mono) block.FontFamily = new FontFamily("Consolas");
        return block;
    }
    internal static StackPanel Stack(double spacing = 0) => new() { Spacing = spacing };
    internal static StackPanel Inline(double spacing = 8) => new() { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
    internal static Border Panel(UIElement child, double radius = 16, double padding = 12, bool border = false) => new()
    {
        Child = child, CornerRadius = new(radius), Padding = new(padding), Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new(border ? 1 : 0)
    };
    internal static Button Button(string label, string key, string style = "OutlineButtonStyle", string? icon = null)
    {
        var button = new Button { Content = label, MinHeight = 36, Padding = new(12, 6, 12, 6), CornerRadius = new(6), FontSize = 14, Style = (Style)Application.Current.Resources[style] };
        if (icon != null) { var content = Inline(8); content.Children.Add(Icon(icon)); content.Children.Add(Text(label)); button.Content = content; }
        AutomationProperties.SetName(button, label); AutomationProperties.SetAutomationId(button, key);
        return button;
    }
    internal static TextBox Input(string placeholder, string key, double width = 320, bool mono = false)
    {
        var input = new TextBox { PlaceholderText = placeholder, Width = width, MinWidth = 80, Height = 36, FontSize = 14, Padding = new(12, 6, 12, 6), CornerRadius = new(6), Background = Brush("InputBrush"), BorderBrush = Brush("BorderBrush") };
        if (mono) input.FontFamily = new("Consolas");
        AutomationProperties.SetName(input, placeholder); AutomationProperties.SetAutomationId(input, key);
        return input;
    }
    internal static ComboBox Select(string key, IEnumerable<(string Value, string Label)> options, double width = 170)
    {
        var select = new ComboBox { Width = width, Height = 36, FontSize = 14, CornerRadius = new(6), Padding = new(12, 5, 12, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch };
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
    internal static Grid Header(string title, string subtitle, FrameworkElement? actions = null)
    {
        var header = new Grid { ColumnSpacing = 24, RowSpacing = 0, Margin = new(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.RowDefinitions.Add(new() { Height = GridLength.Auto }); header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var titles = Stack(8); var heading = Text(title, 32); heading.FontWeight = FontWeights.Bold; heading.CharacterSpacing = -35;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        titles.Children.Add(heading); if (subtitle.Length > 0) { var description = Text(subtitle, 14, true); description.MaxWidth = 800; description.HorizontalAlignment = HorizontalAlignment.Left; titles.Children.Add(description); }
        header.Children.Add(titles);
        if (actions != null) { Grid.SetColumn(actions, 1); actions.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(actions); }
        header.SizeChanged += (_, args) =>
        {
            heading.FontSize = args.NewSize.Width < 640 ? 28 : 32;
            if (actions == null) return;
            var wrap = args.NewSize.Width < 640;
            header.RowSpacing = wrap ? 12 : 0;
            Grid.SetColumn(actions, wrap ? 0 : 1); Grid.SetRow(actions, wrap ? 1 : 0);
            Grid.SetColumnSpan(titles, wrap ? 2 : 1);
        };
        return header;
    }
    internal static StackPanel Metric(string label, string value, bool mono = false)
    {
        var metric = Stack(4); metric.Children.Add(Text(label, 12, true)); metric.Children.Add(Text(string.IsNullOrEmpty(value) ? "—" : value, mono ? 12 : 14, mono: mono)); return metric;
    }
    internal static TextBlock Cell(string text, bool muted = false, bool mono = false, double max = double.PositiveInfinity)
    {
        var cell = Text(text, mono ? 12 : 14, muted, mono); cell.TextWrapping = TextWrapping.NoWrap; cell.TextTrimming = TextTrimming.CharacterEllipsis; cell.MaxWidth = max;
        ToolTipService.SetToolTip(cell, text); return cell;
    }
    internal static UIElement Icon(string name, double size = 16)
    {
        var data = name switch
        {
            "triangle-alert" => "m10.3 3.86-8.48 14.14A2 2 0 0 0 3.53 21h16.94a2 2 0 0 0 1.71-3L13.7 3.86a2 2 0 0 0-3.4 0 M12 9v4 M12 17h.01",
            "bug" => "m8 2 1.88 1.88 m4.24 0L16 2 M9 7.13V6a3 3 0 0 1 6 0v1.13 M12 20v-9 M4 5l3 3 M17 8l3-3 M2 12h6 M16 12h6 M3 19l4-4 M17 15l4 4 M8 7h8v9a4 4 0 0 1-8 0z",
            "filter-x" => "M22 3H2l8 9.46V19l4 2v-8.54 M16 16l5 5 M21 16l-5 5",
            "calendar" => "M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2",
            "trash-2" => "M3 6h18 M19 6l-1 14H6L5 6 M9 6V3h6v3 M10 10v6 M14 10v6",
            _ => null
        };
        if (data == null) return name == "download" ? WebUiIcon.Navigation(name, size) : WebUiIcon.Create(name, size);
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='" + data + "' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>");
        path.Stroke = Brush("PrimaryTextBrush"); var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(path); return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
    internal static Border Banner(string message, bool error = false)
    {
        var row = Inline(12); row.Children.Add(Icon("triangle-alert")); var label = Text(message, 14, !error); if (error) label.Foreground = Brush("ErrorBrush"); row.Children.Add(label);
        return Panel(row, 12, 14, true);
    }
}

internal sealed class LogsWrapPanel : Panel
{
    internal double Spacing { get; set; } = 8;
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1560 : Math.Max(0, availableSize.Width);
        double left = 0, top = 0, rowHeight = 0, used = 0;
        foreach (var child in Children)
        {
            child.Measure(new(width, availableSize.Height)); var desired = child.DesiredSize;
            if (left > 0 && left + desired.Width > width) { top += rowHeight + Spacing; left = 0; rowHeight = 0; }
            used = Math.Max(used, left + Math.Min(width, desired.Width)); left += desired.Width + Spacing; rowHeight = Math.Max(rowHeight, desired.Height);
        }
        return new(Math.Min(width, used), top + rowHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double left = 0, top = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            if (left > 0 && left + desired.Width > finalSize.Width) { top += rowHeight + Spacing; left = 0; rowHeight = 0; }
            child.Arrange(new(left, top, Math.Min(finalSize.Width, desired.Width), desired.Height)); left += desired.Width + Spacing; rowHeight = Math.Max(rowHeight, desired.Height);
        }
        return finalSize;
    }
}

internal sealed class LogsNativeTable : Grid
{
    private readonly StackPanel _rows = new();
    private readonly double[] _widths;
    internal LogsNativeTable(IReadOnlyList<string> headers, double[] widths)
    {
        _widths = widths;
        Children.Add(new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled });
        var heading = Row(headers.Select(label => (UIElement)LogsNativeUi.Cell(label)).ToArray());
        foreach (var label in heading.Children.OfType<Border>().Select(border => border.Child).OfType<TextBlock>()) label.FontWeight = FontWeights.Medium;
        heading.MinHeight = 44; _rows.Children.Add(Rule(heading));
        SizeChanged += (_, args) => _rows.Width = Math.Max(args.NewSize.Width, widths.Sum(width => width > 0 ? width : 120));
    }
    internal void Add(IReadOnlyList<UIElement> cells, Action? select = null, string? name = null, bool highlight = false)
    {
        var grid = Row(cells); grid.MinHeight = 40;
        if (highlight) grid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(13, 167, 139, 250));
        FrameworkElement content = grid;
        if (select != null)
        {
            var button = LogsNativeUi.Button("", name ?? "LogsRow", "GhostButtonStyle"); button.Content = grid; button.Padding = new(0); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.BorderThickness = new(0); button.CornerRadius = new(0);
            if (name != null) AutomationProperties.SetName(button, name); button.Click += (_, _) => select(); content = button;
        }
        _rows.Children.Add(Rule(content));
    }
    internal void AddFull(UIElement content, double height = 112)
    {
        var row = new Border { Child = content, MinHeight = height, Padding = new(16), HorizontalAlignment = HorizontalAlignment.Stretch }; _rows.Children.Add(row);
    }
    private Grid Row(IReadOnlyList<UIElement> cells)
    {
        var grid = new Grid { Padding = new(0, 8, 0, 8) };
        for (var index = 0; index < _widths.Length; index++) grid.ColumnDefinitions.Add(new() { Width = _widths[index] <= 0 ? new(1, GridUnitType.Star) : new(_widths[index]) });
        for (var index = 0; index < cells.Count; index++) { var cell = new Border { Child = cells[index], Padding = new(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(cell, index); grid.Children.Add(cell); }
        return grid;
    }
    private static Border Rule(UIElement child) => new() { Child = child, BorderBrush = LogsNativeUi.Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
}

internal sealed class LogsDetailSheet : Grid
{
    private Control? _previousFocus;
    private readonly Border _panel;
    private readonly TextBlock _title = LogsNativeUi.Text("", 18);
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
        var heading = LogsNativeUi.Stack(8); _title.FontWeight = FontWeights.SemiBold; heading.Children.Add(_title); heading.Children.Add(_description); heading.Margin = new(16, 16, 48, 0);
        _close = LogsNativeUi.Button("", "LogsDetailClose", "GhostButtonStyle", "x"); _close.Width = 32; _close.MinHeight = 32; _close.HorizontalAlignment = HorizontalAlignment.Right; _close.VerticalAlignment = VerticalAlignment.Top; _close.Margin = new(0, 8, 8, 0); AutomationProperties.SetName(_close, LogsNativeUi.T("logs.native.closeDetails")); _close.Click += (_, _) => Close();
        layout.Children.Add(heading); layout.Children.Add(_close); _scroll.Padding = new(16, 0, 16, 32); Grid.SetRow(_scroll, 1); layout.Children.Add(_scroll);
        _panel = new() { Width = width, Background = LogsNativeUi.Brush("PopoverBrush"), BorderBrush = LogsNativeUi.Brush("BorderBrush"), BorderThickness = new(1, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Child = layout };
        Children.Add(_panel); SizeChanged += (_, args) => _panel.Width = Math.Min(width, args.NewSize.Width);
        KeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { Close(); args.Handled = true; } };
    }
    internal void Show(string title, string description, UIElement content)
    {
        _previousFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        _title.Text = title; _description.Text = description; _scroll.Content = content; Visibility = Visibility.Visible; _pageBody.IsEnabled = false; _close.Focus(FocusState.Programmatic);
    }
    internal void Update(string title, string description, UIElement content) { _title.Text = title; _description.Text = description; _scroll.Content = content; }
    internal void Close()
    {
        if (Visibility == Visibility.Collapsed) return;
        Visibility = Visibility.Collapsed; _scroll.Content = null; _title.Text = ""; _description.Text = ""; _pageBody.IsEnabled = true; _previousFocus?.Focus(FocusState.Programmatic); _previousFocus = null; Closed?.Invoke();
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
        try { _calendar.SelectedDates.Clear(); if (_value != null) { _calendar.SelectedDates.Add(_value.Value); _calendar.SetDisplayDate(_value.Value); } _time.Time = _value?.TimeOfDay ?? TimeSpan.Zero; _label.Text = _value == null ? LogsNativeUi.T("web.common.ui.dateTimePicker.anyTime") : AdminLogsPage.Date(_value); _clear.IsEnabled = _value != null; }
        finally { _syncing = false; }
    }
}
