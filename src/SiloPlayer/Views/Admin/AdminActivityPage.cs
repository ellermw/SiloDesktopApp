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
/// routing-node summary, local search/filters/sort, a 6-column table at container
/// width ≥ 977px + 4rem (stacked rows with a Sort row below), per-row details,
/// on-demand FFmpeg console and session actions. Every action captures the
/// current <see cref="MonitorAuthorityLease"/> before any dialog and publishes
/// only while that lease is current.
/// </summary>
public sealed partial class AdminActivityPage : Page
{
    /// <summary>977px of column minimums plus 4rem of gaps/padding and the two table borders.</summary>
    public const double WideTableMinWidth = 977 + 64 + 2;
    private static readonly TimeSpan RefreshSpinnerMinimum = TimeSpan.FromSeconds(1);

    private readonly ServerActivityStore? _store;
    private readonly AdminMonitorAuthority? _authority;
    private readonly AdminMonitorApi? _api;
    private readonly ToastService? _toasts;
    private readonly AdminActivityListState _list = new();
    private readonly AdminActivityIpLookup? _ipLookup;
    private readonly Dictionary<string, RowState> _rowStates = new(StringComparer.Ordinal);
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _page = new() { Spacing = 20 };
    private readonly StackPanel _header = new();
    private readonly StackPanel _ipLookupHost = new();
    private readonly StackPanel _summaryHost = new();
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
        _page.Children.Add(_ipLookupHost);
        _page.Children.Add(_summaryHost);
        _page.Children.Add(_filterHost);
        _page.Children.Add(_tableHost);
        _page.Padding = new Thickness(0, 0, 0, 32);
        _tableHost.SizeChanged += (_, e) =>
        {
            var wasWide = _tableWidth >= WideTableMinWidth;
            _tableWidth = e.NewSize.Width;
            if (wasWide != _tableWidth >= WideTableMinWidth) RenderTable();
        };
        SizeChanged += (_, _) =>
        {
            var width = ActivityViewportWidth;
            var band = width >= 1024 ? 2 : width >= 640 ? 1 : 0;
            _page.Spacing = width >= 1024 ? 24 : 20;
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
    public bool IsWideTable => _tableWidth >= WideTableMinWidth;

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
        _header.Children.Clear();
        _ipLookupHost.Children.Clear();
        _summaryHost.Children.Clear();
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
        // Forget per-row state for sessions that ended.
        foreach (var gone in _rowStates.Keys.Except(snapshot.Sessions.Select(s => s.SessionId)).ToArray())
            _rowStates.Remove(gone);
        if (!snapshot.IsActingAdmin || !snapshot.SessionsLoaded)
        {
            _header.Children.Clear();
            _ipLookupHost.Children.Clear();
            _summaryHost.Children.Clear();
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
        var sessions = _snapshot.Sessions;
        var actions = new LogsWrapPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        if (sessions.Count > 0 && _snapshot.SessionsError == null) actions.Children.Add(A.LiveBadge(AdminActivityListState.LiveCount(sessions.Count), withRadioIcon: true));

        var refreshContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        refreshContent.Children.Add(_refreshing ? A.Spinner(14, A.Foreground) : A.Icon("refresh-cw", 14, A.Foreground));
        refreshContent.Children.Add(A.Text(AdminText.T(_refreshing ? "web.admin.activity.page.refreshing" : "web.admin.activity.page.refresh"), 12, A.Foreground, FontWeights.Medium));
        var refresh = A.Outline("", null);
        refresh.Content = refreshContent;
        refresh.MinWidth = 132;
        refresh.IsEnabled = !_refreshing;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(refresh, AdminText.T(_refreshing ? "web.admin.activity.page.refreshing" : "web.admin.activity.page.refresh"));
        refresh.Click += (_, _) => _ = RefreshAsync();
        actions.Children.Add(refresh);
        var status = new StackPanel { MinWidth = 140, Padding = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var heading = A.Eyebrow(AdminText.T("web.admin.activity.realtime.heading"), A.Muted, 100);
        heading.HorizontalAlignment = HorizontalAlignment.Right;
        status.Children.Add(heading);
        var state = A.Text(_snapshot.Connection switch
        {
            AdminRealtimeConnectionState.Live => AdminText.T("web.admin.activity.realtime.live"),
            AdminRealtimeConnectionState.Connecting => AdminText.T("web.admin.activity.realtime.connecting"),
            _ => AdminText.T("web.admin.activity.realtime.disconnected"),
        }, 12, A.Foreground);
        state.HorizontalAlignment = HorizontalAlignment.Right;
        status.Children.Add(state);
        actions.Children.Add(status);
        _header.Children.Add(new LogsPageHeader(AdminText.T("web.admin.activity.page.title"), _snapshot.SessionsError == null ? AdminActivityListState.Subtitle(sessions) : "", actions) { Margin = new Thickness(0) });
        _header.Margin = new Thickness(0, 0, 0, 12);
    }

    // ── IP Lookup ────────────────────────────────────────────

    private void RenderIpLookup()
    {
        _ipLookupHost.Children.Clear();
        if (!_snapshot.SessionsLoaded || (_snapshot.SessionsError != null && _snapshot.Sessions.Count == 0)) return;
        var summaryLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { A.Icon(_ipOpen ? "chevron-down" : "chevron-right", 12, A.Foreground), A.Text(AdminText.T("web.admin.activity.ipLookup.heading"), 14, A.Foreground, FontWeights.Medium) } };
        var summary = A.Plain(summaryLabel, null, new Thickness(16, 12, 16, 12));

        summary.HorizontalAlignment = HorizontalAlignment.Stretch;
        summary.HorizontalContentAlignment = HorizontalAlignment.Left;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(summary, _ipOpen ? "expanded" : "collapsed");
        summary.Click += (_, _) => { _ipOpen = !_ipOpen; RenderIpLookup(); };
        var body = new StackPanel { Children = { summary } };
        if (_ipOpen)
        {
            var form = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 0, 16, 16) };
            if (_ipBox.Parent is Panel previousForm) previousForm.Children.Remove(_ipBox);
            form.Children.Add(_ipBox);
            var submit = A.Outline(AdminText.T("web.admin.activity.ipLookup.submit"), "search");
            submit.IsEnabled = _ipBox.Text.Trim().Length > 0;
            submit.Click += (_, _) => RunIpLookup(_ipBox.Text);
            form.Children.Add(submit);
            body.Children.Add(form);
            if (_ipLookup is { ActiveIp.Length: > 0 } lookup) body.Children.Add(IpResults(lookup));
        }
        _ipLookupHost.Children.Add(A.Panel(body, new Thickness(0)));
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
        if (_ipLookupHost.Children.FirstOrDefault() is not Border { Child: StackPanel body } ||
            body.Children.Count < 2 || body.Children[1] is not StackPanel form || form.Children.Count < 2 ||
            form.Children[1] is not Button submit) return;
        submit.IsEnabled = _ipBox.Text.Trim().Length > 0;
    }

    /// <summary>Clicking a row's IP fills the form, opens the panel and runs the lookup.</summary>
    private void RunIpLookup(string ip)
    {
        var trimmed = ip.Trim();
        if (trimmed.Length == 0 || _ipLookup == null || _lease is not { } lease) return;
        _ipBox.Text = trimmed;
        _ipOpen = true;
        _ = _ipLookup.StartAsync(lease, trimmed);
        RenderIpLookup();
        _ipLookupHost.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
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

    private void RenderSummary()
    {
        _summaryHost.Children.Clear();
        var sessions = _snapshot.Sessions;
        if (sessions.Count == 0) return;
        var methods = AdminActivityListState.MethodCounts(sessions);
        var panel = new StackPanel();
        panel.Children.Add(A.Eyebrow(AdminText.T("web.admin.activity.summary.methodHeading"), A.Muted, 100));
        var bar = new Grid { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 8, 0, 0) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, AdminText.T("web.admin.activity.summary.methodDistribution"));
        for (var i = 0; i < methods.Count; i++)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(methods[i].Count, GridUnitType.Star) });
            var segment = new Border { Background = A.Solid(A.ToneColor(methods[i].Meta.Tone)) };
            ToolTipService.SetToolTip(segment, AdminText.T("web.admin.activity.summary.methodSegment", new { method = methods[i].Meta.Label, count = methods[i].Count }));
            Grid.SetColumn(segment, i);
            bar.Children.Add(segment);
        }
        panel.Children.Add(bar);
        var legendPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var method in methods)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = A.Solid(A.ToneColor(method.Meta.Tone)), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(A.Text(method.Meta.Label, 11, A.Foreground, FontWeights.Medium));
            content.Children.Add(A.Text(method.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), 11, A.Muted));
            var button = A.Plain(content, AdminText.T("web.admin.activity.summary.methodFilter", new { method = method.Meta.Label, count = method.Count }));
            ToolTipService.SetToolTip(button, method.Meta.Description);
            button.Opacity = _list.MethodFilter != null && _list.MethodFilter != method.Meta.Key ? 0.3 : 1;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(button, _list.MethodFilter == method.Meta.Key ? "pressed" : "");
            var key = method.Meta.Key;
            button.Click += (_, _) => { _list.ToggleMethod(key); RefreshListViews(); };
            legendPanel.Children.Add(button);
        }
        panel.Children.Add(legendPanel);

        var nodes = AdminActivityListState.RouteNodeCounts(sessions);
        if (nodes.Count > 1)
        {
            var nodeSection = new StackPanel
            {
                Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(0, 12, 0, 0),
                BorderBrush = A.Solid(A.Border), BorderThickness = new Thickness(0, 1, 0, 0),
            };
            nodeSection.Children.Add(A.Eyebrow(AdminText.T("web.admin.activity.summary.byNode"), A.Muted, 100));
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var node in nodes.OrderByDescending(n => n.Count))
            {
                var selected = _list.NodeFilter == node.Node.Key;
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                content.Children.Add(A.Text(node.Node.Label, 11, A.Muted, FontWeights.Medium));
                content.Children.Add(A.Text(node.Node.Name, 11, selected ? A.Primary : A.Foreground, FontWeights.Medium));
                content.Children.Add(A.Text(node.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), 11, A.Muted));
                var chip = A.Plain(content, $"{node.Node.Label} {node.Node.Name} {node.Count}", new Thickness(10, 4, 10, 4));
                chip.CornerRadius = new CornerRadius(6);
                chip.BorderThickness = new Thickness(1);
                chip.BorderBrush = selected ? A.Solid(A.Primary, 0.4) : A.Solid(A.Border);
                chip.Background = selected ? A.Solid(A.Primary, 0.10) : A.Brush("SurfaceBrush");
                chip.Opacity = _list.NodeFilter != null && !selected ? 0.3 : 1;
                var key = node.Node.Key;
                chip.Click += (_, _) => { _list.ToggleNode(key); RefreshListViews(); };
                chips.Children.Add(chip);
            }
            nodeSection.Children.Add(chips);
            panel.Children.Add(nodeSection);
        }
        _summaryHost.Children.Add(A.Panel(panel, new Thickness(16)));
    }

    // ── Filters ──────────────────────────────────────────────

    private Grid? _filterRow;
    private Button? _clearSearch;
    private readonly StackPanel _filterToggles = new() { Orientation = Orientation.Horizontal, Spacing = 8 };

    /// <summary>The search box is created once so typing never loses focus; only toggles re-render.</summary>
    private void RenderFilterRow()
    {
        if (_filterRow == null)
        {
            _filterRow = new Grid { ColumnSpacing = 8 };
            _filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
            _filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var searchHost = new Grid();
            searchHost.Children.Add(_searchBox);
            var icon = A.Icon("search", 14, A.Muted);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.Margin = new Thickness(12, 0, 0, 0);
            searchHost.Children.Add(icon);
            _clearSearch = A.Plain(A.Icon("x", 14, A.Muted), AdminText.T("web.admin.activity.filter.clear"), new Thickness(4));
            _clearSearch.HorizontalAlignment = HorizontalAlignment.Right;
            _clearSearch.Margin = new Thickness(0, 0, 6, 0);
            _clearSearch.Click += (_, _) => _searchBox.Text = "";
            searchHost.Children.Add(_clearSearch);
            _filterRow.Children.Add(searchHost);
            Grid.SetColumn(_filterToggles, 1);
            _filterRow.Children.Add(_filterToggles);
            _filterHost.Children.Insert(0, _filterRow);
        }
        _clearSearch!.Visibility = _list.Search.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _filterToggles.Children.Clear();
        var types = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (type, key) in new[] { ("movie", "web.admin.activity.filter.movies"), ("series", "web.admin.activity.filter.series") })
        {
            var selected = _list.TypeFilter == type;
            var toggle = A.Plain(A.Text(AdminText.T(key), 11, selected ? A.Primary : A.Muted, FontWeights.Medium), null, new Thickness(10, 6, 10, 6));
            toggle.CornerRadius = new CornerRadius(6);
            toggle.BorderThickness = new Thickness(1);
            toggle.BorderBrush = selected ? A.Solid(A.Primary, 0.4) : A.Solid(A.Border);
            toggle.Background = selected ? A.Solid(A.Primary, 0.10) : A.Brush("SurfaceBrush");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(toggle, selected ? "pressed" : "");
            toggle.Click += (_, _) => { _list.ToggleType(type); RefreshListViews(); };
            types.Children.Add(toggle);
        }
        _filterToggles.Children.Add(types);
        if (_list.ActiveFilterCount > 0)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            content.Children.Add(A.Icon("x", 12, A.Muted));
            content.Children.Add(A.Text(AdminText.T("web.admin.activity.filter.clear"), 11, A.Muted));
            var clearAll = A.Plain(content, null, new Thickness(4));
            clearAll.Click += (_, _) => { _list.ClearFilters(); RefreshListViews(); };
            _filterToggles.Children.Add(clearAll);
        }
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
        RenderSummary();
        RenderFilterRow();
        RenderFilterStatus();
        RenderTable();
    }
}
