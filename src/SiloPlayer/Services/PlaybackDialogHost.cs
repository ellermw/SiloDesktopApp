using System.Runtime.InteropServices;
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
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

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
        Func<XamlRoot, IntPtr, Task> showDialogAsync)
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
        var host = new Window
        {
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

        CoverPlaybackSurface(videoHwnd, hostHwnd);

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
            host.Activate();
            SetForegroundWindow(hostHwnd);
            var xamlRoot = await loaded.Task;
            await showDialogAsync(xamlRoot, hostHwnd);
        }
        finally
        {
            // Closing an owned WinUI window does not consistently reactivate
            // the native mpv owner. Restore player input only when this modal
            // still owns the foreground; never pull the user back from an app
            // they switched to while the dialog was open.
            restorePlayerInput = GetForegroundWindow() == hostHwnd;
            host.Close();
            if (restorePlayerInput)
                SetForegroundWindow(videoHwnd);
        }

        return restorePlayerInput;
    }

    private static void CoverPlaybackSurface(IntPtr videoHwnd, IntPtr hostHwnd)
    {
        if (!GetWindowRect(videoHwnd, out var videoRect))
            return;

        var videoWidth = Math.Max(1, videoRect.Right - videoRect.Left);
        var videoHeight = Math.Max(1, videoRect.Bottom - videoRect.Top);
        SetWindowPos(
            hostHwnd,
            HwndTop,
            videoRect.Left,
            videoRect.Top,
            videoWidth,
            videoHeight,
            SwpShowWindow);
    }
}
