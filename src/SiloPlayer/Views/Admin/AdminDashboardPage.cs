using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Admin Monitor → Dashboard. Everything on the page belongs to one captured
/// <see cref="MonitorAuthorityLease"/>: data, the layout owner, realtime
/// subscriptions, timers and dialogs. When authority changes (role, profile,
/// account, server or API context — including loss and regain of admin) the
/// whole set retires without publishing, flushing or transferring anything,
/// and a fresh set starts only if the new principal is still an acting admin.
/// Page changes swap in place; nothing slides or fades in.
/// </summary>
public sealed partial class AdminDashboardPage : Page
{
    private readonly AdminMonitorAuthority? _authority;
    private readonly AdminMonitorApi? _api;
    private readonly ServerActivityStore? _activity;
    private readonly SiloApiClient? _client;
    private readonly ToastService? _toast;
    private readonly IDashboardLayoutCache _cache;
    private readonly TimeProvider _time;
    private readonly Grid _root = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
    private readonly StackPanel _content = new();
    private readonly ContentControl _header = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly ContentControl _attention = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly ContentControl _customizeBar = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly ContentControl _saveBlocked = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly TextBlock _live = new() { Height = 0, Opacity = 0 };
    private readonly DashboardGridPanel _grid = new();
    private readonly DashboardGridHost _gridHost;
    private readonly DashboardAddSheet _sheet;
    private readonly Dictionary<string, DashboardWidgetFrame> _frames = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _pageMounts = [];
    private readonly HashSet<ContentDialog> _dialogs = [];
    private MonitorAuthorityLease? _lease;
    private DashboardDataStore? _store;
    private DashboardLayoutSync? _sync;
    private ServerActivityDashboardFeed? _feed;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _labelTimer;
    private bool _active;
    private bool _sheetOpen;
    private bool _hostVisible = true;
    private bool _hostFocused = true;
    private double _viewportWidth = 1440;

    public AdminDashboardPage() : this(
        App.Services.GetService<AdminMonitorAuthority>(),
        App.Services.GetService<AdminMonitorApi>(),
        App.Services.GetService<ServerActivityStore>(),
        App.Services.GetService<SiloApiClient>(),
        App.Services.GetService<ToastService>(),
        App.Services.GetService<IDashboardLayoutCache>() ?? DefaultLayoutCache(),
        App.Services.GetService<TimeProvider>() ?? TimeProvider.System)
    {
    }

