using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using Windows.UI;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The Admin Monitor shell (WebUI <c>AdminLayout</c> + <c>AdminSidebar</c>). It
/// replaces the whole app shell: MainWindow hides its 260px sidebar and phone
/// header while this page owns the frame. Desktop (≥1024): fixed 240px sidebar
/// with logo, scoped search, the Monitor section only (Dashboard, Activity with
/// live badge, Logs topic → Server Log / Client Diagnostics / Settings), build
/// card and Back to App; Server Activity top-right, always shown. Narrow: glass
/// header (menu, ▶ tile, ADMIN eyebrow + title, search, 44px Server Activity)
/// and a 320px drawer. Admin pages load in the inner frame with an
/// <see cref="AdminRouteRequest"/> and swap in place (no entrance animation).
/// </summary>
public sealed partial class AdminShellPage : Page
{
    public const double SidebarWidth = 240;
    public const double DesktopBreakpoint = 1024;
    public const double ContentMaxWidth = 1560;
    public static readonly TimeSpan ServerStatusStaleAfter = TimeSpan.FromSeconds(15);

    /// <summary>Raised whenever the shell changes route; popovers close on it.</summary>
    public static event Action<AdminRoute>? RouteChanged;

    private readonly AdminMonitorAuthority? _authority;
    private readonly AdminMonitorApi? _api;
    private readonly ServerActivityStore? _store;
    private readonly NavigationService? _navigation;
    private readonly SettingsService? _settings;
    private readonly Grid _root = new();
    private readonly Grid _desktopSidebarHost = new() { Width = SidebarWidth, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _narrowHeader = new();
    private readonly TextBlock _narrowTitle = new();
    private readonly Grid _drawerLayer = new() { Visibility = Visibility.Collapsed };
    private readonly Grid _drawerPanelHost = new();
    private readonly Grid _contentColumn = new();
    private readonly Grid _contentHost = new();
    private readonly Frame _frame = new();
    private readonly StackPanel _restartBannerHost = new() { Visibility = Visibility.Collapsed };
    private readonly ServerActivityButton _desktopActivity = new();
    private readonly ServerActivityButton _narrowActivity = new() { ButtonSize = 44 };
    private readonly AdminNavTopicState _topics;
    private MonitorAuthorityLease? _lease;
    private IDisposable? _reads;
    private AdminRoute _route = AdminRoute.Dashboard;
    private AdminRouteRequest _request = new(AdminRoute.Dashboard);
    private AdminIndicatorBuildInfo? _build;
    private bool _buildLoading = true;
    private bool _buildFailed;
    private AdminIndicatorServerStatus? _serverStatus;
    private DateTimeOffset _serverStatusReadAt = DateTimeOffset.MinValue;
    private string? _restartDismissedSignature;
    private int _sessionCount;
    private bool _active;
    private bool _narrow;
    private ContentDialog? _restartDialog;
    private bool _restartPending;

    public AdminShellPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        try
        {
            _authority = App.Services.GetService<AdminMonitorAuthority>();
            _api = App.Services.GetService<AdminMonitorApi>();
            _store = App.Services.GetService<ServerActivityStore>();
            _navigation = App.Services.GetService<NavigationService>();
            _settings = App.Services.GetService<SettingsService>();
        }
        catch (InvalidOperationException) { }
        _topics = new AdminNavTopicState(ReadTopics, WriteTopics, null);
        _frame.Navigated += OnFrameNavigated;
        BuildLayout();
        Content = _root;
        SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width);
        KeyDown += OnKeyDown;
    }

    /// <summary>The route currently shown (tests and MainWindow chrome).</summary>
    public AdminRoute CurrentRoute => _route;

    /// <summary>Opens a Monitor route: in the shell when it is showing, otherwise enters Admin.</summary>
    public static bool Open(AdminRouteRequest request)
    {
        var navigation = App.Services.GetService<NavigationService>();
        if (navigation?.Frame?.Content is AdminShellPage shell)
        {
            shell.Navigate(request);
            return true;
        }
        return navigation?.Navigate(typeof(AdminShellPage), request) == true;
    }

    /// <summary>Back within admin pages first (Alt+Left, mouse back, gamepad B).</summary>
    internal Grid MonitorOverlayRoot => _root;
    private AdminLoggingSettingsPage? _loggingHealthSource;
    private bool _loggingRestartObserved;

    public bool RequiresLeaveConfirmation => _frame.Content is AdminLoggingSettingsPage logging && logging.SettingsHealth.DirtyCount > 0;

    public Task<bool> CanLeaveAsync() => _frame.Content is IAdminLoggingNavigationGuard guard ? guard.CanLeaveAsync() : Task.FromResult(true);

    public bool TryGoBack()
    {
        if (_drawerLayer.Visibility == Visibility.Visible) { CloseDrawer(); return true; }
        if (!_frame.CanGoBack) return false;
        _ = GoBackAsync();
        return true;

        async Task GoBackAsync()
        {
            if (await CanLeaveAsync() && _active && _frame.CanGoBack) _frame.GoBack(new SuppressNavigationTransitionInfo());
        }
    }

    /// <summary>Ctrl+K inside Admin opens the scoped admin search instead of the app search.</summary>
    public bool TryOpenSearch()
    {
        _ = ShowSearchAsync();
        return true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        // RequireAdmin: fail closed for ordinary users, non-primary or unresolved profiles.
        if (_authority?.IsAvailable != true)
        {
            LeaveAdmin();
            return;
        }
        _authority.AvailabilityChanged += OnAvailabilityChanged;
        if (_store != null) _store.Changed += OnStoreChanged;
        Recapture();
        Navigate(e.Parameter as AdminRouteRequest ?? new AdminRouteRequest(AdminRoute.Dashboard));
        ApplyResponsiveLayout(ActualWidth > 0 ? ActualWidth : App.MainWindowInstance?.Bounds.Width ?? 1440);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // An outer Frame departure need not navigate or unload its nested Frame.
        switch (_frame.Content)
        {
            case AdminActivityPage page: page.SuspendMonitor(); break;
            case AdminDashboardPage page: _ = page.SuspendMonitorAsync(); break;
            case AdminLogsPage page: page.SuspendMonitor(); break;
            case AdminDiagnosticsPage page: page.SuspendMonitor(); break;
            case AdminLoggingSettingsPage page: page.SuspendMonitor(); break;
        }
        Retire();
        _active = false;
        if (_authority != null) _authority.AvailabilityChanged -= OnAvailabilityChanged;
        if (_store != null) _store.Changed -= OnStoreChanged;
        _desktopActivity.Eligible = false;
        _narrowActivity.Eligible = false;
    }

    private void Retire()
    {
        DetachLoggingHealth();
        _restartDialog?.Hide();
        _restartDialog = null;
        _restartPending = false;
        _restartBannerHost.Children.Clear();
        _restartBannerHost.Visibility = Visibility.Collapsed;
        _reads?.Dispose();
        _reads = null;
        _lease?.Dispose();
        _lease = null;
        _build = null;
        _buildLoading = true;
        _buildFailed = false;
        _serverStatus = null;
        _serverStatusReadAt = DateTimeOffset.MinValue;
        _sessionCount = 0;
    }

    private void OnAvailabilityChanged() => DispatcherQueue?.TryEnqueue(() =>
    {
        if (!_active) return;
        Retire();
        if (_authority?.IsAvailable != true)
        {
            LeaveAdmin();
            return;
        }
        Recapture();
        RebuildSidebars();
    });

    private void Recapture()
    {
        try { _lease = _authority!.Capture(); }
        catch (OperationCanceledException) { LeaveAdmin(); return; }
        _reads = _store?.AcquireReads();
        _desktopActivity.Eligible = true;
        _narrowActivity.Eligible = true;
        if (_store != null) _sessionCount = _store.Current.Sessions.Count;
        _ = LoadBuildAsync(_lease);
        _ = LoadServerStatusAsync(_lease, force: true);
    }

    /// <summary>WebUI RequireAdmin sends a non-admin principal back to the app.</summary>
    private void LeaveAdmin()
    {
        _active = false;
        _desktopActivity.Eligible = false;
        _narrowActivity.Eligible = false;
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_navigation?.Frame?.Content is AdminShellPage) App.MainWindowInstance?.NavigateToHome();
        });
    }

    private void OnStoreChanged(ServerActivitySnapshot snapshot) => DispatcherQueue?.TryEnqueue(() =>
    {
        snapshot = _store?.Current ?? ServerActivitySnapshot.Empty;
        if (!_active || _lease?.IsCurrent != true || snapshot.Epoch != _lease.Epoch || snapshot.Sessions.Count == _sessionCount) return;
        _sessionCount = snapshot.Sessions.Count;
        RebuildSidebars();
    });

    public async void Navigate(AdminRouteRequest request)
    {
        if (!_active || !await CanLeaveAsync() || !_active) return;
        CloseDrawer();
        _request = request;
        var type = PageType(request.Route);
        _frame.Navigate(type, request, new SuppressNavigationTransitionInfo());
    }

    /// <summary>The five Monitor routes; nothing outside Monitor is reachable from the shell.</summary>
    public static Type PageType(AdminRoute route) => route switch
    {
        AdminRoute.Activity => typeof(AdminActivityPage),
        AdminRoute.ServerLog => typeof(AdminLogsPage),
        AdminRoute.ClientDiagnostics => typeof(AdminDiagnosticsPage),
        AdminRoute.LoggingSettings => typeof(AdminLoggingSettingsPage),
        _ => typeof(AdminDashboardPage),
    };

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        var route = (e.Parameter as AdminRouteRequest)?.Route ?? AdminRoute.Dashboard;
        AttachRestartBanner();
        DetachLoggingHealth();
        if (_frame.Content is AdminLoggingSettingsPage logging) { _loggingHealthSource = logging; logging.SettingsHealthChanged += OnLoggingHealthChanged; }
        _route = route;
        _topics.Arrive(AdminNavigation.TopicOf(route));
        _narrowTitle.Text = AdminNavigation.HeaderTitle(route);
        App.MainWindowInstance?.SetDynamicTitle(AdminNavigation.TabTitle(route));
        RebuildSidebars();
        RouteChanged?.Invoke(route);
        if (_lease != null) _ = LoadServerStatusAsync(_lease, force: false);
    }

    private void DetachLoggingHealth()
    {
        if (_loggingHealthSource != null) _loggingHealthSource.SettingsHealthChanged -= OnLoggingHealthChanged;
        _loggingHealthSource = null; _loggingRestartObserved = false;
    }
    private void OnLoggingHealthChanged(LoggingSettingsHealth health)
    {
        if (!_active || _lease?.IsCurrent != true || !ReferenceEquals(_frame.Content, _loggingHealthSource)) return;
        var newlyRequired = health.RestartRequired && !_loggingRestartObserved;
        _loggingRestartObserved = health.RestartRequired;
        if (newlyRequired) _ = LoadServerStatusAsync(_lease, force: true);
    }

    // ── Data ─────────────────────────────────────────────────

    private void AttachRestartBanner()
    {
        if (_restartBannerHost.Parent is Panel previous) previous.Children.Remove(_restartBannerHost);
        if (_frame.Content is not FrameworkElement page) return;
        void Attach()
        {
            if (!_active || !ReferenceEquals(_frame.Content, page)) return;
            if (FindPageScroller(page)?.Content is StackPanel content && !content.Children.Contains(_restartBannerHost))
                content.Children.Insert(0, _restartBannerHost);
        }
        if (page.IsLoaded) Attach();
        else
        {
            RoutedEventHandler? loaded = null;
            loaded = (_, _) => { page.Loaded -= loaded; Attach(); };
            page.Loaded += loaded;
        }
    }

    private static ScrollViewer? FindPageScroller(DependencyObject root)
    {
        if (root is ScrollViewer scroller) return scroller;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindPageScroller(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private async Task LoadBuildAsync(MonitorAuthorityLease lease)
    {
        if (_api == null) return;
        try
        {
            var build = await _api.GetIndicatorBuildInfoAsync(lease);
            if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent) return;
            _build = build;
            _buildFailed = false;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent) { return; }
        catch (Exception)
        {
            if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent) return;
            _buildFailed = true;
        }
        _buildLoading = false;
        RebuildSidebars();
    }

    private async Task LoadServerStatusAsync(MonitorAuthorityLease lease, bool force)
    {
        if (_api == null || (!force && DateTimeOffset.UtcNow - _serverStatusReadAt < ServerStatusStaleAfter)) return;
        _serverStatusReadAt = DateTimeOffset.UtcNow;
        try
        {
            var status = await _api.GetIndicatorServerStatusAsync(lease);
            if (!ReferenceEquals(lease, _lease) || !lease.IsCurrent) return;
            // The flag latches for the process; a dismissal holds until the mark count changes,
            // and is forgotten once nothing is owed.
            if (!status.RestartRequired) _restartDismissedSignature = null;
            _serverStatus = status;
            RenderRestartBanner();
        }
        catch (OperationCanceledException) when (!lease.IsCurrent) { }
        catch (Exception ex) { Core.Services.LocalLog.AppendLine("admin_monitor.txt", $"server status read failed: {ex.GetType().Name}"); }
    }

    private IReadOnlyDictionary<string, bool> ReadTopics() => _settings?.Load().AdminNavOpenTopics ?? new Dictionary<string, bool>();

    private void WriteTopics(IReadOnlyDictionary<string, bool> topics)
    {
        if (_settings == null) return;
        var settings = _settings.Load();
        settings.AdminNavOpenTopics = new Dictionary<string, bool>(topics);
        _settings.Save(settings);
    }

    // ── Layout ───────────────────────────────────────────────

    private void BuildLayout()
    {
        _root.Background = A.Brush("AppBackgroundBrush");
        // Faint top gradient (from-primary/6, h-40, blurred).
        var glow = new Border
        {
            Height = 160, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = A.WithAlpha(A.Primary, 0.06), Offset = 0 },
                    new GradientStop { Color = A.WithAlpha(A.Primary, 0), Offset = 1 },
                },
            },
        };
        Grid.SetRowSpan(glow, 2);
        _root.Children.Add(glow);

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Desktop sidebar: fixed 240px, bg-sidebar/92 with a right border.
        Grid.SetRowSpan(_desktopSidebarHost, 2);
        _root.Children.Add(_desktopSidebarHost);

        // Narrow glass header.
        BuildNarrowHeader();
        _root.Children.Add(_narrowHeader);

        // Content column.
        _contentColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _contentColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _contentColumn.MaxWidth = ContentMaxWidth;
        _contentColumn.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(_frame, 1);
        _contentColumn.Children.Add(_frame);
        _contentHost.Children.Add(_contentColumn);
        Grid.SetRow(_contentHost, 1);
        _contentHost.Tag = "content";
        _root.Children.Add(_contentHost);

        // Desktop activity indicator: fixed end-5 top-5, always shown in Admin.
        _desktopActivity.HideWhenEmpty = false;
        _desktopActivity.HorizontalAlignment = HorizontalAlignment.Right;
        _desktopActivity.VerticalAlignment = VerticalAlignment.Top;
        _desktopActivity.Margin = new Thickness(0, 20, 20, 0);
        Grid.SetRowSpan(_desktopActivity, 2);
        Canvas.SetZIndex(_desktopActivity, 40);
        _root.Children.Add(_desktopActivity);

        // Drawer overlay (narrow): scrim + 320px panel.
        var scrim = new Border { Background = A.Solid(Microsoft.UI.Colors.Black, 0.5) };
        scrim.Tapped += (_, _) => CloseDrawer();
        _drawerLayer.Children.Add(scrim);
        _drawerPanelHost.HorizontalAlignment = HorizontalAlignment.Left;
        _drawerLayer.Children.Add(_drawerPanelHost);
        Grid.SetRowSpan(_drawerLayer, 2);
        Canvas.SetZIndex(_drawerLayer, 50);
        _root.Children.Add(_drawerLayer);
    }

    private void BuildNarrowHeader()
    {
        var menu = SquareButton("menu", AdminText.T("web.admin.shell.mobileHeader.openNavigation"), bordered: false);
        menu.Click += (_, _) => OpenDrawer();
        var tile = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = A.Solid(A.Border, 0.7), Background = A.Brush("SurfaceBrush"),
            Child = new TextBlock { Text = "▶", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = A.Solid(A.Primary), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        _narrowTitle.FontSize = 15;
        _narrowTitle.FontWeight = FontWeights.ExtraBold;
        _narrowTitle.Foreground = A.Solid(A.Foreground);
        _narrowTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _narrowTitle.Margin = new Thickness(0, 4, 0, 0);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { A.Eyebrow(AdminText.T("web.admin.shell.mobileHeader.eyebrow"), A.Muted, 160), _narrowTitle } };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { menu, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { tile, titles } } } };
        var search = SquareButton("search", AdminText.T("web.admin.shell.search.label"), bordered: true);
        search.Click += (_, _) => _ = ShowSearchAsync();
        _narrowActivity.HideWhenEmpty = false;
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { search, _narrowActivity } };
        var grid = new Grid();
        grid.Children.Add(left);
        grid.Children.Add(right);
        _narrowHeader.Child = grid;
        _narrowHeader.Margin = new Thickness(12, 12, 12, 0);
        _narrowHeader.Padding = new Thickness(12, 10, 12, 10);
        _narrowHeader.CornerRadius = new CornerRadius(16);
        _narrowHeader.Background = A.Solid(A.Surface, 0.85);
        _narrowHeader.BorderBrush = A.Solid(A.Border, 0.7);
        _narrowHeader.BorderThickness = new Thickness(1);
        Canvas.SetZIndex(_narrowHeader, 30);
    }

    private static Button SquareButton(string icon, string name, bool bordered)
    {
        var button = A.Plain(A.Icon(icon, bordered ? 16 : 20, A.Muted), name);
        button.Width = 44;
        button.Height = 44;
        button.CornerRadius = new CornerRadius(12);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        if (bordered)
        {
            button.BorderThickness = new Thickness(1);
            button.BorderBrush = A.Solid(A.Border, 0.7);
            button.Background = A.Solid(A.Surface, 0.7);
        }
        ToolTipService.SetToolTip(button, bordered ? AdminText.T("web.admin.shell.search.title", new { shortcut = "Ctrl K" }) : null);
        return button;
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var narrow = width < DesktopBreakpoint;
        var changed = narrow != _narrow || _desktopSidebarHost.Children.Count == 0;
        _narrow = narrow;
        _desktopSidebarHost.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        _narrowHeader.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        _desktopActivity.Suppressed = narrow;
        _narrowActivity.Suppressed = !narrow;
        if (!narrow) CloseDrawer();

        // Gutter: 1rem / 1.5rem (≥640) / 2rem (≥1024) / 2.5rem (≥1280); lg:pt-16 lg:pb-8; ms-[240px].
        var gutter = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
        _contentHost.Margin = narrow ? new Thickness(0) : new Thickness(SidebarWidth, 0, 0, 0);
        _contentColumn.Margin = narrow
            ? new Thickness(gutter, 16, gutter, 16)
            : new Thickness(gutter, 64, gutter, 32);
        if (changed) RebuildSidebars();
    }

    private void OpenDrawer()
    {
        var width = Math.Min(320, Math.Max(0, (ActualWidth > 0 ? ActualWidth : 800) - 48));
        _drawerPanelHost.Width = width;
        _drawerPanelHost.Children.Clear();
        _drawerPanelHost.Children.Add(BuildSidebar(embedded: true));
        var close = SquareButton("x", AdminText.T("web.admin.shell.drawer.close"), bordered: false);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, 16, 16, 0);
        close.Click += (_, _) => CloseDrawer();
        _drawerPanelHost.Children.Add(close);
        _drawerLayer.Visibility = Visibility.Visible;
        close.Focus(FocusState.Programmatic);
    }

    private void CloseDrawer()
    {
        if (_drawerLayer.Visibility != Visibility.Visible) return;
        _drawerLayer.Visibility = Visibility.Collapsed;
        _drawerPanelHost.Children.Clear();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && _drawerLayer.Visibility == Visibility.Visible)
        {
            CloseDrawer();
            e.Handled = true;
        }
    }

    private void RebuildSidebars()
    {
        _desktopSidebarHost.Children.Clear();
        if (!_narrow) _desktopSidebarHost.Children.Add(BuildSidebar(embedded: false));
        if (_drawerLayer.Visibility == Visibility.Visible && _drawerPanelHost.Children.Count > 0)
            _drawerPanelHost.Children[0] = BuildSidebar(embedded: true);
    }

    // ── Sidebar ──────────────────────────────────────────────

    private FrameworkElement BuildSidebar(bool embedded)
    {
        var grid = new Grid
        {
            Background = A.Solid(A.ColorOf("SidebarBackgroundColor", A.Surface), embedded ? 1 : 0.92),
            BorderBrush = A.Solid(A.ColorOf("SidebarBorderColor", A.Border), 0.7),
            BorderThickness = embedded ? new Thickness(0) : new Thickness(0, 0, 1, 0),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grid, AdminText.T("web.admin.shell.sidebar.navigation"));
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Logo → app home.
        var logo = A.Plain(new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/silo-wordmark-sidebar.png")),
            Width = 112, Height = 48, Stretch = Stretch.Uniform,
        }, AdminText.T("web.admin.shell.sidebar.home"));
        logo.HorizontalAlignment = HorizontalAlignment.Left;
        logo.Margin = new Thickness(20, 24, 20, 16);
        logo.Click += (_, _) => App.MainWindowInstance?.NavigateToHome();
        grid.Children.Add(logo);

        // Search (desktop only; the phone header has its own button).
        if (!embedded)
        {
            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(A.Icon("search", 16, A.Muted));
            var label = A.Text(AdminText.T("web.admin.shell.sidebar.search"), 13, A.Muted);
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            var kbd = new Border
            {
                BorderBrush = A.Solid(A.Border, 0.7), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock { Text = "Ctrl K", FontSize = 10, FontFamily = A.Mono, Foreground = A.Solid(A.Muted) },
                IsHitTestVisible = false,
            };
            Grid.SetColumn(kbd, 2);
            content.Children.Add(kbd);
            var search = A.Plain(content, AdminText.T("web.admin.shell.search.label"), new Thickness(12, 0, 12, 0));
            search.Height = 40;
            search.CornerRadius = new CornerRadius(12);
            search.BorderThickness = new Thickness(1);
            search.BorderBrush = A.Solid(A.Border, 0.7);
            search.Background = A.Solid(A.Surface, 0.7);
            search.Margin = new Thickness(12, 0, 12, 16);
            ToolTipService.SetToolTip(search, AdminText.T("web.admin.shell.search.title", new { shortcut = "Ctrl K" }));
            search.Click += (_, _) => _ = ShowSearchAsync();
            Grid.SetRow(search, 1);
            grid.Children.Add(search);
        }

        // Monitor section.
        var nav = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        var heading = A.Eyebrow(AdminText.T("web.admin.shell.nav.section.monitor"), A.Muted, 180);
        heading.Margin = new Thickness(8, 0, 8, 8);
        nav.Children.Add(heading);
        var list = new StackPanel { Spacing = 2 };
        list.Children.Add(NavItem(AdminRoute.Dashboard, "layout-dashboard", null, embedded));
        list.Children.Add(NavItem(AdminRoute.Activity, "radio",
            _sessionCount > 0 ? A.LiveBadge(AdminText.T("web.admin.shell.sidebar.liveSessions", AdminText.Count(_sessionCount)), false) : null, embedded));
        list.Children.Add(LogsTopic(embedded));
        nav.Children.Add(list);
        var scroller = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroller, 2);
        grid.Children.Add(scroller);

        // Footer: build card and Back to App.
        var buildValue = new TextBlock
        {
            Text = AdminNavigation.BuildDisplay(_build, _buildLoading, _buildFailed),
            FontFamily = A.Mono, FontSize = 12, LineHeight = 20, Margin = new Thickness(0, 4, 0, 0),
            Foreground = A.Brush("SidebarForegroundBrush", A.Solid(A.Foreground)), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (AdminNavigation.BuildTitle(_build) is { } builtAt) ToolTipService.SetToolTip(buildValue, builtAt);
        var buildCard = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = A.Solid(A.ColorOf("SidebarBorderColor", A.Border), 0.7),
            Background = A.Solid(A.ColorOf("SidebarAccentColor", A.Surface), 0.4),
            Child = new StackPanel { Children = { A.Eyebrow(AdminText.T("web.admin.shell.sidebar.build.heading"), A.Muted, 180), buildValue } },
        };
        var backContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        backContent.Children.Add(A.Icon("arrow-left", 18, A.Muted));
        backContent.Children.Add(A.Text(AdminText.T("web.admin.shell.sidebar.backToApp"), 13, A.Muted, FontWeights.Medium));
        var back = A.Plain(backContent, null, new Thickness(12, 10, 12, 10));
        back.CornerRadius = new CornerRadius(12);
        back.HorizontalContentAlignment = HorizontalAlignment.Left;
        back.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (embedded) back.MinHeight = 44;
        back.Click += (_, _) => App.MainWindowInstance?.NavigateToHome();
        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(12, 0, 12, 16), Children = { buildCard, back } };
        Grid.SetRow(footer, 3);
        grid.Children.Add(footer);
        return grid;
    }

    private static Color AccentFill => A.ColorOf("SurfaceHoverColor", A.Surface);

    private FrameworkElement NavItem(AdminRoute route, string icon, UIElement? badge, bool embedded)
    {
        var active = _route == route;
        var color = active ? A.Primary : A.Muted;
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(A.Icon(icon, 18, color));
        var label = A.Text(AdminNavigation.SidebarLabel(route), 13, color, FontWeights.Medium);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (badge is FrameworkElement b) { Grid.SetColumn(b, 2); grid.Children.Add(b); }
        var button = A.Plain(grid, AdminNavigation.SidebarLabel(route), new Thickness(12, 10, 12, 10));
        button.CornerRadius = new CornerRadius(12);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (embedded) button.MinHeight = 44;
        if (active) button.Background = A.Solid(AccentFill);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(button, active ? "current page" : "");
        button.Click += (_, _) => Navigate(new AdminRouteRequest(route));
        return WithActiveBar(button, active, 3, 18, -12);
    }

    private static FrameworkElement WithActiveBar(FrameworkElement item, bool active, double width, double height, double offset)
    {
        if (!active) return item;
        var host = new Grid();
        host.Children.Add(item);
        host.Children.Add(new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(0, 2, 2, 0), Background = A.Solid(A.Primary),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(offset, 0, 0, 0), IsHitTestVisible = false,
        });
        return host;
    }

    private FrameworkElement LogsTopic(bool embedded)
    {
        var open = _topics.IsOpen(AdminNavigation.LogsTopicId);
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var color = open ? A.Foreground : A.Muted;
        grid.Children.Add(A.Icon("scroll-text", 18, color));
        var label = A.Text(AdminText.T("web.admin.shell.nav.topic.logs"), 13, color, FontWeights.Medium);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var chevron = A.Icon(open ? "chevron-down" : "chevron-right", 14, A.Muted);
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        var heading = A.Plain(grid, AdminText.T("web.admin.shell.nav.topic.logs"), new Thickness(12, 10, 12, 10));
        heading.CornerRadius = new CornerRadius(12);
        heading.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (embedded) heading.MinHeight = 44;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(heading, open ? "expanded" : "collapsed");
        heading.Click += (_, _) =>
        {
            _topics.Toggle(AdminNavigation.LogsTopicId);
            RebuildSidebars();
        };

        var topic = new StackPanel { Children = { heading } };
        if (open)
        {
            var children = new StackPanel { Spacing = 1 };
            foreach (var route in new[] { AdminRoute.ServerLog, AdminRoute.ClientDiagnostics, AdminRoute.LoggingSettings })
                children.Children.Add(SubItem(route, embedded));
            topic.Children.Add(new Border
            {
                Margin = new Thickness(21, 2, 0, 6),
                Padding = new Thickness(12, 0, 0, 0),
                BorderBrush = A.Solid(A.Border, 0.7),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = children,
            });
        }
        return topic;
    }

    private FrameworkElement SubItem(AdminRoute route, bool embedded)
    {
        var active = _route == route;
        var label = A.Text(AdminNavigation.SidebarLabel(route), 12.5, active ? A.Primary : A.Muted,
            active ? FontWeights.Medium : FontWeights.Normal);
        var button = A.Plain(label, AdminNavigation.SidebarLabel(route), new Thickness(10, 6, 10, 6));
        button.CornerRadius = new CornerRadius(8);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        if (embedded) button.MinHeight = 44;
        if (active) button.Background = A.Solid(AccentFill);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(button, active ? "current page" : "");
        button.Click += (_, _) => Navigate(new AdminRouteRequest(route));
        return WithActiveBar(button, active, 2, 16, -13);
    }

    // ── Restart banner ───────────────────────────────────────

    private void RenderRestartBanner()
    {
        _restartBannerHost.Children.Clear();
        if (_serverStatus is not { RestartRequired: true } status) return;
        var signature = status.RestartMarkCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_restartDismissedSignature == signature) return;

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = A.Solid(A.Amber500, 0.15),
            Child = new Viewbox { Width = 14, Height = 14, Child = A.Icon("alert-triangle", 14, A.Amber400) },
        });
        var text = new StackPanel
        {
            Children =
            {
                A.Text(AdminText.T("web.admin.common.restartBanner.title"), 13, A.Foreground, FontWeights.Medium),
                A.Text(AdminText.T("web.admin.common.restartBanner.description"), 11.5, A.Muted, trim: false),
            },
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var later = A.Plain(A.Text(AdminText.T("web.admin.common.restartBanner.later"), 12, A.Foreground, FontWeights.Medium), null, new Thickness(12, 6, 12, 6));
        later.Click += (_, _) => { _restartDismissedSignature = signature; RenderRestartBanner(); };
        var restart = A.Outline(AdminText.T("web.admin.common.restartBanner.restart"), "rotate-ccw");
        restart.IsEnabled = !_restartPending;
        restart.Click += (_, _) => _ = ConfirmRestartAsync();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { later, restart },
        };
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        _restartBannerHost.Visibility = Visibility.Visible;
        _restartBannerHost.Children.Add(new Border
        {
            Margin = new Thickness(0, 0, 0, _narrow ? 16 : 24),
            Padding = new Thickness(_narrow ? 16 : 20, 12, _narrow ? 16 : 20, 12),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = A.Solid(A.Amber500, 0.30),
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0),
                GradientStops =
                {
                    new GradientStop { Color = A.WithAlpha(A.Warning, 0.16), Offset = 0 },
                    new GradientStop { Color = A.WithAlpha(A.Warning, 0.06), Offset = 1 },
                },
            },
            Child = grid,
        });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_restartBannerHost, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
    }

    private async Task ConfirmRestartAsync()
    {
        // Capture the displayed owner's lease before showing confirmation.
        if (!_active || _restartPending || _restartDialog != null || _api == null || _lease is not { IsCurrent: true } lease) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AdminText.T("web.admin.common.restartServer.confirmTitle"),
            Content = A.Text(AdminText.T("web.admin.common.restartServer.confirmDescription"), 14, A.Foreground, trim: false),
            PrimaryButtonText = AdminText.T("web.admin.common.restartServer.confirm"),
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (Application.Current.Resources.TryGetValue("WebUiContentDialogStyle", out var style) && style is Style dialogStyle)
            dialog.Style = dialogStyle;
        _restartDialog = dialog;
        try
        {
            var answer = await dialog.ShowAsync();
            if (answer != ContentDialogResult.Primary || !_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent) return;
            _restartPending = true;
            RenderRestartBanner();
            var result = await _api.RequestMonitorServerRestartAsync(lease);
            if (!_active || !ReferenceEquals(_lease, lease) || !lease.IsCurrent) return;
            App.Services.GetService<SiloPlayer.Services.ToastService>()?.Success(AdminText.T(result.Status == "already_requested"
                ? "web.admin.common.restartServer.alreadyRequested" : "web.admin.common.restartServer.shuttingDown"));
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (_active && ReferenceEquals(_lease, lease) && lease.IsCurrent)
                App.Services.GetService<SiloPlayer.Services.ToastService>()?.Error(AdminText.T("web.admin.common.restartServer.failed"));
        }
        finally
        {
            if (ReferenceEquals(_restartDialog, dialog)) _restartDialog = null;
            if (_active && ReferenceEquals(_lease, lease) && lease.IsCurrent)
            {
                _restartPending = false;
                RenderRestartBanner();
                await LoadServerStatusAsync(lease, force: true);
            }
        }
    }

    // ── Scoped admin search ──────────────────────────────────

    private ContentDialog? _searchDialog;

    private async Task ShowSearchAsync()
    {
        if (_searchDialog != null || XamlRoot == null || !_active || _lease is not { IsCurrent: true } lease) return;
        AdminRouteRequest? selected = null;
        var query = new TextBox { PlaceholderText = AdminText.T("web.admin.shell.search.label"), MinWidth = 360 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(query, AdminText.T("web.admin.shell.search.label"));
        var results = new StackPanel { Spacing = 2, Margin = new Thickness(0, 12, 0, 0) };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Content = new StackPanel { Children = { query, results } },
            CloseButtonText = AdminText.T("common.cancel"),
        };
        if (Application.Current.Resources.TryGetValue("WebUiContentDialogStyle", out var style) && style is Style dialogStyle)
            dialog.Style = dialogStyle;
        void Render()
        {
            results.Children.Clear();
            foreach (var page in AdminNavigation.Search(query.Text))
            {
                var row = new StackPanel { Spacing = 2, Children =
                {
                    A.Text(page.Label, 14, A.Foreground, FontWeights.SemiBold),
                    A.Text(page.Description, 12, A.Muted),
                } };
                var item = A.Plain(row, page.Label, new Thickness(12, 8, 12, 8));
                item.HorizontalAlignment = HorizontalAlignment.Stretch;
                item.HorizontalContentAlignment = HorizontalAlignment.Left;
                var target = page.Route;
                item.Click += (_, _) => { selected = new AdminRouteRequest(target); dialog.Hide(); };
                results.Children.Add(item);
            }
        }
        query.TextChanged += (_, _) => Render();
        query.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var first = AdminNavigation.Search(query.Text).FirstOrDefault();
            if (first == null) return;
            e.Handled = true;
            selected = new AdminRouteRequest(first.Route);
            dialog.Hide();
        };
        Render();
        _searchDialog = dialog;
        try
        {
            dialog.Opened += (_, _) => query.Focus(FocusState.Programmatic);
            await dialog.ShowAsync();
        }
        catch (Exception ex) { Core.Services.LocalLog.AppendLine("admin_monitor.txt", $"admin search failed: {ex.GetType().Name}"); }
        finally { _searchDialog = null; }
        // The search dialog must finish closing before Logging can show its leave confirmation.
        if (selected != null && _active && ReferenceEquals(_lease, lease) && lease.IsCurrent)
            Navigate(selected);
    }
}
