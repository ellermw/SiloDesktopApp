using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Core.Services.Admin;
using Windows.Foundation;
using Windows.UI;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Controls.Admin;

internal sealed record DashboardLegendEntry(string Label, Color Color);

internal static class DashboardChartChrome
{
    /// <summary>Legend for two or more series; the swatch mirrors the mark, labels wear text tokens.</summary>
    public static FrameworkElement? Legend(IReadOnlyList<DashboardLegendEntry> entries, bool lineShape, Thickness margin)
    {
        if (entries.Count < 2) return null;
        var panel = new DashboardWrapPanel { Spacing = 12, RowSpacing = 4, Margin = margin };
        foreach (var entry in entries)
        {
            var swatch = new Border
            {
                Width = lineShape ? 12 : 8,
                Height = lineShape ? 2 : 8,
                CornerRadius = new CornerRadius(lineShape ? 999 : 2),
                Background = B(entry.Color),
                VerticalAlignment = VerticalAlignment.Center,
            };
            panel.Children.Add(Row(6, swatch, Text(entry.Label, 11, color: MutedForeground)));
        }
        return panel;
    }

    /// <summary>Hover/focus readout: popover/95, border, rounded-md, 10px title.</summary>
    public static Border Tooltip(string title, IEnumerable<(Color? Swatch, string Label, string Value)> rows, (string Label, string Value)? total = null)
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 10, Foreground = B(MutedForeground), Margin = new Thickness(0, 0, 0, 4) });
        foreach (var (swatch, label, value) in rows) stack.Children.Add(TooltipRow(swatch, label, value));
        if (total is { } t)
            stack.Children.Add(new Border
            {
                BorderBrush = B(BorderColor, 0.6),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(0, 4, 0, 0),
                Child = TooltipRow(null, t.Label, t.Value),
            });
        return new Border
        {
            Background = B(Popover, 0.95),
            BorderBrush = B(BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6),
            Child = stack,
            IsHitTestVisible = false,
        };
    }

    private static FrameworkElement TooltipRow(Color? swatch, string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (swatch is { } color)
            grid.Children.Add(new Border { Width = 12, Height = 2, CornerRadius = new CornerRadius(1.5), Background = B(color), VerticalAlignment = VerticalAlignment.Center });
        var labelText = Text(label, 11, color: MutedForeground);
        Grid.SetColumn(labelText, 1);
        grid.Children.Add(labelText);
        var valueText = Text(value, 11, Semibold);
        valueText.HorizontalAlignment = HorizontalAlignment.Right;
        valueText.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(valueText, 2);
        grid.Children.Add(valueText);
        return grid;
    }

    /// <summary>Positions a tooltip at a ratio of the plot width, shifted to stay inside near the edges.</summary>
    public static void PlaceTooltip(Canvas host, Border tooltip, double ratio, double width)
    {
        tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tipWidth = tooltip.DesiredSize.Width;
        ratio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0, 1) : 0;
        var x = ratio * width;
        var left = ratio < 0.15 ? x : ratio > 0.85 ? x - tipWidth : x - tipWidth / 2;
        Canvas.SetLeft(tooltip, left);
        Canvas.SetTop(tooltip, 0);
        Canvas.SetZIndex(tooltip, 10);
        host.Children.Add(tooltip);
    }

    /// <summary>"Collecting data — samples since …" or the widget's empty message, centered.</summary>
    public static FrameworkElement EmptyState(string message, string? detail, DateTimeOffset? since, DateTimeOffset now, double minHeight)
    {
        var collecting = since is { } s ? DashboardFormat.RelativeTime(s, now) : null;
        var headline = collecting != null ? DashboardText.T("web.admin.dashboard.chart.collectingData") : message;
        var sub = collecting != null ? DashboardText.T("web.admin.dashboard.chart.samplesSince", ("since", collecting)) : detail;
        var stack = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = headline, FontSize = 14, Foreground = B(MutedForeground), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.WrapWholeWords });
        if (!string.IsNullOrEmpty(sub))
            stack.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = B(MutedForeground), Opacity = 0.8, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.WrapWholeWords });
        return new Grid { MinHeight = minHeight, Children = { stack } };
    }

    public static FrameworkElement ChartSkeleton(double minHeight)
    {
        var skeleton = Skeleton(minHeight, 6);
        skeleton.Height = double.NaN;
        skeleton.MinHeight = minHeight;
        skeleton.VerticalAlignment = VerticalAlignment.Stretch;
        return skeleton;
    }

    public static FrameworkElement BarListSkeleton(int rows = 5)
    {
        var stack = new StackPanel { Spacing = 8 };
        for (var i = 0; i < rows; i++) stack.Children.Add(Skeleton(28, 6));
        return stack;
    }
}

