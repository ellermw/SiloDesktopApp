using System.Diagnostics;

namespace SiloPlayer.Core.Services;

public sealed class StartupFrameTelemetry
{
    private readonly HashSet<string> _reported = [];
    private long? _requestedAt;
    public void Reset(long? requestedAt) { _reported.Clear(); _requestedAt = requestedAt; }
    public Dictionary<string, string>? Observe(string attempt, long frameAt)
    {
        if (!_reported.Add(attempt)) return null;
        var result = new Dictionary<string, string>();
        if (_requestedAt is { } request && frameAt >= request)
        {
            var elapsed = (long)Stopwatch.GetElapsedTime(request, frameAt).TotalMilliseconds;
            if (elapsed <= 600000) result["first_frame_ms"] = elapsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        _requestedAt = null;
        return result;
    }
}
