using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace SiloPlayer.Services;

/// <summary>
/// Hosts player dialogs in a borderless window owned by and covering the native
/// mpv surface. The mpv HWND stays alive behind a dim modal layer, while the
/// ownership relationship guarantees that keyboard and pointer input cannot
/// fall through to the seek timeline underneath it.
/// </summary>
internal static class PlaybackDialogHost
{
    private const int GwlpHwndParent = -8;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;
        public int Enable;
        public IntPtr Region;
        public int TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, in DwmBlurBehind settings);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr region);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    internal static bool IsForegroundOwnedBy(IntPtr owner)
    {
        var foreground = GetForegroundWindow();
        for (var i = 0; foreground != IntPtr.Zero && i < 16; i++, foreground = GetWindow(foreground, 4))
            if (foreground == owner) return true;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static async Task<bool> ShowAsync(
        IntPtr videoHwnd,
        Func<XamlRoot, IntPtr, Task> showDialogAsync,
        bool activate = true, Func<bool>? canRestoreFocus = null)
    {
        ArgumentNullException.ThrowIfNull(showDialogAsync);
        if (videoHwnd == IntPtr.Zero)
            throw new InvalidOperationException("The playback surface is not available.");

        var root = new Grid
        {
            // Match the WebUI's full-player modal backdrop. Besides the visual
            // parity, the opaque-to-input surface prevents clicks outside the
            // dialog from reaching mpv's OSC or seek timeline.
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(178, 0, 0, 0)),
        };
        root.Resources["ContentDialogSmokeFill"] = new SolidColorBrush(Colors.Transparent);
        var host = new Window
        {
            SystemBackdrop = new VideoBackdrop(),
            Content = root,
            Title = "Silo Player",
        };

        var hostHwnd = WinRT.Interop.WindowNative.GetWindowHandle(host);
        SetWindowLongPtrW(hostHwnd, GwlpHwndParent, videoHwnd);

        if (host.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var playerWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(videoHwnd));
        var modalWindow = host.AppWindow;
        void FollowVideo(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange || args.DidSizeChange)
                CoverPlaybackSurface(videoHwnd, hostHwnd);
        }

        var loaded = new TaskCompletionSource<XamlRoot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        root.Loaded += (_, _) =>
        {
            if (root.XamlRoot != null)
                loaded.TrySetResult(root.XamlRoot);
        };
        host.Closed += (_, _) =>
            loaded.TrySetException(new OperationCanceledException("The player dialog was closed."));

        var restorePlayerInput = false;
        try
        {
            modalWindow.Changed += FollowVideo;
            playerWindow.Changed += FollowVideo;
            CoverPlaybackSurface(videoHwnd, hostHwnd);
            if (activate) { host.Activate(); SetForegroundWindow(hostHwnd); }
            else host.AppWindow.Show(false);
            var xamlRoot = await loaded.Task;
            CoverPlaybackSurface(videoHwnd, hostHwnd);
            await showDialogAsync(xamlRoot, hostHwnd);
        }
        finally
        {
            // Closing an owned WinUI window does not consistently reactivate
            // the native mpv owner. Restore player input only when this modal
            // still owns the foreground; never pull the user back from an app
            // they switched to while the dialog was open.
            modalWindow.Changed -= FollowVideo;
            playerWindow.Changed -= FollowVideo;
            restorePlayerInput = GetForegroundWindow() == hostHwnd && (canRestoreFocus?.Invoke() ?? true);
            host.Close();
            if (restorePlayerInput)
                SetForegroundWindow(videoHwnd);
        }

        return restorePlayerInput;
    }

    internal static async Task ShowContentDialogAsync(Microsoft.UI.Xaml.Controls.ContentDialog dialog, bool activate)
    {
        void EnableAfterLoad(object sender, RoutedEventArgs args) => dialog.IsEnabled = true;
        if (!activate) { dialog.IsEnabled = false; dialog.Loaded += EnableAfterLoad; }
        try { await dialog.ShowAsync(); }
        finally { dialog.Loaded -= EnableAfterLoad; }
    }

    private static void CoverPlaybackSurface(IntPtr videoHwnd, IntPtr hostHwnd)
    {
        if (!GetWindowRect(videoHwnd, out var videoRect))
            return;

        var display = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(videoHwnd), DisplayAreaFallback.Nearest);
        var bounds = new RectInt32(videoRect.Left, videoRect.Top, videoRect.Right - videoRect.Left, videoRect.Bottom - videoRect.Top);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (display != null) bounds = VisibleVideoBounds(bounds, display.OuterBounds);
        if (GetWindowRect(hostHwnd, out var current) && current.Left == bounds.X && current.Top == bounds.Y &&
            current.Right - current.Left == bounds.Width && current.Bottom - current.Top == bounds.Height) return;

        // Both coordinate sources are physical pixels. Keep the owned window's activation and stacking intact.
        SetWindowPos(hostHwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoZOrder | SwpNoActivate);
    }

    private static RectInt32 VisibleVideoBounds(RectInt32 video, RectInt32 monitor)
    {
        var left = Math.Max(video.X, monitor.X);
        var top = Math.Max(video.Y, monitor.Y);
        var right = Math.Min(video.X + video.Width, monitor.X + monitor.Width);
        var bottom = Math.Min(video.Y + video.Height, monitor.Y + monitor.Height);
        // An offscreen owner must keep its modal offscreen as well.
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : video;
    }

    // A XAML brush with alpha still blends onto opaque black in an ordinary WinUI window.
    // Give the compositor a transparent backing layer so the dim scrim blends over mpv.
    private sealed class VideoBackdrop : SystemBackdrop
    {
        private Windows.UI.Composition.Compositor? _compositor;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            base.OnTargetConnected(target, root);
            // Enable per-pixel alpha on the HWND. The region sits outside the client
            // area so DWM preserves the video's detail behind the XAML dim layer.
            var hwnd = Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
            var region = CreateRectRgn(-2, -2, -1, -1);
            if (region != IntPtr.Zero)
            {
                try
                {
                    var settings = new DwmBlurBehind { Flags = 3 /* ENABLE | BLURREGION */, Enable = 1, Region = region };
                    DwmEnableBlurBehindWindow(hwnd, in settings);
                }
                finally { DeleteObject(region); }
            }
            _compositor = new Windows.UI.Composition.Compositor();
            target.SystemBackdrop = _compositor.CreateColorBrush(Colors.Transparent);
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
        {
            var brush = target.SystemBackdrop;
            target.SystemBackdrop = null;
            brush?.Dispose();
            _compositor?.Dispose();
            _compositor = null;
            base.OnTargetDisconnected(target);
        }
    }
}
