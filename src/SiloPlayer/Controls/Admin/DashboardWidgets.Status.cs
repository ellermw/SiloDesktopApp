using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.UI;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Controls.Admin;

/// <summary>A one-row tile: label + icon, a 28px value, and a muted sub line; "Unavailable" on error.</summary>
internal abstract class StatTileWidget(DashboardWidgetContext context) : DashboardWidgetView(context)
{
    protected abstract string Label { get; }
    protected abstract string IconName { get; }
    protected abstract (bool Loading, Exception? Error, string Value, string Sub) Read();

    protected override FrameworkElement Render()
    {
        var (loading, error, value, sub) = Read();
        if (loading)
        {
            var skeleton = Skeleton(96, 20);
            skeleton.Height = double.NaN;
            skeleton.MinHeight = 96;
            return skeleton;
        }
        var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = Text(Label, 11, Medium, MutedForeground, wrap: true, lineHeight: 11);
        label.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(label);
        var icon = Icon(IconName, 16, MutedForeground);
        icon.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(icon, 1); head.Children.Add(icon);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(head);
        if (error != null)
        {
            stack.Children.Add(Text(T("web.admin.dashboard.widgets.statTile.unavailable"), 14, color: Destructive));
        }
        else
        {
            var big = Text(value, 28, ExtraBold, wrap: true, lineHeight: 28);
            big.CharacterSpacing = -25;
            big.Margin = new Thickness(0, 0, 0, 2);
            stack.Children.Add(big);
            stack.Children.Add(Text(sub, 11, color: MutedForeground, wrap: true));
        }
        return new Border
        {
            Background = SurfacePanel,
            CornerRadius = new CornerRadius(20),
            // CSS clips at the outer tile, not at a padded content viewport.
            Padding = new Thickness(16, Fixed ? 0 : 16, 16, Fixed ? 0 : 16),
            MinHeight = Fixed ? 0 : 96,
            Child = stack,
        };
    }
}

internal sealed class ActiveStreamsTile(DashboardWidgetContext c) : StatTileWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Sessions];
    protected override string Label => T("web.admin.dashboard.widgets.activeStreams.label");
    protected override string IconName => "activity";
    protected override (bool, Exception?, string, string) Read()
    {
        var state = Store.Get(DashboardDataStore.Sessions);
        var count = (long)(state.As<IReadOnlyList<DashboardSession>>()?.Count ?? 0);
        return (state.IsLoading, state.Error, count.ToString(), T("web.admin.dashboard.widgets.activeStreams.sessions", ("count", count), ("count_text", count)));
    }
}

internal sealed class EgressNowTile(DashboardWidgetContext c) : StatTileWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Timeseries(1)];
    protected override string Label => T("web.admin.dashboard.widgets.egressNow.label");
    protected override string IconName => "gauge";
    protected override (bool, Exception?, string, string) Read()
    {
        var state = Store.Get(DashboardDataStore.Timeseries(1));
        // The sampler writes once a minute; a sample older than two minutes is not "now".
        var latest = DashboardChartMath.LatestFreshPoint(state.As<DashboardTimeseries>(), DashboardDataStore.EgressSampleMaxAge, Now);
        return (state.IsLoading, state.Error,
            latest != null ? DashboardFormat.Mbps(latest.EgressKbps / 1000d) : DashboardFormat.Dash,
            latest != null ? T("web.admin.dashboard.widgets.egressNow.source") : T("web.admin.dashboard.widgets.egressNow.waiting"));
    }
}

