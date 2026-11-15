using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.UI;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Controls.Admin;

internal static class DashboardSessionBadges
{
    public static (Color Background, Color Foreground, Color Border) Tone(string tone) => tone switch
    {
        "success" => (Alpha(Success, 0.1), Success, Alpha(Success, 0.15)),
        "info" => (Alpha(Info, 0.1), Info, Alpha(Info, 0.15)),
        "warning" => (Alpha(DashboardUi.Warning, 0.1), DashboardUi.Warning, Alpha(DashboardUi.Warning, 0.15)),
        "destructive" => (Alpha(Destructive, 0.1), Destructive, Alpha(Destructive, 0.15)),
        _ => (Surface, MutedForeground, BorderColor),
    };

    /// <summary>Method, JF, client, route and profile badges in the WebUI's order.</summary>
    public static IEnumerable<FrameworkElement> For(DashboardSession session)
    {
        var (label, tone) = DashboardPresentation.MethodMeta(DashboardPresentation.ClassifyMethod(session));
        var (bg, fg, border) = Tone(tone);
        yield return Pill(label, bg, fg, border);
        if (session.IsJellyfinClient == true)
        {
            var jf = Pill(DashboardText.T("web.player.jellyfinSession.label"), Alpha(Jellyfin, 0.15), Jellyfin, Alpha(Jellyfin, 0.3));
            ToolTipService.SetToolTip(jf, DashboardText.T("web.player.jellyfinSession.title"));
            yield return jf;
        }
        var client = DashboardPresentation.ClientLabel(session);
        if (client != "")
        {
            var pill = Pill(client, Alpha(Muted, 0.3), MutedForeground, Alpha(BorderColor, 0.6));
            pill.MaxWidth = 144;
            ToolTipService.SetToolTip(pill, !string.IsNullOrEmpty(session.ClientUserAgent) ? session.ClientUserAgent : client);
            yield return pill;
        }
        if (!string.IsNullOrEmpty(session.StreamLocation))
        {
            var location = Pill(session.StreamLocation == "local" ? DashboardText.T("web.player.routeBadge.local") : DashboardText.T("web.player.routeBadge.remote"),
                Alpha(Primary, 0.1), Primary, Alpha(Primary, 0.2));
            ToolTipService.SetToolTip(location, DashboardText.T("web.player.routeBadge.locationTitle", ("location", session.StreamLocation)));
            yield return location;
        }
        var provider = session.RoutingNetworkProvider;
        var networkName = !string.IsNullOrEmpty(provider) ? provider
            : provider == "" ? DashboardText.T("web.player.routeBadge.defaultNetwork") : DashboardText.T("web.player.routeBadge.unknownNetwork");
        var network = RoutePill(DashboardText.T("web.player.routeBadge.network"), networkName, Alpha(Primary, 0.1), Primary, Alpha(Primary, 0.2));
        ToolTipService.SetToolTip(network, !string.IsNullOrEmpty(provider) ? DashboardText.T("web.player.routeBadge.networkTitle", ("name", provider))
            : provider == "" ? DashboardText.T("web.player.routeBadge.defaultNetworkTitle") : DashboardText.T("web.player.routeBadge.unknownNetworkTitle"));
        AutomationProperties.SetName(network, DashboardText.T("web.player.routeBadge.networkAria", ("name", networkName)));
        yield return network;
        foreach (var node in DashboardPresentation.RouteNodes(session))
        {
            var (nbg, nfg, nborder) = node.Kind switch
            {
                "transcode" => (Alpha(DashboardUi.Warning, 0.1), DashboardUi.Warning, Alpha(DashboardUi.Warning, 0.25)),
                "proxy" => (Alpha(Info, 0.1), Info, Alpha(Info, 0.25)),
                _ => (Alpha(Primary, 0.05), Primary, Alpha(Primary, 0.1)),
            };
            var text = node.Kind is "transcode" or "proxy" ? $"{node.Label} · {node.Name}" : node.Kind == "server" ? DashboardText.T("web.player.routeBadge.apiServer") : node.Name;
            var badge = node.Kind is "transcode" or "proxy" ? RoutePill(node.Label, node.Name, nbg, nfg, nborder) : Pill(text, nbg, nfg, nborder);
            var serves = session.RoutingEgress == "api" ? "yes" : "no";
            ToolTipService.SetToolTip(badge, node.Kind != "server"
                ? DashboardText.T("web.player.routeBadge.nodeTitle", ("label", node.Label), ("name", node.Name))
                : !string.IsNullOrEmpty(session.ReportingNode)
                    ? DashboardText.T("web.player.routeBadge.apiServerNodeTitle", ("serves", serves), ("node", session.ReportingNode))
                    : DashboardText.T("web.player.routeBadge.apiServerTitle", ("serves", serves)));
            yield return badge;
        }
        var profile = !string.IsNullOrEmpty(session.ProfileName) ? session.ProfileName : session.ProfileId;
        if (!string.IsNullOrEmpty(profile)) yield return ProfilePill(profile);
    }

    private static Border RoutePill(string label, string name, Color background, Color foreground, Color border)
    {
        var pill = Pill(label + " · " + name, background, foreground, border);
        var row = new Grid { ColumnSpacing = 4 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var prefix = Text(label, 9, Semibold, foreground, lineHeight: 13.5);
        prefix.Opacity = 0.7;
        row.Children.Add(prefix);
        var separator = Text("·", 9, Semibold, foreground, lineHeight: 13.5);
        Grid.SetColumn(separator, 1); row.Children.Add(separator);
        var value = Text(name, 9, Semibold, foreground, trim: true, lineHeight: 13.5);
        Grid.SetColumn(value, 2); row.Children.Add(value);
        pill.Child = row;
        return pill;
    }

    public static Border ProfilePill(string label)
    {
        var pill = Pill(label, Alpha(Primary, 0.15), Primary, Alpha(Primary, 0.3));
        ((TextBlock)pill.Child).LineHeight = 9.9;
        return pill;
    }
}

/// <summary>
/// Active streams as cards. An idle server collapses to a one-row strip; a
/// fixed-height widget shows only whole cards and counts the rest in "+N more".
/// </summary>
internal sealed class NowPlayingWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    private const int MaxRenderedStreams = 40;
    private const int StackedStreamLimit = 4;
    private const double MoreLinkHeight = 24;
    private Grid? _cards;
    private Grid? _viewport;
    private FrameworkElement? _more;
    private int _visible = int.MaxValue;
    private int _sessionCount;

