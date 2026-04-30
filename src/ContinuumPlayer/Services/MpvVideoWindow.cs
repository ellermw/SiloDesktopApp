using System.Runtime.InteropServices;

namespace ContinuumPlayer.Services;

/// <summary>
/// A native Win32 popup window for mpv GPU-accelerated rendering.
/// Completely separate from the WinUI 3 compositor — no airspace conflicts.
/// mpv renders directly via D3D11 with vo=gpu, zero frame copies.
/// </summary>
public sealed class MpvVideoWindow : IDisposable
{
    private IntPtr _hwnd;
    private IntPtr _parentHwnd;
    private GCHandle _wndProcHandle;
    private bool _disposed;
    private const string ClassName = "ContinuumMpvHost";
    private static bool _classRegistered;

    // Win32 constants
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080; // Hide from taskbar
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int GWL_STYLE = -16;

    public event Action? EscapeRequested;
    public event Action? MinimizeRequested;
    public event Action? FullscreenToggleRequested;
    public event Action? ExpandRequested;

    // Reference to mpv for forwarding input events
    private ContinuumPlayer.Player.MpvPlayer? _mpv;

    // Win32 imports
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    private const uint TME_LEAVE = 0x00000002;

    private static readonly IntPtr IDC_ARROW = new(32512);

    // Set when TrackMouseEvent is armed; cleared when WM_MOUSELEAVE fires.
    // Ensures we only re-arm after each leave — avoids piling up tracking requests.
    private bool _mouseTracking;

    // Cursor visibility — driven by the Lua OSC. When the controls fade out,
    // Lua sends `continuum-cursor-hidden`; PlayerService routes that to
    // SetCursorVisible(false), which makes WM_SETCURSOR return a NULL cursor
    // until the OSC reappears. Mirrors webui's `cursor-none` toggle (16cf2b4).
    private bool _cursorHidden;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public IntPtr Hwnd => _hwnd;

    public void SetMpv(ContinuumPlayer.Player.MpvPlayer mpv) => _mpv = mpv;

    /// <summary>
    /// Toggle the Win32 cursor over the mpv video window. The Lua OSC
    /// drives this in lock-step with control bar visibility — controls
    /// fade out → cursor hides; mouse moves or controls reappear →
    /// cursor restored.
    /// </summary>
    public void SetCursorVisible(bool visible)
    {
        bool hide = !visible;
        if (_cursorHidden == hide) return;
        _cursorHidden = hide;
        // Apply immediately if the cursor is currently over our window —
        // SetCursor only takes effect synchronously when we own the
        // message; otherwise the next WM_SETCURSOR will pick up the flag.
        if (_hwnd != IntPtr.Zero)
            SetCursor(hide ? IntPtr.Zero : LoadCursorW(IntPtr.Zero, IDC_ARROW));
    }

    public void Create(IntPtr parentHwnd)
    {
        _parentHwnd = parentHwnd;

        if (!_classRegistered)
        {
            WndProcDelegate wndProc = WndProcInstance;
            _wndProcHandle = GCHandle.Alloc(wndProc);

            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                style = 0x0008, // CS_DBLCLKS — enable double-click messages
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = GetModuleHandleW(null),
                hCursor = LoadCursorW(IntPtr.Zero, IDC_ARROW), // Normal arrow cursor
                hbrBackground = IntPtr.Zero, // Black background (mpv will paint over)
                lpszClassName = ClassName
            };

            RegisterClassExW(ref wc);
            _classRegistered = true;
        }

        // Create an OWNED popup window — owned windows always stay above their owner.
        // WS_EX_TOOLWINDOW hides it from the taskbar.
        // Passing parentHwnd as hWndParent with WS_POPUP creates ownership (not child).
        GetWindowRect(parentHwnd, out var parentRect);

