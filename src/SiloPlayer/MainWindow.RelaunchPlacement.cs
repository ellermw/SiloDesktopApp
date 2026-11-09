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

    private static string RelaunchPlacementDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer");

    private static bool RelaunchPlacementDisabled => Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") is not null;

    // A maximized or minimized window reports its current frame, not the one it restores to,
    // so remember the last frame seen while the window was in its normal state.
    private void TrackNormalWindowBounds()
    {
        if (AppWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored }
            && AppWindow.Size is { Width: > 0, Height: > 0 } size)
            _lastNormalWindowBounds = ScreenRect.FromSize(AppWindow.Position.X, AppWindow.Position.Y, size.Width, size.Height);
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
    /// <c>Activate</c>; returns true when the window should be maximized once it is shown.
    /// </summary>
    internal bool ApplyRelaunchPlacement()
    {
        if (RelaunchPlacementDisabled || AppWindow is null) return false;
        try
        {
            var saved = RelaunchWindowPlacement.Take(RelaunchPlacementDirectory);
            if (saved is null) return false;
            var target = RelaunchWindowPlacement.Resolve(saved, EnumerateMonitors(), DateTimeOffset.UtcNow);
            if (target is null)
            {
                LocalLog.AppendLine("updates.txt", "relaunch_placement_default reason=monitor_or_record_unavailable");
                return false;
            }
            var bounds = target.Bounds;
            var rect = new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            // Moving onto a monitor with another scale rescales the window once; the second call sets the exact frame there.
            AppWindow.MoveAndResize(rect);
            AppWindow.MoveAndResize(rect);
            _lastNormalWindowBounds = bounds;
            LocalLog.AppendLine("updates.txt", $"relaunch_placement_restored maximized={target.Maximized}");
            return target.Maximized;
        }
        catch (Exception ex)
        {
            LocalLog.AppendException("updates.txt", ex, "relaunch_placement_restore");
            return false;
        }
    }

    internal void MaximizeAfterRelaunch()
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
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
}
