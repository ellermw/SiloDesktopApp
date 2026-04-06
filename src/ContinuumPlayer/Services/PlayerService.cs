// src/ContinuumPlayer/Services/PlayerService.cs
using System.Runtime.InteropServices;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Player;

namespace ContinuumPlayer.Services;

public enum PlayerState { Idle, Expanded, Fullscreen, Minimized }

public class PlayerService : IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly ContinuumApiClient _apiClient;

    private MpvPlayer? _mpv;
    private MpvVideoWindow? _videoWindow;
    private PlaybackManager? _playbackManager;
    private bool _switchingContent;

    public PlayerService(PlaybackApi playbackApi, CatalogApi catalogApi, AuthService authService, ContinuumApiClient apiClient)
    {
        _playbackApi = playbackApi;
        _catalogApi = catalogApi;
        _authService = authService;
        _apiClient = apiClient;
    }

    // ── State ────────────────────────────────────────────────────────────

    public PlayerState State { get; private set; } = PlayerState.Idle;

    public string? ContentId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Subtitle { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool IsPaused { get; private set; } = true;
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string PlayMethod { get; private set; } = "";
    public string Resolution { get; private set; } = "";
    public double Volume { get; set; } = 100;
    public bool IsMuted { get; set; }

    public MpvPlayer? Mpv => _mpv;
    public PlaybackManager? Manager => _playbackManager;
    public WatchDetailResponse? WatchDetail => _playbackManager?.WatchDetail;
    public List<FileVersion> Versions { get; private set; } = [];

    // ── Events ───────────────────────────────────────────────────────────

    public event Action<PlayerState>? StateChanged;
    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action? PlaybackEnded;
    public event Action? ContentLoaded; // fired when file is loaded and decoding starts

    // ── State transitions ────────────────────────────────────────────────

    public void SetState(PlayerState newState)
    {
        var threadId = Environment.CurrentManagedThreadId;
        LogToFile("state_trace.txt", $"SetState: {State} -> {newState} (thread={threadId})");

        if (State == newState) return;
        State = newState;

        if (newState == PlayerState.Expanded || newState == PlayerState.Fullscreen)
        {
            _videoWindow?.Show();
            _mpv?.SendScriptMessage("osc-set-visibility", "true");
        }
        else if (newState == PlayerState.Minimized)
        {
            _mpv?.SendScriptMessage("osc-set-visibility", "false");
            PositionVideoForMiniBar();
        }
        else
            _videoWindow?.Hide();

        try
        {
            StateChanged?.Invoke(newState);
            LogToFile("state_trace.txt", $"StateChanged invoked OK for {newState}");
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"StateChanged THREW for {newState}: {ex}");
        }
    }

    public void Minimize()
    {
        if (State == PlayerState.Expanded || State == PlayerState.Fullscreen)
        {
            if (State == PlayerState.Fullscreen)
                ExitFullscreen();
            SetState(PlayerState.Minimized);
        }
    }

    public void Expand()
    {
        if (State == PlayerState.Minimized)
            SetState(PlayerState.Expanded);
    }

    // ── Fullscreen (Win32) ───────────────────────────────────────────────

    private const int GWL_STYLE = -16;
    private const long WS_OVERLAPPEDWINDOW = 0x00CF0000L;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

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

    private long _savedStyle;
    private RECT _savedRect;

    public void EnterFullscreen()
    {
        if (State != PlayerState.Expanded) return;
        var mw = App.MainWindowInstance;
        if (mw == null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);

        _savedStyle = GetWindowLongPtrW(hwnd, GWL_STYLE);
        GetWindowRect(hwnd, out _savedRect);

        var monitor = MonitorFromWindow(hwnd, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref mi);

        SetWindowLongPtrW(hwnd, GWL_STYLE, _savedStyle & ~WS_OVERLAPPEDWINDOW);
        // HWND_TOPMOST (-1) puts the window above the taskbar
        SetWindowPos(hwnd, (IntPtr)(-1),
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left,
            mi.rcMonitor.Bottom - mi.rcMonitor.Top,
            SWP_NOACTIVATE);

        SetState(PlayerState.Fullscreen);
    }

    public void ExitFullscreen()
    {
        if (State != PlayerState.Fullscreen) return;
        var mw = App.MainWindowInstance;
        if (mw == null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);

        SetWindowLongPtrW(hwnd, GWL_STYLE, _savedStyle);
        // HWND_NOTOPMOST (-2) drops back below the taskbar
        SetWindowPos(hwnd, (IntPtr)(-2),
            _savedRect.Left, _savedRect.Top,
            _savedRect.Right - _savedRect.Left,
            _savedRect.Bottom - _savedRect.Top,
            SWP_NOACTIVATE);

        SetState(PlayerState.Expanded);
    }

    public void ToggleFullscreen()
    {
        if (State == PlayerState.Fullscreen)
            ExitFullscreen();
        else if (State == PlayerState.Expanded)
            EnterFullscreen();
    }

    // ── Playback ─────────────────────────────────────────────────────────

    public async Task PlayAsync(string contentId, bool fromStart = false, int? fileId = null)
    {
        LogToFile("state_trace.txt", $"PlayAsync called: contentId={contentId} fromStart={fromStart} State={State} IsLoading={IsLoading}");

        // Stop any existing session first (prevents HTTP 400 from server)
        if (_playbackManager != null)
        {
            try
            {
                _mpv?.Stop();
                await _playbackManager.StopSessionAsync();
            }
            catch { }
            _playbackManager.Dispose();
            _playbackManager = null;
            // Give server time to process the session stop
            await Task.Delay(500);
        }

        ErrorMessage = null;
        IsLoading = true;
        ContentId = contentId;
        _switchingContent = true; // Suppress stale PlaybackEnded from previous _mpv.Stop()

        // Show loading indicator on the main window
        App.MainWindowInstance?.ShowLoadingOverlay();

        try
        {
            // Create PlaybackManager for this session
            _playbackManager = new PlaybackManager(_playbackApi, _catalogApi, _authService, _apiClient);

            // Get watch detail (retry once if server hasn't processed previous session stop)
            WatchDetailResponse watchDetail;
            try
            {
                watchDetail = await _playbackManager.GetWatchDetailAsync(contentId);
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                await Task.Delay(1000);
                watchDetail = await _playbackManager.GetWatchDetailAsync(contentId);
            }

            // Build title
            if (watchDetail.SeasonNumber.HasValue && watchDetail.EpisodeNumber.HasValue)
            {
                Title = $"{watchDetail.SeriesTitle ?? watchDetail.Title} - S{watchDetail.SeasonNumber:D2}E{watchDetail.EpisodeNumber:D2}";
                Subtitle = watchDetail.Title; // episode title
            }
            else
            {
                Title = watchDetail.Title;
                Subtitle = watchDetail.Year > 0 ? watchDetail.Year.ToString() : null;
            }

            // Versions
            Versions = watchDetail.Versions.ToList();

            // Select version: use specified fileId if provided, otherwise pick best
            FileVersion? bestVersion = null;
            var versions = watchDetail.Versions ?? new List<FileVersion>();
            if (fileId.HasValue)
                bestVersion = versions.FirstOrDefault(v => v.FileId == fileId.Value);
            bestVersion ??= _playbackManager.SelectBestVersion(versions);
            if (bestVersion == null)
            {
                ErrorMessage = "No playable version found.";
                IsLoading = false;
                return;
            }

            Resolution = bestVersion.Resolution;

            // Determine start position
            double startPosition = 0;
            if (!fromStart && watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
                startPosition = watchDetail.UserData.PositionSeconds!.Value;
            LogToFile("state_trace.txt", $"Resume logic: fromStart={fromStart} userPos={watchDetail.UserData?.PositionSeconds} played={watchDetail.UserData?.Played} → startPosition={startPosition}");

            // Start server session
            var session = await _playbackManager.StartSessionAsync(bestVersion.FileId, startPosition, forceStartPosition: fromStart);
            PlayMethod = session.PlayMethod;

            if (!fromStart && session.Position > 0 && startPosition == 0)
            {
                startPosition = session.Position;
                LogToFile("state_trace.txt", $"Using server session position: {session.Position:F1}");
            }

            // Build stream URL
            var streamUrl = _playbackManager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ErrorMessage = "No stream URL available.";
                IsLoading = false;
                return;
            }

            // HLS transcode fallback
            if (session.PlayMethod == "transcode")
            {
                try
                {
                    var transcodeResponse = await _playbackApi.StartTranscodeAsync(new TranscodeStartRequest
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

                    var baseUrl = _apiClient.BaseUrl;
                    var manifestPath = transcodeResponse.ManifestUrl;
                    if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                        manifestPath = "/api/v1" + manifestPath;
                    streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
                    startPosition = transcodeResponse.PlayerStartSeconds;
                }
                catch (Exception ex)
                {
                    LogToFile("player_transcode_error.txt", ex.ToString());
                }
            }

            // Initialize mpv (lazy — first play only)
            if (_mpv == null)
            {
                var mainWindow = App.MainWindowInstance;
                var parentHwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);

                _videoWindow = new MpvVideoWindow();
                _videoWindow.Create(parentHwnd);
                WireVideoWindowEvents();

                _mpv = new MpvPlayer();
                _mpv.InitializeWithWindow(_videoWindow.Hwnd); // vo=gpu, zero CPU, native resolution
                _videoWindow.SetMpv(_mpv); // Forward mouse/keyboard to mpv for OSC
                WireMpvEvents();

            }

            // Set state to Expanded (shows the overlay)
            SetState(PlayerState.Expanded);

            // Build auth
            var token = _apiClient.AccessToken;
            var authHeader = token != null ? $"Bearer {token}" : null;
            if (session.PlayMethod != "transcode" && token != null)
                streamUrl += (streamUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

            // Store resume position — the FileLoaded handler will seek to it
            _resumePosition = startPosition;

            // Load and play
            LogToFile("state_trace.txt", $"LoadFile: url={streamUrl?.Substring(0, Math.Min(80, streamUrl?.Length ?? 0))}...");
            _mpv.LoadFile(streamUrl, session.PlayMethod == "transcode" ? null : authHeader);
            _mpv.Play();
            LogToFile("state_trace.txt", "Play() called");
            IsPaused = false;
            // DON'T clear _switchingContent here — mpv may still fire PlaybackEnded
            // from the previous Stop(). It's cleared in the FileLoaded handler instead.

            // Tell custom OSC the play method
            _mpv.SendScriptMessage("osc-set-play-method", session.PlayMethod ?? "direct");

            IsLoading = false;
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            ErrorMessage = $"Failed to start playback: {ex.Message}";
            IsLoading = false;
            _switchingContent = false;
            App.MainWindowInstance?.HideLoadingOverlay();

            // Clean up — hide popup window and go back to Idle
            _videoWindow?.Hide();
            if (_playbackManager != null)
            {
                try { await _playbackManager.StopSessionAsync(); } catch { }
                _playbackManager.Dispose();
                _playbackManager = null;
            }
            SetState(PlayerState.Idle);
        }
    }

    private double _resumePosition;

    private void WireMpvEvents()
    {
        if (_mpv == null) return;

        _mpv.PositionChanged += (pos) =>
        {
            Position = pos;
            _playbackManager?.UpdatePosition(pos, IsPaused);
            PositionChanged?.Invoke(pos);
        };

        _mpv.DurationChanged += (dur) =>
        {
            Duration = dur;
            DurationChanged?.Invoke(dur);
        };

        _mpv.PauseChanged += (paused) =>
        {
            IsPaused = paused;
            PauseChanged?.Invoke(paused);
        };

        _mpv.FileLoaded += () =>
        {
            IsLoading = false;
            _switchingContent = false; // Safe to receive PlaybackEnded now
            App.MainWindowInstance?.HideLoadingOverlay();
            LogToFile("state_trace.txt", "FileLoaded fired");

            ContentLoaded?.Invoke();

            // Seek to resume position FIRST — before subtitles block the thread
            if (_resumePosition > 0)
            {
                LogToFile("state_trace.txt", $"Seeking to resume position: {_resumePosition:F1}");
                _mpv?.Seek(_resumePosition);
                _resumePosition = 0;
            }
            else
            {
                LogToFile("state_trace.txt", "No resume position (starting from beginning)");
            }

            // Send media info, subtitles, and quality info to Lua OSC
            SendMediaInfoToOsc();
            SendSubtitleListToOsc();
            SendQualityInfoToOsc();

            // Load subtitles on a background thread — sub-add commands are synchronous
            // and can block for seconds (30+ HTTP requests). Running them here would
            // freeze the event loop, preventing button clicks from being processed.
            Task.Run(() => LoadSubtitles());
        };

        _mpv.PlaybackEnded += () =>
        {
            LogToFile("state_trace.txt", $"PlaybackEnded fired: _switchingContent={_switchingContent} State={State}");
            if (!_switchingContent)
            {
                LogToFile("state_trace.txt", "  → Hiding window and invoking PlaybackEnded");
                _videoWindow?.Hide();
                PlaybackEnded?.Invoke();
            }
            else
            {
                LogToFile("state_trace.txt", "  → Suppressed (switching content)");
            }
        };

        _mpv.ScriptMessageReceived += OnScriptMessage;
        _mpv.Error += (msg) => LogToFile("mpv_error.txt", msg);
    }

    // ── Content switching (version/audio) ────────────────────────────────

    public async Task SwitchVersionAsync(FileVersion version)
    {
        if (_mpv == null || _playbackManager == null) return;

        var currentPos = _mpv.Position;
        _switchingContent = true;
        // Don't call _mpv.Stop() — let current stream keep playing while we set up the new one

        try
        {
            // Run ALL network calls on background thread so UI never blocks
            var result = await Task.Run(async () =>
            {
                try { await _playbackManager.StopSessionAsync(); } catch { }
                var session = await _playbackManager.StartSessionAsync(version.FileId, currentPos);
                var streamUrl = _playbackManager.StreamUrl ?? "";

                if (session.PlayMethod == "transcode")
                {
                    try
                    {
                        var transcodeResponse = await _playbackApi.StartTranscodeAsync(new TranscodeStartRequest
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
                        var baseUrl = _apiClient.BaseUrl;
                        var manifestPath = transcodeResponse.ManifestUrl;
                        if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                            manifestPath = "/api/v1" + manifestPath;
                        streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
                        currentPos = transcodeResponse.PlayerStartSeconds;
                    }
                    catch (Exception ex) { LogToFile("player_transcode_error.txt", ex.ToString()); }
                }

                return (session, streamUrl, currentPos);
            });

            PlayMethod = result.session.PlayMethod;
            Resolution = version.Resolution;

            if (string.IsNullOrEmpty(result.streamUrl))
            {
                ErrorMessage = "No stream URL for selected version.";
                _switchingContent = false;
                return;
            }

            var token = _apiClient.AccessToken;
            var authHeader = token != null ? $"Bearer {token}" : null;
            var finalUrl = result.streamUrl;
            if (result.session.PlayMethod != "transcode" && token != null)
                finalUrl += (finalUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

            _resumePosition = result.currentPos;
            _mpv.LoadFile(finalUrl, result.session.PlayMethod == "transcode" ? null : authHeader);
            _mpv.Play();
            LoadSubtitles();
            _switchingContent = false;
        }
        catch (Exception ex)
        {
            _switchingContent = false;
            LogToFile("player_quality_switch_error.txt", ex.ToString());
            ErrorMessage = $"Failed to switch quality: {ex.Message}";
        }
    }

    public async Task SwitchAudioTrackAsync(int trackIndex)
    {
        if (_mpv == null || _playbackManager == null) return;

        var currentPos = _mpv.Position;
        _switchingContent = true;
        _mpv.Stop();
        IsLoading = true;

        try
        {
            var response = await _playbackApi.ChangeAudioTrackAsync(
                _playbackManager.SessionId!, trackIndex, currentPos);

            var baseUrl = _apiClient.BaseUrl;
            var token = _apiClient.AccessToken;
            var streamPath = response.StreamUrl;
            if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
                streamPath = "/api/v1" + streamPath;
            var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
            if (token != null)
                url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

            PlayMethod = response.PlayMethod;

            _resumePosition = currentPos;
            var authHeader = token != null ? $"Bearer {token}" : null;
            _mpv.LoadFile(url, authHeader);
            _mpv.Play();
            _switchingContent = false;
        }
        catch (Exception ex)
        {
            _switchingContent = false;
            IsLoading = false;
            LogToFile("player_audio_switch_error.txt", ex.ToString());
            ErrorMessage = $"Failed to switch audio: {ex.Message}";
        }
    }

    // ── Subtitles ────────────────────────────────────────────────────────

    private void LoadSubtitles()
    {
        if (_playbackManager?.CurrentSession == null || _mpv == null) return;

        var subtitleUrls = _playbackManager.GetSubtitleUrls();
        foreach (var (track, fullUrl) in subtitleUrls)
        {
            var codec = track.Codec?.ToLowerInvariant() ?? "";
            if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
                continue;

            var label = !string.IsNullOrEmpty(track.Label) ? track.Label : track.Language ?? "Unknown";
            _mpv.AddSubtitle(fullUrl, label, track.Language);
        }
    }

    private void SendMediaInfoToOsc()
    {
        if (_mpv == null || _playbackManager?.WatchDetail == null || _playbackManager.CurrentSession == null) return;

        var wd = _playbackManager.WatchDetail;
        var session = _playbackManager.CurrentSession;
        var version = wd.Versions?.FirstOrDefault(v => v.FileId == session.MediaFileId);
        if (version == null) return;

        var audioTrack = version.AudioTracks?.ElementAtOrDefault(session.AudioTrackIndex);

        var info = new Dictionary<string, object?>
        {
            ["container"] = version.Container ?? "",
            ["file_size"] = version.FileSize,
            ["bitrate"] = version.Bitrate,
            ["codec_video"] = version.CodecVideo ?? "",
            ["codec_audio"] = version.CodecAudio ?? "",
            ["hdr"] = version.Hdr,
            ["resolution"] = version.Resolution ?? "",
            ["audio_channels"] = audioTrack?.Channels ?? version.AudioChannels ?? 0,
            ["audio_title"] = audioTrack?.Title ?? audioTrack?.EmbeddedTitle ?? "",
        };

        var json = System.Text.Json.JsonSerializer.Serialize(info);
        _mpv.SendScriptMessage("osc-set-media-info", json);

        // Send stream info
        var pi = session.PlaybackInfo;
        var playMethodDisplay = session.PlayMethod switch
        {
            "direct" => "Direct Play",
            "remux" => "Direct Streaming",
            "transcode" => "Transcode",
            _ => session.PlayMethod
        };
        var streamType = pi?.StreamType switch
        {
            "hls" => "HLS",
            _ => "Progressive"
        };
        var streamUrl = _playbackManager.StreamUrl ?? "";
        var protocol = streamUrl.StartsWith("https") ? "https" : "http";

        var vcSuffix = session.PlayMethod == "direct" ? "(direct)" : session.PlayMethod == "remux" ? "(copy)" : "(transcoded)";
        var acSuffix = (pi?.TranscodeAudio == true) ? "(transcoded)" : (session.PlayMethod == "direct" ? "(direct)" : "(copy)");
        var vcDisplay = $"{(pi?.VideoCodec ?? version.CodecVideo ?? "").ToUpper()} {vcSuffix}";
        var acDisplay = $"{(pi?.AudioCodec ?? version.CodecAudio ?? "").ToUpper()} {acSuffix}";

        _mpv.SendScriptMessage("osc-set-stream-info", playMethodDisplay, streamType, protocol, vcDisplay, acDisplay);
    }

    private void SendSubtitleListToOsc()
    {
        if (_mpv == null || _playbackManager?.CurrentSession == null) return;

        var tracks = _playbackManager.CurrentSession.SubtitleUrls ?? [];
        var jsonTracks = tracks.Select(t => new Dictionary<string, object?>
        {
            ["index"] = t.Index,
            ["language"] = t.Language ?? "",
            ["label"] = t.Label ?? "",
            ["source"] = t.Source ?? "embedded",
            ["codec"] = t.Codec ?? "",
            ["forced"] = t.Forced
        }).ToArray();

        var json = System.Text.Json.JsonSerializer.Serialize(jsonTracks);
        _mpv.SendScriptMessage("osc-set-subtitles", json);
        _mpv.SendScriptMessage("osc-set-active-subtitle", "-1");
    }

    private void SendQualityInfoToOsc()
    {
        if (_mpv == null || _playbackManager?.WatchDetail == null || _playbackManager.CurrentSession == null) return;

        var wd = _playbackManager.WatchDetail;
        var session = _playbackManager.CurrentSession;

        var versions = (wd.Versions ?? []).Select(v => new Dictionary<string, object?>
        {
            ["file_id"] = v.FileId,
            ["label"] = v.FileName ?? $"{v.Resolution} {v.CodecVideo}",
            ["resolution"] = v.Resolution ?? ""
        }).ToArray();

        var info = new Dictionary<string, object?>
        {
            ["versions"] = versions,
            ["active_file_id"] = session.MediaFileId,
            ["active_quality"] = "auto"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(info);
        _mpv.SendScriptMessage("osc-set-quality-info", json);
    }

    // ── Video window events ────────────────────────────────────────────

    private void WireVideoWindowEvents()
    {
        if (_videoWindow == null) return;

        _videoWindow.EscapeRequested += () =>
        {
            if (_videoWindow.IsFullscreen)
            {
                _videoWindow.ExitFullscreen();
                // Dispatch to UI thread for XAML state updates
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => SetState(PlayerState.Expanded));
            }
            else
            {
                _videoWindow.Hide();
                // Run CloseAsync on UI thread so SetState(Idle) updates XAML properly
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => _ = CloseAsync());
            }
        };
        _videoWindow.MinimizeRequested += () =>
        {
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => Minimize());
        };
        _videoWindow.ExpandRequested += () =>
        {
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => Expand());
        };
        _videoWindow.FullscreenToggleRequested += () =>
        {
            if (_videoWindow.IsFullscreen)
            {
                _videoWindow.ExitFullscreen();
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => SetState(PlayerState.Expanded));
                _mpv?.SendScriptMessage("osc-fullscreen-state", "false");
            }
            else
            {
                _videoWindow.EnterFullscreen();
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => SetState(PlayerState.Fullscreen));
                _mpv?.SendScriptMessage("osc-fullscreen-state", "true");
            }
        };
    }


    // ── Script message dispatch (Lua → Host) ─────────────────────────────

    private void OnScriptMessage(string[] args)
    {
        if (args.Length == 0) return;
        // Only handle continuum-* messages (Lua→Host intents).
        // Ignore echo-back of host→Lua messages (osc-mouse-move etc.) to avoid flooding.
        if (!args[0].StartsWith("continuum-")) return;

        LogToFile("state_trace.txt", $"ScriptMessage received: {args[0]}");

        var dispatch = App.MainWindowInstance?.DispatcherQueue;
        if (dispatch == null) return;

        switch (args[0])
        {
            case "continuum-exit":
                if (State == PlayerState.Idle) return; // Prevent duplicate close
                dispatch.TryEnqueue(() => _ = CloseAsync());
                break;
            case "continuum-fullscreen-toggle":
                dispatch.TryEnqueue(ToggleFullscreenFromOsc);
                break;
            case "continuum-minimize":
                dispatch.TryEnqueue(Minimize);
                break;
            case "continuum-subtitle-select":
                if (args.Length > 1 && int.TryParse(args[1], out var subIdx))
                {
                    _mpv?.SetSubtitleTrack(subIdx <= 0 ? 0 : subIdx);
                    _mpv?.SendScriptMessage("osc-set-active-subtitle", args[1]);
                }
                break;
            case "continuum-subtitle-search":
                dispatch.TryEnqueue(() => _ = SearchAndDownloadSubtitlesAsync());
                break;
            case "continuum-version-select":
                if (args.Length > 1 && int.TryParse(args[1], out var vFileId))
                {
                    var version = Versions.FirstOrDefault(v => v.FileId == vFileId);
                    if (version != null)
                        dispatch.TryEnqueue(() => _ = SwitchVersionAndNotifyAsync(version));
                }
                break;
            case "continuum-quality-select":
                if (args.Length > 1)
                    dispatch.TryEnqueue(() => _ = SwitchQualityTierAsync(args[1]));
                break;
        }
    }

    private async Task SwitchVersionAndNotifyAsync(FileVersion version)
    {
        await SwitchVersionAsync(version);
        SendQualityInfoToOsc();
        SendMediaInfoToOsc();
        _mpv?.SendScriptMessage("osc-set-active-quality", "auto");
    }

    private async Task SwitchQualityTierAsync(string tierId)
    {
        if (_mpv == null || _playbackManager == null) return;

        var currentPos = _mpv.Position;

        if (tierId is "auto" or "original")
        {
            var currentFileId = _playbackManager.CurrentSession?.MediaFileId;
            var version = Versions.FirstOrDefault(v => v.FileId == currentFileId);
            if (version != null)
            {
                _switchingContent = true;
                try
                {
                    await _playbackManager.StopSessionAsync();
                    var session = await _playbackManager.StartSessionAsync(version.FileId, currentPos);
                    PlayMethod = session.PlayMethod;

                    var token = _apiClient.AccessToken;
                    var authHeader = token != null ? $"Bearer {token}" : null;
                    var streamUrl = _playbackManager.StreamUrl ?? "";
                    if (token != null)
                        streamUrl += (streamUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

                    _resumePosition = currentPos;
                    _mpv.LoadFile(streamUrl, authHeader);
                    _mpv.Play();
                    SendMediaInfoToOsc();
                }
                catch (Exception ex)
                {
                    LogToFile("player_quality_switch_error.txt", ex.ToString());
                    _mpv.ShowOsdText("Quality switch failed", 3000);
                }
                finally { _switchingContent = false; }
            }
        }
        else
        {
            var (resolution, bitrate) = tierId switch
            {
                "1080p-high" => ("1080p", 10000),
                "1080p"      => ("1080p", 6000),
                "720p-high"  => ("720p", 4000),
                "720p"       => ("720p", 2000),
                "480p"       => ("480p", 1500),
                "420p"       => ("420p", 720),
                _ => ("1080p", 6000)
            };

            _switchingContent = true;
            try
            {
                var transcodeResponse = await _playbackApi.StartTranscodeAsync(new TranscodeStartRequest
                {
                    SessionId = _playbackManager.SessionId!,
                    SeekSeconds = currentPos,
                    TargetResolution = resolution,
                    TargetCodecVideo = "h264",
                    TargetCodecAudio = "aac",
                    TargetBitrateKbps = bitrate,
                    SegmentDuration = 2,
                    SubtitleTrackIndex = -1,
                    SubtitleBurnIn = false
                });

                var baseUrl = _apiClient.BaseUrl;
                var manifestPath = transcodeResponse.ManifestUrl;
                if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                    manifestPath = "/api/v1" + manifestPath;
                var streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";

                PlayMethod = "transcode";
                _resumePosition = transcodeResponse.PlayerStartSeconds;
                _mpv.LoadFile(streamUrl);
                _mpv.Play();
                SendMediaInfoToOsc();
            }
            catch (Exception ex)
            {
                LogToFile("player_quality_switch_error.txt", ex.ToString());
                _mpv.ShowOsdText("Transcode failed", 3000);
            }
            finally { _switchingContent = false; }
        }

        _mpv?.SendScriptMessage("osc-set-active-quality", tierId);
    }

    private async Task SearchAndDownloadSubtitlesAsync()
    {
        if (_playbackManager?.CurrentSession == null) return;
        var fileId = _playbackManager.CurrentSession.MediaFileId;

        try
        {
            var results = await _playbackApi.SearchSubtitlesAsync(fileId, ["en"]);
            if (results.Results.Count == 0)
            {
                _mpv?.ShowOsdText("No subtitles found", 3000);
                return;
            }

            var best = results.Results[0];
            await _playbackApi.DownloadSubtitleAsync(fileId, best.Provider, best.SubtitleId, best.Language, best.Format);
            _mpv?.ShowOsdText($"Downloaded: {best.Language} subtitle", 3000);

            // Reload subtitles
            Task.Run(() => LoadSubtitles());
            SendSubtitleListToOsc();
        }
        catch (Exception ex)
        {
            LogToFile("player_subtitle_error.txt", ex.ToString());
            _mpv?.ShowOsdText("Subtitle search failed", 3000);
        }
    }

    private void ToggleFullscreenFromOsc()
    {
        if (_videoWindow == null) return;
        LogToFile("state_trace.txt", $"ToggleFullscreenFromOsc: currently={_videoWindow.IsFullscreen}");

        if (_videoWindow.IsFullscreen)
        {
            _videoWindow.ExitFullscreen();
            SetState(PlayerState.Expanded);
            _mpv?.SendScriptMessage("osc-fullscreen-state", "false");
        }
        else
        {
            _videoWindow.EnterFullscreen();
            SetState(PlayerState.Fullscreen);
            _mpv?.SendScriptMessage("osc-fullscreen-state", "true");
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void PositionVideoForMiniBar()
    {
        if (_videoWindow == null) return;
        var mw = App.MainWindowInstance;
        if (mw == null) { _videoWindow.Hide(); return; }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);
        GetWindowRect(hwnd, out var windowRect);

        // Get DPI scale factor (96 = 100%, 144 = 150%, 192 = 200%)
        double dpi = GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;

        // Mini-bar thumbnail: 200x112 logical pixels (16:9), bar height 132
        int thumbW = (int)(200 * scale);
        int thumbH = (int)(112 * scale);
        int thumbX = windowRect.Left + (int)(12 * scale);
        int thumbY = windowRect.Bottom - (int)(132 * scale) + (int)(10 * scale);

        _videoWindow.PositionAt(thumbX, thumbY, thumbW, thumbH);
    }

    public void HandleWindowResize()
    {
        if (State == PlayerState.Minimized)
            PositionVideoForMiniBar();
        else
            _videoWindow?.MatchParentPosition();
    }

    // ── Close / Dispose ──────────────────────────────────────────────────

    private bool _closing;

    public async Task CloseAsync()
    {
        if (_closing) return; // Prevent duplicate close from spammed exit clicks
        _closing = true;

        LogToFile("state_trace.txt", $"CloseAsync called: State={State} _switchingContent={_switchingContent}");
        if (State == PlayerState.Fullscreen)
            ExitFullscreen();

        // Report final position to server BEFORE stopping (so resume works)
        if (_mpv != null && _playbackManager?.SessionId != null)
        {
            var finalPos = _mpv.Position;
            if (finalPos > 0)
            {
                try
                {
                    await _playbackApi.ReportProgressAsync(_playbackManager.SessionId, finalPos, true);
                    LogToFile("state_trace.txt", $"Final progress reported: pos={finalPos:F1}");
                }
                catch { }
            }
        }

        _mpv?.Stop();

        if (_playbackManager != null)
        {
            try { await _playbackManager.StopSessionAsync(); }
            catch { }
            _playbackManager.Dispose();
            _playbackManager = null;
        }

        ContentId = null;
        Title = "";
        Subtitle = null;
        Position = 0;
        Duration = 0;
        IsPaused = true;
        IsLoading = false;
        _switchingContent = false;
        ErrorMessage = null;
        Versions = [];

        App.MainWindowInstance?.HideLoadingOverlay();
        _videoWindow?.Hide();
        SetState(PlayerState.Idle);
        _closing = false;
        LogToFile("state_trace.txt", "CloseAsync completed");
    }

    public void Dispose()
    {
        _videoWindow?.Dispose();
        _videoWindow = null;
        if (State != PlayerState.Idle)
        {
            if (State == PlayerState.Fullscreen)
                ExitFullscreen();
            _playbackManager?.Dispose();
        }
        _mpv?.Dispose();
        _mpv = null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public static string FormatTime(double totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    public static string LanguageCodeToName(string? code)
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
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {content}\n");
        }
        catch { }
    }
}
