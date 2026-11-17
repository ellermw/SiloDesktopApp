using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Shares bounded, short-lived item-detail requests between card intent and
/// the detail page. Entries are keyed by item and library scope, because a
/// library-scoped read is a different server request. Consumer cancellation
/// never cancels a request another surface may still use.
/// <para>
/// Speculative intent (<see cref="Prefetch"/>) owns at most one request at a
/// time. Newer intent replaces and cancels the older speculative request, and
/// a speculative request starts only after earlier speculative requests have
/// finished, so pointer sweeps cannot fan out. Foreground reads
/// (<see cref="GetAsync(string, int?, CancellationToken)"/>) adopt a matching
/// request, which then can no longer be cancelled, or start immediately. They
/// never wait behind unrelated speculative work, and they cancel unadopted
/// speculative work for other keys.
/// </para>
/// </summary>
public sealed class ItemDetailPrefetchCache
{
    private readonly record struct Key(string ContentId, int? LibraryId);

    private sealed class Entry(Key key, DateTimeOffset createdAt, CancellationTokenSource? speculation)
    {
        public Key Key { get; } = key;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public Task<MediaItemDetail> Task { get; set; } = null!;
        // Non-null only while the request is speculative, unadopted and unsettled.
        public CancellationTokenSource? Speculation { get; set; } = speculation;
        // Lets an adopted request skip the speculative start gate.
        public TaskCompletionSource? Promoted { get; } = speculation == null
            ? null : new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _sync = new();
    private readonly Dictionary<Key, Entry> _entries = new();
    private readonly Func<string, int?, CancellationToken, Task<MediaItemDetail>> _fetch;
    private readonly bool _supportsLibraryScope;
    private readonly Func<string> _contextKey;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly int _capacity;
    private string? _activeContextKey;
    // The single unadopted speculative entry, if any.
    private Entry? _speculative;
    // Completes once every previously started speculative request has settled.
    private Task _speculativeTail = Task.CompletedTask;
    // Removal sequence: a consumer that awaited a task across a Clear or an
    // Invalidate of its item must not apply that pre-mutation result.
    private long _sequence;
    private long _clearedAt;
    private readonly Dictionary<string, long> _invalidatedAt = new(StringComparer.Ordinal);
    private const int MaxReadAttempts = 3;
    private readonly Dictionary<string, (Task Pending, long Revision)> _progressSaves = new(StringComparer.Ordinal);
    private long _progressSaveRevision;
    private readonly Func<ApiIdentityContext>? _progressContext;
    private ApiIdentityContext? _progressSaveContext;

    public long ProgressSaveRevision
    {
        get { lock (_sync) { EnsureProgressContext(); return _progressSaveRevision; } }
    }

    private void EnsureProgressContext()
    {
        var context = _progressContext?.Invoke();
        // Access-only read invalidation must not retire same-profile writes.
        if (_progressSaveContext == context) return;
        _progressSaveContext = context;
        _progressSaves.Clear();
        ++_progressSaveRevision;
    }

    // Keep final saves alive independently of detail-page recipients. Scoped
    // loads use this barrier whether or not this cache serves their scope.
    public void TrackProgressSave(Task persisted, string? contentId = null, string? seriesId = null,
        Func<bool>? isCurrent = null)
    {
        List<CancellationTokenSource>? cancel = null;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (isCurrent?.Invoke() == false) return;
            EnsureProgressContext();
            ++_progressSaveRevision;
            ClearLocked(ref cancel);
            // The empty key is reserved for writes whose entire scope is unknown.
            foreach (var id in new[] { contentId, seriesId }.OfType<string>().Where(id => id.Length > 0)
                .DefaultIfEmpty(string.Empty).Distinct(StringComparer.Ordinal))
            {
                var pending = _progressSaves.TryGetValue(id, out var previous) && !previous.Pending.IsCompleted
                    ? Task.WhenAll(previous.Pending, settled.Task) : settled.Task;
                _progressSaves[id] = (pending, _progressSaveRevision);
            }
        }
        CancelAll(cancel);
        // Register outside the gate: an already settled write can invoke its
        // continuation inline, including cancellation callbacks.
        CompleteProgressSave(persisted, isCurrent, settled);
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