    protected override IEnumerable<string> Keys => [DashboardDataStore.Sessions];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.Sessions);
        var sessions = state.As<IReadOnlyList<DashboardSession>>() ?? [];
        _sessionCount = sessions.Count;
        // Only a successful, genuinely empty read earns the strip; loading and errors keep full height.
        var idle = !state.IsLoading && state.Error == null && sessions.Count == 0;
        C.SetCollapsed(idle);
        var activity = C.Navigate(DashboardDestination.Activity);
        if (idle)
        {
            var strip = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            strip.Children.Add(Text(T("web.admin.dashboard.widgets.nowPlaying.title"), 16, Bold));
            var idleText = Text(T("web.admin.dashboard.widgets.nowPlaying.idle"), 14, color: MutedForeground, trim: true);
            idleText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(idleText, 1);
            strip.Children.Add(idleText);
            var link = Destination(T("web.admin.dashboard.widgets.nowPlaying.viewActivity"), 11, activity);
            link.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(link, 2);
            strip.Children.Add(link);
            return strip;
        }
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = Fixed ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var head = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(Text(T("web.admin.dashboard.widgets.nowPlaying.title"), 16, Bold));
        if (sessions.Count > 0)
        {
            var all = Destination(T("web.admin.dashboard.widgets.nowPlaying.viewAll", ("count", (long)sessions.Count), ("count_text", (long)sessions.Count)), 11, activity);
            all.HorizontalAlignment = HorizontalAlignment.Right;
            all.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(all);
        }
        root.Children.Add(head);
        var columns = Fixed ? 2 : 1;
        FrameworkElement body;
        if (state.IsLoading)
        {
            var skeletons = new Grid { ColumnSpacing = 14 };
            for (var i = 0; i < columns; i++) skeletons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < (columns == 2 ? 2 : 1); i++)
            {
                var skeleton = Skeleton(120, 20);
                Grid.SetColumn(skeleton, i);
                skeletons.Children.Add(skeleton);
            }
            body = columns == 2 ? skeletons : Column(14, Skeleton(120, 20), Skeleton(120, 20));
        }
        else if (state.Error != null)
        {
            body = SectionError(T("web.admin.dashboard.widgets.nowPlaying.loadFailed"));
        }
        else
        {
            var rendered = sessions.Take(Fixed ? MaxRenderedStreams : StackedStreamLimit).ToList();
            _cards = new Grid { ColumnSpacing = 14, RowSpacing = 14, VerticalAlignment = VerticalAlignment.Top };
            for (var i = 0; i < columns; i++) _cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < (rendered.Count + columns - 1) / columns; i++) _cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < rendered.Count; i++)
            {
                var card = StreamCard(rendered[i]);
                Grid.SetColumn(card, i % columns);
                Grid.SetRow(card, i / columns);
                _cards.Children.Add(card);
            }
            _viewport = new Grid();
            _viewport.Children.Add(_cards);
            _visible = Fixed ? int.MaxValue : rendered.Count;
            _more = Destination(MoreText(sessions.Count - Math.Min(_visible, rendered.Count)), 12, activity);
            _more.HorizontalAlignment = HorizontalAlignment.Center;
            _more.VerticalAlignment = Fixed ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            _more.Visibility = sessions.Count > Math.Min(_visible, rendered.Count) ? Visibility.Visible : Visibility.Collapsed;
            if (Fixed)
            {
                _viewport.Clip = null;
                _viewport.SizeChanged += (_, _) => Fit();
                _cards.SizeChanged += (_, _) => Fit();
                _viewport.Children.Add(_more);
                body = _viewport;
            }
            else
            {
                _more.Margin = new Thickness(0, 4, 0, 0);
                body = Column(0, _viewport, _more);
            }
        }
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private string MoreText(int count) => T("web.admin.dashboard.widgets.nowPlaying.moreStreams", ("count", (long)count), ("count_text", (long)count));

    /// <summary>Shows as many whole cards as fit; keeps hidden ones laid out so a taller widget can re-measure.</summary>
    private void Fit()
    {
        if (_cards == null || _viewport == null || _more == null) return;
        var height = _viewport.ActualHeight;
        var cards = _cards.Children.OfType<FrameworkElement>().ToList();
        if (cards.Count == 0 || height <= 0) return;
        double Bottom(FrameworkElement card)
        {
            var row = Grid.GetRow(card);
            double offset = 0;
            for (var r = 0; r < row; r++) offset += _cards.RowDefinitions[r].ActualHeight + _cards.RowSpacing;
            return offset + _cards.RowDefinitions[row].ActualHeight;
        }
        var next = int.MaxValue;
        if (Bottom(cards[^1]) > height)
        {
            var limit = height - MoreLinkHeight;
            var index = cards.FindIndex(card => Bottom(card) > limit);
            next = Math.Max(1, index);
        }
        var visible = Math.Min(next, cards.Count);
        if (visible == _visible) return;
        _visible = visible;
        for (var i = 0; i < cards.Count; i++)
        {
            var hidden = i >= visible;
            cards[i].Opacity = hidden ? 0 : 1;
            cards[i].IsHitTestVisible = !hidden;
            AutomationProperties.SetAccessibilityView(cards[i], hidden ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content);
            if (cards[i] is Control control) control.IsTabStop = !hidden;
        }
        var more = _sessionCount - visible;
        _more.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (more > 0)
        {
            if (_more is TextBlock text) text.Text = MoreText(more);
            else if (_more is Button { Content: TextBlock label }) label.Text = MoreText(more);
        }
    }

    private FrameworkElement StreamCard(DashboardSession session)
    {
        var episode = DashboardPresentation.IsEpisode(session);
        var title = DashboardPresentation.SessionTitle(session, "nowPlaying");
        var user = DashboardPresentation.SessionUser(session, "nowPlaying");
        var elapsed = DashboardFormat.RelativeTime(session.StartedAt, Now, floor: true, T("web.admin.dashboard.widgets.nowPlaying.justNow")) ?? "";
        var poster = new Grid { Width = 60, Height = 90, VerticalAlignment = VerticalAlignment.Top };
        var frame = new Border { Background = B(Surface), BorderBrush = B(BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        var image = Poster(session.PosterUrl, 60, 90, session.IsPaused);
        if (image != null) frame.Child = image;
        else
        {
            var play = Icon("play", 20, Alpha(Primary, 0.4));
            play.Opacity = session.IsPaused ? 0.45 : 1;
            play.HorizontalAlignment = HorizontalAlignment.Center;
            play.VerticalAlignment = VerticalAlignment.Center;
            frame.Child = play;
        }
        poster.Children.Add(frame);
        if (session.IsPaused)
        {
            poster.Children.Add(new Border { Background = B(Hex("#59000000")), CornerRadius = new CornerRadius(12) });
            var paused = new Border
            {
                Background = B(BackgroundColor, 0.9), BorderBrush = B(BorderColor, 0.4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = Row(4, Icon("pause", 12, Foreground), Text(T("web.admin.dashboard.widgets.nowPlaying.paused"), 10, Semibold)),
            };
            Stadium(paused);
            // A 60px poster can host a wider centered badge; measure outside
            // the poster's slot so WinUI does not crop the trailing letter.
            paused.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var overlay = new Canvas { Width = 60, Height = 90, IsHitTestVisible = false };
            Canvas.SetLeft(paused, (60 - paused.DesiredSize.Width) / 2);
            Canvas.SetTop(paused, (90 - paused.DesiredSize.Height) / 2);
            overlay.Children.Add(paused);
            poster.Children.Add(overlay);
        }
        var info = new Grid();
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // The title links to the catalog item in the WebUI; that is outside Monitor, so it is text.
        info.Children.Add(Text(title, 14, Bold, trim: true));
        string? subtitle = episode
            ? !string.IsNullOrEmpty(session.SeriesName)
                ? T("web.admin.dashboard.widgets.nowPlaying.episodeLineWithSeries", ("season", session.SeasonNumber.ToString()), ("episode", session.EpisodeNumber.ToString()), ("series", session.SeriesName))
                : T("web.admin.dashboard.widgets.nowPlaying.episodeLine", ("season", session.SeasonNumber.ToString()), ("episode", session.EpisodeNumber.ToString()))
            : !string.IsNullOrEmpty(session.MediaType)
                ? session.MediaType == "movie" ? T("web.admin.dashboard.widgets.nowPlaying.movie") : T("web.admin.dashboard.widgets.nowPlaying.series")
                : null;
        if (subtitle != null)
        {
            var sub = Text(subtitle, 12, color: MutedForeground, trim: true);
            sub.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetRow(sub, 1);
            info.Children.Add(sub);
        }
        var tags = new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var badge in DashboardSessionBadges.For(session)) tags.Children.Add(badge);
        Grid.SetRow(tags, 2);
        info.Children.Add(tags);
        var userRow = new Grid { ColumnSpacing = 6 };
        userRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        userRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        userRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        userRow.Children.Add(Avatar(user, 22, 9));
        var name = Text(user, 12, Medium, trim: true);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        userRow.Children.Add(name);
        var time = Text(elapsed, 10, color: MutedForeground);
        time.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(time, 2);
        userRow.Children.Add(time);
        Grid.SetRow(userRow, 4);
        info.Children.Add(userRow);
        var content = new Grid { ColumnSpacing = 14 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(poster);
        Grid.SetColumn(info, 1);
        content.Children.Add(info);
        return new Border { Background = SurfacePanel, CornerRadius = new CornerRadius(20), Padding = new Thickness(14), Child = content };
    }

    internal static FrameworkElement Avatar(string name, double size, double fontSize)
        => new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = B(Primary), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "", FontSize = fontSize, FontWeight = Bold, Foreground = B(PrimaryForeground), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
}

/// <summary>Stream nodes grouped by deployment group, worst first; columns follow the card width.</summary>
internal sealed class TranscodeNodesWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    private double _width;
    protected override IEnumerable<string> Keys => [DashboardDataStore.Nodes, DashboardDataStore.Build];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.Nodes);
        var revision = Store.Get(DashboardDataStore.Build).As<DashboardBuildInfo>()?.Revision;
        if (string.IsNullOrEmpty(revision)) revision = null;
        var nodes = state.As<IReadOnlyList<DashboardNode>>() ?? [];
        var title = new Grid { ColumnSpacing = 10 };
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        title.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        title.Children.Add(Text(T("web.admin.dashboard.widgets.transcodeNodes.title"), 14, Bold));
        if (nodes.Count > 0)
        {
            var summary = Text(DashboardPresentation.FleetSummary(nodes), 11, color: MutedForeground, trim: true);
            summary.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(summary, 1);
            title.Children.Add(summary);
        }
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(title);
        var manage = Link(T("web.admin.dashboard.widgets.transcodeNodes.manage"), null);
        manage.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(manage, 1);
        header.Children.Add(manage);
        FrameworkElement body;
        if (state.IsLoading) body = Column(6, Skeleton(24, 6), Skeleton(24, 6), Skeleton(24, 6));
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.transcodeNodes.loadFailed"));
        else if (nodes.Count == 0) body = EmptyLine(T("web.admin.dashboard.widgets.transcodeNodes.empty"));
        else body = Table(nodes, revision);
        var card = Card(header, body, paddingBottom: 12, gap: 0);
        SizeChanged -= OnResize;
        SizeChanged += OnResize;
        return card;
    }

    private void OnResize(object sender, SizeChangedEventArgs e)
    {
        var content = e.NewSize.Width - 48;
        static int Band(double w) => w < 360 ? 0 : w < 440 ? 1 : w < 620 ? 2 : w < 720 ? 3 : w < 1000 ? 4 : w < 1200 ? 5 : 6;
        if (Band(content) != Band(_width)) { _width = content; RenderNow(); }
        _width = content;
    }

    private FrameworkElement Table(IReadOnlyList<DashboardNode> nodes, string? revision)
    {
        var w = _width <= 0 ? ActualWidth - 48 : _width;
        var wide = w >= 620;
        var widest = w >= 1200;
        var showsGpu = nodes.Any(DashboardPresentation.NodeReportsAcceleration);
        var columns = new List<(string Id, string Label, double Width, bool Start)>
        {
            ("active", T("web.admin.dashboard.widgets.transcodeNodes.column.active"), 72, false),
            ("mbps", T("web.admin.dashboard.widgets.transcodeNodes.column.mbps"), wide ? 88 : 60, false),
        };
        if (w >= 360) columns.Add(("cpu", T("web.admin.nodes.metric.cpu"), widest ? 150 : wide ? 92 : 62, true));
        if (w >= 440) columns.Add(("ram", T("web.admin.nodes.metric.memory"), widest ? 150 : wide ? 92 : 62, true));
        if (showsGpu && wide) columns.Add(("gpu", T("web.admin.nodes.metric.gpu"), widest ? 150 : 92, true));
        if (widest) columns.Add(("checked", T("web.admin.nodes.unit.checked"), 86, false));
        if (widest) columns.Add(("build", T("web.admin.dashboard.widgets.transcodeNodes.column.build"), 150, false));
        var groups = DashboardPresentation.NodeGroups(nodes, revision, Now);
        var headings = groups.Any(group => group.Name != "");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var column in columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width) });
        var row = 0;
        void AddRow(double height = 24) => grid.RowDefinitions.Add(new RowDefinition { Height = height <= 0 ? GridLength.Auto : new GridLength(height) });
        void Put(FrameworkElement element, int column, bool start)
        {
            element.HorizontalAlignment = start || column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            element.VerticalAlignment = VerticalAlignment.Center;
            if (column > 0) element.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
        AddRow(0);
        var headingBorder = new Border { BorderBrush = B(BorderColor), BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetColumnSpan(headingBorder, columns.Count + 1);
        grid.Children.Add(headingBorder);
        var nodeHeading = Text(T("web.admin.dashboard.widgets.transcodeNodes.column.node"), 11, Medium, MutedForeground);
        nodeHeading.Margin = new Thickness(0, 0, 0, 4);
        Put(nodeHeading, 0, true);
        for (var i = 0; i < columns.Count; i++)
        {
            var heading = Text(columns[i].Label, 11, Medium, MutedForeground);
            Put(heading, i + 1, columns[i].Start);
            heading.Margin = new Thickness(10, 0, 0, 4);
        }
        row++;
        foreach (var group in groups)
        {
            if (headings)
            {
                AddRow(0);
                var groupTitle = Row(6, StatusIcon(group.State, null), Text(group.Label, 11, Bold, trim: true));
                groupTitle.Margin = new Thickness(0, 6, 0, 2);
                if (group.Name != "") ToolTipService.SetToolTip(groupTitle, group.Name);
                Put(groupTitle, 0, true);
                row++;
            }
            foreach (var node in group.Nodes)
            {
                AddRow();
                var state = DashboardPresentation.NodeState(node);
                var disabled = state == DashboardNodeState.Disabled;
                var drain = state is DashboardNodeState.Draining or DashboardNodeState.Drained ? DashboardPresentation.NodeDrain(node) : null;
                var storage = DashboardPresentation.TranscodeDirectoryWarning(node, Now);
                var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(headings ? 20 : 0, 0, 0, 0) };
                nameCell.Children.Add(StatusIcon(state, drain?.Label));
                var name = Text(node.Name, 12, Semibold, disabled ? MutedForeground : Foreground, trim: true);
                name.MaxWidth = Math.Max(60, w - columns.Sum(c => c.Width + 10) - 80);
                ToolTipService.SetToolTip(name, node.Name);
                nameCell.Children.Add(name);
                if (storage != null)
                    nameCell.Children.Add(Flag("hard-drive", w >= 720 ? T("web.admin.dashboard.widgets.transcodeNodes.storageFlag", ("fill", (long)(storage.Fill ?? 0))) : null,
                        T("web.admin.dashboard.widgets.transcodeNodes.storageTitle", ("fill", (long)(storage.Fill ?? 0)), ("detail", storage.Title))));
                if (DashboardPresentation.BuildDiffers(node, revision))
                    nameCell.Children.Add(Flag("triangle-alert", (storage != null ? w >= 1000 : w >= 720) ? T("web.admin.dashboard.widgets.transcodeNodes.buildDiffersFlag") : null,
                        T("web.admin.dashboard.widgets.transcodeNodes.buildDiffersTitle", ("detail", BuildTitle(node) ?? ""))));
                Put(nameCell, 0, true);
                var stale = state == DashboardNodeState.Unhealthy ? T("web.admin.dashboard.widgets.transcodeNodes.staleUnreachableTitle")
                    : disabled ? T("web.admin.dashboard.widgets.transcodeNodes.staleDisabledTitle") : null;
                var system = DashboardPresentation.NodeSystem(node, Now);
                for (var i = 0; i < columns.Count; i++)
                {
                    FrameworkElement cell = columns[i].Id switch
                    {
                        "active" => stale != null ? NoReading(stale) : ActiveCount(node),
                        "mbps" => stale != null ? NoReading(stale) : Egress(node, wide),
                        "cpu" => Load(system.Reported ? system.Cpu : null, system.Reported ? null : system.Title, wide),
                        "ram" => Load(system.Reported ? system.Memory : null, system.Reported ? null : system.Title, wide),
                        "gpu" => DashboardPresentation.NodeReportsAcceleration(node) ? Load(DashboardPresentation.NodeGpuBusy(node, Now), system.Reported ? null : system.Title, wide) : new Grid(),
                        "checked" => Text(DashboardFormat.RelativeTime(node.LastHealthCheck, Now, floor: true, T("web.admin.dashboard.widgets.transcodeNodes.justNow")) ?? T("web.admin.nodes.unit.never"), 11, color: MutedForeground),
                        _ => BuildCell(node),
                    };
                    if (disabled && cell is TextBlock text) text.Foreground = B(MutedForeground);
                    Put(cell, i + 1, columns[i].Start);
                }
                row++;
            }
        }
        return grid;
    }

    private static FrameworkElement StatusIcon(DashboardNodeState state, string? title)
    {
        var (icon, color) = state switch
        {
            DashboardNodeState.Healthy => ("circle-check", Emerald500),
            DashboardNodeState.Draining => ("circle-pause", DashboardUi.Warning),
            DashboardNodeState.Drained => ("circle-pause", Info),
            DashboardNodeState.Unhealthy => ("circle-x", Destructive),
            _ => ("circle-slash", MutedForeground),
        };
        var element = Icon(icon, 14, color);
        var name = title ?? DashboardPresentation.NodeStateLabel(state);
        AutomationProperties.SetName(element, name);
        AutomationProperties.SetAccessibilityView(element, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content);
        ToolTipService.SetToolTip(element, name);
        element.IsHitTestVisible = true;
        return element;
    }

    private static FrameworkElement Flag(string icon, string? label, string title)
    {
        var flag = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        flag.Children.Add(Icon(icon, 14, DashboardUi.Warning));
        if (label != null) flag.Children.Add(Text(label, 11, Medium, DashboardUi.Warning));
        ToolTipService.SetToolTip(flag, title);
        AutomationProperties.SetName(flag, title);
        return flag;
    }

    private static FrameworkElement NoReading(string title)
    {
        var text = Text(DashboardFormat.EnDash, 12, color: MutedForeground);
        ToolTipService.SetToolTip(text, title);
        return text;
    }

    private FrameworkElement ActiveCount(DashboardNode node)
    {
        var jobs = DashboardPresentation.NodeJobs(node);
        var block = new TextBlock { FontSize = 12 };
        block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = N(node.ActiveJobs), Foreground = B(jobs.Warning ? DashboardUi.Warning : Foreground), FontWeight = jobs.Warning ? Semibold : new Windows.UI.Text.FontWeight { Weight = 400 } });
        if (DashboardPresentation.PositiveCap(node.MaxJobs) is { } cap)
            block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " + T("web.admin.dashboard.widgets.transcodeNodes.capSeparator", ("max", N(cap))), Foreground = B(MutedForeground) });
        var proxy = node.Type == "proxy";
        block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = " " + T(proxy ? "web.admin.dashboard.widgets.transcodeNodes.streamsAbbr" : "web.admin.dashboard.widgets.transcodeNodes.transcodesAbbr"),
            FontSize = 10,
            Foreground = B(MutedForeground),
        });
        ToolTipService.SetToolTip(block, jobs.Title);
        return block;
    }

    private static FrameworkElement Egress(DashboardNode node, bool wide)
    {
        var metric = DashboardPresentation.NodeEgress(node);
        var block = new TextBlock { FontSize = 12 };
        block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = DashboardFormat.MbpsValue(node.EgressKbps / 1000d),
            Foreground = B(metric.Warning ? DashboardUi.Warning : node.EgressKbps == 0 ? MutedForeground : Foreground),
            FontWeight = metric.Warning ? Semibold : new Windows.UI.Text.FontWeight { Weight = 400 },
        });
        if (wide && DashboardPresentation.PositiveCap(node.MaxBandwidthKbps) is { } cap)
            block.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " + T("web.admin.dashboard.widgets.transcodeNodes.capSeparator", ("max", DashboardFormat.MbpsValue(cap / 1000d))), Foreground = B(MutedForeground) });
        ToolTipService.SetToolTip(block, metric.Title);
        return block;
    }

    private static FrameworkElement Load(DashboardMetric? metric, string? missing, bool wide)
    {
        if (metric?.Fill is not { } fill)
        {
            var dash = Text(DashboardFormat.EnDash, 12, color: MutedForeground);
            dash.Width = 32;
            dash.TextAlignment = TextAlignment.Right;
            if ((metric?.Title ?? missing) is { } title) ToolTipService.SetToolTip(dash, title);
            return dash;
        }
        var high = fill >= 90;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var percent = Text(T("web.admin.dashboard.widgets.transcodeNodes.loadPercent", ("percent", (long)fill)), 12, high ? Semibold : null, high ? DashboardUi.Warning : Foreground);
        percent.Width = 32;
        percent.TextAlignment = TextAlignment.Right;
        row.Children.Add(percent);
        if (wide)
        {
            var track = new Grid { Width = 60, Height = 4, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { Background = B(Foreground, 0.15), CornerRadius = new CornerRadius(2) });
            track.Children.Add(new Border { Background = B(high ? DashboardUi.Warning : Chart(0)), CornerRadius = new CornerRadius(2), Width = 60 * fill / 100.0, HorizontalAlignment = HorizontalAlignment.Left });
            row.Children.Add(track);
        }
        ToolTipService.SetToolTip(row, metric.Title);
        return row;
    }

    private static string? BuildTitle(DashboardNode node)
    {
        var build = node.LastStats?.Build;
        if (build == null) return null;
        var parts = new List<string>
        {
            !string.IsNullOrEmpty(build.Revision) ? T("web.admin.dashboard.widgets.transcodeNodes.revision", ("revision", build.Revision))
                : T("web.admin.dashboard.widgets.transcodeNodes.revisionUnavailable"),
        };
        if (!string.IsNullOrEmpty(build.BuiltAt)) parts.Add(T("web.admin.dashboard.widgets.transcodeNodes.builtAt", ("date", build.BuiltAt)));
        return string.Join(" · ", parts);
    }

    private static FrameworkElement BuildCell(DashboardNode node)
    {
        var build = node.LastStats?.Build;
        if (build == null) return new Grid();
        var display = !string.IsNullOrEmpty(build.Display) ? build.Display : T("web.admin.dashboard.widgets.transcodeNodes.buildUnavailable");
        var text = Text(build.BuildNumber is > 0 ? $"{build.BuildNumber} · {display}" : display, 11, color: MutedForeground, trim: true);
        if (BuildTitle(node) is { } title) ToolTipService.SetToolTip(text, title);
        return text;
    }
}

