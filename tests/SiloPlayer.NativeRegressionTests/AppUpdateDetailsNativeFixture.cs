using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

internal static partial class AppUpdateNativeFixture
{
    // No progress frames or second click may repair the first preparing-state toggle.
    private static async Task CheckFirstDetailsAsync(Window panel, string name)
    {
        var type = panel.GetType();
        var root = (FrameworkElement)panel.Content;
        var toggle = (HyperlinkButton)type.GetProperty("Details")!.GetValue(panel)!;
        var body = (FrameworkElement)type.GetField("details", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(panel);
        panel.AppWindow.Show(false);
        await Task.Delay(500);
        var model = type.GetProperty("Model")!.GetValue(panel)!;
        if ((bool)model.GetType().GetProperty("ShowsDetails")!.GetValue(model)!)
        {
            await PointerClickAsync(toggle);
            await Task.Delay(450);
            if (body.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Transferred helper Details did not collapse on its next click.");
        }
        GetWindowRect(hwnd, out var collapsed);
        Program.Log($"Details {name} collapsed={collapsed.Top}..{collapsed.Bottom}.");
        await CaptureAsync(root, $"update-{name}-before-details.png");
        for (var turn = 0; turn < 3; turn++)
        {
            await PointerClickAsync(toggle);
            await Task.Delay(450);
            GetWindowRect(hwnd, out var expanded);
            GetClientRect(hwnd, out var client);
            var scale = root.XamlRoot.RasterizationScale;
            var bodyBounds = body.TransformToVisual(root).TransformBounds(new(0, 0, body.ActualWidth, body.ActualHeight));
            Program.Log($"Details {name} turn={turn}: root={root.ActualWidth}x{root.ActualHeight}, desired={root.DesiredSize}, client={client.Right}x{client.Bottom}, body={bodyBounds}, outer={expanded.Top}..{expanded.Bottom}.");
            var origin = new NativePoint(); ClientToScreen(hwnd, ref origin);
            var sample = new NativePoint { X = origin.X + 20, Y = origin.Y + client.Bottom - 8 };
            var desktop = DetailsGetDC(IntPtr.Zero);
            uint pixel;
            try { pixel = DetailsGetPixel(desktop, sample.X, sample.Y); }
            finally { DetailsReleaseDC(IntPtr.Zero, desktop); }
            Program.Log($"Details {name} bottom client pixel=0x{pixel:X8}, sample owner={GetAncestor(WindowFromPoint(sample), 2)}, expected owner={hwnd}.");
            if (GetAncestor(WindowFromPoint(sample), 2) != hwnd) throw new InvalidOperationException("Fixture update panel is covered; cannot inspect its painted client area.");
            if (pixel != 0x00201B18) throw new InvalidOperationException("Expanded updater leaves an unpainted client strip below its content.");
            // Check BEFORE RenderAsync/UpdateLayout: those can conceal a stale root arrange.
            if (body.Visibility != Visibility.Visible || body.ActualHeight < 80 || bodyBounds.Y < -1 || bodyBounds.Bottom > root.ActualHeight + 1
                || Math.Abs(root.ActualHeight * scale - client.Bottom) > 2
                || expanded.Bottom - expanded.Top <= collapsed.Bottom - collapsed.Top + 40 || Math.Abs(expanded.Bottom - collapsed.Bottom) > 2)
                throw new InvalidOperationException("First preparing-state Details click resized the HWND without presenting its details and filling the client area.");
            await CaptureAsync(root, $"update-{name}-details-{turn}.png");
            await PointerClickAsync(toggle);
            await Task.Delay(450);
            GetWindowRect(hwnd, out var again);
            GetClientRect(hwnd, out client);
            if (body.Visibility != Visibility.Collapsed || Math.Abs(again.Top - collapsed.Top) > 2 || Math.Abs(again.Bottom - collapsed.Bottom) > 2
                || Math.Abs(root.ActualHeight * scale - client.Bottom) > 2)
                throw new InvalidOperationException("Preparing-state details did not collapse back to a fully painted compact panel.");
        }
        Program.Log($"PASS: {name} first Details click reveals the complete panel without a black client gap, grows upward at a fixed bottom edge, and collapses correctly through three cycles without a progress frame.");

        async Task PointerClickAsync(FrameworkElement target)
        {
            var position = target.TransformToVisual(root).TransformPoint(new(target.ActualWidth / 2, target.ActualHeight / 2));
            var origin = new NativePoint(); ClientToScreen(hwnd, ref origin);
            var scale = root.XamlRoot.RasterizationScale;
            DetailsGetCursorPos(out var previous);
            try
            {
                DetailsSetCursorPos(origin.X + (int)Math.Round(position.X * scale), origin.Y + (int)Math.Round(position.Y * scale));
                DetailsMouseEvent(2, 0, 0, 0, UIntPtr.Zero);
                DetailsMouseEvent(4, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(50);
            }
            finally { DetailsSetCursorPos(previous.X, previous.Y); }
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetDC")] private static extern IntPtr DetailsGetDC(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "ReleaseDC")] private static extern int DetailsReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "GetPixel")] private static extern uint DetailsGetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool DetailsGetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool DetailsSetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void DetailsMouseEvent(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