internal sealed class TranscodeShareTile(DashboardWidgetContext c) : StatTileWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Sessions];
    protected override string Label => T("web.admin.dashboard.widgets.transcodeShare.label");
    protected override string IconName => "zap";
    protected override (bool, Exception?, string, string) Read()
    {
        var state = Store.Get(DashboardDataStore.Sessions);
        var sessions = state.As<IReadOnlyList<DashboardSession>>() ?? [];
        // Video transcodes only: audio-only conversions are direct_stream.
        var transcoding = sessions.Count(session => DashboardPresentation.ClassifyMethod(session) == "transcode");
        var value = sessions.Count > 0 ? (int)Math.Round(transcoding / (double)sessions.Count * 100, MidpointRounding.AwayFromZero) + "%" : DashboardFormat.Dash;
        var sub = sessions.Count > 0
            ? T("web.admin.dashboard.widgets.transcodeShare.detail", ("count", (long)sessions.Count), ("transcoding", N(transcoding)), ("count_text", N(sessions.Count)))
            : T("web.admin.dashboard.widgets.transcodeShare.none");
        return (state.IsLoading, state.Error, value, sub);
    }
}

internal sealed class ProfilesActiveTile(DashboardWidgetContext c) : StatTileWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Playback(24)];
    protected override string Label => T("web.admin.dashboard.widgets.profilesActive.label");
    protected override string IconName => "user-check";
    protected override (bool, Exception?, string, string) Read()
    {
        var state = Store.Get(DashboardDataStore.Playback(24));
        var profiles = state.As<DashboardPlaybackActivity>()?.ProfilesActive24h;
        return (state.IsLoading, state.Error, profiles is { } p ? N(p) : DashboardFormat.Dash, T("web.admin.dashboard.widgets.profilesActive.detail"));
    }
}

internal abstract class StatsTile(DashboardWidgetContext c) : StatTileWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Stats];
    protected abstract (string Value, string Sub) Describe(DashboardStats? stats);
    protected override (bool, Exception?, string, string) Read()
    {
        var state = Store.Get(DashboardDataStore.Stats);
        var stats = state.As<DashboardStats>();
        var (value, sub) = Describe(stats);
        return (state.IsLoading, state.Error, value, sub);
    }
}

internal sealed class MoviesTile(DashboardWidgetContext c) : StatsTile(c)
{
    protected override string Label => T("web.admin.dashboard.widgets.movies.label");
    protected override string IconName => "film";
    protected override (string, string) Describe(DashboardStats? s) => (s != null ? N(s.TotalMovies) : DashboardFormat.Dash, DashboardFormat.FileCount(s?.TotalMovieFiles));
}

internal sealed class ShowsTile(DashboardWidgetContext c) : StatsTile(c)
{
    protected override string Label => T("web.admin.dashboard.widgets.shows.label");
    protected override string IconName => "tv";
    protected override (string, string) Describe(DashboardStats? s) => (s != null ? N(s.TotalShows) : DashboardFormat.Dash, DashboardFormat.FileCount(s?.TotalShowFiles));
}

internal sealed class AdvisoryCoverageTile(DashboardWidgetContext c) : StatsTile(c)
{
    protected override string Label => T("web.admin.dashboard.widgets.advisoryCoverage.label");
    protected override string IconName => "shield-check";
    protected override (string, string) Describe(DashboardStats? s)
    {
        var titles = s != null ? s.TotalMovies + s.TotalShows : 0;
        var covered = s?.AdvisoryTitles ?? 0;
        var value = s != null && titles > 0 ? (int)Math.Round(covered / (double)titles * 100, MidpointRounding.AwayFromZero) + "%" : DashboardFormat.Dash;
        return (value, T("web.admin.dashboard.widgets.advisoryCoverage.detail", ("count", titles), ("covered", N(covered)), ("count_text", N(titles))));
    }
}

internal sealed class UsersTile(DashboardWidgetContext c) : StatsTile(c)
{
    protected override string Label => T("web.admin.dashboard.widgets.usersStat.label");
    protected override string IconName => "users";
    protected override (string, string) Describe(DashboardStats? s)
        => (s != null ? s.TotalUsers.ToString() : DashboardFormat.Dash,
            T("web.admin.dashboard.widgets.usersStat.registered", ("count", s?.TotalUsers ?? 0), ("count_text", s?.TotalUsers ?? 0)));
}

