using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

namespace SiloPlayer;

public sealed class PlayingNextOnDeckItem
{
    public string ContentId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string TimeLeft { get; init; } = "";
    public string? ThumbnailUrl { get; init; }
    public double ProgressPercent { get; init; }
}

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigationService;
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly CredentialStore _credentialStore;
    private readonly AuthService _authService;
    private readonly AuthApi _authApi;
    private readonly SiloApiClient _apiClient;
    private readonly SettingsApi _settingsApi;
    private readonly CatalogApi _catalogApi;
    private readonly RequestsApi _requestsApi;
    private readonly NotificationsApi _notificationsApi;
    private readonly EventChannelClient _eventChannel;
    private readonly PlayerService _playerService;
    private readonly ThemeService _themeService;
    private readonly UICustomizationService _uiCustomizationService;
    private readonly AsyncLoadVersionGate _brandingLoadGate = new();
    private bool _notificationsAvailable = true;
    private int _notificationUnreadCount;
    private bool _isNarrowShell;
    private bool _desktopSidebarOpen = true;
    private bool _synchronizingDesktopPaneState;
    private long _paneOpenPropertyCallbackToken;
    private bool _mobileHeaderHidden;
    private bool _isWindowActive;
    private double _currentWindowWidth = 1280;
    private Type? _lastShellPageType;
    private object? _lastShellParameter;
    private IDisposable? _notificationSubscription;
    private string? _notificationSubscriptionShellKey;
    private CancellationTokenSource? _shellHydrationCts;
    private string? _pendingActivationArgument;
    private bool _navigationHostLoaded;
    private Style? _profileFlyoutBaseStyle;
    private Controls.GlobalSearchDialog? _activeGlobalSearch;
    private string _globalSearchQuery = "";

    public MainWindow()
    {
        this.InitializeComponent();
        _profileFlyoutBaseStyle = ProfileFooterFlyout.FlyoutPresenterStyle;
        RootGrid.SizeChanged += (_, _) => ReflowProfileFlyout();
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            var icon = (item.Tag as string) switch
            {
                "Home" => "house", "Search" => "search", "Recommendations" => "sparkles",
                "Requests" => "send", "Calendar" => "calendar-days", "Notifications" => "bell",
                "Favorites" => "heart", "Watchlist" => "list", "WatchParty" => "users-round",
                "Collections" => "folder-open", "History" => "clock", "Downloads" => "download", _ => null,
            };
            if (icon != null) item.Icon = SiloPlayer.Controls.WebUiIcon.Navigation(icon);
        }

        // LeftCompact uses an in-app acrylic pane by default. Override the
        // NavigationView theme resource with Silo's live sidebar brush so the
        // expanded pane is opaque and continues following server theme changes.
        var sidebarBackground = Application.Current.Resources["SidebarBackgroundBrush"];
        NavView.Resources["NavigationViewDefaultPaneBackground"] = sidebarBackground;
        NavView.Resources["NavigationViewExpandedPaneBackground"] = sidebarBackground;

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
        _desktopSidebarOpen = _settingsService.Load().DesktopSidebarOpen;
        _credentialStore = App.Services.GetRequiredService<CredentialStore>();
        _authService = App.Services.GetRequiredService<AuthService>();
        _authApi = App.Services.GetRequiredService<AuthApi>();
        _apiClient = App.Services.GetRequiredService<SiloApiClient>();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        _requestsApi = App.Services.GetRequiredService<RequestsApi>();
        _notificationsApi = App.Services.GetRequiredService<NotificationsApi>();
        _eventChannel = App.Services.GetRequiredService<EventChannelClient>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        _uiCustomizationService.Changed += OnUICustomizationChanged;

        NavView.PaneOpening += NavView_PaneOpening;
        NavView.PaneOpened += NavView_PaneOpened;
        NavView.PaneClosed += NavView_PaneClosed;
        RootGrid.AddHandler(
            UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler(RootGrid_KeyDown),
            handledEventsToo: true);
        NavView.AddHandler(
            UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(NavView_PointerPressed),
            handledEventsToo: true);
        NavView.AddHandler(
            UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler(NavView_KeyDown),
            handledEventsToo: true);
        _paneOpenPropertyCallbackToken = NavView.RegisterPropertyChangedCallback(
            NavigationView.IsPaneOpenProperty,
            OnNavViewIsPaneOpenChanged);
        UpdateSidebarPanePresentation(NavView.IsPaneOpen);

        _navigationService.Frame = ContentFrame;

        // F7: update window title on every navigation.
        _navigationService.Navigated += OnNavigated_UpdateWindowTitle;
        _navigationService.Navigated += OnNavigated_ConsumePendingActivation;
        _navigationService.Navigated += OnNavigated_ApplyAccessibility;
        _navigationService.Navigated += OnNavigated_SynchronizeShellChrome;
        _navigationService.Navigated += OnNavigated_AnimatePageEntrance;
        if (AppWindow != null) AppWindow.Title = DocumentTitle.AppName;

        // F2: register the toast host with the ToastService so any VM/page
        // can call App.Services.GetRequiredService<ToastService>().Success(...).
        var toastService = App.Services.GetRequiredService<ToastService>();
        toastService.Register(ToastHost, DispatcherQueue);

        // Hide the nav view initially -- it shows only after login.
        NavView.IsPaneVisible = false;

        // Listen for player state changes
        _playerService = App.Services.GetRequiredService<PlayerService>();
        _playerService.StateChanged += OnPlayerStateChanged;
        MiniPlayerBarControl.SizeChanged += (_, args) =>
        {
            if (_playerService.State == PlayerState.Minimized && _playerService.IsAudiobook)
                NavView.Margin = new Thickness(0, 0, 0, Controls.MiniPlayerBar.AudiobookHeightForWidth(args.NewSize.Width));
        };
        _authService.LoggedOut += OnAuthLoggedOut;
        _authService.UserChanged += OnAccountAuthorityChanged;
        _authService.ProfileVerificationRequired += OnProfileVerificationRequired;
        _authService.CredentialStoreFailed += OnCredentialStoreFailed;
        _eventChannel.SnapshotReceived += OnShellEventSnapshot;
        _eventChannel.EventReceived += OnShellEvent;
        _eventChannel.AccessChanged += OnAccessChanged;

        // Playing Next cinematic overlay — enters during the final 30 seconds
        // of a series episode and marks true EOF separately. PlayerOverlay used to own this but its
        // Activate() is never called, so the subscription lives here now.
        _playerService.ShowPlayingNextRequested += OnShowPlayingNextRequested;
        _playerService.PostRollReturnRequested += OnPostRollReturnRequested;

        // Keep native video window matched to main window size
        this.SizeChanged += OnWindowSizeChanged;
        this.Activated += OnWindowActivated;

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
        _currentWindowWidth = e.Size.Width;
        ApplyResponsiveShellLayout();
        _playerService.HandleWindowResize();
        UpdatePlayingNextLayout(e.Size.Width, e.Size.Height);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (_isWindowActive && CanExposeAuthenticatedNavigation)
            _ = RunShellWorkAsync("shared_appearance_focus", () =>
                _themeService.RefreshSharedAppearanceIfStaleAsync(
                    _shellHydrationCts?.Token ?? CancellationToken.None));
    }

    private void ApplyResponsiveShellLayout()
    {
        var isNarrow = _currentWindowWidth < 1024;
        var wasNarrow = _isNarrowShell;
        _isNarrowShell = isNarrow;
        if (!isNarrow)
        {
            SetMobileHeaderHidden(false);
        }

        // Layout.tsx hides the fixed desktop sidebar below Tailwind's lg
        // breakpoint and exposes navigation through a mobile menu. WinUI's
        // LeftMinimal mode is the native equivalent: content receives the
        // full window width and the pane opens as an overlay from its toggle.
        // Keep desktop display mode and pane state aligned. Left is the stable,
        // side-by-side expanded state and does not auto-dismiss after an item is
        // invoked. LeftCompact is the stable icon-rail state. Forcing a closed
        // pane while leaving it in Left mode, or an open pane in LeftCompact,
        // lets NavigationView fight the user's choice during navigation.
        var desiredPaneDisplayMode = isNarrow
            ? NavigationViewPaneDisplayMode.LeftMinimal
            : _desktopSidebarOpen
                ? NavigationViewPaneDisplayMode.Left
                : NavigationViewPaneDisplayMode.LeftCompact;
        if (NavView.PaneDisplayMode != desiredPaneDisplayMode)
            NavView.PaneDisplayMode = desiredPaneDisplayMode;

        // Desktop sidebar state is controlled only by NavigationView's
        // explicit toggle. The custom mobile header owns its own menu button.
        var shouldShowDesktopToggle = !isNarrow;
        if (NavView.IsPaneToggleButtonVisible != shouldShowDesktopToggle)
            NavView.IsPaneToggleButtonVisible = shouldShowDesktopToggle;

        var mobileHeaderVisibility =
            isNarrow &&
            NavView.IsPaneVisible &&
            CanExposeAuthenticatedNavigation
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (MobileShellHeader.Visibility != mobileHeaderVisibility)
            MobileShellHeader.Visibility = mobileHeaderVisibility;

        if (!NavView.IsPaneVisible)
            return;

        if (isNarrow)
        {
            // Entering the overlay-style mobile shell closes the desktop pane
            // once, without overwriting the user's desktop preference.
            if (!wasNarrow && NavView.IsPaneOpen)
                NavView.IsPaneOpen = false;
        }
        else
        {
            // LeftCompact is the WinUI mode designed for a persistent icon rail.
            // Reassigning IsPaneOpen on every page load restarts NavigationView's
            // pane transition, so only change it when the real state differs.
            if (NavView.IsPaneOpen != _desktopSidebarOpen)
                NavView.IsPaneOpen = _desktopSidebarOpen;
        }

        UpdateSidebarPanePresentation(NavView.IsPaneOpen);
    }

    public void SetMobileHeaderHidden(bool hidden)
    {
        hidden &= _isNarrowShell;
        if (_mobileHeaderHidden == hidden)
            return;

        _mobileHeaderHidden = hidden;
        MobileShellHeader.Translation = hidden
            ? new System.Numerics.Vector3(0, -96, 0)
            : System.Numerics.Vector3.Zero;
        MobileShellHeader.Opacity = hidden ? 0 : 1;
        MobileShellHeader.IsHitTestVisible = !hidden;
    }

    private void MobileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!CanExposeAuthenticatedNavigation)
            return;

        NavView.IsPaneOpen = true;
        UpdateSidebarPanePresentation(isOpen: true);
    }

    private void MobileHome_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate<HomePage>();
        CloseMobileNavigationPane();
    }

    private void MobileSearch_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate<SearchPage>();
        CloseMobileNavigationPane();
    }

    private void MobileProfile_Click(object sender, RoutedEventArgs e)
    {
        // Layout.tsx links the compact-header avatar directly to playback
        // settings; the full account menu remains in the sidebar drawer.
        _navigationService.Navigate<SettingsPage>();
        CloseMobileNavigationPane();
    }

    private void CloseMobileNavigationPane()
    {
        if (!_isNarrowShell)
            return;

        NavView.IsPaneOpen = false;
        UpdateSidebarPanePresentation(isOpen: false);
    }

    private void NavView_PaneOpening(object sender, object e)
    {
        if (!_isNarrowShell && NavView.IsPaneVisible && CanExposeAuthenticatedNavigation)
        {
            if (!_desktopSidebarOpen)
            {
                SynchronizeDesktopPaneState();
                return;
            }

            // Enter the side-by-side desktop mode before WinUI renders the
            // expanded pane. This avoids a transient acrylic LeftCompact frame
            // and prevents item invocation from treating the pane as a flyout.
            if (NavView.PaneDisplayMode != NavigationViewPaneDisplayMode.Left)
                NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        }
    }

    private void NavView_PaneOpened(object sender, object e)
    {
        if (!_isNarrowShell && NavView.IsPaneVisible && CanExposeAuthenticatedNavigation)
        {
            SynchronizeDesktopPaneState();
        }
        else
        {
            UpdateSidebarPanePresentation(isOpen: true);
        }
    }

    private void NavView_PaneClosed(object sender, object e)
    {
        if (!_isNarrowShell && NavView.IsPaneVisible && CanExposeAuthenticatedNavigation)
        {
            SynchronizeDesktopPaneState();
        }
        else
        {
            UpdateSidebarPanePresentation(isOpen: false);
        }
    }

    private void ProfileFooterFlyout_Opening(object sender, object e)
    {
        // Keep the user's pane state unchanged while presenting the profile
        // menu beside a compact rail or above an expanded sidebar.
        ProfileFooterFlyout.Placement =
            !NavView.IsPaneOpen
                ? FlyoutPlacementMode.RightEdgeAlignedBottom
                : FlyoutPlacementMode.Top;
        ReflowProfileFlyout();
    }

    private void ReflowProfileFlyout()
    {
        if (RootGrid.XamlRoot == null) return;
        var width = Math.Min(320, Math.Max(1, RootGrid.XamlRoot.Size.Width - 24));
        var height = Math.Max(1, RootGrid.XamlRoot.Size.Height - 24);
        var style = new Style(typeof(FlyoutPresenter)) { BasedOn = _profileFlyoutBaseStyle };
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, width));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, width));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, height));
        ProfileFooterFlyout.FlyoutPresenterStyle = style;
        ProfileMenuScroller.MaxHeight = Math.Max(1, height - 18);
        foreach (var button in new[] { ProfileSwitchButton, ProfileLogoutButton })
        {
            button.Background = ProfileMenuBrush("SidebarAccentBrush", .4);
            button.BorderBrush = ProfileMenuBrush("SidebarBorderBrush", .6);
            button.Resources["ButtonBackgroundPointerOver"] = Application.Current.Resources["SidebarAccentBrush"];
            button.Resources["ButtonBackgroundPressed"] = Application.Current.Resources["SidebarAccentBrush"];
            button.Resources["ButtonBorderBrushPointerOver"] = button.BorderBrush;
            button.Resources["ButtonBorderBrushPressed"] = button.BorderBrush;
        }
    }

    private static Microsoft.UI.Xaml.Media.Brush ProfileMenuBrush(string resource, double opacity)
        => Application.Current.Resources[resource] is Microsoft.UI.Xaml.Media.SolidColorBrush brush
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(brush.Color) { Opacity = opacity }
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[resource];

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
        CancelShellHydration();
        ReleaseNotificationSubscription();
        _eventChannel.SnapshotReceived -= OnShellEventSnapshot;
        _eventChannel.EventReceived -= OnShellEvent;
        _eventChannel.AccessChanged -= OnAccessChanged;
        _playerService.StateChanged -= OnPlayerStateChanged;
        _uiCustomizationService.Changed -= OnUICustomizationChanged;
        _authService.LoggedOut -= OnAuthLoggedOut;
        _authService.UserChanged -= OnAccountAuthorityChanged;
        _authService.ProfileVerificationRequired -= OnProfileVerificationRequired;
        _authService.CredentialStoreFailed -= OnCredentialStoreFailed;
        _playerService.ShowPlayingNextRequested -= OnShowPlayingNextRequested;
        _playerService.PostRollReturnRequested -= OnPostRollReturnRequested;
        this.SizeChanged -= OnWindowSizeChanged;
        this.Activated -= OnWindowActivated;
        if (AppWindow != null) AppWindow.Changed -= OnAppWindowChanged;
        _navigationService.Navigated -= OnNavigated_UpdateWindowTitle;
        _navigationService.Navigated -= OnNavigated_ConsumePendingActivation;
        _navigationService.Navigated -= OnNavigated_ApplyAccessibility;
        _navigationService.Navigated -= OnNavigated_SynchronizeShellChrome;
        _navigationService.Navigated -= OnNavigated_AnimatePageEntrance;
        if (_paneOpenPropertyCallbackToken != 0)
        {
            NavView.UnregisterPropertyChangedCallback(
                NavigationView.IsPaneOpenProperty,
                _paneOpenPropertyCallbackToken);
            _paneOpenPropertyCallbackToken = 0;
        }
        StopPlayingNextCountdown();
    }

    // ─── Playing Next cinematic overlay ─────────────────────────────────

    private DispatcherTimer? _playingNextTimer;
    private int _playingNextRemaining;
    private bool _playingNextAutoPlay = true;
    private long _playingNextPresentationGeneration;
    private const int PlayingNextCountdownSeconds = 10;
    private const string AutoPlayNextSettingKey = "playback.auto_play_next";

    private void OnShowPlayingNextRequested(bool videoEnded)
    {
        // mpv raises playback events off the UI thread. Remember which item
        // owned this request so a queued callback from the outgoing episode
        // cannot reopen post-roll and disable the successor episode's OSC.
        var ownerContentId = _playerService.ContentId;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_playerService.IsSwitchingContent ||
                !string.Equals(ownerContentId, _playerService.ContentId, StringComparison.Ordinal))
            {
                return;
            }

            var shouldFocusOverlay = _isWindowActive || _playerService.IsPlaybackSurfaceForeground;

            // The early post-roll surface can already be visible when true EOF
            // arrives. In that case only transition the preview/countdown state;
            // do not rebuild the artwork and On Deck data.
            if (videoEnded && PlayingNextOverlay.Visibility == Visibility.Visible)
            {
                _playerService.FinishPostRollPreview();
                UpdatePlayingNextAutoPlayVisuals();
                if (_playerService.HasNextEpisodeForCurrentPlayback &&
                    _playingNextAutoPlay)
                {
                    StartPlayingNextCountdown();
                }
                return;
            }

            var hasNextEpisode = _playerService.HasNextEpisodeForCurrentPlayback;
            var presentationGeneration = Interlocked.Increment(ref _playingNextPresentationGeneration);
            var title = _playerService.NextEpisodeTitle ?? "Next episode";
            var series = _playerService.NextEpisodeSeriesTitle;
            var overview = _playerService.NextEpisodeOverview ?? "";

            PlayingNextLabelText.Text = hasNextEpisode ? "PLAYING NEXT" : "FINISHED";
            PlayingNextEpisodePanel.Visibility = hasNextEpisode ? Visibility.Visible : Visibility.Collapsed;
            PlayingNextFinishedPanel.Visibility = hasNextEpisode ? Visibility.Collapsed : Visibility.Visible;
            PlayingNextFinishedHeading.Text = !string.IsNullOrWhiteSpace(_playerService.WatchDetail?.SeriesTitle)
                ? $"You've finished {_playerService.WatchDetail.SeriesTitle}"
                : "End of playback";

            PlayingNextTitleText.Text = title;
            PlayingNextSeriesText.Text = series ?? "";
            PlayingNextSeriesText.Visibility = string.IsNullOrEmpty(series) ? Visibility.Collapsed : Visibility.Visible;
            PlayingNextOverviewText.Text = overview;
            PlayingNextOverviewText.Visibility = string.IsNullOrEmpty(overview) ? Visibility.Collapsed : Visibility.Visible;
            PlayingNextMetaText.Text = FormatPlayingNextMeta(
                _playerService.NextEpisodeAirDate,
                _playerService.NextEpisodeRuntime);
            PlayingNextMetaText.Visibility = string.IsNullOrEmpty(PlayingNextMetaText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            PlayingNextPoster.Source = null;
            PlayingNextBackdrop.Source = null;
            if (hasNextEpisode)
                _ = LoadPlayingNextPosterAsync();
            PlayingNextOnDeckRepeater.ItemsSource = null;
            PlayingNextOnDeckSection.Visibility = Visibility.Collapsed;
            _ = LoadPlayingNextOnDeckAsync();

            var playbackHasEnded = videoEnded || _playerService.IsPostRollVideoEnded;
            _playingNextRemaining = PlayingNextCountdownSeconds;
            PlayingNextCountdownText.Text = $"{_playingNextRemaining}s";
            PlayingNextCountdownRing.Value = _playingNextRemaining;
            PlayingNextPlayNowText.Text = "Play Now";
            UpdatePlayingNextAutoPlayVisuals();

            // The native mpv popup is an owned HWND above the WinUI content.
            // Move or hide it before exposing the overlay so it cannot consume
            // the first pointer interaction intended for Playing Next.
            if (playbackHasEnded)
                _playerService.FinishPostRollPreview();
            else
                _playerService.EnterPostRollPreview();

            PlayingNextOverlay.Visibility = Visibility.Visible;

            // Match the current WebUI: entering post-roll early does not start
            // autoplay while the episode is still visibly playing.
            if (playbackHasEnded && hasNextEpisode && _playingNextAutoPlay)
                StartPlayingNextCountdown();

            // Never request focus from the background. Windows can translate a
            // focus request on an inactive app into a flashing taskbar button,
            // which was especially visible when auto-next advanced while the
            // user was working in another application. Active playback still
            // receives the expected keyboard/controller focus.
            if (shouldFocusOverlay)
            {
                if (hasNextEpisode)
                    PlayingNextPlayNowButton.Focus(FocusState.Programmatic);
                else
                    PlayingNextCloseButton.Focus(FocusState.Programmatic);
            }

            // The effective-setting request must never hold the entire
            // post-roll surface behind network latency. Early post-roll gives
            // this refresh up to 30 seconds to finish before true EOF.
            _ = RefreshPlayingNextAutoPlayAsync(presentationGeneration);
        });
    }

    private void OnPostRollReturnRequested()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Increment(ref _playingNextPresentationGeneration);
            StopPlayingNextCountdown();
            PlayingNextOverlay.Visibility = Visibility.Collapsed;
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
        PlayingNextCountdownText.Text = $"{_playingNextRemaining}s";
        PlayingNextCountdownRing.Value = _playingNextRemaining;
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

    private async Task RefreshPlayingNextAutoPlayAsync(long presentationGeneration)
    {
        var autoPlay = await GetPlayingNextAutoPlayAsync();
        if (presentationGeneration != Volatile.Read(ref _playingNextPresentationGeneration) ||
            PlayingNextOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        _playingNextAutoPlay = autoPlay;
        if (!autoPlay)
        {
            StopPlayingNextCountdown();
            _playingNextRemaining = PlayingNextCountdownSeconds;
            PlayingNextCountdownText.Text = $"{_playingNextRemaining}s";
            PlayingNextCountdownRing.Value = _playingNextRemaining;
        }
        UpdatePlayingNextAutoPlayVisuals();
        if (_playerService.IsPostRollVideoEnded &&
            _playerService.HasNextEpisodeForCurrentPlayback &&
            autoPlay &&
            _playingNextTimer == null)
        {
            _playingNextRemaining = PlayingNextCountdownSeconds;
            PlayingNextCountdownText.Text = $"{_playingNextRemaining}s";
            PlayingNextCountdownRing.Value = _playingNextRemaining;
            StartPlayingNextCountdown();
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
        PlayingNextCountdownText.Text = $"{_playingNextRemaining}s";
        PlayingNextCountdownRing.Value = _playingNextRemaining;

        if (_playingNextAutoPlay &&
            _playerService.IsPostRollVideoEnded &&
            PlayingNextOverlay.Visibility == Visibility.Visible)
            StartPlayingNextCountdown();
    }

    private void UpdatePlayingNextAutoPlayVisuals()
    {
        var hasNextEpisode = _playerService.HasNextEpisodeForCurrentPlayback;
        PlayingNextCountdownPanel.Visibility = hasNextEpisode &&
            _playingNextAutoPlay &&
            _playerService.IsPostRollVideoEnded
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlayingNextAutoplayToggle.Visibility = hasNextEpisode ? Visibility.Visible : Visibility.Collapsed;
        PlayingNextAutoplayToggleText.Text = _playingNextAutoPlay
            ? "Auto-play is on"
            : "Auto-play is off";
    }

    private async void PlayingNextPlayNow_Click(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _playingNextPresentationGeneration);
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
        => DismissPlayingNext();

    private void PlayingNextClose_PointerPressed(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed)
            return;

        // Dismiss on pointer-down so the owned native video HWND cannot regain
        // the gesture before Button.Click is raised during a post-roll resize.
        e.Handled = true;
        DismissPlayingNext();
    }

    private void DismissPlayingNext()
    {
        if (PlayingNextOverlay.Visibility != Visibility.Visible)
            return;

        Interlocked.Increment(ref _playingNextPresentationGeneration);
        StopPlayingNextCountdown();
        PlayingNextOverlay.Visibility = Visibility.Collapsed;
        _playerService.CancelPlayingNext();
    }

    private async Task LoadPlayingNextPosterAsync()
    {
        var url = _playerService.NextEpisodePosterUrl;
        var contentId = _playerService.NextEpisodeContentId;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var path = await imageService.GetImageDiskPathAsync(
                contentId ?? url,
                "playing-next",
                url,
                httpClient);
            if (string.IsNullOrWhiteSpace(path)) return;
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path))
            {
                DecodePixelWidth = 640,
            };
            if (!string.Equals(contentId, _playerService.NextEpisodeContentId, StringComparison.Ordinal) ||
                PlayingNextOverlay.Visibility != Visibility.Visible)
            {
                return;
            }
            PlayingNextPoster.Source = bitmap;
            PlayingNextBackdrop.Source = bitmap;
        }
        catch { /* Poster is cosmetic */ }
    }

    private static string FormatPlayingNextMeta(string? airDate, int runtimeSeconds)
    {
        var parts = new List<string>();
        if (DateTimeOffset.TryParse(airDate, out var parsedDate))
            parts.Add(DateTimeDisplay.FormatDate(parsedDate, medium: true));
        if (runtimeSeconds > 0)
            parts.Add($"{Math.Max(1, (int)Math.Round(runtimeSeconds / 60d))} min");
        return string.Join("  •  ", parts);
    }

    private void UpdatePlayingNextLayout(double windowWidth, double windowHeight)
    {
        if (PlayingNextHero == null || windowWidth <= 0 || windowHeight <= 0)
            return;

        var availableWidth = Math.Min(820, Math.Max(280, windowWidth - 48));
        var aspectHeight = availableWidth * 9d / 16d;
        PlayingNextHero.Height = Math.Max(180, Math.Min(400, Math.Min(windowHeight * 0.38, aspectHeight)));
    }

    private async Task LoadPlayingNextOnDeckAsync()
    {
        var nextContentId = _playerService.NextEpisodeContentId;
        var currentContentId = _playerService.WatchDetail?.ContentId;

        try
        {
            var currentSeriesId = _playerService.WatchDetail?.SeriesId;
            var sections = await App.Services.GetRequiredService<HomeApi>().GetSectionsAsync();
            if (!string.Equals(_playerService.WatchDetail?.ContentId, currentContentId, StringComparison.Ordinal) ||
                PlayingNextOverlay.Visibility != Visibility.Visible)
            {
                return;
            }

            var continueWatching = sections.Sections.FirstOrDefault(section =>
                string.Equals(section.SectionType, "continue_watching", StringComparison.OrdinalIgnoreCase));
            var cards = (continueWatching?.Items ?? [])
                .Where(item =>
                    !string.Equals(item.ContentId, currentContentId, StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(nextContentId) ||
                     !string.Equals(item.ContentId, nextContentId, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(currentSeriesId) ||
                     !string.Equals(item.SeriesId, currentSeriesId, StringComparison.Ordinal)))
                .Select(item =>
                {
                    var duration = Math.Max(0, item.DurationSeconds ?? 0);
                    var position = Math.Clamp(item.PositionSeconds ?? 0, 0, duration > 0 ? duration : double.MaxValue);
                    var percent = duration > 0 ? Math.Clamp(position / duration * 100, 0, 100) : 0;
                    var minutesLeft = duration > position
                        ? Math.Max(1, (int)Math.Round((duration - position) / 60d))
                        : 0;
                    var episodeMeta = item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue
                        ? $"S{item.SeasonNumber}:E{item.EpisodeNumber}"
                        : "";
                    var displayTitle = string.IsNullOrWhiteSpace(item.SeriesTitle)
                        ? item.Title
                        : item.SeriesTitle;
                    var subtitle = episodeMeta;
                    if (!string.IsNullOrWhiteSpace(item.SeriesTitle) && !string.IsNullOrWhiteSpace(item.Title))
                        subtitle = string.IsNullOrWhiteSpace(subtitle) ? item.Title : $"{subtitle} · {item.Title}";
                    else if (string.IsNullOrWhiteSpace(subtitle) && item.Year > 0)
                        subtitle = item.Year.ToString();

                    return new PlayingNextOnDeckItem
                    {
                        ContentId = item.ContentId,
                        Title = displayTitle,
                        Subtitle = subtitle,
                        TimeLeft = minutesLeft > 0 ? $"{minutesLeft} min left" : "",
                        ThumbnailUrl = item.BackdropUrl ?? item.PosterUrl,
                        ProgressPercent = percent,
                    };
                })
                .ToList();

            PlayingNextOnDeckRepeater.ItemsSource = cards;
            PlayingNextOnDeckScroller.ChangeView(0, null, null, true);
            PlayingNextOnDeckPrev.IsEnabled = false;
            PlayingNextOnDeckNext.IsEnabled = cards.Count > 1;
            PlayingNextOnDeckSection.Visibility = cards.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine("state_trace.txt", $"Playing Next On Deck load failed: {ex.Message}");
        }
    }

    private void PlayingNextOnDeckPrev_Click(object sender, RoutedEventArgs e)
    {
        var amount = Math.Max(320, PlayingNextOnDeckScroller.ViewportWidth * 0.8);
        PlayingNextOnDeckScroller.ChangeView(
            Math.Max(0, PlayingNextOnDeckScroller.HorizontalOffset - amount), null, null);
    }

    private void PlayingNextOnDeckNext_Click(object sender, RoutedEventArgs e)
    {
        var amount = Math.Max(320, PlayingNextOnDeckScroller.ViewportWidth * 0.8);
        PlayingNextOnDeckScroller.ChangeView(
            Math.Min(PlayingNextOnDeckScroller.ScrollableWidth,
                PlayingNextOnDeckScroller.HorizontalOffset + amount), null, null);
    }

    private void PlayingNextOnDeckScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        const double epsilon = 1;
        PlayingNextOnDeckPrev.IsEnabled = PlayingNextOnDeckScroller.HorizontalOffset > epsilon;
        PlayingNextOnDeckNext.IsEnabled = PlayingNextOnDeckScroller.HorizontalOffset <
            PlayingNextOnDeckScroller.ScrollableWidth - epsilon;
    }

    private async void PlayingNextOnDeck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlayingNextOnDeckItem item })
            return;

        Interlocked.Increment(ref _playingNextPresentationGeneration);
        StopPlayingNextCountdown();
        PlayingNextOverlay.Visibility = Visibility.Collapsed;
        try
        {
            await _playerService.PlayAsync(item.ContentId);
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine("state_trace.txt", $"Playing Next On Deck playback failed: {ex}");
            ShowPlaybackError($"Failed to start playback: {ex.Message}");
        }
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
        // Navigation never changes pane state. The user's explicit sidebar
        // toggle remains authoritative across browse and detail routes.
    }

    /// <summary>Ctrl+K — global search palette (webui parity with GlobalSearch.tsx).</summary>
    private async void GlobalSearchAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_activeGlobalSearch != null)
        {
            _activeGlobalSearch.CloseFromShortcut();
            return;
        }
        try
        {
            var dlg = new Controls.GlobalSearchDialog { XamlRoot = this.Content.XamlRoot, InitialQuery = _globalSearchQuery };
            _activeGlobalSearch = dlg;
            await dlg.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GlobalSearchDialog failed: {ex.Message}");
        }
        finally
        {
            _globalSearchQuery = _activeGlobalSearch?.ReopenQuery ?? "";
            _activeGlobalSearch = null;
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
        _navigationHostLoaded = true;
        if (TryConsumePendingActivation()) return;
        if (IsResetActivationRoute(_pendingActivationArgument) && string.IsNullOrWhiteSpace(_apiClient.BaseUrl))
        {
            // A reset link cannot choose a server or restore an unrelated saved account.
            HideMainNavigation();
            _navigationService.Navigate<ServerSelectPage>();
            return;
        }
        await TryAutoLoginAsync();
    }

    private void SidebarBrandHost_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        e.Handled = true;
        NavigateToHome();
    }

    private void SidebarBrandHost_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return;
        e.Handled = true;
        NavigateToHome();
    }

    /// <summary>
    /// Receives supported launch arguments from App. Initial activation is
    /// held until the frame is loaded; later activations are handled in-place.
    /// </summary>
    public void ActivateFromArgument(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return;
        _pendingActivationArgument = argument;
        if (_navigationHostLoaded) TryConsumePendingActivation();
        Activate();
    }

    private bool TryConsumePendingActivation()
    {
        var argument = _pendingActivationArgument;
        if (IsResetActivationRoute(argument) && string.IsNullOrWhiteSpace(_apiClient.BaseUrl)) return false;
        argument = Interlocked.Exchange(ref _pendingActivationArgument, null);
        if (Uri.TryCreate(argument?.Trim().Trim('"'), UriKind.Absolute, out var callback) &&
            callback.Scheme == "org.siloserver.silo" && callback.Host.Length == 0 && callback.AbsolutePath == "/auth/callback")
        {
            _autoLoginAttempted = true;
            _ = HandleNativeOAuthActivationAsync(callback.AbsoluteUri);
            return true;
        }
        if (PasswordRecovery.TryParseActivation(argument, _apiClient.BaseUrl, out var resetLink))
        {
            _autoLoginAttempted = true;
            HideMainNavigation();
            _navigationService.Navigate<PasswordRecoveryPage>(resetLink);
            return true;
        }
        if (!InviteDeepLink.TryParse(argument, out var invitation)) return false;

        // The WebUI does not let an invitation replace an existing signed-in
        // session. Preserve that account and take it home rather than silently
        // clearing credentials or accepting as the wrong user.
        if (_authService.IsLoggedIn)
        {
            if (!string.IsNullOrWhiteSpace(_authService.SelectedProfileId))
                _navigationService.Navigate<HomePage>();
            return true;
        }

        HideMainNavigation();
        _navigationService.Navigate<InviteClaimPage>(invitation);
        return true;
    }

    private async Task HandleNativeOAuthActivationAsync(string callback)
    {
        try
        {
            if (await NativeOAuthCallbacks.TryHandleAsync(callback)) return;
            App.Services.GetRequiredService<ToastService>().Error("This sign-in attempt has expired. Start sign-in again from Silo.");
            if (ContentFrame.Content == null && !_authService.IsLoggedIn)
                _navigationService.Navigate<ServerSelectPage>();
        }
        catch (Exception ex)
        {
            // Callback query values contain one-use credentials; never include the URI or exception message.
            LocalLog.AppendLine("auth_errors.txt", $"native_callback_failed | type={ex.GetType().Name}");
            App.Services.GetRequiredService<ToastService>().Error("Silo could not complete browser sign-in. Please try again.");
        }
    }

    private static bool IsResetActivationRoute(string? argument)
        => Uri.TryCreate(argument?.Trim().Trim('"'), UriKind.Absolute, out var link)
            && link.Scheme is "http" or "https"
            && link.AbsolutePath.Contains("/reset-password/", StringComparison.Ordinal);

    private void OnNavigated_ConsumePendingActivation(object? sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (e.SourcePageType != typeof(PasswordRecoveryPage) && _pendingActivationArgument != null
            && !string.IsNullOrWhiteSpace(_apiClient.BaseUrl))
            DispatcherQueue.TryEnqueue(() => TryConsumePendingActivation());
    }

    private bool _autoLoginAttempted;
    private volatile bool _savedRestoreInProgress;

    private async Task TryAutoLoginAsync()
    {
        if (_autoLoginAttempted)
            return;
        _autoLoginAttempted = true;

        var settings = _settingsService.Load();
        HideMainNavigation();
        LocalLog.AppendLine("auth_startup.txt", $"restore_start | saved_servers={settings.Servers.Count}");

        var resolver = new SavedSessionCredentialResolver(_credentialStore);
        ServerEntry? server = null;
        ResolvedSavedSession? savedSession = null;
        foreach (var candidate in settings.Servers.OrderByDescending(entry => entry.LastUsed))
        {
            try
            {
                savedSession = resolver.Resolve(candidate.Url);
                if (savedSession == null)
                    continue;
                server = candidate;
                break;
            }
            catch (Exception ex)
            {
                LocalLog.AppendLine("auth_startup.txt", $"credential_read_failed | type={ex.GetType().Name}");
            }
        }

        if (server == null || savedSession == null)
        {
            LocalLog.AppendLine("auth_startup.txt", "restore_unavailable | reason=no_saved_refresh");
            _navigationService.Navigate<ServerSelectPage>();
            return;
        }

        if (!string.Equals(server.Url, savedSession.ServerUrl, StringComparison.Ordinal))
        {
            server.Url = savedSession.ServerUrl;
            _settingsService.Save(settings);
        }
        LocalLog.AppendLine(
            "auth_startup.txt",
            $"restore_candidate | migrated={savedSession.MigratedLegacyCredential}");

        _savedRestoreInProgress = true;
        _authService.ConfigureServer(savedSession.ServerUrl);
        var restoreGeneration = _authService.SetTokens(
            "",
            savedSession.RefreshToken,
            0,
            preserveStoredProfile: true,
            expectedServerUrl: savedSession.ServerUrl);

        var refreshed = false;
        for (var attempt = 1; attempt <= 2 && !refreshed; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(
                    attempt == 1 ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(8));
                refreshed = await _authService.TryRefreshAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                LocalLog.AppendLine("auth_startup.txt", $"refresh_timeout | attempt={attempt}");
            }
            catch (Exception ex)
            {
                LocalLog.AppendLine("auth_startup.txt", $"refresh_failed | attempt={attempt} | type={ex.GetType().Name}");
            }

            if (refreshed)
                break;

            string? persistedRefresh = null;
            try
            {
                persistedRefresh = _credentialStore.LoadCredential(savedSession.ServerUrl, "refresh_token");
            }
            catch (Exception ex)
            {
                LocalLog.AppendLine("auth_startup.txt", $"credential_recheck_failed | type={ex.GetType().Name}");
            }

            if (string.IsNullOrWhiteSpace(persistedRefresh))
            {
                LocalLog.AppendLine("auth_startup.txt", $"refresh_terminal | attempt={attempt}");
                break;
            }

            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(750));
        }

        if (refreshed)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    if (await CompleteAutoLoginAsync(server, settings, restoreGeneration, timeout.Token))
                    {
                        LocalLog.AppendLine("auth_startup.txt", $"restore_complete | attempt={attempt}");
                        _savedRestoreInProgress = false;
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    LocalLog.AppendLine("auth_startup.txt", $"bootstrap_timeout | attempt={attempt}");
                }
                catch (Exception ex)
                {
                    LocalLog.AppendLine("auth_startup.txt", $"bootstrap_failed | attempt={attempt} | type={ex.GetType().Name}");
                }

                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromMilliseconds(750));
            }
        }

        if (_authService.IsLoggedIn && _authService.SessionGeneration != restoreGeneration ||
            _authService.ConfiguredServerUrl != savedSession.ServerUrl)
        { _savedRestoreInProgress = false; return; }
        _authService.AbandonRestoreAttempt(restoreGeneration);
        _savedRestoreInProgress = false;
        HideMainNavigation();
        LocalLog.AppendLine("auth_startup.txt", "restore_abandoned");
        ShowSavedSessionRetry(server);
    }

    private void ShowSavedSessionRetry(ServerEntry server)
    {
        var expectedGeneration = _authService.SessionGeneration;
        LoginNavigationRequest? request = null;
        request = new(server, SessionRestoreUnavailable: true,
            SessionRestoreErrorCode: "session_restore_unavailable", RetryRestoreAsync: Retry);
        _navigationService.Navigate<LoginPage>(request);

        async Task<bool> Retry(CancellationToken ct)
        {
            if (ct.IsCancellationRequested || !_navigationService.IsCurrentEntry(typeof(LoginPage), request) ||
                _authService.SessionGeneration != expectedGeneration || _authService.ConfiguredServerUrl != server.Url)
                throw new OperationCanceledException(ct);
            // The first attempt may already have rotated its refresh token. Never reuse its snapshot.
            var saved = new SavedSessionCredentialResolver(_credentialStore).Resolve(server.Url);
            if (saved == null) throw new ApiException("session_expired", "The saved session has expired. Sign in again.", 401);
            _savedRestoreInProgress = true;
            var generation = _authService.SetTokens("", saved.RefreshToken, 0,
                preserveStoredProfile: true, expectedServerUrl: saved.ServerUrl);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                if (await _authService.TryRefreshAsync(timeout.Token) &&
                    _navigationService.IsCurrentEntry(typeof(LoginPage), request))
                    return await CompleteAutoLoginAsync(server, _settingsService.Load(), generation, timeout.Token);
                if (new SavedSessionCredentialResolver(_credentialStore).Resolve(server.Url) == null)
                    throw new ApiException("session_expired", "The saved session has expired. Sign in again.", 401);
                return false;
            }
            finally
            {
                // Success has entered the authenticated route. Failure retires only this attempt.
                if (_navigationService.IsCurrentEntry(typeof(LoginPage), request))
                {
                    _authService.AbandonRestoreAttempt(generation);
                    expectedGeneration = _authService.SessionGeneration;
                }
                _savedRestoreInProgress = false;
            }
        }
    }

    private static void OnNavigated_AnimatePageEntrance(
        object? sender,
        Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (e.Content is FrameworkElement content)
            PageTransitionHelper.AnimateEntrance(content);
    }

    private async Task<bool> CompleteAutoLoginAsync(
        ServerEntry server,
        AppSettings settings,
        long restoreGeneration,
        CancellationToken cancellationToken)
    {
        var user = _authService.CurrentUser ?? await _authApi.GetMeAsync(cancellationToken);
        if (!_authService.SetCurrentUser(user, restoreGeneration))
            return false;
        settings.LastUsername = user.Username;
        settings.LastUserRole = user.Role;

        if (user.PasswordChangeRequired)
        {
            HideMainNavigation();
            _navigationService.Navigate<ChoosePasswordPage>();
            return true;
        }

        var profilesResponse = await _authApi.GetProfilesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_authService.SessionGeneration != restoreGeneration || _authService.ConfiguredServerUrl != server.Url) return false;
        var selectedProfile = !string.IsNullOrEmpty(settings.LastProfileId)
            ? profilesResponse.Profiles.FirstOrDefault(profile => profile.Id == settings.LastProfileId)
            : null;

        selectedProfile ??= profilesResponse.Profiles.Count == 1 && !profilesResponse.Profiles[0].HasPin
            ? profilesResponse.Profiles[0]
            : null;

        if (selectedProfile == null)
        {
            _settingsService.Save(settings);
            HideMainNavigation();
            _navigationService.Navigate<ProfileSelectPage>();
            return true;
        }

        if (selectedProfile.HasPin)
        {
            var persistedProfile = _authService.LoadPersistedProfileSession(server.Url);
            if (persistedProfile is null ||
                !string.Equals(persistedProfile.Value.ProfileId, selectedProfile.Id, StringComparison.Ordinal))
            {
                _settingsService.Save(settings);
                HideMainNavigation();
                _navigationService.Navigate<ProfileSelectPage>();
                return true;
            }

            _authService.SelectProfile(
                selectedProfile.Id,
                persistedProfile.Value.ProfileToken,
                selectedProfile);
            _catalogApi.InvalidateLibraryCache();
            try
            {
                await _catalogApi.GetLibrariesAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (_authService.SessionGeneration != restoreGeneration || _authService.ConfiguredServerUrl != server.Url) return false;
            }
            catch (ApiException ex) when (ex.ErrorCode == "profile_unverified")
            {
                HideMainNavigation();
                _navigationService.Navigate<ProfileSelectPage>();
                return true;
            }
        }
        else
        {
            _authService.SelectProfile(selectedProfile.Id, profile: selectedProfile);
            _catalogApi.InvalidateLibraryCache();
        }

        settings.LastProfileId = selectedProfile.Id;
        server.LastUsed = DateTime.UtcNow;
        _settingsService.Save(settings);
        if (!TryEnterAuthenticatedPage(typeof(HomePage), null, out var navigationFailure))
        {
            // Authentication and profile restoration already succeeded. A XAML
            // or page-construction failure must never discard the valid rotated
            // refresh token and force another login.
            LocalLog.AppendLine(
                "auth_startup.txt",
                $"home_navigation_failed | type={navigationFailure?.GetType().Name ?? "Unknown"}");
            HideMainNavigation();
            _navigationService.Navigate<ProfileSelectPage>();
        }
        return true;
    }

    private bool _navInitialized;
    private bool _pendingAccessRefresh;
    private string? _hydratedShellKey;
    private bool _playerPrewarmed;

    private sealed record PluginAppNavTag(int InstallationId, string RoutePath, string Label);

    private bool CanExposeAuthenticatedNavigation =>
        _authService.IsLoggedIn &&
        !_authService.PasswordChangeRequired &&
        !string.IsNullOrWhiteSpace(_authService.SelectedProfileId);

    private void OnAccessChanged(ApiRequestContext context)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_apiClient.IsCurrentContext(context) || !CanExposeAuthenticatedNavigation) return;
            _catalogApi.InvalidateLibraryCache();
            _catalogApi.InvalidateFilterCache();
            _catalogApi.InvalidateRatingCapability();
            App.Services.GetRequiredService<ItemDetailPrefetchCache>().Clear();
            _pendingAccessRefresh = true;
            ApplyPendingAccessRefresh();
        });
    }

    private void OnAccountAuthorityChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Initial login still belongs to the profile/bootstrap flow.
            if (!CanExposeAuthenticatedNavigation || !NavView.IsPaneVisible) return;
            if (_playerService.State is PlayerState.Expanded or PlayerState.Fullscreen)
            { _pendingAccessRefresh = true; return; }
            ShowMainNavigation();
        });
    }

    private void ApplyPendingAccessRefresh()
    {
        if (!_pendingAccessRefresh || !CanExposeAuthenticatedNavigation || !NavView.IsPaneVisible ||
            _playerService.State is PlayerState.Expanded or PlayerState.Fullscreen) return;
        _pendingAccessRefresh = false;
        ShowMainNavigation();
        CancelShellHydration();
        _shellHydrationCts = new CancellationTokenSource();
        var shellKey = GetAuthenticatedShellKey();
        _hydratedShellKey = shellKey;
        var token = _shellHydrationCts.Token;
        _viewModel.Libraries.Clear();
        UpdateLibraryNavItems();
        _ = RunShellWorkAsync("access_library_navigation", () => LoadShellNavigationAsync(shellKey, token));
        _ = RunShellWorkAsync("access_navigation_capabilities", () => RefreshUserNavigationCapabilitiesAsync(shellKey, token));
        // Foreground playback retains its surface and requests. Catch up the covered route when it becomes useful.
        TryShellAction("access_active_route", () => _navigationService.RefreshCurrentEntry());
    }

    public void ShowMainNavigation()
    {
        if (!CanExposeAuthenticatedNavigation)
        {
            HideMainNavigation();
            return;
        }

        var shellKey = GetAuthenticatedShellKey();
        var shouldHydrateShell = !string.Equals(_hydratedShellKey, shellKey, StringComparison.Ordinal);
        if (shouldHydrateShell)
        {
            _hydratedShellKey = shellKey;
            CancelShellHydration();
            _shellHydrationCts = new CancellationTokenSource();
            _notificationUnreadCount = 0;
            TryShellAction("shared_appearance_reset", _themeService.ResetSharedAppearance);
            ResetShellBranding();
            UpdateSidebarPanePresentation(NavView.IsPaneOpen);
        }

        // None of the shell decoration below is allowed to invalidate a
        // successful profile selection or page navigation. Themes, plugin
        // links, library pins, and player prewarming are all supplemental.
        TryShellAction("shell_visibility", () =>
        {
            // HideMainNavigation closes the pane while login/profile selection
            // owns the window. Restore the user's last explicit desktop state
            // before revealing it so the wrong state cannot render for a frame.
            var desiredPaneOpen = !_isNarrowShell && _desktopSidebarOpen;
            if (NavView.IsPaneOpen != desiredPaneOpen)
                NavView.IsPaneOpen = desiredPaneOpen;
            if (!NavView.IsPaneVisible)
                NavView.IsPaneVisible = true;
            ApplyResponsiveShellLayout();
            UpdateSidebarPanePresentation(NavView.IsPaneOpen);
        });

        if (shouldHydrateShell)
        {
            var shellToken = _shellHydrationCts!.Token;
            var cardOverlayService = App.Services.GetRequiredService<CardOverlayService>();
            cardOverlayService.Invalidate();
            _uiCustomizationService.Invalidate();
            _ = RunShellWorkAsync(
                "card_overlay_settings",
                () => cardOverlayService.EnsureLoadedAsync(shellToken));
            _ = RunShellWorkAsync(
                "ui_customization",
                async () =>
                {
                    await _uiCustomizationService.EnsureLoadedAsync(shellToken);
                    if (!IsCurrentShellHydration(shellKey, shellToken)) return;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (IsCurrentShellHydration(shellKey, shellToken))
                            ApplyPrimaryMenuCustomization();
                    });
                });
            _ = RunShellWorkAsync("profile_display", () => UpdateProfileDisplayAsync(shellKey, shellToken));
            _ = RunShellWorkAsync("server_branding", () => LoadShellBrandingAsync(shellKey, shellToken));
            _ = RunShellWorkAsync(
                "user_navigation_capabilities",
                () => RefreshUserNavigationCapabilitiesAsync(shellKey, shellToken));
            _ = RunShellWorkAsync("theme_sync", () => SyncThemeAfterNavigationAsync(shellKey, shellToken));
            TryShellAction("theme_switcher", BuildThemeDots);
            _ = RunShellWorkAsync("plugin_navigation", () => RefreshPluginAppsAsync(shellKey, shellToken));
            EnsureNotificationSubscription(shellKey);

            // Build the hidden native video host only after authentication and
            // profile selection, at low dispatcher priority. This keeps the first
            // page paint responsive while removing libmpv cold-start work from the
            // user's first Play click.
            if (!_playerPrewarmed)
            {
                _playerPrewarmed = true;
                TryShellAction("player_prewarm_queue", () =>
                    DispatcherQueue.TryEnqueue(
                        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () => TryShellAction("player_prewarm", () => _playerService.Prewarm())));
            }
        }

        if (!_navInitialized)
        {
            _navInitialized = true;

            // Watch for library changes to update nav (marshal to UI thread)
            _viewModel.Libraries.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() => TryShellAction("library_navigation_rebuild", UpdateLibraryNavItems));
            };
        }

        if (shouldHydrateShell)
        {
            // Start on the UI context so ObservableCollection changes remain on
            // the owning dispatcher after awaited network requests. Task.Run here
            // previously let sidebar loading race the profile transition.
            var shellToken = _shellHydrationCts!.Token;
            _ = RunShellWorkAsync(
                "library_navigation_load",
                () => LoadShellNavigationAsync(shellKey, shellToken));
        }
        ApplyPendingAccessRefresh();
    }

    private bool IsCurrentShellHydration(string shellKey, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        CanExposeAuthenticatedNavigation &&
        string.Equals(_hydratedShellKey, shellKey, StringComparison.Ordinal);

    private void CancelShellHydration()
    {
        _shellHydrationCts?.Cancel();
        _shellHydrationCts?.Dispose();
        _shellHydrationCts = null;
    }

    private void EnsureNotificationSubscription(string shellKey)
    {
        if (_notificationSubscription != null &&
            string.Equals(_notificationSubscriptionShellKey, shellKey, StringComparison.Ordinal))
        {
            return;
        }

        ReleaseNotificationSubscription();
        _notificationSubscriptionShellKey = shellKey;
        _notificationSubscription = _eventChannel.Subscribe("notifications");
        if (_eventChannel.TryGetLatestSnapshot("notifications", out var cached))
            OnShellEventSnapshot("notifications", cached);
    }

    private void ReleaseNotificationSubscription()
    {
        _notificationSubscription?.Dispose();
        _notificationSubscription = null;
        _notificationSubscriptionShellKey = null;
    }

    private void OnShellEventSnapshot(string channel, System.Text.Json.JsonElement data)
    {
        if (_notificationSubscription == null ||
            !string.Equals(channel, "notifications", StringComparison.OrdinalIgnoreCase) ||
            data.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return;
        }

        var selectedProfileId = _authService.SelectedProfileId;
        var unread = 0;
        foreach (var row in data.EnumerateArray())
        {
            if (!NotificationBelongsToSelectedProfile(row, selectedProfileId))
                continue;
            if (!row.TryGetProperty("read_at", out var readAt) ||
                readAt.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined ||
                (readAt.ValueKind == System.Text.Json.JsonValueKind.String &&
                 string.IsNullOrWhiteSpace(readAt.GetString())))
            {
                unread++;
            }
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            // The realtime snapshot contains at most 25 recent unread rows.
            // Preserve a larger exact count already loaded from REST instead
            // of briefly regressing the badge to the snapshot lower bound.
            _notificationUnreadCount = Math.Max(_notificationUnreadCount, unread);
            UpdateSidebarPanePresentation(NavView.IsPaneOpen);
            // The realtime snapshot is capped at 25 unread rows. At the cap,
            // refresh the exact count just as the current WebUI does.
            if (unread >= 25)
                _ = RefreshExactNotificationCountAsync();
        });
    }

    private void OnShellEvent(
        string channel,
        string eventName,
        System.Text.Json.JsonElement data)
    {
        if (_notificationSubscription == null ||
            !string.Equals(channel, "notifications", StringComparison.OrdinalIgnoreCase) ||
            !NotificationBelongsToSelectedProfile(data, _authService.SelectedProfileId))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (string.Equals(eventName, "notification.created", StringComparison.OrdinalIgnoreCase))
            {
                var isUnread = !data.TryGetProperty("read_at", out var readAt) ||
                    readAt.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined ||
                    (readAt.ValueKind == System.Text.Json.JsonValueKind.String &&
                     string.IsNullOrWhiteSpace(readAt.GetString()));
                if (isUnread)
                    _notificationUnreadCount++;
            }
            else if (string.Equals(eventName, "notification.read", StringComparison.OrdinalIgnoreCase))
            {
                var all = data.TryGetProperty("all", out var allValue) &&
                    allValue.ValueKind == System.Text.Json.JsonValueKind.True;
                _notificationUnreadCount = all
                    ? 0
                    : Math.Max(0, _notificationUnreadCount - 1);
            }
            else
            {
                return;
            }

            UpdateSidebarPanePresentation(NavView.IsPaneOpen);
        });
    }

    private static bool NotificationBelongsToSelectedProfile(
        System.Text.Json.JsonElement data,
        string? selectedProfileId)
    {
        if (!data.TryGetProperty("profile_id", out var profileValue) ||
            profileValue.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            return true;
        }

        var eventProfileId = profileValue.GetString();
        return string.IsNullOrWhiteSpace(eventProfileId) ||
            string.IsNullOrWhiteSpace(selectedProfileId) ||
            string.Equals(eventProfileId, selectedProfileId, StringComparison.Ordinal);
    }

    private async Task RefreshExactNotificationCountAsync()
    {
        var shellKey = _notificationSubscriptionShellKey;
        if (shellKey == null)
            return;

        try
        {
            var count = await _notificationsApi.GetUnreadCountAsync();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!string.Equals(
                        _notificationSubscriptionShellKey,
                        shellKey,
                        StringComparison.Ordinal))
                {
                    return;
                }
                _notificationUnreadCount = count;
                UpdateSidebarPanePresentation(NavView.IsPaneOpen);
            });
        }
        catch
        {
            // Keep the snapshot lower bound during a transient count failure.
        }
    }

    private void NavigationBackAccelerator_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = TryHandleShellBack();
    }

    private void RootGrid_PointerPressed(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);
        if (!point.Properties.IsXButton1Pressed)
            return;

        e.Handled = TryHandleShellBack();
    }

    private void RootGrid_KeyDown(
        object sender,
        Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.GamepadB)
            return;

        e.Handled = TryHandleShellBack();
    }

    private bool TryHandleShellBack()
    {
        if (PlayingNextOverlay.Visibility == Visibility.Visible)
        {
            DismissPlayingNext();
            return true;
        }

        if (_isNarrowShell && NavView.IsPaneOpen)
        {
            CloseMobileNavigationPane();
            return true;
        }

        if (_playerService.State is PlayerState.Expanded or PlayerState.Fullscreen)
            return false;

        if (!_navigationService.CanGoBack)
            return false;

        _navigationService.GoBack();
        return true;
    }

    private string GetAuthenticatedShellKey()
    {
        var userId = _authService.CurrentUser?.Id.ToString() ?? "";
        var username = _authService.CurrentUser?.Username ?? "";
        var role = _authService.CurrentUser?.Role ?? "";
        var profileId = _authService.SelectedProfileId ?? "";
        var profileName = _authService.SelectedProfile?.Name ?? "";
        var profileFlags = _authService.SelectedProfile is { } profile
            ? $"{profile.IsPrimary}:{profile.IsChild}:{profile.MaxContentRating}:{profile.MaxPlaybackQuality}"
            : "";
        var server = _authService.ConfiguredServerUrl ?? "";
        return string.Join("|", server, userId, username, role, profileId, profileName, profileFlags);
    }

    private async Task LoadShellNavigationAsync(
        string shellKey,
        CancellationToken cancellationToken)
    {
        await _viewModel.ReloadLibrariesAsync(cancellationToken);
        if (!IsCurrentShellHydration(shellKey, cancellationToken))
            return;

        await RefreshSidebarPinsAsync(shellKey, cancellationToken);
    }

    private async Task RefreshUserNavigationCapabilitiesAsync(
        string shellKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestStatus = await _requestsApi.GetStatusAsync(cancellationToken);
            if (!IsCurrentShellHydration(shellKey, cancellationToken))
                return;
            RequestsNavItem.Visibility = requestStatus.RequestsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // WebUI keeps Requests hidden until the capability explicitly says
            // it is enabled.
            RequestsNavItem.Visibility = Visibility.Collapsed;
        }

        try
        {
            var capability = await _notificationsApi.GetCapabilityAsync(cancellationToken);
            if (!IsCurrentShellHydration(shellKey, cancellationToken))
                return;
            _notificationsAvailable = capability.InApp.Enabled;
            NotificationsNavItem.Visibility = _notificationsAvailable
                ? Visibility.Visible
                : Visibility.Collapsed;
            _notificationUnreadCount = _notificationsAvailable
                ? await _notificationsApi.GetUnreadCountAsync(cancellationToken)
                : 0;
            if (!IsCurrentShellHydration(shellKey, cancellationToken))
                return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // Worker modes and older servers may not expose notification APIs.
            _notificationsAvailable = false;
            _notificationUnreadCount = 0;
            NotificationsNavItem.Visibility = Visibility.Collapsed;
        }

        UpdateSidebarPanePresentation(NavView.IsPaneOpen);
    }

    private void UpdateSidebarPanePresentation(bool isOpen)
    {
        // AppSidebar keeps both brand variants in the same fixed 260px surface
        // and cross-fades them, avoiding a one-frame logo pop at either edge.
        SiloWordmarkImage.Opacity = isOpen ? 1 : 0;
        SiloMarkImage.Opacity = isOpen ? 0 : 1;
        LibrariesCompactDividerIcon.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
        UpdateLibraryNavigationVisibility(isOpen);

        // PaneFooter is custom content and retains its expanded desired width
        // unless it is explicitly constrained. Pin it to the 64px rail while
        // compact so the profile icon cannot be centered at x=130 and
        // clipped in half by the native pane viewport.
        SidebarFooterPanel.Width = isOpen ? double.NaN : NavView.CompactPaneLength;
        SidebarFooterPanel.HorizontalAlignment = HorizontalAlignment.Left;
        SidebarFooterSeparator.Width = isOpen ? double.NaN : NavView.CompactPaneLength;
        SidebarFooterSeparator.Margin = new Thickness(0, 0, 0, 8);

        ProfileNameText.Opacity = isOpen ? 1 : 0;
        ProfileFooterContent.Spacing = 10;
        ProfileFooterButton.HorizontalAlignment = isOpen ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        ProfileFooterButton.Width = isOpen ? double.NaN : 40;
        ProfileFooterButton.Height = 52;
        ProfileFooterButton.Margin = isOpen ? new Thickness(12, 0, 12, 0) : new Thickness(12, 0, 0, 0);
        ProfileFooterButton.Padding = isOpen ? new Thickness(12) : new Thickness(0);
        ProfileFooterButton.HorizontalContentAlignment = isOpen
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;

        var hasUnread = _notificationsAvailable && _notificationUnreadCount > 0;
        NotificationUnreadBadge.Visibility = hasUnread ? Visibility.Visible : Visibility.Collapsed;
        // InfoBadge renders Value=-1 as the compact notification dot and caps
        // larger values at 99+. The explicit inset keeps the numeric pill away
        // from NavigationView's right-side clip boundary.
        NotificationUnreadBadge.Margin = isOpen
            ? new Thickness(0, 2, 8, 0)
            : new Thickness(0, 2, 20, 0);
        // WinUI renders values above 99 as "99+", matching AppSidebar.tsx.
        NotificationUnreadBadge.Value = isOpen ? _notificationUnreadCount : -1;
    }

    private async Task RunShellWorkAsync(string stage, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // Profile/server changes intentionally retire in-flight shell work.
        }
        catch (Exception ex)
        {
            LogNavigationFailure(stage, ex);
        }
    }

    private static void TryShellAction(string stage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LogNavigationFailure(stage, ex);
        }
    }

    private static void LogNavigationFailure(string stage, Exception exception)
    {
        LocalLog.AppendLine(
            "navigation_errors.txt",
            $"{stage} | {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception}");
    }

    private void AudiobookAccelerator_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_playerService.IsAudiobook || _playerService.State == PlayerState.Idle)
            return;
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot);
        if (focused is TextBox or PasswordBox or ComboBox)
            return;

        var settings = App.Services.GetRequiredService<SettingsService>().Load();
        switch (sender.Key)
        {
            case Windows.System.VirtualKey.Space:
            case Windows.System.VirtualKey.K:
                _playerService.ToggleAudiobookPlayback();
                break;
            case Windows.System.VirtualKey.Left:
                _playerService.SeekTo(Math.Max(0, _playerService.Position - _playerService.SeekIntervals.AudiobookBack));
                break;
            case Windows.System.VirtualKey.Right:
                _playerService.SeekTo(Math.Min(_playerService.Duration, _playerService.Position + _playerService.SeekIntervals.AudiobookForward));
                break;
            case Windows.System.VirtualKey.Up:
                SetAudiobookVolume(Math.Min(100, _playerService.Volume + 5));
                break;
            case Windows.System.VirtualKey.Down:
                SetAudiobookVolume(Math.Max(0, _playerService.Volume - 5));
                break;
            case Windows.System.VirtualKey.M:
                if (_playerService.Mpv != null)
                {
                    _playerService.IsMuted = !_playerService.Mpv.GetMute();
                    _playerService.Mpv.SetMute(_playerService.IsMuted);
                    _playerService.SaveVolumeState();
                }
                break;
            case Windows.System.VirtualKey.N:
                _playerService.SeekToNextAudiobookChapter();
                break;
            case Windows.System.VirtualKey.P:
                _playerService.SeekToPreviousAudiobookChapter();
                break;
            case Windows.System.VirtualKey.E:
                if (_playerService.State == PlayerState.Minimized) _playerService.Expand();
                else _playerService.Minimize();
                break;
            case Windows.System.VirtualKey.Escape:
                if (_playerService.State == PlayerState.Expanded) _playerService.Minimize();
                else return;
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    private void RootGrid_CharacterReceived(
        UIElement sender,
        Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs args)
    {
        if (!_playerService.IsAudiobook || _playerService.State == PlayerState.Idle ||
            args.Character is not ('<' or '>'))
            return;

        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot);
        if (focused is TextBox or PasswordBox or ComboBox)
            return;

        var direction = args.Character == '>' ? 1 : -1;
        _playerService.SetAudiobookPlaybackRate(
            _playerService.AudiobookPlaybackRate + direction * SettingsService.AudiobookRateStep);
        args.Handled = true;
    }

    private void SetAudiobookVolume(double volume)
    {
        if (_playerService.Mpv == null) return;
        _playerService.Volume = volume;
        _playerService.Mpv.SetVolume(volume);
        if (volume > 0 && _playerService.IsMuted)
        {
            _playerService.IsMuted = false;
            _playerService.Mpv.SetMute(false);
        }
        _playerService.SaveVolumeState();
    }

    private void OnNavigated_ApplyAccessibility(object? sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => App.Services.GetRequiredService<AccessibilityService>().ApplySaved(ContentFrame.Content as DependencyObject));
    }

    private void OnNavigated_SynchronizeShellChrome(object? sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _lastShellPageType = e.SourcePageType;
        _lastShellParameter = e.Parameter;

        if (e.SourcePageType == typeof(PasswordRecoveryPage))
        {
            HideMainNavigation();
            NavView.SelectedItem = null;
            return;
        }
        if (!CanExposeAuthenticatedNavigation)
        {
            NavView.SelectedItem = null;
            return;
        }

        // Restore the authenticated shell at the frame boundary so navigation
        // never leaves the user-controlled pane in a transient hidden state.
        var desiredPaneOpen = !_isNarrowShell && _desktopSidebarOpen;
        if (NavView.IsPaneOpen != desiredPaneOpen)
            NavView.IsPaneOpen = desiredPaneOpen;
        if (!NavView.IsPaneVisible)
            NavView.IsPaneVisible = true;
        ApplyResponsiveShellLayout();
        SynchronizeSelectedNavigationItem(e.SourcePageType, e.Parameter);
    }

    private void ResynchronizeSelectedNavigationItem()
    {
        if (_lastShellPageType != null)
            SynchronizeSelectedNavigationItem(_lastShellPageType, _lastShellParameter);
    }

    private void SynchronizeSelectedNavigationItem(Type pageType, object? parameter)
    {
        NavigationViewItem? selected = pageType switch
        {
            var type when type == typeof(HomePage) => FindNavigationItemByStringTag("Home"),
            var type when type == typeof(SearchPage) => FindNavigationItemByStringTag("Search"),
            var type when type == typeof(RecommendationsPage) => FindNavigationItemByStringTag("Recommendations"),
            var type when type == typeof(RequestsPage) => FindNavigationItemByStringTag("Requests"),
            var type when type == typeof(CalendarPage) => FindNavigationItemByStringTag("Calendar"),
            var type when type == typeof(NotificationsPage) => FindNavigationItemByStringTag("Notifications"),
            var type when type == typeof(WatchTogetherJoinPage) => FindNavigationItemByStringTag("WatchParty"),
            var type when type == typeof(CollectionsPage) => FindNavigationItemByStringTag("Collections"),
            var type when type == typeof(DownloadsPage) => FindNavigationItemByStringTag("Downloads"),
            var type when type == typeof(LibraryPage) => FindLibraryNavigationItem(parameter),
            var type when type == typeof(CollectionBrowsePage) => FindCollectionNavigationItem(parameter),
            var type when type == typeof(PluginRoutePage) => FindPluginNavigationItem(parameter),
            var type when type == typeof(CatalogPage) => FindCatalogNavigationItem(parameter),
            _ => null,
        };

        NavView.SelectedItem = selected;
    }

    private NavigationViewItem? FindNavigationItemByStringTag(string tag) =>
        FindNavigationItem(item =>
            item.Tag is string itemTag &&
            string.Equals(itemTag, tag, StringComparison.Ordinal));

    private NavigationViewItem? FindLibraryNavigationItem(object? parameter)
    {
        var library = parameter switch
        {
            Library value => value,
            LibraryPage.NavigationArgs args => args.Library,
            _ => null,
        };
        return library == null
            ? null
            : FindNavigationItem(item => item.Tag is Library candidate && candidate.Id == library.Id);
    }

    private NavigationViewItem? FindCatalogNavigationItem(object? parameter)
    {
        if (parameter is not CatalogNavigation navigation)
            return null;

        var topLevelTag = navigation.Source.ToLowerInvariant() switch
        {
            "favorites" => "Favorites",
            "watchlist" => "Watchlist",
            "history" => "History",
            _ => null,
        };
        if (topLevelTag != null)
            return FindNavigationItemByStringTag(topLevelTag);

        if (!string.Equals(navigation.Source, "section", StringComparison.OrdinalIgnoreCase) ||
            navigation.LibraryId is not int libraryId ||
            string.IsNullOrWhiteSpace(navigation.SectionId))
        {
            return null;
        }

        return FindNavigationItem(item =>
            item.Tag is SidebarPinNavTag pin &&
            pin.LibraryId == libraryId &&
            string.Equals(pin.PinType, "section", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pin.PinId, navigation.SectionId, StringComparison.Ordinal));
    }

    private NavigationViewItem? FindCollectionNavigationItem(object? parameter)
    {
        if (parameter is not CollectionBrowsePage.NavArgs navigation ||
            navigation.LibraryId is not int libraryId)
        {
            return null;
        }

        return FindNavigationItem(item =>
            item.Tag is SidebarPinNavTag pin &&
            pin.LibraryId == libraryId &&
            string.Equals(pin.PinType, "collection", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pin.PinId, navigation.CollectionId, StringComparison.Ordinal));
    }

    private NavigationViewItem? FindPluginNavigationItem(object? parameter)
    {
        if (parameter is not PluginRoutePage.NavigationArgs navigation)
            return null;

        return FindNavigationItem(item =>
            item.Tag is PluginAppNavTag plugin &&
            plugin.InstallationId == navigation.InstallationId &&
            string.Equals(plugin.RoutePath, navigation.RoutePath, StringComparison.Ordinal));
    }

    private NavigationViewItem? FindNavigationItem(Func<NavigationViewItem, bool> predicate)
    {
        NavigationViewItem? Search(IEnumerable<object> items)
        {
            foreach (var candidate in items)
            {
                if (candidate is not NavigationViewItem item)
                    continue;
                if (predicate(item))
                    return item;

                var nested = Search(item.MenuItems);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        return Search(NavView.MenuItems);
    }

    private Task UpdateProfileDisplayAsync(
        string shellKey,
        CancellationToken cancellationToken)
    {
        var profileId = _authService.SelectedProfileId;
        if (!string.IsNullOrEmpty(profileId))
            return LoadProfileNameAsync(profileId, shellKey, cancellationToken);
        return Task.CompletedTask;
    }

    private async Task LoadProfileNameAsync(
        string profileId,
        string shellKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var authApi = App.Services.GetRequiredService<Core.Api.AuthApi>();
            var response = await authApi.GetProfilesAsync(cancellationToken);
            if (!IsCurrentShellHydration(shellKey, cancellationToken))
                return;
            var profile = response.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!IsCurrentShellHydration(shellKey, cancellationToken))
                        return;
                    ProfileNameText.Text = profile.Name;
                    ProfileInitialText.Text = !string.IsNullOrEmpty(profile.Name)
                        ? profile.Name[0].ToString().ToUpperInvariant()
                        : "?";
                    MobileProfileInitialText.Text = ProfileInitialText.Text;
                    // Populate dropdown header
                    ProfileDropdownInitial.Text = ProfileInitialText.Text;
                    ProfileDropdownName.Text = profile.Name;
                    var username = _authService.CurrentUser?.Username ?? "";
                    ProfileDropdownUsername.Text = username;
                    ProfileDropdownUsername.Visibility =
                        string.IsNullOrWhiteSpace(username) ||
                        string.Equals(username, profile.Name, StringComparison.Ordinal)
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                    _ = ApplyProfileAvatarAsync(profile.AvatarUrl);
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Non-critical, leave default text
        }
    }

    private long _profileAvatarGeneration;

    private async Task ApplyProfileAvatarAsync(string? avatarUrl)
    {
        var generation = Interlocked.Increment(ref _profileAvatarGeneration);
        if (!Uri.TryCreate(avatarUrl, UriKind.Absolute, out var avatarUri))
        {
            ProfileAvatarBrush.ImageSource = null;
            ProfileDropdownAvatarBrush.ImageSource = null;
            ProfileAvatarImage.Visibility = Visibility.Collapsed;
            ProfileDropdownAvatarImage.Visibility = Visibility.Collapsed;
            return;
        }

        Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap;
        try
        {
            var path = await App.Services.GetRequiredService<ImageService>().GetImageDiskPathAsync(
                avatarUri.AbsolutePath,
                "profile-avatar",
                avatarUrl!,
                App.Services.GetRequiredService<HttpClient>());
            if (string.IsNullOrWhiteSpace(path) || generation != Volatile.Read(ref _profileAvatarGeneration))
                return;
            bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path))
            {
                DecodePixelWidth = 96,
            };
        }
        catch
        {
            return;
        }

        if (generation != Volatile.Read(ref _profileAvatarGeneration))
            return;

        bitmap.ImageFailed += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation == Volatile.Read(ref _profileAvatarGeneration) &&
                    ReferenceEquals(ProfileAvatarBrush.ImageSource, bitmap))
                {
                    ProfileAvatarImage.Visibility = Visibility.Collapsed;
                    ProfileDropdownAvatarImage.Visibility = Visibility.Collapsed;
                }
            });
        };
        ProfileAvatarBrush.ImageSource = bitmap;
        ProfileDropdownAvatarBrush.ImageSource = bitmap;
        ProfileAvatarImage.Visibility = Visibility.Visible;
        ProfileDropdownAvatarImage.Visibility = Visibility.Visible;
    }

    public void HideMainNavigation()
    {
        // Hide the pane before closing it so the authenticated-pane guard does
        // not treat login/profile transitions as an accidental collapse.
        NavView.IsPaneVisible = false;
        NavView.IsPaneOpen = false;
        RemoveDynamicLibraryNavItems();
        BuildPluginApps([]);
        _sidebarPins = [];
        _hydratedShellKey = null;
        CancelShellHydration();
        ReleaseNotificationSubscription();
    }

    private void NavView_PaneClosing(
        NavigationView sender,
        NavigationViewPaneClosingEventArgs args)
    {
        if (!_isNarrowShell && NavView.IsPaneVisible && CanExposeAuthenticatedNavigation)
        {
            // NavigationView can request a pane close while changing selection,
            // rebuilding the frame, or realizing a detail page. Those framework
            // requests must never overwrite the user's persistent desktop state.
            // The preference is changed only on input from the real pane-toggle
            // button, so an open preference means this close is unsolicited.
            if (_desktopSidebarOpen)
            {
                args.Cancel = true;
                SynchronizeDesktopPaneState();
                return;
            }
        }

        UpdateSidebarPanePresentation(isOpen: false);
    }

    private void NavView_PointerPressed(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!_isNarrowShell && IsPaneToggleInputSource(args.OriginalSource as DependencyObject))
        {
            RememberDesktopSidebarState(!NavView.IsPaneOpen);
        }
    }

    private void NavView_KeyDown(
        object sender,
        Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (_isNarrowShell ||
            args.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space))
        {
            return;
        }

        if (IsPaneToggleInputSource(args.OriginalSource as DependencyObject))
        {
            RememberDesktopSidebarState(!NavView.IsPaneOpen);
        }
    }

    private void RememberDesktopSidebarState(bool isOpen)
    {
        _desktopSidebarOpen = isOpen;
        try
        {
            var settings = _settingsService.Load();
            if (settings.DesktopSidebarOpen != isOpen)
            {
                settings.DesktopSidebarOpen = isOpen;
                _settingsService.Save(settings);
            }
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine("sidebar_state.txt", $"Could not persist desktop sidebar state: {ex.Message}");
        }
    }

    private bool IsPaneToggleInputSource(DependencyObject? source)
    {
        for (var current = source;
             current != null && !ReferenceEquals(current, NavView);
             current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is not Button button)
                continue;

            var name = button.Name ?? string.Empty;
            return name.Contains("TogglePane", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("PaneToggle", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private void OnNavViewIsPaneOpenChanged(
        DependencyObject sender,
        DependencyProperty property)
    {
        if (_synchronizingDesktopPaneState ||
            _isNarrowShell ||
            !NavView.IsPaneVisible ||
            !CanExposeAuthenticatedNavigation)
        {
            return;
        }

        if (NavView.IsPaneOpen != _desktopSidebarOpen)
            SynchronizeDesktopPaneState();
        else
            UpdateSidebarPanePresentation(_desktopSidebarOpen);
    }

    private void SynchronizeDesktopPaneState()
    {
        if (_synchronizingDesktopPaneState ||
            _isNarrowShell ||
            !NavView.IsPaneVisible ||
            !CanExposeAuthenticatedNavigation)
        {
            return;
        }

        _synchronizingDesktopPaneState = true;
        try
        {
            var desiredMode = _desktopSidebarOpen
                ? NavigationViewPaneDisplayMode.Left
                : NavigationViewPaneDisplayMode.LeftCompact;
            if (NavView.PaneDisplayMode != desiredMode)
                NavView.PaneDisplayMode = desiredMode;
            if (NavView.IsPaneOpen != _desktopSidebarOpen)
                NavView.IsPaneOpen = _desktopSidebarOpen;
            UpdateSidebarPanePresentation(_desktopSidebarOpen);
        }
        finally
        {
            _synchronizingDesktopPaneState = false;
        }
    }

    public void RestoreMainPane()
    {
        if (!CanExposeAuthenticatedNavigation)
        {
            HideMainNavigation();
            return;
        }

        NavView.IsPaneVisible = true;
        ApplyResponsiveShellLayout();
        UpdateSidebarPanePresentation(NavView.IsPaneOpen);
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
                StopPlayingNextCountdown();
                PlayingNextOverlay.Visibility = Visibility.Collapsed;
                PlayerOverlayControl.Visibility = Visibility.Collapsed;
                PlayerOverlayControl.Deactivate();
                AudiobookNowListeningControl.Visibility = Visibility.Collapsed;
                AudiobookNowListeningControl.Deactivate();
                MiniPlayerBarControl.Visibility = Visibility.Collapsed;
                MiniPlayerBarControl.Deactivate();
                MiniPlayerBarControl.ResetSleepTimer();
                NavView.IsPaneVisible = CanExposeAuthenticatedNavigation;
                NavView.Margin = new Thickness(0);
                ApplyResponsiveShellLayout();
                LogState($"  -> Idle: NavView.IsPaneVisible={NavView.IsPaneVisible} _navInitialized={_navInitialized}");
                break;

            case PlayerState.Expanded:
            case PlayerState.Fullscreen:
                MiniPlayerBarControl.Deactivate();
                MiniPlayerBarControl.Visibility = Visibility.Collapsed;
                PlayerOverlayControl.Visibility = Visibility.Collapsed;
                PlayerOverlayControl.Deactivate();
                if (_playerService.IsAudiobook)
                {
                    AudiobookNowListeningControl.Visibility = Visibility.Visible;
                    AudiobookNowListeningControl.Activate();
                }
                else
                {
                    AudiobookNowListeningControl.Visibility = Visibility.Collapsed;
                    AudiobookNowListeningControl.Deactivate();
                }
                // Do NOT hide NavView — the popup window covers it.
                // Hiding it caused the sidebar to disappear and not come back.
                LogState($"  -> Expanded/Fullscreen: NavView untouched");
                break;

            case PlayerState.PictureInPicture:
                AudiobookNowListeningControl.Visibility = Visibility.Collapsed;
                AudiobookNowListeningControl.Deactivate();
                PlayerOverlayControl.Deactivate();
                PlayerOverlayControl.Visibility = Visibility.Collapsed;
                MiniPlayerBarControl.Deactivate();
                MiniPlayerBarControl.Visibility = Visibility.Collapsed;
                NavView.IsPaneVisible = CanExposeAuthenticatedNavigation;
                NavView.Margin = new Thickness(0);
                ApplyResponsiveShellLayout();
                break;

            case PlayerState.Minimized:
                AudiobookNowListeningControl.Visibility = Visibility.Collapsed;
                AudiobookNowListeningControl.Deactivate();
                PlayerOverlayControl.Deactivate();
                PlayerOverlayControl.Visibility = Visibility.Collapsed;
                NavView.IsPaneVisible = CanExposeAuthenticatedNavigation;
                NavView.Margin = new Thickness(0, 0, 0, _playerService.IsAudiobook ? Controls.MiniPlayerBar.AudiobookHeightForWidth(MiniPlayerBarControl.ActualWidth) : 132);
                ApplyResponsiveShellLayout();
                MiniPlayerBarControl.Visibility = Visibility.Visible;
                MiniPlayerBarControl.Activate();
                break;
        }
        ApplyPendingAccessRefresh();
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

    public Task ShowSubtitleAiDialogAsync(XamlRoot? dialogXamlRoot = null)
        => PlayerOverlayControl.ShowSubtitleAiDialogAsync(dialogXamlRoot);

    public void NavigateToHome()
    {
        if (!_navigationService.Navigate<HomePage>())
        {
            var failure = new InvalidOperationException("The navigation frame rejected the Home page.");
            LogNavigationFailure("navigate_home", failure);
            ShowPlaybackError("Page failed to open", "Silo could not open Home. The current page is still available.");
            return;
        }
        NavView.SelectedItem = HomeNavItem;
    }

    public bool TryEnterAuthenticatedPage(Type pageType, object? parameter, out Exception? failure)
    {
        failure = null;
        if (!CanExposeAuthenticatedNavigation)
        {
            failure = new InvalidOperationException("An authenticated profile is required before opening this page.");
            LogNavigationFailure("authenticated_page_guard", failure);
            HideMainNavigation();
            return false;
        }

        try
        {
            if (!_navigationService.Navigate(pageType, parameter))
                throw new InvalidOperationException($"The navigation frame rejected {pageType.Name}.");

            if (pageType == typeof(HomePage))
                NavView.SelectedItem = HomeNavItem;

            // Reveal and hydrate the shell only after the destination page is
            // alive. Supplemental shell failures are isolated and logged by
            // ShowMainNavigation and cannot undo this navigation.
            ShowMainNavigation();
            return true;
        }
        catch (Exception ex)
        {
            failure = ex;
            LogNavigationFailure($"authenticated_page_{pageType.Name}", ex);
            return false;
        }
    }

    private async Task SyncThemeAfterNavigationAsync(
        string shellKey,
        CancellationToken cancellationToken)
    {
        await _themeService.SyncFromServerAsync(cancellationToken);
        if (!IsCurrentShellHydration(shellKey, cancellationToken))
            return;
        TryShellAction("theme_switcher_refresh", BuildThemeDots);
        await LoadShellBrandingAsync(shellKey, cancellationToken);
    }

    private void ResetShellBranding()
    {
        _brandingLoadGate.Cancel();
        var isLightAppearance = ThemeService.IsLightAppearance(_themeService.CurrentTheme);
        var defaultWordmark = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
            new Uri(isLightAppearance
                ? "ms-appx:///Assets/silo-wordmark-sidebar-light.png"
                : "ms-appx:///Assets/silo-wordmark-sidebar.png"));
        var defaultMark = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
            new Uri("ms-appx:///Assets/silo-mark-transparent.png"));
        SiloWordmarkImage.Source = defaultWordmark;
        MobileSiloWordmarkImage.Source = defaultWordmark;
        SiloMarkImage.Source = defaultMark;
        DocumentTitle.SetServerName("Silo");
    }

    private async Task LoadShellBrandingAsync(
        string shellKey,
        CancellationToken cancellationToken)
    {
        var brandingLoadVersion = _brandingLoadGate.BeginNextLoad();
        var branding = await _settingsApi.GetServerBrandingAsync(cancellationToken);
        if (!IsCurrentShellHydration(shellKey, cancellationToken)
            || !_brandingLoadGate.IsCurrent(brandingLoadVersion)) return;

        var isLightAppearance = ThemeService.IsLightAppearance(_themeService.CurrentTheme);
        var assetChoice = BrandingAssetSelector.Select(branding, isLightAppearance);
        var wordmarkUrl = _apiClient.ResolveServerUrl(assetChoice.WordmarkUrl);
        var markUrl = _apiClient.ResolveServerUrl(assetChoice.MarkUrl);
        var imageService = App.Services.GetRequiredService<ImageService>();
        var httpClient = App.Services.GetRequiredService<HttpClient>();
        var wordmarkPathTask = Uri.TryCreate(wordmarkUrl, UriKind.Absolute, out _)
            ? imageService.GetImageDiskPathAsync(shellKey, "server-wordmark", wordmarkUrl!, httpClient, cancellationToken)
            : Task.FromResult<string?>(null);
        var markPathTask = Uri.TryCreate(markUrl, UriKind.Absolute, out _)
            ? imageService.GetImageDiskPathAsync(shellKey, "server-mark", markUrl!, httpClient, cancellationToken)
            : Task.FromResult<string?>(null);
        await Task.WhenAll(wordmarkPathTask, markPathTask);
        if (!IsCurrentShellHydration(shellKey, cancellationToken)
            || !_brandingLoadGate.IsCurrent(brandingLoadVersion)) return;
        var wordmarkPath = await wordmarkPathTask;
        var markPath = await markPathTask;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrentShellHydration(shellKey, cancellationToken)
                || !_brandingLoadGate.IsCurrent(brandingLoadVersion)) return;

            var defaultWordmark = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(isLightAppearance
                    ? "ms-appx:///Assets/silo-wordmark-sidebar-light.png"
                    : "ms-appx:///Assets/silo-wordmark-sidebar.png"));
            SiloWordmarkImage.Source = defaultWordmark;
            MobileSiloWordmarkImage.Source = defaultWordmark;
            if (!string.IsNullOrWhiteSpace(wordmarkPath))
            {
                var source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(wordmarkPath));
                SiloWordmarkImage.Source = source;
                MobileSiloWordmarkImage.Source = source;
            }
            if (!string.IsNullOrWhiteSpace(markPath))
                SiloMarkImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(markPath));

            DocumentTitle.SetServerName(branding.ServerName);
            if (AppWindow != null && ContentFrame.CurrentSourcePageType != null)
                AppWindow.Title = DocumentTitle.FromPageType(ContentFrame.CurrentSourcePageType);
        });
    }

    private static string LibraryNavigationIconName(string type) => type switch
    {
        "movies" => "film",
        "series" => "tv",
        "audiobook" or "audiobooks" => "book-headphones",
        _ => "library",
    };

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

        if (!CanExposeAuthenticatedNavigation)
            return;

        // Filter out hidden libraries
        var appSettings = _settingsService.Load();
        var hiddenIds = new HashSet<int>(appSettings.HiddenLibraryIds);

        // Insert library items after the header
        int insertIndex = headerIndex + 1;
        foreach (var lib in _viewModel.Libraries)
        {
            if (hiddenIds.Contains(lib.Id)) continue;

            var icon = LibraryNavigationIconName(lib.Type);

            var navItem = new NavigationViewItem
            {
                Content = lib.Name,
                Tag = lib,
                Icon = SiloPlayer.Controls.WebUiIcon.Navigation(icon),
                Visibility = NavView.IsPaneOpen && !_librariesExpanded
                    ? Visibility.Collapsed
                    : Visibility.Visible,
            };

            // Nested pinned collections under this library (webui parity,
            // sidebar_pins user setting).
            if (_sidebarPins.TryGetValue(lib.Id.ToString(), out var pins))
            {
                foreach (var pin in pins)
                {
                    var pinTag = new SidebarPinNavTag
                    {
                        LibraryId = lib.Id,
                        PinType = pin.Type,
                        PinId = pin.Id,
                        Label = pin.Label,
                    };
                    var pinItem = new NavigationViewItem
                    {
                        Tag = pinTag,
                        Icon = SiloPlayer.Controls.WebUiIcon.Navigation(pin.Type == "collection" ? "folder-open" : "layout-grid", 14),
                    };
                    pinItem.Icon.Opacity = .6;
                    pinItem.Content = BuildPinnedSidebarContent(pinItem, pinTag);
                    navItem.MenuItems.Add(pinItem);
                }
            }

            NavView.MenuItems.Insert(insertIndex++, navItem);
        }

        ApplyPrimaryMenuCustomization();
        ResynchronizeSelectedNavigationItem();
    }

    private sealed record PrimaryMenuNavTag(PrimaryMenuItem Item);

    private void OnUICustomizationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyPrimaryMenuCustomization();
            ResynchronizeSelectedNavigationItem();
        });
    }

    /// <summary>
    /// Applies the current desktop-family primary menu while retaining the
    /// complete library tree and fixed Discover/Your Stuff areas below it.
    /// This mirrors AppSidebar.tsx: a customized primary menu replaces the
    /// default Home/Recommendations/Calendar shortcuts, not the safe browsing
    /// fallbacks.
    /// </summary>
    private void ApplyPrimaryMenuCustomization()
    {
        var headerIndex = NavView.MenuItems.IndexOf(LibrariesHeader);
        if (headerIndex < 0) return;

        for (var index = headerIndex - 1; index >= 0; index--)
        {
            if (NavView.MenuItems[index] is NavigationViewItem { Tag: PrimaryMenuNavTag })
            {
                NavView.MenuItems.RemoveAt(index);
                headerIndex--;
            }
        }

        var customMenu = _uiCustomizationService.IsSupported
            ? _uiCustomizationService.PrimaryMenu
            : null;
        var customized = customMenu is { Items.Count: > 0 };
        HomeNavItem.Visibility = customized ? Visibility.Collapsed : Visibility.Visible;
        RecommendationsNavItem.Visibility = customized ? Visibility.Collapsed : Visibility.Visible;
        CalendarNavItem.Visibility = customized ? Visibility.Collapsed : Visibility.Visible;
        if (!customized || !CanExposeAuthenticatedNavigation) return;

        foreach (var menuItem in customMenu!.Items)
        {
            var resolved = ResolvePrimaryMenuItem(menuItem);
            if (resolved is null) continue;
            NavView.MenuItems.Insert(headerIndex++, resolved);
        }
    }

    private NavigationViewItem? ResolvePrimaryMenuItem(PrimaryMenuItem source)
    {
        string label;
        string icon;
        if (source.Type == "builtin")
        {
            (label, icon) = source.Destination switch
            {
                "home" => ("Home", "house"),
                "for_you" => ("For You", "sparkles"),
                "calendar" => ("Calendar", "calendar-days"),
                // Global media-family routes are not available yet. Match the
                // WebUI by omitting them instead of silently choosing a library.
                _ => ("", ""),
            };
            if (label.Length == 0) return null;
        }
        else if (source.Type == "library" && source.LibraryId is int libraryId)
        {
            var library = _viewModel.Libraries.FirstOrDefault(candidate => candidate.Id == libraryId);
            if (library is null) return null;
            label = string.IsNullOrWhiteSpace(source.Label) ? library.Name : source.Label!;
            icon = LibraryNavigationIconName(library.Type);
        }
        else if (source.Type == "section" && source.LibraryId > 0 && !string.IsNullOrWhiteSpace(source.SectionId))
        {
            if (!_viewModel.Libraries.Any(candidate => candidate.Id == source.LibraryId)) return null;
            label = source.Label ?? "Section";
            icon = "layout-grid";
        }
        else if (source.Type == "collection" && !string.IsNullOrWhiteSpace(source.CollectionId))
        {
            if (source.LibraryId is int ownerId && !_viewModel.Libraries.Any(candidate => candidate.Id == ownerId))
                return null;
            label = source.Label ?? "Collection";
            icon = "folder-open";
        }
        else
        {
            return null;
        }

        return new NavigationViewItem
        {
            Content = label,
            Tag = new PrimaryMenuNavTag(source.Clone()),
            Icon = SiloPlayer.Controls.WebUiIcon.Navigation(icon),
        };
    }

    private void RemoveDynamicLibraryNavItems()
    {
        var headerIndex = NavView.MenuItems.IndexOf(LibrariesHeader);
        if (headerIndex < 0)
            return;

        var removeIndex = headerIndex + 1;
        while (removeIndex < NavView.MenuItems.Count &&
               NavView.MenuItems[removeIndex] is not NavigationViewItemHeader)
        {
            NavView.MenuItems.RemoveAt(removeIndex);
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

    private Grid BuildPinnedSidebarContent(
        NavigationViewItem pinItem,
        SidebarPinNavTag pin)
    {
        var content = new Grid { MinWidth = 144 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = pin.Label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        content.Children.Add(label);

        var unpinButton = new Button
        {
            Content = SiloPlayer.Controls.WebUiIcon.Navigation("pin-off", 12),
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 0, 0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(unpinButton, 1);
        ToolTipService.SetToolTip(unpinButton, "Unpin");
        AutomationProperties.SetName(unpinButton, $"Unpin {pin.Label}");
        content.Children.Add(unpinButton);

        void ShowUnpin() => unpinButton.Opacity = 1;
        void HideUnpin()
        {
            if (unpinButton.FocusState == FocusState.Unfocused)
                unpinButton.Opacity = 0;
        }

        pinItem.PointerEntered += (_, _) => ShowUnpin();
        pinItem.PointerExited += (_, _) => HideUnpin();
        pinItem.GotFocus += (_, _) => ShowUnpin();
        pinItem.LostFocus += (_, _) => HideUnpin();
        unpinButton.GotFocus += (_, _) => ShowUnpin();
        unpinButton.LostFocus += (_, _) => HideUnpin();
        unpinButton.Click += async (_, _) =>
        {
            pinItem.IsEnabled = false;
            try
            {
                await RemoveSidebarPinAsync(
                    pin.LibraryId,
                    pin.PinType,
                    pin.PinId);
            }
            catch
            {
                pinItem.IsEnabled = true;
                App.Services.GetRequiredService<ToastService>().Error(
                    $"Silo could not unpin {pin.Label}.");
            }
        };

        return content;
    }

    public bool IsSidebarPin(int libraryId, string pinType, string pinId)
    {
        return _sidebarPins.TryGetValue(libraryId.ToString(), out var pins)
            && pins.Any(pin =>
                string.Equals(pin.Type, pinType, StringComparison.OrdinalIgnoreCase)
             && string.Equals(pin.Id, pinId, StringComparison.Ordinal));
    }

    private async Task RefreshPluginAppsAsync(
        string shellKey,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentShellHydration(shellKey, cancellationToken))
        {
            BuildPluginApps([]);
            return;
        }

        try
        {
            var response = await _settingsApi.GetPluginSettingsListAsync(cancellationToken);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsCurrentShellHydration(shellKey, cancellationToken))
                    BuildPluginApps(response.Installations);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsCurrentShellHydration(shellKey, cancellationToken))
                    BuildPluginApps([]);
            });
        }
    }

    private void BuildPluginApps(IReadOnlyList<PluginSettingsSummary> installations)
    {
        var headerIndex = NavView.MenuItems.IndexOf(AppsHeader);
        if (headerIndex < 0) return;
        while (NavView.MenuItems.Count > headerIndex + 1)
            NavView.MenuItems.RemoveAt(headerIndex + 1);

        if (!CanExposeAuthenticatedNavigation)
        {
            AppsHeader.Visibility = Visibility.Collapsed;
            return;
        }

        var links = installations
            .SelectMany(installation => installation.Routes
                .Where(route => route.Navigable &&
                    route.NavigationKind.Equals("user", StringComparison.OrdinalIgnoreCase))
                .Select(route => new
                {
                    Installation = installation,
                    Route = route,
                    Category = installation.Category?.Split('/')[0].Trim(),
                }))
            .ToList();
        AppsHeader.Visibility = links.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (links.Count == 0) return;

        var distinctCategories = links
            .Select(link => string.IsNullOrWhiteSpace(link.Category) ? "Other" : link.Category!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var grouped = distinctCategories.Count >= 2;
        var insertIndex = headerIndex + 1;
        var orderedGroups = links
            .GroupBy(link => string.IsNullOrWhiteSpace(link.Category) ? "Other" : link.Category!,
                StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group.Key.Equals("Other", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in orderedGroups)
        {
            if (grouped)
                NavView.MenuItems.Insert(insertIndex++, new NavigationViewItemHeader { Content = group.Key.ToUpperInvariant() });
            foreach (var link in group)
            {
                var label = string.IsNullOrWhiteSpace(link.Route.NavigationLabel)
                    ? link.Installation.PluginId
                    : link.Route.NavigationLabel;
                NavView.MenuItems.Insert(insertIndex++, new NavigationViewItem
                {
                    Content = label,
                    Tag = new PluginAppNavTag(link.Installation.Id, link.Route.Path, label),
                    Icon = SiloPlayer.Controls.WebUiIcon.Navigation("puzzle"),
                });
            }
        }

        ResynchronizeSelectedNavigationItem();
    }

    public IReadOnlyList<(string Id, string Label)> GetSidebarPins(int libraryId, string pinType)
    {
        if (!_sidebarPins.TryGetValue(libraryId.ToString(), out var pins))
            return [];
        return pins
            .Where(pin => string.Equals(pin.Type, pinType, StringComparison.OrdinalIgnoreCase))
            .Select(pin => (pin.Id, pin.Label))
            .ToList();
    }

    public Library? FindLibrary(int libraryId)
        => _viewModel.Libraries.FirstOrDefault(library => library.Id == libraryId);

    public Library? FindLibraryByType(string mediaType)
    {
        static string Normalize(string value)
        {
            var normalized = value.Trim().TrimEnd('s').ToLowerInvariant();
            return normalized is "manga" or "comic" ? "comic" : normalized;
        }
        var singular = Normalize(mediaType);
        return _viewModel.Libraries.FirstOrDefault(library =>
            Normalize(library.Type).Equals(singular, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> ToggleSidebarPinAsync(
        int libraryId,
        string pinType,
        string pinId,
        string label)
    {
        var settingsApi = App.Services.GetRequiredService<SettingsApi>();
        var entry = await settingsApi.GetSettingAsync("sidebar_pins");
        var map = ParseSidebarPins(entry.Value);
        var key = libraryId.ToString();
        var pins = map.TryGetValue(key, out var existing) ? existing : [];
        var index = pins.FindIndex(pin =>
            string.Equals(pin.Type, pinType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pin.Id, pinId, StringComparison.Ordinal));
        bool nowPinned;
        if (index >= 0)
        {
            pins.RemoveAt(index);
            if (pins.Count == 0) map.Remove(key);
            else map[key] = pins;
            nowPinned = false;
        }
        else
        {
            pins.Add(new SidebarPinRow { Type = pinType, Id = pinId, Label = label });
            map[key] = pins;
            nowPinned = true;
        }

        var serializable = map.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(pin => new Dictionary<string, string>
            {
                ["type"] = pin.Type,
                ["id"] = pin.Id,
                ["label"] = pin.Label,
            }).ToList());
        await settingsApi.PutSettingAsync("sidebar_pins", System.Text.Json.JsonSerializer.Serialize(serializable));
        _sidebarPins = map;
        UpdateLibraryNavItems();
        return nowPinned;
    }

    private async Task RemoveSidebarPinAsync(
        int libraryId,
        string pinType,
        string pinId)
    {
        var entry = await _settingsApi.GetSettingAsync("sidebar_pins");
        var map = ParseSidebarPins(entry.Value);
        var key = libraryId.ToString();
        if (!map.TryGetValue(key, out var pins))
            return;

        pins.RemoveAll(pin =>
            string.Equals(pin.Type, pinType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pin.Id, pinId, StringComparison.Ordinal));
        if (pins.Count == 0)
            map.Remove(key);

        var serializable = map.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(pin => new Dictionary<string, string>
            {
                ["type"] = pin.Type,
                ["id"] = pin.Id,
                ["label"] = pin.Label,
            }).ToList());
        await _settingsApi.PutSettingAsync(
            "sidebar_pins",
            System.Text.Json.JsonSerializer.Serialize(serializable));
        _sidebarPins = map;
        UpdateLibraryNavItems();
    }

    /// <summary>
    /// Reloads the cached sidebar pins from the server setting and rebuilds
    /// the library nav. Call on sign-in and after any CollectionBrowsePage
    /// pin toggle.
    /// </summary>
    public async Task RefreshSidebarPinsAsync(
        string? shellKey = null,
        CancellationToken cancellationToken = default)
    {
        shellKey ??= _hydratedShellKey;
        if (!CanExposeAuthenticatedNavigation)
        {
            _sidebarPins = [];
            UpdateLibraryNavItems();
            return;
        }

        try
        {
            var settingsApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.SettingsApi>();
            var entry = await settingsApi.GetSettingAsync("sidebar_pins", cancellationToken);
            if (shellKey == null || !IsCurrentShellHydration(shellKey, cancellationToken))
                return;
            _sidebarPins = ParseSidebarPins(entry.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            if (shellKey == null || !IsCurrentShellHydration(shellKey, cancellationToken))
                return;
            _sidebarPins = [];
        }
        if (shellKey == null || !IsCurrentShellHydration(shellKey, cancellationToken))
            return;
        if (!CanExposeAuthenticatedNavigation)
            _sidebarPins = [];
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
        // AppSidebar disables section toggles while labels are hidden. The
        // compact rail must always keep library icons available.
        if (!NavView.IsPaneOpen)
        {
            e.Handled = true;
            return;
        }

        ToggleLibrariesSection();
        e.Handled = true;
    }

    private void LibrariesHeader_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return;
        e.Handled = true;
        if (!NavView.IsPaneOpen) return;
        ToggleLibrariesSection();
    }

    private void ToggleLibrariesSection()
    {
        _librariesExpanded = !_librariesExpanded;
        LibrariesChevron.Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri($"ms-appx:///Assets/Icons/{(_librariesExpanded ? "chevron-down" : "chevron-right")}.svg"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(
            LibrariesHeader,
            _librariesExpanded ? "Collapse libraries" : "Expand libraries");
        UpdateLibraryNavigationVisibility(isOpen: true);
    }

    private void UpdateLibraryNavigationVisibility(bool isOpen)
    {
        var headerIndex = NavView.MenuItems.IndexOf(LibrariesHeader);
        if (headerIndex < 0)
            return;

        // The expanded sidebar honors the user's Libraries toggle. The detail
        // rail always exposes library icons, matching AppSidebar.tsx's
        // `(showLabels ? librariesExpanded : true)` contract.
        var visible = !isOpen || _librariesExpanded;
        for (var i = headerIndex + 1; i < NavView.MenuItems.Count; i++)
        {
            if (NavView.MenuItems[i] is NavigationViewItemHeader)
                break;
            if (NavView.MenuItems[i] is NavigationViewItem { Tag: Library } item)
                item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Appearance follows the server-wide theme. Profile theme switching was
    // retired upstream; keep the legacy host empty for existing shell layout.
    private void BuildThemeDots()
    {
        ThemeDotsPanel.Children.Clear();
        ThemeDotsPanel.Visibility = Visibility.Collapsed;
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        DismissProfileFlyout();
        _navigationService.Navigate<SettingsPage>();
    }

    private void DismissProfileFlyout()
    {
        ProfileFooterFlyout.Hide();
        // Flyout presenters live in a separate popup root. A navigation in the
        // same routed event can otherwise leave that root painted over the new
        // page until the next pointer action.
        DispatcherQueue.TryEnqueue(() => ProfileFooterFlyout.Hide());
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
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
                    case "Catalog":
                        _navigationService.Navigate<CatalogPage>();
                        break;
                    case "Recommendations":
                        _navigationService.Navigate<RecommendationsPage>();
                        break;
                    case "Calendar":
                        _navigationService.Navigate<CalendarPage>();
                        break;
                    case "Requests":
                        _navigationService.Navigate<RequestsPage>();
                        break;
                    case "Notifications":
                        _navigationService.Navigate<NotificationsPage>();
                        break;
                    case "Favorites":
                        _navigationService.Navigate<CatalogPage>(new CatalogNavigation(
                            "favorites",
                            "Favorites",
                            "Movies and shows you've marked as favorites."));
                        break;
                    case "Watchlist":
                        _navigationService.Navigate<CatalogPage>(new CatalogNavigation(
                            "watchlist",
                            "Watchlist",
                            "Things you've saved to watch later."));
                        break;
                    case "WatchParty":
                        _navigationService.Navigate<WatchTogetherJoinPage>();
                        break;
                    case "History":
                        _navigationService.Navigate<CatalogPage>(new CatalogNavigation(
                            "history",
                            "History",
                            "Everything you've recently watched."));
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
            else if (args.InvokedItemContainer is NavigationViewItem primaryItem &&
                     primaryItem.Tag is PrimaryMenuNavTag primaryTag)
            {
                NavigatePrimaryMenuItem(primaryTag.Item);
            }
            else if (args.InvokedItemContainer is NavigationViewItem pinItem && pinItem.Tag is SidebarPinNavTag pinTag)
            {
                if (string.Equals(pinTag.PinType, "section", StringComparison.OrdinalIgnoreCase))
                {
                    _navigationService.Navigate<CatalogPage>(new CatalogNavigation(
                        Source: "section",
                        Title: pinTag.Label,
                        Scope: "library",
                        SectionId: pinTag.PinId,
                        LibraryId: pinTag.LibraryId));
                }
                else
                {
                    _navigationService.Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
                    {
                        CollectionId = pinTag.PinId,
                        Title = pinTag.Label,
                        IsUserCollection = false,
                        LibraryId = pinTag.LibraryId,
                    });
                }
            }
            else if (args.InvokedItemContainer is NavigationViewItem pluginItem &&
                     pluginItem.Tag is PluginAppNavTag pluginTag)
            {
                _navigationService.Navigate<PluginRoutePage>(new PluginRoutePage.NavigationArgs(
                    pluginTag.InstallationId,
                    pluginTag.RoutePath,
                    pluginTag.Label));
            }

            CloseMobileNavigationPane();
        }
        catch (Exception ex)
        {
            LogNavigationFailure("sidebar_navigation", ex);
            ShowPlaybackError(
                "Page failed to open",
                "Silo could not open that page. The current page is still available, and you can dismiss this message and continue navigating.");
        }
    }

    private void NavigatePrimaryMenuItem(PrimaryMenuItem item)
    {
        if (item.Type == "builtin")
        {
            switch (item.Destination)
            {
                case "home": _navigationService.Navigate<HomePage>(); return;
                case "for_you": _navigationService.Navigate<RecommendationsPage>(); return;
                case "calendar": _navigationService.Navigate<CalendarPage>(); return;
            }
        }

        if (item.Type == "library" && item.LibraryId is int libraryId)
        {
            var library = _viewModel.Libraries.FirstOrDefault(candidate => candidate.Id == libraryId);
            if (library != null) _navigationService.Navigate<LibraryPage>(library);
            return;
        }

        if (item.Type == "section" && item.LibraryId is int sectionLibraryId &&
            !string.IsNullOrWhiteSpace(item.SectionId))
        {
            _navigationService.Navigate<CatalogPage>(new CatalogNavigation(
                Source: "section",
                Title: item.Label ?? "Section",
                Scope: "library",
                SectionId: item.SectionId,
                LibraryId: sectionLibraryId));
            return;
        }

        if (item.Type == "collection" && !string.IsNullOrWhiteSpace(item.CollectionId))
        {
            _navigationService.Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = item.CollectionId,
                Title = item.Label ?? "Collection",
                IsUserCollection = item.LibraryId is null,
                LibraryId = item.LibraryId,
            });
        }
    }

    private void SwitchProfile_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to profile select, keeping existing auth
        DismissProfileFlyout();
        HideMainNavigation();
        _navigationService.Navigate<ProfileSelectPage>("switch");
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        DismissProfileFlyout();
        try
        {
            await _playerService.CloseAsync();
        }
        catch
        {
            // Signing out must not be blocked by a failed final playback report.
        }

        await _authService.LogoutAsync();
    }

    private void OnAuthLoggedOut()
    {
        if (_savedRestoreInProgress) return; // Restore owns its retry/login route and preserves the selected server.
        _pendingAccessRefresh = false;
        DispatcherQueue.TryEnqueue(async () =>
        {
            TryShellAction("shared_appearance_logout", _themeService.ResetSharedAppearance);
            try
            {
                await _playerService.CloseAsync();
            }
            catch
            {
                // Auth loss is authoritative; close the UI even if playback cleanup fails.
            }

            var settings = _settingsService.Load();
            if (!string.IsNullOrEmpty(settings.LastProfileId))
            {
                settings.LastProfileId = null;
                _settingsService.Save(settings);
            }

            HideMainNavigation();
            _navigationService.Navigate<ServerSelectPage>();
        });
    }

    private void OnProfileVerificationRequired()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await _playerService.CloseAsync();
            }
            catch
            {
                // Losing profile authorization must still return to the PIN chooser.
            }

            _catalogApi.InvalidateLibraryCache();
            HideMainNavigation();
            _navigationService.Navigate<ProfileSelectPage>();
        });
    }

    private void OnCredentialStoreFailed(Exception exception)
    {
        App.Services.GetRequiredService<ToastService>().Error(
            "Windows could not update Silo's secure sign-in data. You may need to sign in again after restarting.");
        LocalLog.AppendLine("auth_errors.txt", $"credential_store | {exception.GetType().Name}: {exception.Message}");
    }

}
