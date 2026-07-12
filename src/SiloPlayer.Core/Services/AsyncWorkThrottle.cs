namespace SiloPlayer.Core.Services;

public sealed class AsyncWorkThrottle : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public AsyncWorkThrottle(int maxConcurrency)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Concurrency must be at least 1.");

        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await work(ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await work(ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
