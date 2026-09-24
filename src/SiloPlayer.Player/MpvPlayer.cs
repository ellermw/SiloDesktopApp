using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using static SiloPlayer.Player.MpvInterop;

namespace SiloPlayer.Player;

/// <summary>
/// High-level wrapper around libmpv that uses the software render API (MPV_RENDER_API_TYPE_SW).
/// mpv decodes video (optionally using d3d11va-copy for hardware decode) and renders each frame
/// into a caller-provided byte buffer. The caller (PlayerService) copies that buffer into a
/// WriteableBitmap displayed in a XAML Image element.
///
/// This completely decouples mpv from WinUI 3's D3D11 compositor, eliminating the thread
/// contention that blocked the UI when using wid/vo=gpu.
/// </summary>
public sealed partial class MpvPlayer : IDisposable
{
    private IntPtr _mpvHandle;
    private IntPtr _renderCtx;
    private Thread? _eventThread;
    private Thread? _renderThread;
    private volatile bool _disposed;
    private int _nativeCleanupStarted;

    // Render context creation artifacts (must be kept alive while render context exists)
    private GCHandle _apiTypePin;
    private IntPtr _createParamsPtr;

    // Update callback delegate (prevent GC collection)
    private MpvRenderUpdateFn? _updateCallbackDelegate;

    // Frame buffer
    private byte[]? _frameBuffer;
    private GCHandle _frameBufferPin;
    private int _renderWidth;
    private int _renderHeight;
    private int _stride;
    private readonly object _renderSizeLock = new();

    // Pre-allocated render params (reused every frame to avoid GC pressure)
    private int[] _renderSizeArr = new int[2];
    private GCHandle _renderSizePin;
    private long _renderStrideValue;
    private GCHandle _renderStridePin;

    // Signals from the update callback that a new frame is available
    private readonly ManualResetEventSlim _frameUpdateEvent = new(false);

    // ── Public properties ────────────────────────────────────────────────

    /// <summary>Current playback position in seconds.</summary>
    public double Position { get; private set; }

    /// <summary>Total duration of the current media in seconds.</summary>
    public double Duration { get; private set; }

    /// <summary>Whether playback is currently paused.</summary>
    public bool IsPaused { get; private set; } = true;

    /// <summary>True when mpv is paused waiting for the network cache to fill
    /// (the <c>paused-for-cache</c> property). Indicates rebuffering during
    /// streaming playback — distinct from user-initiated pause.</summary>
    public bool IsBufferingForCache { get; private set; }

    /// <summary>Fires when <c>paused-for-cache</c> state changes.</summary>
    public event Action<bool>? BufferingChanged;

    /// <summary>Whether a file is loaded and playing (not paused, not ended).</summary>
    public bool IsPlaying => !IsPaused && Duration > 0;

    // ── Events ───────────────────────────────────────────────────────────

    /// <summary>Fired when the playback position changes.</summary>
    public event Action<double>? PositionChanged;

    /// <summary>Fired when the media duration is known or changes.</summary>
    public event Action<double>? DurationChanged;

    /// <summary>Fired when the pause state changes.</summary>
    public event Action<bool>? PauseChanged;
    /// <summary>Fired when a Lua script sends a script-message (Lua -> Host).</summary>
    public event Action<string[]>? ScriptMessageReceived;

    /// <summary>Fired when mpv reports the file has ended.</summary>
    public event Action? PlaybackEnded;

    /// <summary>Fired when mpv's eof-reached property flips true.</summary>
    public event Action? EofReached;

    /// <summary>Fired when a file has been loaded and decoding starts.</summary>
    public event Action? FileLoaded;

    /// <summary>
    /// Fired when mpv has produced output after an initial load, seek, or
    /// buffering pause. For initial loads this is the native first-frame-ready
    /// boundary, which occurs after <see cref="FileLoaded"/>.
    /// </summary>
    public event Action? PlaybackRestarted;

    /// <summary>Fired when a file fails to load or a playback error occurs (END_FILE with reason=error).</summary>
    public event Action<string>? PlaybackError;
    public event Action<string>? DirectStreamError;

    /// <summary>Fired when an error occurs.</summary>
    public event Action<string>? Error;

    /// <summary>
    /// Fired from the render thread when a new frame has been rendered into the buffer.
    /// Args: (byte[] buffer, int width, int height, int stride).
    /// The buffer is valid only until the next frame render -- the subscriber must copy
    /// the data synchronously or dispatch a copy immediately.
    /// </summary>
    public event Action<byte[], int, int, int>? FrameReady;

    // ── Reply userdata IDs for observed properties ───────────────────────

    private const ulong UD_TIME_POS         = 1;
    private const ulong UD_DURATION         = 2;
    private const ulong UD_PAUSE            = 3;
    private const ulong UD_EOF_REACHED      = 4;
    private const ulong UD_PAUSED_FOR_CACHE = 5;

    // ── Initialization ───────────────────────────────────────────────────

