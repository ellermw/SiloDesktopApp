namespace SiloPlayer.Core.Services;

public sealed class AsyncLoadVersionGate
{
    private int _version;

    public int BeginNextLoad() => Interlocked.Increment(ref _version);

    public void Cancel() => Interlocked.Increment(ref _version);

    public bool IsCurrent(int version) => Volatile.Read(ref _version) == version;
}
