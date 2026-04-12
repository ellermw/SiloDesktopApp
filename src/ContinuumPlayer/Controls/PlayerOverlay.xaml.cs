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
    private bool _suppressSeek;
    private bool _isMuted;
    private bool _isActive;

    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _hideTimer;

    public PlayerOverlay()
    {
        _playerService = App.Services.GetRequiredService<PlayerService>();
        this.InitializeComponent();
    }

    // ── Activate / Deactivate (called by MainWindow when visibility toggles) ──

    public void Activate()
    {
        if (_isActive) return;
        _isActive = true;

        // Subscribe to content/playback events (video renders via native GPU window)
        _playerService.ContentLoaded += OnContentLoaded;
        _playerService.PlaybackEnded += OnPlaybackEnded;
        _playerService.ShowPlayingNextRequested += OnShowPlayingNextRequested;
        _playerService.StateChanged += OnPlayerStateChanged;

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
        _playerService.ShowPlayingNextRequested -= OnShowPlayingNextRequested;
        _playerService.StateChanged -= OnPlayerStateChanged;
        StopPlayingNextCountdown();
        PlayingNextOverlay.Visibility = Visibility.Collapsed;

        // Stop timers
        _uiTimer?.Stop();
        _uiTimer = null;
        _hideTimer?.Stop();
        _hideTimer = null;
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

    // ── Playing Next cinematic overlay (Phase 3b) ───────────────────────

    private DispatcherTimer? _playingNextTimer;
    private int _playingNextRemaining;
    private const int PlayingNextCountdownSeconds = 10;

    private void OnShowPlayingNextRequested()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (!_isActive) return;

            var title = _playerService.NextEpisodeTitle ?? "Next episode";
            var series = _playerService.NextEpisodeSeriesTitle;
            var overview = _playerService.NextEpisodeOverview ?? "";

            PlayingNextTitleText.Text = title;
            PlayingNextSeriesText.Text = series ?? "";
            PlayingNextSeriesText.Visibility = string.IsNullOrEmpty(series) ? Visibility.Collapsed : Visibility.Visible;
            PlayingNextOverviewText.Text = overview;
            PlayingNextOverviewText.Visibility = string.IsNullOrEmpty(overview) ? Visibility.Collapsed : Visibility.Visible;
            PlayingNextPoster.Source = null;
            _ = LoadPlayingNextPosterAsync();

            _playingNextRemaining = PlayingNextCountdownSeconds;
            PlayingNextPlayNowText.Text = $"Play next in {_playingNextRemaining}";
            PlayingNextOverlay.Visibility = Visibility.Visible;

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

    private void PlayingNextTimer_Tick(object? sender, object e)
    {
        _playingNextRemaining--;
        if (_playingNextRemaining <= 0)
        {
            StopPlayingNextCountdown();
            PlayingNextOverlay.Visibility = Visibility.Collapsed;
            _ = _playerService.ContinuePlayingNextAsync();
            return;
        }
        PlayingNextPlayNowText.Text = $"Play next in {_playingNextRemaining}";
    }

    private void PlayingNextPlayNow_Click(object sender, RoutedEventArgs e)
    {
        StopPlayingNextCountdown();
        PlayingNextOverlay.Visibility = Visibility.Collapsed;
        _ = _playerService.ContinuePlayingNextAsync();
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
            var imageService = App.Services.GetRequiredService<Core.Services.ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var bytes = await imageService.GetImageAsync(
                _playerService.NextEpisodeContentId ?? "next",
                "backdrop", url, httpClient, CancellationToken.None);
            if (bytes == null) return;

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 640,
                DecodePixelType = DecodePixelType.Logical,
            };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

            DispatcherQueue?.TryEnqueue(() =>
            {
                PlayingNextPoster.Source = bitmap;
            });
        }
        catch { /* Poster is cosmetic */ }
    }

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

        // Update seek slider without triggering seek
        _suppressSeek = true;
        if (dur > 0) SeekSlider.Maximum = dur;
        SeekSlider.Value = pos;
        _suppressSeek = false;

        // Update time labels
        PositionText.Text = PlayerService.FormatTime(pos);
        DurationText.Text = PlayerService.FormatTime(dur);

        // Update play/pause icon based on actual playback state
        PlayPauseIcon.Glyph = _playerService.Mpv.IsPaused ? "\uE768" : "\uE769";

        // Update fullscreen icon
        FullscreenIcon.Glyph = _playerService.State == PlayerState.Fullscreen ? "\uE73F" : "\uE740";

        // Report progress to server
        _playerService.Manager?.UpdatePosition(pos, _playerService.Mpv.IsPaused);

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
        ControlsOverlay.Opacity = 1;
        ControlsOverlay.IsHitTestVisible = true;

        // Restart the hide timer
        _hideTimer?.Stop();
        _hideTimer?.Start();
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

    // ── Seek slider interaction (suppressed feedback loop pattern) ────────
    //
    // WebUI commits seeks only on mouseup, not during drag — avoids seek storms
    // on mpv (which can cause buffering thrashing). We replicate that here with
    // a _isDragging flag: ValueChanged during drag only updates the displayed
    // position; the actual mpv.Seek() fires on PointerCaptureLost (drag-end).
    // Plain clicks (tap without drag) seek immediately via PointerPressed.

    private bool _isDragging;
    private double _pendingSeekValue;

    private void SeekSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isDragging = true;
        _pendingSeekValue = SeekSlider.Value;
    }

    private void SeekSlider_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDragging || _playerService.Mpv == null) { _isDragging = false; return; }
        _isDragging = false;
        // Commit the final seek position
        _playerService.Mpv.Seek(_pendingSeekValue);
        ShowControls();
    }

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // If the change came from our programmatic update in the timer, ignore it
        if (_suppressSeek || _playerService.Mpv == null) return;

        if (_isDragging)
        {
            // Track where the user is scrubbing — don't commit until drag-end
            _pendingSeekValue = e.NewValue;
            ShowControls();
            return;
        }

        // User clicked without dragging — seek to the new position immediately
        _playerService.Mpv.Seek(e.NewValue);
        ShowControls();
    }

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

        // "Off" option to disable subtitles
        var offItem = new MenuFlyoutItem { Text = "Off" };
        offItem.Click += (_, _) =>
        {
            _playerService.Mpv?.SetSubtitleTrack(0); // 0 disables subtitles in mpv
        };
        SubtitleFlyout.Items.Add(offItem);
        SubtitleFlyout.Items.Add(new MenuFlyoutSeparator());

        // Subtitle tracks from the session
        var subtitleUrls = _playerService.Manager?.GetSubtitleUrls() ?? [];
        int mpvTrackIndex = 1; // mpv subtitle tracks are 1-based
        for (int idx = 0; idx < subtitleUrls.Count; idx++)
        {
            var (track, _) = subtitleUrls[idx];
            var codec = track.Codec?.ToLowerInvariant() ?? "";
            if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
                continue; // skip bitmap subs (they were not loaded)

            // Build a readable label
            var langName = PlayerService.LanguageCodeToName(track.Language);
            var trackTitle = track.Label;

            // If the title is just the codec name, ignore it
            if (string.Equals(trackTitle, track.Codec, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trackTitle, "subrip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trackTitle, "ass", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trackTitle, "srt", StringComparison.OrdinalIgnoreCase))
                trackTitle = null;

            var label = langName;
            if (!string.IsNullOrEmpty(trackTitle) && !string.Equals(trackTitle, langName, StringComparison.OrdinalIgnoreCase))
                label += $" - {trackTitle}";
            if (string.IsNullOrEmpty(label)) label = $"Track {idx + 1}";
            if (track.Forced) label += " [Forced]";

            var item = new MenuFlyoutItem { Text = label };
            int capturedIndex = mpvTrackIndex;
            item.Click += (_, _) => _playerService.Mpv?.SetSubtitleTrack(capturedIndex);
            SubtitleFlyout.Items.Add(item);

            mpvTrackIndex++;
        }
    }

    // ── Audio track switching ────────────────────────────────────────────

    private void PopulateAudioFlyout()
    {
        AudioFlyout.Items.Clear();

        var currentSession = _playerService.Manager?.CurrentSession;
        if (currentSession == null) return;

        // Get audio tracks from the version that matches the current session
        var version = _playerService.Versions.FirstOrDefault(v => v.FileId == currentSession.MediaFileId);
        if (version?.AudioTracks == null) return;

        for (int i = 0; i < version.AudioTracks.Count; i++)
        {
            var at = version.AudioTracks[i];
            var label = at.Language ?? "Unknown";
            if (!string.IsNullOrEmpty(at.Title)) label += $" - {at.Title}";
            if (!string.IsNullOrEmpty(at.Codec)) label += $" ({at.Codec.ToUpperInvariant()})";
            if (at.Channels.HasValue) label += $" {at.Channels}ch";
            if (at.Default) label += " \u2605";

            // Bold the currently active audio track
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
