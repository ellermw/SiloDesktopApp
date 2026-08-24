using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
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
    private SubtitleAiStatus? _subtitleAiStatus;
    // Treat pre-capability-probe servers as enabled for backwards
    // compatibility. A current server can explicitly disable the entry point.
    private SubtitleProviderStatus _subtitleProviderStatus = new() { Enabled = true };
    private int _lastStatsUpdateSecond = -1;
    private MarkerEditRow[]? _markerEditRows;
    private MarkerEditRow? _activeMarkerEditRow;
    private bool _markerSaveInProgress;
    private bool _markerPanelDragging;
    private Windows.Foundation.Point _markerPanelDragStart;
    private double _markerPanelDragOriginX;
    private double _markerPanelDragOriginY;

    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _hideTimer;
    private DispatcherTimer? _bufferingDebounceTimer;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _breatheStoryboard;
    private Microsoft.UI.Xaml.Media.ScaleTransform? _breatheTransform;
    private const int BufferingSpinnerDelayMs = 500;
    private const string AutoSkipIntroSettingKey = "playback.auto_skip_intro";
    private const string AutoSkipCreditsSettingKey = "playback.auto_skip_credits";
    private const string AutoSkipRecapSettingKey = "playback.auto_skip_recap";
    private static readonly SolidColorBrush PlayerMenuTextBrush = new(Windows.UI.Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush PlayerMenuMutedBrush = new(Windows.UI.Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush PlayerMenuActiveBrush = new(Windows.UI.Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA));
    private static readonly SolidColorBrush PlayerMenuHoverBrush = new(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

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
        SeekBar.MarkerEdgeChanged += MarkerEdgeChanged;
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
        _playerService.ChaptersChanged += OnChaptersChanged;

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
        _ = RefreshSubtitleAiCapabilityAsync();
        _ = RefreshSubtitleProviderCapabilityAsync();
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
        _playerService.ChaptersChanged -= OnChaptersChanged;

        // Stop timers
        _uiTimer?.Stop();
        _uiTimer = null;
        _hideTimer?.Stop();
        _hideTimer = null;
        _bufferingDebounceTimer?.Stop();
        _bufferingDebounceTimer = null;
        BufferingSpinner.Visibility = Visibility.Collapsed;
        CloseMarkerEditor();
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
        var isFullscreen = _playerService.State == PlayerState.Fullscreen;
        FullscreenIcon.Glyph = isFullscreen
            ? "\uE73F"  // BackToWindow — "exit fullscreen"
            : "\uE740"; // FullScreen — "enter fullscreen"
        var label = isFullscreen ? "Exit fullscreen" : "Enter fullscreen";
        AutomationProperties.SetName(FullscreenButton, label);
        ToolTipService.SetToolTip(FullscreenButton, $"{label} (F)");
    }

    // ── ContentLoaded / PlaybackEnded handlers ───────────────────────────

    private void OnContentLoaded()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (!_isActive) return;
            if (MarkerEditorPanel.Visibility == Visibility.Visible)
                CloseMarkerEditor();
            ResetAutoSkipMarkerState();
            _ = RefreshAutoSkipSettingsAsync();

            LoadingOverlay.Visibility = Visibility.Collapsed;
            ErrorOverlay.Visibility = Visibility.Collapsed;

            // Update title and flyouts now that content is loaded
            TitleText.Text = _playerService.Title;
            UpdatePlaybackInfo();
            PopulateQualityFlyout();
            PopulateSubtitleFlyout();
            _ = RefreshSubtitleAiCapabilityAsync();
            _ = RefreshSubtitleProviderCapabilityAsync();
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

    private void OnChaptersChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isActive) return;

            var version = _playerService.ActiveVersion;
            SeekBar.Chapters = version?.Chapters;
            SeekBar.Invalidate();

            // Rebuild while open so a generated image replaces its placeholder
            // immediately, just as the current WebUI reacts to the realtime
            // chapter_thumbnail_ready event.
            if (ChaptersFlyout.IsOpen)
                PopulateChaptersFlyout();
        });
    }

    private void RefreshMarkerRegions()
    {
        var intro = _playerService.ActiveIntro;
        SeekBar.IntroMarker = intro != null ? (intro.Start, intro.End) : null;
        var recap = _playerService.ActiveRecap;
        SeekBar.RecapMarker = recap != null ? (recap.Start, recap.End) : null;
        var credits = _playerService.ActiveCredits;
        SeekBar.CreditsMarker = credits != null ? (credits.Start, credits.End) : null;
        var preview = _playerService.ActivePreview;
        SeekBar.PreviewMarker = preview != null ? (preview.Start, preview.End) : null;
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
        var playPauseLabel = isPaused ? "Play" : "Pause";
        AutomationProperties.SetName(PlayPauseButton, playPauseLabel);
        ToolTipService.SetToolTip(PlayPauseButton, playPauseLabel);
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

        if (MarkerEditorPanel.Visibility == Visibility.Visible)
            MarkerEditorCurrentTime.Text = FormatChapterTime(pos);

        // Playback information is intentionally sampled once per second, like
        // the WebUI overlay. Rebuilding the diagnostic rows on every 250 ms UI
        // tick adds needless layout work while video is playing.
        var statsSecond = (int)Math.Max(0, pos);
        if (_statsVisible && statsSecond != _lastStatsUpdateSecond)
        {
            _lastStatsUpdateSecond = statsSecond;
            UpdateStats();
        }
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
        bool hasNextEpisode = _playerService.HasNextEpisodeForCurrentPlayback;
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
        if (!_playerService.HasNextEpisodeForCurrentPlayback) return false;
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

        // Keep the HUD and timeline available while editing markers. Hiding
        // them would strand the panel and its seek-bar handles.
        if (MarkerEditorPanel.Visibility == Visibility.Visible) return;

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
                if (MarkerEditorPanel.Visibility == Visibility.Visible)
                    CloseMarkerEditor();
                else if (_playerService.State == PlayerState.Fullscreen)
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
            _lastStatsUpdateSecond = (int)Math.Max(0, _playerService.Position);
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

    private void StatsClose_Click(object sender, RoutedEventArgs e)
    {
        _statsVisible = false;
        _lastStatsUpdateSecond = -1;
        StatsOverlay.Visibility = Visibility.Collapsed;
        InfoActiveDot.Visibility = Visibility.Collapsed;
        InfoButton.Focus(FocusState.Programmatic);
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
        var hasPrevious = _playerService.HasEpisodeNavigationForCurrentPlayback &&
            !string.IsNullOrEmpty(_playerService.PreviousEpisodeContentId);
        var hasNext = _playerService.HasNextEpisodeForCurrentPlayback;
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

    private static TextBlock CreatePlayerMenuHeader(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        CharacterSpacing = 80,
        Foreground = PlayerMenuMutedBrush,
        Margin = new Thickness(12, 3, 12, 4),
    };

    private static Border CreatePlayerMenuDivider() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
        Margin = new Thickness(0, 5, 0, 5),
    };

    private static Button CreatePlayerMenuButton(string text, bool active = false)
        => CreatePlayerMenuButton(new TextBlock
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        }, active);

    private static Button CreatePlayerMenuButton(UIElement content, bool active = false)
    {
        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = active ? PlayerMenuHoverBrush : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            Foreground = active ? PlayerMenuActiveBrush : PlayerMenuTextBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(12, 8, 12, 8),
            MinHeight = 36,
        };
        if (content is TextBlock label)
            AutomationProperties.SetName(button, label.Text);
        return button;
    }

    private static Border CreatePlayerMenuBadge(string text, bool outline = false)
    {
        var badge = new Border
        {
            Background = outline
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(outline ? (byte)0x40 : (byte)0x00, 0xFF, 0xFF, 0xFF)),
            BorderThickness = outline ? new Thickness(1) : new Thickness(0),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 9,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = PlayerMenuMutedBrush,
        };
        return badge;
    }

    private static TextBlock CreateSelectionCheck(bool selected) => new()
    {
        Text = selected ? "✓" : "",
        Width = 16,
        FontSize = 13,
        Foreground = selected ? PlayerMenuActiveBrush : PlayerMenuTextBrush,
        HorizontalTextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private static Grid CreateSelectionLabel(string label, bool selected)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(CreateSelectionCheck(selected));
        var text = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static StackPanel CreateIconLabel(string glyph, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void PopulateQualityFlyout()
    {
        QualityListPanel.Children.Clear();

        var versions = _playerService.Versions;
        var qualities = _playerService.AvailableQualities;
        var activeFileId = _playerService.ActiveMediaFileId;
        var requestedFileId = _playerService.RequestedMediaFileId;

        if (versions.Count > 1)
            QualityListPanel.Children.Add(CreatePlayerMenuHeader("Version"));

        foreach (var version in versions)
        {
            var label = $"{version.Resolution}";
            if (version.Hdr) label += " HDR";
            if (!string.IsNullOrWhiteSpace(version.CodecVideo))
                label += $" ({version.CodecVideo.ToUpperInvariant()})";

            var statuses = new List<string>();
            if (version.FileId == activeFileId) statuses.Add("Playing");
            if (version.FileId == requestedFileId && version.FileId != activeFileId) statuses.Add("Requested");

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = label,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13,
                FontWeight = version.FileId == activeFileId
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (statuses.Count > 0)
            {
                var badgePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                foreach (var status in statuses)
                    badgePanel.Children.Add(CreatePlayerMenuBadge(status, outline: true));
                Grid.SetColumn(badgePanel, 1);
                row.Children.Add(badgePanel);
            }

            var capturedVersion = version;
            var button = CreatePlayerMenuButton(row, version.FileId == activeFileId);
            button.Click += async (_, _) =>
            {
                QualityFlyout.Hide();
                QualityButtonText.Text = "…";
                await _playerService.SwitchVersionAsync(capturedVersion);
                PopulateQualityFlyout();
            };
            QualityListPanel.Children.Add(button);
        }

        if (qualities.Count > 0)
        {
            if (versions.Count > 0)
                QualityListPanel.Children.Add(CreatePlayerMenuDivider());
            QualityListPanel.Children.Add(CreatePlayerMenuHeader("Quality"));

            foreach (var quality in qualities)
            {
                var qualityId = quality.Label;
                var label = qualityId.Equals("original", StringComparison.OrdinalIgnoreCase)
                    ? "Original"
                    : qualityId;
                var detail = quality.PreservesSource
                    ? "Source"
                    : quality.BitrateKbps is > 0
                        ? $"{quality.BitrateKbps.Value / 1000.0:0.#} Mbps"
                        : quality.Height is > 0 ? $"{quality.Height}p" : "";
                var isActive = string.Equals(qualityId, _playerService.ActiveQualityTier, StringComparison.OrdinalIgnoreCase);
                var row = new Grid { ColumnSpacing = 18 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                });
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    var detailText = new TextBlock
                    {
                        Text = detail,
                        FontSize = 11,
                        Foreground = PlayerMenuMutedBrush,
                    };
                    Grid.SetColumn(detailText, 1);
                    row.Children.Add(detailText);
                }

                var capturedQuality = qualityId;
                var item = CreatePlayerMenuButton(row, isActive);
                item.Click += async (_, _) =>
                {
                    QualityFlyout.Hide();
                    QualityButtonText.Text = "…";
                    await _playerService.SelectQualityAsync(capturedQuality);
                    PopulateQualityFlyout();
                };
                QualityListPanel.Children.Add(item);
            }
        }

        var activeQuality = qualities.FirstOrDefault(quality =>
            string.Equals(quality.Label, _playerService.ActiveQualityTier, StringComparison.OrdinalIgnoreCase));
        QualityButtonText.Text = _playerService.IsQualitySwitchActive
            ? "…"
            : activeQuality?.Label.Equals("original", StringComparison.OrdinalIgnoreCase) == true
                ? "Original"
                : activeQuality?.Label ?? _playerService.ActiveQualityTier;
    }

    // ── Subtitle selection ───────────────────────────────────────────────

    private void PopulateSubtitleFlyout()
    {
        SubtitleListPanel.Children.Clear();

        // "Off" option to disable subtitles. Persists the choice under the
        // series (or content) ID so the next play defaults to off too.
        var activeSubtitleIndex = _playerService.ActiveSubtitleServerIndex;
        var captionsAction = activeSubtitleIndex >= 0 ? "Disable captions" : "Enable captions";
        ToolTipService.SetToolTip(SubtitleButton, captionsAction);
        AutomationProperties.SetName(SubtitleButton, captionsAction);
        SubtitleListPanel.Children.Add(CreatePlayerMenuHeader("Subtitles"));
        var offItem = CreatePlayerMenuButton(
            CreateSelectionLabel("Off", activeSubtitleIndex < 0),
            activeSubtitleIndex < 0);
        offItem.Click += (_, _) =>
        {
            SubtitleFlyout.Hide();
            _ = _playerService.SetSubtitleTrackAndPersistAsync(0, null);
        };
        SubtitleListPanel.Children.Add(offItem);
        SubtitleListPanel.Children.Add(CreatePlayerMenuDivider());

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

            var isActive = activeSubtitleIndex == track.Index;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(CreateSelectionCheck(isActive));
            var descriptor = new StackPanel { Spacing = 3 };
            descriptor.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var source = track.Source?.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(format)) badges.Children.Add(CreatePlayerMenuBadge(format));
            if (!string.IsNullOrWhiteSpace(source)) badges.Children.Add(CreatePlayerMenuBadge(source));
            if (track.Forced) badges.Children.Add(CreatePlayerMenuBadge("Forced", outline: true));
            if (track.HearingImpaired) badges.Children.Add(CreatePlayerMenuBadge("HI", outline: true));
            if (badges.Children.Count > 0) descriptor.Children.Add(badges);
            Grid.SetColumn(descriptor, 1);
            row.Children.Add(descriptor);
            var item = CreatePlayerMenuButton(row, isActive);
            int capturedMpvIndex = origIdx + 1;
            var capturedTrack = track;
            item.Click += (_, _) =>
            {
                SubtitleFlyout.Hide();
                _ = _playerService.SetSubtitleTrackAndPersistAsync(
                    capturedMpvIndex, capturedTrack.Language, capturedTrack);
            };
            SubtitleListPanel.Children.Add(item);
        }

        // Match the compact WebUI delay control: label, decrement, current
        // value, increment, and reset share one row. Delay controls are not
        // actionable while subtitles are off.
        SubtitleListPanel.Children.Add(CreatePlayerMenuDivider());
        var delayRow = new Grid
        {
            ColumnSpacing = 4,
            Padding = new Thickness(12, 6, 12, 6),
        };
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        delayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var delayLabel = new TextBlock
        {
            Text = "DELAY",
            FontSize = 10,
            CharacterSpacing = 80,
            Foreground = PlayerMenuMutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var earlier = CreateSubtitleDelayButton("−", "Subtitle delay 100ms earlier");
        earlier.IsEnabled = activeSubtitleIndex >= 0 && _subtitleDelayMs > -10_000;
        earlier.Click += (_, _) => SetSubtitleDelay(Math.Max(-10_000, _subtitleDelayMs - 100));
        var delayValue = new TextBlock
        {
            Text = _subtitleDelayMs == 0 ? "0 ms" : $"{_subtitleDelayMs:+#;-#} ms",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = PlayerMenuTextBrush,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var later = CreateSubtitleDelayButton("+", "Subtitle delay 100ms later");
        later.IsEnabled = activeSubtitleIndex >= 0 && _subtitleDelayMs < 10_000;
        later.Click += (_, _) => SetSubtitleDelay(Math.Min(10_000, _subtitleDelayMs + 100));
        var resetDelay = new Button
        {
            Content = "Reset",
            FontSize = 12,
            Padding = new Thickness(7, 4, 7, 4),
            MinHeight = 28,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Foreground = PlayerMenuMutedBrush,
        };
        resetDelay.IsEnabled = activeSubtitleIndex >= 0 && _subtitleDelayMs != 0;
        AutomationProperties.SetName(resetDelay, "Reset subtitle delay");
        resetDelay.Click += (_, _) => SetSubtitleDelay(0);
        Grid.SetColumn(delayLabel, 0);
        Grid.SetColumn(earlier, 1);
        Grid.SetColumn(delayValue, 2);
        Grid.SetColumn(later, 3);
        Grid.SetColumn(resetDelay, 4);
        delayRow.Children.Add(delayLabel);
        delayRow.Children.Add(earlier);
        delayRow.Children.Add(delayValue);
        delayRow.Children.Add(later);
        delayRow.Children.Add(resetDelay);
        SubtitleListPanel.Children.Add(delayRow);

        SubtitleListPanel.Children.Add(CreatePlayerMenuDivider());
        if (_subtitleProviderStatus.Enabled)
        {
            var addItem = CreatePlayerMenuButton(CreateIconLabel("\uE721", "Search Online…"));
            addItem.Click += async (_, _) =>
            {
                var session = _playerService.Manager?.CurrentSession;
                if (session == null) return;
                SubtitleFlyout.Hide();
                var dialog = new SubtitleSearchDialog(session.MediaFileId, subtitleUrls.FirstOrDefault().Track?.Language)
                {
                    XamlRoot = XamlRoot
                };
                dialog.SubtitleDownloaded += async subtitleId =>
                {
                    await _playerService.RefreshSubtitlesAfterAiAsync(session.MediaFileId, subtitleId);
                    PopulateSubtitleFlyout();
                };
                await dialog.ShowAsync();
                SubtitleButton.Focus(FocusState.Programmatic);
            };
            SubtitleListPanel.Children.Add(addItem);
        }

        if (CanShowSubtitleAi())
        {
            var aiItem = CreatePlayerMenuButton(CreateIconLabel("\uE945", "Translate with AI…"));
            aiItem.Click += async (_, _) =>
            {
                SubtitleFlyout.Hide();
                await ShowSubtitleAiDialogAsync();
                SubtitleButton.Focus(FocusState.Programmatic);
            };
            SubtitleListPanel.Children.Add(aiItem);
        }

        var appearanceItem = CreatePlayerMenuButton(CreateIconLabel("\uE700", "Appearance…"));
        appearanceItem.Click += async (_, _) =>
        {
            try
            {
                SubtitleFlyout.Hide();
                var dlg = new SubtitleAppearanceDialog { XamlRoot = this.XamlRoot };
                await dlg.ShowAsync();
                SubtitleButton.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SubtitleAppearanceDialog failed: {ex.Message}");
            }
        };
        SubtitleListPanel.Children.Add(appearanceItem);
    }

    private void SubtitleFlyout_Opening(object sender, object e)
    {
        PopulateSubtitleFlyout();
        if (_subtitleAiStatus == null)
            _ = RefreshSubtitleAiCapabilityAsync();
        _ = RefreshSubtitleProviderCapabilityAsync();
    }

    private static Button CreateSubtitleDelayButton(string label, string accessibleName)
    {
        var button = new Button
        {
            Width = 28,
            Height = 28,
            MinWidth = 28,
            MinHeight = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Content = new TextBlock { Text = label, FontSize = 15, TextAlignment = TextAlignment.Center },
        };
        AutomationProperties.SetName(button, accessibleName);
        ToolTipService.SetToolTip(button, accessibleName);
        return button;
    }

    private bool CanShowSubtitleAi()
    {
        var status = _subtitleAiStatus;
        var session = _playerService.Manager?.CurrentSession;
        if (status == null || session == null) return false;

        var canTranslate = status.Enabled &&
            (_playerService.Manager?.GetSubtitleUrls() ?? [])
                .Any(pair => IsTranslatableSubtitleSource(pair.Track));
        var version = _playerService.Versions.FirstOrDefault(item => item.FileId == session.MediaFileId);
        var canTranscribe = status.TranscribeEnabled && version?.AudioTracks?.Count > 0;
        return canTranslate || canTranscribe;
    }

    private async Task RefreshSubtitleAiCapabilityAsync()
    {
        try
        {
            _subtitleAiStatus = await App.Services.GetRequiredService<PlaybackApi>().GetSubtitleAiStatusAsync();
        }
        catch
        {
            _subtitleAiStatus = null;
        }

        if (_isActive)
            DispatcherQueue?.TryEnqueue(PopulateSubtitleFlyout);
    }

    private async Task RefreshSubtitleProviderCapabilityAsync()
    {
        try
        {
            _subtitleProviderStatus = await App.Services.GetRequiredService<PlaybackApi>()
                .GetSubtitleProviderStatusAsync();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Servers predating the probe may still have working providers.
            _subtitleProviderStatus = new SubtitleProviderStatus { Enabled = true };
        }
        catch
        {
            // A transient capability failure should not remove a previously
            // usable action from the menu.
        }

        if (_isActive)
            DispatcherQueue?.TryEnqueue(PopulateSubtitleFlyout);
    }

    private void AudioFlyout_Opening(object sender, object e) => PopulateAudioFlyout();

    private void QualityFlyout_Opening(object sender, object e) => PopulateQualityFlyout();

    private void SpeedFlyout_Opening(object sender, object e) => PopulateSpeedFlyout();

    private void SleepFlyout_Opening(object sender, object e) => PopulateSleepFlyout();

    private void SetSubtitleDelay(int milliseconds)
    {
        _subtitleDelayMs = milliseconds;
        _playerService.Mpv?.SetProperty("sub-delay", (milliseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        PopulateSubtitleFlyout();
    }

    public async Task ShowSubtitleAiDialogAsync(XamlRoot? dialogXamlRoot = null)
    {
        var activeXamlRoot = dialogXamlRoot ?? XamlRoot;
        var session = _playerService.Manager?.CurrentSession;
        if (session == null) return;
        var api = App.Services.GetRequiredService<PlaybackApi>();
        SubtitleAiStatus capability;
        try { capability = await api.GetSubtitleAiStatusAsync(); }
        catch (Exception ex) { await ShowPlayerDialogAsync("AI subtitles unavailable", ex.Message, activeXamlRoot); return; }
        if (!capability.Enabled && !capability.TranscribeEnabled)
        {
            await ShowPlayerDialogAsync("AI subtitles unavailable", "This Silo server has not enabled subtitle translation or transcription.", activeXamlRoot);
            return;
        }

        var version = _playerService.Versions.FirstOrDefault(v => v.FileId == session.MediaFileId);
        var subtitleTracks = (_playerService.Manager?.GetSubtitleUrls() ?? [])
            .Select(pair => pair.Track)
            .Where(IsTranslatableSubtitleSource)
            .ToList();
        var audioTracks = version?.AudioTracks ?? [];
        var canTranslate = capability.Enabled && subtitleTracks.Count > 0;
        var canTranscribe = capability.TranscribeEnabled && audioTracks.Count > 0;
        if (!canTranslate && !canTranscribe)
        {
            await ShowPlayerDialogAsync(
                "No compatible source",
                "Add a text subtitle track first, or ask the server administrator to enable audio transcription.",
                activeXamlRoot);
            return;
        }

        SubtitleAiQuota? quota = null;
        if (capability.TranscribeEnabled)
        {
            try { quota = await api.GetSubtitleAiQuotaAsync(); } catch { }
        }
        var dialog = new SubtitleAiDialog(
            subtitleTracks,
            audioTracks,
            capability.Enabled,
            capability.TranscribeEnabled,
            quota,
            async selection =>
            {
                if (!ReferenceEquals(session, _playerService.Manager?.CurrentSession))
                    throw new InvalidOperationException(
                        "Playback changed while the subtitle tool was open. Open it again for the current item.");

                var request = new SubtitleAiRequest
                {
                    MediaFileId = session.MediaFileId,
                    TargetLanguage = MediaLanguageCatalog.Normalize(selection.TargetLanguage),
                    SessionId = session.SessionId,
                    StartPosition = _playerService.Position,
                    SourceIndex = selection.SourceIndex,
                    SourceLanguage = MediaLanguageCatalog.Normalize(selection.SourceLanguage),
                };
                if (selection.Mode == "audio")
                {
                    request.Kind = string.Equals(
                        selection.SourceLanguage,
                        selection.TargetLanguage,
                        StringComparison.OrdinalIgnoreCase)
                        ? "transcribe"
                        : "transcribe_translate";
                    if (request.Kind == "transcribe")
                        request.TargetLanguage = "";
                }

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
            },
            async () =>
            {
                try { return await api.GetSubtitleAiQuotaAsync(); }
                catch { return null; }
            })
        {
            XamlRoot = activeXamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async Task MonitorSubtitleAiJobAsync(PlaybackApi api, long jobId, int mediaFileId)
    {
        if (jobId <= 0) return;
        for (var attempt = 0; attempt < 3600; attempt++)
        {
            var activeSession = _playerService.Manager?.CurrentSession;
            if (activeSession == null ||
                activeSession.MediaFileId != mediaFileId ||
                _playerService.State == PlayerState.Idle)
            {
                return;
            }

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

    private async Task ShowPlayerDialogAsync(string title, string message, XamlRoot? dialogXamlRoot = null)
    {
        var dialog = new ContentDialog { XamlRoot = dialogXamlRoot ?? XamlRoot, Title = title, Content = message, CloseButtonText = "Close" };
        await dialog.ShowAsync();
    }

    private bool CanEditMarkers()
        => AuthorizationPolicy.CanEditMarkers(_authService);

    private void MarkerEdit_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerEditorPanel.Visibility == Visibility.Visible) CloseMarkerEditor();
        else OpenMarkerEditor();
    }

    // Kept as Task-returning API because the native mpv OSC invokes this via
    // MainWindow. Opening the editor itself is synchronous and never replaces
    // or suspends the current playback surface.
    public Task ShowMarkerEditDialogAsync(XamlRoot? dialogXamlRoot = null)
    {
        OpenMarkerEditor();
        return Task.CompletedTask;
    }

    private void OpenMarkerEditor()
    {
        if (_playerService.Manager?.CurrentSession == null || !CanEditMarkers()) return;

        MarkerRowsPanel.Children.Clear();
        _markerEditRows =
        [
            CreateMarkerEditRow("intro", "Intro", "#38BDF8", _playerService.ActiveIntro),
            CreateMarkerEditRow("recap", "Recap", "#A78BFA", _playerService.ActiveRecap),
            CreateMarkerEditRow("credits", "Credits / Outro", "#FBBF24", _playerService.ActiveCredits),
            CreateMarkerEditRow("preview", "Preview", "#34D399", _playerService.ActivePreview)
        ];

        foreach (var row in _markerEditRows)
        {
            MarkerRowsPanel.Children.Add(row.Element);
            row.HeaderButton.Click += (_, _) => SelectMarkerEditRow(row);
            row.SetStartButton.Click += (_, _) =>
            {
                row.SetStart(_playerService.Position, _playerService.Duration);
                RefreshMarkerEditorState();
            };
            row.SetEndButton.Click += (_, _) =>
            {
                row.SetEnd(_playerService.Position, _playerService.Duration);
                RefreshMarkerEditorState();
            };
            row.ResetButton.Click += (_, _) =>
            {
                row.SetRange(row.Original);
                RefreshMarkerEditorState();
            };
            row.ClearButton.Click += (_, _) =>
            {
                row.SetRange(null);
                RefreshMarkerEditorState();
            };
        }

        MarkerEditorError.Visibility = Visibility.Collapsed;
        MarkerEditorCurrentTime.Text = FormatChapterTime(_playerService.Position);
        MarkerEditorPanel.RenderTransform = new TranslateTransform();
        MarkerEditorPanel.Visibility = Visibility.Visible;
        SeekBar.IsMarkerEditing = true;
        SelectMarkerEditRow(_markerEditRows[0]);
        RefreshMarkerEditorState();
        ShowControls();
    }

    private void CloseMarkerEditor()
    {
        MarkerEditorPanel.Visibility = Visibility.Collapsed;
        MarkerEditorError.Visibility = Visibility.Collapsed;
        _markerEditRows = null;
        _activeMarkerEditRow = null;
        _markerSaveInProgress = false;
        SeekBar.IsMarkerEditing = false;
        SeekBar.EditableMarker = null;
        RefreshMarkerRegions();
        SeekBar.Invalidate();
        MarkerEditButton.Focus(FocusState.Programmatic);
    }

    private void SelectMarkerEditRow(MarkerEditRow selected)
    {
        _activeMarkerEditRow = selected;
        if (_markerEditRows == null) return;
        foreach (var row in _markerEditRows)
        {
            var active = ReferenceEquals(row, selected);
            row.Actions.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            row.Element.Background = new SolidColorBrush(active
                ? Windows.UI.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)
                : Windows.UI.Color.FromArgb(0, 0, 0, 0));
            row.Element.BorderThickness = active ? new Thickness(1) : new Thickness(0);
        }
        RefreshMarkerEditorState();
    }

    private void RefreshMarkerEditorState()
    {
        if (_markerEditRows == null) return;
        foreach (var row in _markerEditRows) row.Refresh();
        var dirty = _markerEditRows.Any(row => !RangesEqual(row.Original, row.CurrentRange));
        MarkerEditorSaveButton.IsEnabled = dirty && !_markerSaveInProgress;
        MarkerEditorResetAllButton.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;

        TimeRange? Find(string kind) => _markerEditRows.First(row => row.Kind == kind).CurrentRange;
        var intro = Find("intro");
        var recap = Find("recap");
        var credits = Find("credits");
        var preview = Find("preview");
        SeekBar.IntroMarker = intro == null ? null : (intro.Start, intro.End);
        SeekBar.RecapMarker = recap == null ? null : (recap.Start, recap.End);
        SeekBar.CreditsMarker = credits == null ? null : (credits.Start, credits.End);
        SeekBar.PreviewMarker = preview == null ? null : (preview.Start, preview.End);
        var active = _activeMarkerEditRow?.CurrentRange;
        SeekBar.EditableMarker = active == null ? null : (active.Start, active.End);
        SeekBar.Invalidate();
    }

    private void MarkerEdgeChanged(string edge, double seconds)
    {
        if (_activeMarkerEditRow == null || MarkerEditorPanel.Visibility != Visibility.Visible) return;
        if (edge == "start") _activeMarkerEditRow.SetStart(seconds, _playerService.Duration);
        else _activeMarkerEditRow.SetEnd(seconds, _playerService.Duration);
        RefreshMarkerEditorState();
        ShowControls();
    }

    private void MarkerEditorResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (_markerEditRows == null) return;
        foreach (var row in _markerEditRows) row.SetRange(row.Original);
        RefreshMarkerEditorState();
    }

    private void MarkerEditorCancel_Click(object sender, RoutedEventArgs e) => CloseMarkerEditor();

    private async void MarkerEditorSave_Click(object sender, RoutedEventArgs e)
    {
        var session = _playerService.Manager?.CurrentSession;
        var rows = _markerEditRows;
        if (session == null || rows == null || _markerSaveInProgress) return;

        var changes = new Dictionary<string, object?>();
        foreach (var row in rows)
        {
            var next = row.CurrentRange;
            if (!RangesEqual(row.Original, next))
                changes[row.Kind] = next == null ? null : new { start = next.Start, end = next.End };
            row.Result = next;
        }
        if (changes.Count == 0) return;

        _markerSaveInProgress = true;
        MarkerEditorSaveButton.Content = "Saving…";
        MarkerEditorSaveButton.IsEnabled = false;
        MarkerEditorError.Visibility = Visibility.Collapsed;
        try
        {
            await App.Services.GetRequiredService<PlaybackApi>().SetFileMarkersAsync(session.MediaFileId, changes);
            _playerService.ApplyMarkerEdits(rows[0].Result, rows[1].Result, rows[2].Result, rows[3].Result);
            CloseMarkerEditor();
        }
        catch (Exception ex)
        {
            MarkerEditorError.Text = ex.Message;
            MarkerEditorError.Visibility = Visibility.Visible;
        }
        finally
        {
            _markerSaveInProgress = false;
            MarkerEditorSaveButton.Content = "Save";
            if (MarkerEditorPanel.Visibility == Visibility.Visible) RefreshMarkerEditorState();
        }
    }

    private void MarkerEditorHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement header) return;
        _markerPanelDragging = true;
        _markerPanelDragStart = e.GetCurrentPoint(this).Position;
        var transform = MarkerEditorPanel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        MarkerEditorPanel.RenderTransform = transform;
        _markerPanelDragOriginX = transform.X;
        _markerPanelDragOriginY = transform.Y;
        header.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void MarkerEditorHeader_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_markerPanelDragging) return;
        var current = e.GetCurrentPoint(this).Position;
        var transform = (TranslateTransform)MarkerEditorPanel.RenderTransform;
        var maxX = Math.Max(0, ActualWidth - MarkerEditorPanel.ActualWidth - 16);
        var maxUp = Math.Max(0, ActualHeight - MarkerEditorPanel.ActualHeight - 16);
        transform.X = Math.Clamp(_markerPanelDragOriginX + current.X - _markerPanelDragStart.X, -8, maxX);
        transform.Y = Math.Clamp(_markerPanelDragOriginY + current.Y - _markerPanelDragStart.Y, -maxUp, 134);
        e.Handled = true;
    }

    private void MarkerEditorHeader_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _markerPanelDragging = false;
        if (sender is UIElement header) header.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void MarkerEditorHeader_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        => _markerPanelDragging = false;

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

    private static bool IsTranslatableSubtitleSource(SubtitleTrackInfo track)
    {
        var codec = track.Codec?.Trim().ToLowerInvariant() ?? "";
        if (string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase))
        {
            return codec is not (
                "pgs" or "hdmv_pgs_subtitle" or "sup" or
                "dvdsub" or "dvd_subtitle" or "vobsub" or
                "dvbsub" or "dvb_subtitle");
        }

        return codec is "srt" or "subrip" or "vtt" or "webvtt";
    }

    private void PopulateSpeedFlyout()
    {
        SpeedListPanel.Children.Clear();
        foreach (var rate in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var isActive = Math.Abs(rate - _playbackSpeed) < 0.001;
            var label = new TextBlock
            {
                Text = $"{rate:0.##}×",
                FontSize = 13,
                FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var item = CreatePlayerMenuButton(label, isActive);
            item.HorizontalContentAlignment = HorizontalAlignment.Right;
            item.Click += (_, _) =>
            {
                SpeedFlyout.Hide();
                _playbackSpeed = rate;
                _playerService.Mpv?.SetProperty("speed", rate.ToString(CultureInfo.InvariantCulture));
                SpeedButtonText.Text = $"{rate:0.##}×";
                PopulateSpeedFlyout();
            };
            SpeedListPanel.Children.Add(item);
        }
    }

    private void PopulateSleepFlyout()
    {
        SleepListPanel.Children.Clear();
        if (_sleepDeadline != null || _sleepAtPosition != null)
        {
            var off = CreatePlayerMenuButton("Turn off");
            off.Click += (_, _) =>
            {
                SleepFlyout.Hide();
                _sleepDeadline = null;
                _sleepAtPosition = null;
                PopulateSleepFlyout();
            };
            SleepListPanel.Children.Add(off);
            SleepListPanel.Children.Add(CreatePlayerMenuDivider());
        }
        foreach (var (label, seconds) in new[] { ("5 min", 300), ("15 min", 900), ("30 min", 1800), ("45 min", 2700), ("60 min", 3600) })
        {
            var item = CreatePlayerMenuButton(label);
            item.Click += (_, _) =>
            {
                SleepFlyout.Hide();
                _sleepAtPosition = null;
                _sleepDeadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
                PopulateSleepFlyout();
            };
            SleepListPanel.Children.Add(item);
        }
        var chapterItem = CreatePlayerMenuButton("End of chapter");
        chapterItem.Click += (_, _) =>
        {
            SleepFlyout.Hide();
            var current = _playerService.Position;
            var version = _playerService.Versions.FirstOrDefault(v => v.FileId == (_playerService.Manager?.CurrentSession?.MediaFileId ?? 0));
            var nextEnd = version?.Chapters?.Where(c => c.EndSeconds > current + 1).OrderBy(c => c.EndSeconds).FirstOrDefault()?.EndSeconds;
            _sleepDeadline = null;
            _sleepAtPosition = nextEnd ?? _playerService.Duration;
            PopulateSleepFlyout();
        };
        SleepListPanel.Children.Add(chapterItem);
        UpdateSleepButtonText();
    }

    private void UpdateSleepButtonText()
    {
        double? remainingSeconds = null;
        if (_sleepDeadline is { } deadline)
            remainingSeconds = Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalSeconds);
        else if (_sleepAtPosition is { } target)
            remainingSeconds = Math.Max(0, target - _playerService.Position);

        if (remainingSeconds == null)
        {
            SleepButtonText.Text = "Sleep";
            return;
        }

        var total = (int)Math.Ceiling(remainingSeconds.Value);
        SleepButtonText.Text = $"Sleep {total / 60}:{total % 60:00}";
    }

    private void CheckSleepTimer(double position)
    {
        UpdateSleepButtonText();
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
        AudioListPanel.Children.Clear();

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
        AudioListPanel.Children.Add(CreatePlayerMenuHeader("Audio"));

        for (int i = 0; i < version.AudioTracks.Count; i++)
        {
            var at = version.AudioTracks[i];
            var langName = PlayerService.LanguageCodeToName(at.Language);
            var title = !string.IsNullOrWhiteSpace(at.Title)
                ? at.Title.Trim()
                : !string.IsNullOrWhiteSpace(at.EmbeddedTitle)
                    ? at.EmbeddedTitle.Trim()
                    : !string.IsNullOrWhiteSpace(langName) ? langName : $"Track {i + 1}";
            var channelLabel = at.Channels is > 0
                ? at.Channels.Value switch
                {
                    1 => "Mono",
                    2 => "Stereo",
                    6 => "5.1",
                    8 => "7.1",
                    _ => $"{at.Channels}ch",
                }
                : at.Layout ?? "";
            var codecLabel = "";
            if (!string.IsNullOrEmpty(at.Codec))
            {
                codecLabel = at.Codec.ToUpperInvariant() switch
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
            }

            var metadata = string.Join(" · ", new[]
            {
                !string.Equals(langName, title, StringComparison.OrdinalIgnoreCase) ? langName : "",
                !string.IsNullOrWhiteSpace(at.Layout) && !string.Equals(at.Layout, channelLabel, StringComparison.OrdinalIgnoreCase) ? at.Layout : "",
                at.Bitrate is > 0 ? $"{at.Bitrate.Value / 1000.0:0.#} kbps" : "",
                at.SampleRate is > 0 ? $"{at.SampleRate.Value / 1000.0:0.#} kHz" : "",
                at.BitDepth is > 0 ? $"{at.BitDepth}-bit" : "",
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var isActive = i == currentSession.AudioTrackIndex;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(CreateSelectionCheck(isActive));
            var descriptor = new StackPanel { Spacing = 3 };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            titleRow.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 190,
            });
            if (!string.IsNullOrWhiteSpace(codecLabel)) titleRow.Children.Add(CreatePlayerMenuBadge(codecLabel));
            if (!string.IsNullOrWhiteSpace(channelLabel)) titleRow.Children.Add(CreatePlayerMenuBadge(channelLabel));
            if (at.Default) titleRow.Children.Add(CreatePlayerMenuBadge("Default", outline: true));
            descriptor.Children.Add(titleRow);
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                descriptor.Children.Add(new TextBlock
                {
                    Text = metadata,
                    FontSize = 11,
                    Foreground = PlayerMenuMutedBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            Grid.SetColumn(descriptor, 1);
            row.Children.Add(descriptor);

            var item = CreatePlayerMenuButton(row, isActive);

            int trackIndex = i;
            item.Click += (_, _) =>
            {
                AudioFlyout.Hide();
                _ = _playerService.SwitchAudioTrackAsync(trackIndex);
            };
            AudioListPanel.Children.Add(item);
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
            ChaptersListPanel.Children.Add(BuildChapterRow(chapter, currentSession.MediaFileId));
        }
    }

    private void ChaptersFlyout_Opening(object sender, object e) => PopulateChaptersFlyout();

    private Button BuildChapterRow(Core.Models.Playback.VersionChapter chapter, int mediaFileId)
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
            Child = new FontIcon
            {
                Glyph = "\uE714",
                FontSize = 16,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.25 },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (!string.IsNullOrEmpty(chapter.ThumbnailUrl))
        {
            _ = LoadChapterThumbnailAsync(thumbBorder, chapter, mediaFileId);
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
            Background = new SolidColorBrush(Microsoft.UI.Colors.White)
            {
                Opacity = _playerService.Position >= chapter.StartSeconds && _playerService.Position < chapter.EndSeconds
                    ? 0.05
                    : 0,
            },
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            Content = row,
        };
        AutomationProperties.SetName(btn, $"{(string.IsNullOrEmpty(chapter.Title) ? $"Chapter {chapter.Index + 1}" : chapter.Title)}, {FormatChapterTime(chapter.StartSeconds)}");
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

    private async Task LoadChapterThumbnailAsync(Border container, Core.Models.Playback.VersionChapter chapter, int mediaFileId)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<Core.Services.ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var key = $"chapter_{mediaFileId}_{chapter.Index}";
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
        var version = _playerService.ActiveVersion
                   ?? _playerService.Versions.FirstOrDefault(v => v.FileId == currentSession?.MediaFileId)
                   ?? _playerService.Versions.FirstOrDefault();
        var requested = _playerService.RequestedMediaFileId is int requestedFileId
            ? _playerService.Versions.FirstOrDefault(v => v.FileId == requestedFileId)
            : null;
        var videoTrack = version?.VideoTracks?.FirstOrDefault();
        var audioTrack = version?.AudioTracks?.FirstOrDefault(track => track.Default)
                      ?? version?.AudioTracks?.FirstOrDefault();

        var playerWidth = ActualWidth > 0 ? ActualWidth : 0;
        var playerHeight = ActualHeight > 0 ? ActualHeight : 0;
        var videoWidth = ReadMpvNumber("dwidth");
        var videoHeight = ReadMpvNumber("dheight");
        if (videoWidth <= 0) videoWidth = videoTrack?.Width ?? 0;
        if (videoHeight <= 0) videoHeight = videoTrack?.Height ?? 0;

        var playMethod = FormatDelivery(currentSession?.Delivery, currentSession?.PlayMethod ?? _playerService.PlayMethod);
        var streamType = string.Equals(currentSession?.PlaybackInfo?.StreamType, "hls", StringComparison.OrdinalIgnoreCase)
            || (currentSession?.Delivery?.Contains("hls", StringComparison.OrdinalIgnoreCase) ?? false)
            ? "HLS"
            : "Progressive";
        var requestedSource = requested != null && version != null && requested.FileId != version.FileId
            ? FormatRequestedSource(requested)
            : null;

        var playerRows = new List<(string Label, string Value)>
        {
            ("Player", "libmpv"),
            ("Play method", playMethod),
            ("Protocol", "http"),
            ("Stream type", streamType),
        };
        if (!string.IsNullOrWhiteSpace(requestedSource))
            playerRows.Add(("Auto-switched from", requestedSource));

        StatsSectionsPanel.Children.Clear();
        AddPlaybackInfoSection("Player", playerRows);
        AddPlaybackInfoSection("Video Info",
        [
            ("Player dimensions", FormatDimensions(playerWidth, playerHeight)),
            ("Video resolution", FormatDimensions(videoWidth, videoHeight)),
            ("Dropped frames", FormatFrameCount(ReadMpvNumber("decoder-frame-drop-count"), ReadMpvNumber("frame-drop-count"))),
            ("Corrupted frames", "—"),
        ]);
        AddPlaybackInfoSection("Playback Stream Info",
        [
            ("Video codec", FormatDeliveredCodec(currentSession?.PlaybackInfo?.VideoCodec ?? version?.CodecVideo, playMethod, playMethod == "Transcode")),
            ("Audio codec", FormatDeliveredCodec(currentSession?.PlaybackInfo?.AudioCodec ?? version?.CodecAudio, playMethod, currentSession?.PlaybackInfo?.TranscodeAudio == true)),
        ]);
        AddPlaybackInfoSection("Current Source File",
        [
            ("Container", DisplayValue(version?.Container)),
            ("Size", FormatFileSize(version?.FileSize ?? 0)),
            ("Bitrate", FormatMbps(version?.Bitrate)),
            ("Video codec", FormatSourceVideoCodec(version, videoTrack)),
            ("Video bitrate", FormatMbps(videoTrack?.Bitrate)),
            ("Video range type", FormatVideoRange(version, videoTrack)),
            ("Color range", FormatColorRange(videoTrack?.ColorRange)),
            ("Audio codec", FormatSourceAudioCodec(version, audioTrack)),
            ("Audio bitrate", FormatKbps(audioTrack?.Bitrate)),
            ("Audio channels", FormatPositive(audioTrack?.Channels ?? version?.AudioChannels)),
            ("Audio sample rate", FormatSampleRate(audioTrack?.SampleRate)),
        ]);
    }

    private void AddPlaybackInfoSection(string title, IEnumerable<(string Label, string Value)> rows)
    {
        var section = new StackPanel { Spacing = 2 };
        section.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = 80,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 2),
        });

        foreach (var (label, value) in rows)
        {
            var row = new Grid { ColumnSpacing = 16, MinHeight = 20 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var valueText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(labelText);
            row.Children.Add(valueText);
            section.Children.Add(row);
        }

        StatsSectionsPanel.Children.Add(section);
    }

    private double ReadMpvNumber(string property)
    {
        try { return _playerService.Mpv?.GetPropertyDouble(property) ?? -1; }
        catch { return -1; }
    }

    private static string FormatDelivery(string? delivery, string? fallback)
    {
        return delivery?.Trim().ToLowerInvariant() switch
        {
            "original_http" => "Direct Play",
            "server_remux_progressive" or "server_remux_hls" => "Direct Streaming",
            "server_transcode_hls" => "Transcode",
            _ when fallback?.Contains("transcode", StringComparison.OrdinalIgnoreCase) == true => "Transcode",
            _ when fallback?.Contains("remux", StringComparison.OrdinalIgnoreCase) == true => "Direct Streaming",
            _ => "Direct Play",
        };
    }

    private static string FormatDimensions(double width, double height) =>
        width > 0 && height > 0 ? $"{Math.Round(width):0}x{Math.Round(height):0}" : "—";

    private static string FormatFrameCount(double primary, double fallback)
    {
        var value = primary >= 0 ? primary : fallback;
        return value >= 0 ? Math.Round(value).ToString(CultureInfo.InvariantCulture) : "—";
    }

    private static string FormatDeliveredCodec(string? codec, string playMethod, bool transcoded)
    {
        var label = FormatCodec(codec);
        if (label == "—") return label;
        if (playMethod == "Direct Play") return $"{label} (direct)";
        return $"{label} ({(transcoded ? "transcoded" : "copy")})";
    }

    private static string FormatRequestedSource(FileVersion version)
    {
        var values = new[]
        {
            version.Resolution?.Trim(),
            FormatCodec(version.CodecVideo),
            FormatVideoRange(version, version.VideoTracks?.FirstOrDefault()),
        };
        return string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value) && value != "—"));
    }

    private static string FormatSourceVideoCodec(FileVersion? version, VersionVideoTrack? track)
    {
        var codec = FormatCodec(track?.Codec ?? version?.CodecVideo);
        return codec == "—" ? codec : string.Join(" ", new[] { codec, track?.Profile }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatVideoRange(FileVersion? version, VersionVideoTrack? track)
    {
        if (!string.IsNullOrWhiteSpace(track?.DolbyVision))
        {
            var dv = track.DolbyVision.StartsWith("Dolby Vision", StringComparison.OrdinalIgnoreCase)
                ? track.DolbyVision
                : $"Dolby Vision {track.DolbyVision}";
            return !string.IsNullOrWhiteSpace(track.VideoRange) ? $"{dv} ({track.VideoRange})" : dv;
        }
        if (track?.DvProfile is int dvProfile)
            return $"Dolby Vision Profile {dvProfile}";
        if (!string.IsNullOrWhiteSpace(track?.VideoRange)) return track.VideoRange;
        if (!string.IsNullOrWhiteSpace(track?.VideoRangeType)) return track.VideoRangeType;
        return version == null ? "—" : version.Hdr ? "HDR" : "SDR";
    }

    private static string FormatColorRange(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "tv" => "Limited (tv)",
        "pc" => "Full (pc)",
        "unknown" => "Unknown",
        _ => "—",
    };

    private static string FormatSourceAudioCodec(FileVersion? version, AudioTrackInfo? track) =>
        !string.IsNullOrWhiteSpace(track?.Title) ? track.Title
        : !string.IsNullOrWhiteSpace(track?.EmbeddedTitle) ? track.EmbeddedTitle
        : FormatCodec(track?.Codec ?? version?.CodecAudio);

    private static string FormatCodec(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec)) return "—";
        return codec.Trim().ToLowerInvariant() switch
        {
            "h264" or "avc" or "avc1" => "H.264",
            "hevc" or "h265" or "hev1" or "hvc1" => "HEVC",
            "av1" => "AV1",
            "vp9" => "VP9",
            "aac" => "AAC",
            "ac3" or "ac-3" => "AC3",
            "eac3" or "e-ac-3" => "EAC3",
            "truehd" => "TrueHD",
            "dts" => "DTS",
            "flac" => "FLAC",
            "opus" => "Opus",
            var normalized => normalized.ToUpperInvariant(),
        };
    }

    private static string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "—";
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):F1} GiB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024d * 1024):F1} MiB";
        if (bytes >= 1024L) return $"{bytes / 1024d:F1} KiB";
        return $"{bytes} B";
    }

    private static string FormatMbps(int? kbps) => kbps is > 0 ? $"{kbps.Value / 1000d:F1} Mbps" : "—";
    private static string FormatKbps(int? kbps) => kbps is > 0 ? $"{kbps.Value:N0} kbps" : "—";
    private static string FormatSampleRate(int? hertz) => hertz is > 0 ? $"{hertz.Value:N0} Hz" : "—";
    private static string FormatPositive(int? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : "—";

    // ── Helpers ──────────────────────────────────────────────────────────

    private void UpdateVolumeIcon()
    {
        if (_isMuted || VolumeSlider.Value <= 0)
            VolumeIcon.Glyph = "\uE74F"; // Mute
        else if (VolumeSlider.Value < 50)
            VolumeIcon.Glyph = "\uE993"; // Volume1
        else
            VolumeIcon.Glyph = "\uE767"; // Volume3
        var label = _isMuted || VolumeSlider.Value <= 0 ? "Unmute" : "Mute";
        AutomationProperties.SetName(VolumeButton, label);
        ToolTipService.SetToolTip(VolumeButton, label);
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
