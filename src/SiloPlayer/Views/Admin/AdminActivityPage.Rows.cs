using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
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
        public object? MessageOwner;
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

    private bool ShowRoute { get; set; }
    private Grid TableGrid()
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        foreach (var (min, star) in new[] { (208d, 1.5), (152d, 1.0), (224d, 1.6), (112d, 1.0) })
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(star, GridUnitType.Star), MinWidth = min });
        if (ShowRoute) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 112 });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
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
        for (var i = 0; i < filtered.Count; i++) rows.Children.Add(StreamRow(filtered[i], i == filtered.Count - 1, wide));
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
        content.Children.Add(A.Text(AdminText.T(key), 12, active ? A.Foreground : A.Muted, FontWeights.Medium));
        if (active) content.Children.Add(A.Text(_list.SortAscending ? "▲" : "▼", 9, A.Foreground));
        var button = A.Plain(content, AdminText.T(key));
        button.HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        button.Click += (_, _) => { _list.ToggleSort(field); RenderTable(); };
        return button;
    }

    private UIElement HeaderRow()
    {
        var grid = TableGrid();
        grid.Padding = new Thickness(12, 8, 12, 8);
        // e130f262d: the sticky column header is bg-card/95 with a border-b; surface/50 belongs to the stacked sort bar only.
        grid.Background = A.Brush("CardBackgroundBrush"); grid.BorderBrush = A.Solid(A.Border); grid.BorderThickness = new Thickness(0, 0, 0, 1);
        var cells = new List<UIElement> { new Border(),
            SortHeader(ActivitySortField.Media, "web.admin.activity.column.stream"),
            SortHeader(ActivitySortField.Username, "web.admin.activity.column.user"),
            SortHeader(ActivitySortField.Method, "web.admin.activity.column.playback"),
            SortHeader(ActivitySortField.Bitrate, "web.admin.activity.column.bitrate") };
        if (ShowRoute) cells.Add(SortHeader(ActivitySortField.Node, "web.admin.activity.column.route"));
        cells.Add(SortHeader(ActivitySortField.Started, "web.admin.activity.column.time", alignRight: true));
        cells.Add(new Border { Width = 84 });
        for (var i = 0; i < cells.Count; i++) { Grid.SetColumn((FrameworkElement)cells[i], i); grid.Children.Add(cells[i]); }
        return grid;
    }

    private UIElement SortRow()
    {
        var row = new LogsWrapPanel { Spacing = 16, RowSpacing = 6 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, AdminText.T("web.admin.activity.sort.group"));
        row.Children.Add(A.Text(AdminText.T("web.admin.activity.sort.heading"), 12, A.Muted));
        row.Children.Add(SortHeader(ActivitySortField.Username, "web.admin.activity.column.user"));
        row.Children.Add(SortHeader(ActivitySortField.Media, "web.admin.activity.column.stream"));
        row.Children.Add(SortHeader(ActivitySortField.Method, "web.admin.activity.column.playback"));
        row.Children.Add(SortHeader(ActivitySortField.Bitrate, "web.admin.activity.column.bitrate"));
        if (ShowRoute) row.Children.Add(SortHeader(ActivitySortField.Node, "web.admin.activity.column.route"));
        row.Children.Add(SortHeader(ActivitySortField.Started, "web.admin.activity.column.time"));
        return new Border { Child = row, Padding = new Thickness(16, 10, 16, 10), Background = A.Solid(A.Surface, 0.5), BorderBrush = A.Solid(A.Border), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    // ── Row ──────────────────────────────────────────────────

    private UIElement StreamRow(AdminActivitySession session, bool last, bool wide)
    {
        var state = StateOf(session.SessionId);
        // Optimism lasts only until a new observation of the same session arrives.
        if (state.OptimisticBasis != session.IsPaused) { state.OptimisticPaused = null; state.OptimisticBasis = session.IsPaused; }
        // e130f262d: rows are separated by border-border/40 border-b (none on the last); the official table has no striping.
        var container = new StackPanel
        {
            BorderBrush = A.Solid(A.Border, 0.4), BorderThickness = last ? new Thickness(0) : new Thickness(0, 0, 0, 1),
        };
        container.Children.Add(wide ? WideRow(session, state) : StackedRow(session, state));
        if (state.Details || state.Ffmpeg) container.Children.Add(ExpandedPanel(session, state));
        return container;
    }

    private static string Username(AdminActivitySession s) =>
        string.IsNullOrEmpty(s.Username) ? AdminText.T("web.admin.activity.userFallback", new { id = s.UserId }) : s.Username;


    private static Border? ProfileChip(AdminActivitySession s)
    {
        var profile = string.IsNullOrWhiteSpace(s.ProfileName) ? s.ProfileId?.Trim() : s.ProfileName.Trim();
        if (string.IsNullOrEmpty(profile)) return null;
        var chip = A.Badge(profile, A.Primary, fontSize: 10, backgroundAlpha: 0.10, borderAlpha: 0.30);
        chip.MaxWidth = 112; // max-w-[7rem] truncate
        ToolTipService.SetToolTip(chip, profile);
        return chip;
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

    /// <summary>The address as a ROW_LINK (hover:text-primary hover:underline, e130f262d #4626); it opens IP Lookup, not the row.</summary>
    private Button IpButton(string ip)
    {
        var button = A.LinkButton(A.Text(ip, 11, A.Muted), ip, A.Muted);
        ToolTipService.SetToolTip(button, AdminText.T("web.admin.activity.ipLookup.heading"));
        button.Click += (_, _) => RunIpLookup(ip);
        return button;
    }

    /// <summary>
    /// The viewer's name. The official row links it to /admin/users/{id} as a ROW_LINK (truncating, title = the name); here it is a link whenever the
    /// Users page is registered in the native shell and the same text otherwise.
    /// </summary>
    private static FrameworkElement UserLink(AdminActivitySession s, double size, Windows.UI.Text.FontWeight weight, Color rest)
    {
        var name = Username(s);
        var text = A.Text(name, size, rest, weight);
        ToolTipService.SetToolTip(text, name);
        var href = "/admin/users/" + Uri.EscapeDataString(s.UserId);
        if (!AdminShellPage.CanOpenHref(href)) return text;
        var link = A.LinkButton(text, name, rest);
        link.Click += (_, _) => AdminShellPage.TryOpenHref(href);
        return link;
    }


    /// <summary>
    /// The title (TitleLink): a w-fit, max-w-full, truncating ROW_LINK to the item when the row names one (<c>media_item_id</c>, e130f262d #4561), plain text otherwise.
    /// </summary>
    private UIElement Title(AdminActivitySession s)
    {
        var title = P.GetPlaybackSessionTitle(s);
        var text = A.Text(title, 13, A.Foreground, FontWeights.Medium);
        ToolTipService.SetToolTip(text, title);
        if (string.IsNullOrEmpty(s.MediaItemId)) return text;
        var link = A.LinkButton(text, title, A.Foreground);
        link.HorizontalAlignment = HorizontalAlignment.Left;
        var contentId = s.MediaItemId;
        link.Click += (_, _) =>
        {
            if (App.Services.GetService(typeof(NavigationService)) is NavigationService nav) nav.Navigate<ItemDetailPage>(contentId);
        };
        return link;
    }

    private Button ExpandButton(AdminActivitySession s, RowState state)
    {
        var expanded = state.Details || state.Ffmpeg;
        // data-row-toggle / data-row-chevron: the chevron is muted and lights to the foreground with its row (RowHover); the button itself hovers to surface-raised.
        var chevron = A.Solid(A.Muted);
        var button = A.Plain(A.Icon(expanded ? "chevron-down" : "chevron-right", 16, chevron), AdminText.T("web.admin.activity.row.details"));
        button.Tag = chevron;
        button.Width = button.Height = 28;
        button.CornerRadius = new CornerRadius(6);
        var raised = A.Solid(A.ColorOf("SurfaceRaisedColor", A.Surface));
        button.PointerEntered += (_, _) => { button.Background = raised; chevron.Color = A.Foreground; };
        button.PointerExited += (_, _) => button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var focusId = "AdminActivityExpand:" + s.SessionId;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, focusId);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(button, expanded ? "expanded" : "collapsed");
        button.Click += (_, _) => { ToggleDetails(state); RestoreRowFocus(focusId); };
        return button;
    }

    private void ToggleDetails(RowState state)
    {
        state.Details = !(state.Details || state.Ffmpeg);
        if (!state.Details) state.Ffmpeg = false;
        RenderTable();
    }

    private void RestoreRowFocus(string id)
    {
        var lease = _lease;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_active || !ReferenceEquals(lease, _lease) || lease?.IsCurrent != true || _messageDialog != null || _ipDialog != null) return;
            Button? Find(DependencyObject root)
            {
                if (root is Button button && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == id) return button;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                    if (Find(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
                return null;
            }
            Find(_tableHost)?.Focus(FocusState.Keyboard);
        });
    }

    /// <summary>
    /// ROW_HOVER_CLASS (e130f262d #4626): a clickable row highlights to surface-hover and lights its expand chevron, but holds both back while the pointer is on a
    /// link or a button that does something else; the expand controls (the chevron button and the "+N" reasons button) keep them.
    /// </summary>
    private static void RowHover(Panel row)
    {
        var rest = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var hover = A.Solid(A.ColorOf("SurfaceHoverColor", A.Surface));
        row.Background = rest;
        void Apply(PointerRoutedEventArgs? e, bool inside)
        {
            var held = false;
            if (inside && e?.OriginalSource is DependencyObject source)
                for (var target = source; target != null && !ReferenceEquals(target, row); target = VisualTreeHelper.GetParent(target))
                    if (target is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button && !IsRowToggle(button)) { held = true; break; }
            row.Background = inside && !held ? hover : rest;
            foreach (var toggle in Descendants(row).OfType<Button>().Where(b => b.Tag is SolidColorBrush))
                ((SolidColorBrush)toggle.Tag).Color = inside && !held ? A.Foreground : A.Muted;
        }
        row.PointerEntered += (_, e) => Apply(e, true);
        row.PointerMoved += (_, e) => Apply(e, true);
        row.PointerExited += (_, e) => Apply(e, false);
    }

    private static bool IsRowToggle(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) is { } id && (id.StartsWith("AdminActivityExpand:", StringComparison.Ordinal) || id.StartsWith("AdminActivityReasons:", StringComparison.Ordinal));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private void RowShortcut(FrameworkElement row, RowState state)
    {
        if (row is Panel panel) RowHover(panel);
        row.Tapped += (_, e) =>
        {
            for (var target = e.OriginalSource as DependencyObject; target != null && !ReferenceEquals(target, row); target = VisualTreeHelper.GetParent(target))
                if (target is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or TextBox or MenuFlyoutPresenter
                    || target is TextBlock { SelectedText.Length: > 0 }) return;
            ToggleDetails(state);
        };
    }

    private static FrameworkElement Poster(AdminActivitySession s, bool stacked)
    {
        var box = new Border { Width = stacked ? 40 : 32, Height = stacked ? 60 : 48, CornerRadius = new CornerRadius(2), Background = A.Brush("SurfaceBrush"), VerticalAlignment = VerticalAlignment.Top };
        var client = App.Services.GetService(typeof(SiloPlayer.Core.Api.SiloApiClient)) as SiloPlayer.Core.Api.SiloApiClient;
        if (Uri.TryCreate(client?.ResolveServerUrl(s.PosterUrl), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            box.Child = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri) { DecodePixelWidth = 80 }, Stretch = Stretch.UniformToFill };
        return box;
    }

    private FrameworkElement ProgressLine(AdminActivitySession s, RowState state)
    {
        var row = new LogsWrapPanel { Spacing = 8, RowSpacing = 2 };
        if (P.GetPlaybackProgress(s) is { } progress)
            row.Children.Add(RateBar(progress, (state.OptimisticPaused ?? s.IsPaused) ? A.Warning : A.Primary, 56));
        row.Children.Add(A.Text(P.FormatPlaybackPosition(s), 11, A.Muted));
        var paused = state.OptimisticPaused ?? s.IsPaused;
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        status.Children.Add(A.Icon(paused ? "pause" : "play", 12, paused ? A.Warning : A.Muted));
        if (paused) status.Children.Add(A.Text(AdminText.T("web.admin.activity.row.paused"), 11, A.Warning, FontWeights.Medium));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(status, AdminText.T(paused ? "web.admin.activity.row.paused" : "web.admin.activity.row.playing"));
        row.Children.Add(status);
        return row;
    }

    /// <summary>
    /// A h-1 rate bar. <paramref name="fill"/> is the bitrate cell's <c>max-w-32</c>: it takes the cell's width up to <paramref name="width"/>; otherwise it is exactly that wide
    /// (the progress line's w-14).
    /// </summary>
    private static FrameworkElement RateBar(double share, Color color, double width = 128, bool fill = false)
    {
        var bar = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Background = A.Solid(A.Border), CornerRadius = new CornerRadius(2) };
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(Math.Clamp(share, 0, 1), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = new GridLength(1 - Math.Clamp(share, 0, 1), GridUnitType.Star) });
        bar.Children.Add(new Border { Background = A.Solid(color), CornerRadius = new CornerRadius(2) });
        if (!fill) { bar.Width = width; bar.HorizontalAlignment = HorizontalAlignment.Left; return bar; }
        // A star column capped at the maximum, with a vanishing star column beside it to take the rest, keeps the bar left-aligned (a Stretch element under MaxWidth would centre).
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        host.ColumnDefinitions.Add(new() { Width = new GridLength(1000, GridUnitType.Star), MaxWidth = width });
        host.ColumnDefinitions.Add(new() { Width = new GridLength(0.001, GridUnitType.Star) });
        host.Children.Add(bar);
        return host;
    }

    private FrameworkElement WideRow(AdminActivitySession s, RowState state)
    {
        var grid = TableGrid();
        grid.Padding = new Thickness(12, 8, 12, 8);
        void Cell(FrameworkElement child, int column) { Grid.SetColumn(child, column); grid.Children.Add(child); }
        Cell(ExpandButton(s, state), 0);
        var media = new Grid { ColumnSpacing = 10 };
        media.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        media.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        media.Children.Add(Poster(s, false));
        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(Title(s));
        if (P.GetPlaybackSessionSubtitle(s) is { } subtitle) info.Children.Add(A.Text(subtitle, 12, A.Muted));
        info.Children.Add(ProgressLine(s, state));
        Grid.SetColumn(info, 1); media.Children.Add(info); Cell(media, 1);
        // ViewerCell (e130f262d): the name link (13px medium, truncating) beside the profile pill; then the JF pill and the compact client label (12px, muted);
        // then "local · 203.0.113.10" (11px) with the address opening IP Lookup. Each line is a single flex row: only the name and the client label truncate.
        var viewer = new StackPanel();
        var nameLine = new AdminShrinkPanel { Spacing = 6 };
        nameLine.AddShrinking(UserLink(s, 13, FontWeights.Medium, A.Foreground));
        if (ProfileChip(s) is { } profile) nameLine.Children.Add(profile);
        viewer.Children.Add(nameLine);
        var clientLine = new AdminShrinkPanel { Spacing = 6 };
        if (JellyfinPill(s) is { } jellyfin) clientLine.Children.Add(jellyfin);
        if (P.GetSessionClientLabel(s) is { Length: > 0 } clientLabel)
        {
            var client = A.Text(clientLabel, 12, A.Muted);
            ToolTipService.SetToolTip(client, ClientTitle(s)); clientLine.AddShrinking(client);
        }
        if (clientLine.Children.Count > 0) viewer.Children.Add(clientLine);
        var locationText = s.StreamLocation switch { "local" => AdminText.T("web.player.routeBadge.local"), "remote" => AdminText.T("web.player.routeBadge.remote"), _ => "" };
        var ipText = s.ClientIp?.Trim() ?? "";
        if (locationText.Length > 0 || ipText.Length > 0)
        {
            var location = new AdminShrinkPanel { Spacing = 6 };
            if (locationText.Length > 0) location.Children.Add(A.Text(locationText, 11, A.Muted));
            if (locationText.Length > 0 && ipText.Length > 0) location.Children.Add(A.Text("·", 11, A.Muted));
            if (ipText.Length > 0) location.Children.Add(IpButton(ipText));
            viewer.Children.Add(location);
        }
        Cell(viewer, 2);
        Cell(PlaybackCell(s, state), 3); Cell(BitrateCell(s), 4);
        var column = 5;
        if (ShowRoute) Cell(RouteBadges(s, showLocation: false), column++);
        var elapsed = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        elapsed.Children.Add(A.Icon("clock", 12, A.Muted));
        var time = A.Text(P.GetElapsed(s.StartedAt, DateTimeOffset.UtcNow), 12, A.Foreground);
        ToolTipService.SetToolTip(elapsed, AdminText.T("web.admin.activity.row.sessionActive", new { elapsed = time.Text }));
        elapsed.Children.Add(time); Cell(elapsed, column++); Cell(ActionsCell(s, state), column);
        RowShortcut(grid, state);
        return grid;
    }

    private FrameworkElement StackedRow(AdminActivitySession s, RowState state)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12) };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Poster(s, true));
        var body = new StackPanel { Spacing = 4 };
        var top = new Grid { ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var info = new StackPanel();
        info.Children.Add(Title(s));
        if (!string.IsNullOrEmpty(s.SeriesTitle) && P.GetPlaybackSessionSubtitle(s) is { } subtitle) info.Children.Add(A.Text(subtitle, 12, A.Muted));
        // One flex line (e130f262d): the name link and the client label truncate; the pills and the middle dot keep their width.
        var viewer = new AdminShrinkPanel { Spacing = 6 };
        viewer.AddShrinking(UserLink(s, 12, FontWeights.Normal, A.Muted));
        if (ProfileChip(s) is { } profile) viewer.Children.Add(profile);
        if (JellyfinPill(s) is { } jellyfin) viewer.Children.Add(jellyfin);
        if (P.GetSessionClientLabel(s) is { Length: > 0 } client)
        {
            viewer.Children.Add(A.Text("·", 12, A.Muted));
            var label = A.Text(client, 12, A.Muted); ToolTipService.SetToolTip(label, ClientTitle(s)); viewer.AddShrinking(label);
        }
        info.Children.Add(viewer); top.Children.Add(info);
        var actions = ActionsCell(s, state, compact: true); Grid.SetColumn(actions, 1); top.Children.Add(actions); body.Children.Add(top);
        var method = new Grid { ColumnSpacing = 4 };
        method.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        method.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        method.Children.Add(MethodLine(s, state));
        var expand = ExpandButton(s, state); Grid.SetColumn(expand, 1); method.Children.Add(expand); body.Children.Add(method);
        body.Children.Add(ProgressLine(s, state));
        Grid.SetColumn(body, 1); grid.Children.Add(body);
        RowShortcut(grid, state);
        return grid;
    }


    private FrameworkElement MethodLine(AdminActivitySession s, RowState state)
    {
        var reasons = AdminActivityReasons.Get(s);
        var top = new Grid { ColumnSpacing = 8 };
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var meta = P.MethodMeta(P.ClassifyActivityMethod(s));
        var badge = A.Badge(meta.Label, A.ToneColor(meta.Tone), fontSize: 10, foreground: A.LabelColor(meta.Tone));
        ToolTipService.SetToolTip(badge, meta.Description);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge, AdminText.T("web.admin.activity.row.methodBadge", new { method = meta.Label }));
        top.Children.Add(badge);
        if (reasons.Count > 0)
        {
            var reason = A.Text(reasons[0].Label, 12, A.Foreground);
            ToolTipService.SetToolTip(reason, reasons[0].Description); Grid.SetColumn(reason, 1); top.Children.Add(reason);
        }
        if (reasons.Count > 1)
        {
            var values = AdminText.Count(reasons.Count - 1);
            var more = A.Plain(A.Text(AdminText.T("web.admin.activity.row.moreReasons", values), 11, A.Muted),
                AdminText.T("web.admin.activity.row.moreReasonsLabel", values), new Thickness(4, 0, 4, 0));
            more.BorderThickness = new Thickness(1); more.BorderBrush = A.Solid(A.Border); more.CornerRadius = new CornerRadius(4);
            var focusId = "AdminActivityReasons:" + s.SessionId;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(more, focusId);
            ToolTipService.SetToolTip(more, string.Join(", ", reasons.Skip(1).Select(r => r.Label)));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(more, state.Details || state.Ffmpeg ? "expanded" : "collapsed");
            more.Click += (_, _) => { state.Details = true; RenderTable(); RestoreRowFocus(focusId); };
            Grid.SetColumn(more, 2); top.Children.Add(more);
        }
        return top;
    }

    private FrameworkElement PlaybackCell(AdminActivitySession s, RowState state)
    {
        var cell = new StackPanel { Spacing = 2 };
        cell.Children.Add(MethodLine(s, state));
        foreach (var video in new[] { true, false })
        {
            var conversion = video ? P.GetVideoConversion(s) : P.GetAudioConversion(s);
            var line = new Grid { ColumnSpacing = 6 };
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = A.Icon(video ? "film" : "audio-lines", 12, A.Muted);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(icon, AdminText.T(video ? "web.admin.activity.row.video" : "web.admin.activity.row.audio")); line.Children.Add(icon);
            var text = A.Text(conversion.Delivered == null ? conversion.Source : AdminText.T("web.admin.activity.detail.conversion", new { source = conversion.Source, output = conversion.Delivered }), 12, conversion.Delivered == null ? A.Muted : A.Foreground);
            ToolTipService.SetToolTip(text, text.Text); Grid.SetColumn(text, 1); line.Children.Add(text);
            if (video && conversion.Delivered != null && P.FormatTranscodeModeSummary(s) is { } mode)
            {
                var tag = A.Badge(mode, A.Muted, fontSize: 10, backgroundAlpha: 0, borderAlpha: 1); Grid.SetColumn(tag, 2); line.Children.Add(tag);
            }
            cell.Children.Add(line);
        }
        return cell;
    }

    private FrameworkElement BitrateCell(AdminActivitySession s)
    {
        var rate = P.GetSessionBitrate(s);
        var body = new StackPanel { Spacing = 4 };
        if (rate.Live != null) body.Children.Add(A.Text(rate.Live, 12, A.Foreground));
        var reference = rate.Limit != null && rate.Source != null
            ? AdminText.T("web.admin.activity.bitrate.limitAndSource", new { limit = rate.Limit, source = rate.Source })
            : rate.Limit != null ? AdminText.T("web.admin.activity.bitrate.limit", new { rate = rate.Limit })
            : rate.Source != null ? AdminText.T("web.admin.activity.bitrate.source", new { rate = rate.Source }) : "";
        if (reference.Length > 0) body.Children.Add(A.Text(reference, 11, A.Muted, trim: false));
        if (rate.Share is { } share) body.Children.Add(RateBar(share, rate.Limit == null ? A.Emerald400 : A.Warning, 128, fill: true));
        return body;
    }
    private static FrameworkElement RouteBadges(AdminActivitySession s, bool showLocation = true)
    {
        var panel = new LogsWrapPanel { Spacing = 4, RowSpacing = 4 };
        if (showLocation && !string.IsNullOrEmpty(s.StreamLocation))
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
        var nodes = panel;
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
        return panel;
    }

    // ── Actions ──────────────────────────────────────────────

    private FrameworkElement ActionsCell(AdminActivitySession s, RowState state, bool compact = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        if (!compact) panel.Children.Add(FfmpegButton(s, state, iconOnly: true));

        var busy = state.PendingAction != null;
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
        menu.Items.Add(new MenuFlyoutSeparator());
        var terminate = new MenuFlyoutItem
        {
            Text = AdminText.T("web.admin.sessions.playback.action.terminate"),
            Icon = MenuIcon("octagon-alert"),
            Foreground = A.Solid(A.Destructive),
        };
        terminate.Click += (_, _) => _ = RunActionAsync(s, state, "terminate");
        menu.Items.Add(terminate);
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
            "octagon-alert" => "\uE7BA",
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
        if (XamlRoot == null || _api == null || _lease is not { } lease || !lease.IsCurrent || state.PendingAction != null || !s.HasPlaybackControl) return;
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
        var dialogHeading = new Grid { ColumnSpacing = 16, Width = Math.Min(400, Math.Max(0, XamlRoot.Size.Width - 80)) };
        dialogHeading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        dialogHeading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        dialogHeading.Children.Add(A.Text(AdminText.T("web.admin.sessions.playback.messageDialog.title"), 18, A.Foreground, FontWeights.SemiBold));
        var closeMessage = A.Plain(A.Icon("x", 16, A.Muted), AdminText.T("web.ui.close"));
        closeMessage.Width = 24; closeMessage.Height = 24; closeMessage.Padding = new Thickness(0);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(closeMessage, "AdminMessageClose");
        closeMessage.Click += (_, _) => dialog.Hide();
        Grid.SetColumn(closeMessage, 1); dialogHeading.Children.Add(closeMessage); dialog.Title = dialogHeading;
        Button? CancelButton(DependencyObject parent)
        {
            if (parent is Button { Name: "CloseButton" } cancel) return cancel;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (CancelButton(VisualTreeHelper.GetChild(parent, index)) is { } found) return found;
            return null;
        }
        dialog.CloseButtonClick += (_, args) => args.Cancel = state.PendingAction == "message";
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent ||
                !(_store?.Current.Sessions.Any(current => current.SessionId == s.SessionId && current.HasPlaybackControl) ?? false))
            {
                box.Text = "";
                return;
            }
            if (state.PendingAction != null) { args.Cancel = true; return; }
            var text = box.Text.Trim();
            if (text.Length == 0)
            {
                args.Cancel = true;
                _toasts?.Error(AdminText.T("web.admin.sessions.playback.toast.messageRequired"));
                return;
            }
            // Keep the dialog interactive so its independent Close button remains available.
            // ContentDialog deferrals disable the entire content tree while pending.
            args.Cancel = true;
            sender.IsPrimaryButtonEnabled = false;
            if (CancelButton(sender) is { } cancel) cancel.IsEnabled = false;
            var messageOwner = new object();
            state.MessageOwner = messageOwner;
            state.PendingAction = "message";
            RenderTable();
            try
            {
                var receipt = await _api.SendActivityCommandAsync(lease, s.SessionId, "message", text);
                if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent) return;
                var outcome = AdminActivityCommandText.FromReceipt("message", receipt);
                _toasts?.Success(outcome.Message);
                // A replay keeps the dialog and text so the admin can send a new one deliberately.
                if (receipt.Outcome != "replayed" && ReferenceEquals(_messageDialog, sender)) sender.Hide();
            }
            catch (Exception error)
            {
                args.Cancel = true;
                if (ReferenceEquals(lease, _lease) && lease.IsCurrent)
                    _toasts?.Error(AdminActivityCommandText.DescribeFailure("message", error));
            }
            finally
            {
                if (ReferenceEquals(state.MessageOwner, messageOwner))
                {
                    state.MessageOwner = null;
                    state.PendingAction = null;
                    if (ReferenceEquals(lease, _lease) && lease.IsCurrent) RenderTable();
                }
                sender.IsPrimaryButtonEnabled = true;
                if (CancelButton(sender) is { } restoredCancel) restoredCancel.IsEnabled = true;
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

    private static readonly Color TerminalBg = Color.FromArgb(255, 0x09, 0x0B, 0x0F);
    private static readonly Color TerminalFg = Color.FromArgb(255, 0x72, 0xEA, 0xA2);
    private static readonly Color TerminalMuted = Color.FromArgb(255, 0x59, 0xA1, 0x74);
    private static readonly Color TerminalBorder = Color.FromArgb(255, 0x17, 0x35, 0x23);


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
