using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ContinuumPlayer.Views;

public sealed partial class PlayerPage : Page
{
    private PlayerViewModel? _vm;
    private MediaPlayer? _mediaPlayer;
    private bool _isDisposed;
    private string? _contentId;
    private bool _isFullscreen;

    // Timers
    private DispatcherTimer? _uiUpdateTimer;
    private DispatcherTimer? _controlsHideTimer;

    // Seek slider drag state
    private bool _isSeeking;

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

        // Dispose media player
        DisposeMediaPlayer();
    }

    private void DisposeMediaPlayer()
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Pause();
            VideoPlayer.SetMediaPlayer(null);
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
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

            // Determine start position from user data
            double startPosition = 0;
            if (watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
                startPosition = watchDetail.UserData.PositionSeconds!.Value;

            // 3. Start session on the server
            var session = await _vm.Manager.StartSessionAsync(bestVersion.FileId, startPosition);
            _vm.PlayMethod = session.PlayMethod;

            // 4. Get the stream URL (auth token already in query param)
            var streamUrl = _vm.Manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ShowError("No stream URL available.");
                return;
            }

            // Log init info for debugging
            LogToFile("player_init.txt",
                $"StreamURL: {streamUrl}\nPlayMethod: {_vm.PlayMethod}\nResolution: {_vm.Resolution}\nSession: {_vm.Manager.SessionId}");

            // 5. Create MediaPlayer and attach to MediaPlayerElement
            _mediaPlayer = new MediaPlayer();

            var mediaSource = MediaSource.CreateFromUri(new Uri(streamUrl));

            // Load external subtitles
            LoadSubtitles(mediaSource);

            _mediaPlayer.Source = mediaSource;
            VideoPlayer.SetMediaPlayer(_mediaPlayer);

            // 6. Subscribe to media player events
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

            // 7. Set title and playback info
            TitleText.Text = _vm.Title;
            UpdatePlaybackInfo();

            // 8. Start UI update timer (position, seek bar, skip markers)
            StartUiUpdateTimer();

            // 9. Start controls auto-hide timer
            StartControlsHideTimer();

            // 10. Auto-play (also set by AutoPlay="True" on the element, but be explicit)
            _mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            ShowError($"Failed to start playback: {ex.Message}");
        }
    }

    // -- Subtitle loading -----------------------------------------------------

    private void LoadSubtitles(MediaSource mediaSource)
    {
        if (_vm?.Manager.CurrentSession == null) return;

        var subtitleUrls = _vm.Manager.GetSubtitleUrls();
        foreach (var (track, fullUrl) in subtitleUrls)
        {
            try
            {
                var codec = track.Codec?.ToLowerInvariant() ?? "";
                // Skip bitmap-based subtitle formats (need burn-in via transcode)
                if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
                    continue;

                var tts = TimedTextSource.CreateFromUri(new Uri(fullUrl));
                tts.Resolved += (s, e) =>
                {
                    if (e.Tracks.Count > 0)
                        e.Tracks[0].Label = track.Label;
                };
                mediaSource.ExternalTimedTextSources.Add(tts);
            }
            catch { }
        }
    }

    // -- Media player event handlers ------------------------------------------

    private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed || _vm == null) return;

            _vm.IsLoading = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;

            // Update duration
            var duration = sender.PlaybackSession.NaturalDuration.TotalSeconds;
            if (duration > 0)
                _vm.UpdateDuration(duration);
        });
    }

    private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            NavigateBack();
        });
    }

    private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            var message = args.ErrorMessage ?? "Unknown playback error";
            LogToFile("player_error.txt", $"MediaFailed: {args.Error} - {message}");
            ShowError($"Playback failed: {message}");
        });
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
        if (_isDisposed || _mediaPlayer == null || _vm == null) return;

        var session = _mediaPlayer.PlaybackSession;
        var position = session.Position.TotalSeconds;
        var duration = session.NaturalDuration.TotalSeconds;

        // Update view model (this also reports progress to server)
        bool isPlaying = session.PlaybackState == MediaPlaybackState.Playing;
        _vm.IsPlaying = isPlaying;
        _vm.UpdatePosition(position);

        if (duration > 0 && Math.Abs(duration - _vm.Duration) > 0.5)
            _vm.UpdateDuration(duration);

        // Update seek slider (only if user is not dragging)
        if (!_isSeeking && duration > 0)
        {
            SeekSlider.Maximum = duration;
            SeekSlider.Value = position;
        }

        // Update time displays
        PositionText.Text = PlayerViewModel.FormatTime(position);
        DurationText.Text = PlayerViewModel.FormatTime(duration);

        // Update play/pause icon
        PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768"; // Pause : Play

        // Update skip button visibility
        SkipIntroButton.Visibility = _vm.ShowSkipIntro ? Visibility.Visible : Visibility.Collapsed;
        SkipCreditsButton.Visibility = _vm.ShowSkipCredits ? Visibility.Visible : Visibility.Collapsed;
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
        if (_mediaPlayer?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
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

    private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
        // Ensure focus for keyboard shortcuts
        this.Focus(FocusState.Programmatic);
    }

    // -- Keyboard shortcuts ---------------------------------------------------

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isDisposed || _mediaPlayer == null) return;

        var session = _mediaPlayer.PlaybackSession;

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
        }

        ShowControls();
    }

    // -- Playback controls ----------------------------------------------------

    private void TogglePlayPause()
    {
        if (_mediaPlayer == null) return;

        if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            _mediaPlayer.Pause();
        else
            _mediaPlayer.Play();
    }

    private void SeekRelative(double seconds)
    {
        if (_mediaPlayer == null) return;

        var session = _mediaPlayer.PlaybackSession;
        var newPosition = session.Position.TotalSeconds + seconds;
        newPosition = Math.Max(0, Math.Min(newPosition, session.NaturalDuration.TotalSeconds));
        session.Position = TimeSpan.FromSeconds(newPosition);
    }

    private void AdjustVolume(double delta)
    {
        if (_mediaPlayer == null) return;

        var newVolume = Math.Max(0, Math.Min(100, VolumeSlider.Value + delta));
        VolumeSlider.Value = newVolume;
        // VolumeSlider_ValueChanged will handle updating _mediaPlayer.Volume
    }

    private void ToggleMute()
    {
        if (_mediaPlayer == null) return;

        _mediaPlayer.IsMuted = !_mediaPlayer.IsMuted;
        UpdateVolumeIcon();
    }

    private void ToggleFullscreen()
    {
        SetFullscreen(!_isFullscreen);
    }

    private void SetFullscreen(bool fullscreen)
    {
        if (App.MainWindowInstance == null) return;

        var appWindow = App.MainWindowInstance.AppWindow;
        if (appWindow == null) return;

        if (fullscreen)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            FullscreenIcon.Glyph = "\uE73F"; // ExitFullScreen
        }
        else
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            FullscreenIcon.Glyph = "\uE740"; // FullScreen
        }

        _isFullscreen = fullscreen;
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
        if (_mediaPlayer == null) return;

        _mediaPlayer.Volume = e.NewValue / 100.0;
        _mediaPlayer.IsMuted = false;
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

    private void GoBackButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateBack();
    }

    private void SkipIntro_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer == null || _vm?.IntroEnd == null) return;
        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(_vm.IntroEnd.Value);
    }

    private void SkipCredits_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer == null || _vm?.CreditsEnd == null) return;
        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(_vm.CreditsEnd.Value);
    }

    // -- Seek slider interaction ----------------------------------------------

    private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isSeeking = true;
    }

    private void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        CommitSeek();
    }

    private void SeekSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        CommitSeek();
    }

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // During drag, show the preview position in the time label
        if (_isSeeking)
        {
            PositionText.Text = PlayerViewModel.FormatTime(e.NewValue);
        }
    }

    private void CommitSeek()
    {
        if (!_isSeeking) return;
        _isSeeking = false;

        if (_mediaPlayer == null) return;
        _mediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(SeekSlider.Value);
    }

    // -- Helpers --------------------------------------------------------------

    private void UpdateVolumeIcon()
    {
        if (_mediaPlayer == null) return;

        if (_mediaPlayer.IsMuted || _mediaPlayer.Volume <= 0)
            VolumeIcon.Glyph = "\uE74F"; // Mute
        else if (_mediaPlayer.Volume < 0.5)
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
        // Stop the media player, then navigate back
        StopTimers();
        DisposeMediaPlayer();
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
