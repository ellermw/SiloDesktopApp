using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Views;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private sealed record SettingsSidebarTag(string Section);
    private object[]? _appSidebarItems;
    private SettingsPage? _settingsSidebarPage;

    private bool _settingsSidebarSynchronizationQueued;

    // Frame navigation is raised synchronously from NavView.ItemInvoked. Swapping
    // NavView.MenuItems there removes the container NavigationView is still
    // dispatching, so the swap runs on the next dispatcher turn instead.
    private void QueueSettingsSidebarSynchronization()
    {
        if (_settingsSidebarSynchronizationQueued) return;
        _settingsSidebarSynchronizationQueued = true;
        if (DispatcherQueue.TryEnqueue(() =>
            {
                _settingsSidebarSynchronizationQueued = false;
                SynchronizeSettingsSidebar();
            }))
            return;
        _settingsSidebarSynchronizationQueued = false;
        SynchronizeSettingsSidebar();
    }

    private void SynchronizeSettingsSidebar()
    {
        if (!CanExposeAuthenticatedNavigation || _currentWindowWidth < 1024 || ContentFrame.Content is not SettingsPage page)
        {
            RestoreAppSidebar();
            return;
        }
        if (!ReferenceEquals(_settingsSidebarPage, page))
        {
            if (_settingsSidebarPage != null) DetachSettingsSidebar();
            _settingsSidebarPage = page;
            page.SidebarEntriesChanged += RebuildSettingsSidebar;
            page.SettingsSectionChanged += OnSettingsSectionChanged;
            _appSidebarItems ??= NavView.MenuItems.ToArray();
            SettingsSidebarSearch.Text = "";
            page.SetShellSidebarHosted(true);
            SidebarBrandHost.Visibility = Visibility.Collapsed;
            SidebarFooterPanel.Visibility = Visibility.Collapsed;
            SettingsSidebarHeader.Visibility = Visibility.Visible;
            NavView.IsPaneToggleButtonVisible = false;
            RebuildSettingsSidebar();
        }
        SynchronizeSettingsSidebarSelection();
    }

    private void OnSettingsSectionChanged(string _) => SynchronizeSettingsSidebarSelection();

    private void RebuildSettingsSidebar()
    {
        if (_settingsSidebarPage is not { } page || _appSidebarItems is null) return;
        NavView.MenuItems.Clear();
        var query = SettingsSidebarSearch.Text.Trim();
        string? group = null;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in page.SidebarEntries.Where(entry => words.All(word =>
            (entry.Group + " " + entry.Label + " " + entry.SearchText).Contains(word, StringComparison.OrdinalIgnoreCase))))
        {
            if (entry.Group != group)
            {
                group = entry.Group;
                NavView.MenuItems.Add(new NavigationViewItemHeader
                {
                    Content = group.ToUpperInvariant(),
                    Style = (Style)RootGrid.Resources["SettingsSidebarGroupHeaderStyle"],
                    Margin = new Thickness(0, NavView.MenuItems.Count == 0 ? 0 : 20, 0, 0),
                });
            }
            var item = new NavigationViewItem
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
                    Children = { WebUiIcon.Navigation(SettingsSectionIcon(entry.Section), 18),
                        new TextBlock { Text = entry.Label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium } } },
                Tag = new SettingsSidebarTag(entry.Section),
                CornerRadius = new CornerRadius(12),
                MinHeight = 40,
                Margin = new Thickness(8, 0, 8, 2),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, entry.Label);
            NavView.MenuItems.Add(item);
        }
        if (NavView.MenuItems.Count == 0)
            NavView.MenuItems.Add(new NavigationViewItemHeader { Content = "No matching settings" });
        SynchronizeSettingsSidebarSelection();
    }

    private void SynchronizeSettingsSidebarSelection()
    {
        if (_settingsSidebarPage is not { } page) return;
        NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is SettingsSidebarTag tag && tag.Section == page.ActiveSettingsSection);
    }

    private void DetachSettingsSidebar()
    {
        if (_settingsSidebarPage is not { } page) return;
        page.SidebarEntriesChanged -= RebuildSettingsSidebar;
        page.SettingsSectionChanged -= OnSettingsSectionChanged;
        page.SetShellSidebarHosted(false);
        _settingsSidebarPage = null;
    }

    private void RestoreAppSidebar()
    {
        DetachSettingsSidebar();
        if (_appSidebarItems is not { } original) return;
        _appSidebarItems = null;
        NavView.MenuItems.Clear();
        foreach (var item in original) NavView.MenuItems.Add(item);
        SettingsSidebarHeader.Visibility = Visibility.Collapsed;
        NavView.IsPaneToggleButtonVisible = _currentWindowWidth >= 1024;
        SidebarBrandHost.Visibility = Visibility.Visible;
        SidebarFooterPanel.Visibility = Visibility.Visible;
        if (CanExposeAuthenticatedNavigation)
        {
            UpdateLibraryNavItems();
            UpdateSidebarPanePresentation(NavView.IsPaneOpen);
            ResynchronizeSelectedNavigationItem();
            // Async shell hydration may have completed while its app rows were detached.
            if (_hydratedShellKey is { } key && _shellHydrationCts is { IsCancellationRequested: false } owner)
                _ = RunShellWorkAsync("settings_restore_apps", () => RefreshPluginAppsAsync(key, owner.Token));
        }
    }

    private void SettingsSidebarSearch_TextChanged(object sender, TextChangedEventArgs e) => RebuildSettingsSidebar();
    public bool FocusSettingsSidebarSearch()
    {
        if (_settingsSidebarPage is null || SettingsSidebarHeader.Visibility != Visibility.Visible) return false;
        SettingsSidebarSearch.Focus(FocusState.Keyboard);
        SettingsSidebarSearch.SelectAll();
        return true;
    }
    private void SettingsSidebarBack_Click(object sender, RoutedEventArgs e)
    {
        if (_navigationService.CanGoBack) _navigationService.GoBack();
        else NavigateToHome();
    }

    private static string SettingsSectionIcon(string section) => section switch
    {
        "Playback" => "play", "Subtitles" => "subtitles", "Devices" => "monitor-smartphone",
        "Interface" => "panel-top", "TitlePages" => "clapperboard", "Accessibility" => "eye",
        "HomeScreen" => "layout-dashboard", "Libraries" => "library", "Personalize" => "sparkles",
        "Requests" => "send", "ConnectApps" => "cast", "WatchProviders" => "cloud",
        "WebhookSync" => "server", "HistoryImport" => "clock", "Sessions" => "shield-check",
        "Account" => "key-round", "Profiles" => "users", "NotificationsSettings" => "bell",
        _ => "settings",
    };
}
