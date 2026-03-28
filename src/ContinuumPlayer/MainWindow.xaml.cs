using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;
using ContinuumPlayer.Views;

namespace ContinuumPlayer;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigationService;
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly CredentialStore _credentialStore;
    private readonly AuthService _authService;
    private readonly ContinuumApiClient _apiClient;

    public MainWindow()
    {
        this.InitializeComponent();

        // Set title bar colors
        if (AppWindow?.TitleBar != null)
        {
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = false;
        }

        _navigationService = App.Services.GetRequiredService<NavigationService>();
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        _settingsService = App.Services.GetRequiredService<SettingsService>();
        _credentialStore = App.Services.GetRequiredService<CredentialStore>();
        _authService = App.Services.GetRequiredService<AuthService>();
        _apiClient = App.Services.GetRequiredService<ContinuumApiClient>();

        _navigationService.Frame = ContentFrame;

        // Hide the nav view initially -- it shows only after login
        NavView.IsPaneVisible = false;
    }

    private async void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        await TryAutoLoginAsync();
    }

    private async Task TryAutoLoginAsync()
    {
        var settings = _settingsService.Load();

        // If there's exactly one server with saved tokens, try auto-login
        if (settings.Servers.Count == 1)
        {
            var server = settings.Servers[0];
            var refreshToken = _credentialStore.LoadCredential(server.Url, "refresh_token");

            if (!string.IsNullOrEmpty(refreshToken))
            {
                // Configure API client base URL
                _apiClient.SetBaseUrl(server.Url);
                _apiClient.SetAccessToken(""); // temporary, will be replaced by refresh
                _authService.SetTokens("", refreshToken, 0);

                try
                {
                    var success = await _authService.TryRefreshAsync();
                    if (success)
                    {
                        // Save updated tokens
                        if (_authService.RefreshToken != null)
                            _credentialStore.SaveCredential(server.Url, "refresh_token", _authService.RefreshToken);

                        // Restore user info from saved settings (refresh doesn't return user)
                        if (!string.IsNullOrEmpty(settings.LastUsername))
                        {
                            _authService.SetCurrentUser(new Core.Models.Auth.UserInfo
                            {
                                Username = settings.LastUsername,
                                Role = settings.LastUserRole ?? "user"
                            });
                        }

                        // Auto-select last profile if available
                        if (!string.IsNullOrEmpty(settings.LastProfileId))
                        {
                            _authService.SelectProfile(settings.LastProfileId);
                        }

                        ShowMainNavigation();
                        NavigateToHome();
                        return;
                    }
                }
                catch
                {
                    // Auto-login failed, fall through to server select
                }
            }
        }

        // No auto-login possible, show server select
        _navigationService.Navigate<ServerSelectPage>();
    }

    public void ShowMainNavigation()
    {
        NavView.IsPaneVisible = true;
        _ = _viewModel.LoadLibrariesCommand.ExecuteAsync(null);

        // Update profile display in the sidebar footer
        UpdateProfileDisplay();

        // Show Admin button if user is admin
        AdminButton.Visibility = _authService.CurrentUser?.Role == "admin"
            ? Visibility.Visible : Visibility.Collapsed;

        // Watch for library changes to update nav (marshal to UI thread)
        _viewModel.Libraries.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() => UpdateLibraryNavItems());
        };
    }

    private void UpdateProfileDisplay()
    {
        var profileId = _authService.SelectedProfileId;
        if (!string.IsNullOrEmpty(profileId))
        {
            // Try to load the profile name from the API asynchronously
            _ = LoadProfileNameAsync(profileId);
        }
    }

    private async Task LoadProfileNameAsync(string profileId)
    {
        try
        {
            var authApi = App.Services.GetRequiredService<Core.Api.AuthApi>();
            var response = await authApi.GetProfilesAsync();
            var profile = response.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ProfileNameText.Text = profile.Name;
                    ProfileInitialText.Text = !string.IsNullOrEmpty(profile.Name)
                        ? profile.Name[0].ToString().ToUpperInvariant()
                        : "?";
                    LogoutMenuItem.Text = $"Logout ({profile.Name})";
                });
            }
        }
        catch
        {
            // Non-critical, leave default text
        }
    }

    public void HideMainNavigation()
    {
        NavView.IsPaneVisible = false;
    }

    public void NavigateToHome()
    {
        _navigationService.Navigate<HomePage>();
        NavView.SelectedItem = HomeNavItem;
    }

    public void UpdateLibraryNavItems()
    {
        // Find the LibrariesHeader index
        int headerIndex = -1;
        for (int i = 0; i < NavView.MenuItems.Count; i++)
        {
            if (NavView.MenuItems[i] is NavigationViewItemHeader header && header == LibrariesHeader)
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0) return;

        // Remove old library items (between LibrariesHeader and the next header)
        int removeStart = headerIndex + 1;
        while (removeStart < NavView.MenuItems.Count &&
               NavView.MenuItems[removeStart] is not NavigationViewItemHeader)
        {
            NavView.MenuItems.RemoveAt(removeStart);
        }

        // Filter out hidden libraries
        var appSettings = _settingsService.Load();
        var hiddenIds = new HashSet<int>(appSettings.HiddenLibraryIds);

        // Insert library items after the header
        int insertIndex = headerIndex + 1;
        foreach (var lib in _viewModel.Libraries)
        {
            if (hiddenIds.Contains(lib.Id)) continue;

            var icon = lib.Type switch
            {
                "movies" => "\uE8B2",   // Video
                "series" => "\uE7F4",   // TV
                _ => "\uE8F1"           // Library
            };

            var navItem = new NavigationViewItem
            {
                Content = lib.Name,
                Tag = lib,
                Icon = new FontIcon { Glyph = icon }
            };
            NavView.MenuItems.Insert(insertIndex++, navItem);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate<SettingsPage>();
    }

    private void Admin_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate<Views.Admin.AdminShellPage>();
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            switch (tag)
            {
                case "Home":
                    _navigationService.Navigate<HomePage>();
                    break;
                case "Search":
                    _navigationService.Navigate<SearchPage>();
                    break;
                case "Recommendations":
                    _navigationService.Navigate<RecommendationsPage>();
                    break;
                case "Favorites":
                    _navigationService.Navigate<FavoritesPage>();
                    break;
                case "Watchlist":
                    _navigationService.Navigate<WatchlistPage>();
                    break;
                case "History":
                    _navigationService.Navigate<HistoryPage>();
                    break;
                case "Collections":
                    _navigationService.Navigate<PlaceholderPage>(tag);
                    break;
            }
        }
        else if (args.InvokedItemContainer is NavigationViewItem libItem && libItem.Tag is Library library)
        {
            _navigationService.Navigate<LibraryPage>(library);
        }
    }

    private void SwitchProfile_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to profile select, keeping existing auth
        HideMainNavigation();
        _navigationService.Navigate<ProfileSelectPage>("switch");
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _authService.Logout();
        HideMainNavigation();
        _navigationService.Navigate<ServerSelectPage>();
    }
}
