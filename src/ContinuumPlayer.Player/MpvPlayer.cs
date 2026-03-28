using System.Runtime.InteropServices;
using System.Text;
using static ContinuumPlayer.Player.MpvInterop;

namespace ContinuumPlayer.Player;

/// <summary>
/// High-level wrapper around libmpv that manages an embedded video player instance.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private IntPtr _mpvHandle;
    private IntPtr _childHwnd;
    private Thread? _eventThread;
    private volatile bool _disposed;

    // ── Public properties ────────────────────────────────────────────────

    /// <summary>Current playback position in seconds.</summary>
    public double Position { get; private set; }

    /// <summary>Total duration of the current media in seconds.</summary>
    public double Duration { get; private set; }

    /// <summary>Whether playback is currently paused.</summary>
    public bool IsPaused { get; private set; } = true;

    /// <summary>Whether a file is loaded and playing (not paused, not ended).</summary>
    public bool IsPlaying => !IsPaused && Duration > 0;

    // ── Events ───────────────────────────────────────────────────────────

    /// <summary>Fired when the playback position changes.</summary>
    public event Action<double>? PositionChanged;

    /// <summary>Fired when the media duration is known or changes.</summary>
    public event Action<double>? DurationChanged;

    /// <summary>Fired when the pause state changes.</summary>
    public event Action<bool>? PauseChanged;

    /// <summary>Fired when playback reaches end-of-file or the file ends.</summary>
    public event Action? PlaybackEnded;

    /// <summary>Fired when a file has been loaded and decoding starts.</summary>
    public event Action? FileLoaded;

    /// <summary>Fired when an error occurs.</summary>
    public event Action<string>? Error;

    // ── Reply userdata IDs for observed properties ───────────────────────

    private const ulong UD_TIME_POS    = 1;
    private const ulong UD_DURATION    = 2;
    private const ulong UD_PAUSE       = 3;
    private const ulong UD_EOF_REACHED = 4;

    // ── Initialization ───────────────────────────────────────────────────

    /// <summary>
    /// Creates the mpv instance, a child window for video output, and starts the event loop.
    /// Must be called from the UI thread (needs a valid parent HWND).
    /// </summary>
    /// <param name="parentHwnd">The HWND of the parent window to host the video surface.</param>
    /// <param name="width">Initial width of the video surface.</param>
    /// <param name="height">Initial height of the video surface.</param>
    public void Initialize(IntPtr parentHwnd, int width, int height)
    {
        if (_mpvHandle != IntPtr.Zero)
            throw new InvalidOperationException("MpvPlayer is already initialized.");

        // 1. Create mpv instance
        _mpvHandle = mpv_create();
        if (_mpvHandle == IntPtr.Zero)
            throw new InvalidOperationException("mpv_create() returned null.");

        // 2. Create a child window for video rendering
        _childHwnd = CreateWindowExW(
            0,                              // dwExStyle
            "Static",                       // window class
            "",                             // window name
            WS_CHILD | WS_VISIBLE,          // style
            0, 0, width, height,            // position and size
            parentHwnd,                     // parent
            IntPtr.Zero,                    // menu
            IntPtr.Zero,                    // hInstance
            IntPtr.Zero);                   // lpParam

        if (_childHwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                $"CreateWindowExW failed (error {Marshal.GetLastWin32Error()}).");

        // 3. Set mpv options BEFORE mpv_initialize
        // The wid option takes the window handle as a decimal string
        SetOption("wid", _childHwnd.ToInt64().ToString());
        SetOption("vo", "gpu");
        SetOption("gpu-api", "d3d11");
        SetOption("hwdec", "d3d11va-copy");
        SetOption("keep-open", "yes");
        SetOption("idle", "yes");
        SetOption("osc", "no");
        SetOption("cache", "yes");

        // 4. Initialize mpv
        int err = mpv_initialize(_mpvHandle);
        if (err < 0)
        {
            string errMsg = GetErrorString(err);
            mpv_destroy(_mpvHandle);
            _mpvHandle = IntPtr.Zero;
            DestroyWindow(_childHwnd);
            _childHwnd = IntPtr.Zero;
            throw new InvalidOperationException($"mpv_initialize failed: {errMsg}");
        }

        // 5. Observe properties (after initialize)
        mpv_observe_property(_mpvHandle, UD_TIME_POS, "time-pos", MPV_FORMAT_DOUBLE);
        mpv_observe_property(_mpvHandle, UD_DURATION, "duration", MPV_FORMAT_DOUBLE);
        mpv_observe_property(_mpvHandle, UD_PAUSE, "pause", MPV_FORMAT_FLAG);
        mpv_observe_property(_mpvHandle, UD_EOF_REACHED, "eof-reached", MPV_FORMAT_FLAG);

        // 6. Start background event loop thread
        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "MpvEventLoop"
        };
        _eventThread.Start();
    }

    // ── Playback controls ────────────────────────────────────────────────

    /// <summary>
    /// Loads a media file from the given URL.
    /// </summary>
    /// <param name="url">The URL or file path to load.</param>
    /// <param name="authHeader">Optional Authorization header value (e.g. "Bearer token").</param>
    public void LoadFile(string url, string? authHeader = null)
    {
        ThrowIfNotInitialized();

        if (!string.IsNullOrEmpty(authHeader))
        {
            mpv_set_property_string(_mpvHandle, "http-header-fields",
                $"Authorization: {authHeader}");
        }

        Command("loadfile", url);
    }

    /// <summary>Resumes playback.</summary>
    public void Play()
    {
        ThrowIfNotInitialized();
        mpv_set_property_string(_mpvHandle, "pause", "no");
    }

    /// <summary>Pauses playback.</summary>
    public void Pause()
    {
        ThrowIfNotInitialized();
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
        Command("seek", seconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), "absolute");
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
    /// Adds an external subtitle track.
    /// </summary>
    /// <param name="url">URL or file path of the subtitle file.</param>
    /// <param name="title">Optional display title.</param>
    /// <param name="lang">Optional language code.</param>
    public void AddSubtitle(string url, string? title = null, string? lang = null)
    {
        ThrowIfNotInitialized();

        var args = new List<string> { "sub-add", url };
        if (!string.IsNullOrEmpty(title))
        {
            args.Add("auto"); // flags
            args.Add(title);
            if (!string.IsNullOrEmpty(lang))
                args.Add(lang);
        }

        Command(args.ToArray());
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

    /// <summary>
    /// Resizes the child video window.
    /// </summary>
    public void ResizeVideoWindow(int width, int height)
    {
        if (_childHwnd != IntPtr.Zero)
            MoveWindow(_childHwnd, 0, 0, width, height, true);
    }

    // ── Command helper ───────────────────────────────────────────────────

    /// <summary>
    /// Sends a command to mpv. Each arg is converted to a UTF-8 null-terminated byte array,
    /// pinned, and passed as a null-terminated IntPtr array to mpv_command.
    /// </summary>
    private void Command(params string[] args)
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
                Error?.Invoke($"mpv_command [{string.Join(" ", args)}] failed: {errMsg}");
            }
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

    // ── Event loop ───────────────────────────────────────────────────────

    private void EventLoop()
    {
        while (!_disposed)
        {
            IntPtr evPtr = mpv_wait_event(_mpvHandle, 0.5);
            if (evPtr == IntPtr.Zero)
                continue;

            var ev = Marshal.PtrToStructure<MpvEvent>(evPtr);

            switch (ev.EventId)
            {
                case MPV_EVENT_NONE:
                    // Timeout or wakeup, nothing to do
                    break;

                case MPV_EVENT_PROPERTY_CHANGE:
                    HandlePropertyChange(ev);
                    break;

                case MPV_EVENT_FILE_LOADED:
                    FileLoaded?.Invoke();
                    break;

                case MPV_EVENT_END_FILE:
                    PlaybackEnded?.Invoke();
                    break;

                case MPV_EVENT_SHUTDOWN:
                    return; // Exit the event loop
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
            case UD_TIME_POS:
                if (prop.Format == MPV_FORMAT_DOUBLE && prop.Data != IntPtr.Zero)
                {
                    double pos = Marshal.PtrToStructure<double>(prop.Data);
                    Position = pos;
                    PositionChanged?.Invoke(pos);
                }
                break;

            case UD_DURATION:
                if (prop.Format == MPV_FORMAT_DOUBLE && prop.Data != IntPtr.Zero)
                {
                    double dur = Marshal.PtrToStructure<double>(prop.Data);
                    Duration = dur;
                    DurationChanged?.Invoke(dur);
                }
                break;

            case UD_PAUSE:
                if (prop.Format == MPV_FORMAT_FLAG && prop.Data != IntPtr.Zero)
                {
                    int flag = Marshal.PtrToStructure<int>(prop.Data);
                    bool paused = flag != 0;
                    IsPaused = paused;
                    PauseChanged?.Invoke(paused);
                }
                break;

            case UD_EOF_REACHED:
                if (prop.Format == MPV_FORMAT_FLAG && prop.Data != IntPtr.Zero)
                {
                    int flag = Marshal.PtrToStructure<int>(prop.Data);
                    if (flag != 0)
                        PlaybackEnded?.Invoke();
                }
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void SetOption(string name, string value)
    {
        int err = mpv_set_option_string(_mpvHandle, name, value);
        if (err < 0)
        {
            string errMsg = GetErrorString(err);
            Error?.Invoke($"mpv_set_option_string({name}, {value}) failed: {errMsg}");
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

        // Wake up the event loop so it can exit
        if (_mpvHandle != IntPtr.Zero)
            mpv_wakeup(_mpvHandle);

        // Wait for the event thread to finish
        if (_eventThread is not null && _eventThread.IsAlive)
            _eventThread.Join(TimeSpan.FromSeconds(2));

        // Terminate and destroy the mpv instance
        if (_mpvHandle != IntPtr.Zero)
        {
            mpv_terminate_destroy(_mpvHandle);
            _mpvHandle = IntPtr.Zero;
        }

        // Destroy the child window
        if (_childHwnd != IntPtr.Zero)
        {
            DestroyWindow(_childHwnd);
            _childHwnd = IntPtr.Zero;
        }
    }
}
