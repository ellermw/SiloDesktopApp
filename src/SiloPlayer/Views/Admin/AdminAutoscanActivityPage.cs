using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AutoscanUi;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using A = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityText;
using D = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityDisplay;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Manage › Autoscan › Activity, matching the official page at 88b825d57e4b2296050fda69eaf619eefbf208d1
/// (<c>ActivityPanel.tsx</c>, <c>PollActivityBadges.tsx</c>, <c>PollEventInspectorSheet.tsx</c>): live counts, running polls, the autoscan
/// queue, and the scan and poll histories with search, status filter, numbered pagination and the poll inspector sheet.
/// </summary>
public sealed partial class AdminAutoscanActivityPage : Page, IAdminGutterScrollPage, IAdminPageSuspension
{
    private readonly AdminAutoscanActivityApi? _api;
    private readonly AdminMonitorAuthority? _authority;
    private readonly IAdminRealtimeChannel? _realtime;
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _scrollContent = new();
    private readonly StackPanel _body = Stack(24), _live = Stack(24), _rows = Stack(16), _notices = Stack(8);
    private readonly LogsDetailSheet _sheet;
    private readonly Grid _filters = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly StackPanel _historyHeading = Stack(4);
    private readonly TextBlock _historyTitle = Text("", 14), _description = Text("", 12, true);
    private readonly Border _historyIcon = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search;
    private readonly ComboBox _status;
    private readonly Button _scans, _polls, _reset, _clear;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private AdminAutoscanActivityController? _controller;
    private bool _navigated, _active, _syncing, _compact, _phone, _renderQueued, _filtersWide;
    private int _queuePage;
    private object? _renderedItems;
    private (string Kind, AdminAutoscanHistory View, bool Compact, bool Phone, int Page, long Total, bool Fetching, int Size, string? Inspected)? _renderedSignature;
    private AdminAutoscanActivityLabels? _renderedLabels;
    private AdminAutoscanHistory? _renderedHistory;
    private AutoscanInspector? _inspector;
    public AdminAutoscanActivityController? Controller => _controller;
    public FrameworkElement ScrollContent => _scrollContent;
    public string RenderedContent { get; private set; } = "closed";
    public IReadOnlyList<string> RenderedIds { get; private set; } = [];
    /// <summary>The poll whose inspector sheet is open, for the fixture.</summary>
    public AutoscanInspector? Inspector => _inspector;
    public bool InspectorOpen => _sheet.Visibility == Visibility.Visible;

    public AdminAutoscanActivityPage() : this(App.Services.GetService<AdminAutoscanActivityApi>(),
        App.Services.GetService<AdminMonitorAuthority>(), App.Services.GetService<AdminAutoscanActivityRealtime>()?.Channel) { }

    public AdminAutoscanActivityPage(AdminAutoscanActivityApi? api, AdminMonitorAuthority? authority, IAdminRealtimeChannel? realtime)
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminAutoscanActivityPage");
        _api = api; _authority = authority; _realtime = realtime;
        _body.Children.Add(Header(A.T("page.title"), A.T("page.subtitle")));
        _body.Children.Add(_notices); _body.Children.Add(_live);

