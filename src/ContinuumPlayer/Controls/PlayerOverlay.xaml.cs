using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Services;

namespace ContinuumPlayer.Controls;

/// <summary>COM interface to get raw byte pointer from IBuffer (bypasses slow managed CopyTo).</summary>
[ComImport]
[Guid("905a0fef-bc53-11df-8c49-001e4fc686da")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IBufferByteAccess
{
    void Buffer(out IntPtr buffer);
}

public sealed partial class PlayerOverlay : UserControl
{
    private readonly PlayerService _playerService;
    private Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource? _bitmapSource;

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

        // Subscribe to frame rendering
        _playerService.FrameReady += OnFrameReady;

        // Subscribe to content/playback events
        _playerService.ContentLoaded += OnContentLoaded;
        _playerService.PlaybackEnded += OnPlaybackEnded;

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

        // Update playback info display
        UpdatePlaybackInfo();

        // Cap render size at 1080p
        _playerService.Mpv?.UpdateRenderSize(1920, 1080);

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
        _playerService.FrameReady -= OnFrameReady;
        _playerService.ContentLoaded -= OnContentLoaded;
        _playerService.PlaybackEnded -= OnPlaybackEnded;

        // Stop timers
        _uiTimer?.Stop();
        _uiTimer = null;
        _hideTimer?.Stop();
        _hideTimer = null;
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

    // ── Frame rendering (double buffer + native memcpy) ──────────────────

    private byte[]? _snapBuffer;
    private int _snapW, _snapH, _snapStride;
    private volatile bool _snapReady;
    private volatile bool _uiBusy;

    private void OnFrameReady(byte[] buffer, int width, int height, int stride)
    {
        if (_uiBusy) return;

        int size = stride * height;
        if (_snapBuffer == null || _snapBuffer.Length < size)
            _snapBuffer = new byte[size];
        System.Buffer.BlockCopy(buffer, 0, _snapBuffer, 0, size);
        _snapW = width;
        _snapH = height;
        _snapStride = stride;
        _snapReady = true;

        DispatcherQueue?.TryEnqueue(PresentFrameAsync);
    }

    private async void PresentFrameAsync()
    {
        if (!_isActive || !_snapReady || _snapBuffer == null) return;
        _uiBusy = true;
        _snapReady = false;

        try
        {
            int w = _snapW, h = _snapH, stride = _snapStride;
            int copyLen = stride * h;

            // Create SoftwareBitmap and copy frame data into it
            var bitmap = new Windows.Graphics.Imaging.SoftwareBitmap(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, w, h,
                Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
            bitmap.CopyFromBuffer(_snapBuffer.AsBuffer());

            // Ensure source exists
            if (_bitmapSource == null)
            {
                _bitmapSource = new Microsoft.UI.Xaml.Media.Imaging.SoftwareBitmapSource();
                VideoFrame.Source = _bitmapSource;
            }

            // SetBitmapAsync does the GPU upload on a background thread — doesn't block UI
            await _bitmapSource.SetBitmapAsync(bitmap);
            bitmap.Dispose();
        }
        finally
        {
            _uiBusy = false;
        }
    }

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

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // If the change came from our programmatic update in the timer, ignore it
        if (_suppressSeek || _playerService.Mpv == null) return;

        // User clicked or dragged the slider -- seek to the new position
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
