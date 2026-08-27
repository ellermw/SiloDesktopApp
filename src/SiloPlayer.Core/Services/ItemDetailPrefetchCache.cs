using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Shares bounded, short-lived item-detail requests between card intent and
/// the detail page. Consumer cancellation never cancels a request another
/// surface may still use.
/// </summary>
public sealed class ItemDetailPrefetchCache
{
    private sealed record Entry(Task<MediaItemDetail> Task, DateTimeOffset CreatedAt);

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<string, CancellationToken, Task<MediaItemDetail>> _fetch;
    private readonly Func<string> _contextKey;
    private readonly TimeSpan _ttl;
    private readonly int _capacity;
    private string? _activeContextKey;

    public ItemDetailPrefetchCache(CatalogApi catalogApi, AuthService authService)
        : this((contentId, cancellationToken) =>
                catalogApi.GetItemDetailAsync(contentId, cancellationToken),
            contextKey: () => string.Join('\n',
                authService.ConfiguredServerUrl,
                authService.CurrentUser?.Id,
                authService.SelectedProfileId))
    {
    }

    public ItemDetailPrefetchCache(
        Func<string, CancellationToken, Task<MediaItemDetail>> fetch,
        TimeSpan? ttl = null,
        int capacity = 32,
        Func<string>? contextKey = null)
    {
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        _contextKey = contextKey ?? (() => string.Empty);
        _ttl = ttl ?? TimeSpan.FromMinutes(2);
        _capacity = Math.Max(1, capacity);
    }

    public Task PrefetchAsync(string contentId)
        => GetOrStart(contentId);

    /// <summary>
    /// Starts a speculative request without leaking failures into the UI
    /// event that expressed intent. Failed requests are evicted by
    /// <see cref="FetchAndObserveAsync"/>, so normal navigation can retry.
    /// </summary>
    public void Prefetch(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return;

        _ = ObservePrefetchAsync(GetOrStart(contentId));
    }

    public async Task<MediaItemDetail> GetAsync(
        string contentId,
        CancellationToken cancellationToken)
        => await GetOrStart(contentId).WaitAsync(cancellationToken).ConfigureAwait(false);

    public void Invalidate(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return;
        lock (_sync)
        {
            EnsureCurrentContext();
            _entries.Remove(contentId);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _activeContextKey = null;
        }
    }

    private Task<MediaItemDetail> GetOrStart(string contentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        lock (_sync)
        {
            EnsureCurrentContext();
            PruneExpired(DateTimeOffset.UtcNow);
            if (_entries.TryGetValue(contentId, out var cached))
                return cached.Task;

            while (_entries.Count >= _capacity)
            {
                var oldest = _entries.MinBy(pair => pair.Value.CreatedAt).Key;
                _entries.Remove(oldest);
            }

            Task<MediaItemDetail>? task = null;
            task = FetchAndObserveAsync(contentId, () => task!);
            _entries[contentId] = new Entry(task, DateTimeOffset.UtcNow);
            return task;
        }
    }

    private void EnsureCurrentContext()
    {
        var contextKey = _contextKey() ?? string.Empty;
        if (_activeContextKey != null
            && !string.Equals(_activeContextKey, contextKey, StringComparison.Ordinal))
        {
            _entries.Clear();
        }
        _activeContextKey = contextKey;
    }

    private static async Task ObservePrefetchAsync(Task<MediaItemDetail> prefetch)
    {
        try
        {
            await prefetch.ConfigureAwait(false);
        }
        catch
        {
            // Speculative work must never surface as an unobserved exception.
            // FetchAndObserveAsync already evicted the failed cache entry.
        }
    }

    private async Task<MediaItemDetail> FetchAndObserveAsync(
        string contentId,
        Func<Task<MediaItemDetail>> owner)
    {
        // Ensure GetOrStart can publish the shared task into the dictionary
        // before even a synchronously-completed fetch reports failure.
        await Task.Yield();
        try
        {
            return await _fetch(contentId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                if (_entries.TryGetValue(contentId, out var current)
                    && ReferenceEquals(current.Task, owner()))
                {
                    _entries.Remove(contentId);
                }
            }
            throw;
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var key in _entries
                     .Where(pair => now - pair.Value.CreatedAt >= _ttl)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _entries.Remove(key);
        }
    }
}
