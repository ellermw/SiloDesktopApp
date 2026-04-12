using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminShellPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly Core.Api.AdminApi _adminApi;
    private Button? _activeButton;
    private DispatcherTimer? _sessionTimer;

    // Pairs of (nav button, accent indicator bar, icon element, text element)
    private readonly List<(Button Button, Border Bar, FontIcon Icon, TextBlock Text)> _navItems = [];

    // Starting sub-page — overrides the default Dashboard landing when AdminShellPage
    // is navigated to with a Type parameter (e.g. deep links from Server Activity popover).
    private Type? _startingPage;

    public AdminShellPage()
    {
        this.InitializeComponent();
        _navigationService = App.Services.GetRequiredService<NavigationService>();
        _adminApi = App.Services.GetRequiredService<Core.Api.AdminApi>();

        Loaded += AdminShellPage_Loaded;
        Unloaded += (_, _) => { _sessionTimer?.Stop(); _sessionTimer = null; };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is Type pageType)
            _startingPage = pageType;
    }

    private void AdminShellPage_Loaded(object sender, RoutedEventArgs e)
    {
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
        // USERS
        _navItems.Add((NavUsers,           NavUsersBar,           NavUsersIcon,           NavUsersText));
        _navItems.Add((NavPlaybackHistory, NavPlaybackHistoryBar, NavPlaybackHistoryIcon, NavPlaybackHistoryText));
        _navItems.Add((NavHistoryImport,   NavHistoryImportBar,   NavHistoryImportIcon,   NavHistoryImportText));
        // SERVER
        _navItems.Add((NavScheduledTasks,      NavScheduledTasksBar,      NavScheduledTasksIcon,      NavScheduledTasksText));
        _navItems.Add((NavNodes,               NavNodesBar,               NavNodesIcon,               NavNodesText));
        _navItems.Add((NavMaintenance,         NavMaintenanceBar,         NavMaintenanceIcon,         NavMaintenanceText));
        _navItems.Add((NavPlugins,             NavPluginsBar,             NavPluginsIcon,             NavPluginsText));
        _navItems.Add((NavSettings,            NavSettingsBar,            NavSettingsIcon,            NavSettingsText));
        _navItems.Add((NavRecommendations,     NavRecommendationsBar,     NavRecommendationsIcon,     NavRecommendationsText));
        _navItems.Add((NavApiKeys,             NavApiKeysBar,             NavApiKeysIcon,             NavApiKeysText));

        // Navigate to the requested starting page, or Dashboard by default.
        // Selects the matching sidebar nav item so the active indicator lines up.
        var startType = _startingPage ?? typeof(AdminDashboardPage);
        var startButton = GetNavButtonForPage(startType) ?? NavDashboard;
        SetActiveNavItem(startButton);
        AdminContentFrame.Navigate(startType);
        _startingPage = null;

        // Wire the top-bar Server Activity button nav callbacks
        WireServerActivityNav();

        // Poll active session count for the sidebar "N live" badge
        _ = UpdateSessionBadgeAsync();
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _sessionTimer.Tick += async (_, _) => await UpdateSessionBadgeAsync();
        _sessionTimer.Start();
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
        if (pageType == typeof(AdminSectionsPage)) return NavSections;
        if (pageType == typeof(AdminUsersPage)) return NavUsers;
        if (pageType == typeof(AdminPlaybackHistoryPage)) return NavPlaybackHistory;
        if (pageType == typeof(AdminHistoryImportPage)) return NavHistoryImport;
        if (pageType == typeof(AdminTasksPage)) return NavScheduledTasks;
        if (pageType == typeof(AdminNodesPage)) return NavNodes;
        if (pageType == typeof(AdminMaintenancePage)) return NavMaintenance;
        if (pageType == typeof(AdminPluginsPage)) return NavPlugins;
        if (pageType == typeof(AdminSettingsDetailPage)) return NavSettings;
        if (pageType == typeof(AdminRecommendationsPage)) return NavRecommendations;
        if (pageType == typeof(AdminApiKeysPage)) return NavApiKeys;
        return null;
    }

    private void SetActiveNavItem(Button button)
    {
        var accentBg = (SolidColorBrush)Application.Current.Resources["AccentBackgroundBrush"];
        var accentFg = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var secondaryFg = (SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"];

        foreach (var (btn, bar, icon, text) in _navItems)
        {
            bool isActive = btn == button;
            btn.Background = isActive ? accentBg : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            bar.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            icon.Foreground = isActive ? accentFg : secondaryFg;
            text.Foreground = isActive ? accentFg : secondaryFg;
        }

        _activeButton = button;
    }

    // ===== Nav click handlers =====

    private void NavDashboard_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavDashboard);
        AdminContentFrame.Navigate(typeof(AdminDashboardPage));
    }

    private void NavActivity_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavActivity);
        AdminContentFrame.Navigate(typeof(AdminActivityPage));
    }

    private void NavLogs_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavLogs);
        AdminContentFrame.Navigate(typeof(AdminLogsPage));
    }

    private void NavLibraries_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavLibraries);
        AdminContentFrame.Navigate(typeof(AdminLibrariesPage));
    }

    private void NavCollections_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavCollections);
        AdminContentFrame.Navigate(typeof(AdminCollectionsPage));
    }

    private void NavSections_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavSections);
        AdminContentFrame.Navigate(typeof(AdminSectionsPage));
    }

    private void NavUsers_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavUsers);
        AdminContentFrame.Navigate(typeof(AdminUsersPage));
    }

    private void NavPlaybackHistory_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavPlaybackHistory);
        AdminContentFrame.Navigate(typeof(AdminPlaybackHistoryPage));
    }

    private void NavHistoryImport_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavHistoryImport);
        AdminContentFrame.Navigate(typeof(AdminHistoryImportPage));
    }

    private void NavScheduledTasks_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavScheduledTasks);
        AdminContentFrame.Navigate(typeof(AdminTasksPage));
    }

    private void NavNodes_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavNodes);
        AdminContentFrame.Navigate(typeof(AdminNodesPage));
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavSettings);
        AdminContentFrame.Navigate(typeof(AdminSettingsDetailPage));
    }

    private void NavRecommendations_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavRecommendations);
        AdminContentFrame.Navigate(typeof(AdminRecommendationsPage));
    }

    private void NavApiKeys_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavApiKeys);
        AdminContentFrame.Navigate(typeof(AdminApiKeysPage));
    }

    private void NavPlugins_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavPlugins);
        AdminContentFrame.Navigate(typeof(AdminPluginsPage));
    }

    private void NavMaintenance_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavMaintenance);
        AdminContentFrame.Navigate(typeof(AdminMaintenancePage));
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