/// <summary>
/// Time line with an area wash, gaps where samples are missing, an optional
/// stack of bands or overlay lines, a snapping crosshair and arrow-key
/// inspection. One value axis; time always runs left to right.
/// </summary>
internal sealed class DashboardLineChart : Grid
{
    public required IReadOnlyList<DashboardChartPoint> Points { get; init; }
    public IReadOnlyList<(string Label, IReadOnlyList<DashboardChartPoint> Points, int Slot)> Overlays { get; init; } = [];
    public IReadOnlyList<DashboardChartBand> Bands { get; init; } = [];
    public string SeriesLabel { get; init; } = DashboardText.T("web.admin.dashboard.chart.value");
    public Func<double, string> FormatValue { get; init; } = value => DashboardFormat.Integer((long)Math.Round(value));
    public Func<double, string>? FormatTick { get; init; }
    public required Func<DateTimeOffset, string> FormatTimestamp { get; init; }
    public (string Start, string End)? EdgeLabels { get; init; }
    public double? MinTickStep { get; init; }
    public required string AccessibleLabel { get; init; }
    /// <summary>Fixed plot height; NaN fills the parent.</summary>
    public double PlotHeight { get; init; } = double.NaN;
    public double MinPlotHeight { get; init; }

    private readonly Canvas _ticks = new() { Width = 40 };
    private readonly Canvas _plot = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly ContentControl _hit = new() { IsTabStop = true, UseSystemFocusVisuals = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private IReadOnlyList<double> _tickValues = [];
    private double _top = 1;
    private double[] _ratios = [];
    private int? _active;

    private Color MainColor => Chart(0);
    private bool Stacked => Bands.Count > 0;

    public void Build()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = double.IsNaN(PlotHeight) ? new GridLength(1, GridUnitType.Star) : new GridLength(PlotHeight) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var legendEntries = Stacked
            ? Bands.Reverse().Select(band => new DashboardLegendEntry(band.Label, BandColor(band))).ToList()
            : Overlays.Count > 0
                ? [new DashboardLegendEntry(SeriesLabel, MainColor), .. Overlays.Select(o => new DashboardLegendEntry(o.Label, Chart(o.Slot)))]
                : [];
        if (DashboardChartChrome.Legend(legendEntries, lineShape: true, new Thickness(48, 0, 0, 6)) is { } legend) Children.Add(legend);

        var plotRow = new Grid { ColumnSpacing = 8, MinHeight = MinPlotHeight };
        plotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        plotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        plotRow.Children.Add(_ticks);
        var plotHost = new Grid();
        plotHost.Children.Add(_plot);
        plotHost.Children.Add(_overlay);
        plotHost.Children.Add(_hit);
        _hit.Content = new Border { Background = B(Colors.Transparent) };
        Grid.SetColumn(plotHost, 1);
        plotRow.Children.Add(plotHost);
        Grid.SetRow(plotRow, 1);
        Children.Add(plotRow);
        AutomationProperties.SetName(_hit, DashboardText.T("web.admin.dashboard.chart.inspectSamples", ("chart", AccessibleLabel)));
        AutomationProperties.SetName(this, AccessibleLabel);

        if (Points.Count > 0)
        {
            var edges = new Grid { Margin = new Thickness(48, 6, 0, 0) };
            var start = Text(EdgeLabels?.Start ?? FormatTimestamp(Points[0].T), 10, color: MutedForeground);
            var end = Text(EdgeLabels?.End ?? FormatTimestamp(Points[^1].T), 10, color: MutedForeground);
            end.HorizontalAlignment = HorizontalAlignment.Right;
            edges.Children.Add(start);
            edges.Children.Add(end);
            Grid.SetRow(edges, 2);
            Children.Add(edges);
        }

        ComputeScale();
        plotHost.SizeChanged += (_, e) => Draw(e.NewSize.Width, e.NewSize.Height);
        _hit.PointerMoved += (_, e) =>
        {
            var width = _plot.ActualWidth;
            if (width <= 0) return;
            SetActive(Nearest(e.GetCurrentPoint(_plot).Position.X / width));
        };
        _hit.PointerExited += (_, _) => SetActive(null);
        _hit.LostFocus += (_, _) => SetActive(null);
        _hit.KeyDown += OnKeyDown;
    }

