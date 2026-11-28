using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Helpers;

/// <summary>
/// Lets WinUI's own memory check run while a page is still, instead of in the first frames of the next scroll.
/// WinUI (CCoreServices::TrackMemory, microsoft-ui-xaml release/1.8) looks at the process working set once every
/// 50 frames that need a render walk and, when it has grown more than 50 MB above its baseline, starts a blocking
/// full collection through the reference-tracker host (58-72 ms with Home built). Frames that draw nothing do not
/// count, so growth while the page sits still (rows built ahead, a refresh applied) is only checked once the next
/// scroll draws. When layout on the page has been quiet for a moment and the working set has grown enough since
/// the last check that the collection could be due, this draws one interval of frames (a 1x1 element swaps between
/// two fully transparent brushes: it draws nothing either way) and stops as soon as a full collection happens or the interval
/// has passed. It never starts a collection, and it does nothing while memory has not grown.
/// </summary>
internal sealed class MemoryCheckWhileStill
{
    // WinUI's interval (TRACKING_INTERVAL 50) plus the frame that runs the check and one to spare.
    private const int Frames = 52;
    // WinUI's MAX_ALLOWEDMEMORY_INCREASE is 50 MB; growth this close to it may already be over its baseline.
    private const long Growth = 48L << 20;
    // After a check that started no collection, growth that makes another worth drawing for.
    private const long Again = 8L << 20;
    private static readonly long QuietTicks = Stopwatch.Frequency / 4;

    // Process-wide, as WinUI's baseline is: the lowest working set seen since the last blocking collection (-1: none
    // seen since watching began, so WinUI's baseline is unknown), and the working set at the last check that started
    // no collection (0: none since).
    private static long s_floor = -1;
    private static long s_checkedAt;
    private static int s_blocking = -1;

    /// <summary>Intervals drawn while still, and how many of them ended in a collection (tests and diagnostics).</summary>
    internal static int Intervals { get; private set; }
    internal static int Collected { get; private set; }

    private readonly FrameworkElement _page;
    // Visible, but only ever painted with fully transparent brushes. An element at Opacity 0 would not do: its
    // changes need no render walk, so those frames do not count for WinUI's check (measured: no collection after
    // 60 such frames 140 MB over the baseline, one within 60 frames of this element's brush swaps).
    private readonly Border _probe = new() { Width = 1, Height = 1, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly SolidColorBrush _even = new(Windows.UI.Color.FromArgb(0, 0, 0, 0)), _odd = new(Windows.UI.Color.FromArgb(0, 255, 255, 255));
    private DispatcherQueueTimer? _quiet;
    private bool _quietRunning;
    private long _lastLayout;
    private int _drawn;
    private int _gen2AtStart;
    private bool _drawing;
    private bool _watching;

    private MemoryCheckWhileStill(Panel root)
    {
        _page = root;
        root.Children.Add(_probe);
        root.Loaded += (_, _) => Watch(true);
        // WinUI can deliver a queued Unloaded after the page is back in the tree.
        root.Unloaded += (_, _) => Watch(root.IsLoaded);
        Watch(root.IsLoaded);
    }

    private void Watch(bool watching)
    {
        if (_watching == watching) return;
        _watching = watching;
        if (watching) { _page.LayoutUpdated += OnLayout; return; }
        _page.LayoutUpdated -= OnLayout;
        _quiet?.Stop(); _quietRunning = false;
        StopDrawing();
    }

    /// <summary>Watches the page whose root panel this is, while it is loaded.</summary>
    internal static void Attach(Panel root) => _ = new MemoryCheckWhileStill(root);

    // Every layout pass anywhere in the window: note the time; the quiet timer runs only while layout keeps happening.
    private void OnLayout(object? sender, object e)
    {
        _lastLayout = Stopwatch.GetTimestamp();
        if (_quietRunning) return;
        _quiet ??= CreateQuietTimer();
        _quietRunning = true;
        _quiet.Start();
    }

    private DispatcherQueueTimer CreateQuietTimer()
    {
        var timer = _page.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            Observe();
            if (Stopwatch.GetTimestamp() - _lastLayout < QuietTicks) return;
            timer.Stop(); _quietRunning = false;
            if (_page.IsLoaded) CheckWhileStill();
        };
        return timer;
    }

    // Follows the working set down between checks, as WinUI's baseline does at its own checks.
    private static long Observe()
    {
        var workingSet = WorkingSet();
        var blocking = (int)Math.Min(int.MaxValue, GC.GetGCMemoryInfo(GCKind.FullBlocking).Index);
        if (s_blocking == -1) s_blocking = blocking;
        else if (blocking != s_blocking)
        {
            // A blocking collection (WinUI's check sets its baseline to the working set then): start from here.
            s_blocking = blocking;
            s_floor = workingSet;
            s_checkedAt = 0;
        }
        else if (s_floor >= 0 && workingSet < s_floor) s_floor = workingSet;
        return workingSet;
    }

    private void CheckWhileStill()
    {
        if (_drawing) return;
        var workingSet = Observe();
        // An unknown baseline (WinUI set it before this page was watched) gets one check at the first still moment.
        if (s_floor >= 0 && workingSet - s_floor <= Growth) return;
        if (s_checkedAt != 0 && workingSet - s_checkedAt <= Again) return;
        _drawing = true;
        _drawn = 0;
        _gen2AtStart = GC.CollectionCount(2);
        Intervals++;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        // A brush change: the frame needs a render walk, and both brushes are fully transparent.
        _probe.Background = (++_drawn & 1) == 0 ? _even : _odd;
        if (GC.CollectionCount(2) != _gen2AtStart) { Collected++; StopDrawing(); Observe(); return; }
        if (_drawn < Frames) return;
        StopDrawing();
        // WinUI checked and found its baseline within 50 MB: draw for it again only after more growth.
        s_checkedAt = WorkingSet();
    }

    private void StopDrawing()
    {
        if (!_drawing) return;
        _drawing = false;
        CompositionTarget.Rendering -= OnRendering;
        _probe.Background = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Counters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out Counters counters, uint size);

    // The figure WinUI's check reads (CWindowsServices::GetMemoryCount: GetProcessMemoryInfo's WorkingSetSize).
    private static long WorkingSet() =>
        GetProcessMemoryInfo(new IntPtr(-1), out var counters, (uint)Marshal.SizeOf<Counters>()) ? (long)counters.WorkingSetSize : 0;
}