    /// <summary>The account-scoped arrangement cache beside the app settings; it never holds credentials.</summary>
    private static IDashboardLayoutCache DefaultLayoutCache() => new DashboardFileLayoutCache(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "admin-dashboard-layout.v1.json"));

    /// <summary>Test/host seam: explicit services, cache and clock.</summary>
    internal AdminDashboardPage(AdminMonitorAuthority? authority, AdminMonitorApi? api, ServerActivityStore? activity, SiloApiClient? client,
        ToastService? toast, IDashboardLayoutCache cache, TimeProvider time)
    {
        InitializeComponent();
        _authority = authority;
        _api = api;
        _activity = activity;
        _client = client;
        _toast = toast;
        _cache = cache;
        _time = time;
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        Transitions = null;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _gridHost = new DashboardGridHost(_grid, _scroll, _live, () => _sync, DispatcherQueue);
        _gridHost.AddCompleted += () => SetSheetOpen(false);
        _sheet = new DashboardAddSheet(_gridHost, id => _sync?.AddWidget(id), () => SetSheetOpen(false));
        AutomationProperties.SetLiveSetting(_live, AutomationLiveSetting.Polite);
        _content.Children.Add(_header);
        _content.Children.Add(_attention);
        _content.Children.Add(_customizeBar);
        _content.Children.Add(_saveBlocked);
        _root.Children.Add(_live);
        _content.Children.Add(_grid);
        _scroll.Content = _content;
        _root.Children.Add(_scroll);
        _root.Children.Add(_sheet);
        Content = _root;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => UpdateViewport();
    }

    /// <summary>The shell passes an <see cref="AdminRouteRequest"/>; the dashboard has no URL state.</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = e.Parameter as AdminRouteRequest;
    }

    /// <summary>Optional compact session controls for Recent Activity, supplied by the Activity surface.</summary>
    public Func<DashboardSession, FrameworkElement?>? SessionActionsFactory { get; set; }

    /// <summary>Viewport width override (the window's content width); defaults to XamlRoot.</summary>
    public double? ViewportWidthOverride
    {
        get => _viewportOverride;
        set { _viewportOverride = value; UpdateViewport(); }
    }
    private double? _viewportOverride;

    // Inspection surface for native state fixtures; read-only views of the current authority set.
    public DashboardDataStore? Store => _store;
    public DashboardLayoutSync? LayoutSync => _sync;
    public DashboardGridMode GridMode => _grid.Mode;
    public bool IsSheetOpen => _sheetOpen;
    public bool IsSaveBlockedShown => _saveBlocked.Visibility == Visibility.Visible;
    public bool IsAttentionShown => _attention.Visibility == Visibility.Visible;

    /// <summary>Placed widgets in layout order with their arranged size.</summary>
    public IReadOnlyList<(string Id, double Width, double Height)> PlacedWidgets()
        => _frames.Values.OrderBy(frame => frame.Order).Select(frame => (frame.Id, frame.ActualWidth, frame.ActualHeight)).ToArray();

    /// <summary>The Customize/Done toggle and the Add widget button, for fixtures and keyboard parity.</summary>
    public void ToggleCustomize() => ToggleCustomizing();
    public void OpenAddSheet() => SetSheetOpen(_sync?.IsCustomizing == true);

    /// <summary>Window visibility/focus; the 60s and resource polls only run while visible and focused.</summary>
    public void SetHostActivity(bool visible, bool focused)
    {
        _hostVisible = visible;
        _hostFocused = focused;
        _store?.SetActivity(visible, focused);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_active) return;
        _active = true;
        if (_authority != null) _authority.AvailabilityChanged += OnAvailabilityChanged;
        if (App.MainWindowInstance is { } window) window.Activated += OnWindowActivated;
        if (XamlRoot != null) XamlRoot.Changed += OnXamlRootChanged;
        _labelTimer = DispatcherQueue.CreateTimer();
        _labelTimer.Interval = TimeSpan.FromSeconds(30);
        _labelTimer.Tick += (_, _) => RenderHeader();
        _labelTimer.Start();
        UpdateViewport();
        StartAuthority();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e) => await SuspendMonitorAsync();

    internal async Task SuspendMonitorAsync()
    {
        if (!_active) return;
        _active = false;
        if (_authority != null) _authority.AvailabilityChanged -= OnAvailabilityChanged;
        if (App.MainWindowInstance is { } window) window.Activated -= OnWindowActivated;
        if (XamlRoot != null) XamlRoot.Changed -= OnXamlRootChanged;
        _labelTimer?.Stop();
        _labelTimer = null;
        await RetireAsync(departing: true);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _hostFocused = args.WindowActivationState != WindowActivationState.Deactivated;
        _store?.SetActivity(_hostVisible, _hostFocused);
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        _hostVisible = sender.IsHostVisible;
        _store?.SetActivity(_hostVisible, _hostFocused);
        UpdateViewport();
    }

    private void OnAvailabilityChanged()
    {
        // Raised on any thread. Retire first; recapture only on the dispatcher.
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (!_active) return;
            if (_lease != null && _lease.IsCurrent) return;
            await RetireAsync(departing: false);
            StartAuthority();
        });
    }

    private void StartAuthority()
    {
        if (!_active || _authority == null || _api == null || !_authority.IsAvailable) { RenderAll(); return; }
        try
        {
            _lease = _authority.Capture();
        }
        catch (OperationCanceledException)
        {
            RenderAll();
            return;
        }
        _feed = _activity != null ? new ServerActivityDashboardFeed(_activity) : null;
        _store = new DashboardDataStore(_api, _lease, _feed, _time);
        _store.Changed += OnStoreChanged;
        _store.Notice += OnNotice;
        _store.SetActivity(_hostVisible, _hostFocused);
        _sync = new DashboardLayoutSync(_lease, new AdminMonitorDashboardLayoutServer(_api), _cache, _client?.BaseUrl ?? "", _time);
        _sync.Changed += OnLayoutChanged;
        _sync.SaveUnconfirmed += () => OnNotice(DashboardNoticeKind.Error, DashboardText.T("web.admin.dashboard.hooks.dashboardLayout.saveUnconfirmed"));
        _sync.ResetUnconfirmed += () => OnNotice(DashboardNoticeKind.Error, DashboardText.T("web.admin.dashboard.hooks.dashboardLayout.resetUnconfirmed"));
        _store.Start();
        foreach (var key in new[] { DashboardDataStore.Stats, DashboardDataStore.Sessions, DashboardDataStore.Libraries, DashboardDataStore.Users })
            _pageMounts.Add(_store.Mount(key));
        _pageMounts.AddRange(_store.MountAttention());
        _ = _sync.LoadAsync(_lease.Token);
        RenderAll();
    }

    /// <summary>
    /// Retires the current authority set. A departure sends the queued layout
    /// write under the still-valid lease first; an authority change sends nothing.
    /// </summary>
    private async Task RetireAsync(bool departing)
    {
        _gridHost.EndDrag();
        foreach (var dialog in _dialogs.ToArray()) { try { dialog.Hide(); } catch (Exception) { } }
        _dialogs.Clear();
        SetSheetOpen(false);
        var sync = _sync;
        var store = _store;
        var lease = _lease;
        var feed = _feed;
        _sync = null;
        _store = null;
        _lease = null;
        _feed = null;
        foreach (var mount in _pageMounts) mount.Dispose();
        _pageMounts.Clear();
        foreach (var frame in _frames.Values) _grid.Children.Remove(frame);
        _frames.Clear();
        if (store != null)
        {
            store.Changed -= OnStoreChanged;
            store.Notice -= OnNotice;
            store.Dispose();
        }
        feed?.Dispose();
        if (sync != null)
        {
            sync.Changed -= OnLayoutChanged;
            if (departing && lease != null && lease.IsCurrent)
            {
                try { await sync.DepartAsync(); } catch (Exception) { }
            }
            sync.Dispose();
        }
        lease?.Dispose();
        RenderAll();
    }

    private void OnNotice(DashboardNoticeKind kind, string message)
    {
        if (_lease is not { IsCurrent: true } || _toast == null) return;
        if (kind == DashboardNoticeKind.Success) _toast.Success(message);
        else _toast.Error(message);
    }

    private void OnStoreChanged(string key)
    {
        if (key is "page" or DashboardDataStore.Stats or DashboardDataStore.Sessions or DashboardDataStore.Libraries or DashboardDataStore.Users or "actions")
            RenderHeader();
        if (key is "attention" or DashboardDataStore.AttentionSettings or DashboardDataStore.AttentionBranding or DashboardDataStore.AttentionSensitive
            or DashboardDataStore.AttentionSearch or DashboardDataStore.Status)
        {
            RenderAttention();
            RenderHeader();
        }
    }

    private void OnLayoutChanged()
    {
        SyncFrames();
        RenderCustomizeBar();
        RenderSaveBlocked();
        RenderHeader();
        if (_sheetOpen && _sync != null) _sheet.Populate(_sync.HiddenWidgets);
    }

    private void UpdateViewport()
    {
        var width = ViewportWidthOverride ?? XamlRoot?.Size.Width ?? ActualWidth;
        if (width <= 0) width = ActualWidth;
        if (width <= 0) return;
        var changedMode = DashboardGridPlacement.ModeFor(width) != DashboardGridPlacement.ModeFor(_viewportWidth);
        var changedScanColumns = (width >= 768) != (_viewportWidth >= 768);
        _viewportWidth = width;
        _content.Spacing = width >= DashboardGridPlacement.LargeBreakpoint ? 32 : 24;
        // AdminShellPage supplies the gutters, top/bottom padding, 240px sidebar offset and 1560px frame.
        var mode = DashboardGridPlacement.ModeFor(width);
        _grid.SetMode(mode);
        if (changedMode || changedScanColumns)
        {
            foreach (var frame in _frames.Values)
            {
                frame.SetCustomizing(_sync?.IsCustomizing == true);
                frame.View.Refresh();
            }
            RenderAll();
        }
    }

    private void RenderAll()
    {
        RenderHeader();
        RenderAttention();
        RenderCustomizeBar();
        RenderSaveBlocked();
        SyncFrames();
    }

    // ---- Header ---------------------------------------------------------

    private void RenderHeader()
    {
        var store = _store;
        var sync = _sync;
        var small = _viewportWidth < DashboardGridPlacement.SmallBreakpoint;
        var attention = store?.Attention();
        var hiddenCount = attention?.HiddenCount ?? 0;
        var group = new StackPanel { Orientation = small ? Orientation.Vertical : Orientation.Horizontal, Spacing = 8 };
        if (hiddenCount > 0)
        {
            var chip = new Button
            {
                Padding = new Thickness(10, 4, 10, 4), MinHeight = 0, CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1),
                IsEnabled = store?.AttentionSaving != true, HorizontalAlignment = HorizontalAlignment.Left,
                Content = Row(6, Icon("triangle-alert", 14, DashboardUi.Warning), new TextBlock
                {
                    Text = DashboardText.T("web.admin.dashboard.page.hiddenNoticesChip", ("count", (long)hiddenCount)), FontSize = 12, FontWeight = Medium,
                }),
            };
            chip.Resources["ButtonBackground"] = B(DashboardUi.Warning, 0.08);
            chip.Resources["ButtonBackgroundPointerOver"] = B(DashboardUi.Warning, 0.15);
            chip.Resources["ButtonBackgroundPressed"] = B(DashboardUi.Warning, 0.15);
            chip.Resources["ButtonForeground"] = B(DashboardUi.Warning);
            chip.Resources["ButtonForegroundPointerOver"] = B(DashboardUi.Warning);
            chip.Resources["ButtonBorderBrush"] = B(DashboardUi.Warning, 0.35);
            chip.Resources["ButtonBorderBrushPointerOver"] = B(DashboardUi.Warning, 0.35);
            chip.Background = B(DashboardUi.Warning, 0.08);
            chip.Foreground = B(DashboardUi.Warning);
            chip.BorderBrush = B(DashboardUi.Warning, 0.35);
            AutomationProperties.SetName(chip, DashboardText.T("web.admin.dashboard.page.showHiddenNoticesLabel", ("count", (long)hiddenCount)));
            ToolTipService.SetToolTip(chip, DashboardText.T("web.admin.dashboard.page.showHiddenNoticesHint"));
            chip.Click += (_, _) => _store?.RestoreAllAttention();
            group.Children.Add(chip);
        }
        if (store?.LastUpdatedAt is { } updated && store.HasDashboardData)
        {
            var label = Text(DashboardFormat.UpdatedLabel(store.Now, updated), 12, color: MutedForeground);
            label.VerticalAlignment = VerticalAlignment.Center;
            group.Children.Add(label);
        }
        var pending = store?.ManualRefreshPending == true;
        var refresh = OutlineButton(pending ? DashboardText.T("web.admin.dashboard.page.refreshing") : DashboardText.T("web.admin.dashboard.page.refresh"),
            "refresh-cw", async (_, _) => { if (_store is { } s) await s.RefreshDashboardAsync(manual: true); });
        refresh.MinWidth = 132;
        refresh.IsEnabled = store != null && !pending;
        AutomationProperties.SetItemStatus(refresh, pending ? "busy" : "");
        if (pending && refresh.Content is StackPanel { Children.Count: > 0 } refreshContent && refreshContent.Children[0] is FrameworkElement spinner)
            Spin(spinner);
        var customizing = sync?.IsCustomizing == true;
        var customize = OutlineButton(customizing ? DashboardText.T("common.done") : DashboardText.T("web.admin.dashboard.page.customize"),
            "layout-dashboard", (_, _) => ToggleCustomizing());
        customize.IsEnabled = sync != null;
        AutomationProperties.SetItemStatus(customize, customizing ? "pressed" : "");
        var libraries = store?.Get(DashboardDataStore.Libraries).As<IReadOnlyList<DashboardLibrary>>() ?? [];
        var scanAll = PrimaryButton(DashboardText.T("web.admin.dashboard.page.scanAllLibraries"), "scan-line", async (_, _) =>
        {
            if (_store is { } s && (s.Get(DashboardDataStore.Libraries).As<IReadOnlyList<DashboardLibrary>>()?.Count ?? 0) > 0)
                await s.ScanAllLibrariesAsync();
        });
        scanAll.IsEnabled = store != null && !store.ScanAllPending && libraries.Count > 0;
        foreach (var button in new[] { refresh, customize, scanAll })
        {
            if (small) { button.HorizontalAlignment = HorizontalAlignment.Stretch; button.MinHeight = 44; }
        }
        group.Children.Add(refresh);
        group.Children.Add(customize);
        var actions = small ? Column(8, group, scanAll) : Row(8, group, scanAll);
        actions.HorizontalAlignment = small ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        if (small) scanAll.HorizontalAlignment = HorizontalAlignment.Stretch;

        var header = new LogsPageHeader(DashboardText.T("web.admin.dashboard.page.title"), DashboardText.T("web.admin.dashboard.page.subtitle"), actions);
        _header.Content = header;
    }

    private static void Spin(FrameworkElement element)
    {
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        var rotate = new RotateTransform();
        element.RenderTransform = rotate;
        var animation = new DoubleAnimation { From = 0, To = 360, Duration = new Duration(TimeSpan.FromSeconds(1)), RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(animation, rotate);
        Storyboard.SetTargetProperty(animation, "Angle");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void ToggleCustomizing()
    {
        if (_sync is not { } sync) return;
        var next = !sync.IsCustomizing;
        if (!next) _gridHost.EndDrag();
        sync.SetCustomizing(next);
        if (!next) SetSheetOpen(false);
        foreach (var frame in _frames.Values) frame.SetCustomizing(next);
        _grid.InvalidateMeasure();
    }

    // ---- Attention, customize bar, save blocked ---------------------------

    private void RenderAttention()
    {
        var attention = _store?.Attention();
        _attention.Content = attention is { } a
            ? DashboardAttentionNotice.Create(a.Visible, _store!.AttentionSaving, item => _store?.DismissAttention(item), _grid.Mode, _viewportWidth)
            : null;
        _attention.Visibility = _attention.Content == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderCustomizeBar()
    {
        if (_sync is not { IsCustomizing: true })
        {
            _customizeBar.Content = null;
            _customizeBar.Visibility = Visibility.Collapsed;
            return;
        }
        var bar = new Microsoft.UI.Xaml.Controls.Grid { ColumnSpacing = 16 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var add = OutlineButton(DashboardText.T("web.admin.dashboard.customize.addWidget"), "plus", (_, _) => SetSheetOpen(true));
        bar.Children.Add(add);
        var hint = Text(DashboardText.T("web.admin.dashboard.customize.hint"), 12, color: MutedForeground, wrap: true);
        hint.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(hint, 1);
        bar.Children.Add(hint);
        var reset = TextLink(DashboardText.T("web.admin.dashboard.customize.resetLayout"), 12, (_, _) => _sync?.ResetLayout());
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(reset, 2);
        bar.Children.Add(reset);
        _customizeBar.Content = new Border
        {
            Background = SurfacePanelSubtle, BorderBrush = B(DashboardUi.BorderColor, 0.62), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 10, 16, 10), Child = bar,
        };
        _customizeBar.Visibility = Visibility.Visible;
    }

    private void RenderSaveBlocked()
    {
        if (_sync is not { ServerSaveBlocked: true })
        {
            _saveBlocked.Content = null;
            _saveBlocked.Visibility = Visibility.Collapsed;
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(new TextBlock { Text = DashboardText.T("web.admin.dashboard.page.saveBlocked"), FontSize = 14, Foreground = B(DashboardUi.Foreground), TextWrapping = TextWrapping.WrapWholeWords, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(OutlineButton(DashboardText.T("web.admin.dashboard.page.reloadServerLayout"), null, async (_, _) =>
        {
            if (_sync is { } sync && _lease is { } lease) await sync.ReloadServerLayoutAsync(lease.Token);
        }));
        AutomationProperties.SetLiveSetting(row, AutomationLiveSetting.Polite);
        _saveBlocked.Content = row;
        _saveBlocked.Visibility = Visibility.Visible;
    }

    private void SetSheetOpen(bool open)
    {
        _sheetOpen = open && _sync != null;
        _sheet.Visibility = _sheetOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_sheetOpen)
        {
            _sheet.Populate(_sync!.HiddenWidgets);
            _sheet.Focus(FocusState.Programmatic);
        }
    }

    // ---- Grid -----------------------------------------------------------

    private void SyncFrames()
    {
        if (_sync is not { } sync || _store is not { } store)
        {
            foreach (var frame in _frames.Values) _grid.Children.Remove(frame);
            _frames.Clear();
            return;
        }
        var entries = sync.Entries;
        var placed = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _frames.Keys.Where(id => !placed.Contains(id)).ToList())
        {
            _grid.Children.Remove(_frames[id]);
            _frames.Remove(id);
        }
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (DashboardRegistry.Find(entry.Id) is not { } widget) continue;
            if (!_frames.TryGetValue(entry.Id, out var frame))
            {
                DashboardWidgetFrame? created = null;
                var context = new DashboardWidgetContext
                {
                    Store = store,
                    Id = entry.Id,
                    Range = DashboardLayoutDocument.EffectiveRange(entry),
                    SetRange = range => _sync?.SetWidgetRange(entry.Id, range),
                    SetCollapsed = collapsed => created?.SetCollapsed(collapsed),
                    Mode = () => _grid.Mode,
                    ViewportWidth = () => _viewportWidth,
                    Navigate = NavigateFor,
                    SessionActions = SessionActionsFactory ?? BuildSessionActions,
                    ResolveImage = url => string.IsNullOrWhiteSpace(url) ? null : _client?.ResolveServerUrl(url),
                    Root = () => XamlRoot,
                    TrackDialog = Track,
                };
                frame = created = new DashboardWidgetFrame(widget, DashboardWidgetFactory.Create(context), _gridHost);
                frame.SetCustomizing(sync.IsCustomizing);
                _frames[entry.Id] = frame;
                _grid.Children.Add(frame);
            }
            frame.Order = i;
            frame.Span = entry.Span;
            frame.Rows = entry.Rows;
            frame.TabIndex = i;
            frame.View.SetRange(DashboardLayoutDocument.EffectiveRange(entry));
        }
        _grid.InvalidateMeasure();
    }

    /// <summary>Monitor destinations open through the admin shell; only under the current lease.</summary>
    private Action? NavigateFor(DashboardDestination destination) => () =>
    {
        if (_lease is not { IsCurrent: true }) return;
        AdminShellPage.Open(new AdminRouteRequest(destination == DashboardDestination.Logs ? AdminRoute.ServerLog : AdminRoute.Activity));
    };


    private FrameworkElement? BuildSessionActions(DashboardSession session)
    {
        if (_api == null || _lease is not { IsCurrent: true } lease) return null;
        var button = SiloPlayer.Controls.AdminMonitorUi.Plain(
            SiloPlayer.Controls.AdminMonitorUi.Icon("more-horizontal", 16, SiloPlayer.Controls.AdminMonitorUi.Muted),
            AdminText.T("web.admin.sessions.playback.menu.ariaLabel"));
        button.Width = button.Height = 28;
        var menu = new MenuFlyout();
        var pending = false;
        bool Current() => _active && ReferenceEquals(_lease, lease) && lease.IsCurrent;
        async Task<AdminActivityCommandReceipt?> Run(string action, string? message = null)
        {
            if (!Current() || pending) return null;
            if (action is "pause" or "resume" or "message" &&
                !(_activity?.Current.Sessions.Any(s => s.SessionId == session.SessionId && s.HasPlaybackControl) ?? false)) return null;
            pending = true;
            button.IsEnabled = false;
            try
            {
                AdminActivityCommandReceipt? receipt = null;
                var outcome = action == "terminate"
                    ? AdminActivityCommandText.FromTerminate(await _api.TerminateActivitySessionAsync(lease, session.SessionId))
                    : AdminActivityCommandText.FromReceipt(action, receipt = await _api.SendActivityCommandAsync(lease, session.SessionId, action, message));
                if (!Current()) return null;
                _toast?.Success(outcome.Message);
                if (_activity != null) await _activity.RefreshSessionsAsync();
                return receipt;
            }
            catch (Exception error) { if (Current()) _toast?.Error(AdminActivityCommandText.DescribeFailure(action, error)); return null; }
            finally { pending = false; if (Current()) button.IsEnabled = true; }
        }
        void Item(string action, Func<Task> run)
        {
            var item = new MenuFlyoutItem { Text = AdminText.T("web.admin.sessions.playback.action." + action) };
            item.Click += async (_, _) => await run();
            menu.Items.Add(item);
        }
        if (session.HasPlaybackControl)
        {
            var action = session.IsPaused ? "resume" : "pause";
            Item(action, () => Run(action));
        }
        else menu.Items.Add(new MenuFlyoutItem { Text = AdminText.T("web.admin.sessions.playback.unsupportedControl"), IsEnabled = false });
        Item("stop", () => Run("stop"));
        if (session.HasPlaybackControl) Item("message", async () =>
        {
            if (!Current() || XamlRoot == null) return;
            var draft = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96,
                PlaceholderText = AdminText.T("web.admin.sessions.playback.messageDialog.placeholder") };
            var content = new StackPanel { Spacing = 12, Children = {
                SiloPlayer.Controls.AdminMonitorUi.Text(AdminText.T("web.admin.sessions.playback.messageDialog.description", new { name = session.Username }), 14, SiloPlayer.Controls.AdminMonitorUi.Muted, trim: false), draft } };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Content = content,
                Title = AdminText.T("web.admin.sessions.playback.messageDialog.title"),
                PrimaryButtonText = AdminText.T("web.admin.sessions.playback.messageDialog.send"),
                CloseButtonText = AdminText.T("common.cancel"), DefaultButton = ContentDialogButton.Primary };
            if (Application.Current.Resources.TryGetValue("WebUiContentDialogStyle", out var style) && style is Style ds) dialog.Style = ds;
            using var tracking = Track(dialog);
            using var retirement = lease.Token.Register(() => DispatcherQueue.TryEnqueue(() => { draft.Text = ""; dialog.Hide(); }));
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                if (!Current()) return;
                var text = draft.Text.Trim();
                if (text.Length == 0) { args.Cancel = true; _toast?.Error(AdminText.T("web.admin.sessions.playback.toast.messageRequired")); return; }
                var deferral = args.GetDeferral();
                dialog.IsPrimaryButtonEnabled = false;
                try { var receipt = await Run("message", text); args.Cancel = Current() && (receipt == null || receipt.Outcome == "replayed"); }
                finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
            };
            try { await dialog.ShowAsync(); }
            finally { draft.Text = ""; }
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("terminate", () => Run("terminate"));
        button.Flyout = menu;
        return button;
    }

    private IDisposable Track(ContentDialog dialog)
    {
        _dialogs.Add(dialog);
        return new Tracking(() => _dialogs.Remove(dialog));
    }

    private sealed class Tracking(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
