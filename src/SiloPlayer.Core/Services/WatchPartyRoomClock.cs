namespace SiloPlayer.Core.Services;

/// <summary>Maps server room timestamps onto a local monotonic playback schedule.</summary>
public sealed class WatchPartyRoomClock
{
    private readonly List<(double Rtt, double Offset)> _samples = [];
    private int _connectionSamples;
    public double OffsetMilliseconds => Best?.Offset ?? 0;
    public int PingDelayMilliseconds => _connectionSamples < 4 ? 2000 : 15000;
    private (double Rtt, double Offset)? Best => _samples.Count == 0 ? null :
        _samples.Aggregate((best, sample) => sample.Rtt <= best.Rtt ? sample : best);
    public void BeginConnection() => _connectionSamples = 0;
    public void Reset() { _samples.Clear(); BeginConnection(); }
    public bool RecordSample(double clientSent, double serverReceived, double serverSent, double clientReceived)
    {
        if (!double.IsFinite(clientSent) || !double.IsFinite(serverReceived) ||
            !double.IsFinite(serverSent) || !double.IsFinite(clientReceived)) return false;
        var rtt = clientReceived - clientSent - (serverSent - serverReceived);
        if (rtt is >= -2 and < 0) rtt = 0;
        if (rtt is < 0 or > 5000) return false;
        var offset = (serverReceived - clientSent + serverSent - clientReceived) / 2;
        if (Best is { } best && Math.Abs(offset - best.Offset) > (rtt + best.Rtt) / 2 + 50)
        {
            _samples.Clear();
            _connectionSamples = 0;
        }
        _connectionSamples++;
        _samples.Add((rtt, offset));
        if (_samples.Count > 8) _samples.RemoveAt(0);
        return true;
    }
    public long ExecutionTime(string? executeAt, DateTimeOffset wallNow, long monotonicNow)
    {
        if (!DateTimeOffset.TryParse(executeAt, out var at)) return monotonicNow;
        // Keep past times: a delayed command must project from execute_at, not arrival.
        return monotonicNow + (long)((at - wallNow).TotalMilliseconds - OffsetMilliseconds);
    }
}