        _hwnd = CreateWindowExW(
            WS_EX_TOOLWINDOW,
            ClassName,
            "Continuum Player",
            WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            parentRect.Left, parentRect.Top,
            parentRect.Right - parentRect.Left,
            parentRect.Bottom - parentRect.Top,
            parentHwnd, // Owner — popup stays above this window automatically
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);
    }

    public void Show()
    {
        if (_hwnd == IntPtr.Zero) return;
        _isMiniBar = false;
        MatchParentPosition();
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
    }

    public void EnterFullscreen()
    {
        if (_hwnd == IntPtr.Zero) return;
        GetWindowRect(_hwnd, out _savedRect);
        var monitor = MonitorFromWindow(_hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref mi);
        _fullscreenRect = mi.rcMonitor;
        _isFullscreen = true;
        // Position fullscreen and bring to top, but NOT topmost — topmost
        // forces the player above every other window including file
        // explorer/browsers the user drags over, which is more aggressive
        // than the webui (a normal browser tab). HWND_TOP moves the window
        // to the front of non-topmost windows, same Z-order as any focused
        // window, so clicking away lets other windows cover it naturally.
        SetWindowPos(_hwnd, HWND_TOP,
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left,
            mi.rcMonitor.Bottom - mi.rcMonitor.Top,
            SWP_SHOWWINDOW);
        SetForegroundWindow(_hwnd);
        // Re-assert position after 200ms — mpv's internal fullscreen handling
        // may reposition the window after our call.
        System.Threading.Tasks.Task.Delay(200).ContinueWith(_ =>
        {
            if (_isFullscreen && _hwnd != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, HWND_TOP,
                    _fullscreenRect.Left, _fullscreenRect.Top,
                    _fullscreenRect.Right - _fullscreenRect.Left,
                    _fullscreenRect.Bottom - _fullscreenRect.Top,
                    SWP_SHOWWINDOW);
                SetForegroundWindow(_hwnd);
            }
        });
    }

    private RECT _fullscreenRect;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    public void ExitFullscreen()
    {
        if (_hwnd == IntPtr.Zero) return;
        _isFullscreen = false;
        // Owned window — just reposition to client area, z-order is automatic.
        MatchParentPosition();
    }

    public bool IsFullscreen => _isFullscreen;
    private bool _isFullscreen;
    private bool _isMiniBar;
    private RECT _savedRect;

    // Click-to-pause detection: a "clean" click is a short press with minimal
    // pointer movement. On WM_LBUTTONUP we hand the hit-test to Lua
    // (osc-video-click), which toggles pause iff the point is empty video.
    private long _lbuttonDownTicks;
    private int _lbuttonDownX;
    private int _lbuttonDownY;
    private const int CleanClickMaxMs = 300;
    private const int CleanClickMaxDeltaPx = 5;

    public void Hide()
    {
        if (_hwnd == IntPtr.Zero) return;
        ShowWindow(_hwnd, SW_HIDE);
    }

    /// <summary>
    /// Positions the popup at a specific screen rectangle (for mini-bar thumbnail).
    /// </summary>
    public void PositionAt(int x, int y, int width, int height)
    {
        if (_hwnd == IntPtr.Zero) return;
        _isMiniBar = true;
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(_hwnd, HWND_TOP, x, y, width, height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void MatchParentPosition()
    {
        if (_hwnd == IntPtr.Zero || _parentHwnd == IntPtr.Zero) return;
        // Use client area (excludes title bar) so the title bar stays visible
        GetClientRect(_parentHwnd, out var client);
        var topLeft = new POINT { X = client.Left, Y = client.Top };
        ClientToScreen(_parentHwnd, ref topLeft);
        // Owned window stays above owner automatically — just reposition
        SetWindowPos(_hwnd, IntPtr.Zero,
            topLeft.X, topLeft.Y,
            client.Right - client.Left, client.Bottom - client.Top,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private IntPtr WndProcInstance(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_MOUSEMOVE = 0x0200;
        const uint WM_LBUTTONDOWN = 0x0201;
        const uint WM_LBUTTONUP = 0x0202;
        const uint WM_LBUTTONDBLCLK = 0x0203;
        const uint WM_RBUTTONUP = 0x0205;
        const uint WM_MOUSEWHEEL = 0x020A;
        const uint WM_MOUSELEAVE = 0x02A3;
        const uint WM_SETCURSOR = 0x0020;
        const uint WM_KEYDOWN = 0x0100;

        int loWord(IntPtr lp) => (short)(lp.ToInt64() & 0xFFFF);
        int hiWord(IntPtr lp) => (short)((lp.ToInt64() >> 16) & 0xFFFF);

        // Forward mouse events to mpv — both raw input (for mpv bindings)
        // and script messages (for continuum-osc.lua).
        // When embedded with wid=, mpv's mouse-pos property doesn't update from
        // the "mouse" command, so we also send script-messages with coordinates.
        if (msg == WM_MOUSEMOVE)
        {
            int x = loWord(lParam), y = hiWord(lParam);
            _mpv?.SendMousePos(x, y);
            _mpv?.SendScriptMessage("osc-mouse-move", x.ToString(), y.ToString());

            // Arm TrackMouseEvent so we get a single WM_MOUSELEAVE when the cursor
            // leaves this window. Without this, the OSC can stay visible forever
            // if the user flicks the mouse off-window while it's over the bar —
            // the Lua hide timer skips hiding while mouse_in_bar is true.
            if (!_mouseTracking)
            {
                var tme = new TRACKMOUSEEVENT
                {
                    cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                    dwFlags = TME_LEAVE,
                    hwndTrack = hWnd,
                    dwHoverTime = 0,
                };
                if (TrackMouseEvent(ref tme)) _mouseTracking = true;
            }
        }
        else if (msg == WM_MOUSELEAVE)
        {
            _mouseTracking = false;
            _mpv?.SendScriptMessage("osc-mouse-leave");
        }
        else if (msg == WM_SETCURSOR)
        {
            // Only override when the hit-test target is the window's client
            // area (low word of lParam == HTCLIENT == 1). Otherwise let the
            // default proc handle frame/border cursors.
            if ((lParam.ToInt64() & 0xFFFF) == 1 && _cursorHidden)
            {
                SetCursor(IntPtr.Zero);
                return new IntPtr(1); // TRUE — we handled it
            }
        }
        else if (msg == WM_LBUTTONDOWN)
        {
            // In mini-bar mode (small popup), click expands back to full player
            if (_isMiniBar) { ExpandRequested?.Invoke(); return IntPtr.Zero; }

            int x = loWord(lParam), y = hiWord(lParam);
            SetCapture(hWnd); // Capture mouse so we get WM_LBUTTONUP even outside window
            _lbuttonDownTicks = Environment.TickCount64;
            _lbuttonDownX = x;
            _lbuttonDownY = y;
            _mpv?.SendMousePos(x, y);
            _mpv?.SendScriptMessage("osc-mouse-move", x.ToString(), y.ToString());
            _mpv?.SendScriptMessage("osc-mouse-down", x.ToString(), y.ToString());
            _mpv?.SendKeydown("MBTN_LEFT");
        }
        else if (msg == WM_LBUTTONUP)
        {
            ReleaseCapture();
            int x = loWord(lParam), y = hiWord(lParam);
            _mpv?.SendMousePos(x, y);
            _mpv?.SendScriptMessage("osc-mouse-up", x.ToString(), y.ToString());
            _mpv?.SendKeyup("MBTN_LEFT");

            // Clean click? Delegate hit-check to Lua which toggles pause if
            // the point isn't on OSC chrome or an open menu overlay.
            long elapsed = Environment.TickCount64 - _lbuttonDownTicks;
            int dx = x - _lbuttonDownX, dy = y - _lbuttonDownY;
            if (_lbuttonDownTicks > 0
                && elapsed <= CleanClickMaxMs
                && Math.Abs(dx) <= CleanClickMaxDeltaPx
                && Math.Abs(dy) <= CleanClickMaxDeltaPx)
            {
                _mpv?.SendScriptMessage("osc-video-click", x.ToString(), y.ToString());
            }
            _lbuttonDownTicks = 0;
        }
        else if (msg == WM_LBUTTONDBLCLK)
        {
            _mpv?.SendKeypress("MBTN_LEFT_DBL");
        }
        else if (msg == WM_RBUTTONUP)
        {
            _mpv?.SendKeypress("MBTN_RIGHT");
        }
        else if (msg == WM_MOUSEWHEEL)
        {
            int delta = hiWord(wParam);
            _mpv?.SendKeypress(delta > 0 ? "WHEEL_UP" : "WHEEL_DOWN");
        }
        else if (msg == WM_KEYDOWN)
        {
            int vk = (int)wParam & 0xFF;
            // Escape — our custom handling
            if (vk == 0x1B) { EscapeRequested?.Invoke(); return IntPtr.Zero; }
            // N — minimize
            if (vk == 0x4E) { MinimizeRequested?.Invoke(); return IntPtr.Zero; }
            // F — fullscreen toggle (handled by host, not mpv)
            if (vk == 0x46) { FullscreenToggleRequested?.Invoke(); return IntPtr.Zero; }
            // Forward all other keys to mpv
            var keyName = VkToMpvKey(vk);
            if (keyName != null) _mpv?.SendKeypress(keyName);
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static string? VkToMpvKey(int vk) => vk switch
    {
        0x20 => "SPACE",
        0x25 => "LEFT",
        0x26 => "UP",
        0x27 => "RIGHT",
        0x28 => "DOWN",
        0x0D => "ENTER",
        0x09 => "TAB",
        0x08 => "BS",
        0x2E => "DEL",
        0x24 => "HOME",
        0x23 => "END",
        0x21 => "PGUP",
        0x22 => "PGDWN",
        0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
        0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
        0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
        0x46 => null, // F key — handled directly in WndProc, not forwarded to mpv
        >= 0x41 and <= 0x5A => ((char)vk).ToString().ToLower(), // A-Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(), // 0-9
        _ => null
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (_wndProcHandle.IsAllocated)
            _wndProcHandle.Free();
    }
}