internal sealed class StorageTile(DashboardWidgetContext c) : StatsTile(c)
{
    protected override string Label => T("web.admin.dashboard.widgets.storage.label");
    protected override string IconName => "hard-drive";
    protected override (string, string) Describe(DashboardStats? s)
        => (s != null ? DashboardFormat.Storage(s.TotalStorageBytes) : DashboardFormat.Dash, DashboardFormat.FileCount(s?.TotalFiles));
}

/// <summary>Version, uptime, Postgres, Redis, nodes and 24h errors in six cells.</summary>
internal sealed class HealthStripWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Status, DashboardDataStore.Build, DashboardDataStore.Nodes];

    protected override FrameworkElement Render()
    {
        var status = Store.Get(DashboardDataStore.Status);
        var build = Store.Get(DashboardDataStore.Build).As<DashboardBuildInfo>();
        var nodesState = Store.Get(DashboardDataStore.Nodes);
        if (status.IsLoading)
        {
            var skeleton = Skeleton(92, 20);
            skeleton.Height = double.NaN;
            skeleton.MinHeight = 92;
            return skeleton;
        }
        var data = status.As<DashboardServerStatus>();
        if (status.Error != null || data == null)
            return CardShell(new Border { Padding = new Thickness(24, 0, 24, 0), Child = SectionError(T("web.admin.dashboard.widgets.healthStrip.loadFailed")), VerticalAlignment = VerticalAlignment.Center },
                new Thickness(0, 12, 0, 12));
        var health = data.Health;
        var version = !string.IsNullOrEmpty(build?.Display) ? build!.Display : T("web.admin.dashboard.widgets.healthStrip.versionUnknown");
        var nodesReady = nodesState.Data != null;
        var nodes = nodesState.As<IReadOnlyList<DashboardNode>>() ?? [];
        var enabled = nodes.Where(n => n.Enabled).ToList();
        var healthy = enabled.Count(n => n.Healthy);
        var cells = new List<FrameworkElement>
        {
            Cell("tag", T("web.admin.dashboard.widgets.healthStrip.version"), build is { BuildNumber: > 0 } ? $"{build.BuildNumber} · {version}" : version,
                build?.Dirty == true ? T("web.admin.dashboard.widgets.healthStrip.dirtyBuild") : null),
            Cell("clock", T("web.admin.dashboard.widgets.healthStrip.uptime"), DashboardFormat.Uptime(data.StartedAt, Now),
                data.RestartRequired ? T("web.admin.dashboard.widgets.healthStrip.restartRequired") : null, data.RestartRequired ? Amber500 : null),
            Dependency("database", "Postgres", health?.Postgres),
            Dependency("zap", "Redis", health?.Redis),
            Cell("server", T("web.admin.dashboard.widgets.healthStrip.nodes"),
                !nodesReady ? DashboardFormat.Dash : enabled.Count == 0 ? T("web.admin.dashboard.widgets.healthStrip.nodesNone") : $"{healthy}/{enabled.Count}",
                !nodesReady ? nodesState.Error != null ? T("web.admin.dashboard.widgets.healthStrip.nodesUnavailable") : null
                    : enabled.Count == 0 ? T("web.admin.dashboard.widgets.healthStrip.nodesLocal") : T("web.admin.dashboard.widgets.healthStrip.nodesHealthy"),
                nodesReady && healthy < enabled.Count ? Amber500 : null),
            Cell("triangle-alert", T("web.admin.dashboard.widgets.healthStrip.errors24h"), health != null ? N(health.Errors24h) : DashboardFormat.Dash,
                health != null ? T("web.admin.dashboard.widgets.healthStrip.warnings", DashboardText.Count(health.Warnings24h)) : null,
                health is { Errors24h: > 0 } ? Destructive : null, C.Navigate(DashboardDestination.Logs)),
        };
        var columns = C.Mode() switch { DashboardGridMode.Twelve => 6, DashboardGridMode.Two => 3, _ => 2 };
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < (cells.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < cells.Count; i++)
        {
            Grid.SetColumn(cells[i], i % columns);
            Grid.SetRow(cells[i], i / columns);
            grid.Children.Add(cells[i]);
        }
        var shell = CardShell(new Border { Padding = new Thickness(24, 0, 24, 0), Child = grid }, new Thickness(0, Fixed ? 0 : 12, 0, Fixed ? 0 : 12));
        shell.VerticalAlignment = Fixed ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        return shell;
    }

    private FrameworkElement Dependency(string icon, string label, DashboardHealthComponent? component)
    {
        if (component == null) return Cell(icon, label, DashboardFormat.Dash, null);
        if (!component.Configured) return Cell(icon, label, T("web.admin.dashboard.widgets.healthStrip.notConfigured"), T("web.admin.dashboard.widgets.healthStrip.optional"));
        if (component.Ok != true) return Cell(icon, label, T("web.admin.dashboard.widgets.healthStrip.unreachable"), null, Destructive);
        return Cell(icon, label, T("web.admin.dashboard.widgets.healthStrip.ok"), component.LatencyMs is { } latency ? DashboardFormat.Latency(latency) : null);
    }

    private static FrameworkElement Cell(string icon, string label, string value, string? detail, Color? tone = null, Action? navigate = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(Row(6, Icon(icon, 14, MutedForeground), Text(label, 11, Medium, MutedForeground)));
        var valueText = Text(value, 15, Bold, tone ?? Foreground, trim: true, lineHeight: 18.75);
        valueText.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(valueText);
        if (detail != null)
        {
            var detailText = Text(detail, 10, color: MutedForeground, trim: true);
            detailText.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(detailText);
        }
        var cell = new Border
        {
            Background = B(Surface),
            BorderBrush = B(BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Child = stack,
        };
        if (navigate == null) return cell;
        var button = new Button { Content = cell, Padding = new Thickness(0), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(12) };
        button.Resources["ButtonBackground"] = B(Microsoft.UI.Colors.Transparent);
        button.Resources["ButtonBackgroundPointerOver"] = B(SurfaceHover);
        button.Resources["ButtonBackgroundPressed"] = B(SurfaceHover);
        button.PointerEntered += (_, _) => cell.Background = B(SurfaceHover);
        button.PointerExited += (_, _) => cell.Background = B(Surface);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label + ": " + value);
        button.Click += (_, _) => navigate();
        return button;
    }
}

