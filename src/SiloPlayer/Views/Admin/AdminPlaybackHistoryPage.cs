using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using U = SiloPlayer.Views.Admin.AdminPlaybackAuditUi;
using P = SiloPlayer.Core.Services.Admin.AdminPlaybackHistoryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Admin › Playback › History (official Silo e61187da <c>pages/AdminPlaybackHistory.tsx</c>):
/// finalized playback attempts across every account and profile. URL state
/// (<c>user_id</c>, <c>profile_id</c>, <c>completed</c>, <c>media_item_id</c>)
/// is replaced in place; the first bounded page is paged locally (25/50/100).
/// Read-only: no delete or terminate action exists here. Reads, polling and
/// pickers end on page departure, outer Admin exit or any authority change.
/// Interactive filter and pager controls stay mounted; only their state changes.
/// </summary>
public sealed partial class AdminPlaybackHistoryPage : Page, IAdminPageSuspension
{
    private readonly AdminPlaybackHistoryApi? _api;
    private readonly AdminMonitorAuthority? _authority;
    private readonly StackPanel _body = Stack(24);
    private readonly LogsWrapPanel _actions = new();
    private readonly Border _itemChip = new();
    private readonly TextBlock _itemChipCaption = Text("", 12, true);
    private readonly ComboBox _user;
    private readonly ComboBox _profile;
    private readonly ComboBox _completion;
    private readonly Button _reset;
    private readonly Button _refresh;
    private readonly TextBlock _refreshLabel = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pickerNote = U.Status("AdminHistoryPickerNote");
    private readonly TextBlock _visibleValue, _completedValue, _partialValue;
    private readonly TextBlock _status = U.Status("AdminHistoryStatus");
    private readonly StackPanel _content = Stack(12);
    private readonly List<(AdminPlaybackHistoryRow Row, TextBlock Label)> _relativeStarts = [];
    private readonly TextBlock _windowNote = U.Status("AdminHistoryWindowNote");
    private readonly Grid _pager = new() { ColumnSpacing = 16, Padding = new(8, 16, 8, 16) };
    private readonly TextBlock _range = U.Status("AdminHistoryRange", 14);
    private readonly ComboBox _pageSize;
    private readonly Button _previous;
    private readonly Button _next;
    private AdminPlaybackHistoryController? _controller;
    private AdminPlaybackHistoryFilter _filter = new();
    private AdminHistoryUserList? _renderedUsers;
    private AdminHistoryProfileList? _renderedProfiles;
    private string? _renderedUserSelection;
    private string? _renderedKind;
    private AdminPlaybackHistoryWindow? _renderedWindow;
    private int _renderedPage, _renderedSize;
    private string? _renderedError;
    private long _renderedRevision = -1;
    private bool _navigated, _active, _syncing;

    /// <summary>Test seam: the controller of the active page owner.</summary>
    public AdminPlaybackHistoryController? Controller => _controller;
    /// <summary>Test seam: the session ids of the rendered local page, in order.</summary>
    public IReadOnlyList<string> RenderedSessionIds { get; private set; } = [];
    /// <summary>Test seam: what the content area shows ("closed", "loading", "error", "empty", "table").</summary>
    public string RenderedContent { get; private set; } = "closed";

    public AdminPlaybackHistoryPage() : this(App.Services.GetService<AdminPlaybackHistoryApi>()
        ?? (App.Services.GetService<SiloApiClient>() is { } client ? new AdminPlaybackHistoryApi(client) : null), App.Services.GetService<AdminMonitorAuthority>()) { }

    public AdminPlaybackHistoryPage(AdminPlaybackHistoryApi? api, AdminMonitorAuthority? authority)
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminPlaybackHistoryPage");
        _api = api;
        _authority = authority;

