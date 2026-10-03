namespace SiloPlayer.Core.Services;

/// <summary>Serializes remote settings writes while preserving the newest local value.</summary>
public sealed class LatestSettingsWriter<T>(Func<T, CancellationToken, Task> write, Action<Exception?> status, TimeSpan delay)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _revision;
    public async Task SaveAsync(T value, CancellationToken cancellationToken)
    {
        var revision = Interlocked.Increment(ref _revision);
        var entered = false;
        try
        {
            await Task.Delay(delay, cancellationToken);
            await _gate.WaitAsync(cancellationToken);
            entered = true;
            if (revision != Volatile.Read(ref _revision)) return;
            await write(value, cancellationToken);
            if (revision == Volatile.Read(ref _revision)) status(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { if (revision == Volatile.Read(ref _revision)) status(error); }
        finally { if (entered) _gate.Release(); }
    }
}
