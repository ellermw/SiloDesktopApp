using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.System;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using A = SiloPlayer.Controls.AdminMonitorUi;
using AP = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;
using P = SiloPlayer.Core.Services.Admin.AdminPlaybackHistoryPresentation;
using U = SiloPlayer.Views.Admin.AdminPlaybackAuditUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPlaybackHistoryPage
{
    private const string DetailKey = "web.admin.history.detail.";
    private const string Dash = "—";
    private Control? _detailOpener;
    private readonly StackPanel _detailNavigation = Inline(4);
    private Button _detailPrevious = null!;
    private Button _detailNext = null!;
    private (string Id, AdminPlaybackHistoryRow? Row, bool Loading, bool Unavailable, string? Error, bool Previous, bool Next)? _renderedDetail;

    private void SelectSession(string id, Control? opener = null)
    {
        _detailOpener = opener;
        ChangeQuery("session", id);
    }

    private void CloseDetail(bool restoreFocus)
    {
        _detailOpener = null;
        _renderedDetail = null;
        _detailNavigation.Visibility = Visibility.Collapsed;
        _closingDetail = true;
        try { _detailSheet.Close(restoreFocus); }
        finally { _closingDetail = false; }
    }

    private void OnDetailKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is not (VirtualKey.Up or VirtualKey.Down) || _detailSheet.Visibility != Visibility.Visible) return;
        var target = args.OriginalSource as DependencyObject;
        if (_detailSheet.ContainsScrollTarget(target)) return;
        for (var element = target; element != null && element != _detailSheet; element = VisualTreeHelper.GetParent(element))
            if (element is TextBox or RichEditBox or PasswordBox or ComboBox) return;
        args.Handled = StepSession(args.Key == VirtualKey.Up ? -1 : 1);
    }

    private bool StepSession(int step)
    {
        if (_controller?.State.AdjacentSession(step) is not { } target) return false;
        SelectSession(target);
        return true;
    }

    private void RenderDetail(AdminPlaybackHistoryState state)
    {
        if (!IsLoaded) return;
        if (state.Filter.SessionId is not { } id)
        {
            CloseDetail(restoreFocus: true);
            return;
        }

        var hasPrevious = state.AdjacentSession(-1) != null;
        var hasNext = state.AdjacentSession(1) != null;
        var data = (id, state.Detail, state.DetailLoading, state.DetailUnavailable, state.DetailError, hasPrevious, hasNext);
        if (_renderedDetail == data && _detailSheet.Visibility == Visibility.Visible) return;
        _renderedDetail = data;
        _detailPrevious.IsEnabled = hasPrevious;
        _detailNext.IsEnabled = hasNext;
        _detailNavigation.Visibility = hasPrevious || hasNext ? Visibility.Visible : Visibility.Collapsed;
        var row = state.Detail;
        var (title, description) = row == null ? (U.T(DetailKey + "title"), "") : P.RowTitle(row);
        if (row != null && description.Length == 0) description = row.SessionId;
        var session = row == null ? null : P.AsActivitySession(row);
        var heading = session == null || row == null ? null : DetailHeading(row, session, hasPrevious, hasNext);
        var body = DetailBody(state, session);
        var footer = row == null ? null : DetailFooter(row);
        if (_detailSheet.Visibility == Visibility.Visible)
            _detailSheet.Update(title, description, body, footer: footer, headingSupplement: heading);
        else _detailSheet.Show(title, description, body, footer: footer, opener: _detailOpener, headingSupplement: heading);
        _detailOpener = null;
    }

    private FrameworkElement DetailHeading(AdminPlaybackHistoryRow row, AdminActivitySession session, bool previous, bool next)
    {
        var stack = Stack(6);
        var line = new LogsWrapPanel { Spacing = 6 };
        line.Children.Add(MethodBadge(row));
        if (AP.IsJellyfinSession(session)) line.Children.Add(JellyfinBadge());
        line.Children.Add(StatusBadge(row));
        if (AP.FormatTranscodeModeSummary(session) is { } mode) line.Children.Add(DetailBadge(mode));
        if (AP.FormatToneMapSummary(session) is { } toneMap)
        {
            var badge = DetailBadge(toneMap.Badge);
            ToolTipService.SetToolTip(badge, toneMap.Detail);
            line.Children.Add(badge);
        }
        stack.Children.Add(line);
        if (previous || next)
        {
            stack.Children.Add(Text(U.T(DetailKey + "keyboardHint"), 12, true));
        }
        return stack;
    }

    private FrameworkElement CreateDetailNavigation()
    {
        _detailPrevious = StepButton("previousSession", "chevron-up", -1);
        _detailNext = StepButton("nextSession", "chevron-down", 1);
        _detailNavigation.Children.Add(_detailPrevious);
        _detailNavigation.Children.Add(_detailNext);
        _detailNavigation.Visibility = Visibility.Collapsed;
        var host = new Grid();
        host.Children.Add(_detailNavigation);
        return host;
    }

    private Button StepButton(string key, string icon, int step)
    {
        var button = Button("", step < 0 ? "AdminHistoryDetailPrevious" : "AdminHistoryDetailNext");
        button.Content = A.Icon(icon, 14, A.Foreground);
        button.Width = button.Height = 28;
        button.MinHeight = button.MinWidth = 0;
        button.Padding = new(0);
        button.CornerRadius = new(6);
        AutomationProperties.SetName(button, U.T(DetailKey + key));
        ToolTipService.SetToolTip(button, U.T(DetailKey + "keyboardHint"));
        button.Click += (_, _) => StepSession(step);
        return button;
    }

    private FrameworkElement DetailBody(AdminPlaybackHistoryState state, AdminActivitySession? session)
    {
        var body = Stack(20);
        if (state.DetailLoading)
        {
            AutomationProperties.SetName(body, U.T(DetailKey + "loading"));
            foreach (var height in new[] { 96, 128, 112 }) body.Children.Add(U.Skeleton(height));
        }
        else if (state.DetailError is { } error)
        {
            var icon = A.Icon("circle-alert", 28, A.Destructive);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.Margin = new(0, 40, 0, 0);
            body.Children.Add(icon);
            var title = U.T(DetailKey + "loadFailed");
            var heading = U.Strong(title);
            heading.TextAlignment = TextAlignment.Center;
            body.Children.Add(heading);
            if (error != title)
            {
                var detail = Text(error, 12, true);
                detail.TextAlignment = TextAlignment.Center;
                body.Children.Add(detail);
            }
            var retry = Button(AdminInsightsText.T("web.admin.insights.retry"), "AdminHistoryDetailRetry");
            retry.HorizontalAlignment = HorizontalAlignment.Center;
            retry.Click += (_, _) => _controller?.RetryDetail();
            body.Children.Add(retry);
        }
        else if (state.Detail is not { } row || session == null)
        {
            body.Children.Add(DetailMessage(U.T(state.DetailUnavailable ? "admin.native.playbackAudit.detailUnavailable" : DetailKey + "notFound")));
        }
        else
        {
            if (!row.RouteFactsRecorded) body.Children.Add(AdminInsightsUi.Notice(U.T(DetailKey + "preUpgradeNotice")));
            body.Children.Add(Section("streams", StreamComparison(row, session)));
            var reasons = AdminActivityReasons.Get(session);
            if (reasons.Count > 0)
            {
                var list = Stack(8);
                foreach (var reason in reasons)
                {
                    var reasonText = Stack(2);
                    reasonText.Children.Add(U.Strong(reason.Label, 12));
                    reasonText.Children.Add(Text(reason.Description, 12, true));
                    list.Children.Add(DetailCard(reasonText, new(12, 8, 12, 8), shaded: true));
                }
                body.Children.Add(Section("reasons", list));
            }
            body.Children.Add(Section("timing", Timing(row)));
            body.Children.Add(Section("clientRoute", AccountDeviceRoute(row, session)));
        }
        return body;
    }

    private static FrameworkElement DetailMessage(string value)
    {
        var message = U.Status("AdminHistoryDetailMessage", 14);
        message.Text = value;
        message.TextAlignment = TextAlignment.Center;
        message.Margin = new(0, 40, 0, 32);
        return message;
    }

    private static FrameworkElement Section(string key, UIElement contents)
    {
        var section = Stack(8);
        var label = U.T(DetailKey + "section." + key);
        var heading = U.Strong(label.ToUpper(System.Globalization.CultureInfo.CurrentCulture), 12);
        AutomationProperties.SetName(heading, label);
        heading.Foreground = Brush("SecondaryTextBrush");
        U.SetHeading(heading);
        section.Children.Add(heading);
        section.Children.Add(contents);
        return section;
    }

    private static Border DetailCard(UIElement content, Thickness padding, bool shaded = false) => new()
    {
        Child = content, Padding = padding, CornerRadius = new(12),
        BorderBrush = Tint("BorderBrush", 0.7), BorderThickness = new(1),
        Background = shaded ? Tint("MutedBrush", 0.25) : null,
    };

    private static FrameworkElement StreamComparison(AdminPlaybackHistoryRow row, AdminActivitySession session)
    {
        var rows = Stack();
        Add(U.T(DetailKey + "column.stream"), Text(U.T(DetailKey + "column.source"), 12, true),
            Text(U.T(DetailKey + "column.delivered"), 12, true), header: true);
        var sourceVideo = new LogsWrapPanel { Spacing = 6 };
        sourceVideo.Children.Add(Text(AP.FormatVideoSummary(session), 12));
        if (!string.IsNullOrEmpty(row.SourceVideoRangeType))
            sourceVideo.Children.Add(A.Badge(row.SourceVideoRangeType, A.Foreground, fontSize: 10, backgroundAlpha: 0, borderAlpha: 0.3));
        Add(U.T(DetailKey + "row.video"), sourceVideo, Text(AP.FormatDeliveredVideoSummary(session), 12));
        AddText("audio", AP.FormatAudioSummary(session), AP.FormatDeliveredAudioSummary(session));
        AddText("container", AP.FormatSourceContainerSummary(session), AP.FormatDeliveredContainerSummary(session));
        var (source, delivered) = P.Bitrates(row);
        AddText("bitrate", source, delivered);
        var (sourceSubtitle, deliveredSubtitle) = P.Subtitles(row);
        var subtitle = row.Subtitle?.Delivery == "burn_in"
            ? (FrameworkElement)A.Badge(deliveredSubtitle, A.Warning, fontSize: 10, borderAlpha: 0.30)
            : Text(deliveredSubtitle, 12);
        subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        Add(U.T(DetailKey + "row.subtitles"), Text(sourceSubtitle, 12), subtitle);
        return DetailCard(rows, new(0));

        void AddText(string label, string original, string output) =>
            Add(U.T(DetailKey + "row." + label), Text(original, 12), Text(output, 12));

        void Add(string label, FrameworkElement original, FrameworkElement output, bool header = false)
        {
            var line = new Grid { ColumnSpacing = 12, Padding = new(12, 8, 12, 8) };
            line.ColumnDefinitions.Add(new() { Width = new(0.7, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            line.Children.Add(Text(label, 12, true));
            Grid.SetColumn(original, 1);
            Grid.SetColumn(output, 2);
            line.Children.Add(original);
            line.Children.Add(output);
            rows.Children.Add(new Border { Child = line, BorderBrush = Tint("BorderBrush", 0.5),
                BorderThickness = new(0, header ? 0 : 1, 0, 0), Background = header ? Tint("MutedBrush", 0.3) : null,
                CornerRadius = header ? new(11, 11, 0, 0) : new(0) });
        }
    }

    private static FrameworkElement Timing(AdminPlaybackHistoryRow row)
    {
        var watch = U.Strong(P.WatchTimeText(row), 12);
        if (P.WatchTime(row).Estimated) ToolTipService.SetToolTip(watch, U.T("web.admin.history.row.estimatedTitle"));
        var endPosition = P.FormatDuration(row.WatchedSeconds);
        if (row.DurationSeconds is > 0) endPosition += " " + U.T("web.admin.history.row.ofDuration", ("duration", P.FormatDuration(row.DurationSeconds)));
        return PairGrid(
            ("started", U.Strong(U.FormatDateTime(row.StartedAt), 12)),
            ("ended", U.Strong(U.FormatDateTime(row.EndedAt), 12)),
            ("watched", watch),
            ("endPosition", U.Strong(endPosition, 12)),
            ("paused", U.Strong(row.PausedSeconds is { } paused ? P.FormatDuration(paused) : Dash, 12)),
            ("endReason", U.Strong(P.EndReason(row.EndReason), 12)));
    }

    private static FrameworkElement AccountDeviceRoute(AdminPlaybackHistoryRow row, AdminActivitySession session)
    {
        var userRequest = AdminRouteRequest.UserDetail(row.UserId);
        var name = P.Username(row);
        var user = U.Strong(name, 12);
        var account = AdminShellPage.CanOpen(userRequest)
            ? (FrameworkElement)U.Link(user, name, "AdminHistoryDetailAccount", () => AdminShellPage.Open(userRequest))
            : user;
        var client = string.Join(" ", new[] { row.ClientName, row.ClientVersion }.Where(value => !string.IsNullOrEmpty(value)));
        var network = row.StreamLocation switch
        {
            "remote" => U.T("web.admin.history.filter.locationRemote"),
            "local" => U.T("web.admin.history.filter.locationLocal"),
            _ => Dash,
        };
        return PairGrid(
            ("account", account), ("profile", U.Strong(P.ProfileLabel(row), 12)),
            ("library", U.Strong(row.LibraryName ?? Dash, 12)), ("client", U.Strong(client.Length > 0 ? client : Dash, 12)),
            ("platform", U.Strong(row.DevicePlatform ?? Dash, 12)), ("network", U.Strong(network, 12)),
            ("clientIp", Text(row.ClientIp ?? Dash, 11, mono: true)), ("route", RouteBadges(P.RouteNodes(row, session), session)),
            ("sessionId", Text(row.SessionId, 11, mono: true)));
    }

    private static FrameworkElement PairGrid(params (string Key, FrameworkElement Value)[] facts)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < facts.Length; index++)
        {
            var (key, value) = facts[index];
            if (index % 2 == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var pair = Stack(2);
            pair.Children.Add(Text(U.T(DetailKey + "field." + key), 12, true));
            pair.Children.Add(value);
            Grid.SetRow(pair, index / 2);
            Grid.SetColumn(pair, index % 2);
            if (key == "sessionId") Grid.SetColumnSpan(pair, 2);
            grid.Children.Add(pair);
        }
        return DetailCard(grid, new(12));
    }

    private static FrameworkElement RouteBadges(IReadOnlyList<ActivityRouteNode> nodes, AdminActivitySession session)
    {
        if (nodes.Count == 0) return U.Strong(Dash, 12);
        var badges = new LogsWrapPanel { Spacing = 4 };
        foreach (var node in nodes)
        {
            var (label, tone, foreground, background, border) = node.Kind switch
            {
                ActivityRouteKind.Transcode => ($"{node.Label} · {node.Name}", A.Warning, A.LabelColor(ActivityTone.Warning), 0.10, 0.25),
                ActivityRouteKind.Proxy => ($"{node.Label} · {node.Name}", A.ToneColor(ActivityTone.Info), A.LabelColor(ActivityTone.Info), 0.10, 0.25),
                ActivityRouteKind.Server => (AdminText.T("web.player.routeBadge.apiServer"), A.Primary, A.Primary, 0.05, 0.10),
                _ => (node.Name, A.Primary, A.Primary, 0.05, 0.10),
            };
            var badge = A.Badge(label, tone, fontSize: 10, backgroundAlpha: background, borderAlpha: border, foreground: foreground);
            var serves = session.RoutingEgress == "api" ? "yes" : "no";
            var tooltip = node.Kind != ActivityRouteKind.Server
                ? AdminText.T("web.player.routeBadge.nodeTitle", new { label = node.Label, name = node.Name })
                : !string.IsNullOrEmpty(session.ReportingNode)
                    ? AdminText.T("web.player.routeBadge.apiServerNodeTitle", new { serves, node = session.ReportingNode })
                    : AdminText.T("web.player.routeBadge.apiServerTitle", new { serves });
            ToolTipService.SetToolTip(badge, tooltip);
            AutomationProperties.SetName(badge, tooltip);
            badges.Children.Add(badge);
        }
        return badges;
    }

    private static FrameworkElement DetailFooter(AdminPlaybackHistoryRow row)
    {
        var actions = new LogsWrapPanel { Spacing = 8, Margin = new(16, 10, 16, 12) };
        var logs = Inline(8);
        logs.Children.Add(DetailAction("sessionLogs", "scroll-text", () => AdminShellPage.Open(P.LogsRequest(row, ffmpeg: false))));
        logs.Children.Add(DetailAction("ffmpegLogs", "terminal", () => AdminShellPage.Open(P.LogsRequest(row, ffmpeg: true))));
        actions.Children.Add(logs);
        if (row.MediaItemId.Length > 0)
        {
            var open = DetailAction("openMedia", "external-link", () => OpenItem(row.MediaItemId), ghost: true);
            actions.Children.Add(open);
            actions.TrailingActions = open;
        }
        return new Border { Child = actions, BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 1, 0, 0) };
    }

    private static Button DetailAction(string key, string icon, Action open, bool ghost = false)
    {
        var label = U.T(DetailKey + "action." + key);
        var button = Button(label, "AdminHistoryDetail_" + key, ghost ? "GhostButtonStyle" : "OutlineButtonStyle");
        var content = Inline(8);
        content.Children.Add(icon == "external-link" ? Icon(icon, 14) : A.Icon(icon, 14, A.Foreground));
        content.Children.Add(Text(label, 13));
        button.Content = content;
        button.Padding = new(12, 4, 12, 4);
        button.MinHeight = 32;
        button.Click += (_, _) => open();
        return button;
    }

    private static Border MethodBadge(AdminPlaybackHistoryRow row)
    {
        var tone = AP.MethodMeta(P.MethodKey(row.PlayMethod)).Tone;
        var badge = A.Badge(P.Method(row), A.ToneColor(tone), 11, borderAlpha: 0.25, foreground: A.LabelColor(tone), radius: 6);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        return badge;
    }

    private static Border StatusBadge(AdminPlaybackHistoryRow row, double fontSize = 12)
    {
        var badge = U.Badge(P.Status(row), row.Completed ? U.BadgeKind.Default : U.BadgeKind.Outline);
        badge.CornerRadius = new(6);
        if (badge.Child is TextBlock label) label.FontSize = fontSize;
        return badge;
    }

    private static Border DetailBadge(string text)
    {
        var badge = U.Badge(text, U.BadgeKind.Secondary);
        badge.CornerRadius = new(6);
        return badge;
    }

    private static Border JellyfinBadge()
    {
        var badge = A.Badge(AdminText.T("web.player.jellyfinSession.label"), A.Jellyfin, 10, backgroundAlpha: 0.15, borderAlpha: 0.30);
        ToolTipService.SetToolTip(badge, AdminText.T("web.player.jellyfinSession.title"));
        return badge;
    }
}
