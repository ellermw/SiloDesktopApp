using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using Windows.Graphics;

namespace SiloPlayer.Services;

/// <summary>
/// A nonactivating window owned by the video HWND. Its native region is the
/// union of the toast cards: gaps and the rest of the surface belong to mpv.
/// No transparent fullscreen input shield, topmost flag or polling timer.
/// </summary>
internal sealed class PlaybackToastWindow : IDisposable
{
    private const int GwlpHwndParent = -8;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const uint SwpNoActivate = 0x0010;
    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _owner;
    private readonly SubclassProc _subclass;
    private RectInt32[]? _lastRegions;
    private double _lastScale;
    private bool _closed;
    private bool _disposed;

    internal Grid Root { get; } = new();
    internal event Action? Closed;

    internal PlaybackToastWindow(IntPtr videoHwnd)
    {
        _owner = videoHwnd;
        _window = new Window { Content = Root, Title = "Silo Player" };
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _subclass = WindowProc;
        try
        {
            SetWindowLongPtrW(_hwnd, GwlpHwndParent, videoHwnd);
            if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
            }
            SetWindowLongPtrW(_hwnd, GwlExStyle, new IntPtr(
                (GetWindowLongPtrW(_hwnd, GwlExStyle).ToInt64() | WsExNoActivate | WsExToolWindow) & ~0x00060301L));
            // As in the playback menu host, remove any retained classic frame.
            SetWindowLongPtrW(_hwnd, -16, new IntPtr(GetWindowLongPtrW(_hwnd, -16).ToInt64() & ~0x00C40000L));
            if (!SetWindowSubclass(_hwnd, _subclass, 1, 0)) throw new Win32Exception();
            ApplyRegion([]); // Never flash an opaque, full-video WinUI surface.
            Root.LayoutUpdated += OnLayoutUpdated;
            _window.Closed += OnClosed;
        }
        catch { Dispose(); throw; }
    }

    internal static bool IsSurfaceVisible(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindowVisible(hwnd);

    internal void Show()
    {
        Position();
        _window.AppWindow.Show(false);
    }

    internal void Position()
    {
        if (_closed || _disposed || !GetWindowRect(_owner, out var rect)) return;
        SetWindowPos(_hwnd, IntPtr.Zero, rect.Left, rect.Top,
            Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top),
            SwpNoActivate | 0x0004 | 0x0020); // NOZORDER | FRAMECHANGED
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (_disposed || _closed || Root.XamlRoot == null) return;
        try
        {
            var scale = Root.XamlRoot.RasterizationScale;
            var rectangles = new List<RectInt32>();
            if (Root.Children.FirstOrDefault() is ToastContainer container)
            {
                foreach (var toast in container.Toasts)
                {
                    // Use the layout slot, excluding the slide-in transform.
                    // Only the card's final rectangle captures input, including
                    // while its contents animate in or fade out.
                    if (VisualTreeHelper.GetParent(toast) is not UIElement parent) continue;
                    var slot = LayoutInformation.GetLayoutSlot(toast);
                    var bounds = parent.TransformToVisual(Root).TransformBounds(slot);
                    if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                    var left = (int)Math.Floor(bounds.Left * scale);
                    var top = (int)Math.Floor(bounds.Top * scale);
                    rectangles.Add(new RectInt32(left, top,
                        (int)Math.Ceiling(bounds.Right * scale) - left,
                        (int)Math.Ceiling(bounds.Bottom * scale) - top));
                }
            }
            ApplyRegion(rectangles.ToArray());
        }
        catch (Exception ex)
        {
            // Fail closed if region calculation fails: never cover the video.
            LocalLog.AppendException("errors.txt", ex, "playback_toast_region");
            _window.Close();
        }
    }

    private void ApplyRegion(RectInt32[] rectangles)
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        if (_lastRegions != null && _lastScale == scale && _lastRegions.SequenceEqual(rectangles)) return;
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero) throw new Win32Exception();
        try
        {
            foreach (var rect in rectangles)
            {
                var cornerDiameter = (int)Math.Round(20 * scale); // Toast card CornerRadius=10.
                var card = CreateRoundRectRgn(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height,
                    cornerDiameter, cornerDiameter);
                if (card == IntPtr.Zero) throw new Win32Exception();
                try { if (CombineRgn(region, region, card, 2 /* RGN_OR */) == 0) throw new Win32Exception(); }
                finally { DeleteObject(card); }
            }
            if (SetWindowRgn(_hwnd, region, true) == 0) throw new Win32Exception();
            region = IntPtr.Zero; // Windows owns a successfully applied region.
            _lastRegions = rectangles;
            _lastScale = scale;
        }
        finally { if (region != IntPtr.Zero) DeleteObject(region); }
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
        => message == 0x0021 /* WM_MOUSEACTIVATE */
            ? new IntPtr(3) /* MA_NOACTIVATE: deliver the click without focus */
            : DefSubclassProc(hwnd, message, wParam, lParam);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        Closed?.Invoke();
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Root.LayoutUpdated -= OnLayoutUpdated;
        _window.Closed -= OnClosed;
        RemoveWindowSubclass(_hwnd, _subclass, 1);
        if (!_closed) _window.Close();
        Closed = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dest, IntPtr first, IntPtr second, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
