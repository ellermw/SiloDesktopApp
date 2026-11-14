using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Controls.Admin;

/// <summary>Loading / error / collecting / plotted states shared by the sampled line charts.</summary>
internal abstract class TimeseriesWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected string SeriesKey => DashboardDataStore.Timeseries(DashboardRanges.Hours(Range));
    protected override IEnumerable<string> Keys => [SeriesKey];

    protected FrameworkElement ChartBody(DashboardQueryState state, IReadOnlyList<DashboardChartPoint> points, string seriesLabel, string ariaLabel,
        string errorMessage, string emptyMessage, Func<double, string>? formatValue = null, Func<double, string>? formatTick = null,
        double? minTickStep = null, IReadOnlyList<(string, IReadOnlyList<DashboardChartPoint>, int)>? overlays = null,
        IReadOnlyList<DashboardChartBand>? bands = null)
    {
        if (state.IsLoading) return DashboardChartChrome.ChartSkeleton(Fixed ? 0 : DashboardGridPlacement.StackedChartMinHeight);
        if (state.Error != null) return SectionError(errorMessage);
        if (!points.Any(point => point.Value != null))
            return DashboardChartChrome.EmptyState(emptyMessage, T("web.admin.dashboard.widgets.timeseriesChart.emptyDetail"),
                state.As<DashboardTimeseries>()?.OldestSampleAt, Now, ChartMinHeight);
        var range = Range;
        var chart = new DashboardLineChart
        {
            Points = points,
            SeriesLabel = seriesLabel,
            AccessibleLabel = ariaLabel,
            FormatValue = formatValue ?? (value => N((long)Math.Round(value))),
            FormatTick = formatTick,
            FormatTimestamp = time => DashboardFormat.RangeTimestamp(range, time, withTime: true),
            EdgeLabels = DashboardRanges.EdgeLabels(range),
            MinTickStep = minTickStep,
            Overlays = overlays ?? [],
            Bands = bands ?? [],
            MinPlotHeight = ChartMinHeight,
        };
        chart.Build();
        return chart;
    }
}

internal sealed class ConcurrentStreamsWidget(DashboardWidgetContext c) : TimeseriesWidget(c)
{
    protected override FrameworkElement Render()
    {
        var state = Store.Get(SeriesKey);
        var points = DashboardChartMath.TimeseriesPoints(state.As<DashboardTimeseries>(), point => point.Streams);
        var peak = points.Select(point => point.Value ?? 0).DefaultIfEmpty(0).Max();
        var header = Header(DashboardRanges.Title(T("web.admin.dashboard.widgets.concurrentStreams.title"), Range),
            Muted11(T("web.admin.dashboard.widgets.concurrentStreams.peak", ("value", N((long)peak)))), RangePicker());
        var body = ChartBody(state, points, T("web.admin.dashboard.widgets.concurrentStreams.seriesLabel"),
            T("web.admin.dashboard.widgets.concurrentStreams.chartLabel", ("range", DashboardRanges.Phrase(Range))),
            T("web.admin.dashboard.widgets.concurrentStreams.loadFailed"), T("web.admin.dashboard.widgets.concurrentStreams.empty"), minTickStep: 1);
        return Card(header, body, scroll: false);
    }
}

/// <summary>Egress in Mbps: total line, a downloads overlay when measured, and a Total/Nodes stacked view.</summary>
internal sealed class EgressWidget(DashboardWidgetContext c) : TimeseriesWidget(c)
{
    private bool _byNode;