    private void CompleteProgressSave(Task persisted, Func<bool>? isCurrent, TaskCompletionSource settled) => _ = persisted.ContinueWith(save =>
    {
        // The write owner reports errors; readers wait for settlement only.
        _ = save.Exception;
        List<CancellationTokenSource>? cancel = null;
        lock (_sync)
            if (isCurrent?.Invoke() != false) ClearLocked(ref cancel);
        CancelAll(cancel);
        settled.TrySetResult();
    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public ItemDetailPrefetchCache(CatalogApi catalogApi, AuthService authService)
        : this((contentId, libraryId, cancellationToken) => libraryId.HasValue
                ? catalogApi.GetItemDetailAsync(contentId, libraryId, cancellationToken)
                : catalogApi.GetItemDetailAsync(contentId, cancellationToken),
            contextKey: () => string.Join('\n',
                authService.ConfiguredServerUrl,
                authService.CurrentUser?.Id,
                authService.SelectedProfileId,
                catalogApi.CaptureContext().RequestContextGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
                catalogApi.LocalizedReadGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture)))
    {
        _progressContext = catalogApi.CaptureIdentityContext;
    }

    /// <summary>
    /// Creates an unscoped-only cache. Library-scoped reads are not served
    /// (<see cref="SupportsLibraryScope"/> is false), so callers keep reading
    /// them directly and scoped speculative intent is ignored.
    /// </summary>
    public ItemDetailPrefetchCache(
        Func<string, CancellationToken, Task<MediaItemDetail>> fetch,
        TimeSpan? ttl = null,
        int capacity = 32,
        Func<string>? contextKey = null,
        TimeProvider? timeProvider = null)
        : this(Unscoped(fetch), supportsLibraryScope: false, ttl, capacity, contextKey, timeProvider)
    {
    }

    /// <summary>Creates a cache that serves both unscoped and library-scoped reads.</summary>
    public ItemDetailPrefetchCache(
        Func<string, int?, CancellationToken, Task<MediaItemDetail>> fetch,
        TimeSpan? ttl = null,
        int capacity = 32,
        Func<string>? contextKey = null,
        TimeProvider? timeProvider = null)
        : this(fetch ?? throw new ArgumentNullException(nameof(fetch)), supportsLibraryScope: true,
            ttl, capacity, contextKey, timeProvider)
    {
    }

    private ItemDetailPrefetchCache(
        Func<string, int?, CancellationToken, Task<MediaItemDetail>> fetch,
        bool supportsLibraryScope,
        TimeSpan? ttl,
        int capacity,
        Func<string>? contextKey,
        TimeProvider? timeProvider)
    {
        _fetch = fetch;
        _supportsLibraryScope = supportsLibraryScope;
        _contextKey = contextKey ?? (() => string.Empty);
        _time = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? TimeSpan.FromMinutes(2);
        _capacity = Math.Max(1, capacity);
    }

    private static Func<string, int?, CancellationToken, Task<MediaItemDetail>> Unscoped(
        Func<string, CancellationToken, Task<MediaItemDetail>> fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        return (contentId, _, cancellationToken) => fetch(contentId, cancellationToken);
    }

    /// <summary>True when library-scoped detail is read through this cache.</summary>
    public bool SupportsLibraryScope => _supportsLibraryScope;

    /// <summary>Cumulative request counters for request-count and concurrency diagnosis.</summary>
    public DetailPrefetchCounters Counters
    {
        get
        {
            lock (_sync)
                return new(_speculativeStarted, _speculativeCancelled, _speculativeAdopted,
                    _foregroundStarted, _foregroundReused, _speculativeRunning, _peakSpeculativeRunning);
        }
    }

    private int _speculativeStarted, _speculativeCancelled, _speculativeAdopted,
        _foregroundStarted, _foregroundReused, _speculativeRunning, _peakSpeculativeRunning;

    /// <summary>
    /// Starts or joins an owned read that the caller awaits. Like a foreground
    /// read it is never cancelled by newer speculative intent.
    /// </summary>
    public Task PrefetchAsync(string contentId, int? libraryId = null)
    {
        var key = KeyFor(contentId, libraryId);
        List<CancellationTokenSource>? cancel = null;
        Task<MediaItemDetail> task;
        lock (_sync) task = AcquireLocked(key, speculative: false, ref cancel);
        CancelAll(cancel);
        return task;
    }

    /// <summary>
    /// Starts a speculative request without leaking failures into the UI
    /// event that expressed intent. At most one speculative request is live;
    /// newer intent replaces it. Failed requests are evicted by
    /// <see cref="FetchAndObserveAsync"/>, so normal navigation can retry.
    /// </summary>
    public void Prefetch(string? contentId, int? libraryId = null)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return;
        // Nothing could consume a scoped result this cache cannot serve.
        if (libraryId.HasValue && !_supportsLibraryScope)
            return;

        var key = new Key(contentId, libraryId);
        List<CancellationTokenSource>? cancel = null;
        Task<MediaItemDetail> task;
        lock (_sync) task = AcquireLocked(key, speculative: true, ref cancel);
        CancelAll(cancel);
        _ = ObservePrefetchAsync(task);
    }

