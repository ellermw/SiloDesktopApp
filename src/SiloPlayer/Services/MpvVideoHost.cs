using System.Runtime.InteropServices;

namespace SiloPlayer.Services;

/// <summary>
/// Manages a native Win32 child window that mpv renders into via wid=.
/// The child HWND sits on top of the XAML visual tree (WinUI 3 airspace behavior).
/// </summary>
public class MpvVideoHost : IDisposable
{
    private IntPtr _hostHwnd;
    private IntPtr _parentHwnd;
    private bool _disposed;

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public IntPtr Hwnd => _hostHwnd;

    public void Create(IntPtr parentHwnd)
    {
        _parentHwnd = parentHwnd;

        // Create a simple child window using the built-in "Static" window class
        _hostHwnd = CreateWindowExW(
            WS_EX_NOACTIVATE,
            "Static",          // Built-in window class (no custom WndProc needed)
            "MpvVideoHost",
            WS_CHILD | WS_CLIPSIBLINGS,  // Start hidden (no WS_VISIBLE)
            0, 0, 1, 1,       // Initial size (will be resized)
            parentHwnd,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    public void Show()
    {
        if (_hostHwnd == IntPtr.Zero) return;
        Resize(); // Fill parent
        ShowWindow(_hostHwnd, SW_SHOW);
    }

    public void Hide()
    {
        if (_hostHwnd == IntPtr.Zero) return;
        ShowWindow(_hostHwnd, SW_HIDE);
    }

    public void Resize()
    {
        if (_hostHwnd == IntPtr.Zero || _parentHwnd == IntPtr.Zero) return;
        GetClientRect(_parentHwnd, out var rect);
        SetWindowPos(_hostHwnd, IntPtr.Zero,
            0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hostHwnd != IntPtr.Zero)
        {
            DestroyWindow(_hostHwnd);
            _hostHwnd = IntPtr.Zero;
        }
    }
}
