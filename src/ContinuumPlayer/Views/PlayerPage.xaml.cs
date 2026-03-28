using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Player;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class PlayerPage : Page
{
    private PlayerViewModel? _vm;
    private MpvPlayer? _player;
    private DispatcherTimer? _hideTimer;
    private bool _isSeeking;
    private bool _isFullscreen;
    private bool _controlsVisible;
    private bool _isDisposed;
    private string? _contentId;

    public PlayerPage()
    {
        this.InitializeComponent();
    }

    // ── Page lifecycle ───────────────────────────────────────────────────

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Focus the page so keyboard events work
        this.Focus(FocusState.Programmatic);

        // Set up hide timer for controls
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            HideControls();
        };

        // Get content ID from navigation parameter
        var frame = this.Frame;
        if (frame?.BackStack.Count > 0 || _contentId != null)
        {
            // contentId was set via OnNavigatedTo
        }

        if (string.IsNullOrEmpty(_contentId))
        {
            ShowError("No content specified for playback.");
            return;
        }

        await InitializePlaybackAsync(_contentId);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string contentId && !string.IsNullOrEmpty(contentId))
            _contentId = contentId;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CleanupAsync();
    }

    private async void CleanupAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _hideTimer?.Stop();

        // Restore from fullscreen
        if (_isFullscreen)
            ToggleFullscreen();

        // Show nav again
        if (App.MainWindowInstance is MainWindow mw)
            mw.ShowMainNavigation();

        // Stop playback session
        if (_vm?.Manager != null)
        {
            try { await _vm.Manager.StopSessionAsync(); }
            catch { }
        }

        // Dispose mpv
        _player?.Dispose();
        _player = null;
    }

    // ── Playback initialization ──────────────────────────────────────────

    private async Task InitializePlaybackAsync(string contentId)
    {
        try
        {
            // Hide nav for immersive experience
            if (App.MainWindowInstance is MainWindow mw)
                mw.HideMainNavigation();

            // Create view model via DI
            _vm = App.Services.GetRequiredService<PlayerViewModel>();
            _vm.IsLoading = true;

            // 1. Get watch detail
            var watchDetail = await _vm.Manager.GetWatchDetailAsync(contentId);

            // Build title
            if (watchDetail.SeasonNumber.HasValue && watchDetail.EpisodeNumber.HasValue)
                _vm.Title = $"{watchDetail.SeriesTitle ?? watchDetail.Title} - S{watchDetail.SeasonNumber:D2}E{watchDetail.EpisodeNumber:D2}";
            else
                _vm.Title = watchDetail.Title;

            TitleText.Text = _vm.Title;

            // Populate versions
            foreach (var v in watchDetail.Versions)
                _vm.Versions.Add(v);

            // 2. Select best version
            var bestVersion = _vm.Manager.SelectBestVersion(watchDetail.Versions);
            if (bestVersion == null)
            {
                ShowError("No playable version found.");
                return;
            }

            _vm.Resolution = bestVersion.Resolution;

            // Determine start position from user data
            double startPosition = 0;
            if (watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
                startPosition = watchDetail.UserData.PositionSeconds!.Value;

            // 3. Start session
            var session = await _vm.Manager.StartSessionAsync(bestVersion.FileId, startPosition);
            _vm.PlayMethod = session.PlayMethod;

            // Update playback info display
            UpdatePlaybackInfo();

            // Populate subtitle tracks
            foreach (var sub in session.SubtitleUrls)
                _vm.SubtitleTracks.Add(sub);

            BuildSubtitleFlyout();

            // 4. Initialize mpv
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);

            _player = new MpvPlayer();
            var (px, py, pw, ph) = GetVideoHostBounds();
            _player.Initialize(windowHandle, pw, ph);

            // Position the video window correctly
            _player.ResizeVideoWindow(px, py, pw, ph);

            // 5. Subscribe to mpv events (must marshal to UI thread)
            var dq = DispatcherQueue;

            _player.PositionChanged += pos =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed || _vm == null) return;
                    _vm.UpdatePosition(pos);
                    if (!_isSeeking)
                    {
                        SeekSlider.Maximum = _vm.SeekMax;
                        SeekSlider.Value = pos;
                    }
                    PositionText.Text = _vm.PositionDisplay;
                    UpdateSkipButtons();
                });
            };

            _player.DurationChanged += dur =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed || _vm == null) return;
                    _vm.UpdateDuration(dur);
                    SeekSlider.Maximum = _vm.SeekMax;
                    DurationText.Text = _vm.DurationDisplay;
                });
            };

            _player.PauseChanged += paused =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed || _vm == null) return;
                    _vm.IsPlaying = !paused;
                    PlayPauseIcon.Glyph = paused ? "\uE768" : "\uE769";
                });
            };

            _player.PlaybackEnded += () =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed) return;
                    NavigateBack();
                });
            };

            _player.FileLoaded += () =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed || _vm == null) return;
                    _vm.IsLoading = false;
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    // Auto-play
                    _player?.Play();
                });
            };

            _player.Error += msg =>
            {
                dq.TryEnqueue(() =>
                {
                    if (_isDisposed) return;
                    ShowError(msg);
                });
            };

            // 6. Load file
            var streamUrl = _vm.Manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ShowError("No stream URL available.");
                return;
            }

            _player.LoadFile(streamUrl);

            // 7. Load external subtitles
            var subtitleUrls = _vm.Manager.GetSubtitleUrls();
            foreach (var (track, fullUrl) in subtitleUrls)
            {
                _player.AddSubtitle(fullUrl, track.Label, track.Language);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Failed to start playback: {ex.Message}");
        }
    }

    // ── Video host positioning ───────────────────────────────────────────

    private (int x, int y, int width, int height) GetVideoHostBounds()
    {
        try
        {
            var transform = VideoHost.TransformToVisual(null);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            return (
                (int)(point.X * scale),
                (int)(point.Y * scale),
                (int)(VideoHost.ActualWidth * scale),
                (int)(VideoHost.ActualHeight * scale)
            );
        }
        catch
        {
            return (0, 0, (int)VideoHost.ActualWidth, (int)VideoHost.ActualHeight);
        }
    }

    private void VideoHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_player == null) return;
        var (px, py, pw, ph) = GetVideoHostBounds();
        if (pw > 0 && ph > 0)
            _player.ResizeVideoWindow(px, py, pw, ph);
    }

    // ── Controls visibility ──────────────────────────────────────────────

    private void ShowControls()
    {
        _controlsVisible = true;
        ControlsOverlay.Opacity = 1;
        // Move the capture layer behind controls when visible
        _hideTimer?.Stop();
        _hideTimer?.Start();
    }

    private void HideControls()
    {
        _controlsVisible = false;
        ControlsOverlay.Opacity = 0;
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
    }

    private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_controlsVisible)
            HideControls();
        else
            ShowControls();
    }

    private void Controls_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hideTimer?.Stop();
    }

    private void Controls_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hideTimer?.Stop();
        _hideTimer?.Start();
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_player == null || _isDisposed) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                _player.TogglePause();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Left:
                _player.Seek(Math.Max(0, (_vm?.Position ?? 0) - 10));
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Right:
                _player.Seek((_vm?.Position ?? 0) + 10);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Up:
                if (_vm != null)
                {
                    _vm.Volume = Math.Min(100, _vm.Volume + 5);
                    VolumeSlider.Value = _vm.Volume;
                    _player.SetVolume(_vm.Volume);
                }
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Down:
                if (_vm != null)
                {
                    _vm.Volume = Math.Max(0, _vm.Volume - 5);
                    VolumeSlider.Value = _vm.Volume;
                    _player.SetVolume(_vm.Volume);
                }
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.F:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Escape:
                if (_isFullscreen)
                    ToggleFullscreen();
                else
                    NavigateBack();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.M:
                ToggleMute();
                e.Handled = true;
                break;
        }

        ShowControls();
    }

    // ── Button click handlers ────────────────────────────────────────────

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _player?.TogglePause();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        ToggleMute();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        NavigateBack();
    }

    private void GoBackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateBack();
    }

    private void SkipIntro_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.IntroEnd != null && _player != null)
        {
            _player.Seek(_vm.IntroEnd.Value);
        }
    }

    private void SkipCredits_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.CreditsEnd != null && _player != null)
        {
            _player.Seek(_vm.CreditsEnd.Value);
        }
    }

    // ── Seek slider ──────────────────────────────────────────────────────

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSeeking && _player != null)
        {
            _player.Seek(e.NewValue);
        }
    }

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = true;
    }

    private void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isSeeking && _player != null)
        {
            _player.Seek(SeekSlider.Value);
        }
        _isSeeking = false;
    }

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isSeeking && _player != null)
        {
            _player.Seek(SeekSlider.Value);
        }
        _isSeeking = false;
    }

    // ── Volume slider ────────────────────────────────────────────────────

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.Volume = e.NewValue;
            _player?.SetVolume(e.NewValue);
            UpdateVolumeIcon();
        }
    }

    // ── Subtitle flyout ──────────────────────────────────────────────────

    private void BuildSubtitleFlyout()
    {
        SubtitleFlyout.Items.Clear();

        // "Off" option
        var offItem = new MenuFlyoutItem { Text = "Off" };
        offItem.Click += (_, _) => _player?.SetSubtitleTrack(0);
        SubtitleFlyout.Items.Add(offItem);

        if (_vm == null) return;

        foreach (var track in _vm.SubtitleTracks)
        {
            var item = new MenuFlyoutItem { Text = track.Label ?? $"{track.Language} ({track.Index})" };
            var trackIndex = track.Index;
            item.Click += (_, _) => _player?.SetSubtitleTrack(trackIndex);
            SubtitleFlyout.Items.Add(item);
        }
    }

    // ── Skip markers ─────────────────────────────────────────────────────

    private void UpdateSkipButtons()
    {
        if (_vm == null) return;
        SkipIntroButton.Visibility = _vm.ShowSkipIntro ? Visibility.Visible : Visibility.Collapsed;
        SkipCreditsButton.Visibility = _vm.ShowSkipCredits ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Fullscreen ───────────────────────────────────────────────────────

    private void ToggleFullscreen()
    {
        if (App.MainWindowInstance == null) return;

        var appWindow = App.MainWindowInstance.AppWindow;
        if (appWindow == null) return;

        if (_isFullscreen)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
            _isFullscreen = false;
            FullscreenIcon.Glyph = "\uE740"; // Maximize
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _isFullscreen = true;
            FullscreenIcon.Glyph = "\uE73F"; // Restore
        }
    }

    // ── Mute ─────────────────────────────────────────────────────────────

    private void ToggleMute()
    {
        if (_vm == null || _player == null) return;
        _vm.IsMuted = !_vm.IsMuted;
        _player.SetMute(_vm.IsMuted);
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        if (_vm == null) return;
        if (_vm.IsMuted || _vm.Volume <= 0)
            VolumeIcon.Glyph = "\uE74F"; // Mute
        else if (_vm.Volume < 50)
            VolumeIcon.Glyph = "\uE993"; // Low volume
        else
            VolumeIcon.Glyph = "\uE767"; // Full volume
    }

    // ── Playback info ────────────────────────────────────────────────────

    private void UpdatePlaybackInfo()
    {
        if (_vm == null) return;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_vm.Resolution))
            parts.Add(_vm.Resolution);
        if (!string.IsNullOrEmpty(_vm.PlayMethod))
            parts.Add(_vm.PlayMethod.ToUpperInvariant());

        PlaybackInfoText.Text = string.Join(" \u2022 ", parts);
    }

    // ── Navigation ───────────────────────────────────────────────────────

    private void NavigateBack()
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
    }

    // ── Error display ────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        if (_vm != null)
        {
            _vm.IsLoading = false;
            _vm.ErrorMessage = message;
        }
        LoadingOverlay.Visibility = Visibility.Collapsed;
        ErrorOverlay.Visibility = Visibility.Visible;
        ErrorText.Text = message;
    }
}