    private Color BandColor(DashboardChartBand band) => band.Neutral ? MutedForeground : Chart(band.SeriesSlot ?? 0);

    private void ComputeScale()
    {
        var values = Points.Select(p => p.Value).Concat(Overlays.SelectMany(o => o.Points.Select(p => p.Value)))
            .Where(v => v is { } x && double.IsFinite(x)).Select(v => v!.Value).ToList();
        _tickValues = DashboardChartMath.NiceTicks(0, values.Count > 0 ? values.Max() : 0, 3, MinTickStep);
        _top = _tickValues.Count > 0 && _tickValues[^1] != 0 ? _tickValues[^1] : 1;
        var first = Points.Count > 0 ? Points[0].T.ToUnixTimeMilliseconds() : 0;
        var last = Points.Count > 0 ? Points[^1].T.ToUnixTimeMilliseconds() : 0;
        var span = last - first;
        _ratios = Points.Select((point, index) => Points.Count <= 1 ? 0.5
            : span <= 0 ? index / (double)(Points.Count - 1)
            : (point.T.ToUnixTimeMilliseconds() - first) / (double)span).ToArray();
    }

    private IReadOnlyList<int> Plotted => Enumerable.Range(0, Points.Count)
        .Where(i => Points[i].Value != null || Overlays.Any(o => i < o.Points.Count && o.Points[i].Value != null)).ToArray();

