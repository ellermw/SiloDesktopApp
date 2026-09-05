using System.Runtime.InteropServices;

namespace SiloPlayer.Player;

/// <summary>
/// Raw P/Invoke declarations for libmpv and Win32 window management.
/// </summary>
public static class MpvInterop
{
    private const string LibMpv = "libmpv-2.dll";

    // ── mpv_format constants ─────────────────────────────────────────────

    public const int MPV_FORMAT_NONE   = 0;
    public const int MPV_FORMAT_STRING = 1;
    public const int MPV_FORMAT_FLAG   = 3;
    public const int MPV_FORMAT_INT64  = 4;
    public const int MPV_FORMAT_DOUBLE = 5;

    // ── mpv_event_id constants ───────────────────────────────────────────

    public const int MPV_EVENT_NONE            = 0;
    public const int MPV_EVENT_SHUTDOWN         = 1;
    public const int MPV_EVENT_LOG_MESSAGE      = 2;
    public const int MPV_EVENT_END_FILE         = 7;
    public const int MPV_EVENT_FILE_LOADED      = 8;
    public const int MPV_EVENT_CLIENT_MESSAGE    = 16;
    public const int MPV_EVENT_PLAYBACK_RESTART  = 21;
    public const int MPV_EVENT_PROPERTY_CHANGE  = 22;

    // ── mpv_end_file_reason constants ───────────────────────────────────
    public const int MPV_END_FILE_REASON_EOF      = 0;
    public const int MPV_END_FILE_REASON_STOP     = 2;
    public const int MPV_END_FILE_REASON_QUIT     = 3;
    public const int MPV_END_FILE_REASON_ERROR    = 4;

    // ── Structs ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the native mpv_event struct.
    /// Layout: event_id (int, 4 bytes), pad (4 bytes on 64-bit), error (int, 4 bytes),
    /// pad (4 bytes on 64-bit), reply_userdata (uint64), data (pointer).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public int EventId;        // mpv_event_id (enum = int)
        public int Error;          // int
        public ulong ReplyUserdata; // uint64_t
        public IntPtr Data;        // void*
    }

    /// <summary>
    /// Mirrors the native mpv_event_property struct.
    /// Layout: name (const char*), format (mpv_format enum = int), pad, data (void*).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventProperty
    {
        public IntPtr Name;   // const char*
        public int Format;    // mpv_format (enum = int)
        public IntPtr Data;   // void*
    }

    /// <summary>
    /// Mirrors the native mpv_event_end_file struct.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventEndFile
    {
        public int Reason;  // mpv_end_file_reason
        public int Error;   // mpv error code (only if reason == ERROR)
    }

    /// <summary>
    /// Mirrors the native mpv_event_client_message struct.
    /// Layout: num_args (int), pad (4 bytes on 64-bit), args (char**).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventClientMessage
    {
        public int NumArgs;    // int
        public IntPtr Args;    // char** (array of null-terminated UTF-8 strings)
    }

    // ── libmpv functions ─────────────────────────────────────────────────

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr ctx);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr ctx);

    /// <summary>
    /// Send a command to the player.
    /// args is a NULL-terminated array of UTF-8 string pointers.
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command(IntPtr ctx, IntPtr[] args);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_get_property_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int mpv_get_property_int(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format, out long data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int mpv_get_property_double(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format, out double data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_observe_property(IntPtr mpv, ulong reply_userdata,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);

    /// <summary>
    /// Wait for the next event, or until the timeout expires.
    /// Returns a pointer to an mpv_event struct (owned by mpv; valid until next call).
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_wakeup(IntPtr ctx);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_request_log_messages(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string level);

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEventLogMessage
    {
        public IntPtr Prefix;
        public IntPtr Level;
        public IntPtr Text;
        public int LogLevel;
    }

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free(IntPtr data);

    /// <summary>
    /// Returns a static string describing the error code. The pointer is valid forever.
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_error_string(int error);

    // ── Render API ──────────────────────────────────────────────────────

    /// <summary>mpv_render_param types (MPV_RENDER_PARAM_*).</summary>
    public const int MPV_RENDER_PARAM_INVALID    = 0;
    public const int MPV_RENDER_PARAM_API_TYPE   = 1;
    public const int MPV_RENDER_PARAM_SW_SIZE    = 17;
    public const int MPV_RENDER_PARAM_SW_FORMAT  = 18;
    public const int MPV_RENDER_PARAM_SW_STRIDE  = 19;
    public const int MPV_RENDER_PARAM_SW_POINTER = 20;

    /// <summary>Flags returned by mpv_render_context_update.</summary>
    public const ulong MPV_RENDER_UPDATE_FRAME = 1;

    /// <summary>
    /// Mirrors the native mpv_render_param struct: { int type; void *data; }.
    /// On 64-bit, the int is padded to 8 bytes before the pointer.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MpvRenderParam
    {
        public IntPtr Type;  // stored as IntPtr-sized for alignment (matches C enum on 64-bit)
        public IntPtr Data;
    }

    /// <summary>
    /// Creates a render context for the given mpv handle.
    /// params must point to a null-terminated array of MpvRenderParam structs.
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_render_context_create(out IntPtr ctx, IntPtr mpv, IntPtr @params);

    /// <summary>
    /// Renders the current video frame into the target described by params.
    /// params must point to a null-terminated array of MpvRenderParam structs.
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_render_context_render(IntPtr ctx, IntPtr @params);

    /// <summary>
    /// Frees the render context. Must be called before mpv_terminate_destroy.
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_render_context_free(IntPtr ctx);

    /// <summary>
    /// Sets a callback that is invoked when a new video frame should be rendered.
    /// The callback is invoked from any mpv thread and must not call mpv APIs directly.
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_render_context_set_update_callback(IntPtr ctx, IntPtr callback, IntPtr callback_ctx);

    /// <summary>
    /// Returns a set of flags indicating what should be re-rendered.
    /// Must be called from the render thread after the update callback fires.
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong mpv_render_context_update(IntPtr ctx);

    /// <summary>Delegate matching mpv_render_update_fn (void (*)(void *ctx)).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MpvRenderUpdateFn(IntPtr ctx);

    // ── Win32 window management ──────────────────────────────────────────

    public const uint WS_CHILD   = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight,
        [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // ── Monitor detection (for fullscreen on correct monitor) ────────────

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// Determines the zero-based monitor index for the monitor containing the given window.
    /// Returns 0 if detection fails.
    /// </summary>
    public static int GetMonitorIndex(IntPtr hwnd)
    {
        var targetMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        int index = 0;
        int result = 0; // default to monitor 0
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            if (hMonitor == targetMonitor)
                result = index;
            index++;
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
