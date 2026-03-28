using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;
using ContinuumPlayer.Player;

namespace ContinuumPlayer.Views;

public sealed partial class PlayerPage : Page
{
    private PlayerViewModel? _vm;
    private MpvPlayer? _player;
    private bool _isDisposed;
    private string? _contentId;
    private bool _isFullscreen;
    private bool _isMuted;

    // WriteableBitmap for software-rendered frames
    private WriteableBitmap? _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;

    // Timers
    private DispatcherTimer? _uiUpdateTimer;
    private DispatcherTimer? _controlsHideTimer;

    // Seek slider suppression: when true, ValueChanged from the timer update is ignored
    private bool _suppressSeek;

    // Track whether subtitles have been loaded (only after FileLoaded)
    private bool _subtitlesLoaded;

    // Resume position from server
    private double _resumePosition;

    public PlayerPage()
    {
        this.InitializeComponent();
    }

    // -- Page lifecycle -------------------------------------------------------

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_contentId))
        {
            ShowError("No content specified for playback.");
            return;
        }

        // Focus the page so keyboard shortcuts work immediately
        this.Focus(FocusState.Programmatic);

        try
        {
            await InitializePlaybackAsync(_contentId);
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            ShowError($"Player error: {ex.Message}");
        }
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

        StopTimers();

        // Exit fullscreen if active
        if (_isFullscreen)
            SetFullscreen(false);

        // Show nav again
        if (App.MainWindowInstance is MainWindow mw)
            mw.ShowMainNavigation();

        // Stop playback session on the server
        if (_vm?.Manager != null)
        {
            try { await _vm.Manager.StopSessionAsync(); }
            catch { }
        }

        // Dispose mpv player
        DisposeMpvPlayer();
    }

    private void DisposeMpvPlayer()
    {
        if (_player != null)
        {
            _player.PositionChanged -= OnMpvPositionChanged;
            _player.DurationChanged -= OnMpvDurationChanged;
            _player.PauseChanged -= OnMpvPauseChanged;
            _player.FileLoaded -= OnMpvFileLoaded;
            _player.PlaybackEnded -= OnMpvPlaybackEnded;
            _player.Error -= OnMpvError;
            _player.FrameReady -= OnFrameReady;
            _player.Dispose();
            _player = null;
        }
    }

    // -- Playback initialization ----------------------------------------------

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

            // Determine start position from user data (resume support)
            double startPosition = 0;
            if (watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
                startPosition = watchDetail.UserData.PositionSeconds!.Value;

            _resumePosition = startPosition;

            // 3. Start session on the server
            var session = await _vm.Manager.StartSessionAsync(bestVersion.FileId, startPosition);
            _vm.PlayMethod = session.PlayMethod;

            // If the server returned a position (from saved progress), use that
            if (session.Position > 0 && _resumePosition == 0)
                _resumePosition = session.Position;

            // 4. Get the stream URL
            var streamUrl = _vm.Manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ShowError("No stream URL available.");
                return;
            }

            // Log init info for debugging
            LogToFile("player_init.txt",
                $"StreamURL: {streamUrl}\nPlayMethod: {_vm.PlayMethod}\nResolution: {_vm.Resolution}\nSession: {_vm.Manager.SessionId}\nResumePos: {_resumePosition}");

            // 5. Initialize mpv player with software render API
            _player = new MpvPlayer();

            // Subscribe to mpv events before initialization
            _player.PositionChanged += OnMpvPositionChanged;
            _player.DurationChanged += OnMpvDurationChanged;
            _player.PauseChanged += OnMpvPauseChanged;
            _player.FileLoaded += OnMpvFileLoaded;
            _player.PlaybackEnded += OnMpvPlaybackEnded;
            _player.Error += OnMpvError;
            _player.FrameReady += OnFrameReady;

            // Get initial render size from VideoImage (in physical pixels)
            var scale = GetDpiScale();
            int width = (int)(VideoImage.ActualWidth * scale);
            int height = (int)(VideoImage.ActualHeight * scale);
            if (width <= 0) width = 1280;
            if (height <= 0) height = 720;

            _player.Initialize(width, height);

            // Create the WriteableBitmap at the render size
            CreateBitmap(width, height);

            // 6. Set title and playback info
            TitleText.Text = _vm.Title;
            UpdatePlaybackInfo();
            PopulateQualityFlyout();

            // 7. Start UI update timer
            StartUiUpdateTimer();

            // 8. Start controls auto-hide timer
            StartControlsHideTimer();

            // 9. Load the stream
            _player.LoadFile(streamUrl);
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            ShowError($"Failed to start playback: {ex.Message}");
        }
    }

    // -- WriteableBitmap management -------------------------------------------

    private void CreateBitmap(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _bitmapWidth = width;
        _bitmapHeight = height;
        _bitmap = new WriteableBitmap(width, height);
        VideoImage.Source = _bitmap;
    }

    private void OnFrameReady(byte[] buffer, int width, int height, int stride)
    {
        // Called from the render thread -- dispatch bitmap update to UI thread
        DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            if (_isDisposed || _bitmap == null) return;

            // If render size changed, recreate the bitmap
            if (width != _bitmapWidth || height != _bitmapHeight)
            {
                CreateBitmap(width, height);
                if (_bitmap == null) return;
            }

            try
            {
                // Copy the frame buffer into the WriteableBitmap's pixel buffer
                using (var stream = _bitmap.PixelBuffer.AsStream())
                {
                    stream.Position = 0;
                    int bytesToWrite = Math.Min(buffer.Length, stride * height);
                    stream.Write(buffer, 0, bytesToWrite);
                }
                _bitmap.Invalidate();
            }
            catch
            {
                // Non-fatal -- skip this frame
            }
        });
    }

    // -- Mpv event handlers (called from background thread) -------------------

    private void OnMpvPositionChanged(double position)
    {
        // Marshal to UI thread
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed || _vm == null) return;
            _vm.UpdatePosition(position);
        });
    }

    private void OnMpvDurationChanged(double duration)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed || _vm == null) return;
            _vm.UpdateDuration(duration);
        });
    }

    private void OnMpvPauseChanged(bool paused)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed || _vm == null) return;
            _vm.IsPlaying = !paused;
            PlayPauseIcon.Glyph = paused ? "\uE768" : "\uE769"; // Play : Pause
        });
    }

    private void OnMpvFileLoaded()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed || _vm == null) return;

            _vm.IsLoading = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;

            // Resume to saved position if needed
            if (_resumePosition > 0 && _player != null)
            {
                _player.Seek(_resumePosition);
                _resumePosition = 0; // Only seek once
            }

            // Load subtitles after file is loaded (avoids sub-add errors during init)
            if (!_subtitlesLoaded)
            {
                _subtitlesLoaded = true;
                LoadSubtitles();
            }
        });
    }

    private void OnMpvPlaybackEnded()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            NavigateBack();
        });
    }

    private void OnMpvError(string errorMessage)
    {
        // Filter out non-fatal subtitle errors
        if (errorMessage.Contains("sub-add", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("subtitle", StringComparison.OrdinalIgnoreCase))
        {
            LogToFile("player_subtitle_error.txt", errorMessage);
            return;
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            LogToFile("player_error.txt", errorMessage);
        });
    }

    // -- Subtitle loading -----------------------------------------------------

    private void LoadSubtitles()
    {
        if (_player == null || _vm?.Manager.CurrentSession == null) return;

        var subtitleUrls = _vm.Manager.GetSubtitleUrls();
        foreach (var (track, fullUrl) in subtitleUrls)
        {
            try
            {
                var codec = track.Codec?.ToLowerInvariant() ?? "";
                // Skip bitmap-based subtitle formats (need burn-in via transcode)
                if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
                    continue;

                _player.AddSubtitle(fullUrl, track.Label, track.Language);
            }
            catch { }
        }
    }

    // -- Video display sizing -------------------------------------------------

    private void VideoImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRenderSize();
    }

    private void UpdateRenderSize()
    {
        if (_player == null || _isDisposed) return;

        try
        {
            var scale = GetDpiScale();
            int w = (int)(VideoImage.ActualWidth * scale);
            int h = (int)(VideoImage.ActualHeight * scale);
            if (w > 0 && h > 0)
                _player.UpdateRenderSize(w, h);
        }
        catch (Exception ex)
        {
            LogToFile("player_resize_error.txt", ex.ToString());
        }
    }

    private double GetDpiScale()
    {
        try
        {
            return XamlRoot?.RasterizationScale ?? 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    // -- UI update timer (position, seek bar, skip markers) -------------------

    private void StartUiUpdateTimer()
    {
        _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
        _uiUpdateTimer.Start();
    }

    private void UiUpdateTimer_Tick(object? sender, object e)
    {
        if (_isDisposed || _player == null || _vm == null) return;

        var position = _player.Position;
        var duration = _player.Duration;

        // Update seek slider (suppress seek event while we update programmatically)
        if (duration > 0)
        {
            _suppressSeek = true;
            SeekSlider.Maximum = duration;
            SeekSlider.Value = position;
            _suppressSeek = false;
        }

        // Update time displays
        PositionText.Text = PlayerViewModel.FormatTime(position);
        DurationText.Text = PlayerViewModel.FormatTime(duration);

        // Update skip button visibility
        SkipIntroButton.Visibility = _vm.ShowSkipIntro ? Visibility.Visible : Visibility.Collapsed;
        SkipCreditsButton.Visibility = _vm.ShowSkipCredits ? Visibility.Visible : Visibility.Collapsed;

        // Update stats overlay if visible
        if (StatsOverlay.Visibility == Visibility.Visible)
            UpdateStatsOverlay();
    }

    // -- Controls auto-hide ---------------------------------------------------

    private void StartControlsHideTimer()
    {
        _controlsHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _controlsHideTimer.Tick += ControlsHideTimer_Tick;
        _controlsHideTimer.Start();
    }

    private void ControlsHideTimer_Tick(object? sender, object e)
    {
        _controlsHideTimer?.Stop();

        // Only hide if playing (keep visible when paused)
        if (_player != null && !_player.IsPaused)
        {
            ControlsOverlay.Opacity = 0;
            ControlsOverlay.IsHitTestVisible = false;
        }
    }

    private void ShowControls()
    {
        ControlsOverlay.Opacity = 1;
        ControlsOverlay.IsHitTestVisible = true;

        // Restart the hide timer
        _controlsHideTimer?.Stop();
        _controlsHideTimer?.Start();
    }

    private void Page_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
    }

    private void Page_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ShowControls();
        // Ensure focus for keyboard shortcuts
        this.Focus(FocusState.Programmatic);
    }

    // -- Keyboard shortcuts ---------------------------------------------------

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isDisposed || _player == null) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                TogglePlayPause();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Left:
                SeekRelative(-10);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Right:
                SeekRelative(10);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Up:
                AdjustVolume(5);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Down:
                AdjustVolume(-5);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.F:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Escape:
                if (_isFullscreen)
                    SetFullscreen(false);
                else
                    ExitPlayer();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.M:
                ToggleMute();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.I:
                ToggleStats();
                e.Handled = true;
                break;
        }

        ShowControls();
    }

    // -- Playback controls ----------------------------------------------------

    private void TogglePlayPause()
    {
        _player?.TogglePause();
    }

    private void SeekRelative(double seconds)
    {
        if (_player == null) return;

        var newPosition = _player.Position + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, _player.Duration));
        _player.Seek(newPosition);
    }

    private void AdjustVolume(double delta)
    {
        var newVolume = Math.Max(0, Math.Min(100, VolumeSlider.Value + delta));
        VolumeSlider.Value = newVolume;
    }

    private void ToggleMute()
    {
        if (_player == null) return;

        _isMuted = !_isMuted;
        _player.SetMute(_isMuted);
        UpdateVolumeIcon();
    }

    private void ToggleFullscreen()
    {
        SetFullscreen(!_isFullscreen);
    }

    private void SetFullscreen(bool fs)
    {
        var appWindow = App.MainWindowInstance?.AppWindow;
        if (appWindow == null) return;

        if (fs)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            if (App.MainWindowInstance is MainWindow mw)
                mw.HideMainNavigation();
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
        }

        _isFullscreen = fs;
        FullscreenIcon.Glyph = fs ? "\uE73F" : "\uE740";

        // Update render size after fullscreen layout change
        DispatcherQueue.TryEnqueue(() => UpdateRenderSize());
    }

    private void ToggleStats()
    {
        if (StatsOverlay.Visibility == Visibility.Collapsed)
        {
            UpdateStatsOverlay();
            StatsOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            StatsOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // -- Button click handlers ------------------------------------------------

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        ToggleMute();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_player == null) return;

        _player.SetVolume(e.NewValue);
        if (_isMuted && e.NewValue > 0)
        {
            _isMuted = false;
            _player.SetMute(false);
        }
        UpdateVolumeIcon();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        ExitPlayer();
    }

    private void GoBack_Click(object sender, RoutedEventArgs e)
    {
        NavigateBack();
    }

    private void Stats_Click(object sender, RoutedEventArgs e)
    {
        ToggleStats();
    }

    private void SkipIntro_Click(object sender, RoutedEventArgs e)
    {
        if (_player == null || _vm?.IntroEnd == null) return;
        _player.Seek(_vm.IntroEnd.Value);
    }

    private void SkipCredits_Click(object sender, RoutedEventArgs e)
    {
        if (_player == null || _vm?.CreditsEnd == null) return;
        _player.Seek(_vm.CreditsEnd.Value);
    }

    // -- Seek slider interaction ----------------------------------------------

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // If the change came from our programmatic update, ignore it
        if (_suppressSeek) return;

        // User is dragging or clicked the slider -- seek to the new position
        if (_player != null && _player.Duration > 0)
        {
            _player.Seek(e.NewValue);
        }
    }

    // -- Quality / version switching ------------------------------------------

    private void PopulateQualityFlyout()
    {
        QualityFlyout.Items.Clear();
        if (_vm == null) return;

        foreach (var version in _vm.Versions)
        {
            var label = $"{version.Resolution}";
            if (version.Hdr) label += " HDR";
            label += $" ({version.CodecVideo.ToUpperInvariant()})";

            var item = new MenuFlyoutItem
            {
                Text = label,
                Tag = version
            };
            item.Click += QualityItem_Click;

            // Mark current version
            if (version.Resolution == _vm.Resolution)
                item.FontWeight = Microsoft.UI.Text.FontWeights.Bold;

            QualityFlyout.Items.Add(item);
        }
    }

    private async void QualityItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is FileVersion version)
        {
            await SwitchVersionAsync(version);
        }
    }

    private async Task SwitchVersionAsync(FileVersion version)
    {
        if (_player == null || _vm == null) return;

        var currentPosition = _player.Position;

        _player.Pause();
        await _vm.Manager.StopSessionAsync();

        try
        {
            var session = await _vm.Manager.StartSessionAsync(version.FileId, currentPosition);
            _vm.PlayMethod = session.PlayMethod;
            _vm.Resolution = version.Resolution;
            UpdatePlaybackInfo();
            PopulateQualityFlyout();

            var streamUrl = _vm.Manager.StreamUrl;
            if (!string.IsNullOrEmpty(streamUrl))
            {
                _subtitlesLoaded = false;
                _resumePosition = currentPosition;
                _player.LoadFile(streamUrl);
            }
        }
        catch (Exception ex)
        {
            LogToFile("player_quality_switch_error.txt", ex.ToString());
            ShowError($"Failed to switch quality: {ex.Message}");
        }
    }

    // -- Stats overlay --------------------------------------------------------

    private void UpdateStatsOverlay()
    {
        if (_vm == null) return;

        var version = _vm.Versions.FirstOrDefault(v => v.Resolution == _vm.Resolution);

        StatsResolution.Text = $"Resolution: {_vm.Resolution}";
        StatsCodec.Text = $"Codec: {version?.CodecVideo ?? "?"} / {version?.CodecAudio ?? "?"}";
        StatsPlayMethod.Text = $"Play Method: {_vm.PlayMethod}";
        StatsBitrate.Text = $"Bitrate: {(version?.Bitrate > 0 ? $"{version.Bitrate / 1000.0:F1} Mbps" : "?")}";
        StatsBuffer.Text = $"Position: {PlayerViewModel.FormatTime(_player?.Position ?? 0)} / {PlayerViewModel.FormatTime(_player?.Duration ?? 0)}";
        StatsSession.Text = $"Session: {_vm.Manager.SessionId ?? "?"}";
    }

    // -- Helpers --------------------------------------------------------------

    private void UpdateVolumeIcon()
    {
        if (_isMuted || VolumeSlider.Value <= 0)
            VolumeIcon.Glyph = "\uE74F"; // Mute
        else if (VolumeSlider.Value < 50)
            VolumeIcon.Glyph = "\uE993"; // Volume1
        else
            VolumeIcon.Glyph = "\uE767"; // Volume3
    }

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

    private void ExitPlayer()
    {
        StopTimers();
        DisposeMpvPlayer();
        NavigateBack();
    }

    private void NavigateBack()
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
    }

    private void StopTimers()
    {
        _uiUpdateTimer?.Stop();
        _uiUpdateTimer = null;
        _controlsHideTimer?.Stop();
        _controlsHideTimer = null;
    }

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

    private static void LogToFile(string fileName, string content)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, $"{DateTime.Now}\n{content}\n");
        }
        catch { }
    }
}
