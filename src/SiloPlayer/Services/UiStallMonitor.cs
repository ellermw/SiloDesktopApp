using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>
/// The interface-thread stall detector behind ui_lag.txt: a 250 ms timer whose late tick means the
/// thread was blocked for that long. A stall line names where it happened (<see cref="UiStallLog"/>);
/// an on-time tick writes nothing. The file stays bounded by LocalLog's rotation.
/// </summary>
public sealed class UiStallMonitor
{
    private readonly UiStallLog _log;
    private readonly Func<Frame?> _frame;
    private readonly Func<string> _playback;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private long _lastTick;

    /// <param name="frame">The main frame, read only when a stall is written.</param>
    /// <param name="playback">"idle", "paused" or "playing", read only when a stall is written.</param>
    public UiStallMonitor(UiStallLog log, Func<Frame?> frame, Func<string> playback)
    {
        _log = log; _frame = frame; _playback = playback;
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _lastTick = Environment.TickCount64;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, object e)
    {
        var now = Environment.TickCount64;
        var delay = now - _lastTick;
        _lastTick = now;
        try
        {
            if (delay < UiStallLog.StallMilliseconds) _log.Quiet();
            else
            {
                var content = _frame()?.Content;
                var admin = content is SiloPlayer.Views.Admin.AdminShellPage shell ? shell.CurrentRoute.ToString() : null;
                LocalLog.AppendLine("ui_lag.txt",
                    _log.Stall(delay, GC.GetTotalMemory(false) / (1024 * 1024), content?.GetType().Name ?? "none", admin, _playback()));
            }
            if (_log.SummaryDue && _log.TakeSummary() is { } summary) LocalLog.AppendLine("ui_lag.txt", summary);
        }
        catch { }
    }
}