        // Header actions (WebUI PageHeader actions, flex-wrap).
        var chip = Inline(8);
        _itemChipCaption.FontWeight = FontWeights.Medium;
        _itemChipCaption.Text = U.T("web.admin.history.itemFilterActive");
        chip.Children.Add(_itemChipCaption);
        _itemChip.Child = chip;
        _itemChip.CornerRadius = new(8);
        _itemChip.Padding = new(12, 6, 12, 6);
        _itemChip.BorderThickness = new(1);
        _itemChip.BorderBrush = Brush("BorderBrush");
        _itemChip.Background = Tint("MutedBrush", 0.4);
        _itemChip.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_itemChip, "AdminHistoryItemFilter");
        _actions.Children.Add(_itemChip);

        _user = U.Picker("AdminHistoryUser", U.T("admin.native.playbackAudit.userFilterLabel"), 220);
        _user.SelectionChanged += (_, _) => { if (!_syncing) Replace(_controller?.SelectUser(NullIfAll(U.Selected(_user)))); };
        _profile = U.Picker("AdminHistoryProfile", U.T("admin.native.playbackAudit.profileFilterLabel"), 220);
        _profile.SelectionChanged += (_, _) => { if (!_syncing) Replace(_controller?.SelectProfile(NullIfAll(U.Selected(_profile)))); };
        _completion = U.Picker("AdminHistoryCompletion", U.T("admin.native.playbackAudit.completionFilterLabel"), 180);
        U.SetOptions(_completion, [("all", U.T("web.admin.history.filter.allAttempts")), ("true", U.T("web.admin.history.filter.completed")),
            ("false", U.T("web.admin.history.filter.partial"))], "all");
        _completion.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            Replace(_controller?.SelectCompletion(U.Selected(_completion) switch
            {
                "true" => AdminPlaybackHistoryCompletion.Completed,
                "false" => AdminPlaybackHistoryCompletion.Partial,
                _ => AdminPlaybackHistoryCompletion.All,
            }));
        };
        _reset = Button(U.T("web.admin.history.filter.reset"), "AdminHistoryReset");
        _reset.Click += (_, _) => Replace(_controller?.Reset());
        _refresh = Button("", "AdminHistoryRefresh");
        var refreshContent = Inline(8);
        refreshContent.Children.Add(SiloPlayer.Controls.AdminMonitorUi.Icon("refresh-cw", 14, Brush("PrimaryTextBrush")));
        refreshContent.Children.Add(_refreshLabel);
        _refresh.Content = refreshContent;
        _refresh.MinWidth = 132;
        _refresh.Click += (_, _) => { if (_controller != null) _ = _controller.RefreshAsync(manual: true); };
        foreach (var control in new FrameworkElement[] { _user, _profile, _completion, _reset, _refresh }) _actions.Children.Add(control);
        _body.Children.Add(Header(U.T("web.admin.history.title"), U.T("web.admin.history.subtitle"), _actions));
        _pickerNote.Visibility = Visibility.Collapsed;
        _body.Children.Add(_pickerNote);

        // Summary cards: counts of the bounded visible window, never lifetime totals.
        var cards = new LogsMetricGrid(3, 1) { ColumnSpacing = 12, RowSpacing = 12 };
        (var visible, _visibleValue) = U.InfoCard(U.T("web.admin.history.stats.visibleRows"), "AdminHistoryStatVisible");
        (var completed, _completedValue) = U.InfoCard(U.T("web.admin.history.stats.completed"), "AdminHistoryStatCompleted");
        (var partial, _partialValue) = U.InfoCard(U.T("web.admin.history.stats.partial"), "AdminHistoryStatPartial");
        cards.Add(visible); cards.Add(completed); cards.Add(partial);
        _body.Children.Add(cards);

        // Recent Playback card.
        var panel = Stack(16);
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = U.Strong(U.T("web.admin.history.recent.heading"));
        title.FontWeight = FontWeights.Bold;
        U.SetHeading(title);
        heading.Children.Add(title);
        Grid.SetColumn(_status, 1);
        heading.Children.Add(_status);
        panel.Children.Add(heading);
        panel.Children.Add(_content);
        _windowNote.Visibility = Visibility.Collapsed;
        panel.Children.Add(_windowNote);

        _pager.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _pager.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var left = Inline(16);
        left.Children.Add(_range);
        _pageSize = U.Picker("AdminHistoryPageSize", U.T("admin.native.playbackAudit.pageSizeLabel"), 120);
        U.SetOptions(_pageSize, AdminPlaybackHistoryController.PageSizeOptions.Select(size =>
            (size.ToString(System.Globalization.CultureInfo.InvariantCulture), U.T("web.admin.history.pagination.pageSize", AdminPlaybackAuditText.Count(size)))), "25");
        _pageSize.Height = 32;
        _pageSize.SelectionChanged += (_, _) => { if (!_syncing && int.TryParse(U.Selected(_pageSize), out var size)) _controller?.SetPageSize(size); };
        left.Children.Add(_pageSize);
        _pager.Children.Add(left);
        var buttons = Inline(8);
        _previous = Button(U.T("web.admin.history.pagination.previous"), "AdminHistoryPrevious");
        _previous.Click += (_, _) => _controller?.PreviousPage();
        _next = Button(U.T("web.admin.history.pagination.next"), "AdminHistoryNext");
        _next.Click += (_, _) => _controller?.NextPage();
        buttons.Children.Add(_previous);
        buttons.Children.Add(_next);
        Grid.SetColumn(buttons, 1);
        _pager.Children.Add(buttons);
        _pager.Visibility = Visibility.Collapsed;
        panel.Children.Add(_pager);
        _body.Children.Add(U.Card(panel));

        _body.MaxWidth = AdminShellPage.ContentMaxWidth;
        _body.HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 0, 24) };
        Loaded += (_, _) => { if (_navigated) Activate(); };
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!IsLoaded) Deactivate(dispose: false); });
        RenderClosed();
    }

    private static string? NullIfAll(string value) => value is "" or "all" ? null : value;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _filter = e.Parameter is AdminRouteRequest request ? AdminPlaybackHistoryFilter.FromQuery(request.Get) : new();
        _navigated = true;
        Activate();
        // WebUI replaces a profile_id without a user (or "all" values) away; after the shell records this page.
        if (e.Parameter is AdminRouteRequest shown && !shown.Equals(RequestFor(_filter)))
            DispatcherQueue.TryEnqueue(() => { if (_active) Replace(_filter); });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _navigated = false;
        Deactivate(dispose: true);
    }

    /// <summary>Outer Admin departure: retire everything and never resume from it.</summary>
    public void SuspendAdminPage()
    {
        _navigated = false;
        Deactivate(dispose: true);
    }

    private void Activate()
    {
        if (_active || _api == null || _authority == null) return;
        _active = true;
        if (_controller == null)
        {
            _controller = new AdminPlaybackHistoryController(_api, _authority);
            _controller.Changed += QueueRender;
            _controller.FilterReplaced += filter => DispatcherQueue.TryEnqueue(() => { if (_active) Replace(filter); });
            _controller.AuthorityRetired += () => DispatcherQueue.TryEnqueue(() => { if (_active) _controller?.Reopen(); });
        }
        else _controller.Resume();
        if (App.MainWindowInstance is { } window) window.VisibilityChanged += OnWindowVisibilityChanged;
        _controller.Open(_filter);
        _controller.SetVisible(App.MainWindowInstance?.Visible ?? true);
        Render();
    }

    private void Deactivate(bool dispose)
    {
        if (App.MainWindowInstance is { } window) window.VisibilityChanged -= OnWindowVisibilityChanged;
        if (_controller is { } controller)
        {
            controller.SetVisible(false);
            if (dispose)
            {
                controller.Changed -= QueueRender;
                controller.Dispose();
                _controller = null;
            }
            else if (_active) controller.Suspend();
        }
        _active = false;
        RenderClosed();
    }

    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args) => _controller?.SetVisible(_active && args.Visible);

    private static AdminRouteRequest RequestFor(AdminPlaybackHistoryFilter filter)
    {
        var query = filter.ToQuery();
        return new AdminRouteRequest(AdminRoute.PlaybackHistory, query.Count > 0 ? query : null);
    }

    /// <summary>WebUI <c>setSearchParams(next, {replace: true})</c>.</summary>
    private void Replace(AdminPlaybackHistoryFilter? filter)
    {
        if (filter == null) return;
        _filter = filter;
        AdminShellPage.TryReplace(this, RequestFor(filter));
    }

    private void QueueRender()
    {
        var controller = _controller;
        DispatcherQueue?.TryEnqueue(() => { if (_active && ReferenceEquals(controller, _controller)) Render(); });
    }

    private void RenderClosed()
    {
        _relativeStarts.Clear();
        _renderedRevision = -1;
        _renderedKind = null;
        _renderedWindow = null;
        _renderedError = null;
        _renderedUsers = null;
        _renderedProfiles = null;
        _renderedUserSelection = null;
        _content.Children.Clear();
        RenderedSessionIds = [];
        RenderedContent = "closed";
        _pager.Visibility = Visibility.Collapsed;
        _windowNote.Visibility = Visibility.Collapsed;
        _pickerNote.Visibility = Visibility.Collapsed;
        _itemChip.Visibility = Visibility.Collapsed;
        _visibleValue.Text = _completedValue.Text = _partialValue.Text = "";
        _status.Text = "";
        _syncing = true;
        try
        {
            U.SetOptions(_user, [("all", U.T("web.admin.history.filter.allUsers"))], "all");
            U.SetOptions(_profile, [("all", U.T("web.admin.history.filter.chooseUserFirst"))], "all");
            _profile.IsEnabled = false;
        }
        finally { _syncing = false; }
        SetRefresh(pending: false, enabled: false);
        _reset.IsEnabled = _previous.IsEnabled = _next.IsEnabled = false;
    }

    private void Render()
    {
        if (_controller is not { } controller) return;
        var state = controller.State;
        if (!state.IsOpen) { RenderClosed(); return; }
        if (state.Revision == _renderedRevision) return;
        _renderedRevision = state.Revision;
        _filter = state.Filter;
        RenderFilters(state);
        var count = (long)state.Total;
        _visibleValue.Text = AdminText.FormatNumber(count);
        _completedValue.Text = AdminText.FormatNumber(state.CompletedCount);
        _partialValue.Text = AdminText.FormatNumber(state.PartialCount);
        _status.Text = U.T(state.Fetching ? "web.admin.activity.page.refreshing" : "web.admin.history.recent.autoRefreshing");
        SetRefresh(state.ManualRefreshPending, enabled: !state.ManualRefreshPending);
        _reset.IsEnabled = true;
        RenderContent(state);
    }

    private void SetRefresh(bool pending, bool enabled)
    {
        _refresh.IsEnabled = enabled;
        _refreshLabel.Text = U.T(pending ? "web.admin.activity.page.refreshing" : "web.admin.activity.page.refresh");
        AutomationProperties.SetName(_refresh, _refreshLabel.Text);
        AutomationProperties.SetItemStatus(_refresh, pending ? _refreshLabel.Text : "");
    }

    private void RenderFilters(AdminPlaybackHistoryState state)
    {
        _syncing = true;
        try
        {
            var filter = state.Filter;
            var userValue = filter.UserId ?? "all";
            if (!ReferenceEquals(_renderedUsers, state.Users) || _renderedUserSelection != userValue)
            {
                var options = new List<(string, string)> { ("all", U.T("web.admin.history.filter.allUsers")) };
                foreach (var user in state.Users?.Items ?? []) options.Add((user.Id, user.Username));
                // A chosen account outside the bounded list stays selectable by id.
                if (filter.UserId != null && options.All(option => option.Item1 != filter.UserId))
                    options.Add((filter.UserId, U.T("web.admin.activity.userFallback", ("id", filter.UserId))));
                U.SetOptions(_user, options, userValue);
                _renderedUsers = state.Users;
                _renderedUserSelection = userValue;
            }
            if (!ReferenceEquals(_renderedProfiles, state.Profiles) || U.Selected(_profile) != (filter.ProfileId ?? "all") || _profile.IsEnabled != filter.HasUser)
            {
                var options = new List<(string, string)> { ("all", U.T(filter.HasUser ? "web.admin.history.filter.allProfiles" : "web.admin.history.filter.chooseUserFirst")) };
                foreach (var profile in state.Profiles?.Items ?? []) options.Add((profile.Id, profile.Name.Length > 0 ? profile.Name : profile.Id));
                if (filter.ProfileId != null && options.All(option => option.Item1 != filter.ProfileId)) options.Add((filter.ProfileId, filter.ProfileId));
                U.SetOptions(_profile, options, filter.ProfileId ?? "all");
                _profile.IsEnabled = state.ProfilePickerEnabled;
                _renderedProfiles = state.Profiles;
            }
            U.Select(_completion, filter.CompletedValue);
            U.Select(_pageSize, state.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { _syncing = false; }

        var notes = new List<string>();
        if (state.Users is { Truncated: true } users) notes.Add(U.T("admin.native.playbackAudit.usersTruncated", AdminPlaybackAuditText.Count(users.Items.Count)));
        if (state.Profiles is { Truncated: true } profiles) notes.Add(U.T("admin.native.playbackAudit.profilesTruncated", AdminPlaybackAuditText.Count(profiles.Items.Count)));
        _pickerNote.Text = string.Join(" ", notes);
        _pickerNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (state.ActiveMediaItemLabel is { } label && state.Filter.RequestMediaItemId is { } itemId)
        {
            var chip = (StackPanel)_itemChip.Child;
            while (chip.Children.Count > 1) chip.Children.RemoveAt(1);
            var text = U.Strong(label, 12);
            text.FontWeight = FontWeights.SemiBold;
            chip.Children.Add(U.Link(text, label, "AdminHistoryItemFilterLink", () => OpenItem(itemId)));
            _itemChip.Visibility = Visibility.Visible;
        }
        else _itemChip.Visibility = Visibility.Collapsed;
    }

    private void RenderContent(AdminPlaybackHistoryState state)
    {
        string kind;
        if (state.Loading) kind = "loading";
        else if (state.Error != null) kind = "error";
        else if (state.Total == 0) kind = state.Window == null ? "loading" : "empty";
        else kind = "table";

        // Rebuild only when what is shown changes; every successful read is a new window instance.
        var same = kind == _renderedKind && (kind == "table"
            ? state.Window != null && _renderedWindow != null && state.Window.Rows.SequenceEqual(_renderedWindow.Rows)
                && state.PageIndex == _renderedPage && state.PageSize == _renderedSize
            : kind != "error" || state.Error == _renderedError);
        if (!same)
        {
            _renderedKind = kind;
            _renderedWindow = state.Window;
            _renderedPage = state.PageIndex;
            _renderedSize = state.PageSize;
            _renderedError = state.Error;
            _content.Children.Clear();
            _relativeStarts.Clear();
            switch (kind)
            {
                case "loading":
                    _content.Children.Add(U.Skeleton(40));
                    for (var index = 0; index < 5; index++) _content.Children.Add(U.Skeleton(48));
                    AutomationProperties.SetName(_content, U.T("admin.native.playbackAudit.loading"));
                    RenderedSessionIds = [];
                    break;
                case "error":
                    var error = Text(state.Error ?? U.T("web.admin.history.loadError"), 14);
                    error.Foreground = Brush("ErrorBrush");
                    error.TextAlignment = TextAlignment.Center;
                    error.HorizontalAlignment = HorizontalAlignment.Center;
                    error.Margin = new(0, 32, 0, 32);
                    AutomationProperties.SetAutomationId(error, "AdminHistoryError");
                    AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
                    _content.Children.Add(error);
                    RenderedSessionIds = [];
                    break;
                case "empty":
                    var empty = Stack(12);
                    empty.HorizontalAlignment = HorizontalAlignment.Center;
                    empty.Padding = new(0, 48, 0, 48);
                    var icon = SiloPlayer.Controls.AdminMonitorUi.Icon("history", 40, Tint("SecondaryTextBrush", 0.5));
                    icon.HorizontalAlignment = HorizontalAlignment.Center;
                    empty.Children.Add(icon);
                    var heading = U.Strong(U.T("web.admin.history.empty.title"));
                    heading.TextAlignment = TextAlignment.Center;
                    heading.HorizontalAlignment = HorizontalAlignment.Center;
                    empty.Children.Add(heading);
                    var body = Text(U.T("web.admin.history.empty.body"), 12, true);
                    body.MaxWidth = 384;
                    body.TextAlignment = TextAlignment.Center;
                    empty.Children.Add(body);
                    AutomationProperties.SetAutomationId(empty, "AdminHistoryEmpty");
                    _content.Children.Add(empty);
                    RenderedSessionIds = [];
                    break;
                default:
                    var rows = state.PageRows;
                    _content.Children.Add(Table(rows));
                    RenderedSessionIds = rows.Select(row => row.SessionId).ToArray();
                    break;
            }
        }
        // Keep relative times current without replacing unchanged rows or their focus.
        var now = DateTimeOffset.UtcNow;
        foreach (var (row, label) in _relativeStarts) label.Text = P.Started(row, now);
        RenderedContent = kind;
        var showPager = kind == "table";
        _pager.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;
        _range.Text = showPager ? U.T("web.admin.history.pagination.range", ("from", state.RangeFrom), ("to", state.RangeTo), ("total", state.Total)) : "";
        _previous.IsEnabled = showPager && state.CanPrevious;
        _next.IsEnabled = showPager && state.CanNext;
        var bounded = kind == "table" && state.Window is { ServerHasMore: true };
        _windowNote.Text = bounded ? U.T("admin.native.playbackAudit.windowBounded", AdminPlaybackAuditText.Count(state.Total)) : "";
        _windowNote.Visibility = bounded ? Visibility.Visible : Visibility.Collapsed;
    }

    private LogsNativeTable Table(IReadOnlyList<AdminPlaybackHistoryRow> rows)
    {
        var headers = new[] { "media", "user", "profile", "method", "watchTime", "status", "ended", "logs" }.Select(key => U.T("web.admin.history.column." + key)).ToArray();
        var table = new LogsNativeTable(headers, [192, 120, 140, 100, 120, 100, 170, 180]);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            // Media: series-led title, item link, meta line.
            var media = Stack(4);
            media.MinWidth = 192;
            media.MaxWidth = 288;
            var (title, detail) = P.RowTitle(row);
            var titleBlock = Stack(0);
            var titleText = U.Strong(title);
            titleBlock.Children.Add(titleText);
            if (detail.Length > 0) titleBlock.Children.Add(Text(detail, 14, true));
            if (row.MediaItemId.Length > 0)
            {
                var itemId = row.MediaItemId;
                media.Children.Add(U.Link(titleBlock, P.RowLabel(row), "AdminHistoryItem_" + row.SessionId, () => OpenItem(itemId)));
            }
            else media.Children.Add(titleBlock);
            media.Children.Add(Text(P.Meta(row), 12, true));

            // User: account link when that page exists natively, else the same text.
            var username = P.Username(row);
            var userRequest = AdminRouteRequest.UserDetail(row.UserId);
            UIElement user = AdminShellPage.CanOpen(userRequest)
                ? U.Link(U.Strong(username), username, "AdminHistoryUser_" + row.SessionId, () => AdminShellPage.Open(userRequest))
                : U.Strong(username);

            // Profile: filters this page to the row's account and profile (push).
            var profile = Stack(4);
            var profileLabel = P.ProfileLabel(row);
            var profileRequest = P.ProfileRequest(row);
            profile.Children.Add(U.Link(Text(profileLabel), profileLabel, "AdminHistoryProfile_" + row.SessionId, () => AdminShellPage.Open(profileRequest)));
            profile.Children.Add(Text(row.ProfileId, 12, true));

            var watch = Stack(4);
            watch.MinWidth = 100;
            var watchText = Text(P.WatchTimeText(row));
            if (P.WatchTime(row).Estimated) ToolTipService.SetToolTip(watchText, U.T("web.admin.history.row.estimatedTitle"));
            watch.Children.Add(watchText);
            watch.Children.Add(Text(U.T("web.admin.history.row.ofDuration", ("duration", P.FormatDuration(row.DurationSeconds))), 12, true));

            var ended = Stack(4);
            ended.Children.Add(Text(U.FormatDateTime(row.EndedAt)));
            var relativeStart = Text(P.Started(row, now), 12, true);
            ended.Children.Add(relativeStart);
            _relativeStarts.Add((row, relativeStart));

            var logs = Inline(12);
            logs.MinWidth = 192;
            logs.HorizontalAlignment = HorizontalAlignment.Right;
            var logsRequest = P.LogsRequest(row, ffmpeg: false);
            var ffmpegRequest = P.LogsRequest(row, ffmpeg: true);
            var viewLogs = U.T("web.admin.activity.row.viewLogs");
            var ffmpegLogs = U.T("web.admin.activity.row.ffmpegLogs");
            logs.Children.Add(U.Link(U.Strong(viewLogs), viewLogs, "AdminHistoryLogs_" + row.SessionId, () => AdminShellPage.Open(logsRequest)));
            logs.Children.Add(U.Link(U.Strong(ffmpegLogs), ffmpegLogs, "AdminHistoryFFmpeg_" + row.SessionId, () => AdminShellPage.Open(ffmpegRequest)));

            table.Add([media, user, profile, U.Badge(P.Method(row), U.BadgeKind.Secondary),
                watch, U.Badge(P.Status(row), row.Completed ? U.BadgeKind.Default : U.BadgeKind.Outline), ended, logs]);
        }
        table.AlignEnd(7);
        return table;
    }

    private static void OpenItem(string itemId)
    {
        if (App.Services.GetService<NavigationService>() is { } navigation) navigation.Navigate<ItemDetailPage>(itemId);
    }
}