/// <summary>CPU, memory and GPU on the API host; attribution footnotes when the server supports them.</summary>
internal sealed class ServerResourcesWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Resources, DashboardDataStore.ResourceCapabilities];

    protected override FrameworkElement Render()
    {
        var resources = Store.Get(DashboardDataStore.Resources);
        var caps = Store.Get(DashboardDataStore.ResourceCapabilities).As<DashboardResourceCapabilities>();
        var attribution = caps is { State: "available", InstanceAttribution: true };
        // A server predating the endpoint 404s: the same plain "not sampled" state, not an error.
        var sample = DashboardPresentation.DescribeResourceSample(resources.Error != null ? null : resources.As<DashboardSystemResources>(), Now, attribution);
        FrameworkElement? sampled = null;
        if (sample.Available && sample.SampledAt is { } at)
        {
            var label = DashboardFormat.RelativeTime(at, Now, floor: true, T("web.admin.dashboard.widgets.serverResources.justNow"))!;
            sampled = Text(T(sample.Stale ? "web.admin.dashboard.widgets.serverResources.sampledStale" : "web.admin.dashboard.widgets.serverResources.sampled", ("time", label)),
                11, color: sample.Stale ? DashboardUi.Warning : MutedForeground);
        }
        FrameworkElement body;
        if (resources.Data == null && resources.Error == null)
        {
            body = Column(12, Skeleton(16, 4), Skeleton(16, 4), Skeleton(16, 4));
        }
        else if (!sample.Available)
        {
            body = new TextBlock { Text = sample.UnavailableTitle, FontSize = 14, Foreground = B(MutedForeground), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.WrapWholeWords, VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            body = Sampled(sample);
        }
        return Card(Header(T("web.admin.dashboard.widgets.serverResources.title"), sampled), body, paddingTop: 16, paddingBottom: 16, gap: 8);
    }

    private static FrameworkElement Sampled(DashboardResourceSample sample)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 32 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 220 });
        var rows = new List<(string, DashboardMetric)>
        {
            (T("web.admin.dashboard.widgets.serverResources.cpu"), sample.Cpu!),
            (T("web.admin.dashboard.widgets.serverResources.memory"), sample.Memory!),
        };
        if (sample.Gpu != null) rows.Add((T("web.admin.dashboard.widgets.serverResources.gpu"), sample.Gpu));
        for (var i = 0; i < rows.Count; i++)
        {
            var (label, metric) = rows[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = T("web.admin.dashboard.widgets.serverResources.metricTitle", ("label", metric.Label), ("title", metric.Title));
            var labelText = Text(label, 14);
            ToolTipService.SetToolTip(labelText, title);
            var meter = DashboardLoadMeter.Create(metric.Fill);
            var value = Text(metric.Value, 14, Semibold, metric.Muted ? MutedForeground : Foreground);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            ToolTipService.SetToolTip(value, title);
            var detail = Text(metric.Detail, 12, color: MutedForeground, trim: true);
            ToolTipService.SetToolTip(detail, title);
            UIElement[] cells = [labelText, meter, value, detail];
            for (var col = 0; col < cells.Length; col++)
            {
                Grid.SetRow((FrameworkElement)cells[col], i);
                Grid.SetColumn((FrameworkElement)cells[col], col);
                ((FrameworkElement)cells[col]).VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(cells[col]);
            }
        }
        var root = new Grid { Opacity = sample.Stale ? 0.6 : 1 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(grid);
        if (sample.ProcessMemory != null)
        {
            var notes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            notes.Children.Add(Footnote(T("web.admin.dashboard.widgets.serverResources.siloProcess"), sample.ProcessMemory));
            if (sample.HeapMemory != null) notes.Children.Add(Footnote(T("web.admin.dashboard.widgets.serverResources.goHeap"), sample.HeapMemory));
            if (sample.InstanceId != null)
            {
                var instance = Text(T("web.admin.dashboard.widgets.serverResources.apiInstance", ("id", sample.InstanceId[..Math.Min(8, sample.InstanceId.Length)])), 11, color: MutedForeground);
                ToolTipService.SetToolTip(instance, sample.InstanceId);
                notes.Children.Add(instance);
            }
            var footer = new Border { BorderBrush = B(BorderColor, 0.6), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(0, 8, 0, 0), Child = notes };
            Grid.SetRow(footer, 1);
            root.Children.Add(footer);
        }
        return root;
    }

    private static FrameworkElement Footnote(string label, DashboardMetric metric)
    {
        var runs = DashboardText.Rich("web.admin.dashboard.widgets.serverResources.footnote", ("label", label), ("value", metric.Value));
        var block = new TextBlock { FontSize = 11, Foreground = B(MutedForeground) };
        foreach (var run in runs) block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = run.Text });
        // The value is the trailing argument; emphasise it like the WebUI's span.
        block.Inlines.Clear();
        block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = label + " " });
        block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = metric.Value, FontWeight = Semibold, Foreground = B(metric.Muted ? MutedForeground : Foreground) });
        ToolTipService.SetToolTip(block, metric.Title);
        return block;
    }
}