/// <summary>Queue counters, autoscan state and the libraries scanning right now (from the scans channel).</summary>
internal sealed class ScannerWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Autoscan, DashboardDataStore.Libraries];
    protected override IEnumerable<string> RenderTriggers => ["scans"];

    protected override FrameworkElement Render()
    {
        var statusState = Store.Get(DashboardDataStore.Autoscan);
        var status = statusState.As<DashboardAutoscanStatus>();
        var names = (Store.Get(DashboardDataStore.Libraries).As<IReadOnlyList<DashboardLibrary>>() ?? []).ToDictionary(l => l.Id, l => l.Name);
        var groups = DashboardPresentation.GroupActiveScans(Store.ActiveScans);
        // Autoscan history opens a page outside the Monitor scope: plain text.
        var header = Header(T("web.admin.dashboard.widgets.scanner.title"), Link(T("web.admin.dashboard.widgets.scanner.activity"), null));
        FrameworkElement body;
        if (statusState.IsLoading && groups.Count == 0) body = Skeleton(92, 10);
        else if (statusState.Error != null && groups.Count == 0) body = SectionError(T("web.admin.dashboard.widgets.scanner.loadFailed"));
        else
        {
            var stack = new StackPanel { Spacing = 12 };
            var counts = new Grid { ColumnSpacing = 8 };
            for (var i = 0; i < 3; i++) counts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Count(string label, long? value, int column)
            {
                var cell = Column(4, Text(value is { } v ? N(v) : DashboardFormat.Dash, 18, ExtraBold, lineHeight: 18), Text(label, 11, Medium, MutedForeground));
                Grid.SetColumn(cell, column);
                counts.Children.Add(cell);
            }
            Count(T("web.admin.dashboard.widgets.scanner.running"), status?.RunningScans, 0);
            Count(T("web.admin.dashboard.widgets.scanner.queued"), status?.AcceptedScans, 1);
            Count(T("web.admin.dashboard.widgets.scanner.polling"), status?.RunningPolls?.Count ?? 0, 2);
            stack.Children.Add(counts);
            var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (status?.Enabled is not { } enabled) state.Children.Add(Muted11(T("web.admin.dashboard.widgets.scanner.autoscanUnknown")));
            else
            {
                state.Children.Add(Row(4, Icon(enabled ? "circle-check" : "circle-slash", 14, enabled ? Emerald500 : MutedForeground),
                    Muted11(enabled ? T("web.admin.dashboard.widgets.scanner.autoscanEnabled") : T("web.admin.dashboard.widgets.scanner.autoscanDisabled"))));
                if (enabled)
                {
                    state.Children.Add(Text("·", 11, color: Alpha(BorderColor, 0.7)));
                    var last = DashboardFormat.RelativeTime(status.LatestEventAt, Now, floor: true);
                    state.Children.Add(Muted11(last == null ? T("web.admin.dashboard.widgets.scanner.lastEventNever") : T("web.admin.dashboard.widgets.scanner.lastEvent", ("time", last))));
                }
            }
            stack.Children.Add(state);
            var list = new StackPanel { Spacing = 8 };
            if (groups.Count == 0)
                list.Children.Add(Row(8, Icon("scan-line", 16, MutedForeground), Text(T("web.admin.dashboard.widgets.scanner.noScans"), 14, color: MutedForeground)));
            else
            {
                foreach (var group in groups.Take(4))
                    list.Children.Add(new Border
                    {
                        Background = B(Surface), BorderBrush = B(BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10),
                        Child = Column(2,
                            Text(names.TryGetValue(group.LibraryId, out var name) ? name : T("web.admin.dashboard.widgets.scanner.libraryFallback", ("id", group.LibraryId)), 13, Semibold, trim: true),
                            Text(DashboardPresentation.LibraryScanProgress(group.Primary, group.Count), 11, color: MutedForeground, trim: true)),
                    });
                if (groups.Count > 4)
                    list.Children.Add(Muted11(T("web.admin.dashboard.widgets.scanner.moreLibraries", DashboardText.Count(groups.Count - 4))));
            }
            stack.Children.Add(new Border { BorderBrush = B(BorderColor, 0.6), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Child = list });
            body = stack;
        }
        return Card(header, body);
    }
}

