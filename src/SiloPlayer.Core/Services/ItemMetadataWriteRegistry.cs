using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using System.Runtime.ExceptionServices;

namespace SiloPlayer.Core.Services;

/// <summary>
/// App-lifetime ownership of metadata writes, independent of any editor opening.
/// An observer can leave without canceling an accepted write or its reconciliation.
/// </summary>
public sealed class ItemMetadataWriteRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<(ApiIdentityContext Owner, string ItemId), Entry> _entries = [];
    private long _revision;
    public long Revision => Volatile.Read(ref _revision);
    public event Action<ApiIdentityContext, string>? WriteStarted;

    private sealed class Entry
    {
        public int Count;
        public MediaItemDetail? Refreshed;
        public TaskCompletionSource<MediaItemDetail?> Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public bool IsPending(ApiIdentityContext owner, string itemId)
    {
        lock (_gate) return _entries.ContainsKey((owner, itemId));
    }

    public Task<MediaItemDetail?> WaitForIdleAsync(ApiIdentityContext owner, string itemId, CancellationToken ct)
    {
        lock (_gate)
            return _entries.TryGetValue((owner, itemId), out var entry)
                ? entry.Settled.Task.WaitAsync(ct) : Task.FromResult<MediaItemDetail?>(null);
    }

    public async Task<MediaItemDetail> RunAsync(ApiIdentityContext owner, string itemId,
        Func<CancellationToken, Task> write, Func<CancellationToken, Task<MediaItemDetail>> refetch,
        CancellationToken dialogLifetime, CancellationToken authority = default)
    {
        // The dialog token admits the operation; it does not own an HTTP write
        // that may already be changing server state when the dialog closes.
        dialogLifetime.ThrowIfCancellationRequested();
        authority.ThrowIfCancellationRequested();
        var key = (owner, itemId);
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries.Add(key, entry = new());
            entry.Count++;
            Interlocked.Increment(ref _revision);
        }
        MediaItemDetail? refreshed = null;
        Exception? failure = null;
        try
        {
            WriteStarted?.Invoke(owner, itemId);
            using (var writeLifetime = CancellationTokenSource.CreateLinkedTokenSource(authority))
            {
                writeLifetime.CancelAfter(TimeSpan.FromMinutes(2));
                try { await write(writeLifetime.Token).ConfigureAwait(false); }
                catch (Exception ex) { failure = ex; }
            }
            // Reconcile even a failed/ambiguous write, with a separate bounded
            // read budget. Never replay a mutation after an uncertain outcome.
            using var readLifetime = CancellationTokenSource.CreateLinkedTokenSource(authority);
            readLifetime.CancelAfter(TimeSpan.FromSeconds(30));
            try { refreshed = await refetch(readLifetime.Token).ConfigureAwait(false); }
            catch (Exception ex) { failure ??= ex; }
        }
        finally
        {
            lock (_gate)
            {
                entry.Refreshed = refreshed;
                Interlocked.Increment(ref _revision);
                if (--entry.Count == 0)
                {
                    _entries.Remove(key);
                    entry.Settled.TrySetResult(entry.Refreshed);
                }
            }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return refreshed!;
    }
}
