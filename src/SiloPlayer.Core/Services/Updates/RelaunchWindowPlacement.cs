using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Services.Updates;

/// <summary>A screen rectangle in native physical pixels (never XAML DIPs).</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    [JsonIgnore] public int Width => Right - Left;
    [JsonIgnore] public int Height => Bottom - Top;
    [JsonIgnore] public bool IsEmpty => Width <= 0 || Height <= 0;
    public static ScreenRect FromSize(int left, int top, int width, int height) => new(left, top, left + width, top + height);
    public bool Intersects(ScreenRect other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    public ScreenRect Inflate(int amount) => new(Left - amount, Top - amount, Right + amount, Bottom + amount);
    public bool Contains(ScreenRect other) => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
}

/// <summary>One connected monitor: its device name (for example <c>\\.\DISPLAY2</c>), bounds and work area.</summary>
public sealed record DisplayMonitor(string DeviceName, ScreenRect Bounds, ScreenRect WorkArea);

/// <summary>Where the main window was when Silo closed itself to install an update and restart.</summary>
public sealed record SavedWindowPlacement(string MonitorDevice, ScreenRect MonitorBounds, ScreenRect MonitorWorkArea,
    ScreenRect NormalBounds, bool Maximized, DateTimeOffset SavedUtc);

public sealed record WindowPlacementTarget(ScreenRect Bounds, bool Maximized);

/// <summary>
/// Carries the main window's monitor, position, size and maximized state across an update restart.
/// The closing client writes it only when it hands off an update that will start Silo again, and the
/// next startup takes it once. Both restart paths (the small update window and the full installer)
/// start the root launcher, so the file needs no launch arguments and no change to the update protocol.
/// </summary>
public static class RelaunchWindowPlacement
{
    public const string FileName = "relaunch-window-placement.json";
    // A full installer gives up the restart after its 15-minute warning; anything older is a later, ordinary launch.
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(30);
    private const int MaximumFileBytes = 4096;
    // Windows 10/11 frames include invisible resize borders that extend past the work area when a window
    // touches an edge or is snapped. Keep such a rectangle exactly as it was.
    private const int FrameSlack = 32;

    /// <summary>The monitor the window was on, or null when it is no longer connected.</summary>
    public static DisplayMonitor? FindMonitor(SavedWindowPlacement saved, IReadOnlyList<DisplayMonitor> monitors)
    {
        // Same name and geometry: the same monitor. Same geometry under a new name: Windows renumbered the
        // same arrangement. Same name with new geometry: the same output at another resolution or position.
        return monitors.FirstOrDefault(m => NameMatches(m, saved) && m.Bounds == saved.MonitorBounds)
            ?? monitors.FirstOrDefault(m => m.Bounds == saved.MonitorBounds)
            ?? monitors.FirstOrDefault(m => NameMatches(m, saved));
    }

    /// <summary>
    /// The placement to apply at startup, or null for Silo's normal startup placement: nothing saved,
    /// a stale or invalid record, or the monitor it was on is not connected.
    /// </summary>
    public static WindowPlacementTarget? Resolve(SavedWindowPlacement? saved, IReadOnlyList<DisplayMonitor> monitors, DateTimeOffset now)
    {
        if (saved is null || !IsUsable(saved, now)) return null;
        var monitor = FindMonitor(saved, monitors);
        if (monitor is null || monitor.WorkArea.IsEmpty) return null;
        return new(Fit(saved, monitor), saved.Maximized);
    }

    /// <summary>The normal (restored) rectangle on <paramref name="monitor"/>, kept fully reachable in its work area.</summary>
    public static ScreenRect Fit(SavedWindowPlacement saved, DisplayMonitor monitor)
    {
        var work = monitor.WorkArea;
        var bounds = saved.NormalBounds;
        var unchanged = monitor.Bounds == saved.MonitorBounds && work == saved.MonitorWorkArea;
        if (unchanged && work.Inflate(FrameSlack).Contains(bounds)) return bounds;
        if (!bounds.Intersects(saved.MonitorBounds))
        {
            // A maximized window moved to another monitor keeps its old restore rectangle; centre it here
            // so that both the maximized window and a later restore stay on this monitor.
            bounds = ScreenRect.FromSize(work.Left + (work.Width - bounds.Width) / 2, work.Top + (work.Height - bounds.Height) / 2,
                bounds.Width, bounds.Height);
        }
        else if (!unchanged)
        {
            // Keep the offset from the work area's corner when the monitor moved or changed resolution.
            bounds = ScreenRect.FromSize(bounds.Left - saved.MonitorWorkArea.Left + work.Left,
                bounds.Top - saved.MonitorWorkArea.Top + work.Top, bounds.Width, bounds.Height);
        }
        var width = Math.Min(bounds.Width, work.Width);
        var height = Math.Min(bounds.Height, work.Height);
        var left = Math.Clamp(bounds.Left, work.Left, work.Right - width);
        var top = Math.Clamp(bounds.Top, work.Top, work.Bottom - height);
        return ScreenRect.FromSize(left, top, width, height);
    }

    public static void Save(string directory, SavedWindowPlacement placement)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, FileName);
        var temporary = Path.Combine(directory, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, placement);
                stream.Flush(true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Reads and removes the saved placement. It applies to one startup only.</summary>
    public static SavedWindowPlacement? Take(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length is <= 0 or > MaximumFileBytes)
            {
                File.Delete(path);
                return null;
            }
            var bytes = File.ReadAllBytes(path);
            File.Delete(path);
            return JsonSerializer.Deserialize<SavedWindowPlacement>(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
    }

    public static void Discard(string directory)
    {
        try { File.Delete(Path.Combine(directory, FileName)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsUsable(SavedWindowPlacement saved, DateTimeOffset now)
    {
        var age = now - saved.SavedUtc;
        return age >= TimeSpan.FromMinutes(-1) && age <= MaximumAge
            && !string.IsNullOrWhiteSpace(saved.MonitorDevice) && saved.MonitorDevice.Length <= 64
            && !saved.MonitorBounds.IsEmpty && !saved.MonitorWorkArea.IsEmpty && !saved.NormalBounds.IsEmpty
            && saved.NormalBounds.Width <= 65536 && saved.NormalBounds.Height <= 65536;
    }

    private static bool NameMatches(DisplayMonitor monitor, SavedWindowPlacement saved) =>
        string.Equals(monitor.DeviceName, saved.MonitorDevice, StringComparison.OrdinalIgnoreCase);
}
