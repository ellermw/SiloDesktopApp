using System.Runtime.CompilerServices;

namespace SiloPlayer.Services;

/// <summary>
/// Decides whether a held /watch snapshot may still start playback. Reads wait
/// (bounded) for a pending final progress write, and every response is stamped
/// with the user-state revision current when its server read began; any later
/// progress or watched mutation makes it stale.
/// </summary>
public sealed class WatchDetailFreshness<T>(UserStateRevision revision, TimeSpan finalSaveBarrier) where T : class
{
    private readonly ConditionalWeakTable<T, StrongBox<long>> _stamps = new();

    public async Task<T> ReadAsync(Task pendingFinalSave, Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        if (!pendingFinalSave.IsCompleted)
        {
            try { await pendingFinalSave.WaitAsync(finalSaveBarrier, ct).ConfigureAwait(false); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* Final-write failures are reported by their owner. */ }
        }
        var readRevision = revision.Current;
        var value = await read(ct).ConfigureAwait(false);
        lock (_stamps)
        {
            // A shared response keeps the oldest revision any read observed.
            if (_stamps.TryGetValue(value, out var stamp)) stamp.Value = Math.Min(stamp.Value, readRevision);
            else _stamps.Add(value, new StrongBox<long>(readRevision));
        }
        return value;
    }

    public bool IsCurrent(T value)
    {
        lock (_stamps)
            return _stamps.TryGetValue(value, out var stamp) && stamp.Value == revision.Current;
    }
}
