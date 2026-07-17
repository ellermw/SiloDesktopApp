using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views.Admin;

public sealed record AdminShellNavigation(Type PageType, object? Parameter = null);

public sealed partial class AdminShellPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly Core.Api.AdminApi _adminApi;
    private readonly Core.Api.SettingsApi _settingsApi;
    private readonly PluginsApi _pluginsApi;
    private readonly Core.Api.SiloApiClient _apiClient;
    private Button? _activeButton;
    private DispatcherTimer? _sessionTimer;
    private bool _policyAvailable;
    private object? _adminCurrentParameter;
    private readonly List<Button> _adminPluginButtons = [];

    // Pairs of (nav button, accent indicator bar, icon element, text element)
    private readonly List<(Button Button, Border Bar, FontIcon Icon, TextBlock Text)> _navItems = [];

    // Starting sub-page — overrides the default Dashboard landing when AdminShellPage
    // is navigated to with a Type parameter (e.g. deep links from Server Activity popover).
    private Type? _startingPage;
    private object? _startingParameter;

    public AdminShellPage()
    {
        this.InitializeComponent();
        _navigationService = App.Services.GetRequiredService<NavigationService>();
        _adminApi = App.Services.GetRequiredService<Core.Api.AdminApi>();
        _settingsApi = App.Services.GetRequiredService<Core.Api.SettingsApi>();
        _pluginsApi = App.Services.GetRequiredService<PluginsApi>();
        _apiClient = App.Services.GetRequiredService<Core.Api.SiloApiClient>();

        Loaded += AdminShellPage_Loaded;
        Unloaded += (_, _) => { _sessionTimer?.Stop(); _sessionTimer = null; };
    }

    private void AdminContentHost_SizeChanged(object sender, SizeChangedEventArgs e)
        => AdminContentFrame.Width = Math.Min(1640, Math.Max(0, e.NewSize.Width));

    private void AdminContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AdminOverlayHost.Children.Clear();
        AdminOverlayHost.IsHitTestVisible = false;
        if (e.Content is not FrameworkElement page) return;
        PageTransitionHelper.AnimateEntrance(page);

        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            page.Loaded -= loaded;
            NormalizeAdminPageViewport(page);
        };
        page.Loaded += loaded;
    }

    public void AttachPageOverlay(FrameworkElement overlay)
    {
        if (VisualTreeHelper.GetParent(overlay) is Panel previousParent)
            previousParent.Children.Remove(overlay);

        if (!AdminOverlayHost.Children.Contains(overlay))
            AdminOverlayHost.Children.Add(overlay);
    }

    public void DetachPageOverlay(FrameworkElement overlay)
    {
        if (AdminOverlayHost.Children.Contains(overlay))
            AdminOverlayHost.Children.Remove(overlay);

        AdminOverlayHost.IsHitTestVisible = AdminOverlayHost.Children.Any(child =>
            child is FrameworkElement element && element.Visibility == Visibility.Visible);
    }

    public void SetPageOverlayVisible(FrameworkElement overlay, bool visible)
    {
        if (!AdminOverlayHost.Children.Contains(overlay))
            AttachPageOverlay(overlay);

        overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AdminOverlayHost.IsHitTestVisible = AdminOverlayHost.Children.Any(child =>
            child is FrameworkElement element && element.Visibility == Visibility.Visible);
    }

    private static void NormalizeAdminPageViewport(DependencyObject page)
    {
        var scrollViewer = FindFirstDescendant<ScrollViewer>(page);
        if (scrollViewer?.Content is not FrameworkElement content) return;

        scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        scrollViewer.HorizontalContentAlignment = HorizontalAlignment.Center;

        void ApplyWidth(double width) => content.Width = Math.Max(0, width);
        ApplyWidth(scrollViewer.ActualWidth);
        scrollViewer.SizeChanged += (_, args) => ApplyWidth(args.NewSize.Width);
    }

    private static T? FindFirstDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindFirstDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Type pageType)
            _startingPage = pageType;
        else if (e.Parameter is AdminShellNavigation navigation)
        {
            _startingPage = navigation.PageType;
            _startingParameter = navigation.Parameter;
        }
    }

    private void AdminShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        BuildVersionText.Text = "loading...";
        _ = ApplyServerBrandingAsync();
        _ = UpdateBuildInfoAsync();
        ReorderNavigationToMatchWebUi();

        // Register all nav items for batch state management
        _navItems.Clear();
        // OVERVIEW
        _navItems.Add((NavDashboard,       NavDashboardBar,       NavDashboardIcon,       NavDashboardText));
        _navItems.Add((NavActivity,        NavActivityBar,        NavActivityIcon,        NavActivityText));
        _navItems.Add((NavLogs,            NavLogsBar,            NavLogsIcon,            NavLogsText));
        // CONTENT
        _navItems.Add((NavLibraries,       NavLibrariesBar,       NavLibrariesIcon,       NavLibrariesText));
        _navItems.Add((NavCollections,     NavCollectionsBar,     NavCollectionsIcon,     NavCollectionsText));
        _navItems.Add((NavSections,        NavSectionsBar,        NavSectionsIcon,        NavSectionsText));
        _navItems.Add((NavRequests,        NavRequestsBar,        NavRequestsIcon,        NavRequestsText));
        // AUTOMATION
        _navItems.Add((NavAutoscan,          NavAutoscanBar,          NavAutoscanIcon,          NavAutoscanText));
        _navItems.Add((NavScheduledTasks,      NavScheduledTasksBar,      NavScheduledTasksIcon,      NavScheduledTasksText));
        _navItems.Add((NavSubtitles,       NavSubtitlesBar,       NavSubtitlesIcon,       NavSubtitlesText));
        _navItems.Add((NavMarkerHistory,       NavMarkerHistoryBar,       NavMarkerHistoryIcon,       NavMarkerHistoryText));
        _navItems.Add((NavRecommendations,     NavRecommendationsBar,     NavRecommendationsIcon,     NavRecommendationsText));
        // USERS
        _navItems.Add((NavUsers,           NavUsersBar,           NavUsersIcon,           NavUsersText));
        _navItems.Add((NavAccessGroups,    NavAccessGroupsBar,    NavAccessGroupsIcon,    NavAccessGroupsText));
        _navItems.Add((NavDevices,         NavDevicesBar,         NavDevicesIcon,         NavDevicesText));
        _navItems.Add((NavPlaybackHistory, NavPlaybackHistoryBar, NavPlaybackHistoryIcon, NavPlaybackHistoryText));
        _navItems.Add((NavHistoryImport,   NavHistoryImportBar,   NavHistoryImportIcon,   NavHistoryImportText));
        // SYSTEM
        _navItems.Add((NavSettings,            NavSettingsBar,            NavSettingsIcon,            NavSettingsText));
        _navItems.Add((NavPlugins,             NavPluginsBar,             NavPluginsIcon,             NavPluginsText));
        _navItems.Add((NavPolicy,              NavPolicyBar,              NavPolicyIcon,              NavPolicyText));
        _navItems.Add((NavNodes,               NavNodesBar,               NavNodesIcon,               NavNodesText));
        _navItems.Add((NavApiKeys,             NavApiKeysBar,             NavApiKeysIcon,             NavApiKeysText));
        _navItems.Add((NavMaintenance,         NavMaintenanceBar,         NavMaintenanceIcon,         NavMaintenanceText));

        // Navigate to the requested starting page, or Dashboard by default.
        // Selects the matching sidebar nav item so the active indicator lines up.
        var startType = _startingPage ?? typeof(AdminDashboardPage);
        var startButton = GetNavButtonForPage(startType) ?? NavDashboard;
        SetActiveNavItem(startButton);
        NavigateAdmin(startType, _startingParameter);
        _startingPage = null;
        _startingParameter = null;

        // Wire the top-bar Server Activity button nav callbacks
        WireServerActivityNav();

        // Poll active session count for the sidebar "N live" badge
        _ = UpdateSessionBadgeAsync();
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _sessionTimer.Tick += async (_, _) => await UpdateSessionBadgeAsync();
        _sessionTimer.Start();
        _ = RefreshPolicyAvailabilityAsync();
        _ = RefreshAdminPluginNavigationAsync();
    }

    private async Task ApplyServerBrandingAsync()
    {
        try
        {
            var branding = await _settingsApi.GetServerBrandingAsync();
            DispatcherQueue.TryEnqueue(() =>
            {
                DocumentTitle.SetServerName(branding.ServerName);
                App.MainWindowInstance?.SetDynamicTitle("Admin");
            });
            if (string.IsNullOrWhiteSpace(branding.WordmarkUrl)) return;

            var url = branding.WordmarkUrl;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                uri = new Uri(_apiClient.BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/'));

            DispatcherQueue.TryEnqueue(() =>
            {
                AdminSiloBrandImage.Source = new BitmapImage(uri);
                AutomationProperties.SetName(
                    AdminSiloBrandImage,
                    string.IsNullOrWhiteSpace(branding.ServerName) ? "Silo" : branding.ServerName);
            });
        }
        catch
        {
            // Branding is optional; retain the bundled Silo wordmark on failure.
        }
    }

    private async Task UpdateBuildInfoAsync()
    {
        try
        {
            var build = await _adminApi.GetBuildInfoAsync();
            var display = build.Available && !string.IsNullOrWhiteSpace(build.Display)
                ? build.Display
                : "dev build";
            DispatcherQueue.TryEnqueue(() => BuildVersionText.Text = display);
        }
        catch
        {
            DispatcherQueue.TryEnqueue(() => BuildVersionText.Text = "load failed");
        }
    }

    /// <summary>
    /// The XAML keeps each named button available to compiled bindings, while this
    /// method applies the authoritative current WebUI grouping and order at runtime.
    /// Missing WebUI routes are added to these groups as their complete pages land.
    /// </summary>
    private void ReorderNavigationToMatchWebUi()
    {
        AdminNavStack.Children.Clear();

        AddNavGroup("OVERVIEW", NavDashboard, NavActivity, NavLogs);
        AddNavGroup("CONTENT", NavLibraries, NavCollections, NavSections, NavRequests);
        AddNavGroup("AUTOMATION", NavAutoscan, NavScheduledTasks, NavSubtitles, NavMarkerHistory, NavRecommendations);
        AddNavGroup("USERS", NavUsers, NavAccessGroups, NavDevices, NavPlaybackHistory, NavHistoryImport);
        if (_policyAvailable)
            AddNavGroup("SYSTEM", NavSettings, NavPlugins, NavPolicy, NavNodes, NavApiKeys, NavMaintenance);
        else
            AddNavGroup("SYSTEM", NavSettings, NavPlugins, NavNodes, NavApiKeys, NavMaintenance);
        if (_adminPluginButtons.Count > 0)
            AddNavGroup("PLUGIN APPS", _adminPluginButtons.ToArray());
    }

    private async Task RefreshPolicyAvailabilityAsync()
    {
        try
        {
            var capability = await _adminApi.GetPolicyCapabilityAsync();
            var available = capability.Enabled && capability.EditorAvailable;
            if (available == _policyAvailable) return;
            _policyAvailable = available;
            DispatcherQueue.TryEnqueue(ReorderNavigationToMatchWebUi);
        }
        catch { _policyAvailable = false; }
    }

    private async Task RefreshAdminPluginNavigationAsync()
    {
        try
        {
            var installations = await _pluginsApi.GetInstallationsAsync();
            var routes = installations
                .Where(installation => installation.Enabled)
                .SelectMany(installation => installation.Routes
                    .Where(route => route.Navigable &&
                                    string.Equals(route.NavigationKind, "admin", StringComparison.OrdinalIgnoreCase))
                    .Select(route => (Installation: installation, Route: route)))
                .ToList();

            DispatcherQueue.TryEnqueue(() =>
            {
                foreach (var button in _adminPluginButtons)
                {
                    _navItems.RemoveAll(item => ReferenceEquals(item.Button, button));
                    if (ReferenceEquals(_activeButton, button))
                        _activeButton = null;
                }
                _adminPluginButtons.Clear();

                foreach (var (installation, route) in routes)
                {
                    var label = string.IsNullOrWhiteSpace(route.NavigationLabel)
                        ? installation.PluginId
                        : route.NavigationLabel;
                    _adminPluginButtons.Add(CreateAdminPluginButton(label, installation.Id, route.Path));
                }

                ReorderNavigationToMatchWebUi();
            });
        }
        catch
        {
            // Plugin navigation is optional; core admin navigation remains usable.
        }
    }

    private Button CreateAdminPluginButton(string label, int installationId, string routePath)
    {
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = (Brush)Application.Current.Resources["SidebarPrimaryBrush"],
            Margin = new Thickness(0, 0, 10, 0),
            Visibility = Visibility.Collapsed,
        };
        var icon = new FontIcon
        {
            Glyph = "\uE7FC",
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };
        var text = new TextBlock
        {
            Text = label,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(bar, 0);
        content.Children.Add(bar);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(icon);
        row.Children.Add(text);
        Grid.SetColumn(row, 1);
        content.Children.Add(row);

        var button = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(4, 1, 4, 1),
            CornerRadius = new CornerRadius(12),
            Content = content,
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            SetActiveNavItem(button);
            NavigateAdmin(
                typeof(Views.PluginRoutePage),
                new Views.PluginRoutePage.NavigationArgs(installationId, routePath, label));
        };
        _navItems.Add((button, bar, icon, text));
        return button;
    }

    private void AddNavGroup(string label, params Button[] buttons)
    {
        AdminNavStack.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["SectionHeaderTextStyle"],
            Margin = new Thickness(20, 12, 16, 4),
        });

        foreach (var button in buttons)
            AdminNavStack.Children.Add(button);
    }

    // ===== SetActiveNavItem =====

    /// <summary>
    /// Polls the session count for the sidebar "N live" Activity badge.
    /// The top-bar ServerActivityButton owns its own polling timer for the
    /// full Streams/Tasks/Scans popover — we only drive the sidebar badge here.
    /// </summary>
    private async Task UpdateSessionBadgeAsync()
    {
        try
        {
            var sessions = await _adminApi.GetSessionsAsync();
            var count = sessions?.Count ?? 0;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (count > 0)
                {
                    NavActivityBadge.Visibility = Visibility.Visible;
                    NavActivityBadgeText.Text = $"{count} live";
                }
                else
                {
                    NavActivityBadge.Visibility = Visibility.Collapsed;
                }
            });
        }
        catch { }
    }

    /// <summary>
    /// Wires the ServerActivityButton's "View all" nav callbacks to the admin
    /// shell's own navigation handlers, so clicking View All jumps to the
    /// right admin page without the button needing to know about routing.
    /// Called once from AdminShellPage_Loaded.
    /// </summary>
    private void WireServerActivityNav()
    {
        // Note: HideWhenEmpty=false is set on the AdminServerActivityButton in XAML,
        // not here — if set after Loaded has fired, the button would stay Collapsed
        // until the next poll cycle.
        AdminServerActivityButton.OnViewStreams = () => NavActivity_Click(NavActivity, new RoutedEventArgs());
        AdminServerActivityButton.OnViewTasks = () => NavScheduledTasks_Click(NavScheduledTasks, new RoutedEventArgs());
        AdminServerActivityButton.OnViewScans = () => NavLibraries_Click(NavLibraries, new RoutedEventArgs());
    }

    /// <summary>
    /// Maps an admin page type back to the sidebar nav button that represents it.
    /// Used when AdminShellPage is navigated to with a starting-page parameter
    /// so the correct nav item highlights.
    /// </summary>
    private Button? GetNavButtonForPage(Type pageType)
    {
        if (pageType == typeof(AdminDashboardPage)) return NavDashboard;
        if (pageType == typeof(AdminActivityPage)) return NavActivity;
        if (pageType == typeof(AdminLogsPage)) return NavLogs;
        if (pageType == typeof(AdminLibrariesPage)) return NavLibraries;
        if (pageType == typeof(AdminCollectionsPage)) return NavCollections;
        if (pageType == typeof(AdminRequestsPage)) return NavRequests;
        if (pageType == typeof(AdminSectionsPage)) return NavSections;
        if (pageType == typeof(AdminSubtitlesPage)) return NavSubtitles;
        if (pageType == typeof(AdminUsersPage)) return NavUsers;
        if (pageType == typeof(AdminAccessGroupsPage)) return NavAccessGroups;
        if (pageType == typeof(AdminDevicesPage)) return NavDevices;
        if (pageType == typeof(AdminPlaybackHistoryPage)) return NavPlaybackHistory;
        if (pageType == typeof(AdminHistoryImportPage)) return NavHistoryImport;
        if (pageType == typeof(AdminTasksPage)) return NavScheduledTasks;
        if (pageType == typeof(AdminAutoscanPage)) return NavAutoscan;
        if (pageType == typeof(AdminMarkerHistoryPage)) return NavMarkerHistory;
        if (pageType == typeof(AdminNodesPage)) return NavNodes;
        if (pageType == typeof(AdminMaintenancePage)) return NavMaintenance;
        if (pageType == typeof(AdminPluginsPage)) return NavPlugins;
        if (pageType == typeof(AdminPolicyPage)) return NavPolicy;
        if (pageType == typeof(AdminSettingsDetailPage)) return NavSettings;
        if (pageType == typeof(AdminRecommendationsPage)) return NavRecommendations;
        if (pageType == typeof(AdminApiKeysPage)) return NavApiKeys;
        return null;
    }

    private void SetActiveNavItem(Button button)
    {
        var accentBg = (SolidColorBrush)Application.Current.Resources["SidebarAccentBrush"];
        var accentFg = (SolidColorBrush)Application.Current.Resources["SidebarAccentForegroundBrush"];
        var activeBar = (SolidColorBrush)Application.Current.Resources["SidebarPrimaryBrush"];
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        foreach (var (btn, bar, icon, text) in _navItems)
        {
            bool isActive = btn == button;
            btn.Background = isActive ? accentBg : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            bar.Background = activeBar;
            bar.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            icon.Foreground = isActive ? accentFg : secondaryFg;
            text.Foreground = isActive ? accentFg : secondaryFg;
        }

        _activeButton = button;
    }

    private bool NavigateAdmin(Type pageType, object? parameter = null)
    {
        if (AdminContentFrame.CurrentSourcePageType == pageType &&
            (ReferenceEquals(_adminCurrentParameter, parameter) || Equals(_adminCurrentParameter, parameter)))
            return false;

        _adminCurrentParameter = parameter;
        return AdminContentFrame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo());
    }

    // ===== Nav click handlers =====

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavDashboard);
        NavigateAdmin(typeof(AdminDashboardPage));
    }

    private void NavActivity_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavActivity);
        NavigateAdmin(typeof(AdminActivityPage));
    }

    private void NavLogs_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavLogs);
        NavigateAdmin(typeof(AdminLogsPage));
    }

    private void NavLibraries_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavLibraries);
        NavigateAdmin(typeof(AdminLibrariesPage));
    }

    private void NavCollections_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavCollections);
        NavigateAdmin(typeof(AdminCollectionsPage));
    }

    private void NavRequests_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavRequests);
        NavigateAdmin(typeof(AdminRequestsPage));
    }

    private void NavSections_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavSections);
        NavigateAdmin(typeof(AdminSectionsPage));
    }

    private void NavSubtitles_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavSubtitles);
        NavigateAdmin(typeof(AdminSubtitlesPage));
    }

    private void NavUsers_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavUsers);
        NavigateAdmin(typeof(AdminUsersPage));
    }

    private void NavAccessGroups_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavAccessGroups);
        NavigateAdmin(typeof(AdminAccessGroupsPage));
    }

    private void NavDevices_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavDevices);
        NavigateAdmin(typeof(AdminDevicesPage));
    }

    private void NavPlaybackHistory_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavPlaybackHistory);
        NavigateAdmin(typeof(AdminPlaybackHistoryPage));
    }

    private void NavHistoryImport_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavHistoryImport);
        NavigateAdmin(typeof(AdminHistoryImportPage));
    }

    private void NavScheduledTasks_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavScheduledTasks);
        NavigateAdmin(typeof(AdminTasksPage));
    }

    private void NavAutoscan_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavAutoscan);
        NavigateAdmin(typeof(AdminAutoscanPage));
    }

    private void NavMarkerHistory_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavMarkerHistory);
        NavigateAdmin(typeof(AdminMarkerHistoryPage));
    }

    private void NavNodes_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavNodes);
        NavigateAdmin(typeof(AdminNodesPage));
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavSettings);
        NavigateAdmin(typeof(AdminSettingsDetailPage));
    }

    private void NavRecommendations_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavRecommendations);
        NavigateAdmin(typeof(AdminRecommendationsPage));
    }

    private void NavApiKeys_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavApiKeys);
        NavigateAdmin(typeof(AdminApiKeysPage));
    }

    private void NavPlugins_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavPlugins);
        NavigateAdmin(typeof(AdminPluginsPage));
    }

    private void NavPolicy_Click(object sender, RoutedEventArgs e)
    {
        if (!_policyAvailable) return;
        SetActiveNavItem(NavPolicy);
        NavigateAdmin(typeof(AdminPolicyPage));
    }

    private void NavMaintenance_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavMaintenance);
        NavigateAdmin(typeof(AdminMaintenancePage));
    }

    private void BackToApp_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.RestoreMainPane();
            App.MainWindowInstance.NavigateToHome();
        }
    }
}
