using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;
using Windows.Graphics;

namespace SiloPlayer;

// Reopens the window on the monitor it was on, at the same position, size and maximized state,
// when Silo restarts itself after an in-app update. Ordinary launches keep the default placement.
public sealed partial class MainWindow
{
    private ScreenRect? _lastNormalWindowBounds;
    private WindowPlacementTarget? _pendingRelaunchPlacement;

    private static string RelaunchPlacementDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer");

    private static bool RelaunchPlacementDisabled => Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") is not null;

    // A maximized or minimized window reports its current frame, not the one it restores to,
    // so remember the last frame seen while the window was in its normal state.
    private void TrackNormalWindowBounds()
    {
        if (AppWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored }
            && TryReadRelaunchWindowBounds(out var normal))
            _lastNormalWindowBounds = normal;
    }

    private bool TryReadRelaunchWindowBounds(out ScreenRect bounds)
    {
        bounds = default;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return false;
        // The app is PerMonitorV2-aware: GetWindowRect and GetMonitorInfo both return
        // physical screen pixels, including the window's invisible resize borders.
        bounds = new(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return !bounds.IsEmpty;
    }

    /// <summary>Records the placement for the restart that follows an update handoff.</summary>
    private void SaveRelaunchPlacement()
    {
        if (RelaunchPlacementDisabled || AppWindow is null) return;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            // For a minimized window this is the monitor it restores to.
            if (!TryReadMonitor(MonitorFromWindow(hwnd, MonitorDefaultToNearest), out var monitor)) return;
            TrackNormalWindowBounds();
            var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            var work = monitor.WorkArea;
            var normal = _lastNormalWindowBounds ?? ScreenRect.FromSize(work.Left + work.Width / 8, work.Top + work.Height / 8,
                work.Width * 3 / 4, work.Height * 3 / 4);
            RelaunchWindowPlacement.Save(RelaunchPlacementDirectory,
                new(monitor.DeviceName, monitor.Bounds, work, normal, maximized, DateTimeOffset.UtcNow));
            LocalLog.AppendLine("updates.txt", $"relaunch_placement_saved maximized={maximized}");
        }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "relaunch_placement_save"); }
    }

    private static void DiscardRelaunchPlacement()
    {
        if (!RelaunchPlacementDisabled) RelaunchWindowPlacement.Discard(RelaunchPlacementDirectory);
    }

    /// <summary>
    /// Applies a placement saved by the update that just restarted Silo. Call before the first
    /// <c>Activate</c>; the first layout rechecks the frame before applying the maximized state.
    /// </summary>
    internal void ApplyRelaunchPlacement()
    {
        if (RelaunchPlacementDisabled || AppWindow is null) return;
        try
        {
            var saved = RelaunchWindowPlacement.Take(RelaunchPlacementDirectory);
            if (saved is null) return;
            var target = RelaunchWindowPlacement.Resolve(saved, EnumerateMonitors(), DateTimeOffset.UtcNow);
            if (target is null)
            {
                LocalLog.AppendLine("updates.txt", "relaunch_placement_default reason=monitor_or_record_unavailable");
                return;
            }
            RestoreRelaunchBounds(target.Bounds);
            _pendingRelaunchPlacement = target;
            RootGrid.Loaded += OnRelaunchPlacementLoaded;
        }
        catch (Exception ex)
        {
            LocalLog.AppendException("updates.txt", ex, "relaunch_placement_restore");
        }
    }

    private void OnRelaunchPlacementLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
    {
        RootGrid.Loaded -= OnRelaunchPlacementLoaded;
        var target = _pendingRelaunchPlacement;
        _pendingRelaunchPlacement = null;
        if (target is null || AppWindow is null) return;
        try
        {
            // Initial layout can replace the hidden window's frame. Reapply it once the root is loaded.
            RestoreRelaunchBounds(target.Bounds);
            if (target.Maximized) MaximizeAfterRelaunch();
            LocalLog.AppendLine("updates.txt", $"relaunch_placement_restored maximized={target.Maximized}");
        }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "relaunch_placement_verify"); }
    }

    private void RestoreRelaunchBounds(ScreenRect bounds)
    {
        // AppWindow.MoveAndResize takes physical screen pixels, not XAML DIPs.
        var rect = new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        AppWindow.MoveAndResize(rect);
        var frameRead = TryReadRelaunchWindowBounds(out var observed);
        // Moving between DPI scales can change the frame; retry once if the actual frame differs.
        if (!frameRead || observed != bounds) AppWindow.MoveAndResize(rect);
        TrackNormalWindowBounds();
    }

    private void MaximizeAfterRelaunch()
    {
        try { if (AppWindow?.Presenter is OverlappedPresenter presenter) presenter.Maximize(); }
        catch (Exception ex) { LocalLog.AppendException("updates.txt", ex, "relaunch_placement_maximize"); }
    }

    private static List<DisplayMonitor> EnumerateMonitors()
    {
        var monitors = new List<DisplayMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            if (TryReadMonitor(handle, out var monitor)) monitors.Add(monitor);
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private static bool TryReadMonitor(IntPtr handle, out DisplayMonitor monitor)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(handle, ref info))
        {
            monitor = null!;
            return false;
        }
        monitor = new(info.Device ?? "", new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
            new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
        return true;
    }

    private const uint MonitorDefaultToNearest = 2;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
}
