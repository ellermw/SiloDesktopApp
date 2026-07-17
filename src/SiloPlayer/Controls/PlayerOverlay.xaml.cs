using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

public sealed partial class PlayerOverlay : UserControl
{
    private readonly PlayerService _playerService;
    private readonly SettingsApi _settingsApi;
    private readonly AuthService _authService;

    private bool _statsVisible;
    private bool _isMuted;
    private bool _isActive;
    private bool _autoSkipIntro;
    private bool _autoSkipCredits;
    private bool _autoSkipRecap;
    private bool _introAutoSkipped;
    private bool _creditsAutoSkipped;
    private bool _recapAutoSkipped;
    private int _autoSkipSettingsLoadVersion;
    private DateTimeOffset? _sleepDeadline;
    private double? _sleepAtPosition;
    private double _playbackSpeed = 1;
    private int _subtitleDelayMs;

    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _hideTimer;
    private DispatcherTimer? _bufferingDebounceTimer;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _breatheStoryboard;
    private Microsoft.UI.Xaml.Media.ScaleTransform? _breatheTransform;
    private const int BufferingSpinnerDelayMs = 500;
    private const string AutoSkipIntroSettingKey = "playback.auto_skip_intro";
    private const string AutoSkipCreditsSettingKey = "playback.auto_skip_credits";
    private const string AutoSkipRecapSettingKey = "playback.auto_skip_recap";

