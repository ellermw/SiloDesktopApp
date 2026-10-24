namespace SiloPlayer.Core.Services;

/// <summary>Rates media bytes only. A silent, paused or seeking window is never a slow-link observation.</summary>
public sealed class NativeMediaThroughputMeter
{
    private long? _bytes;
    private long _at;
    private bool _discard;
    public void Reset() { _bytes = null; _discard = false; }
    public int? Observe(long nowMs, long? totalMediaBytes, bool paused, bool seeking)
    {
        var old = _bytes; var elapsed = nowMs - _at;
        _bytes = totalMediaBytes; _at = nowMs;
        var excluded = paused || seeking;
        var discard = _discard; _discard = excluded;
        if (excluded || discard || old == null || totalMediaBytes == null || elapsed < 250 || elapsed > 3000 || totalMediaBytes <= old) return null;
        var rate = (totalMediaBytes.Value - old.Value) * 8d / elapsed;
        return double.IsFinite(rate) && rate >= 100 ? (int)Math.Min(1_000_000, rate) : null;
    }
}
