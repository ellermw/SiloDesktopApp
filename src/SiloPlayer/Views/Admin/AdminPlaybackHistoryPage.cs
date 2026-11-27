using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using A = SiloPlayer.Controls.AdminMonitorUi;
using U = SiloPlayer.Views.Admin.AdminPlaybackAuditUi;
using P = SiloPlayer.Core.Services.Admin.AdminPlaybackHistoryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>Playback History, with server paging and session details at the current route.</summary>
public sealed partial class AdminPlaybackHistoryPage : Page, IAdminPageSuspension, IAdminGutterScrollPage
{
    private readonly AdminPlaybackHistoryApi? _api;
    private readonly AdminMonitorAuthority? _authority;
    private readonly StackPanel _body = Stack(24);
    private readonly AdminLibrariesApi? _librariesApi;
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll;
    private readonly LogsDetailSheet _detailSheet;
    private readonly LogsWrapPanel _chips = new();
    private readonly TextBox _search;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ToggleButton _burnIn;
    private readonly ComboBox _user;
    private readonly ComboBox _profile;
    private readonly ComboBox _completion, _library, _method, _network;
    private readonly Button _reset;
    private readonly Button _refresh;
    private readonly TextBlock _refreshLabel = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pickerNote = U.Status("AdminHistoryPickerNote");
    private readonly LogsMetricGrid _cards = new(3, 1) { ColumnSpacing = 12, RowSpacing = 12 };
    private (int Shown, int Completed, int Partial)? _counts;
    private IReadOnlyList<P.FilterChip> _renderedChips = [];
    private readonly TextBlock _status = U.Status("AdminHistoryStatus");
    private readonly StackPanel _content = Stack(12);
    private readonly List<(AdminPlaybackHistoryRow Row, TextBlock Label)> _relativeStarts = [];
    private readonly LogsWrapPanel _pager = new() { Spacing = 12, Margin = new(8, 0, 8, 0) };
    private readonly TextBlock _range = U.Status("AdminHistoryRange", 14);
    private readonly ComboBox _pageSize;
    private readonly Button _first, _previous;
    private readonly Button _next;
    private AdminPlaybackHistoryController? _controller;
    private AdminPlaybackHistoryFilter _filter = new();
    private AdminRouteRequest _location = new(AdminRoute.PlaybackHistory);
    private string? _syncedSearch;
    private readonly List<(string Id, Grid Grid)> _rowGrids = [];
    private string? _renderedKind;
    private AdminPlaybackHistoryWindow? _renderedWindow;
    private string? _renderedError;
    private long _renderedRevision = -1;
    private bool _navigated, _active, _syncing;
    private bool _closingDetail;

    FrameworkElement IAdminGutterScrollPage.ScrollContent => _body;

    /// <summary>Test seam: the controller of the active page owner.</summary>
    public AdminPlaybackHistoryController? Controller => _controller;
    /// <summary>Test seam: the session ids of the rendered server page, in order.</summary>
    public IReadOnlyList<string> RenderedSessionIds { get; private set; } = [];
    /// <summary>Test seam: what the content area shows ("closed", "loading", "error", "empty", "table").</summary>
    public string RenderedContent { get; private set; } = "closed";

    public AdminPlaybackHistoryPage() : this(App.Services.GetService<AdminPlaybackHistoryApi>()
        ?? (App.Services.GetService<SiloApiClient>() is { } client ? new AdminPlaybackHistoryApi(client) : null), App.Services.GetService<AdminMonitorAuthority>(), App.Services.GetService<AdminLibrariesApi>()) { }

    public AdminPlaybackHistoryPage(AdminPlaybackHistoryApi? api, AdminMonitorAuthority? authority, AdminLibrariesApi? librariesApi = null)
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminPlaybackHistoryPage");
        _api = api;
        _authority = authority;
        _librariesApi = librariesApi;