    private int? Nearest(double ratio)
    {
        int? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var index in Plotted)
        {
            var distance = Math.Abs(_ratios[index] - ratio);
            if (distance < bestDistance) { bestDistance = distance; best = index; }
        }
        return best;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)) return;
        var order = Plotted;
        if (order.Count == 0) return;
        e.Handled = true;
        var current = _active is { } a ? order.ToList().IndexOf(a) : -1;
        var step = e.Key == Windows.System.VirtualKey.Right ? 1 : -1;
        var next = current == -1 ? order.Count - 1 : Math.Clamp(current + step, 0, order.Count - 1);
        SetActive(order[next]);
    }

    private void SetActive(int? index)
    {
        if (_active == index) return;
        _active = index;
        DrawOverlay();
    }

    private double Y(double value, double height) => height - value / _top * height;

    private void Draw(double width, double height)
    {
        _plot.Children.Clear();
        _ticks.Children.Clear();
        if (width <= 0 || height <= 0) return;
        foreach (var tick in _tickValues)
        {
            var y = (1 - tick / _top) * height;
            _plot.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = B(BorderColor), StrokeThickness = 1, Opacity = tick == 0 ? 0.9 : 0.5 });
            var label = Text((FormatTick ?? FormatValue)(tick), 10, color: MutedForeground, lineHeight: 10);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, 40 - label.DesiredSize.Width);
            Canvas.SetTop(label, y - label.DesiredSize.Height / 2);
            _ticks.Children.Add(label);
        }
        List<(double, double?)> Project(IReadOnlyList<DashboardChartPoint> series)
            => series.Select((point, i) => (i < _ratios.Length ? _ratios[i] * width : 0, point.Value is { } v && double.IsFinite(v) ? Y(v, height) : (double?)null)).ToList();

        if (Stacked)
        {
            var baseline = Points.Select(p => p.Value == null ? (double?)null : 0d).ToArray();
            foreach (var band in Bands)
            {
                var top = baseline.Select((below, i) => below is { } b && i < band.Points.Count && band.Points[i].Value is { } v ? b + v : (double?)null).ToArray();
                var color = BandColor(band);
                var upper = top.Select((v, i) => (_ratios[i] * width, v is { } x ? Y(x, height) : (double?)null)).ToList();
                var lower = baseline.Select((v, i) => (_ratios[i] * width, v is { } x ? Y(x, height) : (double?)null)).ToList();
                AddBand(upper, lower, color);
                AddLines(upper, color);
                baseline = top;
            }
        }
        else
        {
            var projected = Project(Points);
            foreach (var run in DashboardChartMath.Runs(projected))
            {
                var polygon = new Polygon { Fill = B(MainColor), Opacity = 0.12 };
                polygon.Points.Add(new Point(run[0].X, height));
                foreach (var (x, y) in run) polygon.Points.Add(new Point(x, y));
                polygon.Points.Add(new Point(run[^1].X, height));
                _plot.Children.Add(polygon);
            }
            AddLines(projected, MainColor);
        }
        foreach (var overlay in Overlays) AddLines(Project(overlay.Points), Chart(overlay.Slot));
        DrawOverlay();
    }


    private void AddLines(IReadOnlyList<(double X, double? Y)> points, Color color)
    {
        foreach (var run in DashboardChartMath.Runs(points))
        {
            var line = new Polyline
            {
                Stroke = B(color),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            foreach (var (x, y) in run) line.Points.Add(new Point(x, y));
            if (run.Count == 1) line.Points.Add(new Point(run[0].X + 0.01, run[0].Y));
            _plot.Children.Add(line);
        }
    }

    private void AddBand(IReadOnlyList<(double X, double? Y)> upper, IReadOnlyList<(double X, double? Y)> lower, Color color)
    {
        var run = new List<(double X, double Top, double Bottom)>();
        void Flush()
        {
            if (run.Count == 0) return;
            var polygon = new Polygon { Fill = B(color), Opacity = 0.12 };
            foreach (var p in run) polygon.Points.Add(new Point(p.X, p.Top));
            for (var i = run.Count - 1; i >= 0; i--) polygon.Points.Add(new Point(run[i].X, run[i].Bottom));
            _plot.Children.Add(polygon);
            run.Clear();
        }
        for (var i = 0; i < upper.Count; i++)
        {
            if (upper[i].Y is { } top && i < lower.Count && lower[i].Y is { } bottom) run.Add((upper[i].X, top, bottom));
            else Flush();
        }
        Flush();
    }

    private void DrawOverlay()
    {
        _overlay.Children.Clear();
        var width = _plot.ActualWidth;
        var height = _plot.ActualHeight;
        if (_active is not { } index || index >= Points.Count || width <= 0) return;
        var main = Points[index].Value;
        var overlayValues = Overlays.Select(o => index < o.Points.Count ? o.Points[index].Value : null).ToList();
        if (main == null && overlayValues.All(v => v == null)) return;
        var x = _ratios[index] * width;
        _overlay.Children.Add(new Rectangle { Width = 1, Height = height, Fill = B(BorderColor) });
        Canvas.SetLeft(_overlay.Children[^1], x);
        void Dot(double value, Color color)
        {
            var dot = new Ellipse { Width = 12, Height = 12, Fill = B(color), Stroke = B(Card), StrokeThickness = 2 };
            Canvas.SetLeft(dot, x - 6);
            Canvas.SetTop(dot, Y(value, height) - 6);
            _overlay.Children.Add(dot);
        }
        var rows = new List<(Color?, string, string)>();
        if (Stacked)
        {
            double cumulative = 0;
            foreach (var band in Bands)
            {
                if (index < band.Points.Count && band.Points[index].Value is { } v)
                {
                    cumulative += v;
                    Dot(cumulative, BandColor(band));
                }
            }
            foreach (var (band, value) in Bands.Select(b => (b, index < b.Points.Count ? b.Points[index].Value : null))
                         .Where(pair => pair.Item2 != null).OrderByDescending(pair => pair.Item2))
                rows.Add((BandColor(band), band.Label, FormatValue(value!.Value)));
            if (main is { } total) rows.Add((null, DashboardText.T("web.admin.dashboard.chart.total"), FormatValue(total)));
        }
        else
        {
            if (main is { } value)
            {
                Dot(value, MainColor);
                rows.Add((MainColor, SeriesLabel, FormatValue(value)));
            }
        }
        for (var i = 0; i < Overlays.Count; i++)
            if (overlayValues[i] is { } v) rows.Add((Chart(Overlays[i].Slot), Overlays[i].Label, FormatValue(v)));
        DashboardChartChrome.PlaceTooltip(_overlay, DashboardChartChrome.Tooltip(FormatTimestamp(Points[index].T), rows), _ratios[index], width);
    }
}