    /// <summary>
    /// Creates the mpv instance with a software render context.
    /// No child window or video output driver is used -- frames are rendered to a memory buffer.
    /// </summary>
    /// <param name="renderWidth">Initial render width in physical pixels.</param>
    /// <param name="renderHeight">Initial render height in physical pixels.</param>
    public void Initialize(int renderWidth, int renderHeight)
    {
        if (_mpvHandle != IntPtr.Zero)
            throw new InvalidOperationException("MpvPlayer is already initialized.");

        _mpvHandle = mpv_create();
        if (_mpvHandle == IntPtr.Zero)
            throw new InvalidOperationException("mpv_create() returned null.");

        // Hardware decode back to auto-copy
        SetOption("hwdec", "auto-copy");

        // Disable mpv's on-screen controller and input — we handle everything in XAML
        SetOption("osc", "no");
        SetOption("osd-level", "0");
        SetOption("input-default-bindings", "no");
        SetOption("input-vo-keyboard", "no");

        // Player behavior
        SetOption("keep-open", "yes");
        SetOption("idle", "yes");

        // === High-bitrate / 4K remux buffering ===
        SetOption("cache", "yes");
        SetOption("ytdl", "no");
        // At 100 Mbps, 256 MiB is roughly 21 seconds of compressed media.
        // Do not hold first frame behind the rebuffer threshold.
        SetOption("demuxer-max-bytes", "256MiB");
        SetOption("demuxer-max-back-bytes", "64MiB");
        SetOption("demuxer-readahead-secs", "30");
        SetOption("cache-secs", "30");
        SetOption("cache-pause-initial", "no");
        SetOption("cache-pause-wait", "2");
        SetOption("stream-buffer-size", "1MiB");

        // === Network resilience ===
        SetOption("network-timeout", "30");
        SetOption("stream-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_delay_max=5");

        // === Seeking performance ===
        SetOption("hr-seek-framedrop", "yes");           // Drop frames during seek for speed
        SetOption("hr-seek", "default");                 // Commands choose exact vs keyframe seeking explicitly

        // === Audio — preserve full quality, no resampling ===
        SetOption("audio-channels", "auto-safe");        // Avoid unsafe HDMI layout assumptions
        SetOption("audio-samplerate", "0");              // No resampling — native sample rate
        SetOption("audio-pitch-correction", "no");       // No pitch correction artifacts
        SetOption("ad-lavc-downmix", "no");              // Never downmix — preserve all channels
        SetOption("replaygain", "no");                   // No volume normalization

        // === Video — preserve full quality ===
        SetOption("video-sync", "audio");
        SetOption("framedrop", "vo");
        SetOption("correct-downscaling", "yes");
        SetOption("deband", "no");



        // vo=libmpv is required when using the render API
        SetOption("vo", "libmpv");

        // Logging (production: status only, not verbose)
        var logPath = GetPlayerLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        RotateLogIfNeeded(logPath);
        SetOption("log-file", logPath);
        SetOption("msg-level", "all=warn");

        // Initialize mpv
        int err = mpv_initialize(_mpvHandle);
        if (err < 0)
        {
            string errMsg = GetErrorString(err);
            mpv_terminate_destroy(_mpvHandle);
            _mpvHandle = IntPtr.Zero;
            throw new InvalidOperationException($"mpv_initialize failed: {errMsg}");
        }

        // Create software render context
        CreateRenderContext();

        // Set update callback (fires from mpv's internal thread when a new frame is ready)
        _updateCallbackDelegate = OnRenderUpdate;
        var callbackPtr = Marshal.GetFunctionPointerForDelegate(_updateCallbackDelegate);
        mpv_render_context_set_update_callback(_renderCtx, callbackPtr, IntPtr.Zero);

        // Allocate frame buffer
        lock (_renderSizeLock)
        {
            _renderWidth = Math.Max(renderWidth, 1);
            _renderHeight = Math.Max(renderHeight, 1);
            _stride = _renderWidth * 4; // bgr0 = 4 bytes per pixel
            _frameBuffer = new byte[_stride * _renderHeight];
            _frameBufferPin = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);
        }

        // Observe properties (after initialize)
        mpv_observe_property(_mpvHandle, UD_TIME_POS, "time-pos", MPV_FORMAT_DOUBLE);
        mpv_observe_property(_mpvHandle, UD_DURATION, "duration", MPV_FORMAT_DOUBLE);
        mpv_observe_property(_mpvHandle, UD_PAUSE, "pause", MPV_FORMAT_FLAG);
        mpv_observe_property(_mpvHandle, UD_EOF_REACHED, "eof-reached", MPV_FORMAT_FLAG);
        mpv_observe_property(_mpvHandle, UD_PAUSED_FOR_CACHE, "paused-for-cache", MPV_FORMAT_FLAG);
        ObserveDiagnostics();