    protected override FrameworkElement Render()
    {
        var state = Store.Get(SeriesKey);
        var series = state.As<DashboardTimeseries>();
        var bands = DashboardChartMath.EgressBands(series);
        var showBands = _byNode && bands != null;
        var hasDownloads = (series?.Points ?? []).Any(point => (point.DownloadEgressKbps ?? 0) > 0);
        var points = DashboardChartMath.TimeseriesPoints(series, point => point.EgressKbps / 1000d);
        var downloads = hasDownloads ? DashboardChartMath.TimeseriesPoints(series, point => (point.DownloadEgressKbps ?? 0) / 1000d) : [];
        var peak = points.Select(point => point.Value ?? 0).DefaultIfEmpty(0).Max();
        // Integrated server-side from every minute; summing points would be wrong for peak buckets.
        var total = series?.EgressBytes;
        FrameworkElement? totalLabel = null;
        if (total != null && ActualWidth >= 448)
            totalLabel = Muted11(T("web.admin.dashboard.widgets.egress.total", ("value", DashboardFormat.DataTotal(total))));
        FrameworkElement? toggle = bands == null ? null : Segmented(
            [(false, T("web.admin.dashboard.widgets.egress.viewTotal"), T("web.admin.dashboard.widgets.egress.viewTotalLabel")),
             (true, T("web.admin.dashboard.widgets.egress.viewNodes"), T("web.admin.dashboard.widgets.egress.viewNodesLabel"))],
            _byNode, value => { _byNode = value; RenderNow(); });
        var header = Header(DashboardRanges.Title(T("web.admin.dashboard.widgets.egress.title"), Range),
            totalLabel, Muted11(T("web.admin.dashboard.widgets.egress.peak", ("value", DashboardFormat.Mbps(peak)))), toggle, RangePicker());
        var body = ChartBody(state, points, T("web.admin.dashboard.widgets.egress.seriesLabel"),
            T("web.admin.dashboard.widgets.egress.chartLabel", ("range", DashboardRanges.Phrase(Range))),
            T("web.admin.dashboard.widgets.egress.loadFailed"), T("web.admin.dashboard.widgets.egress.empty"),
            DashboardFormat.Mbps, DashboardFormat.MbpsValue,
            overlays: hasDownloads && !showBands ? [(T("web.admin.dashboard.widgets.egress.downloadsSeries"), downloads, 1)] : null,
            bands: showBands ? bands : null);
        SizeChanged -= OnResize;
        SizeChanged += OnResize;
        return Card(header, body, scroll: false);
    }

    private double _lastWidth;
    private void OnResize(object sender, SizeChangedEventArgs e)
    {
        // The window total only shows when the header has room (the WebUI's @md container query).
        if ((_lastWidth >= 448) != (e.NewSize.Width >= 448)) { _lastWidth = e.NewSize.Width; RenderNow(); }
        _lastWidth = e.NewSize.Width;
    }
}

/// <summary>Playback starts per bucket, stacked direct play / direct stream / transcode.</summary>
internal sealed class PlaybackActivityWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    private string Key => DashboardDataStore.Playback(DashboardRanges.Hours(Range));
    protected override IEnumerable<string> Keys => [Key];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(Key);
        var data = state.As<DashboardPlaybackActivity>();
        var hours = DashboardRanges.Hours(Range);
        var bucketSeconds = data?.BucketSeconds is > 0 ? data.BucketSeconds : DashboardChartMath.DefaultPlaybackBucketSeconds;
        // Anchor on the server's window end: the buckets were cut on the database clock.
        var columns = DashboardChartMath.PlaybackColumns(data?.Buckets, hours, bucketSeconds, data?.To ?? Now);
        var total = (long)columns.Sum(column => column.Total);
        var daily = bucketSeconds >= 86_400;
        var header = Header(DashboardRanges.Title(T("web.admin.dashboard.widgets.playbackActivity.title"), Range),
            Muted11(T("web.admin.dashboard.widgets.playbackActivity.sessionCount", DashboardText.Count(total))), RangePicker());
        FrameworkElement body;
        if (state.IsLoading) body = DashboardChartChrome.ChartSkeleton(ChartMinHeight);
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.playbackActivity.loadFailed"));
        else if (total == 0)
            body = DashboardChartChrome.EmptyState(T("web.admin.dashboard.widgets.playbackActivity.empty", ("range", DashboardRanges.Phrase(Range))),
                T("web.admin.dashboard.widgets.playbackActivity.emptyDetail"), null, Now, ChartMinHeight);
        else
        {
            var chart = new DashboardStackedColumnChart
            {
                Columns = columns,
                SeriesLabels = [T("web.admin.dashboard.widgets.playbackActivity.series.directPlay"), T("web.admin.dashboard.widgets.playbackActivity.series.directStream"), T("web.admin.dashboard.widgets.playbackActivity.series.transcode")],
                AccessibleLabel = T(daily ? "web.admin.dashboard.widgets.playbackActivity.chartLabelDaily" : "web.admin.dashboard.widgets.playbackActivity.chartLabelHourly", ("range", DashboardRanges.Phrase(Range))),
                FormatBucket = time => daily ? DashboardFormat.DayLabel(time) : DashboardFormat.HourTick(time),
                TotalLabel = T("web.admin.dashboard.widgets.playbackActivity.totalLabel"),
                PlotHeight = Fixed ? double.NaN : DashboardGridPlacement.StackedChartMinHeight,
                MinPlotHeight = ChartMinHeight,
            };
            chart.Build();
            body = chart;
        }
        return Card(header, body, scroll: false);
    }
}

