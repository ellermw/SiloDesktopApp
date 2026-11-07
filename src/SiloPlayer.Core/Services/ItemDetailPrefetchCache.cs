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
    private readonly Dictionary<string, (Task Pending, long Revision)> _progressSaves = new(StringComparer.Ordinal);
    private long _progressSaveRevision;
    private readonly Func<ApiRequestContext>? _progressContext;
    private ApiRequestContext? _progressSaveContext;

    public long ProgressSaveRevision
    {
        get { lock (_sync) { EnsureProgressContext(); return _progressSaveRevision; } }
    }

    private void EnsureProgressContext()
    {
        var context = _progressContext?.Invoke();
        // Access-only read invalidation must not retire same-profile writes.
        if ((_progressSaveContext?.AuthenticationGeneration, _progressSaveContext?.BaseUrl, _progressSaveContext?.ProfileId)
            == (context?.AuthenticationGeneration, context?.BaseUrl, context?.ProfileId)) return;
        _progressSaveContext = context;
        _progressSaves.Clear();
        ++_progressSaveRevision;
    }

    // Keep final saves alive independently of detail-page recipients. Scoped
    // loads also use this barrier even though they bypass the prefetch cache.
    public void TrackProgressSave(Task persisted, string? contentId = null, string? seriesId = null)
    {
        lock (_sync)
        {
            EnsureProgressContext();
            ++_progressSaveRevision;
            Clear();
            var settled = ClearAfterProgressSaveAsync(persisted);
            // The empty key is reserved for writes whose entire scope is unknown.
            foreach (var id in new[] { contentId, seriesId }.OfType<string>().Where(id => id.Length > 0)
                .DefaultIfEmpty(string.Empty).Distinct(StringComparer.Ordinal))
            {
                var pending = _progressSaves.TryGetValue(id, out var previous) && !previous.Pending.IsCompleted
                    ? Task.WhenAll(previous.Pending, settled) : settled;
                _progressSaves[id] = (pending, _progressSaveRevision);
            }
        }
    }

    private static bool ProgressScopeMatches(string id, string? contentId, string? seriesId) =>
        contentId == null || id.Length == 0 || id == contentId || id == seriesId;

    private long ProgressSaveRevisionLocked(string? contentId, string? seriesId) =>
        _progressSaves.Where(pair => ProgressScopeMatches(pair.Key, contentId, seriesId))
            .Select(pair => pair.Value.Revision).DefaultIfEmpty(0).Max();

    public long GetProgressSaveRevision(string contentId, string? seriesId = null)
    {
        lock (_sync)
        {
            EnsureProgressContext();
            return ProgressSaveRevisionLocked(contentId, seriesId);
        }
    }

    // Capture every scope before a read, since its series may only become
    // known in the response. Pending saves are deliberately absent: even if
    // they settle during the read, that snapshot still needs reconciliation.
    public IReadOnlyDictionary<string, long> CaptureSettledProgressSaves()
    {
        lock (_sync)
        {
            EnsureProgressContext();
            return _progressSaves.Where(pair => pair.Value.Pending.IsCompleted)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Revision, StringComparer.Ordinal);
        }
    }

    public bool HasProgressSavesSince(
        IReadOnlyDictionary<string, long> settled, string contentId, string? seriesId = null)
    {
        lock (_sync)
        {
            EnsureProgressContext();
            return _progressSaves.Any(pair => ProgressScopeMatches(pair.Key, contentId, seriesId)
                && (!settled.TryGetValue(pair.Key, out var revision) || revision != pair.Value.Revision));
        }
    }

    public async Task WaitForProgressSettlementAsync(
        CancellationToken cancellationToken, string? contentId = null, string? seriesId = null)
    {
        while (true)
        {
            Task pending;
            long revision;
            lock (_sync)
            {
                EnsureProgressContext();
                revision = ProgressSaveRevisionLocked(contentId, seriesId);
                pending = Task.WhenAll(_progressSaves.Where(pair => ProgressScopeMatches(pair.Key, contentId, seriesId))
                    .Select(pair => pair.Value.Pending));
            }
            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                EnsureProgressContext();
                if (revision == ProgressSaveRevisionLocked(contentId, seriesId)) return;
            }
        }
    }

    private Task ClearAfterProgressSaveAsync(Task persisted) => persisted.ContinueWith(static (save, state) =>
    {
        // The write owner reports errors; readers wait for settlement only.
        _ = save.Exception;
        ((ItemDetailPrefetchCache)state!).Clear();
    }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

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
        _progressContext = catalogApi.CaptureContext;
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
