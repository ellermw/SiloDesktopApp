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
/// mpv surface. The mpv HWND stays alive behind the dim modal layer, which
/// intercepts pointer input even when the video spans multiple displays.
/// </summary>
internal static class PlaybackDialogHost
{
    private const int GwlpHwndParent = -8;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
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
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    // Also reject activation when Windows is configured to focus windows on hover.
    private static readonly SubclassProc ScrimProc = (hwnd, message, wParam, lParam, _, _) => message switch
    {
        0x0021 /* WM_MOUSEACTIVATE */ => new IntPtr(4) /* MA_NOACTIVATEANDEAT */,
        0x024B /* WM_POINTERACTIVATE */ => new IntPtr(3) /* PA_NOACTIVATE */,
        _ => DefSubclassProc(hwnd, message, wParam, lParam),
    };

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

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

        var root = new Grid();
        root.Resources["ContentDialogSmokeFill"] = new SolidColorBrush(Colors.Transparent);
        var (host, hostHwnd) = CreateCover(videoHwnd, root);
        var videoId = Win32Interop.GetWindowIdFromWindow(videoHwnd);
        var playerWindow = AppWindow.GetFromWindowId(videoId);
        var modalWindow = host.AppWindow;
        var scrims = new Dictionary<RectInt32, (Window Window, IntPtr Hwnd)>();
        var shown = false;
        var updatingCoverage = false;
        var coveragePending = false;

        void FollowVideo(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange || args.DidSizeChange)
                CoverPlaybackSurface();
        }

        void CloseScrim(RectInt32 monitor)
        {
            if (!scrims.Remove(monitor, out var scrim) || !IsWindow(scrim.Hwnd)) return;
            scrim.Window.AppWindow.Changed -= FollowVideo;
            scrim.Window.Close();
        }

        void CoverPlaybackSurface()
        {
            if (updatingCoverage)
            {
                coveragePending = true;
                return;
            }

            updatingCoverage = true;
            try
            {
                // A DPI transition can resize a cover during SetWindowPos. Replay
                // those notifications after the pass, without unbounded recursion.
                for (var pass = 0; pass < 3; pass++)
                {
                    coveragePending = false;
                    UpdateCoverage();
                    if (!coveragePending) break;
                }
            }
            finally { updatingCoverage = false; }
        }

        void UpdateCoverage()
        {
            if (!IsWindow(hostHwnd) || !GetWindowRect(videoHwnd, out var rect)) return;
            var video = new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            if (video.Width <= 0 || video.Height <= 0) return;

            var display = DisplayArea.GetFromWindowId(videoId, DisplayAreaFallback.Nearest);
            var monitor = display?.OuterBounds;
            var visible = monitor is { } bounds ? Intersect(video, bounds) : null;
            // Keep the dialog on one display (or keep an offscreen owner offscreen).
            PositionCover(hostHwnd, visible ?? video);
            if (!shown) return;

            // The dialog stays in the visible region of its own display. Add only
            // the missing scrim on each other display, without taking modal focus.
            var used = new HashSet<RectInt32>();
            if (monitor is { } dialogMonitor && visible is { } region && !region.Equals(video))
            {
                foreach (var area in DisplayArea.FindAll())
                {
                    var screen = area.OuterBounds;
                    if (screen.Equals(dialogMonitor) || Intersect(video, screen) is not { } extra || !used.Add(screen))
                        continue;
                    var created = !scrims.TryGetValue(screen, out var scrim);
                    if (created)
                    {
                        scrim = CreateCover(hostHwnd, new Grid());
                        scrims.Add(screen, scrim);
                        var style = GetWindowLongPtrW(scrim.Hwnd, GwlExStyle).ToInt64();
                        SetWindowLongPtrW(scrim.Hwnd, GwlExStyle, new IntPtr(style | WsExNoActivate));
                        SetWindowSubclass(scrim.Hwnd, ScrimProc, 1, 0);
                        scrim.Window.AppWindow.Changed += FollowVideo;
                    }
                    PositionCover(scrim.Hwnd, extra);
                    if (created) scrim.Window.AppWindow.Show(false);
                }
            }
            foreach (var screen in scrims.Keys.Where(screen => !used.Contains(screen)).ToArray())
                CloseScrim(screen);
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
            CoverPlaybackSurface();
            if (activate) { host.Activate(); SetForegroundWindow(hostHwnd); }
            else host.AppWindow.Show(false);
            shown = true;
            CoverPlaybackSurface();
            var xamlRoot = await loaded.Task;
            CoverPlaybackSurface();
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
            shown = false;
            foreach (var screen in scrims.Keys.ToArray()) CloseScrim(screen);
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

    private static (Window Window, IntPtr Hwnd) CreateCover(IntPtr owner, Grid root)
    {
        // The scrim is translucent to the compositor but opaque to input.
        root.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(178, 0, 0, 0));
        var window = new Window { SystemBackdrop = new VideoBackdrop(), Content = root, Title = "Silo Player" };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowLongPtrW(hwnd, GwlpHwndParent, owner);
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        return (window, hwnd);
    }

    private static void PositionCover(IntPtr hwnd, RectInt32 bounds)
    {
        if (GetWindowRect(hwnd, out var current) && current.Left == bounds.X && current.Top == bounds.Y &&
            current.Right - current.Left == bounds.Width && current.Bottom - current.Top == bounds.Height) return;

        // Both coordinate sources are physical pixels. Keep the owned window's activation and stacking intact.
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoZOrder | SwpNoActivate);
    }

    private static RectInt32? Intersect(RectInt32 video, RectInt32 monitor)
    {
        var left = Math.Max(video.X, monitor.X);
        var top = Math.Max(video.Y, monitor.Y);
        var right = Math.Min(video.X + video.Width, monitor.X + monitor.Width);
        var bottom = Math.Min(video.Y + video.Height, monitor.Y + monitor.Height);
        return right > left && bottom > top ? new RectInt32(left, top, right - left, bottom - top) : null;
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
