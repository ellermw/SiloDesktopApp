using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Services;

namespace ContinuumPlayer.Controls;

public sealed partial class PlayerOverlay : UserControl
{
    private readonly PlayerService _playerService;

    private bool _statsVisible;
    private bool _isMuted;
    private bool _isActive;

    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _hideTimer;
    private DispatcherTimer? _bufferingDebounceTimer;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _breatheStoryboard;
    private Microsoft.UI.Xaml.Media.ScaleTransform? _breatheTransform;
    private const int BufferingSpinnerDelayMs = 500;

    public PlayerOverlay()
    {
        _playerService = App.Services.GetRequiredService<PlayerService>();
        this.InitializeComponent();
        SeekBar.SeekRequested += (seconds) =>
        {
            try { _playerService.Mpv?.Seek(seconds); } catch { }
            ShowControls();
        };
    }

    // ── Activate / Deactivate (called by MainWindow when visibility toggles) ──

    public void Activate()
    {
        if (_isActive) return;
        _isActive = true;

        // Subscribe to content/playback events (video renders via native GPU window)
        _playerService.ContentLoaded += OnContentLoaded;
        _playerService.PlaybackEnded += OnPlaybackEnded;
        _playerService.StateChanged += OnPlayerStateChanged;
        _playerService.BufferingChanged += OnBufferingChanged;

        // Sync the fullscreen icon eagerly so the first paint after re-activation
        // reflects the current state (otherwise it lingers on the "exit fullscreen"
        // glyph after a Fullscreen → Minimized → Expanded round trip).
        SyncFullscreenIcon();

        // Show loading overlay if still loading
        if (_playerService.IsLoading)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            ErrorOverlay.Visibility = Visibility.Collapsed;
        }
        else
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        // Show error if there is one
        if (_playerService.ErrorMessage != null)
        {
            ErrorOverlay.Visibility = Visibility.Visible;
            ErrorText.Text = _playerService.ErrorMessage;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        // Sync volume/mute from shared state
        VolumeSlider.Value = _playerService.Volume;
        _isMuted = _playerService.IsMuted;
        UpdateVolumeIcon();

        // Update title
        TitleText.Text = _playerService.Title;

        // Populate flyouts
        PopulateQualityFlyout();
        PopulateSubtitleFlyout();
        PopulateAudioFlyout();
        PopulateChaptersFlyout();

        // Update playback info display
        UpdatePlaybackInfo();

        // Cap render size at 1080p
        // Render size is set dynamically in FileLoaded to match video's native resolution

        // Start UI update timer (250ms)
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        // Start controls auto-hide timer (3s)
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += HideTimer_Tick;
        _hideTimer.Start();

        // Focus for keyboard shortcuts
        this.Focus(FocusState.Programmatic);
    }

    public void Deactivate()
    {
        if (!_isActive) return;
        _isActive = false;

        // Unsubscribe from events
        _playerService.ContentLoaded -= OnContentLoaded;
        _playerService.PlaybackEnded -= OnPlaybackEnded;
        _playerService.StateChanged -= OnPlayerStateChanged;
        _playerService.BufferingChanged -= OnBufferingChanged;

        // Stop timers
        _uiTimer?.Stop();
        _uiTimer = null;
        _hideTimer?.Stop();
        _hideTimer = null;
        _bufferingDebounceTimer?.Stop();
        _bufferingDebounceTimer = null;
        BufferingSpinner.Visibility = Visibility.Collapsed;

        // Stop the breathing animation cleanly on deactivate so it doesn't
        // accumulate Storyboards across Activate/Deactivate cycles.
        SetBreatheActive(false);
    }