/// <summary>Library rows with per-library scan/stop controls and live progress.</summary>
internal sealed class LibrariesWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Libraries];
    protected override IEnumerable<string> RenderTriggers => ["scans", "actions"];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.Libraries);
        var libraries = state.As<IReadOnlyList<DashboardLibrary>>() ?? [];
        // Library management is outside the Monitor scope: "Manage ›" is plain text.
        var header = Header(T("web.admin.dashboard.widgets.libraries.title"), Link(T("web.admin.dashboard.widgets.libraries.manage"), null));
        FrameworkElement body;
        if (state.IsLoading) body = Column(8, Skeleton(60, 10), Skeleton(60, 10), Skeleton(60, 10));
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.libraries.loadFailed"));
        else if (libraries.Count == 0) body = EmptyLine(T("web.admin.dashboard.widgets.libraries.empty"));
        else
        {
            var active = Store.ActiveScans.Where(scan => scan.IsActive).GroupBy(scan => scan.LibraryId)
                .ToDictionary(group => group.Key, group => group.OrderBy(scan => scan, Comparer<DashboardActiveScan>.Create(DashboardPresentation.CompareActiveScans)).ToList());
            var list = new StackPanel { Spacing = 8 };
            foreach (var library in libraries) list.Children.Add(LibraryRow(library, active.GetValueOrDefault(library.Id) ?? []));
            body = list;
        }
        return Card(header, body);
    }

    private FrameworkElement LibraryRow(DashboardLibrary library, IReadOnlyList<DashboardActiveScan> scans)
    {
        var hasActive = scans.Count > 0;
        var starting = Store.IsStartingLibraryScan(library.Id);
        var cancelling = Store.IsCancellingLibraryScans(library.Id);
        var progress = hasActive ? DashboardPresentation.LibraryScanProgress(scans[0], scans.Count)
            : starting ? T("web.admin.dashboard.widgets.libraries.startingScan") : "";
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement art = Poster(library.PosterUrl, 56, 32) is { } image
            ? new Border { Width = 56, Height = 32, CornerRadius = new CornerRadius(6), BorderBrush = B(BorderColor), BorderThickness = new Thickness(1), Child = image }
            : new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = B(Primary, 0.05), BorderBrush = B(Primary, 0.1), BorderThickness = new Thickness(1),
                Child = new Grid { Children = { Center(Icon("library", 16, Primary)) } },
            };
        art.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(art);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var paths = library.Paths?.Count ?? 0;
        meta.Children.Add(Muted11(T("web.admin.dashboard.widgets.libraries.typeAndPaths", ("count", (long)paths), ("count_text", (long)paths), ("type", DashboardPresentation.LibraryTypeLabel(library.Type)))));
        if (progress != "")
        {
            meta.Children.Add(Text("·", 11, color: Alpha(BorderColor, 0.7)));
            var progressText = Text(progress, 11, color: Amber300, trim: true);
            progressText.MaxWidth = 352;
            meta.Children.Add(progressText);
        }
        var info = Column(0, Text(library.Name, 14, Bold, trim: true), meta);
        info.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var button = hasActive
            ? IconButton("square", 28, 12, T("web.admin.dashboard.widgets.libraries.stopScansLabel", ("name", library.Name)), Microsoft.UI.Colors.White, Microsoft.UI.Colors.White,
                Destructive, Alpha(Destructive, 0.9), async (_, _) => await Store.CancelLibraryScansAsync(library.Id), 10)
            : IconButton("scan-line", 28, 12, T("web.admin.dashboard.widgets.libraries.scanLabel", ("name", library.Name)), Foreground, Res("ThemeAccentForegroundColor", "#E8E8EC"),
                Microsoft.UI.Colors.Transparent, Accent, async (_, _) => await Store.StartLibraryScanAsync(library.Id), 10);
        if (hasActive) button.Content = Icon("square", 12, Microsoft.UI.Colors.White, fill: true);
        ToolTipService.SetToolTip(button, hasActive ? T("web.admin.dashboard.widgets.libraries.stopScansTitle") : T("web.admin.dashboard.widgets.libraries.scanTitle"));
        button.IsEnabled = hasActive ? !cancelling : !starting;
        controls.Children.Add(button);
        var dotColor = hasActive || starting ? Amber400 : library.Enabled ? Green500 : Alpha(MutedForeground, 0.3);
        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = B(dotColor), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(dot, hasActive ? T("web.admin.dashboard.widgets.libraries.scanInProgress")
            : library.Enabled ? T("web.admin.dashboard.widgets.libraries.enabled") : T("web.admin.dashboard.widgets.libraries.disabled"));
        controls.Children.Add(dot);
        Grid.SetColumn(controls, 2);
        grid.Children.Add(controls);
        var row = new Border { Background = B(Surface), BorderBrush = B(BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = grid };
        row.PointerEntered += (_, _) => row.Background = B(SurfaceHover);
        row.PointerExited += (_, _) => row.Background = B(Surface);
        return row;
    }

    private static FrameworkElement Center(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        return element;
    }
}