    public PlayerOverlay()
    {
        _playerService = App.Services.GetRequiredService<PlayerService>();
        _settingsApi = App.Services.GetRequiredService<SettingsApi>();
        _authService = App.Services.GetRequiredService<AuthService>();
        this.InitializeComponent();
        SeekBar.SeekRequested += (seconds) =>
        {
            SeekInteractive(seconds);
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
        _playerService.MarkersChanged += OnMarkersChanged;

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
        MarkerEditButton.Visibility = CanEditMarkers() ? Visibility.Visible : Visibility.Collapsed;

        // Populate flyouts
        PopulateQualityFlyout();
        PopulateSubtitleFlyout();
        PopulateAudioFlyout();
        PopulateChaptersFlyout();
        PopulateSpeedFlyout();
        PopulateSleepFlyout();

        ResetAutoSkipMarkerState();
        _ = RefreshAutoSkipSettingsAsync();

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
        _playerService.MarkersChanged -= OnMarkersChanged;

        // Stop timers
        _uiTimer?.Stop();
        _uiTimer = null;
        _hideTimer?.Stop();
        _hideTimer = null;
        _bufferingDebounceTimer?.Stop();
        _bufferingDebounceTimer = null;
        BufferingSpinner.Visibility = Visibility.Collapsed;
        System.Threading.Interlocked.Increment(ref _autoSkipSettingsLoadVersion);

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
            ResetAutoSkipMarkerState();
            _ = RefreshAutoSkipSettingsAsync();

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
            RefreshMarkerRegions();
            var version = _playerService.Versions.FirstOrDefault(v => v.FileId == (_playerService.Manager?.CurrentSession?.MediaFileId ?? 0));
            SeekBar.Chapters = version?.Chapters;
            SeekBar.Invalidate();
        });
    }

    private void ResetAutoSkipMarkerState()
    {
        _introAutoSkipped = false;
        _creditsAutoSkipped = false;
        _recapAutoSkipped = false;
    }

    private async Task RefreshAutoSkipSettingsAsync()
    {
        var loadVersion = System.Threading.Interlocked.Increment(ref _autoSkipSettingsLoadVersion);
        var profileId = _authService.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            ApplyAutoSkipSettings(loadVersion, intro: false, credits: false, recap: false);
            return;
        }

        try
        {
            var profiles = await _settingsApi.GetProfilesAsync();
            var profile = profiles.Profiles.FirstOrDefault(p => p.Id == profileId);
            var intro = profile?.AutoSkipIntro == true;
            var credits = profile?.AutoSkipCredits == true;
            var recap = profile?.AutoSkipRecap == true;

            try
            {
                var effective = await _settingsApi.GetEffectiveSettingsAsync(
                    [AutoSkipIntroSettingKey, AutoSkipCreditsSettingKey, AutoSkipRecapSettingKey]);
                intro = ResolveEffectiveBool(effective, AutoSkipIntroSettingKey, intro);
                credits = ResolveEffectiveBool(effective, AutoSkipCreditsSettingKey, credits);
                recap = ResolveEffectiveBool(effective, AutoSkipRecapSettingKey, recap);
            }
            catch
            {
                // Older servers may not expose effective device settings yet.
                // Profile-level settings still enable the feature.
            }

            ApplyAutoSkipSettings(loadVersion, intro, credits, recap);
        }
        catch
        {
            ApplyAutoSkipSettings(loadVersion, intro: false, credits: false, recap: false);
        }
    }

    private void ApplyAutoSkipSettings(int loadVersion, bool intro, bool credits, bool recap)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (!_isActive || loadVersion != _autoSkipSettingsLoadVersion) return;
            _autoSkipIntro = intro;
            _autoSkipCredits = credits;
            _autoSkipRecap = recap;
        });
    }

    private static bool ResolveEffectiveBool(EffectiveSettingsResponse effective, string key, bool fallback)
    {
        var setting = effective.Settings.FirstOrDefault(s => s.Key == key);
        if (setting?.HasDeviceOverride != true) return fallback;

        var raw = setting.EffectiveValue?.Trim();
        if (string.IsNullOrEmpty(raw)) return fallback;
        if (bool.TryParse(raw, out var parsed)) return parsed;
        if (int.TryParse(raw, out var numeric)) return numeric != 0;
        return fallback;
    }

    private void OnMarkersChanged()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (!_isActive) return;
            RefreshMarkerRegions();
            SeekBar.Invalidate();
            UpdateSkipButtons(_playerService.Position);
        });
    }

    private void RefreshMarkerRegions()
    {
        var intro = _playerService.ActiveIntro;
        SeekBar.IntroMarker = intro != null ? (intro.Start, intro.End) : null;
        var credits = _playerService.ActiveCredits;
        SeekBar.CreditsMarker = credits != null ? (credits.Start, credits.End) : null;
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

        var pos = _playerService.Position;
        var dur = _playerService.Duration;

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

        // Reconcile from PlayerService's authoritative state. The event-driven
        // update is immediate; this periodic pass also repairs any missed XAML
        // repaint after display-mode or window activation changes.
        SyncFullscreenIcon();

        // Report progress to server
        _playerService.Manager?.UpdatePosition(pos, _playerService.Mpv.IsPaused);

        // Slide embedded subtitle windows forward if we're near the tail
        // (webui parity — upstream streams embedded subs in 10min–1hr windows).
        _playerService.TickSubtitleWindows(pos);

        // Check skip markers
        ApplyAutoSkipMarkers(pos, dur);
        UpdateSkipButtons(pos);
        UpdateEpisodeNav(pos, dur);
        CheckSleepTimer(pos);

        // Update stats if visible
        if (_statsVisible) UpdateStats();
    }

    private void ApplyAutoSkipMarkers(double pos, double dur)
    {
        var intro = _playerService.ActiveIntro;
        if (_autoSkipIntro
            && !_introAutoSkipped
            && IsWithinMarker(intro, pos)
            && intro!.End > 0)
        {
            _introAutoSkipped = true;
            SeekAndResume(intro.End);
            return;
        }

        var recap = _playerService.ActiveRecap;
        if (_autoSkipRecap
            && !_recapAutoSkipped
            && IsWithinMarker(recap, pos)
            && recap!.End > 0)
        {
            _recapAutoSkipped = true;
            SeekAndResume(recap.End);
            return;
        }

        var credits = _playerService.ActiveCredits;
        if (!_autoSkipCredits
            || _creditsAutoSkipped
            || credits == null
            || !IsPlausibleCreditsMarker(credits, dur)
            || !IsWithinMarker(credits, pos))
        {
            return;
        }

        _creditsAutoSkipped = true;
        if (dur > 0)
            SeekAndResume(Math.Min(credits.End, dur));
        else
            SeekAndResume(credits.End);
    }

    private static bool IsWithinMarker(TimeRange? marker, double position)
        => marker != null
            && marker.End > marker.Start
            && position >= marker.Start
            && position < marker.End;

    private void UpdateSkipButtons(double pos)
    {
        var intro = _playerService.ActiveIntro;
        bool showIntro = intro != null && pos >= intro.Start && pos < intro.End;
        SkipIntroButton.Visibility = showIntro ? Visibility.Visible : Visibility.Collapsed;

        var credits = _playerService.ActiveCredits;
        bool hasNextEpisode = !string.IsNullOrEmpty(_playerService.NextEpisodeContentId);
        var dur = _playerService.Duration;
        bool showCredits = !hasNextEpisode
            && credits != null
            && IsPlausibleCreditsMarker(credits, dur)
            && pos >= credits.Start
            && pos < credits.End;
        SkipCreditsButton.Visibility = showCredits ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsPlausibleCreditsMarker(TimeRange credits, double duration)
    {
        if (duration <= 0) return false;
        if (credits.End <= credits.Start) return false;
        if (credits.Start < 0 || credits.Start >= duration) return false;

        // Ignore credits markers that start far from the tail. A few server
        // auto-detections land 10+ minutes early, which would otherwise make
        // Skip Credits / Next Episode appear while the episode is still going.
        var allowedWindow = Math.Max(TimeSpan.FromMinutes(5).TotalSeconds, duration * 0.10);
        return duration - credits.Start <= allowedWindow;
    }

    private bool ShouldShowNextEpisodeButton(double pos, double duration)
    {
        if (string.IsNullOrEmpty(_playerService.NextEpisodeContentId)) return false;
        if (pos <= 0 || duration <= 0) return false;

        var credits = _playerService.ActiveCredits;
        var inPlausibleCredits = credits != null
            && IsPlausibleCreditsMarker(credits, duration)
            && pos >= credits.Start
            && pos < credits.End;
        var nearEnd = pos >= duration * 0.95;

        return inPlausibleCredits || nearEnd;
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

        var newPos = _playerService.Position + seconds;
        newPos = Math.Max(0, Math.Min(newPos, _playerService.Duration));
        SeekInteractive(newPos);
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
        SeekAndResume(_playerService.ActiveIntro?.End ?? 0);
    }

    private void SkipCredits_Click(object sender, RoutedEventArgs e)
    {
        var end = _playerService.ActiveCredits?.End ?? 0;
        var dur = _playerService.Duration;
        if (dur > 0) end = Math.Min(end, dur);
        SeekAndResume(end);
    }

    private void SeekAndResume(double seconds)
    {
        SeekInteractive(seconds, forceResume: true);
    }

    private void SeekInteractive(double seconds, bool forceResume = false)
    {
        var mpv = _playerService.Mpv;
        if (mpv == null) return;

        var wasPaused = mpv.IsPaused;
        _playerService.SeekFastTo(seconds, forceResume);
        if (forceResume || !wasPaused)
        {
            mpv.Play();
            _ = ForceResumeAfterSeekAsync();
        }
    }

    private async Task ForceResumeAfterSeekAsync()
    {
        foreach (var delayMs in new[] { 100, 350, 750 })
        {
            await Task.Delay(delayMs);
            try { _playerService.Mpv?.Play(); } catch { }
        }
    }

    // ── New center-cluster skip buttons (webui parity: back 10s, forward 30s) ──

    private const double SkipBackSeconds = 10;
    private const double SkipForwardSeconds = 30;

    private void SkipBack_Click(object sender, RoutedEventArgs e) => SeekRelative(-SkipBackSeconds);
    private void SkipForward_Click(object sender, RoutedEventArgs e) => SeekRelative(SkipForwardSeconds);

    private async void PrevEpisode_Click(object sender, RoutedEventArgs e)
    {
        try { await _playerService.PlayPreviousEpisodeAsync(); }
        catch { }
    }

    // ── Episode navigation ─────────────────────────────────────────────────
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
    private void UpdateEpisodeNav(double pos = 0, double duration = 0)
    {
        var hasPrevious = !string.IsNullOrEmpty(_playerService.PreviousEpisodeContentId);
        var hasNext = !string.IsNullOrEmpty(_playerService.NextEpisodeContentId);
        bool hasAnyEpisodeSlot = hasPrevious || hasNext;

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
        PrevEpisodeButton.Visibility = hasPrevious ? Visibility.Visible : Visibility.Collapsed;
        PrevEpisodeSpacer.Visibility = hasPrevious ? Visibility.Collapsed : Visibility.Visible;
        NextEpisodeButton.Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
        NextEpisodeSpacer.Visibility = hasNext ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Picture-in-Picture (#155) ──────────────────────────────────────────
    private void PipButton_Click(object sender, RoutedEventArgs e)
    {
        _playerService.EnterPictureInPicture();
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
            .OrderBy(s => SourceSortKey(s.Track.Source))
            .ToList();

        // Build a map from original index → mpv track index (accounts for
        // skipped bitmap subs). We need this because we render sorted but
        // mpv's sid is the load order.

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
            if (track.HearingImpaired && !label.Contains("SDH", StringComparison.OrdinalIgnoreCase)) label += " (SDH)";
            if (track.Forced && !label.Contains("Forced", StringComparison.OrdinalIgnoreCase)) label += " (Forced)";
            var format = SubtitleFormatLabel(track.Codec);
            if (!string.IsNullOrEmpty(format)) label += $" · {format}";

            // Source badge: EXTERNAL / DOWNLOADED / EMBEDDED uppercase tag
            var source = track.Source?.ToUpperInvariant();
            if (!string.IsNullOrEmpty(source) && source != "EMBEDDED")
                label += $"  [{source}]";

            var item = new MenuFlyoutItem { Text = label };
            int capturedMpvIndex = origIdx + 1;
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

        var addItem = new MenuFlyoutItem
        {
            Text = "Add subtitles...",
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        addItem.Click += async (_, _) =>
        {
            var session = _playerService.Manager?.CurrentSession;
            if (session == null) return;
            var dialog = new SubtitleSearchDialog(session.MediaFileId, subtitleUrls.FirstOrDefault().Track?.Language)
            {
                XamlRoot = XamlRoot
            };
            dialog.SubtitleDownloaded += async () =>
            {
                var version = _playerService.Versions.FirstOrDefault(v => v.FileId == session.MediaFileId);
                if (version != null) await _playerService.SwitchVersionAsync(version);
            };
            await dialog.ShowAsync();
        };
        SubtitleFlyout.Items.Add(addItem);

        var aiItem = new MenuFlyoutItem
        {
            Text = "Translate or generate with AI...",
            Icon = new FontIcon { Glyph = "\uE945" }
        };
        aiItem.Click += async (_, _) => await ShowSubtitleAiDialogAsync();
        SubtitleFlyout.Items.Add(aiItem);

        SubtitleFlyout.Items.Add(new MenuFlyoutSeparator());
        var earlier = new MenuFlyoutItem { Text = $"Subtitle delay: 100 ms earlier ({_subtitleDelayMs:+#;-#;0} ms)" };
        earlier.Click += (_, _) => SetSubtitleDelay(Math.Max(-10_000, _subtitleDelayMs - 100));
        SubtitleFlyout.Items.Add(earlier);
        var later = new MenuFlyoutItem { Text = $"Subtitle delay: 100 ms later ({_subtitleDelayMs:+#;-#;0} ms)" };
        later.Click += (_, _) => SetSubtitleDelay(Math.Min(10_000, _subtitleDelayMs + 100));
        SubtitleFlyout.Items.Add(later);
        var resetDelay = new MenuFlyoutItem { Text = "Reset subtitle delay", IsEnabled = _subtitleDelayMs != 0 };
        resetDelay.Click += (_, _) => SetSubtitleDelay(0);
        SubtitleFlyout.Items.Add(resetDelay);
    }

    private void SetSubtitleDelay(int milliseconds)
    {
        _subtitleDelayMs = milliseconds;
        _playerService.Mpv?.SetProperty("sub-delay", (milliseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        PopulateSubtitleFlyout();
    }

    public async Task ShowSubtitleAiDialogAsync()
    {
        var session = _playerService.Manager?.CurrentSession;
        if (session == null) return;
        var api = App.Services.GetRequiredService<PlaybackApi>();
        SubtitleAiStatus capability;
        try { capability = await api.GetSubtitleAiStatusAsync(); }
        catch (Exception ex) { await ShowPlayerDialogAsync("AI subtitles unavailable", ex.Message); return; }
        if (!capability.Enabled && !capability.TranscribeEnabled)
        {
            await ShowPlayerDialogAsync("AI subtitles unavailable", "This Silo server has not enabled subtitle translation or transcription.");
            return;
        }

        var version = _playerService.Versions.FirstOrDefault(v => v.FileId == session.MediaFileId);
        var subtitleTracks = (_playerService.Manager?.GetSubtitleUrls() ?? [])
            .Select(pair => pair.Track)
            .Where(track => track.Source == "embedded" ? !IsUnsupportedBitmapSubtitle(track.Codec) : track.Codec?.ToLowerInvariant() is "srt" or "subrip" or "vtt" or "webvtt")
            .ToList();
        var audioTracks = version?.AudioTracks ?? [];
        var mode = new ComboBox { Header = "Source", HorizontalAlignment = HorizontalAlignment.Stretch };
        if (capability.Enabled && subtitleTracks.Count > 0) mode.Items.Add(new ComboBoxItem { Content = "Translate subtitles", Tag = "subtitles" });
        if (capability.TranscribeEnabled && audioTracks.Count > 0) mode.Items.Add(new ComboBoxItem { Content = "Generate from audio", Tag = "audio" });
        if (mode.Items.Count == 0) { await ShowPlayerDialogAsync("No compatible source", "Add a text subtitle track first, or ask the server administrator to enable audio transcription."); return; }
        mode.SelectedIndex = 0;

        var source = new ComboBox { Header = "Track", HorizontalAlignment = HorizontalAlignment.Stretch };
        var target = new ComboBox { Header = "Language", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var language in MediaLanguageCatalog.All)
            target.Items.Add(new ComboBoxItem { Content = language.Label, Tag = language.Code });
        target.SelectedIndex = target.Items.Cast<ComboBoxItem>()
            .Select((item, index) => (item, index))
            .FirstOrDefault(pair => string.Equals(pair.item.Tag?.ToString(), "en", StringComparison.Ordinal)).index;
        void RebuildSource()
        {
            source.Items.Clear();
            if ((mode.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "audio")
            {
                for (var i = 0; i < audioTracks.Count; i++)
                    source.Items.Add(new ComboBoxItem { Content = $"{PlayerService.LanguageCodeToName(audioTracks[i].Language)} - {audioTracks[i].Title ?? audioTracks[i].Codec}", Tag = i });
            }
            else
            {
                foreach (var track in subtitleTracks)
                    source.Items.Add(new ComboBoxItem { Content = $"{PlayerService.LanguageCodeToName(track.Language)} - {track.Label}", Tag = track });
            }
            if (source.Items.Count > 0) source.SelectedIndex = 0;
        }
        mode.SelectionChanged += (_, _) => RebuildSource();
        RebuildSource();
        SubtitleAiQuota? quota = null;
        if (capability.TranscribeEnabled)
        {
            try { quota = await api.GetSubtitleAiQuotaAsync(); } catch { }
        }
        var quotaText = new TextBlock
        {
            FontSize = 11,
            Foreground = quota?.Limited == true && quota.Remaining <= 0
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(90, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = quota?.Limited == true ? Visibility.Visible : Visibility.Collapsed,
            Text = quota?.Limited == true
                ? quota.Remaining <= 0
                    ? $"You've used all {quota.Limit} transcriptions for the {FormatQuotaPeriod(quota.Period)}. Try again later."
                    : $"{quota.Remaining} of {quota.Limit} transcriptions left for the {FormatQuotaPeriod(quota.Period)}."
                : ""
        };
        var helpText = new TextBlock
        {
            FontSize = 11,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(90, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap
        };
        void UpdateHelpText()
        {
            var fromAudio = (mode.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "audio";
            helpText.Text = fromAudio
                ? "The audio is transcribed on the server (and translated if the language differs) — longer files take a while. The finished track is saved for everyone."
                : "Playback pauses while the first lines are translated, then resumes with subtitles streaming in. The finished track is saved for everyone.";
        }
        UpdateHelpText();
        var form = new StackPanel { Spacing = 12, MinWidth = 390, Children = { mode, source, target, quotaText, helpText } };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = (mode.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "audio"
                ? "Generate subtitles with AI"
                : "Translate subtitles with AI",
            Content = form,
            PrimaryButtonText = (mode.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "audio" ? "Generate" : "Translate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        void UpdateDialogMode()
        {
            var fromAudio = (mode.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "audio";
            dialog.Title = fromAudio ? "Generate subtitles with AI" : "Translate subtitles with AI";
            dialog.PrimaryButtonText = fromAudio ? "Generate" : "Translate";
            dialog.IsPrimaryButtonEnabled = !fromAudio || quota?.Limited != true || quota.Remaining > 0;
            UpdateHelpText();
        }
        mode.SelectionChanged += (_, _) => UpdateDialogMode();
        UpdateDialogMode();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var selectedMode = (mode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "subtitles";
        var targetLanguage = (target.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
        var request = new SubtitleAiRequest
        {
            MediaFileId = session.MediaFileId,
            TargetLanguage = targetLanguage,
            SessionId = session.SessionId,
            StartPosition = _playerService.Position
        };
        if (selectedMode == "audio")
        {
            var index = (source.SelectedItem as ComboBoxItem)?.Tag is int selectedIndex ? selectedIndex : 0;
            var language = audioTracks.ElementAtOrDefault(index)?.Language ?? "";
            request.Kind = string.Equals(language, targetLanguage, StringComparison.OrdinalIgnoreCase) ? "transcribe" : "transcribe_translate";
            request.SourceIndex = index;
            request.SourceLanguage = language;
            if (request.Kind == "transcribe") request.TargetLanguage = "";
        }
        else if ((source.SelectedItem as ComboBoxItem)?.Tag is SubtitleTrackInfo track)
        {
            request.SourceIndex = track.Index;
            request.SourceLanguage = track.Language;
        }
        try
        {
            var started = await api.StartSubtitleAiAsync(request);
            var liveLanguage = string.IsNullOrWhiteSpace(request.TargetLanguage)
                ? request.SourceLanguage
                : request.TargetLanguage;
            if (string.Equals(started.Job.Status, "running", StringComparison.OrdinalIgnoreCase))
            {
                App.Services.GetService<ToastService>()?.Info(
                    "A job for this track is already in progress — it'll appear when it's ready.");
            }
            else
            {
                _playerService.PrepareLiveSubtitleTranslation(
                    started.Job.Id,
                    session.MediaFileId,
                    liveLanguage,
                    $"{PlayerService.LanguageCodeToName(liveLanguage)} AI");
            }
            _ = MonitorSubtitleAiJobAsync(api, started.Job.Id, session.MediaFileId);
        }
        catch (Exception ex) { await ShowPlayerDialogAsync("Could not start AI subtitles", ex.Message); }
    }

    private static string FormatQuotaPeriod(string period) => period switch
    {
        "hour" or "hourly" => "last hour",
        "day" or "daily" => "last day",
        "week" or "weekly" => "last week",
        "month" or "monthly" => "last month",
        _ => string.IsNullOrWhiteSpace(period) ? "current period" : period
    };

    private async Task MonitorSubtitleAiJobAsync(PlaybackApi api, long jobId, int mediaFileId)
    {
        if (jobId <= 0) return;
        for (var attempt = 0; attempt < 3600; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            SubtitleAiJob job;
            try { job = await api.GetSubtitleAiJobAsync(jobId); }
            catch { continue; }
            if (job.Status is "failed" or "cancelled")
            {
                DispatcherQueue.TryEnqueue(() =>
                    _playerService.FailLiveSubtitleTranslation(jobId, job.ErrorMessage));
                return;
            }
            if (job.Status != "completed") continue;
            DispatcherQueue.TryEnqueue(() => _ = _playerService.RefreshSubtitlesAfterAiAsync(
                mediaFileId, job.ResultSubtitleId));
            return;
        }
    }

    private async Task ShowPlayerDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = message, CloseButtonText = "Close" };
        await dialog.ShowAsync();
    }

    private bool CanEditMarkers()
        => AuthorizationPolicy.CanEditMarkers(_authService);

    private async void MarkerEdit_Click(object sender, RoutedEventArgs e)
        => await ShowMarkerEditDialogAsync();

    public async Task ShowMarkerEditDialogAsync()
    {
        var session = _playerService.Manager?.CurrentSession;
        if (session == null || !CanEditMarkers()) return;

        var rows = new[]
        {
            CreateMarkerEditRow("intro", "Intro", "#38BDF8", _playerService.ActiveIntro),
            CreateMarkerEditRow("recap", "Recap", "#A78BFA", _playerService.ActiveRecap),
            CreateMarkerEditRow("credits", "Credits / Outro", "#FBBF24", _playerService.ActiveCredits),
            CreateMarkerEditRow("preview", "Preview", "#34D399", _playerService.ActivePreview)
        };

        var segmentPanel = new StackPanel { Spacing = 4 };
        foreach (var row in rows) segmentPanel.Children.Add(row.Element);

        var currentTimeText = new TextBlock
        {
            Text = FormatChapterTime(_playerService.Position),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var resetAll = new Button
        {
            Content = "↶  Reset all",
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed
        };
        var footer = new Grid { Margin = new Thickness(4, 4, 4, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(resetAll, 1);
        footer.Children.Add(currentTimeText);
        footer.Children.Add(resetAll);

        var panel = new StackPanel { Spacing = 8, MinWidth = 352, MaxWidth = 352 };
        panel.Children.Add(new TextBlock
        {
            Text = "Drag the timeline handles, or set points to the playhead.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(4, 0, 4, 2)
        });
        panel.Children.Add(segmentPanel);
        panel.Children.Add(footer);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Edit markers",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };

        MarkerEditRow activeRow = rows[0];
        void SelectRow(MarkerEditRow selected)
        {
            activeRow = selected;
            foreach (var row in rows)
            {
                var active = ReferenceEquals(row, selected);
                row.Actions.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                row.Element.Background = new SolidColorBrush(active
                    ? Windows.UI.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)
                    : Windows.UI.Color.FromArgb(0, 0, 0, 0));
                row.Element.BorderThickness = active ? new Thickness(1) : new Thickness(0);
            }
        }

        void RefreshDirtyState()
        {
            foreach (var row in rows) row.Refresh();
            var dirty = rows.Any(row => !RangesEqual(row.Original, row.CurrentRange));
            dialog.IsPrimaryButtonEnabled = dirty;
            resetAll.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var row in rows)
        {
            row.HeaderButton.Click += (_, _) => SelectRow(row);
            row.SetStartButton.Click += (_, _) =>
            {
                row.SetStart(_playerService.Position, _playerService.Duration);
                RefreshDirtyState();
            };
            row.SetEndButton.Click += (_, _) =>
            {
                row.SetEnd(_playerService.Position, _playerService.Duration);
                RefreshDirtyState();
            };
            row.ResetButton.Click += (_, _) =>
            {
                row.SetRange(row.Original);
                RefreshDirtyState();
            };
            row.ClearButton.Click += (_, _) =>
            {
                row.SetRange(null);
                RefreshDirtyState();
            };
        }
        resetAll.Click += (_, _) =>
        {
            foreach (var row in rows) row.SetRange(row.Original);
            RefreshDirtyState();
        };
        SelectRow(activeRow);
        RefreshDirtyState();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => currentTimeText.Text = FormatChapterTime(_playerService.Position);
        timer.Start();
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            timer.Stop();
        }
        if (result != ContentDialogResult.Primary) return;

        var changes = new Dictionary<string, object?>();
        foreach (var row in rows)
        {
            var next = row.CurrentRange;
            if (!RangesEqual(row.Original, next))
                changes[row.Kind] = next == null ? null : new { start = next.Start, end = next.End };
            row.Result = next;
        }
        if (changes.Count == 0) return;

        try
        {
            await App.Services.GetRequiredService<PlaybackApi>().SetFileMarkersAsync(session.MediaFileId, changes);
            _playerService.ApplyMarkerEdits(rows[0].Result, rows[1].Result, rows[2].Result, rows[3].Result);
        }
        catch (Exception ex)
        {
            var error = new ContentDialog { XamlRoot = XamlRoot, Title = "Could not save markers", Content = ex.Message, CloseButtonText = "Close" };
            await error.ShowAsync();
        }
    }

    private MarkerEditRow CreateMarkerEditRow(string kind, string label, string colorHex, TimeRange? original)
    {
        var color = (Windows.UI.Color)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Windows.UI.Color), colorHex);
        var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(color) };
        var title = new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xDC, 0xFF, 0xFF, 0xFF)) };
        var rangeText = new TextBlock { FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Right };
        var headerGrid = new Grid { ColumnSpacing = 10 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(dot, 0); Grid.SetColumn(title, 1); Grid.SetColumn(rangeText, 2);
        headerGrid.Children.Add(dot); headerGrid.Children.Add(title); headerGrid.Children.Add(rangeText);
        var header = new Button { Content = headerGrid, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)), BorderThickness = new Thickness(0), Padding = new Thickness(2, 3, 2, 3), HorizontalContentAlignment = HorizontalAlignment.Stretch };

        Button EdgeButton(string text) => new()
        {
            Content = text,
            FontSize = 11,
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF))
        };
        var setStart = EdgeButton("Set start");
        var setEnd = EdgeButton("Set end");
        var reset = EdgeButton("↶");
        ToolTipService.SetToolTip(reset, "Reset to saved");
        var clear = EdgeButton("⌫");
        ToolTipService.SetToolTip(clear, "Clear marker");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(21, 5, 0, 2), Children = { setStart, setEnd, reset, clear }, Visibility = Visibility.Collapsed };
        var body = new StackPanel { Spacing = 0, Children = { header, actions } };
        var border = new Border { Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(12), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), Child = body };
        return new MarkerEditRow(kind, original, rangeText, header, actions, setStart, setEnd, reset, clear, border);
    }

    private static bool RangesEqual(TimeRange? left, TimeRange? right)
        => left == null || right == null
            ? left == null && right == null
            : Math.Abs(left.Start - right.Start) < 0.001 && Math.Abs(left.End - right.End) < 0.001;

    private static int SourceSortKey(string? source) => (source?.ToLowerInvariant()) switch
    {
        "external" => 0,
        "downloaded" => 1,
        "embedded" => 2,
        _ => 3,
    };

    private static string? SubtitleFormatLabel(string? codec) => codec?.ToLowerInvariant() switch
    {
        "ass" or "ssa" => "ASS",
        "srt" or "subrip" => "SRT",
        "vtt" or "webvtt" => "VTT",
        "pgs" or "hdmv_pgs_subtitle" => "PGS",
        "dvdsub" or "dvd_subtitle" or "vobsub" => "DVD",
        "dvbsub" or "dvb_subtitle" => "DVB",
        _ => null,
    };

    // ── Audio track switching ────────────────────────────────────────────

    private static bool IsUnsupportedBitmapSubtitle(string? codec)
    {
        var normalized = codec?.ToLowerInvariant() ?? "";
        return normalized is "dvdsub" or "dvd_subtitle" or "vobsub" or "dvbsub" or "dvb_subtitle";
    }

    private void PopulateSpeedFlyout()
    {
        SpeedFlyout.Items.Clear();
        foreach (var rate in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var item = new MenuFlyoutItem
            {
                Text = $"{rate:0.##}×",
                Tag = rate,
                FontWeight = Math.Abs(rate - _playbackSpeed) < 0.001
                    ? Microsoft.UI.Text.FontWeights.Bold
                    : Microsoft.UI.Text.FontWeights.Normal
            };
            item.Click += (_, _) =>
            {
                _playbackSpeed = rate;
                _playerService.Mpv?.SetProperty("speed", rate.ToString(CultureInfo.InvariantCulture));
                SpeedButtonText.Text = $"{rate:0.##}×";
                PopulateSpeedFlyout();
            };
            SpeedFlyout.Items.Add(item);
        }
    }

    private void PopulateSleepFlyout()
    {
        SleepFlyout.Items.Clear();
        if (_sleepDeadline != null || _sleepAtPosition != null)
        {
            var off = new MenuFlyoutItem { Text = "Turn off" };
            off.Click += (_, _) => { _sleepDeadline = null; _sleepAtPosition = null; PopulateSleepFlyout(); };
            SleepFlyout.Items.Add(off);
            SleepFlyout.Items.Add(new MenuFlyoutSeparator());
        }
        foreach (var (label, seconds) in new[] { ("5 min", 300), ("15 min", 900), ("30 min", 1800), ("45 min", 2700), ("60 min", 3600) })
        {
            var item = new MenuFlyoutItem { Text = label, Tag = seconds };
            item.Click += (_, _) =>
            {
                _sleepAtPosition = null;
                _sleepDeadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
                PopulateSleepFlyout();
            };
            SleepFlyout.Items.Add(item);
        }
        var chapterItem = new MenuFlyoutItem { Text = "End of chapter" };
        chapterItem.Click += (_, _) =>
        {
            var current = _playerService.Position;
            var version = _playerService.Versions.FirstOrDefault(v => v.FileId == (_playerService.Manager?.CurrentSession?.MediaFileId ?? 0));
            var nextEnd = version?.Chapters?.Where(c => c.EndSeconds > current + 1).OrderBy(c => c.EndSeconds).FirstOrDefault()?.EndSeconds;
            _sleepDeadline = null;
            _sleepAtPosition = nextEnd ?? _playerService.Duration;
            PopulateSleepFlyout();
        };
        SleepFlyout.Items.Add(chapterItem);
    }

    private void CheckSleepTimer(double position)
    {
        var expired = _sleepDeadline is { } deadline && DateTimeOffset.UtcNow >= deadline;
        expired |= _sleepAtPosition is { } target && target > 0 && position >= target - 0.25;
        if (!expired) return;
        _sleepDeadline = null;
        _sleepAtPosition = null;
        PopulateSleepFlyout();
        _ = _playerService.CloseAsync();
    }

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
                SeekInteractive(chapter.StartSeconds);
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

    private sealed class MarkerEditRow
    {
        private double? _start;
        private double? _end;

        public MarkerEditRow(
            string kind,
            TimeRange? original,
            TextBlock rangeText,
            Button headerButton,
            StackPanel actions,
            Button setStartButton,
            Button setEndButton,
            Button resetButton,
            Button clearButton,
            Border element)
        {
            Kind = kind;
            Original = Clone(original);
            _start = original?.Start;
            _end = original?.End;
            RangeText = rangeText;
            HeaderButton = headerButton;
            Actions = actions;
            SetStartButton = setStartButton;
            SetEndButton = setEndButton;
            ResetButton = resetButton;
            ClearButton = clearButton;
            Element = element;
            Refresh();
        }

        public string Kind { get; }
        public TimeRange? Original { get; }
        public TextBlock RangeText { get; }
        public Button HeaderButton { get; }
        public StackPanel Actions { get; }
        public Button SetStartButton { get; }
        public Button SetEndButton { get; }
        public Button ResetButton { get; }
        public Button ClearButton { get; }
        public Border Element { get; }
        public TimeRange? Result { get; set; }

        public TimeRange? CurrentRange => _start.HasValue && _end.HasValue
            ? new TimeRange { Start = _start.Value, End = _end.Value }
            : null;

        public void SetRange(TimeRange? range)
        {
            _start = range?.Start;
            _end = range?.End;
            Refresh();
        }

        public void SetStart(double seconds, double duration)
        {
            _start = Math.Max(0, seconds);
            if (!_end.HasValue || _end <= _start)
                _end = Math.Min(Math.Max(0, duration), _start.Value + 60);
            Refresh();
        }

        public void SetEnd(double seconds, double duration)
        {
            _end = Math.Min(Math.Max(0, duration), Math.Max(0, seconds));
            if (!_start.HasValue || _start >= _end)
                _start = Math.Max(0, _end.Value - 60);
            Refresh();
        }

        public void Refresh()
        {
            RangeText.Text = CurrentRange is { } range
                ? $"{FormatChapterTime(range.Start)} – {FormatChapterTime(range.End)}"
                : "Not set";
            var dirty = !RangesEqual(Original, CurrentRange);
            ResetButton.IsEnabled = dirty;
            ClearButton.IsEnabled = CurrentRange != null;
        }

        private static TimeRange? Clone(TimeRange? range)
            => range == null ? null : new TimeRange { Start = range.Start, End = range.End };
    }
}
