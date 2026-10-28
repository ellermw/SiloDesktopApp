using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>
/// Decides whether a /watch result may start playback. A read of an item waits
/// (bounded) only for a pending final progress write of that same item, and
/// every result is stamped with the user-state revision current when its server
/// read began; a later mutation of its content or series makes it stale, and
/// stale results are refetched before playback consumes them.
/// </summary>
public sealed class WatchDetailFreshness<T>(
    UserStateRevision revision,
    TimeSpan finalSaveBarrier,
    Func<T, string?> contentOf,
    Func<T, string?> seriesOf) where T : class
{
    private readonly ConditionalWeakTable<T, StrongBox<long>> _stamps = new();
    private readonly ConcurrentDictionary<string, Task> _finalSaves = new(StringComparer.Ordinal);

    /// <summary>Makes later reads of <paramref name="contentId"/> wait for its final write.</summary>
    public void TrackFinalSave(string contentId, Task persisted)
    {
        if (string.IsNullOrEmpty(contentId) || persisted.IsCompleted) return;
        var tracked = _finalSaves.AddOrUpdate(contentId, persisted,
            (_, previous) => previous.IsCompleted ? persisted : Task.WhenAll(previous, persisted));
        _ = tracked.ContinueWith(_ => _finalSaves.TryRemove(new KeyValuePair<string, Task>(contentId, tracked)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async Task<T> ReadAsync(string contentId, Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        if (_finalSaves.TryGetValue(contentId, out var pending) && !pending.IsCompleted)
        {
            try { await pending.WaitAsync(finalSaveBarrier, ct).ConfigureAwait(false); }
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
        long stamp;
        lock (_stamps)
        {
            if (!_stamps.TryGetValue(value, out var box)) return false;
            stamp = box.Value;
        }
        return revision.IsCurrent(stamp, contentOf(value), seriesOf(value));
    }

    /// <summary>
    /// Returns a result that was current when returned: a stale cached, in-flight
    /// or freshly read result is evicted and fetched again. When every attempt
    /// came back stale, a <see cref="UserStateChangedException"/> is thrown so
    /// playback never starts from a saved position already known to be wrong.
    /// </summary>
    public async Task<T> GetCurrentAsync(Func<CancellationToken, Task<T>> get, Action<T> evict, CancellationToken ct, int attempts = 3)
    {
        var value = await get(ct).ConfigureAwait(false);
        for (var attempt = 1; attempt < attempts && !IsCurrent(value); attempt++)
        {
            evict(value);
            value = await get(ct).ConfigureAwait(false);
        }
        if (!IsCurrent(value))
        {
            evict(value);
            throw new UserStateChangedException(contentOf(value) ?? "");
        }
        return value;
    }
}