    /// <summary>
    /// Raised off-UI-thread from mpv's paused-for-cache property observer. We
    /// debounce by 500ms before actually showing the spinner — quick cache
    /// recoveries (common on good networks) never flash chrome.
    /// </summary>
    private void OnBufferingChanged(bool buffering)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isActive) return;
            if (buffering)
            {
                // Start / restart the debounce timer.
                if (_bufferingDebounceTimer == null)
                {
                    _bufferingDebounceTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(BufferingSpinnerDelayMs),
                    };
                    _bufferingDebounceTimer.Tick += (_, _) =>
                    {
                        _bufferingDebounceTimer?.Stop();
                        // Only show if we're STILL buffering when the delay expires.
                        if (_isActive && _playerService.IsBufferingForCache)
                            BufferingSpinner.Visibility = Visibility.Visible;
                    };
                }
                _bufferingDebounceTimer.Stop();
                _bufferingDebounceTimer.Start();
            }
            else
            {
                _bufferingDebounceTimer?.Stop();
                BufferingSpinner.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>
    /// Fires on every PlayerService state transition. Used to keep the fullscreen
    /// icon glyph in sync without waiting for the 250ms UI tick — otherwise the
    /// icon stays in its "exit fullscreen" state after a
    /// Fullscreen → Minimized → Expanded round trip.
    /// </summary>
    private void OnPlayerStateChanged(PlayerState newState)
    {
        // StateChanged may fire on a non-UI thread — marshal to the UI thread
        // before touching XAML.
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (!_isActive) return;
            SyncFullscreenIcon();
        });
    }

    private void SyncFullscreenIcon()
    {
        FullscreenIcon.Glyph = _playerService.State == PlayerState.Fullscreen
            ? "\uE73F"  // BackToWindow — "exit fullscreen"
            : "\uE740"; // FullScreen — "enter fullscreen"
    }

    // ── ContentLoaded / PlaybackEnded handlers ───────────────────────────

    private void OnContentLoaded()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (!_isActive) return;

            LoadingOverlay.Visibility = Visibility.Collapsed;
            ErrorOverlay.Visibility = Visibility.Collapsed;

            // Update title and flyouts now that content is loaded
            TitleText.Text = _playerService.Title;
            UpdatePlaybackInfo();
            PopulateQualityFlyout();
            PopulateSubtitleFlyout();
            PopulateAudioFlyout();
            PopulateChaptersFlyout();
            UpdateEpisodeNav();

            // Seed the seek bar with markers + chapters for the newly loaded item.
            var intro = _playerService.WatchDetail?.Intro;
            SeekBar.IntroMarker = intro != null ? (intro.Start, intro.End) : null;
            var credits = _playerService.WatchDetail?.Credits;
            SeekBar.CreditsMarker = credits != null ? (credits.Start, credits.End) : null;
            var version = _playerService.Versions.FirstOrDefault(v => v.FileId == (_playerService.Manager?.CurrentSession?.MediaFileId ?? 0));
            SeekBar.Chapters = version?.Chapters;
            SeekBar.Invalidate();
        });
    }

    private void OnPlaybackEnded()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (!_isActive) return;
            _playerService.Minimize();
        });
    }

    // Playing Next cinematic overlay (Phase 3b) was moved to MainWindow.xaml
    // + MainWindow.xaml.cs — this UserControl is never activated so the
    // handlers here would never run. See MainWindow.OnShowPlayingNextRequested.

    // B53: mpv now renders directly into its own GPU popup window; the old
    // SW frame pipeline (OnFrameReady → SoftwareBitmap double buffer →
    // VideoFrame Image source) was dead code and has been removed. Nothing
    // calls OnFrameReady anymore — the PlayerService/MpvVideoWindow pairing
    // owns the video output path.

    // ── UI update timer (position, seek bar, play/pause icon, skip markers) ──

    private void UiTimer_Tick(object? sender, object e)
    {
        if (!_isActive || _playerService.Mpv == null) return;

        var pos = _playerService.Mpv.Position;
        var dur = _playerService.Mpv.Duration;

        // Push current position + duration to the custom seek bar; it
        // re-lays-out internally. No suppression flags needed — unlike the
        // Slider it doesn't emit ValueChanged on programmatic updates.
        SeekBar.Duration = dur;
        SeekBar.CurrentTime = pos;
        // Buffered state — mpv exposes demuxer-cache-time as "seconds ahead
        // of current position that are already downloaded".
        try
        {
            var cacheAhead = _playerService.Mpv.GetPropertyDouble("demuxer-cache-time");
            SeekBar.BufferedEnd = pos + (cacheAhead > 0 ? cacheAhead : 0);
        }
        catch { SeekBar.BufferedEnd = pos; }

        // Update time labels
        PositionText.Text = PlayerService.FormatTime(pos);
        DurationText.Text = PlayerService.FormatTime(dur);

        // Update play/pause icon based on actual playback state
        var isPaused = _playerService.Mpv.IsPaused;
        PlayPauseIcon.Glyph = isPaused ? "\uE768" : "\uE769";
        // player-breathe: pulse the primary disc when paused (webui parity).
        SetBreatheActive(isPaused);

        // Update fullscreen icon
        FullscreenIcon.Glyph = _playerService.State == PlayerState.Fullscreen ? "\uE73F" : "\uE740";

        // Report progress to server
        _playerService.Manager?.UpdatePosition(pos, _playerService.Mpv.IsPaused);

        // Slide embedded subtitle windows forward if we're near the tail
        // (webui parity — upstream streams embedded subs in 10min–1hr windows).
        _playerService.TickSubtitleWindows(pos);

        // Check skip markers
        UpdateSkipButtons(pos);

        // Update stats if visible
        if (_statsVisible) UpdateStats();
    }

    private void UpdateSkipButtons(double pos)
    {
        var intro = _playerService.WatchDetail?.Intro;
        bool showIntro = intro != null && pos >= intro.Start && pos < intro.End;
        SkipIntroButton.Visibility = showIntro ? Visibility.Visible : Visibility.Collapsed;

        var credits = _playerService.WatchDetail?.Credits;
        bool showCredits = credits != null && pos >= credits.Start && pos < credits.End;
        SkipCreditsButton.Visibility = showCredits ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Controls auto-hide ───────────────────────────────────────────────

    private void HideTimer_Tick(object? sender, object e)
    {
        _hideTimer?.Stop();

        // Only hide if playing (keep visible when paused)
        if (_playerService.Mpv != null && !_playerService.Mpv.IsPaused)
        {
            ControlsOverlay.Opacity = 0;
            ControlsOverlay.IsHitTestVisible = false;
        }
    }

    private void ShowControls()
    {
        // Only play the reveal animation when transitioning from hidden state.
        bool wasHidden = ControlsOverlay.Opacity < 1;
        ControlsOverlay.Opacity = 1;
        ControlsOverlay.IsHitTestVisible = true;

        if (wasHidden) PlayRiseAnimation();

        // Restart the hide timer
        _hideTimer?.Stop();
        _hideTimer?.Start();
    }

    /// <summary>
    /// player-rise cinematic reveal (webui parity): a subtle 6px slide-up
    /// combined with a fade on the bottom HUD when controls appear. Plays
    /// once per reveal and doesn't loop.
    /// </summary>
    private void PlayRiseAnimation()
    {
        var xform = new Microsoft.UI.Xaml.Media.TranslateTransform();
        ControlsOverlay.RenderTransform = xform;

        var slide = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 6,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slide, xform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slide, "Y");

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(slide);
        sb.Begin();
    }

    private void Overlay_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
    }

    private void Overlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ShowControls();
        // Ensure focus for keyboard shortcuts
        this.Focus(FocusState.Programmatic);
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────

    private void Overlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isActive || _playerService.Mpv == null) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                _playerService.Mpv.TogglePause();
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
                _playerService.ToggleFullscreen();
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Escape:
                if (_playerService.State == PlayerState.Fullscreen)
                    _playerService.ExitFullscreen();
                else
                    _playerService.Minimize();
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

            // K = toggle play/pause (YouTube-style)
            case Windows.System.VirtualKey.K:
                _playerService.Mpv.TogglePause();
                e.Handled = true;
                break;

            // C = toggle captions/subtitles visibility
            case Windows.System.VirtualKey.C:
                _playerService.ToggleSubtitleVisibility();
                e.Handled = true;
                break;

            // P = Picture in Picture (webui parity)
            case Windows.System.VirtualKey.P:
                _playerService.Minimize();
                e.Handled = true;
                break;
        }

        ShowControls();
    }

    // ── Playback controls ────────────────────────────────────────────────

    private void SeekRelative(double seconds)
    {
        if (_playerService.Mpv == null) return;

        var newPos = _playerService.Mpv.Position + seconds;
        newPos = Math.Max(0, Math.Min(newPos, _playerService.Mpv.Duration));
        _playerService.Mpv.Seek(newPos);
    }

    private void AdjustVolume(double delta)
    {
        var newVolume = Math.Max(0, Math.Min(100, VolumeSlider.Value + delta));
        VolumeSlider.Value = newVolume;
    }

    private void ToggleMute()
    {
        if (_playerService.Mpv == null) return;

        _isMuted = !(_playerService.Mpv.GetMute());
        _playerService.Mpv.SetMute(_isMuted);
        _playerService.IsMuted = _isMuted;
        _playerService.SaveVolumeState();
        UpdateVolumeIcon();
    }

    private void ToggleStats()
    {
        _statsVisible = !_statsVisible;

        if (_statsVisible)
        {
            UpdateStats();
            StatsOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            StatsOverlay.Visibility = Visibility.Collapsed;
        }

        // Sync the Info utility button's amber data-active dot (webui parity).
        InfoActiveDot.Visibility = _statsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Button click handlers ────────────────────────────────────────────

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _playerService.Mpv?.TogglePause();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        ToggleMute();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_playerService?.Mpv == null) return;

        _playerService.Mpv.SetVolume(e.NewValue);
        _playerService.Volume = e.NewValue;
        if (_isMuted && e.NewValue > 0)
        {
            _isMuted = false;
            _playerService.Mpv.SetMute(false);
            _playerService.IsMuted = false;
        }
        _playerService.SaveVolumeState();
        UpdateVolumeIcon();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        _playerService.ToggleFullscreen();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        _playerService.Minimize();
    }

    private void ClosePlayer_Click(object sender, RoutedEventArgs e)
    {
        _ = _playerService.CloseAsync();
    }

    private void Stats_Click(object sender, RoutedEventArgs e)
    {
        ToggleStats();
    }

    private void SkipIntro_Click(object sender, RoutedEventArgs e)
    {
        _playerService.Mpv?.Seek(_playerService.WatchDetail?.Intro?.End ?? 0);
    }

    private void SkipCredits_Click(object sender, RoutedEventArgs e)
    {
        _playerService.Mpv?.Seek(_playerService.WatchDetail?.Credits?.End ?? 0);
    }

    // ── New center-cluster skip buttons (webui parity: back 10s, forward 30s) ──

    private const double SkipBackSeconds = 10;
    private const double SkipForwardSeconds = 30;

    private void SkipBack_Click(object sender, RoutedEventArgs e) => SeekRelative(-SkipBackSeconds);
    private void SkipForward_Click(object sender, RoutedEventArgs e) => SeekRelative(SkipForwardSeconds);

    // ── Episode navigation stubs ───────────────────────────────────────────
    // Wire these to PlayerService once prev/next episode context is exposed.
    // Buttons are Collapsed in XAML today; flip visibility + populate via the
    // same path that shows/hides the ClusterSlotSpacer pair (webui parity).

    private void PrevEpisode_Click(object sender, RoutedEventArgs e) { /* TODO #166b */ }
    private async void NextEpisode_Click(object sender, RoutedEventArgs e)
    {
        // Fast-path to the next episode without waiting for the credits
        // countdown — PlayerService reuses the same path used by the
        // PlayingNext auto-continue flow.
        try { await _playerService.ContinuePlayingNextAsync(); }
        catch { /* reported via ErrorMessage elsewhere */ }
    }

    /// <summary>
    /// Show / hide the prev/next episode buttons and their spacers based on
    /// PlayerService's current episode context. Called from OnContentLoaded
    /// and after each state transition. Mirrors webui's showEpisodeSlots
    /// logic — both slots are reserved when ANY episode nav exists so the
    /// play button stays on the cluster centerline.
    /// </summary>
    private void UpdateEpisodeNav()
    {
        var hasNext = !string.IsNullOrEmpty(_playerService.NextEpisodeContentId);
        // Desktop doesn't currently track PrevEpisode on the service; only
        // reserve the slot when next exists (asymmetric but honest).
        bool hasAnyEpisodeSlot = hasNext;

        if (!hasAnyEpisodeSlot)
        {
            PrevEpisodeButton.Visibility = Visibility.Collapsed;
            PrevEpisodeSpacer.Visibility = Visibility.Collapsed;
            NextEpisodeButton.Visibility = Visibility.Collapsed;
            NextEpisodeSpacer.Visibility = Visibility.Collapsed;
            return;
        }

        // Reserve both slots so the cluster stays centered. Only populate
        // the buttons that actually have destinations.
        PrevEpisodeButton.Visibility = Visibility.Collapsed;
        PrevEpisodeSpacer.Visibility = Visibility.Visible;
        NextEpisodeButton.Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
        NextEpisodeSpacer.Visibility = hasNext ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Picture-in-Picture (#155) ──────────────────────────────────────────
    // PiP-lite: collapse the player to the mini bar so video keeps playing
    // at the bottom of the window while the user navigates the main UI. True
    // detached top-most window PiP is tracked as a follow-up.

    private void PipButton_Click(object sender, RoutedEventArgs e)
    {
        _playerService.Minimize();
    }

    // ── player-breathe animation (webui parity: paused play button halo) ──
    //
    // Gentle pulsing scale on the primary play/pause disc when paused so the
    // "tap to resume" affordance reads clearly without distracting during
    // active playback. Scales 1.0 → 1.04 → 1.0 over 2.6s, matching the web's
    // box-shadow pulse cadence.

    private void SetBreatheActive(bool active)
    {
        if (active)
        {
            if (_breatheStoryboard != null) return;

            _breatheTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { CenterX = 28, CenterY = 28 };
            PlayPauseButton.RenderTransform = _breatheTransform;

            var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(2600)),
                RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            };
            anim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
            { KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1.0 });
            anim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
            { KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1560)), Value = 1.04 });
            anim.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
            { KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600)), Value = 1.0 });
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, _breatheTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "ScaleX");

            var animY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(2600)),
                RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            };
            animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
            { KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1.0 });
            animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
            { KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1560)), Value = 1.04 });
            animY.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.LinearDoubleKeyFrame
            { KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600)), Value = 1.0 });
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animY, _breatheTransform);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animY, "ScaleY");

            _breatheStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            _breatheStoryboard.Children.Add(anim);
            _breatheStoryboard.Children.Add(animY);
            _breatheStoryboard.Begin();
        }
        else
        {
            _breatheStoryboard?.Stop();
            _breatheStoryboard = null;
            PlayPauseButton.RenderTransform = null;
            _breatheTransform = null;
        }
    }

    // Seek slider interaction moved into CustomSeekBar — scrub-and-commit
    // semantics preserved (no seek during drag; commit on pointer up). See
    // Controls/CustomSeekBar.xaml.cs for the implementation.

    // ── Quality / version switching ──────────────────────────────────────

    private void PopulateQualityFlyout()
    {
        QualityFlyout.Items.Clear();

        foreach (var version in _playerService.Versions)
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
            if (version.Resolution == _playerService.Resolution)
                item.FontWeight = Microsoft.UI.Text.FontWeights.Bold;

            QualityFlyout.Items.Add(item);
        }
    }

    private void QualityItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is FileVersion version)
        {
            _ = _playerService.SwitchVersionAsync(version);
        }
    }

    // ── Subtitle selection ───────────────────────────────────────────────

    private void PopulateSubtitleFlyout()
    {
        SubtitleFlyout.Items.Clear();

        // "Off" option to disable subtitles. Persists the choice under the
        // series (or content) ID so the next play defaults to off too.
        var offItem = new MenuFlyoutItem { Text = "Off" };
        offItem.Click += (_, _) =>
        {
            _ = _playerService.SetSubtitleTrackAndPersistAsync(0, null);
        };
        SubtitleFlyout.Items.Add(offItem);
        SubtitleFlyout.Items.Add(new MenuFlyoutSeparator());

        // Subtitle tracks from the session, sorted by source priority:
        // external > downloaded > embedded (matches webui).
        var subtitleUrls = _playerService.Manager?.GetSubtitleUrls() ?? [];
        var sortedSubs = subtitleUrls
            .Select((pair, idx) => (pair.Track, pair.FullUrl, OrigIndex: idx))
            .Where(s =>
            {
                var c = s.Track.Codec?.ToLowerInvariant() ?? "";
                return c is not ("pgs" or "pgssub" or "dvdsub" or "vobsub");
            })
            .OrderBy(s => SourceSortKey(s.Track.Source))
            .ToList();

        int mpvTrackIndex = 1;
        // Build a map from original index → mpv track index (accounts for
        // skipped bitmap subs). We need this because we render sorted but
        // mpv's sid is the load order.
        var mpvIndexMap = new Dictionary<int, int>();
        for (int i = 0; i < subtitleUrls.Count; i++)
        {
            var c = subtitleUrls[i].Track.Codec?.ToLowerInvariant() ?? "";
            if (c is "pgs" or "pgssub" or "dvdsub" or "vobsub") continue;
            mpvIndexMap[i] = mpvTrackIndex++;
        }

        foreach (var (track, _, origIdx) in sortedSubs)
        {
            var langName = PlayerService.LanguageCodeToName(track.Language);
            var trackTitle = track.Label;
            if (string.Equals(trackTitle, track.Codec, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trackTitle, "subrip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trackTitle, "ass", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trackTitle, "srt", StringComparison.OrdinalIgnoreCase))
                trackTitle = null;

            var label = langName;
            if (!string.IsNullOrEmpty(trackTitle) && !string.Equals(trackTitle, langName, StringComparison.OrdinalIgnoreCase))
                label += $" \u2014 {trackTitle}";
            if (string.IsNullOrEmpty(label)) label = $"Track {origIdx + 1}";
            if (track.Forced) label += " [Forced]";

            // Source badge: EXTERNAL / DOWNLOADED / EMBEDDED uppercase tag
            var source = track.Source?.ToUpperInvariant();
            if (!string.IsNullOrEmpty(source) && source != "EMBEDDED")
                label += $"  [{source}]";

            var item = new MenuFlyoutItem { Text = label };
            int capturedMpvIndex = mpvIndexMap.GetValueOrDefault(origIdx, 1);
            var capturedTrack = track;
            item.Click += (_, _) => _ = _playerService.SetSubtitleTrackAndPersistAsync(
                capturedMpvIndex, capturedTrack.Language, capturedTrack);
            SubtitleFlyout.Items.Add(item);
        }

        // Appearance… — opens the in-player SubtitleAppearanceDialog (webui
        // parity, commit 1adbcd1). Separator keeps it visually distinct from
        // the track list above.
        SubtitleFlyout.Items.Add(new MenuFlyoutSeparator());
        var appearanceItem = new MenuFlyoutItem
        {
            Text = "Appearance…",
            Icon = new FontIcon { Glyph = "\uE700" }, // GlobalNavButton → sliders approximation
        };
        appearanceItem.Click += async (_, _) =>
        {
            try
            {
                var dlg = new SubtitleAppearanceDialog { XamlRoot = this.XamlRoot };
                await dlg.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SubtitleAppearanceDialog failed: {ex.Message}");
            }
        };
        SubtitleFlyout.Items.Add(appearanceItem);
    }

    private static int SourceSortKey(string? source) => (source?.ToLowerInvariant()) switch
    {
        "external" => 0,
        "downloaded" => 1,
        "embedded" => 2,
        _ => 3,
    };

    // ── Audio track switching ────────────────────────────────────────────

    private void PopulateAudioFlyout()
    {
        AudioFlyout.Items.Clear();

        var currentSession = _playerService.Manager?.CurrentSession;
        if (currentSession == null) return;

        var version = _playerService.Versions.FirstOrDefault(v => v.FileId == currentSession.MediaFileId);
        if (version?.AudioTracks == null || version.AudioTracks.Count == 0) return;

        // Disable the button when only 1 track — there's nothing to switch.
        if (version.AudioTracks.Count == 1)
        {
            AudioButton.IsEnabled = false;
            return;
        }
        AudioButton.IsEnabled = true;

        // Header row matching webui "AUDIO" section header
        AudioFlyout.Items.Add(new MenuFlyoutItem
        {
            Text = "AUDIO",
            IsEnabled = false,
            FontSize = 10,
        });
        AudioFlyout.Items.Add(new MenuFlyoutSeparator());

        for (int i = 0; i < version.AudioTracks.Count; i++)
        {
            var at = version.AudioTracks[i];

            // webui label format: "Language · Layout · CODEC"
            // e.g. "English · 5.1 · TrueHD"
            var langName = PlayerService.LanguageCodeToName(at.Language);
            var parts = new List<string> { langName };

            if (at.Channels.HasValue && at.Channels.Value > 0)
            {
                string chLabel = at.Channels.Value switch
                {
                    1 => "Mono",
                    2 => "Stereo",
                    6 => "5.1",
                    8 => "7.1",
                    _ => $"{at.Channels}ch",
                };
                parts.Add(chLabel);
            }

            if (!string.IsNullOrEmpty(at.Codec))
            {
                // Normalize codec name (reuse the same mapper from the version flyout)
                var normalized = at.Codec.ToUpperInvariant() switch
                {
                    "TRUEHD" => "TrueHD",
                    "DTSHDMA" or "DTS-HD MA" => "DTS-HD MA",
                    "EAC3" => "E-AC3",
                    "AC3" => "AC3",
                    "AAC" => "AAC",
                    "FLAC" => "FLAC",
                    "OPUS" => "Opus",
                    _ => at.Codec,
                };
                parts.Add(normalized);
            }

            var label = string.Join(" \u00B7 ", parts);
            if (at.Default) label += " \u2605";

            var item = new MenuFlyoutItem { Text = label };
            if (i == currentSession.AudioTrackIndex)
                item.FontWeight = Microsoft.UI.Text.FontWeights.Bold;

            int trackIndex = i;
            item.Click += (_, _) => _ = _playerService.SwitchAudioTrackAsync(trackIndex);
            AudioFlyout.Items.Add(item);
        }
    }

    // ── Chapters menu (Phase 3a) ─────────────────────────────────────────

    /// <summary>
    /// Populate the chapters flyout from the currently-playing FileVersion's
    /// chapters list. Each row has an optional thumbnail, the chapter title,
    /// and the timestamp. Click → mpv seeks to the chapter start.
    /// Mirrors the WebUI ChaptersMenu.tsx layout.
    /// </summary>
    private void PopulateChaptersFlyout()
    {
        ChaptersListPanel.Children.Clear();

        var currentSession = _playerService.Manager?.CurrentSession;
        if (currentSession == null)
        {
            ChaptersButton.Visibility = Visibility.Collapsed;
            return;
        }

        var version = _playerService.Versions.FirstOrDefault(v => v.FileId == currentSession.MediaFileId);
        var chapters = version?.Chapters;
        if (chapters == null || chapters.Count == 0)
        {
            ChaptersButton.Visibility = Visibility.Collapsed;
            return;
        }

        ChaptersButton.Visibility = Visibility.Visible;

        // Header row
        ChaptersListPanel.Children.Add(new TextBlock
        {
            Text = "CHAPTERS",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 80,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gainsboro) { Opacity = 0.55 },
            Margin = new Thickness(12, 6, 12, 6),
        });

        foreach (var chapter in chapters)
        {
            ChaptersListPanel.Children.Add(BuildChapterRow(chapter));
        }
    }

    private Button BuildChapterRow(Core.Models.Playback.VersionChapter chapter)
    {
        var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Thumbnail (80×48 aspect-preserving, placeholder border while null)
        var thumbBorder = new Border
        {
            Width = 80,
            Height = 48,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.06 },
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(chapter.ThumbnailUrl))
        {
            _ = LoadChapterThumbnailAsync(thumbBorder, chapter);
        }
        else
        {
            thumbBorder.Child = new FontIcon
            {
                Glyph = "\uE714", // "Film" placeholder
                FontSize = 16,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.25 },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        Grid.SetColumn(thumbBorder, 0);
        row.Children.Add(thumbBorder);

        // Title + timestamp stack
        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        textStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(chapter.Title) ? $"Chapter {chapter.Index + 1}" : chapter.Title,
            FontSize = 13,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        textStack.Children.Add(new TextBlock
        {
            Text = FormatChapterTime(chapter.StartSeconds),
            FontSize = 11,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.45 },
        });
        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        var btn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            Content = row,
        };
        btn.Click += (_, _) =>
        {
            try
            {
                _playerService.Mpv?.Seek(chapter.StartSeconds);
                ChaptersFlyout.Hide();
            }
            catch { }
        };
        return btn;
    }

    private async Task LoadChapterThumbnailAsync(Border container, Core.Models.Playback.VersionChapter chapter)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<Core.Services.ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var key = $"chapter_{chapter.Index}";
            var bytes = await imageService.GetImageAsync(key, "chapter", chapter.ThumbnailUrl!, httpClient, CancellationToken.None);
            if (bytes == null) return;

            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
            {
                DecodePixelWidth = 160,
                DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical,
            };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

            DispatcherQueue.TryEnqueue(() =>
            {
                container.Child = new Image
                {
                    Source = bitmap,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                };
            });
        }
        catch { /* Thumbnail is cosmetic */ }
    }

    private static string FormatChapterTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    // ── Stats overlay ────────────────────────────────────────────────────

    private void UpdateStats()
    {
        var currentSession = _playerService.Manager?.CurrentSession;
        var version = _playerService.Versions.FirstOrDefault(v => v.FileId == currentSession?.MediaFileId)
                   ?? _playerService.Versions.FirstOrDefault();

        StatsResolution.Text = $"Resolution:  {_playerService.Resolution}";
        StatsCodec.Text = $"Video:       {currentSession?.PlaybackInfo?.VideoCodec ?? version?.CodecVideo ?? "?"}\nAudio:       {currentSession?.PlaybackInfo?.AudioCodec ?? version?.CodecAudio ?? "?"}";
        StatsPlayMethod.Text = $"Play Method: {_playerService.PlayMethod}";
        StatsBitrate.Text = $"Bitrate:     {(version?.Bitrate > 0 ? $"{version.Bitrate / 1000.0:F1} Mbps" : "?")}";

        var hdr = "SDR";
        if (version is { Hdr: true })
        {
            var codec = (version.CodecVideo ?? "").ToLowerInvariant();
            hdr = codec.Contains("dovi") || codec.Contains("dolby") ? "Dolby Vision" : "HDR10";
        }
        StatsHdr.Text = $"HDR:         {hdr}";
        // Live bandwidth from mpv cache speed
        double bw = 0;
        try { bw = _playerService.Mpv?.GetPropertyDouble("cache-speed") ?? 0; } catch { }
        StatsPosition.Text = $"Bandwidth:   {(bw > 0 ? $"{bw / 1_000_000:F1} Mbps" : "N/A")}";
        StatsSession.Text = $"Session:     {_playerService.Manager?.SessionId ?? "?"}";
    }

    // ── Helpers ──────────────────────────────────────────────────────────

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
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_playerService.Resolution))
            parts.Add(_playerService.Resolution);
        if (!string.IsNullOrEmpty(_playerService.PlayMethod))
            parts.Add(_playerService.PlayMethod.ToUpperInvariant());

        PlaybackInfoText.Text = string.Join(" \u2022 ", parts);
    }
}
