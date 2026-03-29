using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Player;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

/// <summary>COM interface to get raw byte pointer from IBuffer (bypasses slow managed CopyTo).</summary>
[ComImport]
[Guid("905a0fef-bc53-11df-8c49-001e4fc686da")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IBufferByteAccess
{
    void Buffer(out IntPtr buffer);
}

public sealed partial class PlayerPage : Page
{
    private PlayerViewModel? _vm;
    private MpvPlayer? _mpvPlayer;
    private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? _frameBitmap;
    private bool _isDisposed;
    private string? _contentId;
    private bool _isFullscreen;
    private bool _statsVisible;
    private double _resumePosition;
    private bool _suppressSeek;
    private bool _isMuted;
    private bool _switchingVersion; // suppresses PlaybackEnded during version/audio switch

    private DispatcherTimer? _uiTimer;
    private DispatcherTimer? _hideTimer;

    public PlayerPage()
    {
        this.InitializeComponent();
    }

    // -- Page lifecycle -------------------------------------------------------

    private bool _playFromStart;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string param && !string.IsNullOrEmpty(param))
        {
            if (param.EndsWith("|fromstart"))
            {
                _contentId = param.Replace("|fromstart", "");
                _playFromStart = true;
            }
            else
            {
                _contentId = param;
            }
        }
    }

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

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        CleanupAsync();
    }

    private async void CleanupAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _uiTimer?.Stop();
        _hideTimer?.Stop();

        // Dispose MpvPlayer (stops rendering)
        if (_mpvPlayer != null)
        {
            _mpvPlayer.Dispose();
            _mpvPlayer = null;
        }

        // Restore window state
        if (App.MainWindowInstance is MainWindow mw)
        {
            if (_isFullscreen)
            {
                // Restore saved style + rect in one shot -- no flash
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);
                SetWindowLongPtrW(hwnd, GWL_STYLE, _savedStyle);
                SetWindowPos(hwnd, IntPtr.Zero,
                    _savedRect.Left, _savedRect.Top,
                    _savedRect.Right - _savedRect.Left,
                    _savedRect.Bottom - _savedRect.Top,
                    SWP_NOACTIVATE | SWP_NOZORDER);
                _isFullscreen = false;
            }
            mw.RestoreMainPane();
        }

        // Stop playback session on the server (non-blocking)
        if (_vm?.Manager != null)
        {
            try { await _vm.Manager.StopSessionAsync(); }
            catch { }
        }
    }

    // -- Playback initialization ----------------------------------------------

    private async Task InitializePlaybackAsync(string contentId)
    {
        try
        {
            // 1. Hide nav for immersive experience
            if (App.MainWindowInstance is MainWindow mw)
                mw.HideMainNavigation();

            // Create view model via DI
            _vm = App.Services.GetRequiredService<PlayerViewModel>();
            _vm.IsLoading = true;

            // 2. Get watch detail
            var watchDetail = await _vm.Manager.GetWatchDetailAsync(contentId);

            // Build title
            if (watchDetail.SeasonNumber.HasValue && watchDetail.EpisodeNumber.HasValue)
                _vm.Title = $"{watchDetail.SeriesTitle ?? watchDetail.Title} - S{watchDetail.SeasonNumber:D2}E{watchDetail.EpisodeNumber:D2}";
            else
                _vm.Title = watchDetail.Title;

            // Populate versions
            foreach (var v in watchDetail.Versions)
                _vm.Versions.Add(v);

            // 3. Select best version
            var bestVersion = _vm.Manager.SelectBestVersion(watchDetail.Versions);
            if (bestVersion == null)
            {
                ShowError("No playable version found.");
                return;
            }

            _vm.Resolution = bestVersion.Resolution;

            // Determine start position from user data (resume support)
            double startPosition = 0;
            if (!_playFromStart && watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
                startPosition = watchDetail.UserData.PositionSeconds!.Value;

            _resumePosition = startPosition;

            // 4. Start session on the server
            var session = await _vm.Manager.StartSessionAsync(bestVersion.FileId, startPosition, forceStartPosition: _playFromStart);
            _vm.PlayMethod = session.PlayMethod;

            // If the server returned a position (from saved progress), use that
            // but NOT when the user explicitly chose "play from start"
            if (!_playFromStart && session.Position > 0 && _resumePosition == 0)
                _resumePosition = session.Position;

            // 5. Get the stream URL (may be overridden by HLS transcode below)
            var streamUrl = _vm.Manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ShowError("No stream URL available.");
                return;
            }

            // 6. HLS transcode fallback: if play_method is "transcode", start transcode session
            if (session.PlayMethod == "transcode")
            {
                try
                {
                    var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
                    var transcodeResponse = await playbackApi.StartTranscodeAsync(new TranscodeStartRequest
                    {
                        SessionId = session.SessionId,
                        SeekSeconds = startPosition,
                        TargetResolution = bestVersion.Resolution,
                        TargetCodecVideo = "h264",
                        TargetCodecAudio = "aac",
                        TargetBitrateKbps = 8000,
                        SegmentDuration = 2,
                        SubtitleTrackIndex = -1,
                        SubtitleBurnIn = false
                    });

                    // Build HLS manifest URL
                    var apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
                    var baseUrl = apiClient.BaseUrl;
                    var manifestPath = transcodeResponse.ManifestUrl;
                    if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                        manifestPath = "/api/v1" + manifestPath;
                    streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
                    // HLS endpoints don't need auth -- session UUID is the token

                    _resumePosition = transcodeResponse.PlayerStartSeconds;

                    LogToFile("player_transcode.txt",
                        $"TranscodeStatus: {transcodeResponse.Status}\nManifestURL: {streamUrl}\nStartSeconds: {transcodeResponse.PlayerStartSeconds}\nSwitchedFile: {transcodeResponse.SwitchedFileId}");
                }
                catch (Exception ex)
                {
                    LogToFile("player_transcode_error.txt", ex.ToString());
                    // Fall through to try direct stream URL as a last resort
                }
            }

            // Log init info for debugging
            LogToFile("player_init.txt",
                $"StreamURL: {streamUrl}\nPlayMethod: {_vm.PlayMethod}\nResolution: {_vm.Resolution}\nSession: {_vm.Manager.SessionId}\nResumePos: {_resumePosition}");

            // 7. Initialize mpv with SW render (GPU decodes via d3d11va-copy, renders to buffer)
            // Cap render output at 1080p — source is still decoded at native resolution (4K etc.)
            // by the GPU, mpv just scales the render output. The XAML Image upscales to display.
            // This keeps the frame buffer at ~8MB instead of ~33MB for 4K, making copies fast.
            _mpvPlayer = new MpvPlayer();
            int maxRenderW = 1920;
            int maxRenderH = 1080;
            var rawW = (int)Math.Max(VideoFrame.ActualWidth, 1280);
            var rawH = (int)Math.Max(VideoFrame.ActualHeight, 720);
            var renderW = Math.Min(rawW, maxRenderW);
            var renderH = Math.Min(rawH, maxRenderH);
            _mpvPlayer.Initialize(renderW, renderH);

            _frameBitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(renderW, renderH);
            VideoFrame.Source = _frameBitmap;

            _mpvPlayer.FrameReady += OnFrameReady;

            // Don't resize the render target when the window resizes — keep it capped at 1080p
            // The XAML Image with Stretch="Uniform" handles the display scaling

            // 8. Subscribe to mpv events
            _mpvPlayer.FileLoaded += () => DispatcherQueue?.TryEnqueue(() =>
            {
                if (_isDisposed || _vm == null) return;

                _vm.IsLoading = false;
                LoadingOverlay.Visibility = Visibility.Collapsed;

                if (_resumePosition > 0)
                {
                    _mpvPlayer?.Seek(_resumePosition);
                    _resumePosition = 0;
                }
            });

            _mpvPlayer.PlaybackEnded += () => DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isDisposed && !_switchingVersion) NavigateBack();
            });

            _mpvPlayer.Error += (msg) => DispatcherQueue?.TryEnqueue(() =>
            {
                LogToFile("mpv_error.txt", msg);
            });

            _mpvPlayer.PositionChanged += (pos) => { /* handled by UI timer */ };
            _mpvPlayer.DurationChanged += (dur) => { /* handled by UI timer */ };

            // 9. Load subtitles via mpv
            LoadSubtitlesMpv();

            // 10. Build stream URL with auth token for mpv
            var apiClientForAuth = App.Services.GetRequiredService<ContinuumApiClient>();
            var token = apiClientForAuth.AccessToken;
            var authHeader = token != null ? $"Bearer {token}" : null;

            // For direct/remux, append token as query param since mpv handles HTTP
            if (session.PlayMethod != "transcode" && token != null)
            {
                streamUrl += (streamUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
            }

            // 11. Load and play
            _mpvPlayer.LoadFile(streamUrl, session.PlayMethod == "transcode" ? null : authHeader);
            _mpvPlayer.Play();

            // 12. Set title and playback info, populate flyouts
            TitleText.Text = _vm.Title;
            UpdatePlaybackInfo();
            PopulateQualityFlyout();
            PopulateSubtitleFlyout();
            PopulateAudioFlyout();

            // 13. Start UI update timer (250ms)
            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            // 14. Start controls auto-hide timer (3s)
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hideTimer.Tick += HideTimer_Tick;
            _hideTimer.Start();
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            ShowError($"Failed to start playback: {ex.Message}");
        }
    }

    // -- Frame rendering (double buffer + native memcpy) ----------------------

    private byte[]? _snapBuffer;
    private int _snapW, _snapH, _snapStride;
    private volatile bool _snapReady;
    private volatile bool _uiBusy;

    private void OnFrameReady(byte[] buffer, int width, int height, int stride)
    {
        // Skip if UI thread is still processing previous frame
        if (_uiBusy) return;

        // Immediate copy on render thread into our own buffer
        int size = stride * height;
        if (_snapBuffer == null || _snapBuffer.Length < size)
            _snapBuffer = new byte[size];
        Buffer.BlockCopy(buffer, 0, _snapBuffer, 0, size);
        _snapW = width;
        _snapH = height;
        _snapStride = stride;
        _snapReady = true;

        DispatcherQueue?.TryEnqueue(PresentFrame);
    }

    private void PresentFrame()
    {
        if (_isDisposed || !_snapReady || _snapBuffer == null) return;
        _uiBusy = true;
        _snapReady = false;

        try
        {
            int w = _snapW, h = _snapH, srcStride = _snapStride;
            int dstStride = w * 4;

            if (_frameBitmap == null || _frameBitmap.PixelWidth != w || _frameBitmap.PixelHeight != h)
            {
                _frameBitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(w, h);
                VideoFrame.Source = _frameBitmap;
            }

            // Copy frame data into WriteableBitmap pixel buffer
            var pixelBuffer = _frameBitmap.PixelBuffer;

            if (srcStride == dstStride)
            {
                int copyLen = Math.Min(dstStride * h, (int)pixelBuffer.Length);
                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions
                    .CopyTo(_snapBuffer!, 0, pixelBuffer, 0, copyLen);
            }
            else
            {
                int rowBytes = Math.Min(srcStride, dstStride);
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions
                        .CopyTo(_snapBuffer!, y * srcStride, pixelBuffer, (uint)(y * dstStride), rowBytes);
                }
            }

            _frameBitmap.Invalidate();
        }
        finally
        {
            _uiBusy = false;
        }
    }

    // -- UI update timer (position, seek bar, play/pause icon, skip markers) --

    private void UiTimer_Tick(object? sender, object e)
    {
        if (_isDisposed || _mpvPlayer == null || _vm == null) return;

        var pos = _mpvPlayer.Position;
        var dur = _mpvPlayer.Duration;

        // Update seek slider without triggering seek
        _suppressSeek = true;
        if (dur > 0) SeekSlider.Maximum = dur;
        SeekSlider.Value = pos;
        _suppressSeek = false;

        // Update time labels
        PositionText.Text = PlayerViewModel.FormatTime(pos);
        DurationText.Text = PlayerViewModel.FormatTime(dur);

        // Update play/pause icon based on actual playback state
        PlayPauseIcon.Glyph = _mpvPlayer.IsPaused ? "\uE768" : "\uE769";

        // Report progress to server
        _vm.Manager.UpdatePosition(pos, _mpvPlayer.IsPaused);

        // Check skip markers
        UpdateSkipButtons(pos);

        // Update stats if visible
        if (_statsVisible) UpdateStats();
    }

    private void UpdateSkipButtons(double pos)
    {
        if (_vm == null) return;

        var intro = _vm.Manager.WatchDetail?.Intro;
        bool showIntro = intro != null && pos >= intro.Start && pos < intro.End;
        SkipIntroButton.Visibility = showIntro ? Visibility.Visible : Visibility.Collapsed;

        var credits = _vm.Manager.WatchDetail?.Credits;
        bool showCredits = credits != null && pos >= credits.Start && pos < credits.End;
        SkipCreditsButton.Visibility = showCredits ? Visibility.Visible : Visibility.Collapsed;
    }

    // -- Controls auto-hide ---------------------------------------------------

    private void HideTimer_Tick(object? sender, object e)
    {
        _hideTimer?.Stop();

        // Only hide if playing (keep visible when paused)
        if (_mpvPlayer != null && !_mpvPlayer.IsPaused)
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
        if (_isDisposed || _mpvPlayer == null) return;

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
        _mpvPlayer?.TogglePause();
    }

    private void SeekRelative(double seconds)
    {
        if (_mpvPlayer == null) return;

        var newPos = _mpvPlayer.Position + seconds;
        newPos = Math.Max(0, Math.Min(newPos, _mpvPlayer.Duration));
        _mpvPlayer.Seek(newPos);
    }

    private void AdjustVolume(double delta)
    {
        var newVolume = Math.Max(0, Math.Min(100, VolumeSlider.Value + delta));
        VolumeSlider.Value = newVolume;
    }

    private void ToggleMute()
    {
        if (_mpvPlayer == null) return;

        _isMuted = !(_mpvPlayer.GetMute());
        _mpvPlayer.SetMute(_isMuted);
        UpdateVolumeIcon();
    }

    private void ToggleFullscreen()
    {
        SetFullscreen(!_isFullscreen);
    }

    private bool _wasMaximizedBeforeFullscreen;
    private long _savedStyle;
    private RECT _savedRect;

    // Win32 interop for reliable fullscreen (WinUI FullScreen presenter is buggy from Maximized)
    private const int GWL_STYLE = -16;
    private const long WS_OVERLAPPEDWINDOW = 0x00CF0000L;

    [DllImport("user32.dll")] private static extern long GetWindowLongPtrW(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern long SetWindowLongPtrW(IntPtr hWnd, int nIndex, long dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    private void SetFullscreen(bool fs)
    {
        var mw = App.MainWindowInstance;
        if (mw == null) return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);

        if (fs)
        {
            // Save current window style and position
            _savedStyle = GetWindowLongPtrW(hwnd, GWL_STYLE);
            GetWindowRect(hwnd, out _savedRect);
            _wasMaximizedBeforeFullscreen = (_savedStyle & 0x01000000L /* WS_MAXIMIZE */) != 0;

            // Get the full monitor rect (including taskbar area)
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfoW(monitor, ref mi);

            // Strip window chrome and resize to full monitor in one step
            SetWindowLongPtrW(hwnd, GWL_STYLE, _savedStyle & ~WS_OVERLAPPEDWINDOW);
            SetWindowPos(hwnd, HWND_TOP,
                mi.rcMonitor.Left, mi.rcMonitor.Top,
                mi.rcMonitor.Right - mi.rcMonitor.Left,
                mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                SWP_NOACTIVATE);
        }
        else
        {
            // Restore window chrome and position in one step -- no intermediate state
            SetWindowLongPtrW(hwnd, GWL_STYLE, _savedStyle);
            SetWindowPos(hwnd, IntPtr.Zero,
                _savedRect.Left, _savedRect.Top,
                _savedRect.Right - _savedRect.Left,
                _savedRect.Bottom - _savedRect.Top,
                SWP_NOACTIVATE | SWP_NOZORDER);
        }

        _isFullscreen = fs;
        FullscreenIcon.Glyph = fs ? "\uE73F" : "\uE740";
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
        if (_mpvPlayer == null) return;

        _mpvPlayer.SetVolume(e.NewValue);
        if (_isMuted && e.NewValue > 0)
        {
            _isMuted = false;
            _mpvPlayer.SetMute(false);
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
        if (_mpvPlayer == null || _vm?.IntroEnd == null) return;
        _mpvPlayer.Seek(_vm.IntroEnd.Value);
    }

    private void SkipCredits_Click(object sender, RoutedEventArgs e)
    {
        if (_mpvPlayer == null || _vm?.CreditsEnd == null) return;
        _mpvPlayer.Seek(_vm.CreditsEnd.Value);
    }

    // -- Seek slider interaction (suppressed feedback loop pattern) ------------

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // If the change came from our programmatic update in the timer, ignore it
        if (_suppressSeek || _mpvPlayer == null) return;

        // User clicked or dragged the slider -- seek to the new position
        _mpvPlayer.Seek(e.NewValue);
        ShowControls();
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
        if (_mpvPlayer == null || _vm == null) return;

        var currentPos = _mpvPlayer.Position;

        _switchingVersion = true;

        // Stop mpv's current playback cleanly (releases HTTP connection, won't trigger NavigateBack)
        _mpvPlayer.Stop();

        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            // Now safely stop the server session
            try { await _vm.Manager.StopSessionAsync(); }
            catch { /* Session may already be gone */ }

            // Start new session with the selected version
            var session = await _vm.Manager.StartSessionAsync(version.FileId, currentPos);
            _vm.PlayMethod = session.PlayMethod;
            _vm.Resolution = version.Resolution;
            UpdatePlaybackInfo();
            PopulateQualityFlyout();
            PopulateSubtitleFlyout();
            PopulateAudioFlyout();

            var streamUrl = _vm.Manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ShowError("No stream URL for selected version.");
                return;
            }

            // Handle transcode fallback on version switch
            if (session.PlayMethod == "transcode")
            {
                try
                {
                    var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
                    var transcodeResponse = await playbackApi.StartTranscodeAsync(new TranscodeStartRequest
                    {
                        SessionId = session.SessionId,
                        SeekSeconds = currentPos,
                        TargetResolution = version.Resolution,
                        TargetCodecVideo = "h264",
                        TargetCodecAudio = "aac",
                        TargetBitrateKbps = 8000,
                        SegmentDuration = 2,
                        SubtitleTrackIndex = -1,
                        SubtitleBurnIn = false
                    });

                    var apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
                    var baseUrl = apiClient.BaseUrl;
                    var manifestPath = transcodeResponse.ManifestUrl;
                    if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                        manifestPath = "/api/v1" + manifestPath;
                    streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
                    currentPos = transcodeResponse.PlayerStartSeconds;
                }
                catch (Exception ex)
                {
                    LogToFile("player_transcode_error.txt", ex.ToString());
                }
            }

            // Build auth for mpv
            var apiClientForAuth = App.Services.GetRequiredService<ContinuumApiClient>();
            var token = apiClientForAuth.AccessToken;
            var authHeader = token != null ? $"Bearer {token}" : null;

            if (session.PlayMethod != "transcode" && token != null)
            {
                streamUrl += (streamUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
            }

            // Set resume position so FileLoaded handler seeks to it
            _resumePosition = currentPos;
            _mpvPlayer.LoadFile(streamUrl, session.PlayMethod == "transcode" ? null : authHeader);
            _mpvPlayer.Play();
            _switchingVersion = false;
        }
        catch (Exception ex)
        {
            _switchingVersion = false;
            LogToFile("player_quality_switch_error.txt", ex.ToString());
            ShowError($"Failed to switch quality: {ex.Message}");
        }
    }

    // -- Subtitle selection ---------------------------------------------------

    private void LoadSubtitlesMpv()
    {
        if (_vm?.Manager.CurrentSession == null || _mpvPlayer == null) return;

        var subtitleUrls = _vm.Manager.GetSubtitleUrls();
        foreach (var (track, fullUrl) in subtitleUrls)
        {
            var codec = track.Codec?.ToLowerInvariant() ?? "";
            if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
                continue; // bitmap subs need burn-in via transcode

            var label = !string.IsNullOrEmpty(track.Label) ? track.Label : track.Language ?? "Unknown";
            _mpvPlayer.AddSubtitle(fullUrl, label, track.Language);
        }
    }

    private void PopulateSubtitleFlyout()
    {
        SubtitleFlyout.Items.Clear();

        // "Off" option to disable subtitles
        var offItem = new MenuFlyoutItem { Text = "Off" };
        offItem.Click += (_, _) =>
        {
            _mpvPlayer?.SetSubtitleTrack(0); // 0 disables subtitles in mpv
        };
        SubtitleFlyout.Items.Add(offItem);
        SubtitleFlyout.Items.Add(new MenuFlyoutSeparator());

        // Subtitle tracks from the session
        var subtitleUrls = _vm?.Manager.GetSubtitleUrls() ?? [];
        int mpvTrackIndex = 1; // mpv subtitle tracks are 1-based
        for (int idx = 0; idx < subtitleUrls.Count; idx++)
        {
            var (track, _) = subtitleUrls[idx];
            var codec = track.Codec?.ToLowerInvariant() ?? "";
            if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
                continue; // skip bitmap subs (they were not loaded)

            // Build a readable label: "English", "Spanish - Latin American", etc.
            var langName = LanguageCodeToName(track.Language);
            var trackTitle = track.Label;

            // If the title is just the codec name (e.g., "SUBRIP"), ignore it
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
            item.Click += (_, _) => _mpvPlayer?.SetSubtitleTrack(capturedIndex);
            SubtitleFlyout.Items.Add(item);

            mpvTrackIndex++;
        }
    }

    // -- Audio track switching ------------------------------------------------

    private void PopulateAudioFlyout()
    {
        AudioFlyout.Items.Clear();

        var currentSession = _vm?.Manager.CurrentSession;
        if (currentSession == null || _vm == null) return;

        // Get audio tracks from the version that matches the current session
        var version = _vm.Versions.FirstOrDefault(v => v.FileId == currentSession.MediaFileId);
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
            item.Click += async (_, _) => await SwitchAudioTrackAsync(trackIndex);
            AudioFlyout.Items.Add(item);
        }
    }

    private async Task SwitchAudioTrackAsync(int trackIndex)
    {
        if (_mpvPlayer == null || _vm == null) return;

        var currentPos = _mpvPlayer.Position;
        _switchingVersion = true;
        _mpvPlayer.Stop();

        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
            var response = await playbackApi.ChangeAudioTrackAsync(
                _vm.Manager.SessionId!, trackIndex, currentPos);

            // Build the new stream URL
            var apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
            var baseUrl = apiClient.BaseUrl;
            var token = apiClient.AccessToken;
            var streamPath = response.StreamUrl;
            if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
                streamPath = "/api/v1" + streamPath;
            var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
            if (token != null)
                url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

            // Update play method in case it changed
            _vm.PlayMethod = response.PlayMethod;
            UpdatePlaybackInfo();
            PopulateAudioFlyout();

            // Reload stream at the current position via mpv
            _resumePosition = currentPos;
            var authHeader = token != null ? $"Bearer {token}" : null;
            _mpvPlayer.LoadFile(url, authHeader);
            _mpvPlayer.Play();
            _switchingVersion = false;
        }
        catch (Exception ex)
        {
            _switchingVersion = false;
            LogToFile("player_audio_switch_error.txt", ex.ToString());
            ShowError($"Failed to switch audio: {ex.Message}");
        }
    }

    // -- Stats overlay --------------------------------------------------------

    private void UpdateStats()
    {
        if (_vm == null) return;

        var currentSession = _vm.Manager.CurrentSession;
        var version = _vm.Versions.FirstOrDefault(v => v.FileId == currentSession?.MediaFileId)
                   ?? _vm.Versions.FirstOrDefault();

        StatsResolution.Text = $"Resolution:  {_vm.Resolution}";
        StatsCodec.Text = $"Video:       {currentSession?.PlaybackInfo?.VideoCodec ?? version?.CodecVideo ?? "?"}\nAudio:       {currentSession?.PlaybackInfo?.AudioCodec ?? version?.CodecAudio ?? "?"}";
        StatsPlayMethod.Text = $"Play Method: {_vm.PlayMethod}";
        StatsBitrate.Text = $"Bitrate:     {(version?.Bitrate > 0 ? $"{version.Bitrate / 1000.0:F1} Mbps" : "?")}";

        var hdr = "SDR";
        if (version is { Hdr: true })
        {
            var codec = (version.CodecVideo ?? "").ToLowerInvariant();
            hdr = codec.Contains("dovi") || codec.Contains("dolby") ? "Dolby Vision" : "HDR10";
        }
        StatsHdr.Text = $"HDR:         {hdr}";
        StatsPosition.Text = $"Position:    {PlayerViewModel.FormatTime(_mpvPlayer?.Position ?? 0)} / {PlayerViewModel.FormatTime(_mpvPlayer?.Duration ?? 0)}";
        StatsSession.Text = $"Session:     {_vm.Manager.SessionId ?? "?"}";
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
        // Just navigate back — Page_Unloaded will trigger CleanupAsync
        NavigateBack();
    }

    private void NavigateBack()
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
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

    private static string LanguageCodeToName(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "Unknown";
        return code.ToLowerInvariant() switch
        {
            "eng" or "en" => "English",
            "spa" or "es" => "Spanish",
            "fre" or "fra" or "fr" => "French",
            "ger" or "deu" or "de" => "German",
            "ita" or "it" => "Italian",
            "por" or "pt" => "Portuguese",
            "rus" or "ru" => "Russian",
            "jpn" or "ja" => "Japanese",
            "kor" or "ko" => "Korean",
            "chi" or "zho" or "zh" => "Chinese",
            "ara" or "ar" => "Arabic",
            "hin" or "hi" => "Hindi",
            "tur" or "tr" => "Turkish",
            "pol" or "pl" => "Polish",
            "dut" or "nld" or "nl" => "Dutch",
            "swe" or "sv" => "Swedish",
            "dan" or "da" => "Danish",
            "fin" or "fi" => "Finnish",
            "nob" or "nor" or "no" => "Norwegian",
            "cze" or "ces" or "cs" => "Czech",
            "hun" or "hu" => "Hungarian",
            "rum" or "ron" or "ro" => "Romanian",
            "bul" or "bg" => "Bulgarian",
            "hrv" or "hr" => "Croatian",
            "gre" or "ell" or "el" => "Greek",
            "heb" or "he" => "Hebrew",
            "tha" or "th" => "Thai",
            "vie" or "vi" => "Vietnamese",
            "ind" or "id" => "Indonesian",
            "may" or "msa" or "ms" => "Malay",
            "fil" or "tl" => "Filipino",
            "ukr" or "uk" => "Ukrainian",
            "cat" or "ca" => "Catalan",
            "baq" or "eus" or "eu" => "Basque",
            "glg" or "gl" => "Galician",
            _ => code.ToUpperInvariant()
        };
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