internal abstract class DashboardTableWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    /// <summary>shadcn Table: 40px muted header row with bottom border, padded cells, bordered rows.</summary>
    protected Grid Table(IReadOnlyList<(string Header, GridLength Width, bool End)> columns, IReadOnlyList<FrameworkElement[]> rows, bool contentWidths = false)
    {
        var padding = C.ViewportWidth() >= 640 ? 12 : 8;
        var grid = new Grid();
        foreach (var column in columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Width });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        var headBorder = new Border { BorderBrush = B(BorderColor), BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetColumnSpan(headBorder, columns.Count);
        grid.Children.Add(headBorder);
        for (var i = 0; i < columns.Count; i++)
        {
            var head = Text(columns[i].Header, 14, Medium, Foreground);
            head.Margin = new Thickness(padding, 0, padding, 0);
            head.VerticalAlignment = VerticalAlignment.Center;
            head.HorizontalAlignment = columns[i].End ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Grid.SetColumn(head, i);
            grid.Children.Add(head);
        }
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (r < rows.Count - 1)
            {
                var line = new Border { BorderBrush = B(BorderColor), BorderThickness = new Thickness(0, 0, 0, 1) };
                Grid.SetRow(line, r + 1);
                Grid.SetColumnSpan(line, columns.Count);
                grid.Children.Add(line);
            }
            for (var i = 0; i < rows[r].Length && i < columns.Count; i++)
            {
                var cell = rows[r][i];
                cell.Margin = new Thickness(padding, 8, padding, 8);
                cell.VerticalAlignment = VerticalAlignment.Center;
                if (columns[i].End) cell.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(cell, r + 1);
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
        }
        if (contentWidths)
        {
            // HTML's automatic table layout shares spare width in proportion to
            // the columns' preferred content widths rather than equally.
            for (var column = 0; column < columns.Count; column++)
            {
                var preferred = 1d;
                foreach (var child in grid.Children.OfType<FrameworkElement>().Where(child => Grid.GetColumn(child) == column && Grid.GetColumnSpan(child) == 1))
                {
                    child.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    preferred = Math.Max(preferred, child.DesiredSize.Width);
                }
                grid.ColumnDefinitions[column].Width = new GridLength(preferred, GridUnitType.Star);
            }
        }
        return grid;
    }

    protected static FrameworkElement UserSkeletons() => Column(8, Skeleton(40, 10), Skeleton(40, 10), Skeleton(40, 10), Skeleton(40, 10));
}

