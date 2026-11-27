using System.Runtime.InteropServices;
using SiloPlayer.Core.Services;
using SiloPlayer.Views.Admin;

namespace SiloPlayer.Services;

/// <summary>
/// Samples dispatcher lateness beyond the expected tick interval, not proven blocked time.
/// On-time ticks write nothing; output stays bounded by LocalLog's rotation.
/// </summary>
public sealed class UiStallMonitor
{
    private readonly UiStallLog _log;
    private readonly Func<Frame?> _frame;
    private readonly Func<string> _playback;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(UiStallLog.TickMilliseconds) };
    private long _lastTick, _lastAwakeTick;

    /// <param name="frame">The main frame, read only when a stall is written.</param>
    /// <param name="playback">"idle", "paused" or "playing", read only when a stall is written.</param>
    public UiStallMonitor(UiStallLog log, Func<Frame?> frame, Func<string> playback)
    {
        _log = log; _frame = frame; _playback = playback;
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _log.Quiet();
        _lastTick = BootMilliseconds;
        _lastAwakeTick = AwakeMilliseconds;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    internal static UiStallLog.Location LocationOf(object? content) =>
        new(content?.GetType().Name ?? "none", content is AdminShellPage shell ? shell.CurrentRoute.ToString() : null);

    private void OnTick(object? sender, object e)
    {
        var now = BootMilliseconds;
        var awake = AwakeMilliseconds;
        var delay = now - _lastTick;
        var late = UiStallLog.TickLateness(delay, awake < 0 || _lastAwakeTick < 0 ? -1 : awake - _lastAwakeTick);
        _lastTick = now;
        _lastAwakeTick = awake;
        try
        {
            if (late is not long lateness || lateness < UiStallLog.StallMilliseconds) _log.Quiet();
            else
            {
                var location = LocationOf(_frame()?.Content);
                LocalLog.AppendLine("ui_lag.txt",
                    _log.Stall(delay, lateness, GC.GetTotalMemory(false) / (1024 * 1024), location, _playback()));
            }
            if (_log.TakeSummary() is { } summary) LocalLog.AppendLine("ui_lag.txt", summary);
        }
        catch { _log.Quiet(); }
    }

    // Compare clocks that explicitly include and exclude suspend, regardless of the .NET tick clock.
    internal static long BootMilliseconds => (long)GetTickCount64();
    private static long AwakeMilliseconds => QueryUnbiasedInterruptTime(out var time) ? (long)(time / 10_000) : -1;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern ulong GetTickCount64();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
}
