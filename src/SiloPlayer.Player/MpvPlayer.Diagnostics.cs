using System.Diagnostics;
using static SiloPlayer.Player.MpvInterop;

namespace SiloPlayer.Player;

public sealed partial class MpvPlayer
{
    private PlaybackDiagnostics? _diagnostics;
    private double _diagnosticCache = -1, _diagnosticSpeed = -1, _diagnosticDropped = -1;
    private long _lastEventTick = Stopwatch.GetTimestamp();
    private int _diagnosticLoad;
    private const ulong UdDiagnosticCache = 101, UdDiagnosticSpeed = 102, UdDiagnosticDropped = 103;
    internal PlaybackDiagnostics? Diagnostics => _diagnostics;

    private void ObserveDiagnostics()
    {
        mpv_observe_property(_mpvHandle, UdDiagnosticCache, "demuxer-cache-duration", MPV_FORMAT_DOUBLE);
        mpv_observe_property(_mpvHandle, UdDiagnosticSpeed, "cache-speed", MPV_FORMAT_DOUBLE);
        mpv_observe_property(_mpvHandle, UdDiagnosticDropped, "frame-drop-count", MPV_FORMAT_DOUBLE);
    }

    private void StartDiagnostics(bool direct, double start)
    {
        _diagnosticCache = _diagnosticSpeed = _diagnosticDropped = -1;
        _diagnostics ??= new PlaybackDiagnostics(() => new {
            position = Position, duration = Duration, paused = IsPaused, buffering = IsBufferingForCache,
            cacheSeconds = _diagnosticCache, cacheBytesPerSecond = _diagnosticSpeed, droppedFrames = _diagnosticDropped,
            eventLoopAgeMs = Stopwatch.GetElapsedTime(Volatile.Read(ref _lastEventTick)).TotalMilliseconds,
            reader = Volatile.Read(ref _directRequest)?.Reader?.DiagnosticSnapshot()
        });
        _diagnostics.Record("load", new { load = ++_diagnosticLoad, transport = direct ? "signed_direct" : "native_other", startPosition = start });
    }

    private void RecordBuffering(bool buffering)
    {
        _diagnostics?.Record(buffering ? "buffer_empty" : "buffer_recovered", new {
            position = Position, cacheSeconds = _diagnosticCache, paused = IsPaused,
            reader = Volatile.Read(ref _directRequest)?.Reader?.DiagnosticSnapshot()
        });
        if (buffering) _diagnostics?.Incident("buffer_empty");
    }
}
