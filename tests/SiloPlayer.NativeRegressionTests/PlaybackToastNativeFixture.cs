using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Services;
using Windows.Graphics;

/// <summary>Real offscreen HWND ownership, reparenting, timers and Undo; no media/account access.</summary>
internal static class PlaybackToastNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync(Window owner, StackPanel parent)
    {
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var video = new MpvVideoWindow();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var container = new ToastContainer();
        parent.Children.Add(container);
        var service = new ToastService();
        service.Register(container, parent.DispatcherQueue);
        typeof(ToastService).GetMethod("SetPlaybackSurface", Private)!.Invoke(service, [video]);
        var stack = (StackPanel)container.FindName("ToastStack");
        var clock = Stopwatch.StartNew();
        var actions = 0;
        var releaseAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            container.Show("Active before playback", ToastContainer.ToastKind.Info, durationMs: 2200);
            var existing = (Border)stack.Children.Single();
            var timer = ((IDictionary)typeof(ToastContainer).GetField("_timers", Private)!.GetValue(container)!)[existing];
            var foreground = GetForegroundWindow();
            video.Show();
            await Until(() => Overlay() is { } overlay && Root(overlay).XamlRoot != null,
                "Shared toast never moved above the video.");
            var firstOverlay = Overlay()!;
            var hwnd = (IntPtr)Get(firstOverlay, "_hwnd")!;
            var root = Root(firstOverlay);
            Require(ReferenceEquals(container.Parent, root) && !parent.Children.Contains(container), "Toast was duplicated instead of moved.");
            Require(ReferenceEquals(existing, stack.Children.Single()), "Moving hosts recreated the active toast.");
            Require(ReferenceEquals(timer, ((IDictionary)Get(container, "_timers")!)[existing]), "Moving hosts replaced its timer.");
            Require(GetWindow(hwnd, 4) == video.Hwnd, "Toast overlay is not owned by playback.");
            Require((GetWindowLongPtrW(hwnd, -20).ToInt64() & 0x08000080) == 0x08000080, "Toast overlay lacks nonactivation/tool-window styles.");
            Require(SendMessageW(hwnd, 0x0021, video.Hwnd, IntPtr.Zero) == new IntPtr(3), "Toast click would activate its window.");
            Require(GetForegroundWindow() == foreground, "Toast presentation stole foreground focus.");
            await Task.Delay(300);
            Require(GetWindowRect(hwnd, out var overlayBounds) && GetWindowRect(video.Hwnd, out var videoBounds)
                && overlayBounds.Equals(videoBounds), "Overlay did not follow the playback rectangle.");
            var region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                Require(GetWindowRgn(hwnd, region) != 0 && !PtInRegion(region, 0, 0), "Empty video area intercepts input.");
                var cardBounds = existing.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, existing.ActualWidth, existing.ActualHeight));
                var scale = root.XamlRoot.RasterizationScale;
                Require(PtInRegion(region, (int)((cardBounds.X + cardBounds.Width / 2) * scale),
                    (int)((cardBounds.Y + cardBounds.Height / 2) * scale)), "Toast card is excluded from native hit testing.");
            }
            finally { DeleteObject(region); }

            service.Success("Undo during playback", "Undo", async () => { actions++; await releaseAction.Task; });
            await Until(() => stack.Children.Count == 2, "A new shared toast was hidden during playback.");
            var actionToast = (Border)stack.Children[0];
            var button = Descendants<Button>(actionToast).Single(b => b.Content is string label && label == "Undo");
            var invoke = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!;
            invoke.Invoke();
            await Until(() => actions == 1, "Undo was not dispatched.");
            Require(!button.IsEnabled, "Pending Undo can be invoked again.");
            try { invoke.Invoke(); }
            catch (Exception) when (!button.IsEnabled) { /* UIA may reject a disabled control before Click. */ }

            video.PositionAt(-20000, -20000, 320, 180);
            await Until(() => ReferenceEquals(container.Parent, parent) && Overlay() == null, "Mini preview did not restore shell toasts.");
            Require(!IsWindow(hwnd), "Overlay survived after playback uncovered the shell.");
            video.Show();
            await Until(() => Overlay() != null, "Expanding did not restore playback toasts.");
            Require(ReferenceEquals(actionToast, stack.Children[0]) && !button.IsEnabled && actions == 1,
                "Moving a pending Undo reset its action state.");
            releaseAction.TrySetResult();
            await Until(() => !stack.Children.Contains(actionToast), "Completed Undo did not dismiss its toast.");
            await Until(() => !stack.Children.Contains(existing), "Original toast did not expire.");
            Require(clock.Elapsed < TimeSpan.FromSeconds(4), "Host transitions restarted the original 2.2-second toast lifetime.");
            await Until(() => Overlay() == null, "Last toast expiry left an empty overlay alive.");

            container.Show("Move during fade-out", ToastContainer.ToastKind.Warning, durationMs: 30);
            var fading = (Border)stack.Children.Single();
            await Until(() => ((HashSet<Border>)Get(container, "_dismissing")!).Contains(fading), "Toast did not begin fading.");
            video.PositionAt(-20000, -20000, 320, 180);
            await Until(() => ReferenceEquals(container.Parent, parent), "Fading toast did not return to shell.");
            video.Show();
            await Until(() => !stack.Children.Contains(fading) && Overlay() == null, "Reparenting during fade-out stranded the toast.");

            service.Info("Hide cleanup");
            await Until(() => Overlay() != null, "No overlay for hide check.");
            hwnd = (IntPtr)Get(Overlay()!, "_hwnd")!;
            video.Hide();
            await Until(() => Overlay() == null && ReferenceEquals(container.Parent, parent), "Hidden video retained the overlay.");
            Require(!IsWindow(hwnd), "Hidden video's overlay HWND was orphaned.");
            video.Show();
            await Until(() => Overlay() != null, "No overlay for owner destruction check.");
            hwnd = (IntPtr)Get(Overlay()!, "_hwnd")!;
            video.Dispose();
            Require(Overlay() == null && ReferenceEquals(container.Parent, parent) && !IsWindow(hwnd),
                "Video destruction did not synchronously retire the overlay.");

            using var replacement = new MpvVideoWindow();
            replacement.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner));
            typeof(ToastService).GetMethod("SetPlaybackSurface", Private)!.Invoke(service, [replacement]);
            replacement.Show();
            await Until(() => Overlay() != null, "Replacement playback could not acquire the toast host.");
            hwnd = (IntPtr)Get(Overlay()!, "_hwnd")!;
            service.Unregister();
            Require(!IsWindow(hwnd) && ReferenceEquals(container.Parent, parent), "App/service close orphaned the overlay.");
            Require(GetForegroundWindow() == foreground, "Toast lifecycle stole foreground focus.");
            Program.Log("PASS: playback toast ownership/region/focus, one live host/timer/Undo, preview/hide/expiry/owner/unregister cleanup.");
        }
        finally
        {
            releaseAction.TrySetResult();
            service.Unregister();
            parent.Children.Remove(container);
        }

        object? Overlay() => Get(service, "_playbackHost") is { } host ? Get(host, "_overlay") : null;
    }

    private static object? Get(object target, string field) => target.GetType().GetField(field, Private)!.GetValue(target);
    private static Grid Root(object overlay) => (Grid)overlay.GetType().GetProperty("Root", Private)!.GetValue(overlay)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> predicate, string message)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate() && timeout.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Require(predicate(), message);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