/// <summary>Baseline-anchored stacked columns: 2px gaps, rounded top segment, per-column readout.</summary>
internal sealed class DashboardStackedColumnChart : Grid
{
    public required IReadOnlyList<DashboardChartColumn> Columns { get; init; }
    public required IReadOnlyList<string> SeriesLabels { get; init; }
    public required Func<DateTimeOffset, string> FormatBucket { get; init; }
    public Func<double, string> FormatValue { get; init; } = value => DashboardFormat.Integer((long)Math.Round(value));
    public string? TotalLabel { get; init; }
    public required string AccessibleLabel { get; init; }
    public double PlotHeight { get; init; } = double.NaN;
    public double MinPlotHeight { get; init; }

    private readonly Grid _bars = new() { ColumnSpacing = 2 };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private int? _active;
    private double _scaleMax = 1;

    public void Build()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = double.IsNaN(PlotHeight) ? new GridLength(1, GridUnitType.Star) : new GridLength(PlotHeight) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (DashboardChartChrome.Legend(SeriesLabels.Select((label, i) => new DashboardLegendEntry(label, Chart(i))).ToList(), false,
                new Thickness(0, 0, 0, 10)) is { } legend) Children.Add(legend);
        _scaleMax = Math.Max(1, Columns.Count > 0 ? Columns.Max(c => c.Total) : 0);
        var plot = new Grid { MinHeight = MinPlotHeight };
        plot.Children.Add(_bars);
        plot.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = B(BorderColor, 0.7), IsHitTestVisible = false });
        plot.Children.Add(_overlay);
        Grid.SetRow(plot, 1);
        Children.Add(plot);
        AutomationProperties.SetName(this, AccessibleLabel);
        for (var i = 0; i < Columns.Count; i++) _bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        plot.SizeChanged += (_, e) => DrawBars(e.NewSize.Height);
        _bars.PointerExited += (_, _) => SetActive(null);

        var ticks = new Canvas { Height = 14, Margin = new Thickness(0, 6, 0, 0) };
        var stride = Math.Max(1, (int)Math.Ceiling(Columns.Count / 4.0));
        ticks.SizeChanged += (_, e) =>
        {
            ticks.Children.Clear();
            for (var i = 0; i < Columns.Count; i += stride)
            {
                var ratio = (i + 0.5) / Columns.Count;
                var label = Text(FormatBucket(Columns[i].T), 10, color: MutedForeground, lineHeight: 14);
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var w = label.DesiredSize.Width;
                var x = ratio * e.NewSize.Width;
                Canvas.SetLeft(label, ratio < 0.1 ? x : ratio > 0.9 ? x - w : x - w / 2);
                ticks.Children.Add(label);
            }
        };
        Grid.SetRow(ticks, 2);
        Children.Add(ticks);
    }

    private void DrawBars(double height)
    {
        _bars.Children.Clear();
        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];
            var index = i;
            var segments = DashboardChartMath.StackSegments(column.Segments.Take(SeriesLabels.Count).Concat(Enumerable.Repeat(0d, Math.Max(0, SeriesLabels.Count - column.Segments.Count))).ToArray());
            var positiveCount = segments.Count(s => s.Value > 0);
            var segmentBudget = Math.Max(0, height - 2 * Math.Max(0, positiveCount - 1));
            var topIndex = segments.Where(s => s.Value > 0).Select(s => s.Index).DefaultIfEmpty(-1).Last();
            var stack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var segment in segments.Reverse())
            {
                if (segment.Value <= 0) continue;
                stack.Children.Add(new Border
                {
                    Height = segment.Value / _scaleMax * segmentBudget,
                    Background = B(Chart(segment.Index)),
                    CornerRadius = segment.Index == topIndex ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0),
                });
            }
            var cell = new ContentControl
            {
                IsTabStop = true,
                UseSystemFocusVisuals = true,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = new Border { CornerRadius = new CornerRadius(4), Background = B(_active == index ? Alpha(Foreground, 0.05) : Colors.Transparent), Child = stack },
            };
            AutomationProperties.SetName(cell, DashboardText.T("web.admin.dashboard.chart.columnReading",
                ("bucket", FormatBucket(column.T)), ("value", FormatValue(column.Total))));
            cell.PointerEntered += (_, _) => SetActive(index);
            cell.GotFocus += (_, _) => SetActive(index);
            cell.LostFocus += (_, _) => SetActive(null);
            Grid.SetColumn(cell, i);
            _bars.Children.Add(cell);
        }
        DrawTooltip();
    }

    private void SetActive(int? index)
    {
        if (_active == index) return;
        _active = index;
        DrawBars(_bars.ActualHeight);
    }

    private void DrawTooltip()
    {
        _overlay.Children.Clear();
        if (_active is not { } index || index >= Columns.Count) return;
        var column = Columns[index];
        var rows = SeriesLabels.Select((label, i) => ((Color?)Chart(i), label, FormatValue(i < column.Segments.Count ? Math.Max(0, column.Segments[i]) : 0)));
        var tooltip = DashboardChartChrome.Tooltip(FormatBucket(column.T), rows,
            TotalLabel == "" ? null : (TotalLabel ?? DashboardText.T("web.admin.dashboard.chart.total"), FormatValue(column.Total)));
        DashboardChartChrome.PlaceTooltip(_overlay, tooltip, (index + 0.5) / Columns.Count, _bars.ActualWidth);
    }
}

