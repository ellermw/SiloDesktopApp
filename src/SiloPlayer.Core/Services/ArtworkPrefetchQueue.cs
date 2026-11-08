namespace SiloPlayer.Core.Services;

/// <summary>
/// Artwork read-ahead that survives viewport changes: <see cref="Replace"/> swaps the work not yet
/// started for a newer neighborhood (nearest first) while started work finishes, so steady scrolling
/// keeps it ahead instead of restarting it on every range. At most <c>workers</c> items run at once,
/// in a lane of their own beside on-screen loads; <see cref="Clear"/> drops everything for a new
/// owner or context. Thread-safe.
/// </summary>
public sealed class ArtworkPrefetchQueue<T>
{
    private readonly object _sync = new();
    private readonly Func<T, CancellationToken, Task> _work;
    private readonly int _workers;
    private readonly int _capacity;
    private readonly Queue<T> _pending = new();
    private readonly HashSet<T> _started = new();
    private readonly Queue<T> _completed = new();
    private CancellationTokenSource _owner = new();
    private int _running;

    public ArtworkPrefetchQueue(int workers, int capacity, Func<T, CancellationToken, Task> work)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _workers = workers;
        _capacity = capacity;
        _work = work ?? throw new ArgumentNullException(nameof(work));
    }

    public int PendingCount { get { lock (_sync) return _pending.Count; } }

    public int RunningCount { get { lock (_sync) return _running; } }

    /// <summary>
    /// Replaces the work not yet started; at most <c>capacity</c> distinct items are kept.
    /// Running items and the last <c>capacity</c> completed items are skipped using value equality.
    /// </summary>
    public void Replace(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_sync)
        {
            _pending.Clear();
            var pending = new HashSet<T>();
            foreach (var item in items)
            {
                if (_pending.Count == _capacity) break;
                if (!_started.Contains(item) && pending.Add(item))
                    _pending.Enqueue(item);
            }
            for (var available = Math.Min(_workers - _running, _pending.Count); available > 0; available--)
            {
                _running++;
                _ = Task.Run(RunAsync);
            }
        }
    }

    /// <summary>Drops the work not yet started and cancels the items running now.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _pending.Clear();
            _started.Clear();
            _completed.Clear();
            _owner.Cancel();
            _owner.Dispose();
            _owner = new CancellationTokenSource();
        }
    }

    // A worker takes the current owner for each item, so one still finishing a cleared item
    // goes on with the newer work instead of leaving it waiting.
    private async Task RunAsync()
    {
        while (true)
        {
            T item;
            CancellationToken ct;
            lock (_sync)
            {
                if (!_pending.TryDequeue(out item!))
                {
                    _running--;
                    return;
                }
                ct = _owner.Token;
                _started.Add(item);
            }
            // Read-ahead is optional: one item's failure never stops the others.
            var completed = false;
            try
            {
                await _work(item, ct).ConfigureAwait(false);
                completed = true;
            }
            catch { }
            lock (_sync)
            {
                // A cleared worker must not alter the newer owner's history, even for the same item.
                if (ct != _owner.Token) continue;
                if (!completed)
                {
                    _started.Remove(item);
                    continue;
                }
                _completed.Enqueue(item);
                if (_completed.Count > _capacity)
                    _started.Remove(_completed.Dequeue());
            }
        }
    }
}