        // Start background event loop thread
        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "MpvEventLoop"
        };
        _eventThread.Start();

        // Start render thread
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "MpvRenderLoop"
        };
        _renderThread.Start();
    }

    private void CreateRenderContext()
    {
        // Pin the API type string "sw\0"
        byte[] apiType = "sw\0"u8.ToArray();
        _apiTypePin = GCHandle.Alloc(apiType, GCHandleType.Pinned);

        // Build param array: [API_TYPE, terminator]
        int paramSize = Marshal.SizeOf<MpvRenderParam>();
        _createParamsPtr = Marshal.AllocHGlobal(paramSize * 2);

        var param0 = new MpvRenderParam
        {
            Type = (IntPtr)MPV_RENDER_PARAM_API_TYPE,
            Data = _apiTypePin.AddrOfPinnedObject()
        };
        var paramEnd = new MpvRenderParam
        {
            Type = IntPtr.Zero,
            Data = IntPtr.Zero
        };

        Marshal.StructureToPtr(param0, _createParamsPtr, false);
        Marshal.StructureToPtr(paramEnd, _createParamsPtr + paramSize, false);

        int err = mpv_render_context_create(out _renderCtx, _mpvHandle, _createParamsPtr);
        if (err < 0)
        {
            string errMsg = GetErrorString(err);
            // Clean up
            Marshal.FreeHGlobal(_createParamsPtr);
            _createParamsPtr = IntPtr.Zero;
            if (_apiTypePin.IsAllocated) _apiTypePin.Free();
            throw new InvalidOperationException($"mpv_render_context_create failed: {errMsg}");
        }
    }

    // ── GPU window-based initialization ─────────────────────────────────

    /// <summary>
    /// Creates the mpv instance using GPU rendering into a provided window handle.
    /// mpv renders directly via D3D11 -- zero frame copies, hardware accelerated.
    /// </summary>
    public void InitializeWithWindow(IntPtr windowHandle, bool nvidiaVideoUpscaling = false,
        VideoUpscalingMode? upscalingMode = null)
    {
        if (_mpvHandle != IntPtr.Zero)
            throw new InvalidOperationException("MpvPlayer is already initialized.");

        _mpvHandle = mpv_create();
        if (_mpvHandle == IntPtr.Zero)
            throw new InvalidOperationException("mpv_create() returned null.");

        // Prefer native Windows hardware decode. The trailing auto entry and
        // mpv's software fallback cover unsupported codecs and display drivers.
        SetOption("hwdec", "d3d11va,auto");

        // gpu-next is libplacebo-backed and is required for current mpv HDR
        // colorspace signaling and Dolby Vision reshaping. Keep gpu as a
        // renderer fallback for unusual/older D3D11 drivers.
        SetOption("vo", "gpu-next,gpu");
        SetOption("gpu-api", "d3d11");
        SetOption("gpu-context", "d3d11");
        ConfigureUpscaling(upscalingMode ?? (nvidiaVideoUpscaling ? VideoUpscalingMode.Nvidia : VideoUpscalingMode.Off));

        // Set the target window handle
        SetOption("wid", windowHandle.ToString());

        // Disable built-in OSC — our custom silo-osc.lua handles everything
        SetOption("osc", "no");
        SetOption("osd-level", "1");

        // Load Silo's custom OSC
        var exeDir = Path.GetDirectoryName(System.Environment.ProcessPath) ?? "";
        var oscPath = Path.Combine(exeDir, "libs", "mpv", "scripts", "silo-osc.lua");
        if (!File.Exists(oscPath))
            oscPath = Path.Combine(exeDir, "..", "..", "..", "..", "libs", "mpv", "scripts", "silo-osc.lua");
        if (File.Exists(oscPath))
            SetOption("scripts", oscPath);

        // Enable mpv's input handling (keyboard + mouse forwarded from WndProc)
        SetOption("input-default-bindings", "yes");
        SetOption("input-vo-keyboard", "yes");
        SetOption("input-cursor", "yes");

        // Player behavior
        SetOption("keep-open", "yes");
        SetOption("idle", "yes");

        // Buffering for ultra high-bitrate content (100+ Mbps 4K remux)
        SetOption("cache", "yes");
        SetOption("ytdl", "no");
        // At 100 Mbps this forward cache holds roughly 21 seconds. It absorbs
        // ordinary CDN jitter without the old 800+200 MiB cache ceiling.
        SetOption("demuxer-max-bytes", "256MiB");
        SetOption("demuxer-max-back-bytes", "64MiB");
        SetOption("demuxer-readahead-secs", "30");
        SetOption("cache-secs", "30");
        SetOption("cache-pause-initial", "no");
        SetOption("cache-pause-wait", "2");
        SetOption("stream-buffer-size", "1MiB");

        // Network resilience (reconnect on stream errors, HLS segment retries)
        SetOption("stream-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_delay_max=5");
        SetOption("network-timeout", "30");

        // Performance tuning
        // The mpv target is an owned popup window. Windows/DWM may throttle or
        // change presentation timing when that popup loses foreground focus.
        // display-resample follows the presentation clock and can then queue a
        // burst of catch-up video while audio continues normally. Keep audio as
        // the stable master clock so late video frames are dropped instead of
        // being presented at an accelerated rate after focus changes.
        SetOption("video-sync", "audio");
        SetOption("interpolation", "no");
        SetOption("framedrop", "vo");
        SetOption("hr-seek-framedrop", "yes");
        SetOption("deinterlace", "auto");

        // Adapt HDR10, HDR10+, HLG, and supported Dolby Vision sources to the
        // actual Windows display colorspace. Auto avoids forcing HDR metadata
        // when the OS/driver cannot report target display parameters.
        SetOption("target-colorspace-hint", "auto");
        SetOption("target-colorspace-hint-mode", "target");

        // Stable shared-mode output with the endpoint's safe native PCM layout.
        // Compressed Atmos/DTS:X bitstream passthrough must only be enabled
        // after explicit sink capability verification; enabling it blindly can
        // produce silence on speakers, Bluetooth, USB, and unsupported HDMI.
        SetOption("audio-channels", "auto-safe");
        SetOption("audio-samplerate", "0");
        SetOption("audio-exclusive", "no");
        SetOption("ad-lavc-downmix", "no");
        SetOption("replaygain", "no");

        // Logging
        var logPath = GetPlayerLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        RotateLogIfNeeded(logPath);
        SetOption("log-file", logPath);
        SetOption("msg-level", "all=warn");

        // Initialize
        int err = mpv_initialize(_mpvHandle);
        if (err < 0)
        {
            string errMsg = GetErrorString(err);
            mpv_terminate_destroy(_mpvHandle);
            _mpvHandle = IntPtr.Zero;
            throw new InvalidOperationException($"mpv_initialize failed: {errMsg}");
        }

        // Observe properties
        mpv_observe_property(_mpvHandle, UD_TIME_POS, "time-pos", MPV_FORMAT_DOUBLE);
        mpv_observe_property(_mpvHandle, UD_DURATION, "duration", MPV_FORMAT_DOUBLE);
        mpv_observe_property(_mpvHandle, UD_PAUSE, "pause", MPV_FORMAT_FLAG);
        mpv_observe_property(_mpvHandle, UD_EOF_REACHED, "eof-reached", MPV_FORMAT_FLAG);
        mpv_observe_property(_mpvHandle, UD_PAUSED_FOR_CACHE, "paused-for-cache", MPV_FORMAT_FLAG);
        ObserveDiagnostics();

        // Start event loop thread
        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "MpvEventLoop"
        };
        _eventThread.Start();

        SetProperty("user-data/silo-upscaling-status", _upscalingStatus);
        if (_upscalingEnabled)
            mpv_request_log_messages(_mpvHandle, "warn");

        // No render thread needed -- mpv handles rendering internally via vo=gpu
    }

    // ── Render size management ───────────────────────────────────────────

    /// <summary>
    /// Updates the render target size. Call when the display area resizes.
    /// The new size takes effect on the next frame render.
    /// </summary>
    /// <param name="width">New width in physical pixels.</param>
    /// <param name="height">New height in physical pixels.</param>
    public void UpdateRenderSize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        lock (_renderSizeLock)
        {
            if (width == _renderWidth && height == _renderHeight)
                return;

            // Free old pinned buffer
            if (_frameBufferPin.IsAllocated)
                _frameBufferPin.Free();

            _renderWidth = width;
            _renderHeight = height;
            _stride = _renderWidth * 4;
            _frameBuffer = new byte[_stride * _renderHeight];
            _frameBufferPin = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);
        }

        // Signal a re-render at the new size
        _frameUpdateEvent.Set();
    }

    // ── Render loop ─────────────────────────────────────────────────────

    private void OnRenderUpdate(IntPtr ctx)
    {
        // Called from mpv's internal thread -- just signal our render thread
        _frameUpdateEvent.Set();
    }

    private void RenderLoop()
    {
        byte[] formatBytes = "bgr0\0"u8.ToArray();
        var formatPin = GCHandle.Alloc(formatBytes, GCHandleType.Pinned);

        int paramSize = Marshal.SizeOf<MpvRenderParam>();
        IntPtr paramsPtr = Marshal.AllocHGlobal(paramSize * 5);

        // Pre-pin the reusable size buffer (updated in-place each frame)
        _renderSizePin = GCHandle.Alloc(_renderSizeArr, GCHandleType.Pinned);
        // Pin initial stride value
        _renderStridePin = GCHandle.Alloc(_renderStrideValue, GCHandleType.Pinned);

        try
        {
            while (!_disposed)
            {
                _frameUpdateEvent.Wait(100); // timeout so we can check _disposed
                _frameUpdateEvent.Reset();

                if (_disposed || _renderCtx == IntPtr.Zero) return;

                var flags = mpv_render_context_update(_renderCtx);
                if ((flags & MPV_RENDER_UPDATE_FRAME) == 0) continue;

                byte[] buffer;
                int w, h;
                long stride;

                lock (_renderSizeLock)
                {
                    if (_frameBuffer == null || !_frameBufferPin.IsAllocated)
                        continue;

                    w = _renderWidth;
                    h = _renderHeight;
                    stride = _stride;
                    buffer = _frameBuffer;

                    // Update pre-allocated arrays in place
                    _renderSizeArr[0] = w;
                    _renderSizeArr[1] = h;

                    // Stride is a boxed long -- must re-pin when value changes
                    if (_renderStrideValue != stride)
                    {
                        _renderStrideValue = stride;
                        if (_renderStridePin.IsAllocated) _renderStridePin.Free();
                        _renderStridePin = GCHandle.Alloc(_renderStrideValue, GCHandleType.Pinned);
                    }

                    var bufferPtr = _frameBufferPin.AddrOfPinnedObject();

                    var p0 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_SIZE, Data = _renderSizePin.AddrOfPinnedObject() };
                    var p1 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_FORMAT, Data = formatPin.AddrOfPinnedObject() };
                    var p2 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_STRIDE, Data = _renderStridePin.AddrOfPinnedObject() };
                    var p3 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_POINTER, Data = bufferPtr };
                    var pEnd = new MpvRenderParam { Type = IntPtr.Zero, Data = IntPtr.Zero };

                    Marshal.StructureToPtr(p0, paramsPtr, false);
                    Marshal.StructureToPtr(p1, paramsPtr + paramSize, false);
                    Marshal.StructureToPtr(p2, paramsPtr + paramSize * 2, false);
                    Marshal.StructureToPtr(p3, paramsPtr + paramSize * 3, false);
                    Marshal.StructureToPtr(pEnd, paramsPtr + paramSize * 4, false);

                    int err = mpv_render_context_render(_renderCtx, paramsPtr);
                    if (err < 0)
                        continue;
                }

                // Signal frame ready OUTSIDE the lock (subscribers copy the buffer).
                // Isolate each subscriber so one failed renderer cannot suppress a
                // diagnostic/secondary sink or terminate the render pump.
                InvokeFrameReadySafely(buffer, w, h, (int)stride);
            }
        }
        finally
        {
            if (formatPin.IsAllocated) formatPin.Free();
            if (_renderSizePin.IsAllocated) _renderSizePin.Free();
            if (_renderStridePin.IsAllocated) _renderStridePin.Free();
            Marshal.FreeHGlobal(paramsPtr);
        }
    }

    // ── Playback controls ────────────────────────────────────────────────

    /// <summary>
    /// Loads a media file from the given URL.
    /// </summary>
    /// <param name="url">The URL or file path to load.</param>
    /// <param name="authHeader">Optional Authorization header value (e.g. "Bearer token").</param>
    public void LoadFile(string url, string? authHeader = null, double startSeconds = 0)
    {
        ThrowIfNotInitialized();
        ResetUpscalingForLoad();

        // Original files use a seekable HTTP reader directly through mpv's
        // stream API, retaining connection reuse and byte-level recovery.
        var nativeDirect = CanOpenSignedDirectStream(url);
        StartDiagnostics(nativeDirect, startSeconds);
        SetProperty("stream-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_delay_max=5");
        SetProperty("network-timeout", nativeDirect ? "10" : "30");
        mpv_set_property_string(_mpvHandle, "http-header-fields",
            !nativeDirect && !string.IsNullOrEmpty(authHeader) ? $"Authorization: {authHeader}" : "");

        if (nativeDirect) url = PrepareNativeDirectInput(url);
        else Volatile.Write(ref _directRequest, null);

        if (startSeconds > 0.001)
            Command("loadfile", url, "replace", "-1", $"start={FormatSeconds(startSeconds)}");
        else
            Command("loadfile", url);
    }

    /// <summary>Signed direct-node URLs need no rotating account-token relay.</summary>
    public static bool CanOpenSignedDirectStream(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        uri.AbsolutePath.StartsWith("/stream/direct/", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.Length > "/stream/direct/".Length;

    /// <summary>Applies a bounded cache profile before the next network load.</summary>
    public void ConfigureNetworkBuffer(int maxMiB, int backMiB, int readAheadSeconds, int streamMiB)
    {
        ThrowIfNotInitialized();
        SetProperty("demuxer-max-bytes", $"{Math.Clamp(maxMiB, 64, 1024)}MiB");
        SetProperty("demuxer-max-back-bytes", $"{Math.Clamp(backMiB, 16, 256)}MiB");
        SetProperty("demuxer-readahead-secs", Math.Clamp(readAheadSeconds, 10, 120).ToString());
        SetProperty("cache-secs", Math.Clamp(readAheadSeconds, 10, 120).ToString());
        SetProperty("stream-buffer-size", $"{Math.Clamp(streamMiB, 1, 16)}MiB");
    }

    /// <summary>Stops the current file without triggering end-of-file events.</summary>
    public void Stop()
    {
        ThrowIfNotInitialized();
        _diagnostics?.Record("stop_requested", new { position = Position });
        Command("stop");
        // Keep the flight recorder through stop/reload recovery and episode changes.
        // Its bounded history and incident windows finish independently of playback.
    }

    /// <summary>Resumes playback.</summary>
    public void Play()
    {
        ThrowIfNotInitialized();
        _diagnostics?.Record("play_requested", new { position = Position });
        mpv_set_property_string(_mpvHandle, "pause", "no");
    }

    /// <summary>Pauses playback.</summary>
    public void Pause()
    {
        ThrowIfNotInitialized();
        _diagnostics?.Record("pause_requested", new { position = Position });
        mpv_set_property_string(_mpvHandle, "pause", "yes");
    }

    /// <summary>Toggles the pause state.</summary>
    public void TogglePause()
    {
        ThrowIfNotInitialized();
        Command("cycle", "pause");
    }

    /// <summary>
    /// Seeks to an absolute position in seconds.
    /// </summary>
    public void Seek(double seconds)
    {
        ThrowIfNotInitialized();
        _diagnostics?.Record("seek_requested", new { from = Position, to = seconds });
        Command("seek", FormatSeconds(seconds), "absolute+exact");
    }

    /// <summary>
    /// Seeks to an absolute position using keyframe boundaries. This is much
    /// faster for interactive scrubbing than mpv's default exact absolute seek.
    /// </summary>
    public void SeekFast(double seconds)
    {
        ThrowIfNotInitialized();
        Command("seek", FormatSeconds(seconds), "absolute+keyframes");
    }

    /// <summary>
    /// Sets the volume (0-100).
    /// </summary>
    public void SetVolume(double volume)
    {
        ThrowIfNotInitialized();
        mpv_set_property_string(_mpvHandle, "volume",
            volume.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Sets the mute state.
    /// </summary>
    public void SetMute(bool mute)
    {
        ThrowIfNotInitialized();
        mpv_set_property_string(_mpvHandle, "mute", mute ? "yes" : "no");
    }

    /// <summary>
    /// Gets the current mute state.
    /// </summary>
    public bool GetMute()
    {
        if (_mpvHandle == IntPtr.Zero) return false;
        if (mpv_get_property_int(_mpvHandle, "mute", MPV_FORMAT_FLAG, out long val) == 0)
            return val != 0;
        return false;
    }

    /// <summary>Sets a string property on mpv.</summary>
    public void SetProperty(string name, string value)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        mpv_set_property_string(_mpvHandle, name, value);
    }

    /// <summary>Gets a double property from mpv.</summary>
    public double GetPropertyDouble(string name)
    {
        if (_mpvHandle == IntPtr.Zero) return 0;
        if (mpv_get_property_double(_mpvHandle, name, MPV_FORMAT_DOUBLE, out double val) == 0)
            return val;
        return 0;
    }

    /// <summary>Returns mpv audio IDs in source order, excluding video/subtitle tracks.</summary>
    public IReadOnlyList<int> GetAudioTrackIds()
    {
        ThrowIfNotInitialized();
        if (mpv_get_property_int(_mpvHandle, "track-list/count", MPV_FORMAT_INT64, out var count) < 0)
            throw new InvalidOperationException("The player audio inventory is unavailable.");
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var typePtr = mpv_get_property_string(_mpvHandle, $"track-list/{i}/type");
            try
            {
                if (Marshal.PtrToStringUTF8(typePtr) != "audio")
                    continue;
                if (mpv_get_property_int(_mpvHandle, $"track-list/{i}/id", MPV_FORMAT_INT64, out var id) < 0)
                    throw new InvalidOperationException("The player audio track ID is unavailable.");
                ids.Add(checked((int)id));
            }
            finally
            {
                if (typePtr != IntPtr.Zero) mpv_free(typePtr);
            }
        }
        return ids;
    }

    /// <summary>
    /// Adds an external subtitle track.
    /// </summary>
    /// <param name="url">URL or file path of the subtitle file.</param>
    /// <param name="title">Optional display title.</param>
    /// <param name="lang">Optional language code.</param>
    /// <summary>Sends a mouse position + button state to mpv's input system.</summary>
    public void SendMousePos(int x, int y)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        Command("mouse", x.ToString(), y.ToString());
    }

    /// <summary>Sends a mouse button press/release to mpv's input system.</summary>
    public void SendMouseButton(int x, int y, int button, bool isDown)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        var btnName = button switch
        {
            0 => "MBTN_LEFT",
            1 => "MBTN_MID",
            2 => "MBTN_RIGHT",
            _ => $"MBTN{button}"
        };
        Command("mouse", x.ToString(), y.ToString());
        Command(isDown ? "keydown" : "keyup", btnName);
    }

    /// <summary>Sends a key press to mpv's input system.</summary>
    public void SendKeypress(string keyName)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        Command("keypress", keyName);
    }

    /// <summary>Sends a key down to mpv's input system.</summary>
    public void SendKeydown(string keyName)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        Command("keydown", keyName);
    }

    /// <summary>Sends a key up to mpv's input system.</summary>
    public void SendKeyup(string keyName)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        Command("keyup", keyName);
    }

    /// <summary>Sends a script message to loaded Lua scripts.</summary>
    public void SendScriptMessage(params string[] args)
    {
        if (_mpvHandle == IntPtr.Zero) return;
        var fullArgs = new string[args.Length + 1];
        fullArgs[0] = "script-message";
        Array.Copy(args, 0, fullArgs, 1, args.Length);
        Command(fullArgs);
    }

    /// <summary>Shows text on mpv's OSD.</summary>
    public void ShowOsdText(string text, int durationMs = 5000)
    {
        ThrowIfNotInitialized();
        Command("show-text", text, durationMs.ToString());
    }

    public void AddSubtitle(string url, string? title = null, string? lang = null, bool select = false)
    {
        ThrowIfNotInitialized();

        var args = new List<string> { "sub-add", url };
        if (!string.IsNullOrEmpty(title))
        {
            args.Add(select ? "select" : "auto"); // flags
            args.Add(title);
            if (!string.IsNullOrEmpty(lang))
                args.Add(lang);
        }

        Command(args.ToArray());
    }

    /// <summary>Reloads an already-added subtitle track after its local file
    /// changes. Used by live AI translation as cue batches arrive.</summary>
    public void ReloadSubtitle(int sid)
    {
        ThrowIfNotInitialized();
        if (sid > 0) Command("sub-reload", sid.ToString());
        else Command("sub-reload");
    }

    /// <summary>
    /// Removes a subtitle track by mpv's sid (1-based). Used by the sliding-
    /// window embedded-subtitle fetch to drop a stale window before loading
    /// the next one.
    /// </summary>
    public void RemoveSubtitle(int sid)
    {
        ThrowIfNotInitialized();
        Command("sub-remove", sid.ToString());
    }

    /// <summary>
    /// Selects a subtitle track by index (1-based, 0 to disable).
    /// </summary>
    public void SetSubtitleTrack(int index)
    {
        ThrowIfNotInitialized();
        mpv_set_property_string(_mpvHandle, "sid", index <= 0 ? "no" : index.ToString());
    }

    /// <summary>
    /// Selects an audio track by index (1-based).
    /// </summary>
    public void SetAudioTrack(int index)
    {
        ThrowIfNotInitialized();
        mpv_set_property_string(_mpvHandle, "aid", index.ToString());
    }

    // ── Command helper ───────────────────────────────────────────────────

    /// <summary>
    /// Sends a command to mpv. Each arg is converted to a UTF-8 null-terminated byte array,
    /// pinned, and passed as a null-terminated IntPtr array to mpv_command.
    /// </summary>
    private bool Command(params string[] args)
    {
        ThrowIfNotInitialized();

        // Allocate byte arrays for each argument (UTF-8 + null terminator)
        var byteArrays = new byte[args.Length][];
        for (int i = 0; i < args.Length; i++)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(args[i]);
            byteArrays[i] = new byte[utf8.Length + 1]; // +1 for null terminator
            Array.Copy(utf8, byteArrays[i], utf8.Length);
            // last byte is already 0 from array initialization
        }

        // Pin each byte array and build the pointer array
        var handles = new GCHandle[args.Length];
        var ptrs = new IntPtr[args.Length + 1]; // +1 for null terminator

        try
        {
            for (int i = 0; i < byteArrays.Length; i++)
            {
                handles[i] = GCHandle.Alloc(byteArrays[i], GCHandleType.Pinned);
                ptrs[i] = handles[i].AddrOfPinnedObject();
            }
            ptrs[args.Length] = IntPtr.Zero; // null terminator

            int err = mpv_command(_mpvHandle, ptrs);
            if (err < 0)
            {
                string errMsg = GetErrorString(err);
                ReportErrorSafely($"mpv_command [{string.Join(" ", args.Select(RedactCommandArgument))}] failed: {errMsg}");
            }
            return err >= 0;
        }
        finally
        {
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].IsAllocated)
                    handles[i].Free();
            }
        }
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

    private static void RotateLogIfNeeded(string path, long maxBytes = 2 * 1024 * 1024)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < maxBytes)
                return;

            var archivePath = $"{path}.1";
            try { File.Delete(archivePath); } catch { }
            File.Move(path, archivePath, overwrite: true);
        }
        catch
        {
        }
    }

    // ── Event loop ───────────────────────────────────────────────────────

    private static string RedactCommandArgument(string arg)
    {
        if (arg.StartsWith("Authorization: Bearer ", StringComparison.OrdinalIgnoreCase))
            return "Authorization: Bearer <redacted>";

        return Regex.Replace(
            arg,
            @"(?<key>[?&](?:access_token|refresh_token|profile_token|room_token|token|jwt|password|secret|api_key|apikey|key|AWSAccessKeyId|X-Amz-[A-Za-z0-9-]+)=)[^&\s]+",
            "${key}<redacted>",
            RegexOptions.IgnoreCase);
    }

    private void EventLoop()
    {
        while (!_disposed)
        {
            IntPtr evPtr = mpv_wait_event(_mpvHandle, 0.5);
            Volatile.Write(ref _lastEventTick, System.Diagnostics.Stopwatch.GetTimestamp());
            if (evPtr == IntPtr.Zero)
                continue;

            var ev = Marshal.PtrToStructure<MpvEvent>(evPtr);

            try
            {
                switch (ev.EventId)
                {
                    case MPV_EVENT_NONE:
                        // Timeout or wakeup, nothing to do
                        break;

                    case MPV_EVENT_PROPERTY_CHANGE:
                        HandlePropertyChange(ev);
                        break;

                    case MPV_EVENT_FILE_LOADED:
                        _diagnostics?.Record("file_loaded", new { position = Position, duration = Duration });
                        lock (_upscalingLock) _upscalingFileLoaded = true;
                        InvokeSafely(FileLoaded, nameof(FileLoaded));
                        break;

                    case MPV_EVENT_LOG_MESSAGE:
                        HandleUpscalingLog(ev);
                        break;

                    case MPV_EVENT_PLAYBACK_RESTART:
                        _diagnostics?.Record("playback_restart", new { position = Position });
                        InvokeSafely(PlaybackRestarted, nameof(PlaybackRestarted));
                        break;

                    case MPV_EVENT_END_FILE:
                        lock (_upscalingLock) _upscalingFileLoaded = false;
                        if (ev.Data != IntPtr.Zero)
                        {
                            var endFile = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                            _diagnostics?.Record("end_file", new { reason = endFile.Reason, error = endFile.Error, position = Position });
                            if (endFile.Reason == MPV_END_FILE_REASON_ERROR) _diagnostics?.Incident("native_playback_error");
                            if (endFile.Reason is MPV_END_FILE_REASON_ERROR or MPV_END_FILE_REASON_EOF &&
                                ReportDirectStreamFailure())
                            {
                                break;
                            }
                            if (endFile.Reason == MPV_END_FILE_REASON_ERROR)
                            {
                                var errMsg = GetErrorString(endFile.Error);
                                InvokeSafely(
                                    PlaybackError,
                                    $"Playback failed: {errMsg}",
                                    nameof(PlaybackError));
                            }
                            else if (endFile.Reason == MPV_END_FILE_REASON_EOF)
                            {
                                // Only a natural EOF may drive post-roll or
                                // auto-next. STOP/QUIT events are generated by
                                // deliberate reloads, quality switches, seeks,
                                // and teardown and must not masquerade as an
                                // ended title.
                                InvokeSafely(PlaybackEnded, nameof(PlaybackEnded));
                            }
                        }
                        break;

                    case MPV_EVENT_CLIENT_MESSAGE:
                        HandleClientMessage(ev);
                        break;

                    case MPV_EVENT_SHUTDOWN:
                        return; // Exit the event loop
                }
                UpdateUpscaling();
            }
            catch (Exception ex)
            {
                // Never let malformed native event data or an unexpected
                // application callback terminate mpv's only event pump.
                ReportErrorSafely($"mpv event {ev.EventId} failed: {ex.Message}");
            }
        }
    }

    private void HandlePropertyChange(MpvEvent ev)
    {
        if (ev.Data == IntPtr.Zero)
            return;

        var prop = Marshal.PtrToStructure<MpvEventProperty>(ev.Data);

        switch (ev.ReplyUserdata)
        {
            case UdDiagnosticCache:
            case UdDiagnosticSpeed:
            case UdDiagnosticDropped:
                if (prop.Format == MPV_FORMAT_DOUBLE && prop.Data != IntPtr.Zero)
                {
                    var value = Marshal.PtrToStructure<double>(prop.Data);
                    if (ev.ReplyUserdata == UdDiagnosticCache) _diagnosticCache = value;
                    if (ev.ReplyUserdata == UdDiagnosticSpeed) _diagnosticSpeed = value;
                    if (ev.ReplyUserdata == UdDiagnosticDropped) _diagnosticDropped = value;
                }
                break;
            case UD_TIME_POS:
                if (prop.Format == MPV_FORMAT_DOUBLE && prop.Data != IntPtr.Zero)
                {
                    double pos = Marshal.PtrToStructure<double>(prop.Data);
                    Position = pos;
                    InvokeSafely(PositionChanged, pos, nameof(PositionChanged));
                }
                break;

            case UD_DURATION:
                if (prop.Format == MPV_FORMAT_DOUBLE && prop.Data != IntPtr.Zero)
                {
                    double dur = Marshal.PtrToStructure<double>(prop.Data);
                    Duration = dur;
                    Volatile.Write(ref _directDuration, dur);
                    InvokeSafely(DurationChanged, dur, nameof(DurationChanged));
                }
                break;

            case UD_PAUSE:
                if (prop.Format == MPV_FORMAT_FLAG && prop.Data != IntPtr.Zero)
                {
                    int flag = Marshal.PtrToStructure<int>(prop.Data);
                    bool paused = flag != 0;
                    IsPaused = paused;
                    InvokeSafely(PauseChanged, paused, nameof(PauseChanged));
                }
                break;

            case UD_EOF_REACHED:
                if (prop.Format == MPV_FORMAT_FLAG && prop.Data != IntPtr.Zero)
                {
                    int flag = Marshal.PtrToStructure<int>(prop.Data);
                    if (flag != 0 && !ReportDirectStreamFailure())
                        InvokeSafely(EofReached, nameof(EofReached));
                }
                break;

            case UD_PAUSED_FOR_CACHE:
                if (prop.Format == MPV_FORMAT_FLAG && prop.Data != IntPtr.Zero)
                {
                    int flag = Marshal.PtrToStructure<int>(prop.Data);
                    bool buffering = flag != 0;
                    if (buffering != IsBufferingForCache)
                    {
                        IsBufferingForCache = buffering;
                        RecordBuffering(buffering);
                        InvokeSafely(BufferingChanged, buffering, nameof(BufferingChanged));
                    }
                }
                break;

        }
    }

    private static ReadOnlySpan<byte> SiloPrefix => "silo-"u8;

    private void HandleClientMessage(MpvEvent ev)
    {
        if (ev.Data == IntPtr.Zero) return;

        try
        {
            var msg = Marshal.PtrToStructure<MpvEventClientMessage>(ev.Data);
            if (msg.NumArgs <= 0 || msg.Args == IntPtr.Zero) return;

            // Fast check: only process "silo-*" messages (Lua→Host intents).
            // Skip echo-backs of host→Lua messages (osc-mouse-move, osc-cursor-visible, etc.)
            // to avoid unnecessary allocations on the event thread.
            IntPtr firstArgPtr = Marshal.ReadIntPtr(msg.Args, 0);
            if (firstArgPtr == IntPtr.Zero) return;
            bool isSilo = true;
            for (int i = 0; i < SiloPrefix.Length; i++)
            {
                if (Marshal.ReadByte(firstArgPtr, i) != SiloPrefix[i])
                { isSilo = false; break; }
            }
            if (!isSilo) return;

            var args = new string[msg.NumArgs];
            for (int i = 0; i < msg.NumArgs; i++)
            {
                IntPtr strPtr = Marshal.ReadIntPtr(msg.Args, i * IntPtr.Size);
                args[i] = Marshal.PtrToStringUTF8(strPtr) ?? "";
            }

            InvokeSafely(ScriptMessageReceived, args, nameof(ScriptMessageReceived));
        }
        catch (Exception ex)
        {
            ReportErrorSafely($"HandleClientMessage failed: {ex.Message}");
        }
    }

    private void InvokeSafely(Action? handler, string eventName)
    {
        if (handler == null)
            return;

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action)subscriber)();
            }
            catch (Exception ex)
            {
                ReportErrorSafely($"{eventName} callback failed: {ex.Message}");
            }
        }
    }

    private void InvokeSafely<T>(Action<T>? handler, T value, string eventName)
    {
        if (handler == null)
            return;

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<T>)subscriber)(value);
            }
            catch (Exception ex)
            {
                ReportErrorSafely($"{eventName} callback failed: {ex.Message}");
            }
        }
    }

    private void InvokeFrameReadySafely(byte[] buffer, int width, int height, int stride)
    {
        var handler = FrameReady;
        if (handler == null)
            return;

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<byte[], int, int, int>)subscriber)(buffer, width, height, stride);
            }
            catch (Exception ex)
            {
                ReportErrorSafely($"FrameReady callback failed: {ex.Message}");
            }
        }
    }

    private void ReportErrorSafely(string message)
    {
        var handler = Error;
        if (handler == null)
            return;

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber)(message);
            }
            catch
            {
                // Error reporting must never tear down libmpv's event/render loop.
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string GetPlayerLogPath() => Path.Combine(
        Environment.GetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer"),
        "mpv_log.txt");

    private void SetOption(string name, string value)
    {
        int err = mpv_set_option_string(_mpvHandle, name, value);
        if (err < 0)
        {
            string errMsg = GetErrorString(err);
            ReportErrorSafely($"mpv_set_option_string({name}, {value}) failed: {errMsg}");
        }
    }

    private static string GetErrorString(int error)
    {
        IntPtr ptr = mpv_error_string(error);
        if (ptr == IntPtr.Zero)
            return $"Unknown error ({error})";
        return Marshal.PtrToStringUTF8(ptr) ?? $"Unknown error ({error})";
    }

    private void ThrowIfNotInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mpvHandle == IntPtr.Zero)
            throw new InvalidOperationException("MpvPlayer is not initialized. Call Initialize() first.");
    }

    // ── Dispose ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _diagnostics?.Dispose();

        // Clear event subscribers to prevent external memory leaks
        PositionChanged = null;
        DurationChanged = null;
        PauseChanged = null;
        PlaybackEnded = null;
        PlaybackRestarted = null;
        PlaybackError = null;
        DirectStreamError = null;
        EofReached = null;
        FileLoaded = null;
        UpscalingStatusChanged = null;
        BufferingChanged = null;
        FrameReady = null;
        ScriptMessageReceived = null;
        Error = null;

        // Signal render thread to exit
        _frameUpdateEvent.Set();

        // Wake the mpv event loop before waiting so both native-facing threads
        // have a chance to observe disposal promptly.
        if (_mpvHandle != IntPtr.Zero)
            mpv_wakeup(_mpvHandle);

        var renderStopped = WaitForThread(_renderThread, TimeSpan.FromSeconds(2));
        var eventStopped = WaitForThread(_eventThread, TimeSpan.FromSeconds(2));

        if (renderStopped && eventStopped)
        {
            CompleteNativeCleanup();
            return;
        }

        // Never free the render context, mpv handle, pinned buffers, or callback
        // state while either worker can still be inside native code. A rare slow
        // teardown is completed in the background after both workers actually exit.
        var cleanupThread = new Thread(() =>
        {
            WaitForThreadWithoutTimeout(_renderThread);
            WaitForThreadWithoutTimeout(_eventThread);
            CompleteNativeCleanup();
        })
        {
            IsBackground = true,
            Name = "Silo mpv deferred cleanup",
        };
        cleanupThread.Start();
    }

    private static bool WaitForThread(Thread? thread, TimeSpan timeout)
    {
        if (thread is null || !thread.IsAlive)
            return true;
        if (ReferenceEquals(thread, Thread.CurrentThread))
            return false;
        return thread.Join(timeout);
    }

    private static void WaitForThreadWithoutTimeout(Thread? thread)
    {
        if (thread is null || !thread.IsAlive || ReferenceEquals(thread, Thread.CurrentThread))
            return;
        thread.Join();
    }

    private void CompleteNativeCleanup()
    {
        if (Interlocked.Exchange(ref _nativeCleanupStarted, 1) != 0)
            return;

        // Free the render context only after the render thread is gone.
        if (_renderCtx != IntPtr.Zero)
        {
            mpv_render_context_free(_renderCtx);
            _renderCtx = IntPtr.Zero;
        }

        // Terminate and destroy the mpv instance
        if (_mpvHandle != IntPtr.Zero)
        {
            mpv_terminate_destroy(_mpvHandle);
            _mpvHandle = IntPtr.Zero;
        }

        // Free pinned buffers
        if (_frameBufferPin.IsAllocated)
            _frameBufferPin.Free();
        if (_renderSizePin.IsAllocated)
            _renderSizePin.Free();
        if (_renderStridePin.IsAllocated)
            _renderStridePin.Free();

        // Free render context creation artifacts
        if (_createParamsPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_createParamsPtr);
            _createParamsPtr = IntPtr.Zero;
        }
        if (_apiTypePin.IsAllocated)
            _apiTypePin.Free();

        _frameUpdateEvent.Dispose();
    }
}
