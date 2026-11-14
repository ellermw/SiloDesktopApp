using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using Windows.UI;
using A = SiloPlayer.Controls.AdminMonitorUi;
using P = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminActivityPage
{
    private static readonly TimeSpan FfmpegStaleAfter = TimeSpan.FromSeconds(5);

    /// <summary>Per-session UI state kept across live refreshes; dropped when the session ends.</summary>
    private sealed class RowState
    {
        public bool Details;
        public bool Ffmpeg;
        public bool? OptimisticPaused;
        public bool? OptimisticBasis;
        public string? PendingAction;
        public IReadOnlyList<AdminActivityLogRow> FfmpegRows = [];
        public bool FfmpegLoading;
        public bool FfmpegFetching;
        public DateTimeOffset FfmpegReadAt = DateTimeOffset.MinValue;
        public long FfmpegVersion;
    }

    private RowState StateOf(string sessionId)
    {
        if (!_rowStates.TryGetValue(sessionId, out var state)) _rowStates[sessionId] = state = new RowState();
        return state;
    }

    // ── Table ────────────────────────────────────────────────

    private static readonly (double Min, double Star)[] Columns = [(120, 1.1), (190, 1.7), (220, 1.9), (90, 0.8), (125, 0.9), (232, 1.4)];

    private static Grid TableGrid()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        foreach (var (min, star) in Columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(star, GridUnitType.Star), MinWidth = min });
        return grid;
    }

    private void RenderTable()
    {
        _tableHost.Children.Clear();
        if (!_snapshot.SessionsLoaded) return;
        var sessions = _snapshot.Sessions;
        if (_snapshot.SessionsError is { } error)
        {
            var warning = new StackPanel { Spacing = 8 };
            warning.Children.Add(A.Text(AdminText.T(sessions.Count == 0
                ? "web.admin.activity.page.loadFailed" : "web.admin.activity.page.stale"), 14, A.Warning, trim: false));
            warning.Children.Add(A.Text(error, 14, A.Foreground, trim: false));
            var retry = A.Outline(AdminText.T("common.retry"), "refresh-cw");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(retry, AdminText.T("common.retry"));
            retry.IsEnabled = !_refreshing;
            retry.Click += (_, _) => _ = RefreshAsync();
            warning.Children.Add(retry);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(warning, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            _tableHost.Children.Add(A.Panel(warning, new Thickness(16)));
            if (sessions.Count == 0)
            {
                RenderedSessionIds = [];
                return;
            }
        }
        var filtered = _list.Apply(sessions);
        RenderedSessionIds = filtered.Select(s => s.SessionId).ToArray();
        if (filtered.Count == 0)
        {
            _tableHost.Children.Add(EmptyState(sessions.Count > 0));
            return;
        }
        var wide = IsWideTable;
        var card = new StackPanel();
        if (wide) card.Children.Add(HeaderRow());
        else card.Children.Add(SortRow());
        var rows = new StackPanel();
        for (var i = 0; i < filtered.Count; i++) rows.Children.Add(StreamRow(filtered[i], i % 2 == 0, wide));
        var viewport = XamlRoot?.Size.Height ?? 900;
        card.Children.Add(new ScrollViewer
        {
            Content = rows,
            MaxHeight = Math.Max(240, viewport - 420),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        _tableHost.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = A.Solid(A.Border),
            Background = A.Brush("CardBackgroundBrush"), Child = card,
        });
    }

    private static UIElement EmptyState(bool hasData)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(0, 80, 0, 80), Spacing = 12 };
        var icon = A.Icon(hasData ? "filter" : "play", 32, A.Muted);
        icon.Opacity = 0.2;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(icon);
        var text = A.Text(AdminText.T(hasData ? "web.admin.activity.empty.noMatches" : "web.admin.activity.page.noStreams"), 14, A.Muted);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(text);
        return panel;
    }

    private Button SortHeader(ActivitySortField field, string key, bool alignRight = false)
    {
        var active = _list.SortField == field;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(A.Eyebrow(AdminText.T(key), active ? A.Foreground : A.Muted, 100));
        if (active) content.Children.Add(A.Text(_list.SortAscending ? "▲" : "▼", 8, A.Foreground));
        var button = A.Plain(content, AdminText.T(key));
        button.HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        button.Click += (_, _) => { _list.ToggleSort(field); RenderTable(); };
        return button;
    }

    private UIElement HeaderRow()
    {
        var grid = TableGrid();
        grid.Padding = new Thickness(12, 10, 12, 10);
        grid.Background = A.Solid(A.Surface, 0.5);
        grid.BorderBrush = A.Solid(A.Border);
        grid.BorderThickness = new Thickness(0, 0, 0, 1);
        var cells = new UIElement[]
        {
            SortHeader(ActivitySortField.Username, "web.admin.activity.column.user"),
            SortHeader(ActivitySortField.Media, "web.admin.activity.column.stream"),
            SortHeader(ActivitySortField.Method, "web.admin.activity.column.playback"),
            SortHeader(ActivitySortField.Node, "web.admin.activity.column.route"),
            SortHeader(ActivitySortField.Started, "web.admin.activity.column.time", alignRight: true),
            new TextBlock
            {
                Text = AdminText.T("web.admin.activity.column.actions").ToUpper(System.Globalization.CultureInfo.CurrentCulture),
                FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 100, Foreground = A.Solid(A.Muted),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn((FrameworkElement)cells[i], i);
            grid.Children.Add(cells[i]);
        }
        return grid;
    }

    private UIElement SortRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16, Padding = new Thickness(16, 10, 16, 10),
            Background = A.Solid(A.Surface, 0.5), BorderBrush = A.Solid(A.Border), BorderThickness = new Thickness(0, 0, 0, 1),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, AdminText.T("web.admin.activity.sort.group"));
        row.Children.Add(A.Eyebrow(AdminText.T("web.admin.activity.sort.heading"), A.WithAlpha(A.Muted, 0.7), 100));
        row.Children.Add(SortHeader(ActivitySortField.Username, "web.admin.activity.column.user"));
        row.Children.Add(SortHeader(ActivitySortField.Media, "web.admin.activity.column.stream"));
        row.Children.Add(SortHeader(ActivitySortField.Method, "web.admin.activity.column.playback"));
        row.Children.Add(SortHeader(ActivitySortField.Node, "web.admin.activity.column.route"));
        row.Children.Add(SortHeader(ActivitySortField.Started, "web.admin.activity.column.time"));
        return row;
    }

    // ── Row ──────────────────────────────────────────────────

    private UIElement StreamRow(AdminActivitySession session, bool even, bool wide)
    {
        var state = StateOf(session.SessionId);
        // Optimism lasts only until a new observation of the same session arrives.
        if (state.OptimisticBasis != session.IsPaused) { state.OptimisticPaused = null; state.OptimisticBasis = session.IsPaused; }
        var container = new StackPanel
        {
            Background = even ? null : A.Solid(A.Surface, 0.2),
            BorderBrush = A.Solid(A.Border, 0.3), BorderThickness = new Thickness(0, 0, 0, 1),
        };
        container.Children.Add(wide ? WideRow(session, state) : StackedRow(session, state));
        if (state.Details || state.Ffmpeg) container.Children.Add(ExpandedPanel(session, state));
        return container;
    }

    private static string Username(AdminActivitySession s) =>
        string.IsNullOrEmpty(s.Username) ? AdminText.T("web.admin.activity.userFallback", new { id = s.UserId }) : s.Username;

    private static Border Avatar(string username, double size, double fontSize) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = A.Solid(A.Primary),
        Child = new TextBlock
        {
            Text = username.Length > 0 ? username[..1].ToUpper(System.Globalization.CultureInfo.CurrentCulture) : "?",
            FontSize = fontSize, FontWeight = FontWeights.Bold, Foreground = A.Brush("AccentForegroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static Border? ProfileChip(AdminActivitySession s)
    {
        var profile = string.IsNullOrEmpty(s.ProfileName) ? s.ProfileId : s.ProfileName;
        if (string.IsNullOrEmpty(profile)) return null;
        return A.Badge(profile, A.Primary, fontSize: 10, backgroundAlpha: 0.15, borderAlpha: 0.30);
    }

    private static Border? JellyfinPill(AdminActivitySession s)
    {
        if (!P.IsJellyfinSession(s)) return null;
        var pill = A.Badge(AdminText.T("web.player.jellyfinSession.label"), A.Jellyfin, backgroundAlpha: 0.15, borderAlpha: 0.30);
        ToolTipService.SetToolTip(pill, AdminText.T("web.player.jellyfinSession.title"));
        return pill;
    }

    private static string ClientTitle(AdminActivitySession s)
    {
        var full = P.GetSessionClientLabelFull(s);
        var label = string.IsNullOrEmpty(full) ? P.GetSessionClientLabel(s) : full;
        var agent = s.ClientUserAgent?.Trim();
        return string.IsNullOrEmpty(agent) ? label : $"{label} — {agent}";
    }

    private Button IpButton(string ip)
    {
        var button = A.Plain(A.Text(ip, 10, A.Muted), ip);
        button.Click += (_, _) => RunIpLookup(ip);
        return button;
    }

    private static string StreamMeta(AdminActivitySession s) =>
        string.Join(" · ", new[] { s.SourceContainer?.Trim().ToUpperInvariant(), P.FormatSessionBitrate(s.StreamBitrateKbps) }.Where(v => !string.IsNullOrEmpty(v)));

    private UIElement StreamLink(AdminActivitySession s, UIElement content)
    {
        if (string.IsNullOrEmpty(s.ContentId)) return content;
        var link = A.Plain(content, P.GetPlaybackSessionTitle(s));
        link.HorizontalContentAlignment = HorizontalAlignment.Left;
        var contentId = s.ContentId;
        link.Click += (_, _) =>
        {
            if (App.Services.GetService(typeof(NavigationService)) is NavigationService nav) nav.Navigate<ItemDetailPage>(contentId);
        };
        return link;
    }

    private FrameworkElement WideRow(AdminActivitySession s, RowState state)
    {
        var grid = TableGrid();
        grid.Padding = new Thickness(12, 10, 12, 10);

        // User (the /admin/users destination is outside Monitor: plain text).
        var username = Username(s);
        var user = new Grid { ColumnSpacing = 8 };
        user.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        user.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        user.Children.Add(Avatar(username, 24, 9));
        var userText = new StackPanel { Spacing = 2 };
        userText.Children.Add(A.Text(username, 13, A.Foreground, FontWeights.Medium));
        if (ProfileChip(s) is { } chip) { chip.HorizontalAlignment = HorizontalAlignment.Left; userText.Children.Add(chip); }
        var clientLabel = P.GetSessionClientLabel(s);
        var ip = s.ClientIp?.Trim() ?? "";
        if (clientLabel.Length > 0 || ip.Length > 0 || P.IsJellyfinSession(s))
        {
            var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
            if (JellyfinPill(s) is { } pill) meta.Children.Add(pill);
            if (clientLabel.Length > 0)
            {
                var client = A.Text(clientLabel, 10, A.Muted);
                client.MaxWidth = 128;
                ToolTipService.SetToolTip(client, ClientTitle(s));
                meta.Children.Add(client);
            }
            if (clientLabel.Length > 0 && ip.Length > 0) meta.Children.Add(A.Text("·", 10, A.Muted));
            if (ip.Length > 0) meta.Children.Add(IpButton(ip));
            userText.Children.Add(meta);
        }
        Grid.SetColumn(userText, 1);
        user.Children.Add(userText);
        grid.Children.Add(user);

        // Stream.
        var stream = new StackPanel();
        stream.Children.Add(A.Text(P.GetPlaybackSessionTitle(s), 13, A.Foreground, FontWeights.Medium));
        if (P.GetPlaybackSessionSubtitle(s) is { } subtitle) stream.Children.Add(A.Text(subtitle, 10, A.Muted));
        if (StreamMeta(s) is { Length: > 0 } streamMeta) stream.Children.Add(A.Text(streamMeta, 10, A.Muted));
        var streamCell = (FrameworkElement)StreamLink(s, stream);
        Grid.SetColumn(streamCell, 1);
        grid.Children.Add(streamCell);

        // Playback.
        var playback = PlaybackCell(s, state);
        Grid.SetColumn(playback, 2);
        grid.Children.Add(playback);

        // Route.
        var route = RouteBadges(s);
        Grid.SetColumn(route, 3);
        grid.Children.Add(route);

        // Time.
        var time = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var stateChip = PlayingChip(s, state);
        stateChip.HorizontalAlignment = HorizontalAlignment.Right;
        stateChip.MinWidth = 76;
        time.Children.Add(stateChip);
        var position = A.Text(P.FormatPlaybackPosition(s), 12, A.Foreground);
        position.FontFamily = A.Mono;
        position.HorizontalAlignment = HorizontalAlignment.Right;
        position.Margin = new Thickness(0, 4, 0, 0);
        time.Children.Add(position);
        var elapsed = A.Text(AdminText.T("web.admin.activity.row.sessionActive", new { elapsed = P.GetElapsed(s.StartedAt, DateTimeOffset.UtcNow) }), 10, A.Muted);
        elapsed.HorizontalAlignment = HorizontalAlignment.Right;
        time.Children.Add(elapsed);
        Grid.SetColumn(time, 4);
        grid.Children.Add(time);

        // Actions.
        var actions = ActionsCell(s, state);
        Grid.SetColumn(actions, 5);
        grid.Children.Add(actions);
        return grid;
    }

    private FrameworkElement StackedRow(AdminActivitySession s, RowState state)
    {
        var username = Username(s);
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 12, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var avatar = Avatar(username, 28, 10);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(avatar);
        var body = new StackPanel { Spacing = 4 };

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nameRow.Children.Add(A.Text(username, 13, A.Foreground, FontWeights.SemiBold));
        if (ProfileChip(s) is { } chip) nameRow.Children.Add(chip);
        if (JellyfinPill(s) is { } pill) nameRow.Children.Add(pill);
        body.Children.Add(nameRow);

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        titleRow.Children.Add(StreamLink(s, A.Text(P.GetPlaybackSessionTitle(s), 11, A.Muted)));
        titleRow.Children.Add(A.Text("·", 11, A.Muted));
        var elapsedText = A.Text(P.GetElapsed(s.StartedAt, DateTimeOffset.UtcNow), 11, A.Muted);
        elapsedText.FontFamily = A.Mono;
        titleRow.Children.Add(elapsedText);
        body.Children.Add(titleRow);

        var clientLabel = P.GetSessionClientLabel(s);
        var ip = s.ClientIp?.Trim() ?? "";
        var meta = StreamMeta(s);
        if (clientLabel.Length > 0 || ip.Length > 0 || meta.Length > 0)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (clientLabel.Length > 0)
            {
                var client = A.Text(clientLabel, 10, A.Muted);
                client.MaxWidth = 144;
                ToolTipService.SetToolTip(client, ClientTitle(s));
                line.Children.Add(client);
            }
            if (clientLabel.Length > 0 && (ip.Length > 0 || meta.Length > 0)) line.Children.Add(A.Text("·", 10, A.Muted));
            if (ip.Length > 0) line.Children.Add(IpButton(ip));
            if (ip.Length > 0 && meta.Length > 0) line.Children.Add(A.Text("·", 10, A.Muted));
            if (meta.Length > 0) line.Children.Add(StreamLink(s, A.Text(meta, 10, A.Muted)));
            body.Children.Add(line);
        }

        var stateLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        stateLine.Children.Add(PlayingChip(s, state));
        var position = A.Text(P.FormatPlaybackPosition(s), 10, A.Muted);
        position.FontFamily = A.Mono;
        stateLine.Children.Add(position);
        body.Children.Add(stateLine);
        body.Children.Add(RouteBadges(s));
        body.Children.Add(new Border
        {
            Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1), BorderBrush = A.Solid(Microsoft.UI.Colors.White, 0.06), Background = A.Solid(Microsoft.UI.Colors.White, 0.03),
            Child = PlaybackCell(s, state),
        });
        var actions = ActionsCell(s, state);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(actions);
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return grid;
    }

    private Border PlayingChip(AdminActivitySession s, RowState state)
    {
        var paused = state.OptimisticPaused ?? s.IsPaused;
        var tone = paused ? A.Amber400 : A.Emerald400;
        var text = paused ? A.Amber300 : A.Emerald300;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(A.Icon(paused ? "pause" : "play", 10, text));
        content.Children.Add(new TextBlock { Text = AdminText.T(paused ? "web.admin.activity.row.paused" : "web.admin.activity.row.playing"), FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = A.Solid(text) });
        return new Border
        {
            CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = A.Solid(tone, 0.35), Background = A.Solid(tone, 0.10),
            Padding = new Thickness(6, 2, 6, 2), Child = content, HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    private FrameworkElement PlaybackCell(AdminActivitySession s, RowState state)
    {
        var cell = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var meta = P.MethodMeta(P.ClassifyActivityMethod(s));
        var badge = A.Badge(meta.Label, A.ToneColor(meta.Tone));
        ToolTipService.SetToolTip(badge, meta.Description);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge, AdminText.T("web.admin.activity.row.methodBadge", new { method = meta.Label }));
        top.Children.Add(badge);
        var expanded = state.Details || state.Ffmpeg;
        var detailsContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        detailsContent.Children.Add(A.Text(AdminText.T("web.admin.activity.row.details"), 10, A.Muted, FontWeights.Medium));
        detailsContent.Children.Add(A.Icon(expanded ? "chevron-up" : "chevron-down", 12, A.Muted));
        var details = A.Plain(detailsContent, AdminText.T("web.admin.activity.row.details"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(details, expanded ? "expanded" : "collapsed");
        details.Click += (_, _) =>
        {
            state.Details = !(state.Details || state.Ffmpeg);
            if (!state.Details) state.Ffmpeg = false;
            RenderTable();
        };
        top.Children.Add(details);
        cell.Children.Add(top);
        var lines = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        lines.Children.Add(SummaryLine("web.admin.activity.row.container", P.NormalizeContainerDecision(s.PlayMethod), P.FormatDeliveredContainerSummary(s)));
        lines.Children.Add(SummaryLine("web.admin.activity.row.video", P.VideoDecision(s), P.FormatDeliveredVideoSummary(s)));
        lines.Children.Add(SummaryLine("web.admin.activity.row.audio", P.AudioDecision(s), P.FormatDeliveredAudioSummary(s)));
        cell.Children.Add(lines);
        return cell;
    }

    private static UIElement SummaryLine(string labelKey, string decision, string value)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(A.Eyebrow(AdminText.T(labelKey), A.Muted, 40, 9));
        var badge = A.Badge(P.FormatDecisionLabel(decision), A.ToneColor(P.DecisionTone(decision)), fontSize: 8);
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        var text = A.Text(value, 11, A.Foreground, FontWeights.Medium);
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>PlaybackRouteBadges: stream location, access network, then serving nodes.</summary>
    private static FrameworkElement RouteBadges(AdminActivitySession s)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var wrap = new StackPanel { Spacing = 4 };
        if (!string.IsNullOrEmpty(s.StreamLocation))
        {
            var location = A.Badge(AdminText.T(s.StreamLocation == "local" ? "web.player.routeBadge.local" : "web.player.routeBadge.remote"), A.Primary, backgroundAlpha: 0.10, borderAlpha: 0.20);
            ToolTipService.SetToolTip(location, AdminText.T("web.player.routeBadge.locationTitle", new { location = s.StreamLocation }));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(location, AdminText.T("web.player.routeBadge.locationAria", new { location = s.StreamLocation }));
            panel.Children.Add(location);
        }
        var provider = s.RoutingNetworkProvider;
        var networkName = !string.IsNullOrEmpty(provider) ? provider
            : provider == "" ? AdminText.T("web.player.routeBadge.defaultNetwork") : AdminText.T("web.player.routeBadge.unknownNetwork");
        var networkTitle = !string.IsNullOrEmpty(provider) ? AdminText.T("web.player.routeBadge.networkTitle", new { name = provider })
            : provider == "" ? AdminText.T("web.player.routeBadge.defaultNetworkTitle") : AdminText.T("web.player.routeBadge.unknownNetworkTitle");
        var network = A.Badge($"{AdminText.T("web.player.routeBadge.network")} · {networkName}", A.Primary, backgroundAlpha: 0.10, borderAlpha: 0.20);
        ToolTipService.SetToolTip(network, networkTitle);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(network, AdminText.T("web.player.routeBadge.networkAria", new { name = networkName }));
        panel.Children.Add(network);
        wrap.Children.Add(panel);
        var nodes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var node in P.GetSessionRouteNodes(s))
        {
            var (tone, bg, border) = node.Kind switch
            {
                ActivityRouteKind.Transcode => (A.Warning, 0.10, 0.25),
                ActivityRouteKind.Proxy => (A.ToneColor(ActivityTone.Info), 0.10, 0.25),
                _ => (A.Primary, 0.05, 0.10),
            };
            var text = node.Kind is ActivityRouteKind.Transcode or ActivityRouteKind.Proxy
                ? $"{node.Label} · {node.Name}"
                : node.Kind == ActivityRouteKind.Server ? AdminText.T("web.player.routeBadge.apiServer") : node.Name;
            var serves = s.RoutingEgress == "api" ? "yes" : "no";
            var title = node.Kind != ActivityRouteKind.Server
                ? AdminText.T("web.player.routeBadge.nodeTitle", new { label = node.Label, name = node.Name })
                : !string.IsNullOrEmpty(s.ReportingNode)
                    ? AdminText.T("web.player.routeBadge.apiServerNodeTitle", new { serves, node = s.ReportingNode })
                    : AdminText.T("web.player.routeBadge.apiServerTitle", new { serves });
            var badge = A.Badge(text, tone, backgroundAlpha: bg, borderAlpha: border);
            ToolTipService.SetToolTip(badge, title);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge, title);
            nodes.Children.Add(badge);
        }
        wrap.Children.Add(nodes);
        return wrap;
    }

    // ── Actions ──────────────────────────────────────────────

    private FrameworkElement ActionsCell(AdminActivitySession s, RowState state)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var ffmpeg = A.Outline(AdminText.T(state.Ffmpeg ? "web.admin.activity.row.hideFfmpeg" : "web.admin.activity.row.ffmpeg"), "terminal", height: 28, fontSize: 11);
        ffmpeg.Padding = new Thickness(8, 0, 8, 0);
        ffmpeg.Click += (_, _) =>
        {
            state.Ffmpeg = !state.Ffmpeg;
            if (state.Ffmpeg) { state.Details = true; _ = LoadFfmpegAsync(s.SessionId, state, force: false); }
            RenderTable();
        };
        panel.Children.Add(ffmpeg);

        var busy = state.PendingAction != null;
        var terminate = A.Outline(AdminText.T("web.admin.sessions.playback.action.terminate"), "octagon-alert", A.Destructive, height: 28, fontSize: 11);
        terminate.Padding = new Thickness(8, 0, 8, 0);
        terminate.IsEnabled = !busy;
        terminate.Click += (_, _) => _ = RunActionAsync(s, state, "terminate");
        panel.Children.Add(terminate);

        var more = A.Plain(A.Icon("more-horizontal", 16, A.Foreground), AdminText.T("web.admin.sessions.playback.menu.ariaLabel"));
        more.Width = 28;
        more.Height = 28;
        more.HorizontalContentAlignment = HorizontalAlignment.Center;
        more.IsEnabled = !busy;
        more.Flyout = SessionMenu(s, state);
        panel.Children.Add(more);
        return panel;
    }

    /// <summary>The "⋯" menu: Session title (inline layout), primary pause/resume or the limited-control note, Stop, logs links, Message.</summary>
    private MenuFlyout SessionMenu(AdminActivitySession s, RowState state)
    {
        var menu = new MenuFlyout();
        menu.MenuFlyoutPresenterStyle = new Style(typeof(MenuFlyoutPresenter)) { Setters = { new Setter(MinWidthProperty, 224d) } };
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = AdminText.T("web.admin.sessions.playback.menu.titleSession"), IsEnabled = false, FontWeight = FontWeights.SemiBold,
        });
        var supportsControl = s.HasPlaybackControl;
        if (supportsControl)
        {
            var paused = state.OptimisticPaused ?? s.IsPaused;
            var primary = new MenuFlyoutItem { Text = AdminActivityCommandText.PrimaryLabel(paused), Icon = MenuIcon(paused ? "play" : "pause") };
            primary.Click += (_, _) => _ = RunActionAsync(s, state, paused ? "resume" : "pause");
            menu.Items.Add(primary);
        }
        else
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = AdminText.T("web.admin.sessions.playback.unsupportedControl"), IsEnabled = false, FontSize = 12,
            });
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        var stop = new MenuFlyoutItem { Text = AdminText.T("web.admin.sessions.playback.action.stop"), Icon = MenuIcon("square") };
        stop.Click += (_, _) => _ = RunActionAsync(s, state, "stop");
        menu.Items.Add(stop);
        menu.Items.Add(new MenuFlyoutSeparator());
        var logs = new MenuFlyoutItem { Text = AdminText.T("web.admin.activity.row.viewLogs"), Icon = MenuIcon("file-text") };
        logs.Click += (_, _) => AdminShellPage.Open(P.LogsRoute(s.SessionId, ffmpeg: false));
        menu.Items.Add(logs);
        var ffmpegLogs = new MenuFlyoutItem { Text = AdminText.T("web.admin.activity.row.ffmpegLogs"), Icon = MenuIcon("terminal") };
        ffmpegLogs.Click += (_, _) => AdminShellPage.Open(P.LogsRoute(s.SessionId, ffmpeg: true));
        menu.Items.Add(ffmpegLogs);
        if (supportsControl)
        {
            var message = new MenuFlyoutItem { Text = AdminText.T("web.admin.sessions.playback.action.message"), Icon = MenuIcon("message-square") };
            message.Click += (_, _) => _ = ShowMessageDialogAsync(s, state);
            menu.Items.Add(message);
        }
        return menu;
    }

    /// <summary>Segoe Fluent glyphs standing in for the Lucide menu icons.</summary>
    private static IconElement MenuIcon(string name) => new FontIcon
    {
        FontSize = 16,
        Glyph = name switch
        {
            "play" => "\uE768",
            "pause" => "\uE769",
            "square" => "\uE71A",
            "file-text" => "\uE8A5",
            "terminal" => "\uE756",
            "message-square" => "\uE8BD",
            _ => "\uE712",
        },
    };

    private async Task RunActionAsync(AdminActivitySession s, RowState state, string action)
    {
        // Authority is captured before anything else; a retired lease publishes nothing.
        if (_api == null || _lease is not { } lease || !lease.IsCurrent || state.PendingAction != null) return;
        state.PendingAction = action;
        RenderTable();
        AdminActivityCommandOutcome? outcome = null;
        string? failure = null;
        try
        {
            if (action == "terminate")
                outcome = AdminActivityCommandText.FromTerminate(await _api.TerminateActivitySessionAsync(lease, s.SessionId));
            else
            {
                var receipt = await _api.SendActivityCommandAsync(lease, s.SessionId, action);
                outcome = AdminActivityCommandText.FromReceipt(action, receipt);
            }
        }
        catch (Exception error)
        {
            failure = AdminActivityCommandText.DescribeFailure(action, error);
        }
        if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent) return;
        state.PendingAction = null;
        if (outcome is { OptimisticPaused: { } paused }) { state.OptimisticPaused = paused; state.OptimisticBasis = s.IsPaused; }
        if (outcome != null) _toasts?.Success(outcome.Message);
        else if (failure != null) _toasts?.Error(failure);
        RenderTable();
    }

    private async Task ShowMessageDialogAsync(AdminActivitySession s, RowState state)
    {
        if (XamlRoot == null || _api == null || _lease is not { } lease || !lease.IsCurrent || !s.HasPlaybackControl) return;
        var name = string.IsNullOrEmpty(s.Username)
            ? AdminText.T("web.admin.sessions.playback.messageDialog.userFallback", new { id = s.UserId })
            : s.Username;
        var box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96, Height = 110,
            PlaceholderText = AdminText.T("web.admin.sessions.playback.messageDialog.placeholder"),
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AdminText.T("web.admin.sessions.playback.messageDialog.title"),
            Content = new StackPanel
            {
                Spacing = 12, MaxWidth = 448,
                Children =
                {
                    A.Text(AdminText.T("web.admin.sessions.playback.messageDialog.description", new { name }), 14, A.Muted, trim: false),
                    box,
                },
            },
            PrimaryButtonText = AdminText.T("web.admin.sessions.playback.messageDialog.send"),
            CloseButtonText = AdminText.T("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (Application.Current.Resources.TryGetValue("WebUiContentDialogStyle", out var style) && style is Style dialogStyle)
            dialog.Style = dialogStyle;
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent ||
                !(_store?.Current.Sessions.Any(current => current.SessionId == s.SessionId && current.HasPlaybackControl) ?? false))
            {
                box.Text = "";
                return;
            }
            var text = box.Text.Trim();
            if (text.Length == 0)
            {
                args.Cancel = true;
                _toasts?.Error(AdminText.T("web.admin.sessions.playback.toast.messageRequired"));
                return;
            }
            var deferral = args.GetDeferral();
            sender.IsPrimaryButtonEnabled = false;
            state.PendingAction = "message";
            try
            {
                var receipt = await _api.SendActivityCommandAsync(lease, s.SessionId, "message", text);
                if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent) return;
                var outcome = AdminActivityCommandText.FromReceipt("message", receipt);
                _toasts?.Success(outcome.Message);
                // A replay keeps the dialog and text so the admin can send a new one deliberately.
                if (receipt.Outcome == "replayed") args.Cancel = true;
            }
            catch (Exception error)
            {
                args.Cancel = true;
                if (ReferenceEquals(lease, _lease) && lease.IsCurrent)
                    _toasts?.Error(AdminActivityCommandText.DescribeFailure("message", error));
            }
            finally
            {
                state.PendingAction = null;
                sender.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        _messageDialog = dialog;
        _messageDraft = box;
        _messageSessionId = s.SessionId;
        try { await dialog.ShowAsync(); }
        catch (Exception ex) { LocalLog.AppendLine("admin_monitor.txt", $"message dialog failed: {ex.GetType().Name}"); }
        finally
        {
            box.Text = "";
            if (ReferenceEquals(_messageDialog, dialog))
            {
                _messageDialog = null;
                _messageDraft = null;
                _messageSessionId = null;
            }
        }
    }

    // ── Expanded panel and FFmpeg console ─────────────────────

    private async Task LoadFfmpegAsync(string sessionId, RowState state, bool force)
    {
        if (_api == null || _lease is not { } lease) return;
        if (!force && state.FfmpegReadAt != DateTimeOffset.MinValue && DateTimeOffset.UtcNow - state.FfmpegReadAt < FfmpegStaleAfter) return;
        var version = ++state.FfmpegVersion;
        state.FfmpegLoading = state.FfmpegReadAt == DateTimeOffset.MinValue;
        state.FfmpegFetching = true;
        try
        {
            var rows = await _api.ListActivityFfmpegRowsAsync(lease, sessionId);
            if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent || version != state.FfmpegVersion) return;
            state.FfmpegRows = rows;
            state.FfmpegReadAt = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent) { return; }
        catch (Exception ex) { LocalLog.AppendLine("admin_monitor.txt", $"ffmpeg rows failed: {ex.GetType().Name}"); }
        if (version != state.FfmpegVersion) return;
        state.FfmpegLoading = false;
        state.FfmpegFetching = false;
        if (_active) RenderTable();
    }

    /// <summary>Window re-activation refreshes open consoles older than 5 s, as the WebUI's focus refetch.</summary>
    internal void RefreshOpenConsoles()
    {
        foreach (var (sessionId, state) in _rowStates)
            if (state.Ffmpeg) _ = LoadFfmpegAsync(sessionId, state, force: false);
    }

    private static readonly Color TerminalBg = Color.FromArgb(255, 0x0B, 0x10, 0x18);
    private static readonly Color TerminalFg = Color.FromArgb(255, 0xD7, 0xE3, 0xF4);
    private static readonly Color TerminalMuted = Color.FromArgb(255, 0x7D, 0x8B, 0xA1);
    private static readonly Color TerminalBorder = Color.FromArgb(255, 0x23, 0x30, 0x42);

    private FrameworkElement ExpandedPanel(AdminActivitySession s, RowState state)
    {
        var panel = new StackPanel { Padding = new Thickness(16, 12, 16, 12), BorderBrush = A.Solid(A.Border, 0.5), BorderThickness = new Thickness(0, 1, 0, 0), Background = A.Brush("CardBackgroundBrush") };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        var titles = new StackPanel();
        var pillRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pillRow.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1), BorderBrush = A.Solid(TerminalBorder), Background = A.Solid(TerminalBg),
            Padding = new Thickness(8, 2, 8, 2), Child = A.Eyebrow(AdminText.T("web.admin.activity.panel.heading"), TerminalFg, 200),
        });
        pillRow.Children.Add(A.Text(AdminText.T(state.Ffmpeg ? "web.admin.activity.panel.streamDetailsWithConsole" : "web.admin.activity.panel.streamDetails"), 11, A.WithAlpha(A.Foreground, 0.85), FontWeights.Medium));
        titles.Children.Add(pillRow);
        var id = A.Text(s.SessionId, 10, A.Muted);
        id.FontFamily = A.Mono;
        id.Margin = new Thickness(0, 4, 0, 0);
        id.IsTextSelectionEnabled = true;
        titles.Children.Add(id);
        header.Children.Add(titles);
        if (state.Ffmpeg && state.FfmpegFetching && !state.FfmpegLoading)
        {
            var refreshing = A.Text(AdminText.T("web.admin.activity.panel.refreshing"), 10, A.Muted);
            refreshing.HorizontalAlignment = HorizontalAlignment.Right;
            header.Children.Add(refreshing);
        }
        panel.Children.Add(header);

        var container = P.NormalizeContainerDecision(s.PlayMethod);
        var video = P.VideoDecision(s);
        var audio = P.AudioDecision(s);
        var mode = P.FormatTranscodeModeSummary(s);
        var tone = P.FormatToneMapSummary(s)?.Detail;
        var cards = new List<UIElement>
        {
            DetailCard("web.admin.activity.row.container", container, P.FormatSourceContainerSummary(s), P.FormatDeliveredContainerSummary(s), P.FormatContainerDetail(s), null, null),
            DetailCard("web.admin.activity.row.video", video, P.FormatVideoSummary(s), P.FormatDeliveredVideoSummary(s), P.FormatVideoDetail(s),
                video == "transcode" ? mode : null, video == "transcode" ? tone : null),
            DetailCard("web.admin.activity.row.audio", audio, P.FormatAudioSummary(s), P.FormatDeliveredAudioSummary(s), P.FormatAudioDetail(s),
                audio == "transcode" && video != "transcode" ? mode : null, null),
            ClientCard(s),
        };
        // grid gap-2 sm:grid-cols-2 lg:grid-cols-3, by the content column width.
        var width = _tableWidth > 0 ? _tableWidth : 1200;
        var columns = width >= 1024 ? 3 : width >= 640 ? 2 : 1;
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < cards.Count; i++)
        {
            if (i % columns == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var element = (FrameworkElement)cards[i];
            Grid.SetRow(element, i / columns);
            Grid.SetColumn(element, i % columns);
            grid.Children.Add(element);
        }
        panel.Children.Add(grid);

        if (state.Ffmpeg) panel.Children.Add(Console(s, state));
        return panel;
    }

    private static Border CardShell(string label, UIElement? badge, IEnumerable<UIElement> lines)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(A.Eyebrow(label, TerminalMuted, 180));
        if (badge != null) head.Children.Add(badge);
        var body = new StackPanel { Spacing = 4 };
        foreach (var line in lines) body.Children.Add(line);
        return new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = A.Solid(TerminalBorder, 0.6),
            Background = A.Solid(TerminalBg, 0.6), Padding = new Thickness(12, 8, 12, 8),
            Child = new StackPanel { Children = { head, body } },
        };
    }

    private static UIElement DetailLine(string labelKey, string value, bool muted = false)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(A.Text(AdminText.T(labelKey), 10, TerminalMuted));
        var text = A.Text(value, 11, muted ? TerminalMuted : TerminalFg, FontWeights.Medium, trim: false);
        text.IsTextSelectionEnabled = true;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static UIElement DetailCard(string labelKey, string decision, string source, string delivered, string detail, string? mode, string? toneMapping)
    {
        var lines = new List<UIElement>
        {
            DetailLine("web.admin.activity.detail.source", source),
            DetailLine("web.admin.activity.detail.delivered", delivered),
        };
        if (mode != null) lines.Add(DetailLine("web.admin.activity.detail.mode", mode));
        if (toneMapping != null) lines.Add(DetailLine("web.admin.activity.detail.toneMapping", toneMapping));
        lines.Add(DetailLine("web.admin.activity.detail.detail", detail, muted: true));
        return CardShell(AdminText.T(labelKey), A.Badge(P.FormatDecisionLabel(decision), A.ToneColor(P.DecisionTone(decision))), lines);
    }

    private static UIElement ClientCard(AdminActivitySession s)
    {
        var label = P.GetSessionClientLabelFull(s);
        var lines = new List<UIElement> { DetailLine("web.admin.activity.client.app", string.IsNullOrEmpty(label) ? AdminText.T("web.admin.activity.client.unknown") : label) };
        if (s.ClientUserAgent?.Trim() is { Length: > 0 } agent) lines.Add(DetailLine("web.admin.activity.client.agent", agent, muted: true));
        return CardShell(AdminText.T("web.admin.activity.client.heading"), null, lines);
    }

    private UIElement Console(AdminActivitySession s, RowState state)
    {
        var host = new StackPanel { Spacing = 8 };
        var open = A.Plain(A.Text(AdminText.T("web.admin.activity.panel.openFfmpegLogs"), 11, TerminalFg, FontWeights.Medium), null);
        open.HorizontalAlignment = HorizontalAlignment.Right;
        open.Click += (_, _) => AdminShellPage.Open(P.LogsRoute(s.SessionId, ffmpeg: true));
        host.Children.Add(open);
        UIElement content;
        if (state.FfmpegLoading)
            content = ConsoleNote(AdminText.T("web.admin.activity.panel.loadingFfmpeg"));
        else if (state.FfmpegRows.Count == 0)
            content = ConsoleNote(AdminText.T("web.admin.activity.panel.noFfmpegRows"));
        else
        {
            var rows = new StackPanel();
            foreach (var row in state.FfmpegRows)
            {
                var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 10, 16, 10), BorderBrush = A.Solid(TerminalBorder, 0.3), BorderThickness = new Thickness(0, 0, 0, 1) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var left = new StackPanel { Spacing = 4 };
                var time = A.Text(FormatTimeOnly(row.Timestamp), 10, TerminalMuted);
                time.FontFamily = A.Mono;
                left.Children.Add(time);
                left.Children.Add(A.Eyebrow(P.FfmpegRowKind(row), A.WithAlpha(TerminalMuted, 0.6), 180));
                grid.Children.Add(left);
                var right = new StackPanel();
                var line = A.Text(P.FfmpegRowText(row), 11, TerminalFg, trim: false);
                line.FontFamily = A.Mono;
                line.IsTextSelectionEnabled = true;
                right.Children.Add(line);
                var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
                if (!string.IsNullOrEmpty(row.NodeId)) meta.Children.Add(A.Text(row.NodeId, 10, A.WithAlpha(TerminalMuted, 0.6)));
                foreach (var key in new[] { "target_resolution", "hw_accel" })
                    if (P.LogAttr(row, key) is { } value && value != "-") meta.Children.Add(A.Text(value, 10, A.WithAlpha(TerminalMuted, 0.6)));
                if (P.LogAttr(row, "restart_count") is { } restarts && restarts != "-")
                    meta.Children.Add(A.Text(AdminText.T("web.admin.activity.panel.restart", new { count = restarts }), 10, A.WithAlpha(TerminalMuted, 0.6)));
                right.Children.Add(meta);
                Grid.SetColumn(right, 1);
                grid.Children.Add(right);
                rows.Children.Add(grid);
            }
            content = new ScrollViewer { Content = rows, MaxHeight = 256, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        host.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = A.Solid(TerminalBorder), Background = A.Solid(TerminalBg),
            Child = content,
        });
        return host;
    }

    private static UIElement ConsoleNote(string text)
    {
        var note = A.Text(text, 11, TerminalMuted, trim: false);
        note.FontFamily = A.Mono;
        return new Border { Padding = new Thickness(16, 24, 16, 24), Child = note };
    }

    private static string FormatTimeOnly(string value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? DateTimeDisplay.FormatTime(parsed, seconds: true)
            : value;
}
