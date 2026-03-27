using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
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
                // Configure API client base address
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

        // Watch for library changes to update nav
        _viewModel.Libraries.CollectionChanged += (_, _) => UpdateLibraryNavItems();
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

    private void UpdateLibraryNavItems()
    {
        // Remove old library items (keep Home)
        while (NavView.MenuItems.Count > 1)
        {
            NavView.MenuItems.RemoveAt(NavView.MenuItems.Count - 1);
        }

        // Add separator if we have libraries
        if (_viewModel.Libraries.Count > 0)
        {
            NavView.MenuItems.Add(new NavigationViewItemSeparator());
        }

        // Add library items
        foreach (var lib in _viewModel.Libraries)
        {
            var icon = lib.Type switch
            {
                "movie" => "\uE8B2",   // Video
                "tv" => "\uE7F4",       // TV
                "music" => "\uE8D6",    // Music
                _ => "\uE8F1"           // Library
            };

            var navItem = new NavigationViewItem
            {
                Content = lib.Name,
                Tag = lib,
                Icon = new FontIcon { Glyph = icon }
            };
            NavView.MenuItems.Add(navItem);
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            if (item.Tag is string tag && tag == "Home")
            {
                _navigationService.Navigate<HomePage>();
            }
            else if (item.Tag is Library library)
            {
                _navigationService.Navigate<LibraryPage>(library);
            }
        }
    }

    private void ProfileNavItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Navigate to profile select, keeping existing auth
        HideMainNavigation();
        _navigationService.Navigate<ProfileSelectPage>("switch");
    }
}
