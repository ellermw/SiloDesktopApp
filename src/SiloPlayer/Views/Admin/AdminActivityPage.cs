using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Admin › Activity (WebUI <c>pages/AdminActivity.tsx</c>): live streams from the
/// shared <see cref="ServerActivityStore"/>, realtime state, IP Lookup, method and
/// routing-node filters, a media-first table with conditional Route and Bitrate
/// columns, stacked cards below 62rem/70rem, source/delivered details and server reasons,
/// on-demand FFmpeg console and session actions. Every action captures the
/// current <see cref="MonitorAuthorityLease"/> before any dialog and publishes
/// only while that lease is current.
/// </summary>
public sealed partial class AdminActivityPage : Page, IAdminGutterScrollPage
{
    FrameworkElement IAdminGutterScrollPage.ScrollContent => _page;

    /// <summary>Official container boundaries: 62rem, or 70rem with the Route column.</summary>
    public const double WideTableMinWidth = 992;
    public const double WideTableWithRouteMinWidth = 1120;
    private double CurrentTableMinWidth => ShowRoute ? WideTableWithRouteMinWidth : WideTableMinWidth;
    private static readonly TimeSpan RefreshSpinnerMinimum = TimeSpan.FromSeconds(1);

    private readonly ServerActivityStore? _store;
    private readonly AdminMonitorAuthority? _authority;
    private readonly AdminMonitorApi? _api;
    private readonly ToastService? _toasts;
    private readonly AdminActivityListState _list = new();
    private readonly AdminActivityIpLookup? _ipLookup;
    private readonly Dictionary<string, RowState> _rowStates = new(StringComparer.Ordinal);
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _page = new() { Spacing = 16 };
    private readonly StackPanel _header = new();
    private readonly StackPanel _ipLookupHost = new();
    private readonly StackPanel _filterHost = new();
    private readonly StackPanel _tableHost = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBox _ipBox = new();
    private ServerActivitySnapshot _snapshot = ServerActivitySnapshot.Empty;
    private MonitorAuthorityLease? _lease;
    private IDisposable? _reads;
    private bool _active;
    private bool _navigated;
    private ContentDialog? _messageDialog;
    private TextBox? _messageDraft;
    private string? _messageSessionId;
    private bool _refreshing;
    private bool _ipOpen;
    private bool _wasHidden;
    private double _tableWidth;
    private int _viewportBand = -1;
    private double ActivityViewportWidth => XamlRoot?.Size.Width ?? ActualWidth;

