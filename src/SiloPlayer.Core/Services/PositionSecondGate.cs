namespace SiloPlayer.Core.Services;

/// <summary>
/// Admits per-frame player position events once per displayed whole second.
/// mpv raises time-pos for every decoded frame; UI that shows h:mm:ss has
/// nothing new to draw between second boundaries. Safe from any thread.
/// </summary>
public sealed class PositionSecondGate
{
    private const long Unset = long.MinValue;
    private long _lastSecond = Unset;

    public bool ShouldUpdate(double position)
    {
        var second = double.IsFinite(position) ? (long)Math.Floor(position) : Unset + 1;
        return Interlocked.Exchange(ref _lastSecond, second) != second;
    }

    public void Reset() => Interlocked.Exchange(ref _lastSecond, Unset);
}
