using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.Views.Admin;

public sealed partial class AdminShellPage : Page
{
    private readonly NavigationService _navigationService;
    private Button? _activeButton;

    // Pairs of (nav button, accent indicator bar, icon element, text element)
    private readonly List<(Button Button, Border Bar, FontIcon Icon, TextBlock Text)> _navItems = [];

    public AdminShellPage()
    {
        this.InitializeComponent();
        _navigationService = App.Services.GetRequiredService<NavigationService>();

        Loaded += AdminShellPage_Loaded;
    }

    private void AdminShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Register all nav items for batch state management
        _navItems.Clear();
        _navItems.Add((NavDashboard,       NavDashboardBar,       NavDashboardIcon,       NavDashboardText));
        _navItems.Add((NavActivity,        NavActivityBar,        NavActivityIcon,        NavActivityText));
        _navItems.Add((NavLogs,            NavLogsBar,            NavLogsIcon,            NavLogsText));
        _navItems.Add((NavLibraries,       NavLibrariesBar,       NavLibrariesIcon,       NavLibrariesText));
        _navItems.Add((NavCollections,     NavCollectionsBar,     NavCollectionsIcon,     NavCollectionsText));
        _navItems.Add((NavSections,        NavSectionsBar,        NavSectionsIcon,        NavSectionsText));
        _navItems.Add((NavUsers,           NavUsersBar,           NavUsersIcon,           NavUsersText));
        _navItems.Add((NavPlaybackHistory, NavPlaybackHistoryBar, NavPlaybackHistoryIcon, NavPlaybackHistoryText));
        _navItems.Add((NavScheduledTasks,  NavScheduledTasksBar,  NavScheduledTasksIcon,  NavScheduledTasksText));
        _navItems.Add((NavNodes,           NavNodesBar,           NavNodesIcon,           NavNodesText));
        _navItems.Add((NavMaintenance,     NavMaintenanceBar,     NavMaintenanceIcon,     NavMaintenanceText));
        _navItems.Add((NavSettings,        NavSettingsBar,        NavSettingsIcon,        NavSettingsText));
        _navItems.Add((NavRecommendations, NavRecommendationsBar, NavRecommendationsIcon, NavRecommendationsText));
        _navItems.Add((NavApiKeys,         NavApiKeysBar,         NavApiKeysIcon,         NavApiKeysText));

        // Navigate to Dashboard on load
        SetActiveNavItem(NavDashboard);
        AdminContentFrame.Navigate(typeof(AdminDashboardPage));
    }

    // ===== SetActiveNavItem =====

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
        AdminContentFrame.Navigate(typeof(PlaceholderPage), "Playback History");
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

    private void NavMaintenance_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavMaintenance);
        AdminContentFrame.Navigate(typeof(PlaceholderPage), "Maintenance");
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavSettings);
        AdminContentFrame.Navigate(typeof(PlaceholderPage), "Settings");
    }

    private void NavRecommendations_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavRecommendations);
        AdminContentFrame.Navigate(typeof(PlaceholderPage), "Recommendations");
    }

    private void NavApiKeys_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavItem(NavApiKeys);
        AdminContentFrame.Navigate(typeof(PlaceholderPage), "API Keys");
    }

    private void BackToApp_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate<HomePage>();

        // Restore the main nav pane visibility
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.NavigateToHome();
        }
    }
}