/// <summary>One compact row per watch provider: mark, name, connection/24h activity, errors.</summary>
internal sealed class WatchProvidersWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Stats];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.Stats);
        if (state.IsLoading)
        {
            var skeleton = Skeleton(56, 20);
            skeleton.Height = double.NaN;
            skeleton.MinHeight = 56;
            return skeleton;
        }
        Border Panel(UIElement child) => new() { Background = SurfacePanel, CornerRadius = new CornerRadius(20), MinHeight = 56, Child = child, VerticalAlignment = Fixed ? VerticalAlignment.Stretch : VerticalAlignment.Top };
        if (state.Error != null)
            return Panel(new Border { Padding = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Child = Text(T("web.admin.dashboard.widgets.watchProviders.loadFailed"), 14, color: Destructive, wrap: true) });
        var providers = state.As<DashboardStats>()?.WatchProviders ?? [];
        if (providers.Count == 0)
            return Panel(new Border { Padding = new Thickness(16, 12, 16, 12), VerticalAlignment = VerticalAlignment.Center, Child = Text(T("web.admin.dashboard.widgets.watchProviders.empty"), 14, color: MutedForeground) });
        var list = new StackPanel();
        for (var i = 0; i < providers.Count; i++)
        {
            var row = ProviderRow(providers[i]);
            if (i > 0) row.BorderBrush = B(BorderColor, 0.5);
            row.BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0);
            list.Children.Add(row);
        }
        return Panel(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }

    private Border ProviderRow(DashboardWatchProvider provider)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 56, Padding = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var source = string.IsNullOrEmpty(provider.DisplayName) ? provider.Provider : provider.DisplayName;
        var mark = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(12), Background = B(Primary, 0.1), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = source.Length >= 2 ? source[..2].ToUpperInvariant() : source.ToUpperInvariant(), FontSize = 10, FontWeight = ExtraBold, Foreground = B(Primary), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        grid.Children.Add(mark);
        var line = new TextBlock { FontSize = 14, Foreground = B(MutedForeground), TextWrapping = TextWrapping.WrapWholeWords, VerticalAlignment = VerticalAlignment.Center };
        line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = provider.DisplayName, FontWeight = Semibold, Foreground = B(Foreground) });
        line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " });
        if (provider.ConnectedProfiles == 0)
        {
            line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = provider.Registered ? T("web.admin.dashboard.widgets.watchProviders.notConnected") : T("web.admin.dashboard.widgets.watchProviders.notInstalled") });
        }
        else
        {
            var lastSync = DashboardFormat.RelativeTime(provider.LastSyncCompletedAt, Now, floor: true, T("web.admin.dashboard.widgets.watchProviders.justNow"))
                           ?? T("web.admin.dashboard.widgets.watchProviders.never");
            line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = T("web.admin.dashboard.widgets.watchProviders.profilesSynced", DashboardText.Count(provider.ConnectedProfiles, ("last_sync", lastSync))) });
            line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  |  ", Foreground = B(BorderColor, 0.7) });
            void AddRich(IReadOnlyList<DashboardTextRun> runs)
            {
                foreach (var run in runs)
                    line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = run.Text, FontWeight = run.Tag == "value" ? Medium : new Windows.UI.Text.FontWeight { Weight = 400 }, Foreground = B(run.Tag == "value" ? Foreground : MutedForeground) });
            }
            AddRich(provider.Exporting
                ? DashboardText.Rich("web.admin.dashboard.widgets.watchProviders.importedExported24h", ("imported", N(provider.ImportedWatched24h)), ("exported", N(provider.ExportedWatched24h)))
                : DashboardText.Rich("web.admin.dashboard.widgets.watchProviders.imported24h", ("imported", N(provider.ImportedWatched24h))));
            if (provider.Scrobbling && provider.OpenScrobbles > 0)
            {
                line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " });
                AddRich(DashboardText.Rich("web.admin.dashboard.widgets.watchProviders.scrobbling", DashboardText.Count(provider.OpenScrobbles)));
            }
        }
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        var errors = provider.SyncErrors24h + provider.FailedExports;
        if (errors > 0)
        {
            var pill = Pill(T("web.admin.dashboard.widgets.watchProviders.errorCount", DashboardText.Count(errors)), Alpha(Destructive, 0.1), Destructive, Microsoft.UI.Colors.Transparent, 11, 999, Medium, new Thickness(8, 2, 8, 2));
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
        }
        // "Manage ›" opens task administration, which is outside the Monitor scope: plain text.
        var manage = Link(T("web.admin.dashboard.widgets.watchProviders.manage"), null);
        manage.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(manage, 3);
        grid.Children.Add(manage);
        return new Border { Child = grid };
    }
}