/// <summary>Four counts and one rate; no invented start-failure numbers.</summary>
internal sealed class PlaybackReliabilityWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    private string Key => DashboardDataStore.Playback(DashboardRanges.Hours(Range));
    protected override IEnumerable<string> Keys => [Key];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(Key);
        var reliability = state.As<DashboardPlaybackActivity>()?.Reliability;
        var header = Header(DashboardRanges.Title(T("web.admin.dashboard.widgets.playbackReliability.title"), Range), RangePicker());
        var columns = C.Mode() == DashboardGridMode.Single ? 2 : 4;
        FrameworkElement body;
        if (state.IsLoading) body = Grid(columns, Enumerable.Range(0, 4).Select(_ => Skeleton(64, 12)).ToArray());
        else if (state.Error != null || reliability == null) body = SectionError(T("web.admin.dashboard.widgets.playbackReliability.loadFailed"));
        else
        {
            var completion = reliability.FinalizedSessions <= 0 || !double.IsFinite(reliability.CompletionRate)
                ? DashboardFormat.Dash
                : (int)Math.Round(Math.Clamp(reliability.CompletionRate, 0, 1) * 100, MidpointRounding.AwayFromZero) + "%";
            body = Grid(columns,
                MiniStat(T("web.admin.dashboard.widgets.playbackReliability.started"), N(reliability.SessionsStarted)),
                MiniStat(T("web.admin.dashboard.widgets.playbackReliability.transcodeStarts"), N(reliability.TranscodeStarts)),
                MiniStat(T("web.admin.dashboard.widgets.playbackReliability.completed"), completion,
                    reliability.FinalizedSessions > 0
                        ? T("web.admin.dashboard.widgets.playbackReliability.completedDetail", ("completed", N(reliability.CompletedSessions)), ("finished", N(reliability.FinalizedSessions)))
                        : T("web.admin.dashboard.widgets.playbackReliability.noFinished")),
                MiniStat(T("web.admin.dashboard.widgets.playbackReliability.profiles"), N(reliability.UniqueProfiles)));
        }
        return Card(header, body);
    }

    private static Grid Grid(int columns, params FrameworkElement[] cells)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < (cells.Length + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < cells.Length; i++)
        {
            Microsoft.UI.Xaml.Controls.Grid.SetColumn(cells[i], i % columns);
            Microsoft.UI.Xaml.Controls.Grid.SetRow(cells[i], i / columns);
            grid.Children.Add(cells[i]);
        }
        return grid;
    }

    private static FrameworkElement MiniStat(string label, string value, string? detail = null)
    {
        var stack = new StackPanel();
        var big = Text(value, 20, ExtraBold, lineHeight: 20);
        big.CharacterSpacing = -25;
        stack.Children.Add(big);
        var labelText = Text(label, 11, Medium, MutedForeground);
        labelText.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(labelText);
        if (detail != null)
        {
            var detailText = Text(detail, 10, color: MutedForeground, wrap: true);
            detailText.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(detailText);
        }
        return new Border { Background = B(Surface), BorderBrush = B(BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = stack };
    }
}