/// <summary>The eight most recently active accounts with role and status.</summary>
internal sealed class UsersWidget(DashboardWidgetContext c) : DashboardTableWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Users];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.Users);
        var header = Header(T("web.admin.dashboard.widgets.users.title"), Link(T("web.admin.dashboard.widgets.users.manage"), null));
        FrameworkElement body;
        var users = DashboardPresentation.SortUsersByLastActive(state.As<IReadOnlyList<DashboardUser>>() ?? []);
        if (state.IsLoading) body = UserSkeletons();
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.users.loadFailed"));
        else if (users.Count == 0) body = EmptyLine(T("web.admin.dashboard.widgets.users.empty"));
        else
        {
            var small = C.Mode() == DashboardGridMode.Single;
            var columns = new List<(string, GridLength, bool)> { (T("web.admin.dashboard.widgets.users.user"), new GridLength(1, GridUnitType.Star), false) };
            if (!small) columns.Add((T("web.admin.dashboard.widgets.users.lastActive"), GridLength.Auto, false));
            columns.Add((T("web.admin.dashboard.widgets.users.status"), GridLength.Auto, false));
            var rows = users.Take(8).Select(user =>
            {
                var nameLine = new Grid { ColumnSpacing = 6 };
                nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                nameLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                nameLine.Children.Add(Text(user.Username, 13, Semibold, trim: true));
                if (user.Role == "admin")
                {
                    var role = Pill(DashboardPresentation.AccountRole(user), Primary, PrimaryForeground, Microsoft.UI.Colors.Transparent, 9, 10, Medium, new Thickness(6, 0, 6, 0));
                    Grid.SetColumn(role, 1);
                    nameLine.Children.Add(role);
                }
                var identity = Column(0, nameLine);
                if (!small) identity.Children.Add(Text(user.Email, 10, color: MutedForeground, trim: true));
                var who = new Grid { ColumnSpacing = 10 };
                who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                who.Children.Add(NowPlayingWidget.Avatar(user.Username, 28, 10));
                Grid.SetColumn(identity, 1);
                who.Children.Add(identity);
                var cells = new List<FrameworkElement> { who };
                if (!small) cells.Add(Text(DashboardFormat.RelativeTime(user.LastActiveAt, Now, floor: true, T("web.admin.dashboard.widgets.users.justNow")) ?? DashboardFormat.Dash, 12, color: MutedForeground));
                cells.Add(user.Enabled
                    ? Pill(T("web.admin.dashboard.widgets.users.active"), Microsoft.UI.Colors.Transparent, Foreground, BorderColor, 12, 10, Medium, new Thickness(8, 2, 8, 2))
                    : Pill(T("web.admin.dashboard.widgets.users.disabled"), Destructive, Microsoft.UI.Colors.White, Microsoft.UI.Colors.Transparent, 12, 10, Medium, new Thickness(8, 2, 8, 2)));
                return cells.ToArray();
            }).ToList();
            body = Table(columns, rows);
        }
        return Card(header, body);
    }
}