        _user = FilterPicker("AdminHistoryUser", "admin.native.playbackAudit.userFilterLabel", 160, "user_id");
        _profile = FilterPicker("AdminHistoryProfile", "admin.native.playbackAudit.profileFilterLabel", 194, "profile_id");
        _library = FilterPicker("AdminHistoryLibrary", "web.admin.history.filter.allLibraries", 164, "library_id");
        _method = FilterPicker("AdminHistoryMethod", "web.admin.history.filter.allMethods", 122, "play_method");
        _completion = FilterPicker("AdminHistoryCompletion", "admin.native.playbackAudit.completionFilterLabel", 128, "completed");
        _network = FilterPicker("AdminHistoryNetwork", "web.admin.history.filter.allLocations", 134, "stream_location");
        U.SetOptions(_method, [("all", U.T("web.admin.history.filter.allMethods")),
            ("direct", AdminActivityPresentation.FormatDecisionLabel("direct")), ("remux", AdminActivityPresentation.FormatDecisionLabel("remux")),
            ("transcode", AdminActivityPresentation.FormatDecisionLabel("transcode"))], "all");
        U.SetOptions(_completion, [("all", U.T("web.admin.history.filter.allAttempts")),
            ("true", U.T("web.admin.history.filter.completed")), ("false", U.T("web.admin.history.filter.partial"))], "all");
        U.SetOptions(_network, [("all", U.T("web.admin.history.filter.allLocations")),
            ("local", U.T("web.admin.history.filter.locationLocal")), ("remote", U.T("web.admin.history.filter.locationRemote"))], "all");

