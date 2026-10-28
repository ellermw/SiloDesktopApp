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
    // Removal sequence: a consumer that awaited a task across a Clear or an
    // Invalidate of its item must not apply that pre-mutation result.
    private long _sequence;
    private long _clearedAt;
    private readonly Dictionary<string, long> _invalidatedAt = new(StringComparer.Ordinal);
    private const int MaxReadAttempts = 3;

    public ItemDetailPrefetchCache(CatalogApi catalogApi, AuthService authService)
        : this((contentId, cancellationToken) =>
                catalogApi.GetItemDetailAsync(contentId, cancellationToken),
            contextKey: () => string.Join('\n',
                authService.ConfiguredServerUrl,
                authService.CurrentUser?.Id,
                authService.SelectedProfileId,
                catalogApi.CaptureContext().RequestContextGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                catalogApi.LocalizedReadGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture)))
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

    /// <summary>
    /// Returns detail that was not invalidated while this caller awaited it.
    /// Clear() cannot retract a task already handed out, so a result whose
    /// item was cleared or invalidated during the await is read again; after
    /// <see cref="MaxReadAttempts"/> reads a <see cref="UserStateChangedException"/>
    /// is thrown instead of returning state known to be superseded.
    /// </summary>
    public async Task<MediaItemDetail> GetAsync(
        string contentId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            long observed;
            Task<MediaItemDetail> task;
            lock (_sync)
            {
                task = GetOrStart(contentId);
                observed = _sequence;
            }
            var detail = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (!RemovedSince(contentId, observed)) return detail;
            }
            if (attempt >= MaxReadAttempts) throw new UserStateChangedException(contentId);
        }
    }

    private bool RemovedSince(string contentId, long observed)
        => _clearedAt > observed || (_invalidatedAt.TryGetValue(contentId, out var at) && at > observed);

    public void Invalidate(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return;
        lock (_sync)
        {
            EnsureCurrentContext();
            _entries.Remove(contentId);
            _invalidatedAt[contentId] = ++_sequence;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _activeContextKey = null;
            MarkClearedLocked();
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
            MarkClearedLocked();
        }
        _activeContextKey = contextKey;
    }

    private void MarkClearedLocked()
    {
        _clearedAt = ++_sequence;
        _invalidatedAt.Clear(); // a clear supersedes every per-item mark
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