/// <summary>Recent and in-flight scan runs with a derived duration.</summary>
internal sealed class ScanActivityWidget(DashboardWidgetContext c) : DashboardTableWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.ScanHistory, DashboardDataStore.Libraries];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.ScanHistory);
        var names = (Store.Get(DashboardDataStore.Libraries).As<IReadOnlyList<DashboardLibrary>>() ?? []).ToDictionary(l => l.Id, l => l.Name);
        var scans = state.As<DashboardScanHistoryPage>()?.Items ?? [];
        var header = Header(T("web.admin.dashboard.widgets.scanActivity.title"), Link(T("web.admin.dashboard.widgets.scanActivity.allScans"), null));
        FrameworkElement body;
        if (state.IsLoading) body = UserSkeletons();
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.scanActivity.loadFailed"));
        else if (scans.Count == 0) body = EmptyLine(T("web.admin.dashboard.widgets.scanActivity.empty"));
        else
        {
            var mode = C.Mode();
            var columns = new List<(string, GridLength, bool)> { (T("web.admin.dashboard.widgets.scanActivity.library"), new GridLength(1, GridUnitType.Star), false) };
            if (mode != DashboardGridMode.Single) columns.Add((T("web.admin.dashboard.widgets.scanActivity.mode"), new GridLength(1, GridUnitType.Star), false));
            if (C.ViewportWidth() >= 768) columns.Add((T("web.admin.dashboard.widgets.scanActivity.trigger"), new GridLength(1, GridUnitType.Star), false));
            columns.Add((T("web.admin.dashboard.widgets.scanActivity.status"), new GridLength(1, GridUnitType.Star), false));
            columns.Add((T("web.admin.dashboard.widgets.scanActivity.duration"), new GridLength(1, GridUnitType.Star), true));
            var rows = scans.Take(8).Select(scan =>
            {
                var library = Text(names.TryGetValue(scan.LibraryId, out var name) ? name : T("web.admin.dashboard.widgets.scanActivity.libraryFallback", ("id", scan.LibraryId)), 13, Semibold, trim: true);
                library.MaxWidth = 224;
                library.HorizontalAlignment = HorizontalAlignment.Left;
                var cells = new List<FrameworkElement> { library };
                if (mode != DashboardGridMode.Single) cells.Add(Muted11(DashboardPresentation.ScanMode(scan.Mode)));
                if (C.ViewportWidth() >= 768) cells.Add(Muted11(DashboardPresentation.ScanTrigger(scan.Trigger)));
                cells.Add(Status(scan.Status));
                cells.Add(Muted11(DashboardFormat.ScanDuration(scan.StartedAt, scan.CompletedAt, Now)));
                return cells.ToArray();
            }).ToList();
            body = Table(columns, rows, contentWidths: true);
        }
        return Card(header, body);
    }

    private static FrameworkElement Status(string status)
    {
        var (key, icon, color) = status switch
        {
            "completed" => ("completed", "circle-check", Emerald500),
            "failed" => ("failed", "triangle-alert", Destructive),
            "running" => ("running", "loader-circle", Sky500),
            "cancelled" => ("cancelled", "circle-slash", Amber500),
            _ => ("queued", "clock", MutedForeground),
        };
        return Row(4, Icon(icon, 14, color), Text(DashboardText.T("web.admin.dashboard.widgets.scanActivity." + key), 11, Medium, color));
    }
}

