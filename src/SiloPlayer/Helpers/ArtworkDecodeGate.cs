namespace SiloPlayer.Helpers;

/// <summary>Revalidates the original bind at each native processing stage without holding a decoder while motion waits.</summary>
internal sealed class ArtworkDecodeGate(FrameworkElement card, Func<bool> isCurrent, SemaphoreSlim? decoder = null)
{
    internal async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!isCurrent()) throw new OperationCanceledException(ct);
        if (decoder == null) await ArtworkViewport.WaitAsync(card, ct);
        else await ArtworkViewport.EnterDecodeAsync(card, decoder, ct);
        if (ct.IsCancellationRequested || !isCurrent())
        {
            decoder?.Release();
            throw new OperationCanceledException(ct);
        }
        return new Lease(decoder);
    }

    private sealed class Lease(SemaphoreSlim? decoder) : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref decoder, null)?.Release();
    }
}
