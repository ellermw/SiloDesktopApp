using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Services;
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
    private readonly SettingsApi _settingsApi;
    private readonly PlayerService _playerService;

    public MainWindow()
    {
        this.InitializeComponent();

        // Set title bar colors
        if (AppWindow?.TitleBar != null)
        {
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = false;
        }

        // Set window icon (non-fatal if it fails)
        try
        {
            var exeDir = Path.GetDirectoryName(System.Environment.ProcessPath) ?? "";
            var iconPath = Path.Combine(exeDir, "Assets", "app.ico");
            if (File.Exists(iconPath))
                AppWindow?.SetIcon(iconPath);
        }
        catch { /* Icon is cosmetic — don't crash the app */ }

        _navigationService = App.Services.GetRequiredService<NavigationService>();
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        _settingsService = App.Services.GetRequiredService<SettingsService>();
        _credentialStore = App.Services.GetRequiredService<CredentialStore>();
        _authService = App.Services.GetRequiredService<AuthService>();
        _apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();

        _navigationService.Frame = ContentFrame;

        // F7: update window title on every navigation.
        _navigationService.Navigated += OnNavigated_UpdateWindowTitle;
        if (AppWindow != null) AppWindow.Title = DocumentTitle.AppName;

        // F2: register the toast host with the ToastService so any VM/page
        // can call App.Services.GetRequiredService<ToastService>().Success(...).
        var toastService = App.Services.GetRequiredService<ToastService>();
        toastService.Register(ToastHost, DispatcherQueue);

        // Hide the nav view initially -- it shows only after login.
        // Server Activity button follows the same admin-gate as AdminButton and
        // stays hidden until ShowMainNavigation() fires post-login.
        NavView.IsPaneVisible = false;
        MainServerActivityButton.Visibility = Visibility.Collapsed;

        // Wire Server Activity "View all" callbacks. Routes navigate through
        // AdminShellPage so the admin sidebar stays present — passing the target
        // sub-page type as a parameter, which AdminShellPage consumes in
        // OnNavigatedTo and opens inside its internal AdminContentFrame.
        //
        // HideWhenEmpty=false keeps the button visible for admins on every page
        // (not just when something is active). Non-admin users never see it
        // regardless — the role check inside the control handles that.
        MainServerActivityButton.HideWhenEmpty = false;
        MainServerActivityButton.OnViewStreams = () =>
        {
            // Hide the main sidebar BEFORE navigating — same as Admin_Click —
            // otherwise the user sees two navigation panes side-by-side
            // (main nav + AdminShellPage's own admin nav).
            NavView.IsPaneVisible = false;
            _navigationService.Navigate<Views.Admin.AdminShellPage>(typeof(Views.Admin.AdminActivityPage));
        };
        MainServerActivityButton.OnViewTasks = () =>
        {
            NavView.IsPaneVisible = false;
            _navigationService.Navigate<Views.Admin.AdminShellPage>(typeof(Views.Admin.AdminTasksPage));
        };
        MainServerActivityButton.OnViewScans = () =>
        {
            NavView.IsPaneVisible = false;
            _navigationService.Navigate<Views.Admin.AdminShellPage>(typeof(Views.Admin.AdminLibrariesPage));
        };

        // Listen for player state changes
        _playerService = App.Services.GetRequiredService<PlayerService>();
        _playerService.StateChanged += OnPlayerStateChanged;

        // Playing Next cinematic overlay — fires when an episode ends with
        // another episode queued. PlayerOverlay used to own this but its
        // Activate() is never called, so the subscription lives here now.
        _playerService.ShowPlayingNextRequested += OnShowPlayingNextRequested;

        // Keep native video window matched to main window size
        this.SizeChanged += OnWindowSizeChanged;

        // Hide/show player popup when main window is minimized/restored
        if (AppWindow != null)
        {
            AppWindow.Changed += OnAppWindowChanged;
        }

        // Clean up event subscriptions when window closes
        this.Closed += OnWindowClosed;
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        _playerService.HandleWindowResize();
    }

    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidSizeChange || args.DidPositionChange)
        {
            var p = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (p != null)
            {
                _playerService.HandleWindowMinimized(p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized);
            }
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _playerService.StateChanged -= OnPlayerStateChanged;
        _playerService.ShowPlayingNextRequested -= OnShowPlayingNextRequested;
        this.SizeChanged -= OnWindowSizeChanged;
        if (AppWindow != null) AppWindow.Changed -= OnAppWindowChanged;
        _navigationService.Navigated -= OnNavigated_UpdateWindowTitle;
        StopPlayingNextCountdown();
    }

    // ─── Playing Next cinematic overlay ─────────────────────────────────

    private DispatcherTimer? _playingNextTimer;
    private int _playingNextRemaining;
    private bool _playingNextAutoPlay = true;
    private const int PlayingNextCountdownSeconds = 10;
    private const string AutoPlayNextSettingKey = "playback.auto_play_next";

    private void OnShowPlayingNextRequested()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            var title = _playerService.NextEpisodeTitle ?? "Next episode";
            var series = _playerService.NextEpisodeSeriesTitle;
            var overview = _playerService.NextEpisodeOverview ?? "";

            PlayingNextTitleText.Text = title;
            PlayingNextSeriesText.Text = series ?? "";
            PlayingNextSeriesText.Visibility = string.IsNullOrEmpty(series) ? Visibility.Collapsed : Visibility.Visible;
            PlayingNextOverviewText.Text = overview;
            PlayingNextOverviewText.Visibility = string.IsNullOrEmpty(overview) ? Visibility.Collapsed : Visibility.Visible;
            PlayingNextPoster.Source = null;
            PlayingNextBackdrop.Source = null;
            _ = LoadPlayingNextPosterAsync();

            _playingNextAutoPlay = await GetPlayingNextAutoPlayAsync();
            _playingNextRemaining = PlayingNextCountdownSeconds;
            PlayingNextCountdownText.Text = _playingNextRemaining.ToString();
            PlayingNextPlayNowText.Text = "Play Now";
            UpdatePlayingNextAutoPlayVisuals();
            PlayingNextOverlay.Visibility = Visibility.Visible;

            // Post-roll starts only after mpv reports true media end. Move the
            // finished player to the mini bar while the Up Next card owns the
            // countdown, matching the user's "after playback ends" contract.
            if (_playerService.State == PlayerState.Expanded || _playerService.State == PlayerState.Fullscreen)
            {
                _playerService.Minimize();
            }

            if (_playingNextAutoPlay)
                StartPlayingNextCountdown();
        });
    }

    private void StartPlayingNextCountdown()
    {
        StopPlayingNextCountdown();
        _playingNextTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _playingNextTimer.Tick += PlayingNextTimer_Tick;
        _playingNextTimer.Start();
    }

    private void StopPlayingNextCountdown()
    {
        if (_playingNextTimer != null)
        {
            _playingNextTimer.Stop();
            _playingNextTimer.Tick -= PlayingNextTimer_Tick;
            _playingNextTimer = null;
        }
    }

    private async void PlayingNextTimer_Tick(object? sender, object e)
    {
        _playingNextRemaining--;
        if (_playingNextRemaining <= 0)
        {
            StopPlayingNextCountdown();
            PlayingNextOverlay.Visibility = Visibility.Collapsed;
            try
            {
                await _playerService.ContinuePlayingNextAsync();
            }
            catch (Exception ex)
            {
                LocalLog.AppendLine("state_trace.txt", $"PlayingNextTimer error: {ex}");
                ShowPlaybackError($"Failed to start next episode: {ex.Message}");
            }
            return;
        }
        PlayingNextCountdownText.Text = _playingNextRemaining.ToString();
    }

    private async Task<bool> GetPlayingNextAutoPlayAsync()
    {
        try
        {
            var response = await _settingsApi.GetEffectiveSettingsAsync([AutoPlayNextSettingKey]);
            var entry = response.Settings.FirstOrDefault(setting => setting.Key == AutoPlayNextSettingKey);
            return !string.Equals(entry?.EffectiveValue, "false", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine("state_trace.txt", $"GetPlayingNextAutoPlayAsync failed: {ex.Message}");
            return true;
        }
    }

    private async void PlayingNextAutoplayToggle_Click(object sender, RoutedEventArgs e)
    {
        _playingNextAutoPlay = !_playingNextAutoPlay;
        UpdatePlayingNextAutoPlayVisuals();

        try
        {
            await _settingsApi.PutDeviceSettingAsync(AutoPlayNextSettingKey, _playingNextAutoPlay ? "true" : "false");
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine("state_trace.txt", $"PutDeviceSettingAsync playback.auto_play_next failed: {ex.Message}");
        }

        StopPlayingNextCountdown();
        _playingNextRemaining = PlayingNextCountdownSeconds;
        PlayingNextCountdownText.Text = _playingNextRemaining.ToString();

        if (_playingNextAutoPlay && PlayingNextOverlay.Visibility == Visibility.Visible)
            StartPlayingNextCountdown();
    }

    private void UpdatePlayingNextAutoPlayVisuals()
    {
        PlayingNextCountdownPanel.Visibility = _playingNextAutoPlay ? Visibility.Visible : Visibility.Collapsed;
        PlayingNextAutoplayToggleText.Text = _playingNextAutoPlay
            ? "Auto-play is on"
            : "Auto-play is off";
    }

    private async void PlayingNextPlayNow_Click(object sender, RoutedEventArgs e)
    {
        StopPlayingNextCountdown();
        PlayingNextOverlay.Visibility = Visibility.Collapsed;
        try
        {
            await _playerService.ContinuePlayingNextAsync();
        }
        catch (Exception ex)
        {
            // Log and surface the error instead of silently crashing.
            LocalLog.AppendLine("state_trace.txt", $"PlayingNextPlayNow error: {ex}");
            ShowPlaybackError($"Failed to start next episode: {ex.Message}");
        }
    }

    private void PlayingNextCancel_Click(object sender, RoutedEventArgs e)
    {
        StopPlayingNextCountdown();
        PlayingNextOverlay.Visibility = Visibility.Collapsed;
        _playerService.CancelPlayingNext();
    }

    private async Task LoadPlayingNextPosterAsync()
    {
        var url = _playerService.NextEpisodePosterUrl;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var bytes = await httpClient.GetByteArrayAsync(url);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            PlayingNextPoster.Source = bitmap;
            PlayingNextBackdrop.Source = bitmap;
        }
        catch { /* Poster is cosmetic */ }
    }

    /// <summary>F7: update AppWindow.Title on every page navigation.</summary>
    private void OnNavigated_UpdateWindowTitle(object? sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (AppWindow == null) return;
        var pageType = e.SourcePageType;
        if (pageType == null)
        {
            AppWindow.Title = DocumentTitle.AppName;
            return;
        }

        // For pages that display a specific item, use a dynamic title
        // instead of the generic page-type label. The actual name gets
        // set later (after the async load) via SetDynamicTitle from the
        // page's code-behind. Start with the generic label as a placeholder
        // so there's never a blank title bar.
        AppWindow.Title = DocumentTitle.FromPageType(pageType);

        // Auto-collapse the nav pane on detail pages so the content gets
        // more horizontal canvas (webui parity — detail pages shift the
        // sidebar from 260px to 64px). Browse-type pages expand back.
        // Only applies in Idle / Minimized states where the pane is
        // visible at all.
        if (NavView.IsPaneVisible)
        {
            NavView.IsPaneOpen = !IsDetailPage(pageType);
        }
    }

    /// <summary>
    /// Whether a page type should trigger the nav-pane auto-collapse. These
    /// are pages that prefer a wider content canvas (item detail hero, person
    /// filmography grid). Browse/home/settings pages stay expanded.
    /// </summary>
    private static bool IsDetailPage(Type pageType)
    {
        var name = pageType.Name;
        return name is "ItemDetailPage" or "PersonDetailPage";
    }

    /// <summary>Ctrl+K — global search palette (webui parity with GlobalSearch.tsx).</summary>
    private async void GlobalSearchAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        try
        {
            var dlg = new Controls.GlobalSearchDialog { XamlRoot = this.Content.XamlRoot };
            await dlg.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GlobalSearchDialog failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called by pages (ItemDetailPage, LibraryPage, PersonDetailPage, etc.)
    /// after their data loads to replace the generic window title with the
    /// actual item/library/person name.
    /// </summary>
    public void SetDynamicTitle(string label)
    {
        if (AppWindow != null)
            AppWindow.Title = DocumentTitle.FromLabel(label);
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
            // Clean up any previously-persisted access token (B1 security fix — we no
            // longer write access tokens to disk; this removes stale ones from old installs).
            _credentialStore.DeleteCredential(server.Url, "access_token");
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

                        // Restore user info — prefer JWT-parsed role (from TryRefreshAsync),
                        // only fall back to saved settings if JWT didn't have user info
                        if (_authService.CurrentUser == null && !string.IsNullOrEmpty(settings.LastUsername))
                        {
                            _authService.SetCurrentUser(new Core.Models.Auth.UserInfo
                            {
                                Username = settings.LastUsername,
                                Role = settings.LastUserRole ?? "user"
                            });
                        }
                        else if (_authService.CurrentUser != null && string.IsNullOrEmpty(_authService.CurrentUser.Username) && !string.IsNullOrEmpty(settings.LastUsername))
                        {
                            _authService.CurrentUser.Username = settings.LastUsername;
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

    private bool _navInitialized;

    public void ShowMainNavigation()
    {
        NavView.IsPaneVisible = true;

        // Always update admin button and profile display for current user
        bool isAdmin = _authService.CurrentUser?.Role == "admin";
        AdminButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        // Server Activity button uses the exact same gate as the admin sidebar
        // button — whatever decision is made here for AdminButton applies to
        // MainServerActivityButton too. Keeps the two controls in lock-step
        // regardless of login/logout/navigation timing.
        MainServerActivityButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        _ = UpdateProfileDisplayAsync();
        BuildThemeDots();

        if (!_navInitialized)
        {
            _navInitialized = true;

            // Load libraries on first init, then fetch sidebar pins so the
            // library nav renders pinned collections underneath each library.
            _ = Task.Run(async () =>
            {
                await _viewModel.LoadLibrariesCommand.ExecuteAsync(null);
                DispatcherQueue.TryEnqueue(async () => await RefreshSidebarPinsAsync());
            });

            // Watch for library changes to update nav (marshal to UI thread)
            _viewModel.Libraries.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateLibraryNavItems());
            };
        }
        else
        {
            _ = Task.Run(async () =>
            {
                await _viewModel.LoadLibrariesCommand.ExecuteAsync(null);
                DispatcherQueue.TryEnqueue(async () => await RefreshSidebarPinsAsync());
            });
        }
    }

    private Task UpdateProfileDisplayAsync()
    {
        var profileId = _authService.SelectedProfileId;
        if (!string.IsNullOrEmpty(profileId))
            return LoadProfileNameAsync(profileId);
        return Task.CompletedTask;
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
                    // Populate dropdown header
                    ProfileDropdownInitial.Text = ProfileInitialText.Text;
                    ProfileDropdownName.Text = profile.Name;
                    ProfileDropdownUsername.Text = _authService.CurrentUser?.Username ?? "";
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
        // Keep the Server Activity button in sync with the rest of the shell —
        // while the nav is hidden (login / profile select / setup), no admin
        // chrome should be visible.
        MainServerActivityButton.Visibility = Visibility.Collapsed;
    }

    public void RestoreMainPane()
    {
        NavView.IsPaneVisible = true;
    }

    private void OnPlayerStateChanged(PlayerState state)
    {
        var threadId = Environment.CurrentManagedThreadId;
        var hasAccess = DispatcherQueue.HasThreadAccess;
        LogState($"OnPlayerStateChanged: state={state} thread={threadId} hasUIAccess={hasAccess}");

        // Ensure XAML updates run on the UI thread
        if (!hasAccess)
        {
            LogState($"  Dispatching to UI thread...");
            DispatcherQueue.TryEnqueue(() => OnPlayerStateChanged(state));
            return;
        }

        LogState($"  Executing state={state} on UI thread");
        switch (state)
        {
            case PlayerState.Idle:
                PlayerOverlayControl.Visibility = Visibility.Collapsed;
                PlayerOverlayControl.Deactivate();
                MiniPlayerBarControl.Visibility = Visibility.Collapsed;
                MiniPlayerBarControl.Deactivate();
                if (_navInitialized) NavView.IsPaneVisible = true;
                NavView.Margin = new Thickness(0);
                LogState($"  -> Idle: NavView.IsPaneVisible={NavView.IsPaneVisible} _navInitialized={_navInitialized}");
                break;

            case PlayerState.Expanded:
            case PlayerState.Fullscreen:
                MiniPlayerBarControl.Deactivate();
                MiniPlayerBarControl.Visibility = Visibility.Collapsed;
                PlayerOverlayControl.Visibility = Visibility.Collapsed;
                PlayerOverlayControl.Deactivate();
                // Do NOT hide NavView — the popup window covers it.
                // Hiding it caused the sidebar to disappear and not come back.
                LogState($"  -> Expanded/Fullscreen: NavView untouched");
                break;

            case PlayerState.Minimized:
                PlayerOverlayControl.Deactivate();
                PlayerOverlayControl.Visibility = Visibility.Collapsed;
                if (_navInitialized) NavView.IsPaneVisible = true;
                NavView.Margin = new Thickness(0, 0, 0, 132);
                MiniPlayerBarControl.Visibility = Visibility.Visible;
                MiniPlayerBarControl.Activate();
                break;
        }
    }

    public void ShowLoadingOverlay()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingState.Visibility = Visibility.Visible;
            ErrorState.Visibility = Visibility.Collapsed;
            LoadingOverlay.Visibility = Visibility.Visible;
        });
    }

    public void HideLoadingOverlay()
    {
        DispatcherQueue.TryEnqueue(() => LoadingOverlay.Visibility = Visibility.Collapsed);
    }

    public void ShowPlaybackError(string message)
        => ShowPlaybackError("Media Unavailable", message);

    public void ShowPlaybackError(string title, string detail)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingState.Visibility = Visibility.Collapsed;
            ErrorState.Visibility = Visibility.Visible;
            ErrorTitle.Text = title;
            ErrorDetail.Text = detail;
            LoadingOverlay.Visibility = Visibility.Visible;
        });
    }

    private void ErrorDismiss_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private static void LogState(string msg)
    {
        LocalLog.AppendLine("state_trace.txt", msg);
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
            if (ReferenceEquals(NavView.MenuItems[i], LibrariesHeader))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0) return;

        // Remove old library items (between LibrariesHeader and the next section)
        int removeStart = headerIndex + 1;
        while (removeStart < NavView.MenuItems.Count &&
               NavView.MenuItems[removeStart] is not NavigationViewItemHeader &&
               !ReferenceEquals(NavView.MenuItems[removeStart], LibrariesHeader))
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

            // Nested pinned collections under this library (webui parity,
            // sidebar_pins user setting).
            if (_sidebarPins.TryGetValue(lib.Id.ToString(), out var pins))
            {
                navItem.IsExpanded = true;
                foreach (var pin in pins)
                {
                    var pinItem = new NavigationViewItem
                    {
                        Content = pin.Label,
                        Tag = new SidebarPinNavTag { LibraryId = lib.Id, PinType = pin.Type, PinId = pin.Id, Label = pin.Label },
                        Icon = new FontIcon { Glyph = pin.Type == "collection" ? "\uE8F0" : "\uE8A5" }, // Folder / List
                    };
                    navItem.MenuItems.Add(pinItem);
                }
            }

            NavView.MenuItems.Insert(insertIndex++, navItem);
        }
    }

    // ===== Sidebar pins (webui parity: sidebar_pins user setting) =====

    private sealed class SidebarPinRow
    {
        public string Type { get; set; } = "";
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private sealed class SidebarPinNavTag
    {
        public int LibraryId { get; set; }
        public string PinType { get; set; } = "";
        public string PinId { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private Dictionary<string, List<SidebarPinRow>> _sidebarPins = [];

    /// <summary>
    /// Reloads the cached sidebar pins from the server setting and rebuilds
    /// the library nav. Call on sign-in and after any CollectionBrowsePage
    /// pin toggle.
    /// </summary>
    public async Task RefreshSidebarPinsAsync()
    {
        try
        {
            var settingsApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.SettingsApi>();
            var entry = await settingsApi.GetSettingAsync("sidebar_pins");
            _sidebarPins = ParseSidebarPins(entry.Value);
        }
        catch
        {
            _sidebarPins = [];
        }
        UpdateLibraryNavItems();
    }

    private static Dictionary<string, List<SidebarPinRow>> ParseSidebarPins(string? raw)
    {
        var map = new Dictionary<string, List<SidebarPinRow>>();
        if (string.IsNullOrWhiteSpace(raw)) return map;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return map;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                var entries = new List<SidebarPinRow>();
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    entries.Add(new SidebarPinRow
                    {
                        Type = el.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                        Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Label = el.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : "",
                    });
                }
                if (entries.Count > 0) map[prop.Name] = entries;
            }
        }
        catch { }
        return map;
    }

    // ===== Collapsible Libraries =====

    private bool _librariesExpanded = true;

    private void LibrariesHeader_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        _librariesExpanded = !_librariesExpanded;
        LibrariesChevron.Glyph = _librariesExpanded ? "\uE972" : "\uE974"; // down : right

        // Toggle visibility of all library items between LibrariesHeader and the next section header
        int headerIndex = -1;
        for (int i = 0; i < NavView.MenuItems.Count; i++)
        {
            if (ReferenceEquals(NavView.MenuItems[i], LibrariesHeader)) { headerIndex = i; break; }
        }
        if (headerIndex < 0) return;

        for (int i = headerIndex + 1; i < NavView.MenuItems.Count; i++)
        {
            if (NavView.MenuItems[i] is NavigationViewItemHeader) break;
            if (NavView.MenuItems[i] is NavigationViewItem navItem)
            {
                // Only toggle library items (they have a Library tag)
                if (navItem.Tag is ContinuumPlayer.Core.Models.Catalog.Library)
                    navItem.Visibility = _librariesExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        e.Handled = true;
    }

    // ===== Theme Switcher Dots =====

    private static readonly (string Id, string Label, string BgHex, string AccentHex)[] CuratedThemes =
    [
        ("midnight-cinema", "Cinema Dark", "#141417", "#e8e8ec"),
        ("cinema-light", "Cinema Light", "#f4f4f6", "#1a1a1e"),
        ("cobalt-studio", "Cobalt", "#101722", "#78aefc"),
        ("oxblood-noir", "Oxblood", "#171113", "#d16a78"),
        ("evergreen-studio", "Evergreen", "#101715", "#5bc39d"),
    ];

    private string _activeThemeId = "cobalt-studio";

    private void BuildThemeDots()
    {
        ThemeDotsPanel.Children.Clear();
        ThemeDotsPanel.Visibility = Visibility.Visible;

        foreach (var (id, label, bgHex, accentHex) in CuratedThemes)
        {
            bool isActive = id == _activeThemeId;
            var dot = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseHexColor(bgHex)),
                BorderBrush = isActive
                    ? (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AccentBrush"]
                    : (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(isActive ? 2 : 1),
            };
            // Inner accent dot
            dot.Child = new Border
            {
                Width = 8, Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseHexColor(accentHex)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(dot, label);

            var capturedId = id;
            dot.Tapped += (_, _) =>
            {
                _activeThemeId = capturedId;
                BuildThemeDots(); // Rebuild to update selection ring
                // TODO: Apply theme colors when multi-theme support is added
            };

            ThemeDotsPanel.Children.Add(dot);
        }
    }

    private static Windows.UI.Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[0..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate<SettingsPage>();
    }

    private void Admin_Click(object sender, RoutedEventArgs e)
    {
        NavView.IsPaneVisible = false;
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
                case "Calendar":
                    _navigationService.Navigate<CalendarPage>();
                    break;
                case "Favorites":
                    _navigationService.Navigate<FavoritesPage>();
                    break;
                case "Watchlist":
                    _navigationService.Navigate<WatchlistPage>();
                    break;
                case "WatchParty":
                    _navigationService.Navigate<WatchTogetherJoinPage>();
                    break;
                case "History":
                    _navigationService.Navigate<HistoryPage>();
                    break;
                case "Collections":
                    _navigationService.Navigate<CollectionsPage>();
                    break;
                case "Downloads":
                    _navigationService.Navigate<DownloadsPage>();
                    break;
            }
        }
        else if (args.InvokedItemContainer is NavigationViewItem libItem && libItem.Tag is Library library)
        {
            _navigationService.Navigate<LibraryPage>(library);
        }
        else if (args.InvokedItemContainer is NavigationViewItem pinItem && pinItem.Tag is SidebarPinNavTag pinTag)
        {
            // Pinned collection: navigate to the CollectionBrowsePage.
            _navigationService.Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = pinTag.PinId,
                Title = pinTag.Label,
                IsUserCollection = false,
                LibraryId = pinTag.LibraryId,
            });
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

    // ===== Impersonation =====

    public void ShowImpersonationBanner(string username, string impersonatorUsername = "")
    {
        _viewModel.IsImpersonating = true;
        _viewModel.ImpersonatedUsername = username;
        ImpersonationBannerControl.ImpersonatedUsername = username;
        ImpersonationBannerControl.ImpersonatorUsername = impersonatorUsername;
        ImpersonationBannerControl.Visibility = Visibility.Visible;
    }

    public void HideImpersonationBanner()
    {
        _viewModel.IsImpersonating = false;
        _viewModel.ImpersonatedUsername = "";
        ImpersonationBannerControl.ImpersonatorUsername = "";
        ImpersonationBannerControl.Visibility = Visibility.Collapsed;
    }

    private void ImpersonationBanner_EndRequested(object? sender, EventArgs e)
    {
        // End impersonation: restore original admin tokens
        // For now, the simplest approach is to log out and require re-login
        _authService.Logout();
        HideImpersonationBanner();
        HideMainNavigation();
        _navigationService.Navigate<ServerSelectPage>();
    }
}
