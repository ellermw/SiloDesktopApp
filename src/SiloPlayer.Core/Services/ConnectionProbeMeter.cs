using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>Official 500ms windows, first-window TCP ramp exclusion and bounded quick/accurate transfers.</summary>
public sealed class ConnectionProbeMeter(bool manual, int? targetKbps)
{
    private readonly Dictionary<int,long> _windows = [];
    private long _total;
    private int _checked;
    public int MaximumMs => manual ? 10000 : 3000;
    public long MaximumBytes => manual ? 1_000_000_000 : 100_000_000;
    public bool Done { get; private set; }
    public PlaybackProbeResult? Result { get; private set; }
    public bool Add(double elapsedMs, int bytes)
    {
        if (Tick(elapsedMs)) return true;
        var window = (int)(elapsedMs / 500);
        _windows[window] = _windows.GetValueOrDefault(window) + bytes;
        _total += bytes; return false;
    }
    public bool Tick(double elapsedMs)
    {
        if (Done) return true;
        if (elapsedMs >= MaximumMs) { Finish(MaximumMs); return true; }
        return Early(elapsedMs);
    }
    public PlaybackProbeResult? Finish(double elapsedMs, bool ended = false)
    {
        if (Done || Early(elapsedMs)) return Result;
        var stop = Math.Min(elapsedMs, MaximumMs); var complete = (int)(stop / 500);
        var first = Math.Max(1, complete - (manual ? 6 : 2));
        if (first >= complete)
        {
            if (ended && elapsedMs <= MaximumMs && _total >= MaximumBytes)
            {
                var duration = Math.Max(1, (int)Math.Round(stop));
                var kbps = (int)Math.Min(1000000, _total * 8d / duration);
                if (!manual && targetKbps is > 0 && kbps >= targetKbps) kbps = targetKbps.Value;
                Set(kbps, _total, duration, true);
            }
        }
        else
        {
            long bytes = 0; for (var i=first; i<complete; i++) bytes += _windows.GetValueOrDefault(i);
            var duration = (complete - first) * 500;
            var rate = bytes * 8d / duration;
            Set((int)Math.Min(1000000, rate), bytes, duration, rate > 1000000);
        }
        Done = true; return Result;
    }
    private bool Early(double elapsedMs)
    {
        if (manual || targetKbps is not > 0) return false;
        var complete = (int)(elapsedMs / 500);
        for (var i = Math.Max(1, _checked + 1); i < complete; i++)
        {
            _checked = i; var bytes = _windows.GetValueOrDefault(i);
            if (bytes * 8 / 500 < targetKbps) continue;
            Set(targetKbps.Value, bytes, 500, true); Done = true; return true;
        }
        return false;
    }
    private void Set(int kbps, long bytes, int duration, bool lowerBound)
    {
        if (kbps >= 100) Result = new() { Kbps = kbps, Bytes = bytes, DurationMs = duration, LowerBound = lowerBound, Manual = manual };
    }
}