/// <summary>The newest error and warning lines from the operational log.</summary>
internal sealed class RecentErrorsWidget(DashboardWidgetContext c) : DashboardTableWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.RecentErrors];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.RecentErrors);
        var entries = state.As<IReadOnlyList<DashboardLogEntry>>() ?? [];
        var header = Header(T("web.admin.dashboard.widgets.recentErrors.title"), Link(T("web.admin.dashboard.widgets.recentErrors.allLogs"), DashboardDestination.Logs));
        FrameworkElement body;
        if (state.IsLoading) body = UserSkeletons();
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.recentErrors.loadFailed"));
        else if (entries.Count == 0) body = EmptyLine(T("web.admin.dashboard.widgets.recentErrors.empty"));
        else
        {
            var list = new StackPanel();
            foreach (var entry in entries.Take(8))
            {
                var (label, icon, color) = entry.Level.ToLowerInvariant() switch
                {
                    "error" or "fatal" => (T("web.admin.dashboard.widgets.recentErrors.levelError"), "circle-x", Destructive),
                    "warn" or "warning" => (T("web.admin.dashboard.widgets.recentErrors.levelWarn"), "triangle-alert", Amber500),
                    _ => (string.IsNullOrEmpty(entry.Level) ? T("web.admin.dashboard.widgets.recentErrors.levelFallback") : entry.Level, "info", MutedForeground),
                };
                var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 8, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var level = Row(4, Icon(icon, 14, color), Text(label.ToUpperInvariant(), 10, Bold, color));
                level.VerticalAlignment = VerticalAlignment.Top;
                level.Margin = new Thickness(0, 2, 0, 0);
                grid.Children.Add(level);
                var meta = (string.IsNullOrEmpty(entry.Component) ? T("web.admin.dashboard.widgets.recentErrors.componentFallback") : entry.Component) + " · "
                    + (DashboardFormat.RelativeTime(entry.Timestamp, Now, floor: true, T("web.admin.dashboard.widgets.recentErrors.justNow")) ?? entry.Timestamp?.ToString("O") ?? "");
                var text = Column(2, Text(entry.Message, 12, trim: true), Text(meta, 10, color: MutedForeground));
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);
                list.Children.Add(new Border { BorderBrush = B(BorderColor, 0.3), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
            }
            body = list;
        }
        return Card(header, body);
    }
}

/// <summary>Feed of recently started sessions with the optional compact session controls.</summary>
internal sealed class RecentActivityWidget(DashboardWidgetContext c) : DashboardTableWidget(c)
{
    protected override IEnumerable<string> Keys => [DashboardDataStore.Sessions];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(DashboardDataStore.Sessions);
        var sessions = state.As<IReadOnlyList<DashboardSession>>() ?? [];
        var header = Header(T("web.admin.dashboard.widgets.recentActivity.title"), Link(T("web.admin.dashboard.widgets.recentActivity.viewAll"), DashboardDestination.Activity));
        FrameworkElement body;
        if (state.IsLoading) body = Column(0, Enumerable.Range(0, 4).Select(_ => (UIElement)ActivitySkeletonRow()).ToArray());
        else if (state.Error != null) body = SectionError(T("web.admin.dashboard.widgets.recentActivity.loadFailed"));
        else if (sessions.Count == 0) body = EmptyLine(T("web.admin.dashboard.widgets.recentActivity.empty"));
        else
        {
            var list = new StackPanel();
            foreach (var session in sessions.Take(10)) list.Children.Add(ActivityRow(session));
            body = list;
        }
        return Card(header, body);
    }

    private static FrameworkElement ActivitySkeletonRow()
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Skeleton(30, 12, 30));
        var lines = Column(6, Skeleton(12, 4), Skeleton(8, 4));
        ((FrameworkElement)lines.Children[0]).HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);
        return new Border { BorderBrush = B(BorderColor, 0.3), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private FrameworkElement ActivityRow(DashboardSession session)
    {
        var title = DashboardPresentation.SessionTitle(session, "recentActivity");
        var user = DashboardPresentation.SessionUser(session, "recentActivity");
        var profile = !string.IsNullOrEmpty(session.ProfileName) ? session.ProfileName : session.ProfileId ?? "";
        var client = DashboardPresentation.ClientLabel(session);
        var meta = string.Join(" · ", new[] { DashboardFormat.RelativeTime(session.StartedAt, Now, floor: true, T("web.admin.dashboard.widgets.recentActivity.justNow")), client }
            .Where(part => !string.IsNullOrEmpty(part)));
        var runs = DashboardText.Rich(profile != "" ? "web.admin.dashboard.widgets.recentActivity.startedWatchingWithProfile" : "web.admin.dashboard.widgets.recentActivity.startedWatching",
            ("username", user), ("profile", profile), ("title", title));
        var sentence = new WrapPanel { HorizontalSpacing = 0, VerticalSpacing = 2 };
        var pendingSpace = false;
        foreach (var run in runs)
        {
            var runText = run.Text.Trim();
            if (runText.Length == 0) { pendingSpace |= run.Text.Length > 0; continue; }
            FrameworkElement part = run.Tag == "pill"
                ? DashboardSessionBadges.ProfilePill(runText)
                : Text(runText, 12, run.Tag is "user" or "link" ? Semibold : null, run.Tag is "user" or "link" ? Foreground : MutedForeground, lineHeight: 19.5);
            // Inline whitespace is measured explicitly: TextBlock discards trailing spaces.
            if (sentence.Children.Count > 0 && (pendingSpace || char.IsWhiteSpace(run.Text[0])))
                part.Margin = new Thickness(3, 0, 0, 0);
            sentence.Children.Add(part);
            pendingSpace = char.IsWhiteSpace(run.Text[^1]);
        }
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(12), Background = B(Primary, 0.05), BorderBrush = B(Primary, 0.1), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Grid { Children = { Center(Icon("play", 14, Primary)) } },
        };
        grid.Children.Add(icon);
        var text = Column(2, sentence, Text(meta, 10, color: MutedForeground));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (C.SessionActions?.Invoke(session) is { } actions)
        {
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);
        }
        return new Border { BorderBrush = B(BorderColor, 0.3), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private static FrameworkElement Center(FrameworkElement element)
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        return element;
    }
}
