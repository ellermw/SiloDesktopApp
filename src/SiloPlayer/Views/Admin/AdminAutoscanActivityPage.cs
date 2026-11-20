using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using A = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityText;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAutoscanActivityPage : Page, IAdminPageSuspension
{
    private readonly AdminAutoscanActivityApi? _api;
    private readonly AdminMonitorAuthority? _authority;
    private readonly IAdminRealtimeChannel? _realtime;
    private readonly StackPanel _body = Stack(24), _live = Stack(16), _rows = Stack(8), _notices = Stack(8);
    private readonly TextBlock _historyTitle = Text("", 16), _description = Text("", 12, true), _summary = Text("", 12, true);
    private readonly TextBox _search;
    private readonly ComboBox _status, _size;
    private readonly Button _refresh, _scans, _polls, _previous, _next, _reset, _restart;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private AdminAutoscanActivityController? _controller;
    private bool _navigated, _active, _syncing, _compact;
    private int _queuePage;
    private object? _renderedItems;
    private AdminAutoscanHistory? _renderedHistory;
    private AdminAutoscanActivityLabels? _renderedLabels;
    public AdminAutoscanActivityController? Controller => _controller;
    public string RenderedContent { get; private set; } = "closed";
    public IReadOnlyList<string> RenderedIds { get; private set; } = [];

    public AdminAutoscanActivityPage() : this(App.Services.GetService<AdminAutoscanActivityApi>(),
        App.Services.GetService<AdminMonitorAuthority>(), App.Services.GetService<AdminAutoscanActivityRealtime>()?.Channel) { }

    public AdminAutoscanActivityPage(AdminAutoscanActivityApi? api, AdminMonitorAuthority? authority, IAdminRealtimeChannel? realtime)
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminAutoscanActivityPage");
        _api = api; _authority = authority; _realtime = realtime;
        _refresh = Action(A.T("refreshLabel"), "AutoscanActivityRefresh", () => _controller?.RefreshAsync(true));
        _refresh.Content = Icon("refresh-cw", 18); _refresh.Width = 36; _refresh.Padding = new(8);
        ToolTipService.SetToolTip(_refresh, A.T("refreshLabel"));
        _body.Children.Add(Header(A.T("page.title"), A.T("page.subtitle"), _refresh));
        _body.Children.Add(_notices); _body.Children.Add(_live);
        var history = Stack(12);
        history.Children.Add(_historyTitle); history.Children.Add(_description);
        var tabs = Inline();
        _scans = Action(A.T("history.scans"), "AutoscanActivityScans", () => _controller?.SelectAsync(AdminAutoscanHistory.Scans));
        _polls = Action(A.T("history.polls"), "AutoscanActivityPolls", () => _controller?.SelectAsync(AdminAutoscanHistory.Polls));
        tabs.Children.Add(_scans); tabs.Children.Add(_polls); history.Children.Add(tabs);
        var filters = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        filters.RowDefinitions.Add(new() { Height = GridLength.Auto }); filters.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _search = Input(A.T("history.searchScans"), "AutoscanActivitySearch"); _search.Width = double.NaN; _search.MaxLength = 1024;
        _search.TextChanged += (_, _) => { if (!_syncing) { _debounce.Stop(); _debounce.Start(); } };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilters(); };
        filters.Children.Add(_search);
        var clear = Action(A.T("history.clearSearch"), "AutoscanActivityClearSearch", () => { _search.Text = ""; ApplyFilters(); return Task.CompletedTask; });
        clear.Content = Icon("x", 16); clear.Width = 36; clear.Padding = new(8);
        clear.Style = (Style)Application.Current.Resources["GhostButtonStyle"];
        ToolTipService.SetToolTip(clear, A.T("history.clearSearch"));
        Grid.SetColumn(clear, 1); filters.Children.Add(clear);
        var selectors = Inline();
        _status = Select("AutoscanActivityStatus", [("", A.T("filter.allStatuses"))], 180);
        AutomationProperties.SetName(_status, A.T("column.status"));
        _status.SelectionChanged += (_, _) => { if (!_syncing) ApplyFilters(); };
        _size = Select("AutoscanActivityPageSize", AdminAutoscanActivityApi.PageSizes.Select(size => (size.ToString(), size.ToString())), 90);
        AutomationProperties.SetName(_size, A.T("web.ui.pagination.rows"));
        _size.SelectionChanged += (_, _) => { if (!_syncing) ApplyFilters(); };
        _reset = Action(A.T("history.reset"), "AutoscanActivityReset", () => _controller?.FilterAsync("", "", int.TryParse(Selected(_size), out var rows) ? rows : 25));
        selectors.Children.Add(_status); selectors.Children.Add(Text(A.T("web.ui.pagination.rows"), 12, true)); selectors.Children.Add(_size); selectors.Children.Add(_reset);
        Grid.SetRow(selectors, 1); Grid.SetColumnSpan(selectors, 2); filters.Children.Add(selectors);
        history.Children.Add(filters); history.Children.Add(_rows);
        var pagination = Stack(8); var pagingActions = Inline();
        _previous = Action(A.T("web.ui.pagination.previous"), "AutoscanActivityPrevious", () => _controller?.PreviousAsync());
        _next = Action(A.T("web.ui.pagination.next"), "AutoscanActivityNext", () => _controller?.NextAsync());
        _restart = Action(A.T("native.restart"), "AutoscanActivityRestart", () => _controller?.ReloadHistoryAsync());
        pagingActions.Children.Add(_previous); pagingActions.Children.Add(_next); pagingActions.Children.Add(_restart);
        pagination.Children.Add(_summary); pagination.Children.Add(pagingActions); history.Children.Add(pagination);
        _body.Children.Add(history);
        _body.MaxWidth = AdminShellPage.ContentMaxWidth;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 0, 24) };
        Loaded += (_, _) => { if (_navigated) Activate(); };
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
        SizeChanged += (_, _) => { var compact = ActualWidth < 1024; if (compact != _compact) { _compact = compact; _renderedItems = null; Render(); } };
        Closed();
    }

    protected override void OnNavigatedTo(NavigationEventArgs args) { base.OnNavigatedTo(args); _navigated = true; Activate(); }
    protected override void OnNavigatedFrom(NavigationEventArgs args) { _navigated = false; Deactivate(); base.OnNavigatedFrom(args); }
    public void SuspendAdminPage() { _navigated = false; Deactivate(); }

    private void Activate()
    {
        if (_active || !_navigated || _api == null || _authority == null) return;
        _active = true;
        _controller = new(_api, _authority, _realtime);
        _controller.Changed += QueueRender; _controller.AuthorityRetired += AuthorityRetired;
        if (App.MainWindowInstance is { } window) window.VisibilityChanged += WindowVisibilityChanged;
        _ = _controller.OpenAsync(); Render();
    }

    private void AuthorityRetired() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        Closed();
        if (_authority?.IsAvailable == true) _ = _controller?.OpenAsync();
    });

    private void Deactivate()
    {
        _active = false; _debounce.Stop();
        if (App.MainWindowInstance is { } window) window.VisibilityChanged -= WindowVisibilityChanged;
        if (_controller is { } controller)
        {
            controller.Changed -= QueueRender; controller.AuthorityRetired -= AuthorityRetired;
            controller.Dispose(); _controller = null;
        }
        _expanded.Clear(); _queuePage = 0; Closed();
    }

    private void WindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        if (_active) _ = _controller?.SetVisibleAsync(args.Visible);
    }

    private void RerenderLive() { if (_active && _controller?.State is { IsOpen: true } state) RenderLive(state); }

    private void QueueRender()
    {
        var controller = _controller;
        DispatcherQueue.TryEnqueue(() => { if (_active && ReferenceEquals(controller, _controller)) Render(); });
    }

    private void ApplyFilters()
    {
        _debounce.Stop();
        if (_syncing || !_active || !int.TryParse(Selected(_size), out var size)) return;
        _ = _controller?.FilterAsync(_search.Text, Selected(_status), size);
    }

    private void Render()
    {
        if (!_active || _controller?.State is not { IsOpen: true } state) { Closed(); return; }
        var scans = state.Selected == AdminAutoscanHistory.Scans;
        var filter = scans ? state.Scans.Filter : state.Polls.Filter;
        var fetching = scans ? state.Scans.Fetching : state.Polls.Fetching;
        var loaded = scans ? state.Scans.Loaded : state.Polls.Loaded;
        var failure = scans ? state.Scans.Failure : state.Polls.Failure;
        var page = scans ? state.Scans.PageIndex : state.Polls.PageIndex;
        var total = scans ? state.Scans.Total : state.Polls.Total;
        var count = scans ? state.Scans.Items.Count : state.Polls.Items.Count;
        var next = scans ? state.Scans.NextCursor : state.Polls.NextCursor;
        var capped = scans ? state.Scans.Capped : state.Polls.Capped;
        _syncing = true;
        try
        {
            if (_renderedHistory != state.Selected)
            {
                _status.Items.Clear(); _status.Items.Add(new ComboBoxItem { Content = A.T("filter.allStatuses"), Tag = "" });
                foreach (var status in scans ? new[] { "accepted", "running", "completed", "failed", "cancelled" } : ["running", "success", "unresolved", "error"])
                    _status.Items.Add(new ComboBoxItem { Content = scans ? A.ScanStatus(status, true) : A.T("pollStatus." + status), Tag = status });
                _queuePage = 0;
            }
            if (!_debounce.IsEnabled && _search.Text != filter.Query) _search.Text = filter.Query;
            SetSelected(_status, filter.Status); SetSelected(_size, filter.Limit.ToString());
        }
        finally { _syncing = false; }
        _scans.IsEnabled = !scans; _polls.IsEnabled = scans;
        _search.IsEnabled = _status.IsEnabled = _size.IsEnabled = true;
        _refresh.IsEnabled = !fetching; _reset.IsEnabled = filter.Query.Length > 0 || filter.Status.Length > 0;
        _previous.IsEnabled = !fetching && page > 0;
        _next.IsEnabled = !fetching && next != null && !capped;
        _restart.IsEnabled = !fetching;
        _historyTitle.Text = A.T(scans ? "history.scansTitle" : "history.pollsTitle");
        _description.Text = A.T(scans ? "history.scansDescription" : "history.pollsDescription");
        _search.PlaceholderText = A.T(scans ? "history.searchScans" : "history.searchPolls");
        AutomationProperties.SetName(_search, _search.PlaceholderText);
        _summary.Text = A.T(scans ? "web.admin.common.pagination.scans" : "web.admin.common.pagination.polls",
            new { from = count == 0 ? 0 : page * filter.Limit + 1, to = page * filter.Limit + count, total, count = total }) + " · " + A.T("native.page", new { page = page + 1 });
        _notices.Children.Clear();
        if (state.LabelsFailed || state.Labels.Truncated) _notices.Children.Add(Text(A.T("native.labelsPartial"), 12, true));
        if (state.Message != null) _notices.Children.Add(Text(state.Message, 13));
        foreach (var review in state.CancelReview)
        {
            var notice = Stack(8); notice.Children.Add(SafeText(A.Library(review.Key, state.Labels) + ": " + review.Value, 13));
            var reconcile = Action(A.T("native.reconcile"), "AutoscanReconcile_" + review.Key, () => _controller?.ReconcileCancelAsync(review.Key));
            reconcile.IsEnabled = state.Reconciling == null;
            notice.Children.Add(reconcile);
            _notices.Children.Add(Card(notice));
        }
        if (failure != null && count > 0)
        {
            // A background refresh failed: the rows already shown stay, the notice says so, and polling continues.
            var stale = Stack(8); stale.Children.Add(Text(A.T("history.loadFailed"), 13));
            stale.Children.Add(Action(A.T("common.tryAgain"), "AutoscanActivityStaleRetry", () => _controller?.RefreshAsync(true)));
            _notices.Children.Add(Card(stale));
        }
        RenderLive(state);
        var kind = failure != null && count == 0 ? "error" : !loaded ? "loading" : count == 0 ? "empty" : "table";
        object items = scans ? state.Scans.Items : state.Polls.Items;
        if (RenderedContent == kind && ReferenceEquals(items, _renderedItems) && _renderedHistory == state.Selected && ReferenceEquals(state.Labels, _renderedLabels)) return;
        _rows.Children.Clear(); RenderedContent = kind; _renderedItems = items; _renderedHistory = state.Selected; _renderedLabels = state.Labels;
        _expanded.IntersectWith(state.Polls.Items.Select(poll => poll.Id));
        RenderedIds = scans ? state.Scans.Items.Select(row => row.Id).ToArray() : state.Polls.Items.Select(row => row.Id).ToArray();
        if (kind == "loading") _rows.Children.Add(new ProgressRing { IsActive = true, Width = 28, Height = 28 });
        else if (kind == "error")
        {
            _rows.Children.Add(Text(A.T("history.loadFailed"), 14));
            if (failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden or AdminFailureKind.ProfileVerificationRequired) _rows.Children.Add(Text(A.T("native.readDenied"), 12, true));
            _rows.Children.Add(Action(A.T("common.tryAgain"), "AutoscanActivityRetry", () => _controller?.RefreshAsync(true)));
            _rows.Children.Add(Action(A.T("native.restart"), "AutoscanActivityErrorRestart", () => _controller?.ReloadHistoryAsync()));
        }
        else if (kind == "empty") _rows.Children.Add(Text(A.T(filter.Query.Length > 0 || filter.Status.Length > 0 ? "history.emptyFiltered" : scans ? "history.emptyScans" : "history.emptyPolls"), 14, true));
        else
        {
            if (!_compact) _rows.Children.Add(HistoryHead(scans));
            if (scans) foreach (var scan in state.Scans.Items) _rows.Children.Add(ScanRow(scan, state.Labels));
            else foreach (var poll in state.Polls.Items) _rows.Children.Add(PollRow(poll, state.Labels));
        }
        if (capped) _rows.Children.Add(Text(A.T("native.capped"), 12, true));
    }

    private static Button Action(string label, string id, Func<Task?> execute)
    {
        var button = Button(label, id);
        button.Click += async (_, _) => { if (execute() is { } task) await task; };
        return button;
    }

    private static Border Card(UIElement content) => new() { Child = content, Padding = new(16), CornerRadius = new(8),
        BorderBrush = Brush("BorderBrush"), BorderThickness = new(1), Background = Brush("SurfaceBrush") };
}
