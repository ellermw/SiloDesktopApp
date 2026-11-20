namespace SiloPlayer.Core.Services;

/// <summary>
/// Holds final progress writes until an authoritative catalog read can start.
/// Used on the owning UI context; settlement never mutates a page or starts a read.
/// </summary>
public sealed class PendingCatalogWrites
{
    private readonly List<Task> _pending = [];

    public void Track(Task write)
    {
        _pending.RemoveAll(pending => pending.IsCompleted);
        var observed = ObserveAsync(write);
        if (!observed.IsCompleted) _pending.Add(observed);
    }

    private static async Task ObserveAsync(Task write)
    {
        // A failed save still settled: a subsequent read reconciles with the server.
        try { await write.ConfigureAwait(false); }
        catch { }
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _pending.RemoveAll(write => write.IsCompleted);
            if (_pending.Count == 0) return;
            // More messages can arrive while these writes are settling.
            await Task.WhenAll(_pending.ToArray()).WaitAsync(ct);
        }
    }
}
