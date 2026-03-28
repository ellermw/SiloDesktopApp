using System.Runtime.InteropServices;

namespace ContinuumPlayer.Player;

/// <summary>
/// Raw P/Invoke declarations for libmpv and Win32 window management.
/// </summary>
internal static class MpvInterop
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
    public const int MPV_EVENT_END_FILE         = 7;
    public const int MPV_EVENT_FILE_LOADED      = 8;
    public const int MPV_EVENT_PROPERTY_CHANGE  = 22;

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

    // ── libmpv functions ─────────────────────────────────────────────────

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr ctx);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_destroy(IntPtr ctx);

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
    public static extern int mpv_get_property(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format, out double data);

    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    public static extern int mpv_get_property_int(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format, out long data);

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
    public static extern void mpv_free(IntPtr data);

    /// <summary>
    /// Returns a static string describing the error code. The pointer is valid forever.
    /// </summary>
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_error_string(int error);

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
}