        var placeholder = U.T("web.admin.history.filter.searchPlaceholder");
        _search = new TextBox { PlaceholderText = placeholder, FontSize = 12, Height = 36,
            Padding = new(32, 5, 8, 5), CornerRadius = new(8), BorderBrush = Brush("BorderBrush") };
        AutomationProperties.SetName(_search, placeholder);
        AutomationProperties.SetAutomationId(_search, "AdminHistorySearch");
        _search.TextChanged += (_, _) =>
        {
            if (_syncing || !_active) return;
            _searchTimer.Stop();
            if (NullIfEmpty(_search.Text) != _filter.Search) _searchTimer.Start();
        };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ChangeQuery("search", NullIfEmpty(_search.Text));
        };
        var search = new Grid { MinWidth = 300 };
        search.Children.Add(_search);
        var searchIcon = SiloPlayer.Controls.AdminMonitorUi.Icon("search", 14, Brush("SecondaryTextBrush"));
        searchIcon.Margin = new(10, 0, 0, 0);
        searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        searchIcon.VerticalAlignment = VerticalAlignment.Center;
        searchIcon.IsHitTestVisible = false;
        search.Children.Add(searchIcon);

        var burnInLabel = U.T("web.admin.history.filter.burnInOnly");
        var burnInContent = Inline(6);
        burnInContent.Children.Add(SiloPlayer.Controls.AdminMonitorUi.Icon("flame", 14, Brush("PrimaryTextBrush")));
        burnInContent.Children.Add(Text(burnInLabel, 12));
        _burnIn = new ToggleButton { Content = burnInContent, Height = 36, Padding = new(10, 0, 10, 0), CornerRadius = new(8) };
        _burnIn.Background = Brush("AppBackgroundBrush");
        _burnIn.BorderBrush = Brush("BorderBrush");
        _burnIn.BorderThickness = new(1);
        foreach (var state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
        {
            _burnIn.Resources["ToggleButtonBackground" + state] = A.Solid(A.Foreground, 0.12);
            _burnIn.Resources["ToggleButtonBorderBrush" + state] = Brush("BorderBrush");
        }
        AutomationProperties.SetName(_burnIn, burnInLabel);
        AutomationProperties.SetAutomationId(_burnIn, "AdminHistoryBurnIn");
        _burnIn.Checked += (_, _) => { if (!_syncing) ChangeQuery("subtitle_burn_in", "true"); };
        _burnIn.Unchecked += (_, _) => { if (!_syncing) ChangeQuery("subtitle_burn_in", null); };
        var filters = new LogsWrapPanel { GrowFields = true };
        foreach (var control in new FrameworkElement[] { search, _user, _profile, _library, _method, _completion, _network, _burnIn })
            filters.Children.Add(control);
        _burnIn.Width = 142;
        var toolbar = Stack(8);
        toolbar.Children.Add(filters);
        toolbar.Children.Add(_chips);
        _pickerNote.Visibility = Visibility.Collapsed;
        _chips.Visibility = Visibility.Collapsed;
        toolbar.Children.Add(_pickerNote);

        var actions = new LogsWrapPanel();
        _reset = Button(U.T("web.admin.history.filter.reset"), "AdminHistoryReset");
        _reset.Click += (_, _) =>
        {
            _searchTimer.Stop();
            SyncSearch(null, force: true);
            Replace(new AdminRouteRequest(AdminRoute.PlaybackHistory));
        };
        _refresh = Button("", "AdminHistoryRefresh");
        var refreshContent = Inline(8);
        refreshContent.Children.Add(SiloPlayer.Controls.AdminMonitorUi.Icon("refresh-cw", 14, Brush("PrimaryTextBrush")));
        refreshContent.Children.Add(_refreshLabel);
        _refresh.Content = refreshContent;
        _refresh.MinWidth = 132;
        _refresh.Click += (_, _) => { if (_controller != null) _ = _controller.RefreshAsync(manual: true); };
        actions.Children.Add(_reset);
        actions.Children.Add(_refresh);
        _body.Children.Add(Header(U.T("web.admin.history.title"), U.T("web.admin.history.subtitle"), actions));
        _body.Children.Add(U.Card(toolbar, 14));
        _body.Children.Add(_cards);

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
        var left = Inline(12);
        left.Children.Add(_range);
        _pageSize = U.Picker("AdminHistoryPageSize", U.T("admin.native.playbackAudit.pageSizeLabel"), 120);
        U.SetOptions(_pageSize, AdminPlaybackHistoryController.PageSizeOptions.Select(size =>
            (size.ToString(System.Globalization.CultureInfo.InvariantCulture), U.T("web.admin.history.pagination.pageSize", AdminPlaybackAuditText.Count(size)))), "25");
        _pageSize.Height = 32;
        _pageSize.SelectionChanged += (_, _) => { if (!_syncing && int.TryParse(U.Selected(_pageSize), out var size)) _controller?.SetPageSize(size); };
        left.Children.Add(_pageSize);
        _pager.Children.Add(left);
        var buttons = Inline(8);
        _pager.TrailingActions = buttons;
        _first = Button(U.T("web.admin.history.pagination.firstPage"), "AdminHistoryFirst");
        _first.Click += (_, _) => _controller?.FirstPage();
        buttons.Children.Add(_first);
        _previous = Button(U.T("web.admin.history.pagination.previous"), "AdminHistoryPrevious");
        _previous.Click += (_, _) => _controller?.PreviousPage();
        _next = Button(U.T("web.admin.history.pagination.next"), "AdminHistoryNext");
        _next.Click += (_, _) => _controller?.NextPage();
        buttons.Children.Add(_previous);
        buttons.Children.Add(_next);
        _pager.Children.Add(buttons);
        _pager.Visibility = Visibility.Collapsed;
        panel.Children.Add(_pager);
        _body.Children.Add(U.Card(panel));

        _body.MaxWidth = AdminShellPage.ContentMaxWidth;
        _body.HorizontalAlignment = HorizontalAlignment.Stretch;
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new(0, 0, 0, 24) };
        _root.Children.Add(_scroll);
        _detailSheet = new LogsDetailSheet(_scroll, 576);
        AutomationProperties.SetAutomationId(_detailSheet, "AdminHistoryDetail");
        _root.Children.Add(_detailSheet);
        _detailSheet.AddHeadingActions(CreateDetailNavigation());
        _detailSheet.Closed += () =>
        {
            if (!_closingDetail && _active) ChangeQuery("session", null);
        };
        _detailSheet.PreviewKeyDown += OnDetailKeyDown;
        Content = _root;
        Loaded += (_, _) =>
        {
            if (!_navigated) return;
            Activate();
            if (_controller?.State is { IsOpen: true } state) RenderDetail(state);
        };
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded) Deactivate(dispose: false, clearVisuals: false);
        });
        RenderClosed();
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private ComboBox FilterPicker(string id, string label, double width, string key)
    {
        var picker = U.Picker(id, U.T(label), width);
        picker.FontSize = 12;
        picker.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _active && U.Selected(picker) is { Length: > 0 } value)
                ChangeQuery(key, value == "all" ? null : value);
        };
        return picker;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var request = e.Parameter as AdminRouteRequest ?? new(AdminRoute.PlaybackHistory);
        _filter = AdminPlaybackHistoryFilter.FromQuery(request.Get);
        _location = _filter.HasUser ? request : request.WithQuery("profile_id", null);
        _navigated = true;
        Activate();
        if (!request.Equals(_location))
            DispatcherQueue.TryEnqueue(() => { if (_active) AdminShellPage.TryReplace(this, _location); });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _navigated = false;
        Deactivate(dispose: true, clearVisuals: IsLoaded);
    }

    /// <summary>Outer Admin departure: retire everything and never resume from it.</summary>
    public void SuspendAdminPage()
    {
        _navigated = false;
        Deactivate(dispose: true, clearVisuals: IsLoaded);
    }

    internal void CloseRetiredSheet()
    {
        if (!_active) CloseDetail(restoreFocus: false);
    }

    private void Activate()
    {
        if (_active || _api == null || _authority == null) return;
        CloseRetiredSheet();
        _active = true;
        if (_controller == null)
        {
            _controller = new AdminPlaybackHistoryController(_api, _authority, librariesApi: _librariesApi);
            _controller.Changed += QueueRender;
            _controller.AuthorityRetired += () => DispatcherQueue.TryEnqueue(() => { if (_active) _controller?.Reopen(); });
        }
        else _controller.Resume();
        if (App.MainWindowInstance is { } window)
        {
            window.VisibilityChanged += OnWindowVisibilityChanged;
            window.AppWindow.Changed += OnAppWindowChanged;
        }
        _controller.Open(_filter);
        UpdateVisibility();
        Render();
    }

    private void Deactivate(bool dispose, bool clearVisuals = true)
    {
        _searchTimer.Stop();
        if (App.MainWindowInstance is { } window)
        {
            window.VisibilityChanged -= OnWindowVisibilityChanged;
            window.AppWindow.Changed -= OnAppWindowChanged;
        }
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
        if (clearVisuals) RenderClosed();
    }

    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args) => UpdateVisibility(args.Visible);

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => UpdateVisibility();

    private void UpdateVisibility(bool? visible = null)
    {
        var window = App.MainWindowInstance;
        _controller?.SetVisible(_active && (visible ?? window?.Visible ?? true) &&
            window?.AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized });
    }

    private void ChangeQuery(string key, string? value)
    {
        var next = _location.WithQuery(key, value);
        if (key == "user_id") next = next.WithQuery("profile_id", null);
        Replace(next);
    }

    /// <summary>Replace the current address while retaining the mounted controls and navigation entries.</summary>
    private void Replace(AdminRouteRequest request)
    {
        if (!_active || request.Equals(_location)) return;
        AdminShellPage.TryReplace(this, request);
        _location = request;
        _filter = AdminPlaybackHistoryFilter.FromQuery(request.Get);
        _controller?.ApplyFilter(_filter);
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
        _content.Children.Clear();
        _rowGrids.Clear();
        RenderedSessionIds = [];
        RenderedContent = "closed";
        _pager.Visibility = Visibility.Collapsed;
        RenderFilters(AdminPlaybackHistoryState.Closed);
        RenderCounts(null);
        CloseDetail(restoreFocus: false);
        _status.Text = "";
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
        RenderFilters(state);
        RenderCounts((state.Total, state.CompletedCount, state.PartialCount));
        _status.Text = U.T(state.Fetching ? "web.admin.activity.page.refreshing" : "web.admin.history.recent.autoRefreshing");
        SetRefresh(state.ManualRefreshPending, enabled: !state.ManualRefreshPending);
        _reset.IsEnabled = true;
        RenderContent(state);
        RenderDetail(state);
    }

    private void SetRefresh(bool pending, bool enabled)
    {
        _refresh.IsEnabled = enabled;
        _refreshLabel.Text = U.T(pending ? "web.admin.activity.page.refreshing" : "web.admin.activity.page.refresh");
        AutomationProperties.SetName(_refresh, _refreshLabel.Text);
        AutomationProperties.SetItemStatus(_refresh, pending ? _refreshLabel.Text : "");
    }

    private void RenderCounts((int Shown, int Completed, int Partial)? counts)
    {
        if (_counts == counts) return;
        _counts = counts;
        _cards.Children.Clear();
        if (counts is not { } values) return;
        foreach (var (key, value, id) in new[]
        {
            ("visibleRows", values.Shown, "Visible"),
            ("completed", values.Completed, "Completed"),
            ("partial", values.Partial, "Partial"),
        })
        {
            var label = U.T("web.admin.history.stats." + key);
            var card = AdminInsightsUi.KpiCard(label, AdminText.FormatNumber(value));
            AutomationProperties.SetAutomationId(card, "AdminHistoryStat" + id);
            AutomationProperties.SetName(card, label);
            _cards.Add(card);
        }
    }

    private void SyncSearch(string? search, bool force = false)
    {
        if (!force && _syncedSearch == search) return;
        var syncing = _syncing;
        _syncing = true;
        try
        {
            _searchTimer.Stop();
            _syncedSearch = search;
            if (_search.Text != (search ?? "")) _search.Text = search ?? "";
        }
        finally { _syncing = syncing; }
    }

    private void RenderFilters(AdminPlaybackHistoryState state)
    {
        _syncing = true;
        try
        {
            var filter = state.Filter;
            if (state.IsOpen) SyncSearch(filter.Search);
            else SyncSearch(null, force: true);
            _search.IsEnabled = _user.IsEnabled = _library.IsEnabled = _method.IsEnabled =
                _completion.IsEnabled = _network.IsEnabled = _burnIn.IsEnabled = state.IsOpen;
            _profile.IsEnabled = state.IsOpen && state.ProfilePickerEnabled;
            RenderOptions(_user, "web.admin.history.filter.allUsers", state.Users?.Items.Select(user => (user.Id, user.Username)),
                filter.UserId, filter.UserId == null ? null : U.T("web.admin.activity.userFallback", ("id", filter.UserId)));
            RenderOptions(_profile, filter.HasUser ? "web.admin.history.filter.allProfiles" : "web.admin.history.filter.chooseUserFirst",
                state.Profiles?.Items.Select(profile => (profile.Id, profile.Name.Length > 0 ? profile.Name : profile.Id)),
                filter.ProfileId, state.MissingProfileLabel);
            RenderOptions(_library, "web.admin.history.filter.allLibraries",
                state.Libraries.Select(library => (library.Id, library.Name)), filter.LibraryId, filter.LibraryId);
            U.Select(_method, filter.PlayMethod ?? "all");
            U.Select(_completion, filter.CompletedValue);
            U.Select(_network, filter.StreamLocation ?? "all");
            _burnIn.IsChecked = filter.SubtitleBurnIn;
            _burnIn.Background = filter.SubtitleBurnIn ? A.Solid(A.Foreground, 0.12) : Brush("AppBackgroundBrush");
            U.Select(_pageSize, state.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { _syncing = false; }

        var notes = new List<string>();
        if (state.Users is { Truncated: true } users) notes.Add(U.T("admin.native.playbackAudit.usersTruncated", AdminPlaybackAuditText.Count(users.Items.Count)));
        if (state.Profiles is { Truncated: true } profiles) notes.Add(U.T("admin.native.playbackAudit.profilesTruncated", AdminPlaybackAuditText.Count(profiles.Items.Count)));
        _pickerNote.Text = string.Join(" ", notes);
        _pickerNote.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderChips(P.Chips(state.Filter, state.Rows));
    }

    private static void RenderOptions(ComboBox picker, string allKey, IEnumerable<(string, string)>? items, string? selected, string? missingLabel)
    {
        var options = new List<(string, string)> { ("all", U.T(allKey)) };
        if (items != null) options.AddRange(items);
        if (selected != null && !options.Any(option => option.Item1 == selected)) options.Add((selected, missingLabel ?? selected));
        if (!picker.Items.OfType<ComboBoxItem>().Select(item => ((string)item.Tag, (string)item.Content)).SequenceEqual(options))
            U.SetOptions(picker, options, selected ?? "all");
        else U.Select(picker, selected ?? "all");
    }

    private void RenderChips(IReadOnlyList<P.FilterChip> chips)
    {
        if (_renderedChips.Select(chip => (chip.Key, chip.Label, chip.ItemId))
            .SequenceEqual(chips.Select(chip => (chip.Key, chip.Label, chip.ItemId)))) return;
        _renderedChips = chips;
        _chips.Children.Clear();
        foreach (var chip in chips)
        {
            var line = new Grid { ColumnSpacing = 6 };
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            if (chip.ItemId is { } itemId)
            {
                var title = new Grid { ColumnSpacing = 6 };
                title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                title.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                title.Children.Add(Text(U.T("web.admin.history.itemFilterActive"), 12, true));
                var link = U.Link(ChipLabel(chip.Label), chip.Label, "AdminHistoryItemFilterLink", () => OpenItem(itemId));
                Grid.SetColumn(link, 1);
                title.Children.Add(link);
                line.Children.Add(title);
            }
            else line.Children.Add(ChipLabel(chip.Label));

            var name = U.T("web.admin.history.filter.removeChip", ("label", chip.Label));
            var remove = Button("", "AdminHistoryRemove_" + chip.Key, "GhostButtonStyle", "x");
            remove.Width = 24;
            remove.Height = 24;
            remove.MinWidth = remove.MinHeight = 0;
            remove.Padding = new(0);
            AutomationProperties.SetName(remove, name);
            ToolTipService.SetToolTip(remove, name);
            remove.Click += (_, _) =>
            {
                var next = _location;
                foreach (var key in chip.RemoveKeys) next = next.WithQuery(key, null);
                Replace(next);
            };
            Grid.SetColumn(remove, 1);
            line.Children.Add(remove);
            _chips.Children.Add(new Border { Child = line, CornerRadius = new(6), Padding = new(8, 2, 4, 2),
                BorderBrush = Brush("BorderBrush"), BorderThickness = new(1), Background = Tint("MutedBrush", 0.4), MaxWidth = 320 });
        }
        _chips.Visibility = chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock ChipLabel(string value)
    {
        var label = Text(value, 12);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        return label;
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
            ? _renderedWindow != null && state.Rows.Count == _renderedWindow.Rows.Count &&
                state.Rows.Zip(_renderedWindow.Rows).All(pair =>
                    (pair.First with { AdaptationReasons = pair.Second.AdaptationReasons, Subtitle = pair.Second.Subtitle }) == pair.Second)
            : kind != "error" || state.Error == _renderedError);
        if (!same)
        {
            _renderedKind = kind;
            _renderedWindow = state.Window;
            _renderedError = state.Error;
            _content.Children.Clear();
            _relativeStarts.Clear();
            _rowGrids.Clear();
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
                    var rows = state.Rows;
                    _content.Children.Add(Table(rows));
                    RenderedSessionIds = rows.Select(row => row.SessionId).ToArray();
                    break;
            }
        }
        // Keep relative times current without replacing unchanged rows or their focus.
        var now = DateTimeOffset.UtcNow;
        foreach (var (row, label) in _relativeStarts) label.Text = P.Started(row, now);
        RenderedContent = kind;
        foreach (var (id, grid) in _rowGrids)
            grid.Background = id == state.Filter.SessionId ? Tint("AccentBrush", 0.10) : _transparentRow;
        var showPager = kind == "table" || (kind == "empty" && state.CanPrevious);
        _pager.Visibility = showPager ? Visibility.Visible : Visibility.Collapsed;
        _range.Text = U.T("web.admin.history.pagination.summary", AdminPlaybackAuditText.Count(state.Total));
        _first.Visibility = state.PageIndex > 1 ? Visibility.Visible : Visibility.Collapsed;
        _previous.IsEnabled = state.CanPrevious;
        _next.IsEnabled = state.CanNext;
    }

    private readonly SolidColorBrush _transparentRow = new(Microsoft.UI.Colors.Transparent);

    private LogsNativeTable Table(IReadOnlyList<AdminPlaybackHistoryRow> rows)
    {
        var headers = new[] { "media", "user", "profile", "client", "method", "watchTime", "status", "ended", "logs" }.Select(key => U.T("web.admin.history.column." + key)).ToArray();
        // At narrow widths the column minimums stay intact inside the table's scroll viewer.
        var table = new LogsNativeTable(headers, [170, 88, 104, 110, 82, 90, 90, 128, 208],
            cellPadding: 6, useColumnMinimums: true);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            // Media: series-led title, item link, meta line.
            var media = Stack(4);
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
            var accountLabel = RowLabel(username, 13, strong: true);
            UIElement user = AdminShellPage.CanOpen(userRequest)
                ? U.Link(accountLabel, username, "AdminHistoryUser_" + row.SessionId, () => AdminShellPage.Open(userRequest))
                : accountLabel;

            // Profile: filters this page to the row's account and profile (push).
            var profile = Stack(4);
            var profileLabel = P.ProfileLabel(row);
            var profileRequest = P.ProfileRequest(row);
            profile.Children.Add(U.Link(RowLabel(profileLabel, 12), profileLabel, "AdminHistoryProfile_" + row.SessionId, () => Replace(profileRequest)));
            profile.Children.Add(RowLabel(row.ProfileId, 12, muted: true));

            var client = Stack(2);
            var clientLine = new Grid { ColumnSpacing = 4 };
            clientLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto, MaxWidth = row.IsJellyfinClient ? 69 : 94 });
            clientLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            clientLine.Children.Add(RowLabel(row.ClientName ?? "—", 12, strong: true));
            if (row.IsJellyfinClient)
            {
                var badge = JellyfinBadge();
                Grid.SetColumn(badge, 1);
                clientLine.Children.Add(badge);
            }
            client.Children.Add(clientLine);
            if (!string.IsNullOrEmpty(row.DevicePlatform)) client.Children.Add(Text(row.DevicePlatform, 11, true));

            var watch = Stack(4);
            var watchText = Text(P.WatchTimeText(row));
            if (P.WatchTime(row).Estimated) ToolTipService.SetToolTip(watchText, U.T("web.admin.history.row.estimatedTitle"));
            watch.Children.Add(watchText);
            watch.Children.Add(Text(U.T("web.admin.history.row.ofDuration", ("duration", P.FormatDuration(row.DurationSeconds))), 12, true));

            var ended = Stack(4);
            ended.Children.Add(RowLabel(U.FormatDateTime(row.EndedAt), 12));
            var relativeStart = Text(P.Started(row, now), 12, true);
            ended.Children.Add(relativeStart);
            _relativeStarts.Add((row, relativeStart));

            var logs = Inline(8);
            logs.HorizontalAlignment = HorizontalAlignment.Right;
            var logsRequest = P.LogsRequest(row, ffmpeg: false);
            var ffmpegRequest = P.LogsRequest(row, ffmpeg: true);
            var details = AdminText.T("web.admin.activity.row.details");
            var detailLink = RowAction(details, "AdminHistoryDetails_" + row.SessionId);
            detailLink.Click += (_, _) => SelectSession(row.SessionId, detailLink);
            logs.Children.Add(detailLink);
            var viewLogs = U.T("web.admin.activity.row.viewLogs");
            var ffmpegLogs = U.T("web.admin.activity.row.ffmpegLogs");
            var logsLink = RowAction(viewLogs, "AdminHistoryLogs_" + row.SessionId);
            logsLink.Click += (_, _) => AdminShellPage.Open(logsRequest);
            logs.Children.Add(logsLink);
            var ffmpegLink = RowAction(ffmpegLogs, "AdminHistoryFFmpeg_" + row.SessionId);
            ffmpegLink.Click += (_, _) => AdminShellPage.Open(ffmpegRequest);
            logs.Children.Add(ffmpegLink);

            var grid = table.Add([media, user, profile, client, MethodBadge(row), watch, StatusBadge(row, 11), ended, logs]);
            AutomationProperties.SetAutomationId(grid, "AdminHistoryRow_" + row.SessionId);
            grid.Background = _transparentRow;
            grid.Tapped += (_, args) =>
            {
                for (var source = args.OriginalSource as DependencyObject; source != null && source != grid; source = VisualTreeHelper.GetParent(source))
                    if (source is ButtonBase) return;
                SelectSession(row.SessionId);
            };
            _rowGrids.Add((row.SessionId, grid));
        }
        table.AlignEnd(8);
        return table;
    }

    private static Button RowAction(string value, string id)
    {
        var label = RowLabel(value, 12, strong: true);
        label.Foreground = Brush("AccentBrush");
        var button = A.LinkButton(label, value, label.Foreground is SolidColorBrush accent ? accent.Color : A.Primary);
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    private static TextBlock RowLabel(string value, double size, bool strong = false, bool muted = false)
    {
        var label = Text(value, size, muted);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        if (strong) label.FontWeight = FontWeights.Medium;
        ToolTipService.SetToolTip(label, value);
        return label;
    }

    private static void OpenItem(string itemId)
    {
        if (App.Services.GetService<NavigationService>() is { } navigation) navigation.Navigate<ItemDetailPage>(itemId);
    }
}