    /// <summary>
    /// Returns detail that was not invalidated while this caller awaited it.
    /// Clear() cannot retract a task already handed out, so a result whose
    /// item was cleared or invalidated during the await is read again; after
    /// <see cref="MaxReadAttempts"/> reads a <see cref="UserStateChangedException"/>
    /// is thrown instead of returning state known to be superseded.
    /// </summary>
    public Task<MediaItemDetail> GetAsync(
        string contentId,
        CancellationToken cancellationToken)
        => GetAsync(contentId, null, cancellationToken);

    /// <inheritdoc cref="GetAsync(string, CancellationToken)"/>
    public async Task<MediaItemDetail> GetAsync(
        string contentId,
        int? libraryId,
        CancellationToken cancellationToken)
    {
        var key = KeyFor(contentId, libraryId);
        for (var attempt = 1; ; attempt++)
        {
            long observed;
            Task<MediaItemDetail> task;
            List<CancellationTokenSource>? cancel = null;
            lock (_sync)
            {
                task = AcquireLocked(key, speculative: false, ref cancel);
                observed = _sequence;
            }
            CancelAll(cancel);
            var detail = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (!RemovedSince(contentId, observed)) return detail;
            }
            if (attempt >= MaxReadAttempts) throw new UserStateChangedException(contentId);
        }
    }

    /// <summary>
    /// Shares one initial snapshot with a detail page that owns its own scoped
    /// realtime and progress-save reconciliation. Invalidation evicts the entry
    /// for later callers without adding an independent corrective read here.
    /// </summary>
    public async Task<MediaItemDetail> GetSnapshotAsync(
        string contentId, int? libraryId, CancellationToken cancellationToken)
    {
        var key = KeyFor(contentId, libraryId);
        List<CancellationTokenSource>? cancel = null;
        Task<MediaItemDetail> task;
        lock (_sync) task = AcquireLocked(key, speculative: false, ref cancel);
        CancelAll(cancel);
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Key KeyFor(string contentId, int? libraryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        if (libraryId.HasValue && !_supportsLibraryScope)
            throw new NotSupportedException("This detail cache does not serve library-scoped reads.");
        return new Key(contentId, libraryId);
    }

    private bool RemovedSince(string contentId, long observed)
        => _clearedAt > observed || (_invalidatedAt.TryGetValue(contentId, out var at) && at > observed);

    /// <summary>Removes every library scope of the item.</summary>
    public void Invalidate(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return;
        List<CancellationTokenSource>? cancel = null;
        lock (_sync)
        {
            EnsureCurrentContext(ref cancel);
            foreach (var entry in _entries.Values.Where(candidate => candidate.Key.ContentId == contentId).ToList())
                RemoveEntryLocked(entry, ref cancel);
            _invalidatedAt[contentId] = ++_sequence;
        }
        CancelAll(cancel);
    }

    /// <summary>
    /// Drops every library scope of the item when the cache holds one, after a
    /// remote catalog change (another device's metadata save, a refresh job).
    /// A consumer still awaiting a dropped request reads again, as with
    /// <see cref="Invalidate"/>. Unlike that, an uncached item records nothing,
    /// so a scan's stream of catalog events cannot grow the per-item marks.
    /// </summary>
    public bool InvalidateCached(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return false;
        List<CancellationTokenSource>? cancel = null;
        var removed = false;
        lock (_sync)
        {
            EnsureCurrentContext(ref cancel);
            foreach (var entry in _entries.Values.Where(candidate => candidate.Key.ContentId == contentId).ToList())
            {
                RemoveEntryLocked(entry, ref cancel);
                removed = true;
            }
            if (removed) _invalidatedAt[contentId] = ++_sequence;
        }
        CancelAll(cancel);
        return removed;
    }

    public void Clear()
    {
        List<CancellationTokenSource>? cancel = null;
        lock (_sync) ClearLocked(ref cancel);
        CancelAll(cancel);
    }

    private void ClearLocked(ref List<CancellationTokenSource>? cancel)
    {
        RetireSpeculativeLocked(ref cancel);
        _entries.Clear();
        _activeContextKey = null;
        MarkClearedLocked();
    }

    private Task<MediaItemDetail> AcquireLocked(Key key, bool speculative, ref List<CancellationTokenSource>? cancel)
    {
        EnsureCurrentContext(ref cancel);
        var now = _time.GetUtcNow();
        PruneExpired(now, ref cancel);
        // Foreground intent for another item supersedes unadopted speculation.
        if (!speculative && _speculative is { } pending && pending.Key != key)
            RetireSpeculativeLocked(ref cancel);

        if (_entries.TryGetValue(key, out var cached))
        {
            if (!speculative)
            {
                if (cached.Speculation != null) AdoptLocked(cached);
                else _foregroundReused++;
            }
            return cached.Task;
        }

        // A newer speculative intent replaces the older one.
        if (speculative) RetireSpeculativeLocked(ref cancel);
        while (_entries.Count >= _capacity)
            RemoveEntryLocked(_entries.Values.MinBy(candidate => candidate.CreatedAt)!, ref cancel);

        var speculation = speculative ? new CancellationTokenSource() : null;
        var entry = new Entry(key, now, speculation);
        var gate = speculative ? _speculativeTail : null;
        entry.Task = FetchAndObserveAsync(entry, gate, speculation?.Token ?? CancellationToken.None);
        _entries[key] = entry;
        if (speculative)
        {
            _speculative = entry;
            // Settled tails are dropped so a long session never chains completed work.
            _speculativeTail = _speculativeTail.IsCompleted
                ? Settled(entry.Task) : Task.WhenAll(_speculativeTail, Settled(entry.Task));
            _speculativeStarted++;
        }
        else
        {
            _foregroundStarted++;
        }
        return entry.Task;
    }

    private void AdoptLocked(Entry entry)
    {
        // Adopted work belongs to a foreground consumer and is never cancelled.
        entry.Speculation = null;
        if (ReferenceEquals(_speculative, entry)) _speculative = null;
        entry.Promoted?.TrySetResult();
        _speculativeAdopted++;
    }

    private void RetireSpeculativeLocked(ref List<CancellationTokenSource>? cancel)
    {
        if (_speculative is not { } entry) return;
        _speculative = null;
        if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            _entries.Remove(entry.Key);
        if (entry.Speculation is { } speculation)
        {
            entry.Speculation = null;
            cancel ??= new List<CancellationTokenSource>();
            cancel.Add(speculation);
            _speculativeCancelled++;
        }
    }

    private void RemoveEntryLocked(Entry entry, ref List<CancellationTokenSource>? cancel)
    {
        if (ReferenceEquals(entry, _speculative)) RetireSpeculativeLocked(ref cancel);
        else if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            _entries.Remove(entry.Key);
    }

    // Cancel after the dictionary mutation: cancellation can run the
    // request's continuation inline, and that continuation takes this lock.
    private static void CancelAll(List<CancellationTokenSource>? cancel)
    {
        if (cancel == null) return;
        foreach (var source in cancel)
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { /* registrations report their own failures */ }
        }
    }

