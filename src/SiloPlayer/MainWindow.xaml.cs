using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Plugins;
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
    private readonly PlayerService _playerService;
    private readonly ThemeService _themeService;
    private bool _notificationsAvailable = true;
    private int _notificationUnreadCount;
    private bool _routeWantsCompactPane;
    private bool _isNarrowShell;
    private bool _sidebarHoverExpanded;
    private bool _pointerInsideSidebar;
    private bool _profileFooterFlyoutOpen;
    private bool _mobileHeaderHidden;
    private DispatcherTimer? _sidebarHoverTimer;
    private bool _isWindowActive;
    private double _currentWindowWidth = 1280;
    private Type? _lastShellPageType;
    private object? _lastShellParameter;

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
        _authApi = App.Services.GetRequiredService<AuthApi>();
        _apiClient = App.Services.GetRequiredService<SiloApiClient>();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        _requestsApi = App.Services.GetRequiredService<RequestsApi>();
        _notificationsApi = App.Services.GetRequiredService<NotificationsApi>();
        _themeService = App.Services.GetRequiredService<ThemeService>();

        NavView.PaneOpened += (_, _) => UpdateSidebarPanePresentation(isOpen: true);
        NavView.PaneClosed += (_, _) => UpdateSidebarPanePresentation(isOpen: false);
        NavView.PointerMoved += NavView_PointerMoved;
        NavView.PointerExited += NavView_PointerExited;
        RootGrid.AddHandler(
            UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler(RootGrid_KeyDown),
            handledEventsToo: true);
        UpdateSidebarPanePresentation(NavView.IsPaneOpen);

        _navigationService.Frame = ContentFrame;

        // F7: update window title on every navigation.
        _navigationService.Navigated += OnNavigated_UpdateWindowTitle;
        _navigationService.Navigated += OnNavigated_ApplyAccessibility;
        _navigationService.Navigated += OnNavigated_SynchronizeShellChrome;
        _navigationService.Navigated += OnNavigated_AnimatePageEntrance;
        if (AppWindow != null) AppWindow.Title = DocumentTitle.AppName;

        // F2: register the toast host with the ToastService so any VM/page
        // can call App.Services.GetRequiredService<ToastService>().Success(...).
        var toastService = App.Services.GetRequiredService<ToastService>();
        toastService.Register(ToastHost, DispatcherQueue);

        // Hide the nav view initially -- it shows only after login.
        // Server Activity button follows the same admin-gate as AdminButton and
        // stays hidden until ShowMainNavigation() fires post-login.
        NavView.IsPaneVisible = false;
        MainServerActivityButton.SetHostVisibility(false);
        MobileServerActivityButton.SetHostVisibility(false);

        // Wire Server Activity "View all" callbacks. Routes navigate through
        // AdminShellPage so the admin sidebar stays present — passing the target
        // sub-page type as a parameter, which AdminShellPage consumes in
        // OnNavigatedTo and opens inside its internal AdminContentFrame.
        //
        // HideWhenEmpty=false keeps the button visible for admins on every page
        // (not just when something is active). Non-admin users never see it
        // regardless — the role check inside the control handles that.
        MainServerActivityButton.HideWhenEmpty = true;
        MainServerActivityButton.OnViewStreams = () =>
        {
            // Hide the main sidebar BEFORE navigating — same as Admin_Click —
            // otherwise the user sees two navigation panes side-by-side
            // (main nav + AdminShellPage's own admin nav).
            NavView.IsPaneVisible = false;
            HideSharedServerActivity();
            _navigationService.Navigate<Views.Admin.AdminShellPage>(typeof(Views.Admin.AdminActivityPage));
        };
        MainServerActivityButton.OnViewTasks = () =>
        {
            NavView.IsPaneVisible = false;
            HideSharedServerActivity();
            _navigationService.Navigate<Views.Admin.AdminShellPage>(typeof(Views.Admin.AdminTasksPage));
        };
        MainServerActivityButton.OnViewScans = () =>
        {
            NavView.IsPaneVisible = false;
            HideSharedServerActivity();
            _navigationService.Navigate<Views.Admin.AdminShellPage>(typeof(Views.Admin.AdminLibrariesPage));
        };
        MobileServerActivityButton.HideWhenEmpty = true;
        MobileServerActivityButton.OnViewStreams = () => MainServerActivityButton.OnViewStreams?.Invoke();
        MobileServerActivityButton.OnViewTasks = () => MainServerActivityButton.OnViewTasks?.Invoke();
        MobileServerActivityButton.OnViewScans = () => MainServerActivityButton.OnViewScans?.Invoke();

        // Listen for player state changes
        _playerService = App.Services.GetRequiredService<PlayerService>();
        _playerService.StateChanged += OnPlayerStateChanged;
        _authService.LoggedOut += OnAuthLoggedOut;
        _authService.UserChanged += OnAuthUserChanged;
        _authService.ProfileVerificationRequired += OnProfileVerificationRequired;
        _authService.CredentialStoreFailed += OnCredentialStoreFailed;

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
    }

    private void ApplyResponsiveShellLayout()
    {
        var isNarrow = _currentWindowWidth < 1024;
        _isNarrowShell = isNarrow;
        if (isNarrow)
        {
            _sidebarHoverExpanded = false;
            _pointerInsideSidebar = false;
            StopSidebarHoverTimer();
        }
        else
        {
            SetMobileHeaderHidden(false);
        }

        // Layout.tsx hides the fixed desktop sidebar below Tailwind's lg
        // breakpoint and exposes navigation through a mobile menu. WinUI's
        // LeftMinimal mode is the native equivalent: content receives the
        // full window width and the pane opens as an overlay from its toggle.
        NavView.PaneDisplayMode = isNarrow
            ? NavigationViewPaneDisplayMode.LeftMinimal
            : NavigationViewPaneDisplayMode.Left;
        // The current WebUI supplies a complete mobile header instead of a
        // lone stock hamburger row.
        NavView.IsPaneToggleButtonVisible = false;
        MobileShellHeader.Visibility =
            isNarrow &&
            NavView.IsPaneVisible &&
            CanExposeAuthenticatedNavigation &&
            ContentFrame.Content is not Views.Admin.AdminShellPage
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (!NavView.IsPaneVisible)
        {
            UpdateServerActivityHostVisibility();
            return;
        }

        var shouldOpen = !isNarrow &&
            (!_routeWantsCompactPane || _sidebarHoverExpanded || _profileFooterFlyoutOpen);
        NavView.IsPaneOpen = shouldOpen;
        UpdateSidebarPanePresentation(shouldOpen);
        UpdateServerActivityHostVisibility();
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

    private void UpdateServerActivityHostVisibility()
    {
        var shellActive =
            NavView.IsPaneVisible &&
            CanExposeAuthenticatedNavigation &&
            ContentFrame.Content is not Views.Admin.AdminShellPage;
        var canShow = shellActive && AuthorizationPolicy.IsActingAdmin(_authService);
        MainServerActivityButton.SetHostVisibility(canShow && !_isNarrowShell);
        MobileServerActivityButton.SetHostVisibility(canShow && _isNarrowShell);
    }

    private void HideSharedServerActivity()
    {
        MainServerActivityButton.SetHostVisibility(false);
        MobileServerActivityButton.SetHostVisibility(false);
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

    private void NavView_PointerMoved(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_routeWantsCompactPane || _isNarrowShell || !NavView.IsPaneVisible)
            return;

        var pointerX = e.GetCurrentPoint(NavView).Position.X;
        var paneWidth = NavView.IsPaneOpen
            ? NavView.OpenPaneLength
            : NavView.CompactPaneLength;
        var isInsidePane = pointerX >= 0 && pointerX <= paneWidth;
        if (isInsidePane == _pointerInsideSidebar)
            return;

        _pointerInsideSidebar = isInsidePane;
        if (isInsidePane)
        {
            if (NavView.IsPaneOpen)
                return;

            StopSidebarHoverTimer();
            _sidebarHoverTimer = new DispatcherTimer
            {
                // AppSidebar.tsx deliberately waits 150ms so merely crossing
                // the compact rail does not expand it accidentally.
                Interval = TimeSpan.FromMilliseconds(150),
            };
            _sidebarHoverTimer.Tick += SidebarHoverTimer_Tick;
            _sidebarHoverTimer.Start();
        }
        else
        {
            StopSidebarHoverTimer();
            CollapseImmersiveSidebarAfterPointerExit();
        }
    }

    private void NavView_PointerExited(
        object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _pointerInsideSidebar = false;
        StopSidebarHoverTimer();
        CollapseImmersiveSidebarAfterPointerExit();
    }

    private void SidebarHoverTimer_Tick(object? sender, object e)
    {
        StopSidebarHoverTimer();
        if (!_pointerInsideSidebar ||
            !_routeWantsCompactPane ||
            _isNarrowShell ||
            !NavView.IsPaneVisible)
        {
            return;
        }

        _sidebarHoverExpanded = true;
        ApplyResponsiveShellLayout();
    }

    private void StopSidebarHoverTimer()
    {
        if (_sidebarHoverTimer == null)
            return;

        _sidebarHoverTimer.Stop();
        _sidebarHoverTimer.Tick -= SidebarHoverTimer_Tick;
        _sidebarHoverTimer = null;
    }

    private void CollapseImmersiveSidebarAfterPointerExit()
    {
        if (!_routeWantsCompactPane || _isNarrowShell || _profileFooterFlyoutOpen)
            return;

        _sidebarHoverExpanded = false;
        ApplyResponsiveShellLayout();
    }

    private void ProfileFooterFlyout_Opened(object sender, object e)
    {
        _profileFooterFlyoutOpen = true;
        if (_routeWantsCompactPane && !_isNarrowShell)
        {
            _sidebarHoverExpanded = true;
            ApplyResponsiveShellLayout();
        }
    }

    private void ProfileFooterFlyout_Closed(object sender, object e)
    {
        _profileFooterFlyoutOpen = false;
        if (!_pointerInsideSidebar)
            CollapseImmersiveSidebarAfterPointerExit();
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
        _authService.LoggedOut -= OnAuthLoggedOut;
        _authService.UserChanged -= OnAuthUserChanged;
        _authService.ProfileVerificationRequired -= OnProfileVerificationRequired;
        _authService.CredentialStoreFailed -= OnCredentialStoreFailed;
        _playerService.ShowPlayingNextRequested -= OnShowPlayingNextRequested;
        _playerService.PostRollReturnRequested -= OnPostRollReturnRequested;
        this.SizeChanged -= OnWindowSizeChanged;
        if (AppWindow != null) AppWindow.Changed -= OnAppWindowChanged;
        _navigationService.Navigated -= OnNavigated_UpdateWindowTitle;
        _navigationService.Navigated -= OnNavigated_ApplyAccessibility;
        _navigationService.Navigated -= OnNavigated_SynchronizeShellChrome;
        NavView.PointerMoved -= NavView_PointerMoved;
        NavView.PointerExited -= NavView_PointerExited;
        StopSidebarHoverTimer();
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
        DispatcherQueue.TryEnqueue(() =>
        {
            var shouldFocusOverlay = _isWindowActive || _playerService.IsPlaybackSurfaceForeground;

            // The early post-roll surface can already be visible when true EOF
            // arrives. In that case only transition the preview/countdown state;
            // do not rebuild the artwork and On Deck data.
            if (videoEnded && PlayingNextOverlay.Visibility == Visibility.Visible)
            {
                _playerService.FinishPostRollPreview();
                UpdatePlayingNextAutoPlayVisuals();
                if (!string.IsNullOrWhiteSpace(_playerService.NextEpisodeContentId) &&
                    _playingNextAutoPlay)
                {
                    StartPlayingNextCountdown();
                }
                return;
            }

            var hasNextEpisode = !string.IsNullOrWhiteSpace(_playerService.NextEpisodeContentId);
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
            !string.IsNullOrWhiteSpace(_playerService.NextEpisodeContentId) &&
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
        var hasNextEpisode = !string.IsNullOrWhiteSpace(_playerService.NextEpisodeContentId);
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
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var bytes = await httpClient.GetByteArrayAsync(url);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
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
        _routeWantsCompactPane = IsDetailPage(pageType);
        if (!_routeWantsCompactPane)
        {
            _sidebarHoverExpanded = false;
            _pointerInsideSidebar = false;
            StopSidebarHoverTimer();
        }
        ApplyResponsiveShellLayout();
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

    private bool _autoLoginAttempted;

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

        _authService.AbandonRestoreAttempt(restoreGeneration);
        HideMainNavigation();
        LocalLog.AppendLine("auth_startup.txt", "restore_abandoned");
        _navigationService.Navigate<ServerSelectPage>();
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

        var profilesResponse = await _authApi.GetProfilesAsync(cancellationToken);
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
    private string? _hydratedShellKey;
    private bool _playerPrewarmed;

    private sealed record PluginAppNavTag(int InstallationId, string RoutePath, string Label);

    private bool CanExposeAuthenticatedNavigation =>
        _authService.IsLoggedIn &&
        !string.IsNullOrWhiteSpace(_authService.SelectedProfileId);

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
            _hydratedShellKey = shellKey;

        // None of the shell decoration below is allowed to invalidate a
        // successful profile selection or page navigation. Themes, plugin
        // links, library pins, and player prewarming are all supplemental.
        TryShellAction("shell_visibility", () =>
        {
            // Always update admin button and profile display for current user.
            bool isAdmin = AuthorizationPolicy.IsActingAdmin(_authService);
            AdminButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            var adminShellActive = ContentFrame.Content is Views.Admin.AdminShellPage;
            if (adminShellActive)
            {
                // AdminShell owns both its sidebar and ServerActivity button.
                // ShowMainNavigation can run immediately after a successful
                // authenticated navigation, so it must not re-layer the main
                // shell controls after the Navigated handler hid them.
                NavView.IsPaneVisible = false;
                HideSharedServerActivity();
                return;
            }

            NavView.IsPaneVisible = true;
            // HideMainNavigation closes the pane while login/profile selection
            // owns the window. Reopening only IsPaneVisible leaves NavigationView
            // in its 64px compact state, which clips the custom Admin/profile
            // footer controls. Restore the route-appropriate pane state as part
            // of the same authenticated shell transition.
            NavView.IsPaneOpen = true;
            ApplyResponsiveShellLayout();
            UpdateSidebarPanePresentation(NavView.IsPaneOpen);
            UpdateServerActivityHostVisibility();
        });

        if (shouldHydrateShell)
        {
            _ = RunShellWorkAsync("profile_display", UpdateProfileDisplayAsync);
            _ = RunShellWorkAsync("user_navigation_capabilities", RefreshUserNavigationCapabilitiesAsync);
            _ = RunShellWorkAsync("theme_sync", SyncThemeAfterNavigationAsync);
            TryShellAction("theme_switcher", BuildThemeDots);
            _ = RunShellWorkAsync("plugin_navigation", RefreshPluginAppsAsync);

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
            _ = RunShellWorkAsync("library_navigation_load", LoadShellNavigationAsync);
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

    private async Task LoadShellNavigationAsync()
    {
        await _viewModel.LoadLibrariesCommand.ExecuteAsync(null);
        if (!CanExposeAuthenticatedNavigation)
            return;

        await RefreshSidebarPinsAsync();
    }

    private async Task RefreshUserNavigationCapabilitiesAsync()
    {
        try
        {
            var requestStatus = await _requestsApi.GetStatusAsync();
            RequestsNavItem.Visibility = requestStatus.RequestsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch
        {
            // WebUI keeps Requests hidden until the capability explicitly says
            // it is enabled.
            RequestsNavItem.Visibility = Visibility.Collapsed;
        }

        try
        {
            var capability = await _notificationsApi.GetCapabilityAsync();
            _notificationsAvailable = capability.InApp.Enabled;
            NotificationsNavItem.Visibility = _notificationsAvailable
                ? Visibility.Visible
                : Visibility.Collapsed;
            _notificationUnreadCount = _notificationsAvailable
                ? await _notificationsApi.GetUnreadCountAsync()
                : 0;
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
        SiloWordmarkImage.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        SiloMarkImage.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;

        AdminButtonLabel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        AdminButtonContent.Spacing = isOpen ? 10 : 0;
        AdminButton.HorizontalAlignment = isOpen ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        AdminButton.Width = isOpen ? double.NaN : 40;
        AdminButton.Height = isOpen ? double.NaN : 40;
        AdminButton.Padding = isOpen ? new Thickness(16, 10, 16, 10) : new Thickness(0);
        AdminButton.HorizontalContentAlignment = isOpen
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;

        ProfileNameText.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        ProfileFooterContent.Spacing = isOpen ? 10 : 0;
        ProfileFooterButton.HorizontalAlignment = isOpen ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        ProfileFooterButton.Width = isOpen ? double.NaN : 40;
        ProfileFooterButton.Height = isOpen ? double.NaN : 40;
        ProfileFooterButton.Padding = isOpen ? new Thickness(16, 10, 16, 10) : new Thickness(0);
        ProfileFooterButton.HorizontalContentAlignment = isOpen
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;

        var hasUnread = _notificationsAvailable && _notificationUnreadCount > 0;
        NotificationUnreadBadge.Visibility = hasUnread ? Visibility.Visible : Visibility.Collapsed;
        // InfoBadge renders Value=-1 as a compact dot and values above 99 as
        // 99+, matching the current WebUI's compact/open notification states.
        NotificationUnreadBadge.Value = isOpen ? _notificationUnreadCount : -1;
    }

    private async Task RunShellWorkAsync(string stage, Func<Task> work)
    {
        try
        {
            await work();
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
                _playerService.SeekTo(Math.Max(0, _playerService.Position - settings.AudiobookSkipBackSeconds));
                break;
            case Windows.System.VirtualKey.Right:
                _playerService.SeekTo(Math.Min(_playerService.Duration, _playerService.Position + settings.AudiobookSkipForwardSeconds));
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

    /// <summary>
    /// The admin shell owns its own server-activity control and navigation rail.
    /// Enforce that ownership at the frame boundary so every route into Admin --
    /// including deep links, back-stack restores, and activity-popover links --
    /// cannot leave the main-shell activity control layered underneath it.
    /// </summary>
    private void OnNavigated_SynchronizeShellChrome(object? sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _lastShellPageType = e.SourcePageType;
        _lastShellParameter = e.Parameter;

        if (e.SourcePageType == typeof(Views.Admin.AdminShellPage))
        {
            NavView.SelectedItem = null;
            NavView.IsPaneVisible = false;
            HideSharedServerActivity();
            return;
        }

        if (!CanExposeAuthenticatedNavigation)
        {
            NavView.SelectedItem = null;
            return;
        }

        // A Back operation or deep link can leave Admin without passing through
        // its explicit exit button. Restore the shared shell at the frame
        // boundary so it never remains hidden after returning to user pages.
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
                    ApplyProfileAvatar(profile.AvatarUrl);
                });
            }
        }
        catch
        {
            // Non-critical, leave default text
        }
    }

    private void ApplyProfileAvatar(string? avatarUrl)
    {
        if (!Uri.TryCreate(avatarUrl, UriKind.Absolute, out var avatarUri))
        {
            ProfileAvatarBrush.ImageSource = null;
            ProfileDropdownAvatarBrush.ImageSource = null;
            ProfileAvatarImage.Visibility = Visibility.Collapsed;
            ProfileDropdownAvatarImage.Visibility = Visibility.Collapsed;
            return;
        }

        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        bitmap.ImageFailed += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(ProfileAvatarBrush.ImageSource, bitmap))
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
        bitmap.UriSource = avatarUri;
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
        // Keep the Server Activity button in sync with the rest of the shell —
        // while the nav is hidden (login / profile select / setup), no admin
        // chrome should be visible.
        HideSharedServerActivity();
    }

    private void NavView_PaneClosing(
        NavigationView sender,
        NavigationViewPaneClosingEventArgs args)
    {
        // This shell has no compact-mode toggle. Allowing NavigationView to
        // auto-close at a transient width leaves the custom Admin/profile
        // footer inside a clipped 64px rail and it can remain stranded there
        // after the window grows again. Authenticated navigation is therefore
        // persistently open, matching the desktop WebUI sidebar.
        if (_isNarrowShell)
        {
            UpdateSidebarPanePresentation(isOpen: false);
        }
        else if (sender.IsPaneVisible &&
                 CanExposeAuthenticatedNavigation &&
                 (!_routeWantsCompactPane || _sidebarHoverExpanded || _profileFooterFlyoutOpen))
        {
            args.Cancel = true;
            UpdateSidebarPanePresentation(isOpen: true);
        }
        else if (_routeWantsCompactPane)
        {
            UpdateSidebarPanePresentation(isOpen: false);
        }
    }

    public void RestoreMainPane()
    {
        if (!CanExposeAuthenticatedNavigation)
        {
            HideMainNavigation();
            return;
        }

        _routeWantsCompactPane = false;
        NavView.IsPaneVisible = true;
        NavView.IsPaneOpen = true;
        ApplyResponsiveShellLayout();
        UpdateServerActivityHostVisibility();
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
                NavView.Margin = new Thickness(0, 0, 0, _playerService.IsAudiobook ? 108 : 132);
                ApplyResponsiveShellLayout();
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

    public Task ShowSubtitleAiDialogAsync(XamlRoot? dialogXamlRoot = null)
        => PlayerOverlayControl.ShowSubtitleAiDialogAsync(dialogXamlRoot);

    public Task ShowMarkerEditDialogAsync(XamlRoot? dialogXamlRoot = null)
        => PlayerOverlayControl.ShowMarkerEditDialogAsync(dialogXamlRoot);

    public void NavigateToHome()
    {
        if (!_navigationService.Navigate<HomePage>())
            throw new InvalidOperationException("The navigation frame rejected the Home page.");
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

    private async Task SyncThemeAfterNavigationAsync()
    {
        await _themeService.SyncFromServerAsync();
        TryShellAction("theme_switcher_refresh", BuildThemeDots);
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

        ResynchronizeSelectedNavigationItem();
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

    public bool IsSidebarPin(int libraryId, string pinType, string pinId)
    {
        return _sidebarPins.TryGetValue(libraryId.ToString(), out var pins)
            && pins.Any(pin =>
                string.Equals(pin.Type, pinType, StringComparison.OrdinalIgnoreCase)
             && string.Equals(pin.Id, pinId, StringComparison.Ordinal));
    }

    private async Task RefreshPluginAppsAsync()
    {
        if (!CanExposeAuthenticatedNavigation)
        {
            BuildPluginApps([]);
            return;
        }

        try
        {
            var response = await _settingsApi.GetPluginSettingsListAsync();
            DispatcherQueue.TryEnqueue(() => BuildPluginApps(response.Installations));
        }
        catch
        {
            DispatcherQueue.TryEnqueue(() => BuildPluginApps([]));
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
                    Icon = new FontIcon { Glyph = "\uEA86" },
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

    /// <summary>
    /// Reloads the cached sidebar pins from the server setting and rebuilds
    /// the library nav. Call on sign-in and after any CollectionBrowsePage
    /// pin toggle.
    /// </summary>
    public async Task RefreshSidebarPinsAsync()
    {
        if (!CanExposeAuthenticatedNavigation)
        {
            _sidebarPins = [];
            UpdateLibraryNavItems();
            return;
        }

        try
        {
            var settingsApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.SettingsApi>();
            var entry = await settingsApi.GetSettingAsync("sidebar_pins");
            _sidebarPins = ParseSidebarPins(entry.Value);
        }
        catch
        {
            _sidebarPins = [];
        }
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
                if (navItem.Tag is SiloPlayer.Core.Models.Catalog.Library)
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

    private string _activeThemeId = "midnight-cinema";

    private void BuildThemeDots()
    {
        var themeService = App.Services.GetRequiredService<ThemeService>();
        _activeThemeId = themeService.CurrentTheme;
        ThemeDotsPanel.Children.Clear();
        ThemeDotsPanel.Visibility = Visibility.Visible;

        var fallbackAccent = new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseHexColor("#e8e8ec"));
        var fallbackBorder = new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseHexColor("#34343a"));
        var accentBrush = Application.Current.Resources.TryGetValue("AccentBrush", out var accentResource)
            ? accentResource as Microsoft.UI.Xaml.Media.Brush ?? fallbackAccent
            : fallbackAccent;
        var borderBrush = Application.Current.Resources.TryGetValue("BorderBrush", out var borderResource)
            ? borderResource as Microsoft.UI.Xaml.Media.Brush ?? fallbackBorder
            : fallbackBorder;

        foreach (var (id, label, bgHex, accentHex) in CuratedThemes)
        {
            bool isActive = id == _activeThemeId;
            var dot = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseHexColor(bgHex)),
                BorderBrush = isActive
                    ? accentBrush
                    : borderBrush,
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
                themeService.ApplyTheme(capturedId);
                BuildThemeDots();
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
        HideSharedServerActivity();
        _navigationService.Navigate<Views.Admin.AdminShellPage>();
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

    private void SwitchProfile_Click(object sender, RoutedEventArgs e)
    {
        // Navigate to profile select, keeping existing auth
        HideMainNavigation();
        _navigationService.Navigate<ProfileSelectPage>("switch");
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
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
        DispatcherQueue.TryEnqueue(async () =>
        {
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

            HideImpersonationBanner();
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

    // ===== Impersonation =====

    private void OnAuthUserChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var user = _authService.CurrentUser;
            if (user?.Impersonation?.Active == true)
            {
                ShowImpersonationBanner(
                    user.Username,
                    user.Impersonation.ImpersonatorUsername);
            }
            else
            {
                HideImpersonationBanner();
            }
        });
    }

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

    private async void ImpersonationBanner_EndRequested(object? sender, EventArgs e)
    {
        ImpersonationBannerControl.IsEnding = true;
        try
        {
            // Stop user-scoped playback before replacing its authentication context.
            await _playerService.CloseAsync();
            var returnPath = await _authService.EndImpersonationAsync();
            HideImpersonationBanner();

            NavView.IsPaneVisible = false;
            HideSharedServerActivity();

            const string userPrefix = "/admin/users/";
            if (returnPath.StartsWith(userPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(returnPath[userPrefix.Length..].Trim('/'), out var userId))
            {
                _navigationService.Navigate<Views.Admin.AdminShellPage>(
                    new Views.Admin.AdminShellNavigation(
                        typeof(Views.Admin.AdminUserDetailPage),
                        userId));
            }
            else
            {
                _navigationService.Navigate<Views.Admin.AdminShellPage>(
                    typeof(Views.Admin.AdminUsersPage));
            }

            App.Services.GetRequiredService<ToastService>()
                .Success("Administrator session restored.");
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error(ex.Message);
        }
        finally
        {
            ImpersonationBannerControl.IsEnding = false;
        }
    }
}
