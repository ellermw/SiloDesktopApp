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
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    // Only custom events — mpv handles standard input (Space/arrows/etc) via its own bindings
    public event Action? EscapeRequested;       // Escape: exit fullscreen or exit playback
    public event Action? MinimizeRequested;     // N: minimize to mini bar

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
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    private static readonly IntPtr IDC_ARROW = new(32512);

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

        // Create a popup window (top-level, no parent-child relationship)
        // WS_EX_TOOLWINDOW hides it from the taskbar
        GetWindowRect(parentHwnd, out var parentRect);

        _hwnd = CreateWindowExW(
            WS_EX_TOOLWINDOW,
            ClassName,
            "Continuum Player",
            WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            parentRect.Left, parentRect.Top,
            parentRect.Right - parentRect.Left,
            parentRect.Bottom - parentRect.Top,
            IntPtr.Zero, // No parent — this is a top-level window
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);
    }

    public void Show()
    {
        if (_hwnd == IntPtr.Zero) return;
        MatchParentPosition();
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        // Put on top of main window
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOACTIVATE | 0x0001 /*SWP_NOSIZE*/ | 0x0002 /*SWP_NOMOVE*/ | SWP_SHOWWINDOW);
        SetForegroundWindow(_hwnd);
    }

    public void Hide()
    {
        if (_hwnd == IntPtr.Zero) return;
        ShowWindow(_hwnd, SW_HIDE);
    }

    public void MatchParentPosition()
    {
        if (_hwnd == IntPtr.Zero || _parentHwnd == IntPtr.Zero) return;
        GetWindowRect(_parentHwnd, out var r);
        SetWindowPos(_hwnd, IntPtr.Zero,
            r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private IntPtr WndProcInstance(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_KEYDOWN = 0x0100;
        const uint WM_SYSKEYDOWN = 0x0104;

        // Only intercept our custom keys — mpv handles everything else
        // (Space, arrows, F, M, Q via its own input bindings + OSC)
        if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
        {
            int vk = (int)wParam & 0xFF;
            if (vk == 0x1B) // Escape
            {
                EscapeRequested?.Invoke();
                return IntPtr.Zero;
            }
            if (vk == 0x4E) // N — minimize to mini bar
            {
                MinimizeRequested?.Invoke();
                return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

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