    public AdminActivityPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        try
        {
            _store = App.Services.GetService<ServerActivityStore>();
            _authority = App.Services.GetService<AdminMonitorAuthority>();
            _api = App.Services.GetService<AdminMonitorApi>();
            _toasts = App.Services.GetService<ToastService>();
        }
        catch (InvalidOperationException) { }
        if (_api != null)
        {
            _ipLookup = new AdminActivityIpLookup(_api);
            _ipLookup.Changed += () => DispatcherQueue?.TryEnqueue(RenderIpLookup);
        }
        _scroller.Content = _page;
        Content = _scroller;
        _page.Children.Add(_header);
        _page.Children.Add(_filterHost);
        _page.Children.Add(_tableHost);
        _page.Padding = new Thickness(0, 0, 0, 32);
        _tableHost.SizeChanged += (_, e) =>
        {
            var wasWide = _tableWidth >= CurrentTableMinWidth;
            _tableWidth = e.NewSize.Width;
            if (wasWide != _tableWidth >= CurrentTableMinWidth) RenderTable();
        };
        SizeChanged += (_, _) =>
        {
            var width = ActivityViewportWidth;
            var band = width >= 1024 ? 2 : width >= 640 ? 1 : 0;
            _page.Spacing = 16;
            if (_viewportBand == band) return;
            _viewportBand = band;
            if (_active) { RenderHeader(); RenderTable(); }
        };
        BuildFilterControls();
        Loaded += (_, _) => { if (_navigated) Activate(); };
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!IsLoaded) Deactivate(); });
    }

    /// <summary>Test seam: the rows currently rendered, in order.</summary>
    public IReadOnlyList<string> RenderedSessionIds { get; private set; } = [];
    public bool IsWideTable => _tableWidth >= CurrentTableMinWidth;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _navigated = true;
        Activate();
    }

    private void Activate()
    {
        if (_active) return;
        _active = true;
        if (_store != null) _store.Changed += OnStoreChanged;
        if (_authority != null) _authority.AvailabilityChanged += OnAvailabilityChanged;
        if (App.MainWindowInstance is { } window)
        {
            window.VisibilityChanged += OnWindowVisibilityChanged;
            window.Activated += OnWindowActivated;
        }
        Recapture();
        Render(_store?.Current ?? ServerActivitySnapshot.Empty);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _navigated = false;
        Deactivate();
    }

    internal void SuspendMonitor()
    {
        _navigated = false;
        Deactivate();
    }

    private void Deactivate()
    {
        if (!_active) return;
        _active = false;
        if (_store != null) _store.Changed -= OnStoreChanged;
        if (_authority != null) _authority.AvailabilityChanged -= OnAvailabilityChanged;
        if (App.MainWindowInstance is { } window)
        {
            window.VisibilityChanged -= OnWindowVisibilityChanged;
            window.Activated -= OnWindowActivated;
        }
        RetireLease();
    }

    private void Recapture()
    {
        RetireLease();
        if (_authority?.IsAvailable != true) return;
        try { _lease = _authority.Capture(); }
        catch (OperationCanceledException) { return; }
        _reads = _store?.AcquireReads();
    }

    private void RetireLease()
    {
        _messageDialog?.Hide();
        _ipDialog?.Hide();
        if (_messageDraft != null) _messageDraft.Text = "";
        _messageDialog = null;
        _messageDraft = null;
        _messageSessionId = null;
        _ipBox.Text = "";
        _searchBox.Text = "";
        _list.Search = "";
        _list.ClearFilters();
        _ipOpen = false;
        _snapshot = ServerActivitySnapshot.Empty;
        ShowRoute = false;
        _header.Children.Clear();
        _ipLookupHost.Children.Clear();
        _ipResultsHost.Children.Clear();
        _tableHost.Children.Clear();
        _filterHost.Visibility = Visibility.Collapsed;
        RenderedSessionIds = [];
        _reads?.Dispose();
        _reads = null;
        _lease?.Dispose();
        _lease = null;
        _ipLookup?.Reset();
        _rowStates.Clear();
        _refreshing = false;
    }

    private void OnAvailabilityChanged() => DispatcherQueue?.TryEnqueue(() =>
    {
        if (!_active) return;
        Recapture();
        Render(_store?.Current ?? ServerActivitySnapshot.Empty);
    });

    private void OnStoreChanged(ServerActivitySnapshot snapshot) => DispatcherQueue?.TryEnqueue(() =>
    {
        if (_active) Render(_store?.Current ?? ServerActivitySnapshot.Empty);
    });

    /// <summary>Returning to a visible window after being hidden refreshes once, with the spinner.</summary>
    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        if (!args.Visible) { _wasHidden = true; return; }
        if (!_wasHidden || _refreshing) return;
        _wasHidden = false;
        _ = RefreshAsync();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_active && args.WindowActivationState != WindowActivationState.Deactivated) RefreshOpenConsoles();
    }

    private async Task RefreshAsync()
    {
        if (_store == null || _refreshing || _lease is not { } lease) return;
        _refreshing = true;
        RenderHeader();
        if (_snapshot.SessionsError != null) RenderTable();
        var started = DateTimeOffset.UtcNow;
        try { await _store.RefreshSessionsAsync(); }
        finally
        {
            var remaining = RefreshSpinnerMinimum - (DateTimeOffset.UtcNow - started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            if (ReferenceEquals(lease, _lease))
            {
                _refreshing = false;
                RenderHeader();
                if (_snapshot.SessionsError != null) RenderTable();
            }
        }
    }

    // ── Rendering ────────────────────────────────────────────

    private void Render(ServerActivitySnapshot snapshot)
    {
        if (!_active || _lease?.IsCurrent != true || snapshot.Epoch != _lease.Epoch)
            snapshot = ServerActivitySnapshot.Empty;
        if (_messageDialog != null && !snapshot.Sessions.Any(s => s.SessionId == _messageSessionId && s.HasPlaybackControl))
        {
            _messageDialog.Hide();
            if (_messageDraft != null) _messageDraft.Text = "";
        }
        _snapshot = snapshot;
        ShowRoute = AdminActivityPresentation.SessionRoutesVary(snapshot.Sessions);
        // Forget per-row state for sessions that ended.
        foreach (var gone in _rowStates.Keys.Except(snapshot.Sessions.Select(s => s.SessionId)).ToArray())
            _rowStates.Remove(gone);
        if (!snapshot.IsActingAdmin || !snapshot.SessionsLoaded)
        {
            _header.Children.Clear();
            _ipLookupHost.Children.Clear();
            _ipResultsHost.Children.Clear();
            _filterHost.Visibility = Visibility.Collapsed;
            _tableHost.Children.Clear();
            _header.Children.Add(A.Text(AdminText.T("web.admin.activity.page.loading"), 14, A.Muted));
            RenderedSessionIds = [];
            return;
        }
        var failedWithoutRows = snapshot.SessionsError != null && snapshot.Sessions.Count == 0;
        _filterHost.Visibility = failedWithoutRows ? Visibility.Collapsed : Visibility.Visible;
        RenderHeader();
        RenderIpLookup();
        RenderSummary();
        RenderFilterStatus();
        RenderTable();
    }

    private void RenderHeader()
    {
        _header.Children.Clear();
        if (!_snapshot.SessionsLoaded) return;
        var actions = new LogsWrapPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        var status = new StackPanel { MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(4, 0, 4, 0) };
        var heading = A.Eyebrow(AdminText.T("web.admin.activity.realtime.heading"), A.Muted, 100); heading.HorizontalAlignment = HorizontalAlignment.Right; status.Children.Add(heading);
        var state = A.Text(AdminText.T(_snapshot.Connection switch
        {
            AdminRealtimeConnectionState.Live => "web.admin.activity.realtime.live",
            AdminRealtimeConnectionState.Connecting => "web.admin.activity.realtime.connecting",
            _ => "web.admin.activity.realtime.disconnected",
        }), 12, A.Foreground); state.HorizontalAlignment = HorizontalAlignment.Right; status.Children.Add(state); actions.Children.Add(status);
        var lookup = A.Outline(AdminText.T("web.admin.activity.ipLookup.heading"), "globe");
        lookup.Click += (_, _) => _ = ShowIpLookupAsync(); actions.Children.Add(lookup);
        var refreshContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        refreshContent.Children.Add(_refreshing ? A.Spinner(14, A.Foreground) : A.Icon("refresh-cw", 14, A.Foreground));
        refreshContent.Children.Add(A.Text(AdminText.T(_refreshing ? "web.admin.activity.page.refreshing" : "web.admin.activity.page.refresh"), 12, A.Foreground, FontWeights.Medium));
        var refresh = A.Outline("", null); refresh.Content = refreshContent; refresh.IsEnabled = !_refreshing;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(refresh, AdminText.T(_refreshing ? "web.admin.activity.page.refreshing" : "web.admin.activity.page.refresh"));
        refresh.Click += (_, _) => _ = RefreshAsync(); actions.Children.Add(refresh);
        _header.Children.Add(new LogsPageHeader(AdminText.T("web.admin.activity.page.title"), _snapshot.SessionsError == null ? AdminActivityPresentation.ActivitySummary(_snapshot.Sessions) : "", actions) { Margin = new Thickness(0) });
    }

    // ── IP Lookup ────────────────────────────────────────────

    private ContentDialog? _ipDialog;
    private Button? _ipSubmit;
    private readonly StackPanel _ipResultsHost = new() { Spacing = 8 };

    private async Task ShowIpLookupAsync()
    {
        if (_ipDialog != null || _lease?.IsCurrent != true || XamlRoot == null) return;
        _ipOpen = true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = AdminText.T("web.admin.activity.ipLookup.heading"), CloseButtonText = AdminText.T("web.ui.close"), Content = _ipLookupHost };
        dialog.Resources["ContentDialogMaxWidth"] = 672d;
        _ipDialog = dialog;
        RenderIpLookup();
        try { await dialog.ShowAsync(); }
        finally
        {
            dialog.Content = null;
            if (ReferenceEquals(_ipDialog, dialog))
            {
                _ipDialog = null; _ipOpen = false; _ipBox.Text = ""; _ipLookup?.Reset(); _ipLookupHost.Children.Clear();
                _ipResultsHost.Children.Clear();
            }
        }
    }

    private void RenderIpLookup()
    {
        if (!_ipOpen) return;
        if (_ipLookupHost.Children.Count == 0)
        {
            var form = new LogsWrapPanel { Spacing = 8 };
            if (_ipBox.Parent is Panel parent) parent.Children.Remove(_ipBox);
            form.Children.Add(_ipBox);
            _ipSubmit = A.Outline(AdminText.T("web.admin.activity.ipLookup.submit"), "search");
            _ipSubmit.Click += (_, _) => RunIpLookup(_ipBox.Text);
            form.Children.Add(_ipSubmit);
            if (_ipResultsHost.Parent is Panel previous) previous.Children.Remove(_ipResultsHost);
            _ipLookupHost.Spacing = 16;
            _ipLookupHost.Children.Add(form);
            _ipLookupHost.Children.Add(_ipResultsHost);
        }
        RenderIpSubmitState();
        _ipResultsHost.Children.Clear();
        if (_ipLookup is { ActiveIp.Length: > 0 } lookup) _ipResultsHost.Children.Add(IpResults(lookup));
    }

    private void BuildFilterControls()
    {
        _ipBox.PlaceholderText = AdminText.T("web.admin.activity.ipLookup.placeholder");
        _ipBox.FontFamily = A.Mono;
        _ipBox.MaxWidth = 320;
        _ipBox.MinWidth = 240;
        _ipBox.TextChanged += (_, _) => { if (_ipOpen) RenderIpSubmitState(); };
        _ipBox.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            RunIpLookup(_ipBox.Text);
        };

        _searchBox.PlaceholderText = AdminText.T("web.admin.activity.filter.placeholder");
        _searchBox.FontSize = 13;
        _searchBox.Height = 32;
        _searchBox.Padding = new Thickness(36, 5, 30, 5);
        _searchBox.TextChanged += (_, _) =>
        {
            _list.Search = _searchBox.Text;
            RenderFilterRow();
            RenderFilterStatus();
            RenderTable();
        };
        RenderFilterRow();
    }

    private void RenderIpSubmitState()
    {
        if (_ipSubmit != null) _ipSubmit.IsEnabled = _ipBox.Text.Trim().Length > 0;
    }

    private void RunIpLookup(string ip)
    {
        var trimmed = ip.Trim();
        if (trimmed.Length == 0 || _ipLookup == null || _lease is not { } lease) return;
        _ipBox.Text = trimmed;
        _ = _ipLookup.StartAsync(lease, trimmed);
        if (_ipDialog == null) _ = ShowIpLookupAsync();
        else RenderIpLookup();
    }

    private UIElement IpResults(AdminActivityIpLookup lookup)
    {
        var host = new StackPanel { Margin = new Thickness(16, 0, 16, 16), Spacing = 8 };
        Button Reload()
        {
            var reload = A.Outline(AdminText.T("web.admin.activity.ipLookup.reload"));
            reload.Click += (_, _) => { if (_lease is { } lease) _ = lookup.RestartAsync(lease); };
            return reload;
        }
        switch (lookup.View)
        {
            case AdminIpLookupView.Searching:
                host.Children.Add(A.Text(AdminText.T("web.admin.activity.ipLookup.searching"), 14, A.Muted));
                return host;
            case AdminIpLookupView.LoadError:
                host.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { A.Text(AdminText.T("web.admin.activity.ipLookup.loadError"), 14, A.Foreground), Reload() } });
                return host;
            case AdminIpLookupView.Empty:
                host.Children.Add(A.Text(AdminText.T("web.admin.activity.ipLookup.empty", new { ip = lookup.ActiveIp }), 14, A.Muted, trim: false));
                return host;
        }
        var table = new Grid { ColumnSpacing = 16, RowSpacing = 0 };
        foreach (var width in new[] { 1.4, 1.0, 1.0, 0.6 })
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        void Cell(UIElement element, int row, int column)
        {
            Grid.SetRow((FrameworkElement)element, row);
            Grid.SetColumn((FrameworkElement)element, column);
            table.Children.Add(element);
        }
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headers = new[] { "user", "firstSeen", "lastSeen", "requests" };
        for (var i = 0; i < headers.Length; i++)
        {
            var head = A.Text(AdminText.T($"web.admin.activity.ipLookup.column.{headers[i]}"), 12, A.Muted, FontWeights.Medium);
            head.Margin = new Thickness(0, 8, 0, 8);
            if (i == 3) head.HorizontalAlignment = HorizontalAlignment.Right;
            Cell(head, 0, i);
        }
        var rows = lookup.Rows;
        for (var r = 0; r < rows.Count; r++)
        {
            var entry = rows[r];
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // /admin/users/{id} is outside the Monitor scope: same text, not a link.
            var user = A.Text(string.IsNullOrEmpty(entry.Username) ? AdminText.T("web.admin.activity.userFallback", new { id = entry.UserId }) : entry.Username,
                14, A.Primary, FontWeights.Medium);
            user.Margin = new Thickness(0, 8, 0, 8);
            Cell(user, r + 1, 0);
            Cell(A.Text(FormatDateTime(entry.FirstSeen), 14, A.Foreground), r + 1, 1);
            Cell(A.Text(FormatDateTime(entry.LastSeen), 14, A.Foreground), r + 1, 2);
            var count = A.Text(AdminText.FormatNumber(entry.RequestCount), 14, A.Foreground);
            count.HorizontalAlignment = HorizontalAlignment.Right;
            Cell(count, r + 1, 3);
        }
        host.Children.Add(table);
        if (lookup.HasError)
            host.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { A.Text(AdminText.T("web.admin.activity.ipLookup.loadMoreError"), 14, A.Foreground), Reload() } });
        if (lookup.HasNextPage)
        {
            var more = A.Outline(AdminText.T("web.admin.activity.ipLookup.loadMore"));
            more.IsEnabled = !lookup.IsFetchingNextPage;
            more.Click += (_, _) => { if (_lease is { } lease) _ = lookup.LoadMoreAsync(lease); };
            host.Children.Add(more);
        }
        return host;
    }

    internal static string FormatDateTime(string value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(parsed)
            : value;

    // ── Summary strip ────────────────────────────────────────

    private void RenderSummary() => RenderFilterRow();

    private LogsWrapPanel? _filterRow;
    private Grid? _searchHost;
    private Button? _clearSearch;

    private void RenderFilterRow()
    {
        if (_filterRow == null)
        {
            _filterRow = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
            _searchHost = new Grid { Width = 320 };
            _searchHost.Children.Add(_searchBox);
            var icon = A.Icon("search", 14, A.Muted); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); _searchHost.Children.Add(icon);
            _clearSearch = A.Plain(A.Icon("x", 14, A.Muted), AdminText.T("web.admin.activity.filter.clear"), new Thickness(4));
            _clearSearch.HorizontalAlignment = HorizontalAlignment.Right; _clearSearch.Margin = new Thickness(0, 0, 6, 0);
            _clearSearch.Click += (_, _) => _searchBox.Text = ""; _searchHost.Children.Add(_clearSearch);
            _filterRow.Children.Add(_searchHost); _filterHost.Children.Insert(0, _filterRow);
            _filterRow.SizeChanged += (_, e) => _searchHost.MaxWidth = e.NewSize.Width;
        }
        _clearSearch!.Visibility = _list.Search.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        while (_filterRow.Children.Count > 1) _filterRow.Children.RemoveAt(_filterRow.Children.Count - 1);
        var methods = new LogsWrapPanel { Spacing = 2, RowSpacing = 2 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(methods, AdminText.T("web.admin.activity.summary.methodHeading"));
        foreach (var method in AdminActivityListState.MethodCounts(_snapshot.Sessions))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = A.Solid(A.ToneColor(method.Meta.Tone)), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(A.Text(method.Meta.Label, 12, A.Foreground));
            row.Children.Add(A.Text(AdminText.FormatNumber(method.Count), 12, A.Muted));
            var selected = _list.MethodFilter == method.Meta.Key;
            var button = FilterSegment(row, AdminText.T("web.admin.activity.summary.methodFilter", new { method = method.Meta.Label, count = method.Count }), selected);
            ToolTipService.SetToolTip(button, method.Meta.Description);
            button.Click += (_, _) => { _list.ToggleMethod(method.Meta.Key); RefreshListViews(); }; methods.Children.Add(button);
        }
        if (methods.Children.Count > 0) _filterRow.Children.Add(methods);
        var types = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (type, key) in new[] { ("movie", "web.admin.activity.filter.movies"), ("series", "web.admin.activity.filter.series") })
        {
            var text = AdminText.T(key);
            var toggle = FilterSegment(A.Text(text, 12, A.Foreground), text, _list.TypeFilter == type);
            toggle.Click += (_, _) => { _list.ToggleType(type); RefreshListViews(); }; types.Children.Add(toggle);
        }
        _filterRow.Children.Add(types);
        var nodes = AdminActivityListState.RouteNodeCounts(_snapshot.Sessions);
        if (nodes.Count > 1)
        {
            var node = A.Outline(AdminText.T("web.admin.activity.summary.byNode"), "server");
            var menu = new MenuFlyout();
            foreach (var item in nodes)
            {
                var option = new ToggleMenuFlyoutItem { Text = item.Node.Label + " " + item.Node.Name + " " + AdminText.FormatNumber(item.Count), IsChecked = _list.NodeFilter == item.Node.Key };
                option.Click += (_, _) => { _list.ToggleNode(item.Node.Key); RefreshListViews(); }; menu.Items.Add(option);
            }
            node.Flyout = menu; _filterRow.Children.Add(node);
        }
        if (_list.ActiveFilterCount > 0)
        {
            var clear = A.Plain(A.Text(AdminText.T("web.admin.activity.filter.clear"), 12, A.Muted), AdminText.T("web.admin.activity.filter.clear"), new Thickness(4));
            clear.Click += (_, _) => { _list.ClearFilters(); RefreshListViews(); }; _filterRow.Children.Add(clear);
        }
    }

    private static Button FilterSegment(UIElement content, string name, bool selected)
    {
        var button = A.Plain(content, name, new Thickness(8, 4, 8, 4));
        button.CornerRadius = new CornerRadius(6); button.Background = A.Solid(selected ? A.Primary : A.Surface, selected ? 0.15 : 0.5);
        button.BorderBrush = A.Solid(selected ? A.Primary : A.Border, 0.4); button.BorderThickness = new Thickness(1);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(button, selected ? "pressed" : "");
        return button;
    }

    private TextBlock? _filterStatus;

    private void RenderFilterStatus()
    {
        if (_filterStatus != null) _filterHost.Children.Remove(_filterStatus);
        _filterStatus = null;
        if (!_list.ShowsFilterStatus) return;
        var shown = _list.Apply(_snapshot.Sessions).Count;
        _filterStatus = A.Text(AdminActivityListState.ShowingText(shown, _snapshot.Sessions.Count), 11, A.Muted);
        _filterStatus.Margin = new Thickness(0, 8, 0, 0);
        _filterHost.Children.Add(_filterStatus);
    }

    private void RefreshListViews()
    {
        RenderFilterRow();
        RenderFilterStatus();
        RenderTable();
    }
}