internal abstract class LeaderboardWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected string Key => DashboardDataStore.TopActivity(DashboardRanges.Days(Range));
    protected override IEnumerable<string> Keys => [Key];
    protected abstract string Prefix { get; }
    protected abstract IReadOnlyList<DashboardBarItem> Items(DashboardTopActivity data);

    protected override FrameworkElement Render()
    {
        var state = Store.Get(Key);
        // "All history ›" opens admin history, outside the Monitor scope: plain text.
        var header = Header(DashboardRanges.Title(T($"web.admin.dashboard.widgets.{Prefix}.title"), Range),
            Link(T($"web.admin.dashboard.widgets.{Prefix}.allHistory"), null), RangePicker());
        FrameworkElement body = state.IsLoading ? DashboardChartChrome.BarListSkeleton()
            : state.Error != null ? SectionError(T($"web.admin.dashboard.widgets.{Prefix}.loadFailed"))
            : DashboardBarList.Create(Items(state.As<DashboardTopActivity>()!),
                plays => T($"web.admin.dashboard.widgets.{Prefix}.playCount", DashboardText.Count((long)plays)),
                T($"web.admin.dashboard.widgets.{Prefix}.empty", ("range", DashboardRanges.Phrase(Range))));
        return Card(header, body);
    }
}

internal sealed class TopTitlesWidget(DashboardWidgetContext c) : LeaderboardWidget(c)
{
    protected override string Prefix => "topTitles";
    protected override IReadOnlyList<DashboardBarItem> Items(DashboardTopActivity data)
        => (data.Titles ?? []).Take(8).Select(title => new DashboardBarItem(title.MediaItemId,
            string.IsNullOrEmpty(title.Title) ? title.MediaItemId : title.Title, title.Plays, DashboardFormat.WatchTime(title.TotalSeconds))).ToArray();
}

internal sealed class TopProfilesWidget(DashboardWidgetContext c) : LeaderboardWidget(c)
{
    protected override string Prefix => "topProfiles";
    protected override IReadOnlyList<DashboardBarItem> Items(DashboardTopActivity data)
        => (data.Profiles ?? []).Take(8).Select(profile =>
        {
            var account = !string.IsNullOrEmpty(profile.Username) ? profile.Username : T("web.admin.dashboard.widgets.topProfiles.userFallback", ("id", profile.UserId));
            var name = !string.IsNullOrEmpty(profile.ProfileName) ? profile.ProfileName : !string.IsNullOrEmpty(profile.ProfileId) ? profile.ProfileId : account;
            return new DashboardBarItem($"{profile.UserId}:{profile.ProfileId}", name == account ? account : $"{name} · {account}",
                profile.Plays, DashboardFormat.WatchTime(profile.TotalSeconds));
        }).ToArray();
}

/// <summary>Who keeps media downloaded on devices; all zeros is a real empty state.</summary>
internal sealed class DownloadsWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Downloads];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.Downloads);
        var stats = state.As<DashboardDownloadsStats>();
        var has = (stats?.ActiveDownloads ?? 0) > 0 || (stats?.DownloadsStarted24h ?? 0) > 0;
        var header = Header(T("web.admin.dashboard.widgets.downloads.title"),
            stats != null && has ? Muted11(T("web.admin.dashboard.widgets.downloads.summary24h", ("started", N(stats.DownloadsStarted24h)), ("finished", N(stats.DownloadsCompleted24h)))) : null);
        FrameworkElement body;
        if (state.IsLoading) body = DashboardChartChrome.BarListSkeleton();
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.downloads.loadFailed"));
        else if (stats == null || !has) body = EmptyLine(T("web.admin.dashboard.widgets.downloads.empty"));
        else
        {
            var grid = new Grid { ColumnSpacing = 8 };
            for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            FrameworkElement Stat(string label, string value, int column)
            {
                var stack = new StackPanel();
                stack.Children.Add(Text(label, 11, Medium, MutedForeground, lineHeight: 11));
                var big = Text(value, 18, ExtraBold, lineHeight: 18);
                big.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(big);
                var cell = new Border { Background = SurfacePanelSubtle, BorderBrush = B(BorderColor, 0.62), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 8), Child = stack };
                Microsoft.UI.Xaml.Controls.Grid.SetColumn(cell, column);
                return cell;
            }
            grid.Children.Add(Stat(T("web.admin.dashboard.widgets.downloads.users"), N(stats.UsersWithDownloads), 0));
            grid.Children.Add(Stat(T("web.admin.dashboard.widgets.downloads.items"), N(stats.ActiveDownloads), 1));
            grid.Children.Add(Stat(T("web.admin.dashboard.widgets.downloads.onDevices"), DashboardFormat.FileSize(stats.TotalBytes, "0 B"), 2));
            var items = (stats.TopUsers ?? []).Take(8).Select(user => new DashboardBarItem(user.UserId,
                !string.IsNullOrEmpty(user.Username) ? user.Username : T("web.admin.dashboard.widgets.downloads.userFallback", ("id", user.UserId)),
                user.Downloads, DashboardFormat.FileSize(user.TotalBytes))).ToArray();
            body = Column(12, grid, DashboardBarList.Create(items,
                count => T("web.admin.dashboard.widgets.downloads.itemCount", DashboardText.Count((long)count)), T("web.admin.dashboard.widgets.downloads.empty")));
        }
        return Card(header, body);
    }
}

