using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

internal static class TransparentFixtureWindow
{
    // Only the fixture's own HWND is touched. It remains entirely transparent,
    // click-through and nonactivating while Windows assigns the real monitor DPI.
    internal static void Configure(Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId != Environment.ProcessId) throw new InvalidOperationException("Not a fixture window.");
        const int extendedStyle = -20;
        const long layered = 0x80000, transparent = 0x20, noActivate = 0x8000000;
        SetWindowLongPtr(hwnd, extendedStyle, new nint(GetWindowLongPtr(hwnd, extendedStyle).ToInt64() | layered | transparent | noActivate));
        if (!SetLayeredWindowAttributes(hwnd, 0, 0, 2)) throw new System.ComponentModel.Win32Exception();
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint color, byte alpha, uint flags);
}