    private static Task Settled(Task task) => task.ContinueWith(static _ => { }, CancellationToken.None,
        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void EnsureCurrentContext(ref List<CancellationTokenSource>? cancel)
    {
        var contextKey = _contextKey() ?? string.Empty;
        if (_activeContextKey != null
            && !string.Equals(_activeContextKey, contextKey, StringComparison.Ordinal))
        {
            RetireSpeculativeLocked(ref cancel);
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
        Entry entry,
        Task? speculativeGate,
        CancellationToken cancellationToken)
    {
        // Ensure the caller can publish the shared task into the dictionary
        // before even a synchronously-completed fetch reports failure.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        var counted = false;
        try
        {
            // Speculative work starts only after earlier speculative requests
            // settle, unless a foreground consumer adopts it first.
            if (speculativeGate is { IsCompleted: false })
                await Task.WhenAny(speculativeGate, entry.Promoted!.Task)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (entry.Speculation != null)
                {
                    counted = true;
                    _peakSpeculativeRunning = Math.Max(_peakSpeculativeRunning, ++_speculativeRunning);
                }
            }
            using var timing = LibraryPerformanceTrace.Measure(
                counted ? "detail-read-speculative" : "detail-read-foreground");
            var detail = await _fetch(entry.Key.ContentId, entry.Key.LibraryId, cancellationToken)
                .ConfigureAwait(false);
            Settle(entry, failed: false, counted);
            return detail;
        }
        catch
        {
            Settle(entry, failed: true, counted);
            throw;
        }
    }

    private void Settle(Entry entry, bool failed, bool counted)
    {
        lock (_sync)
        {
            if (counted) _speculativeRunning--;
            entry.Speculation = null;
            if (ReferenceEquals(_speculative, entry)) _speculative = null;
            if (failed && _entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(entry.Key);
        }
    }

    private void PruneExpired(DateTimeOffset now, ref List<CancellationTokenSource>? cancel)
    {
        foreach (var entry in _entries.Values
                     .Where(candidate => now - candidate.CreatedAt >= _ttl)
                     .ToList())
        {
            RemoveEntryLocked(entry, ref cancel);
        }
    }
}

/// <summary>Cumulative detail-cache request counters for opt-in diagnosis.</summary>
public readonly record struct DetailPrefetchCounters(
    int SpeculativeStarted,
    int SpeculativeCancelled,
    int SpeculativeAdopted,
    int ForegroundStarted,
    int ForegroundReused,
    int SpeculativeRunning,
    int PeakSpeculativeRunning);