        var history = Stack(12);
        var heading = Inline(8); heading.Children.Add(_historyIcon); heading.Children.Add(_historyTitle); _historyTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _historyHeading.Children.Add(heading); _historyHeading.Children.Add(_description);
        _scans = Button(A.T("history.scans"), "AutoscanActivityScans"); _polls = Button(A.T("history.polls"), "AutoscanActivityPolls");
        foreach (var tab in new[] { _scans, _polls }) { tab.Height = 28; tab.MinHeight = 28; tab.Padding = new(12, 0, 12, 0); tab.CornerRadius = new(6); tab.FontSize = 13; }
        _scans.Click += (_, _) => SwitchView(AdminAutoscanHistory.Scans); _polls.Click += (_, _) => SwitchView(AdminAutoscanHistory.Polls);
        var toggle = new Border { Child = Inline(4), Padding = new(4), CornerRadius = new(6), BorderBrush = Brush("BorderBrush"), BorderThickness = new(1), Background = Brush("AppBackgroundBrush"), VerticalAlignment = VerticalAlignment.Center };
        ((StackPanel)toggle.Child).Children.Add(_scans); ((StackPanel)toggle.Child).Children.Add(_polls); toggle.Tag = "toggle";
        _search = Input(A.T("history.searchScans"), "AutoscanActivitySearch", double.NaN); _search.MaxLength = 1024;
        _search.TextChanged += (_, _) => { if (!_syncing) { _debounce.Stop(); _debounce.Start(); } UpdateClear(); };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilters(); };
        _clear = IconButton("x", A.T("history.clearSearch"), "AutoscanActivityClearSearch", () => { _search.Text = ""; ApplyFilters(); }, ghost: true, size: 32);
        _clear.Visibility = Visibility.Collapsed;
        _status = Select("AutoscanActivityStatus", [("", A.T("filter.allStatuses"))], 180);
        AutomationProperties.SetName(_status, A.T("column.status"));
        _status.SelectionChanged += (_, _) => { if (!_syncing) ApplyFilters(); };
        _reset = Button(A.T("history.reset"), "AutoscanActivityReset");
        _reset.Click += (_, _) => { CloseInspector(); _debounce.Stop(); _ = _controller?.ResetFiltersAsync(); };
        history.Children.Add(_historyHeading); history.Children.Add(_filters); history.Children.Add(_rows);
        _filtersToggle = toggle; _body.Children.Add(history);
        _searchRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _searchRow.Children.Add(_search); Grid.SetColumn(_clear, 1); _searchRow.Children.Add(_clear);

        _scrollContent.Children.Add(_body); _scroll.Content = _scrollContent; _root.Children.Add(_scroll);
        _sheet = new LogsDetailSheet(_scroll, 896);
        _sheet.Closed += OnSheetClosed;
        _root.Children.Add(_sheet); Content = _root;
        _scroll.SizeChanged += (_, _) => OnWidthChanged();
        Loaded += (_, _) => { if (_navigated) Activate(); };
        Unloaded += (_, _) => { if (!IsLoaded) Deactivate(); };
        LayoutFilters(true); Closed();
    }

    private readonly Border _filtersToggle;
    private readonly Grid _searchRow = new() { ColumnSpacing = 4 };

    private void OnWidthChanged()
    {
        var width = _scroll.ActualWidth - _scrollContent.Margin.Left - _scrollContent.Margin.Right;
        if (width > 0) _scrollContent.Width = Math.Min(_scrollContent.MaxWidth, width);
        var content = width > 0 ? Math.Min(width, _scrollContent.MaxWidth) : 1200;
        var compact = content < 1024; var phone = content < 640;
        if (compact == _compact && phone == _phone) return;
        _compact = compact; _phone = phone; LayoutFilters(!phone && content >= 900);
        _renderedSignature = null; Render();
    }

    /// <summary>The official filter row: toggle, search, status and Reset on one line when wide; stacked when narrow.</summary>
    private void LayoutFilters(bool wide)
    {
        if (_filtersWide == wide && _filters.Children.Count > 0) return;
        _filtersWide = wide;
        _filters.Children.Clear(); _filters.ColumnDefinitions.Clear(); _filters.RowDefinitions.Clear();
        var searchRow = _searchRow;
        Grid.SetRow(searchRow, 0); Grid.SetColumn(searchRow, 0); Grid.SetColumnSpan(searchRow, 1);
        Grid.SetRow(_status, 0); Grid.SetColumn(_status, 0); Grid.SetRow(_reset, 0); Grid.SetColumn(_reset, 0); Grid.SetColumnSpan(_filtersToggle, 1); Grid.SetColumn(_filtersToggle, 0);
        _status.Width = wide ? 176 : double.NaN; _status.HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        if (wide)
        {
            foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) _filters.ColumnDefinitions.Add(new() { Width = width });
            _filters.Children.Add(_filtersToggle); Grid.SetColumn(searchRow, 1); _filters.Children.Add(searchRow);
            Grid.SetColumn(_status, 2); _filters.Children.Add(_status); Grid.SetColumn(_reset, 3); _filters.Children.Add(_reset);
        }
        else
        {
            _filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            for (var row = 0; row < 3; row++) _filters.RowDefinitions.Add(new() { Height = GridLength.Auto });
            _filtersToggle.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumnSpan(_filtersToggle, 2); _filters.Children.Add(_filtersToggle);
            Grid.SetRow(searchRow, 1); Grid.SetColumnSpan(searchRow, 2); _filters.Children.Add(searchRow);
            Grid.SetRow(_status, 2); _filters.Children.Add(_status); Grid.SetRow(_reset, 2); Grid.SetColumn(_reset, 1); _filters.Children.Add(_reset);
        }
    }

    private void UpdateClear() => _clear.Visibility = _search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

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
        OnWidthChanged();
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
        _queuePage = 0; Closed();
    }

    private void WindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        if (_active) _ = _controller?.SetVisibleAsync(args.Visible);
    }

    private void RerenderLive() { if (_active && _controller?.State is { IsOpen: true } state) RenderLive(state); }

    private void QueueRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        var controller = _controller;
        if (!DispatcherQueue.TryEnqueue(() => { _renderQueued = false; if (_active && ReferenceEquals(controller, _controller)) Render(); })) _renderQueued = false;
    }

    private void SwitchView(AdminAutoscanHistory view)
    {
        if (_controller?.State.Selected == view) return;
        CloseInspector(); _debounce.Stop();
        _ = _controller?.SelectAsync(view);
    }

    private void ApplyFilters()
    {
        _debounce.Stop();
        if (_syncing || !_active || _controller?.State is not { IsOpen: true } state) return;
        // The status chosen here belongs to the shown view; the search and page size are shared by both.
        CloseInspector();
        _ = _controller.FilterAsync(_search.Text, Selected(_status), state.PageSize);
    }

    /// <summary>A newer read of the same poll replaces the inspector's data (the latest snapshot is kept even when the poll leaves the page).</summary>
    private void SyncInspector(AdminAutoscanActivityState state)
    {
        if (_inspector is not { } inspector || _sheet.Visibility != Visibility.Visible) return;
        var live = state.Polls.Items.FirstOrDefault(poll => poll.Id == inspector.Id);
        var snapshot = live ?? inspector.Event;
        if (live == null && ReferenceEquals(state.Labels, _renderedLabels) || ReferenceEquals(live, inspector.Event) && ReferenceEquals(state.Labels, _renderedLabels)) return;
        inspector.SetEvent(snapshot, state.Labels);
        _sheet.Update(A.Safe(PollName(snapshot, state.Labels)), InspectorMeta(snapshot), inspector.Root);
    }

    private static string PollName(AdminAutoscanActivityEvent poll, AdminAutoscanActivityLabels labels) => A.Source(poll.SourceId, poll.PluginId, poll.CapabilityId, labels, true);
    private static string InspectorMeta(AdminAutoscanActivityEvent poll) =>
        Timestamp(D.EventTime(poll)) + D.Separator + D.EventDuration(poll, DateTimeOffset.UtcNow);

    private void OpenInspector(AdminAutoscanActivityEvent poll, Control? trigger)
    {
        if (!_active || _controller?.State is not { IsOpen: true } state || _sheet.Visibility == Visibility.Visible) return;
        // The sheet returns focus to whatever had it; the official one returns it to the row's Details button.
        trigger?.Focus(FocusState.Programmatic);
        _inspector = new AutoscanInspector(poll, state.Labels);
        _sheet.Show(A.Safe(PollName(poll, state.Labels)), InspectorMeta(poll), _inspector.Root);
        _renderedSignature = null; Render();
    }

    private void CloseInspector() { if (_sheet.Visibility == Visibility.Visible) _sheet.Close(); else _inspector = null; }

    private void OnSheetClosed()
    {
        var id = _inspector?.Id; _inspector = null; _renderedSignature = null;
        Render(); _rows.UpdateLayout();
        if (id != null && FindControl(_rows, "AutoscanActivityDetails_" + id) is { IsEnabled: true } trigger) trigger.Focus(FocusState.Programmatic);
    }

    private static bool IsInside(DependencyObject? element, DependencyObject region)
    {
        for (; element != null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element)) if (ReferenceEquals(element, region)) return true;
        return false;
    }

    private string? CaptureFocus(Panel region)
    {
        if (XamlRoot == null || Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is not Control control || !IsInside(control, region)) return null;
        var id = AutomationProperties.GetAutomationId(control);
        return id.Length == 0 ? null : id;
    }

    private void RestoreFocus(Panel region, string? id)
    {
        if (id == null) return;
        DispatcherQueue.TryEnqueue(() => { if (_active && FindControl(region, id) is { IsEnabled: true } control) control.Focus(FocusState.Programmatic); });
    }

    private void Render()
    {
        if (!_active || _controller?.State is not { IsOpen: true } state) { Closed(); return; }
        var scans = state.Selected == AdminAutoscanHistory.Scans;
        var fetching = scans ? state.Scans.Fetching : state.Polls.Fetching;
        var loaded = scans ? state.Scans.Loaded : state.Polls.Loaded;
        var failure = scans ? state.Scans.Failure : state.Polls.Failure;
        var page = scans ? state.Scans.PageIndex : state.Polls.PageIndex;
        var total = scans ? state.Scans.Total : state.Polls.Total;
        var count = scans ? state.Scans.Items.Count : state.Polls.Items.Count;
        _syncing = true;
        try
        {
            if (_renderedHistory != state.Selected)
            {
                _status.Items.Clear(); _status.Items.Add(new ComboBoxItem { Content = A.T("filter.allStatuses"), Tag = "" });
                foreach (var status in scans ? new[] { "accepted", "running", "completed", "failed", "cancelled" } : ["running", "success", "unresolved", "error"])
                    _status.Items.Add(new ComboBoxItem { Content = scans ? A.ScanStatus(status, true) : A.T("pollStatus." + status), Tag = status });
                _historyIcon.Child = Glyph(scans ? "list-checks" : "history", 16, Brush("AccentBrush"));
            }
            if (!_debounce.IsEnabled && _search.Text != state.Query) _search.Text = state.Query;
            SetSelected(_status, state.SelectedStatus);
        }
        finally { _syncing = false; }
        UpdateClear();
        _scans.Style = (Style)Application.Current.Resources[scans ? "SecondaryButtonStyle" : "GhostButtonStyle"];
        _polls.Style = (Style)Application.Current.Resources[scans ? "GhostButtonStyle" : "SecondaryButtonStyle"];
        AutomationProperties.SetItemStatus(_scans, scans ? "selected" : ""); AutomationProperties.SetItemStatus(_polls, scans ? "" : "selected");
        _search.IsEnabled = _status.IsEnabled = true;
        _reset.IsEnabled = state.HasHistoryFilters;
        _historyTitle.Text = A.T(scans ? "history.scansTitle" : "history.pollsTitle");
        _description.Text = A.T(scans ? "history.scansDescription" : "history.pollsDescription");
        _search.PlaceholderText = A.T(scans ? "history.searchScans" : "history.searchPolls");
        AutomationProperties.SetName(_search, _search.PlaceholderText);
        RenderNotices(state, failure, count);
        RenderLive(state);
        var kind = failure != null && count == 0 ? "error" : !loaded ? "loading" : count == 0 ? "empty" : "table";
        object items = scans ? state.Scans.Items : state.Polls.Items;
        SyncInspector(state);
        var signature = (kind, state.Selected, _compact, _phone, page, total, fetching, state.PageSize, _inspector?.Id);
        if (_renderedSignature == signature && ReferenceEquals(items, _renderedItems) && ReferenceEquals(state.Labels, _renderedLabels) && _renderedHistory == state.Selected) return;
        var focus = CaptureFocus(_rows);
        _rows.Children.Clear(); RenderedContent = kind; _renderedItems = items; _renderedHistory = state.Selected; _renderedLabels = state.Labels; _renderedSignature = signature;
        RenderedIds = scans ? state.Scans.Items.Select(row => row.Id).ToArray() : state.Polls.Items.Select(row => row.Id).ToArray();
        if (kind == "loading") for (var placeholder = 0; placeholder < 6; placeholder++) _rows.Children.Add(Skeleton());
        else if (kind == "error")
        {
            var box = Stack(12);
            var message = Text(A.T("history.loadFailed"), 14); message.Foreground = Solid(Destructive); message.TextAlignment = TextAlignment.Center; box.Children.Add(message);
            if (failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden or AdminFailureKind.ProfileVerificationRequired)
            { var denied = Text(A.T("native.readDenied"), 12, true); denied.TextAlignment = TextAlignment.Center; box.Children.Add(denied); }
            var retry = Button(A.T("common.tryAgain"), "AutoscanActivityRetry"); retry.HorizontalAlignment = HorizontalAlignment.Center; retry.Click += (_, _) => _ = _controller?.ReloadHistoryAsync();
            box.Children.Add(retry);
            var frame = Dashed(box, 32); frame.BorderBrush = Solid(Destructive, 0.30); frame.Background = Solid(Destructive, 0.05); _rows.Children.Add(frame);
        }
        else if (kind == "empty")
        {
            var message = Text(A.T(state.HasHistoryFilters ? "history.emptyFiltered" : scans ? "history.emptyScans" : "history.emptyPolls"), 14, true); message.TextAlignment = TextAlignment.Center;
            _rows.Children.Add(Dashed(message, 32));
        }
        else
        {
            if (scans) AddScanRows(state);
            else AddPollRows(state);
            var pager = Pagination("AutoscanActivityPager", total, page, state.PageSize, target => { CloseInspector(); _ = _controller?.GoToPageAsync(target); },
                size => { CloseInspector(); _ = _controller?.FilterAsync(state.Query, state.SelectedStatus, size); }, AdminAutoscanActivityApi.PageSizes,
                scans ? "web.admin.common.pagination.scans" : "web.admin.common.pagination.polls", fetching, _phone, AdminAutoscanActivityApi.MaxHistoryPages);
            _rows.Children.Add(pager);
            if ((scans ? state.Scans.Capped : state.Polls.Capped)) _rows.Children.Add(Text(A.T("native.capped"), 12, true));
        }
        RestoreFocus(_rows, focus);
    }

    private void RenderNotices(AdminAutoscanActivityState state, AdminFailureKind? failure, int count)
    {
        var focus = CaptureFocus(_notices);
        _notices.Children.Clear();
        if (state.LabelsFailed || state.Labels.Truncated) _notices.Children.Add(Text(A.T("native.labelsPartial"), 12, true));
        if (state.Message != null) { var message = Text(A.Safe(state.Message), 13); AutomationProperties.SetAutomationId(message, "AutoscanActivityMessage"); _notices.Children.Add(message); }
        foreach (var review in state.CancelReview)
        {
            var notice = Stack(8); notice.Children.Add(SafeText(A.Library(review.Key, state.Labels) + ": " + review.Value, 13));
            var reconcile = Button(A.T("native.reconcile"), "AutoscanReconcile_" + review.Key); reconcile.IsEnabled = state.Reconciling == null;
            var libraryId = review.Key; reconcile.Click += (_, _) => _ = _controller?.ReconcileCancelAsync(libraryId);
            notice.Children.Add(reconcile); _notices.Children.Add(Card(notice));
        }
        if (failure != null && count > 0)
        {
            // A background refresh failed: the rows already shown stay, the notice says so, and polling continues.
            var stale = Stack(8); stale.Children.Add(Text(A.T("history.loadFailed"), 13));
            var retry = Button(A.T("common.tryAgain"), "AutoscanActivityStaleRetry"); retry.Click += (_, _) => _ = _controller?.ReloadHistoryAsync();
            stale.Children.Add(retry); _notices.Children.Add(Card(stale));
        }
        RestoreFocus(_notices, focus);
    }
}
