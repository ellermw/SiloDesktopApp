namespace SiloPlayer.Helpers;

/// <summary>Revalidates the original bind at each native processing stage without holding a decoder while motion waits.</summary>
internal sealed class ArtworkDecodeGate(FrameworkElement card, Func<bool> isCurrent, SemaphoreSlim? decoder = null)
{
    private bool _deferOnMotion;

    // The caller holds the outer decoder lease for the whole pipeline. A stage
    // must unwind its buffers/native resources before that lease is released,
    // rather than waiting for motion while retaining either resources or a slot.
    internal ArtworkDecodeGate ForPipelineStages() => new(card, isCurrent) { _deferOnMotion = true };

    internal sealed class MotionDeferredException : Exception { }

    internal async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!isCurrent()) throw new OperationCanceledException(ct);
        if (_deferOnMotion)
        {
            if (ArtworkViewport.IsDeferred(card)) throw new MotionDeferredException();
            return new Lease(null);
        }
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