/// <summary>The busiest sign-in sessions; a row expands to its routes and a confirmed single-session sign-out.</summary>
internal sealed class ApiClientsWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    private string? _open;
    private string Key => DashboardDataStore.ApiClients(DashboardRanges.Hours(Range));
    protected override IEnumerable<string> Keys => _open == null ? [Key] : [Key, DashboardDataStore.ApiClientEndpoints(_open, Math.Min(DashboardRanges.Hours(Range), AdminMonitorApiLimits.EndpointHours))];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(Key);
        var data = state.As<DashboardApiClients>();
        var clients = (data?.Clients ?? []).Take(8).ToList();
        var header = Header(DashboardRanges.Title(T("web.admin.dashboard.widgets.apiClients.title"), Range),
            data is { TotalRequests: > 0 } && C.Mode() != DashboardGridMode.Single ? Muted11(T("web.admin.dashboard.widgets.apiClients.totalRequests", DashboardText.Count(data.TotalRequests))) : null,
            RangePicker());
        FrameworkElement body;
        if (state.IsLoading) body = DashboardChartChrome.BarListSkeleton();
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.apiClients.loadFailed"));
        else if (clients.Count == 0) body = EmptyLine(T("web.admin.dashboard.widgets.apiClients.empty", ("range", DashboardRanges.Phrase(Range))));
        else
        {
            var max = clients.Max(client => client.Requests);
            var list = new StackPanel { Spacing = 4 };
            for (var i = 0; i < clients.Count; i++) list.Children.Add(ClientRow(clients[i], i, max, data!.TotalRequests));
            body = list;
        }
        return Card(header, body);
    }

    private string ClientLabel(DashboardApiClient client)
        => !string.IsNullOrEmpty(client.DeviceName) ? client.DeviceName : !string.IsNullOrEmpty(client.UserAgent) ? client.UserAgent : T("web.admin.dashboard.widgets.apiClients.unknownClient");

    private static string Share(long requests, long total)
    {
        if (total <= 0) return "";
        var share = requests / (double)total * 100;
        return share >= 10 ? Math.Round(share, MidpointRounding.AwayFromZero) + "%" : DashboardFormat.Decimal(share, 1) + "%";
    }

    private FrameworkElement ClientRow(DashboardApiClient client, int index, long max, long total)
    {
        var open = _open == client.SessionId;
        var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(8, 6, 8, 6) };
        foreach (var width in new[] { new GridLength(16), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(40) })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        var ratio = max > 0 ? client.Requests / (double)max : 0;
        var track = new Border { Background = B(Chart(0)), Opacity = 0.18, CornerRadius = new CornerRadius(0, 4, 4, 0), HorizontalAlignment = HorizontalAlignment.Left };
        // Decorative width must not feed back into the measured row width.
        var background = new Canvas { IsHitTestVisible = false };
        Canvas.SetLeft(track, -8);
        Canvas.SetTop(track, -6);
        background.Children.Add(track);
        Grid.SetColumnSpan(background, 7);
        row.Children.Add(background);
        row.SizeChanged += (_, e) => { track.Width = Math.Max(client.Requests > 0 ? 6 : 0, ratio * e.NewSize.Width); track.Height = Math.Max(0, e.NewSize.Height); };
        void Add(FrameworkElement element, int column)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, column);
            row.Children.Add(element);
        }
        Add(Muted11((index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture)), 0);
        var chevron = Icon("chevron-right", 12, MutedForeground);
        chevron.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        chevron.RenderTransform = new Microsoft.UI.Xaml.Media.RotateTransform { Angle = open ? 90 : 0 };
        Add(chevron, 1);
        var label = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = B(Foreground) };
        label.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = ClientLabel(client) });
        label.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = " · " + (!string.IsNullOrEmpty(client.Username) ? client.Username : T("web.admin.dashboard.widgets.apiClients.userFallback", ("id", client.UserId))),
            Foreground = B(MutedForeground),
        });
        ToolTipService.SetToolTip(label, ClientLabel(client));
        Add(label, 2);
        if (client.Refused > 0) Add(Text(T("web.admin.dashboard.widgets.apiClients.refused", DashboardText.Count(client.Refused)), 11, color: Destructive), 3);
        if (!client.SessionActive && C.Mode() != DashboardGridMode.Single) Add(Muted11(T("web.admin.dashboard.widgets.apiClients.signedOut")), 4);
        Add(Text(N(client.Requests), 13, Semibold), 5);
        var share = Muted11(Share(client.Requests, total));
        share.HorizontalAlignment = HorizontalAlignment.Right;
        Add(share, 6);
        var button = new Button
        {
            Content = row,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
        };
        button.Resources["ButtonBackground"] = B(Microsoft.UI.Colors.Transparent);
        button.Resources["ButtonBackgroundPointerOver"] = B(Muted, 0.5);
        button.Resources["ButtonBackgroundPressed"] = B(Muted, 0.5);
        AutomationProperties.SetName(button, ClientLabel(client));
        AutomationProperties.SetItemStatus(button, open ? "expanded" : "collapsed");
        button.Click += (_, _) =>
        {
            _open = open ? null : client.SessionId;
            Remount();
            RenderNow();
        };
        var item = new StackPanel();
        item.Children.Add(button);
        if (open) item.Children.Add(Detail(client));
        return item;
    }

    private FrameworkElement Detail(DashboardApiClient client)
    {
        var hours = Math.Min(DashboardRanges.Hours(Range), AdminMonitorApiLimits.EndpointHours);
        var endpoints = Store.Get(DashboardDataStore.ApiClientEndpoints(client.SessionId, hours));
        var stack = new StackPanel { Spacing = 8 };
        var facts = new Grid { ColumnSpacing = 12, RowSpacing = 2 };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        void Fact(string label, string value)
        {
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Text(label, 12, color: MutedForeground);
            var v = Text(value, 12, trim: true);
            Grid.SetRow(l, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
            facts.Children.Add(l); facts.Children.Add(v);
            row++;
        }
        if (!string.IsNullOrEmpty(client.UserAgent)) Fact(T("web.admin.dashboard.widgets.apiClients.userAgent"), client.UserAgent);
        Fact(T("web.admin.dashboard.widgets.apiClients.busiestHour"), T("web.admin.dashboard.widgets.apiClients.peakHourRequests", DashboardText.Count(client.PeakHourRequests)));
        stack.Children.Add(facts);
        stack.Children.Add(Text(T("web.admin.dashboard.widgets.apiClients.routesHeading", ("hours", (long)hours)), 12, color: MutedForeground));
        if (endpoints.IsLoading) stack.Children.Add(Text(T("web.admin.dashboard.widgets.apiClients.loadingRoutes"), 12, color: MutedForeground));
        else if (endpoints.Error != null) stack.Children.Add(Text(T("web.admin.dashboard.widgets.apiClients.routesFailed"), 12, color: MutedForeground));
        else if ((endpoints.As<DashboardApiClientEndpoints>()?.Endpoints ?? []).Count == 0) stack.Children.Add(Text(T("web.admin.dashboard.widgets.apiClients.noRoutes"), 12, color: MutedForeground));
        else
        {
            var routes = new StackPanel { Spacing = 2 };
            foreach (var endpoint in endpoints.As<DashboardApiClientEndpoints>()!.Endpoints!)
            {
                var line = new Grid { ColumnSpacing = 8 };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var method = Text(endpoint.Method, 11, color: MutedForeground);
                method.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
                var route = Text(endpoint.Route, 11, trim: true);
                route.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
                var count = Text(N(endpoint.Requests), 12, color: MutedForeground);
                Grid.SetColumn(route, 1); Grid.SetColumn(count, 2);
                line.Children.Add(method); line.Children.Add(route); line.Children.Add(count);
                routes.Children.Add(line);
            }
            stack.Children.Add(routes);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        if (client.SessionActive) actions.Children.Add(OutlineButton(T("web.admin.dashboard.widgets.apiClients.signOut"), null, async (_, _) => await ConfirmSignOutAsync(client)));
        else actions.Children.Add(Text(T("web.admin.dashboard.widgets.apiClients.sessionSignedOut"), 12, color: MutedForeground));
        // "Open account ›" opens user administration, outside the Monitor scope: plain text.
        actions.Children.Add(Link(T("web.admin.dashboard.widgets.apiClients.openAccount"), null, 12));
        stack.Children.Add(actions);
        return new Border
        {
            BorderBrush = B(BorderColor, 0.6),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Margin = new Thickness(8, 4, 8, 8),
            Padding = new Thickness(12, 0, 0, 0),
            Child = stack,
        };
    }

    /// <summary>
    /// The confirmation captures this widget's lease before it opens; the write
    /// runs only if that lease is still current, and a failure stays in the dialog.
    /// </summary>
    private async Task ConfirmSignOutAsync(DashboardApiClient client)
    {
        if (!Store.IsCurrent || C.Root() is not { } root) return;
        var error = new TextBlock { FontSize = 13, Foreground = B(Destructive), TextWrapping = TextWrapping.WrapWholeWords, Visibility = Visibility.Collapsed };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = T("web.admin.dashboard.widgets.apiClients.confirmTitle", ("client", ClientLabel(client))),
            Content = Column(12,
                new TextBlock
                {
                    Text = T("web.admin.dashboard.widgets.apiClients.confirmDescription", ("user", !string.IsNullOrEmpty(client.Username) ? client.Username
                        : T("web.admin.dashboard.widgets.apiClients.confirmUserFallback", ("id", client.UserId)))),
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Foreground = B(MutedForeground),
                },
                error),
            PrimaryButtonText = T("web.admin.dashboard.widgets.apiClients.confirmLabel"),
            CloseButtonText = T("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["AccentButtonBackground"] = B(Destructive);
        dialog.Resources["AccentButtonBackgroundPointerOver"] = B(Destructive, 0.9);
        dialog.Resources["AccentButtonForeground"] = B(Microsoft.UI.Colors.White);
        var pending = false;
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            if (pending) { deferral.Complete(); return; }
            pending = true;
            sender.IsPrimaryButtonEnabled = false;
            var ok = await Store.RevokeLoginSessionAsync(client.UserId, client.SessionId);
            pending = false;
            sender.IsPrimaryButtonEnabled = true;
            if (!Store.IsCurrent) { deferral.Complete(); sender.Hide(); return; }
            if (ok) { deferral.Complete(); sender.Hide(); return; }
            error.Text = T("web.admin.dashboard.widgets.apiClients.revokeFailed");
            error.Visibility = Visibility.Visible;
            deferral.Complete();
        };
        dialog.Closing += (_, args) => { if (pending) args.Cancel = true; };
        using var tracked = C.TrackDialog(dialog);
        try { await dialog.ShowAsync(); }
        catch (Exception) { }
    }
}

internal static class AdminMonitorApiLimits
{
    public const int EndpointHours = Core.Api.AdminMonitorApi.DashboardMaxEndpointHours;
}