internal sealed record DashboardBarItem(string Id, string Label, double Value, string? Secondary = null, Action? Navigate = null);

/// <summary>Ranked single-hue bars for top-N lists; the track encodes magnitude.</summary>
internal static class DashboardBarList
{
    public static FrameworkElement Create(IReadOnlyList<DashboardBarItem> items, Func<double, string> formatValue, string emptyLabel)
    {
        if (items.Count == 0) return EmptyLine(emptyLabel);
        var color = Chart(0);
        var max = items.Where(i => double.IsFinite(i.Value)).Select(i => i.Value).DefaultIfEmpty(0).Max();
        var list = new StackPanel { Spacing = 4 };
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var value = double.IsFinite(item.Value) ? Math.Max(item.Value, 0) : 0;
            var ratio = max > 0 ? value / max : 0;
            var row = new Grid { Padding = new Thickness(8, 6, 8, 6), ColumnSpacing = 8, CornerRadius = new CornerRadius(4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var track = new Border { Background = B(color), Opacity = 0.18, CornerRadius = new CornerRadius(0, 4, 4, 0), HorizontalAlignment = HorizontalAlignment.Left };
            // Decorative width must not feed back into the measured row width.
            var background = new Canvas { IsHitTestVisible = false };
            Canvas.SetLeft(track, -8);
            Canvas.SetTop(track, -6);
            background.Children.Add(track);
            Grid.SetColumnSpan(background, 4);
            row.Children.Add(background);
            row.SizeChanged += (_, e) => { track.Width = Math.Max(value > 0 ? 6 : 0, ratio * e.NewSize.Width); track.Height = Math.Max(0, e.NewSize.Height); };
            var rank = Text((index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), 11, color: MutedForeground);
            rank.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(rank);
            FrameworkElement label = item.Navigate is { } go ? TextLink(item.Label, 14, (_, _) => go(), Foreground) : Text(item.Label, 14, trim: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            ToolTipService.SetToolTip(label, item.Label);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var valueText = Text(formatValue(value), 13, Semibold);
            valueText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);
            if (!string.IsNullOrEmpty(item.Secondary))
            {
                var secondary = Text(item.Secondary, 11, color: MutedForeground);
                secondary.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(secondary, 3);
                row.Children.Add(secondary);
            }
            list.Children.Add(row);
        }
        return list;
    }
}

/// <summary>The load bar slot: no bar for a null fill, a stub for a measured zero.</summary>
internal static class DashboardLoadMeter
{
    public static FrameworkElement Create(int? fill, bool warning = false, double width = double.NaN)
    {
        var slot = new Grid { Height = 3, Width = width, VerticalAlignment = VerticalAlignment.Center };
        if (fill is not { } value) return slot;
        var track = new Border { Background = B(BorderColor, 0.7), CornerRadius = new CornerRadius(1.5) };
        var bar = new Border { Background = warning ? B(DashboardUi.Warning) : B(Foreground, 0.55), CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 3 };
        slot.Children.Add(track);
        slot.Children.Add(bar);
        slot.SizeChanged += (_, e) => bar.Width = Math.Max(3, Math.Clamp(value, 0, 100) / 100.0 * e.NewSize.Width);
        return slot;
    }
}
