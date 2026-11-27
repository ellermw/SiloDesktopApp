namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// What one Insights read shows right now. <see cref="Data"/> is the result of the requested key, or (when the reader keeps the
/// previous result) the last one shown while the new key loads, in which case <see cref="IsPlaceholder"/> is true: the page keeps it on
/// screen with its links disabled (official <c>keepPrevious</c> and <c>insightRefreshProps</c>, c59798116).
/// </summary>
public sealed record AdminInsightsReadView<T>(T? Data, bool IsPlaceholder, bool IsLoading, bool IsFetching, Exception? Error, Exception? RefreshError)
    where T : class
{
    /// <summary>Nothing has been requested.</summary>
    public static AdminInsightsReadView<T> Idle { get; } = new(null, false, false, false, null, null);

    /// <summary>True when the section should show its load error: the requested key failed and has nothing of its own to show.</summary>
    public bool ShowsError => Data == null && Error != null;
}

/// <summary>
/// One Insights result for one authority lease: an Insights tab's read, or the title and person panels' (official
/// <c>useAdminInsights*</c> hooks, <c>hooks/queries/admin/insights.ts</c> at c59798116).
/// <list type="bullet">
/// <item>A result is fresh for <see cref="AdminInsightsIntervals.TabStale"/> (45 seconds), then it is read again the next time it is
/// requested; nothing polls.</item>
/// <item>The result of every key read in this visit is kept, so going back to a window or tab shows it at once.</item>
/// <item>With <c>keepPrevious</c> the result last shown stays up (marked as a placeholder) while a different key loads. A panel never
/// keeps it: another subject's numbers must not stand in for this one's.</item>
/// <item>A failed read of a key that has data keeps that data and reports <see cref="AdminInsightsReadView{T}.RefreshError"/>; a failed read
/// of a key with none shows the error and no placeholder.</item>
/// <item>The reader belongs to one <see cref="MonitorAuthorityLease"/>. Once that retires nothing is published, so a result never
/// outlives its account, server or profile.</item>
/// </list>
/// Not thread-safe: call it from the UI thread. Loaders run wherever they like and complete back on the caller's context.
/// </summary>
public sealed class AdminInsightsReader<T> : IDisposable where T : class
{
    private const int MaxEntries = 32;

    private sealed class Entry
    {
        public T? Data;
        public DateTimeOffset AttemptedAt;
        public Exception? Error;
        public Task? InFlight;
        public CancellationTokenSource? Cts;
        public long Generation;
    }

    private readonly MonitorAuthorityLease _lease;
    private readonly TimeProvider _time;
    private readonly TimeSpan _stale;
    private readonly bool _keepPrevious;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private string? _key;
    private T? _placeholder;
    private bool _disposed;

    public AdminInsightsReader(MonitorAuthorityLease lease, bool keepPrevious, TimeProvider? time = null, TimeSpan? stale = null)
    {
        _lease = lease;
        _keepPrevious = keepPrevious;
        _time = time ?? TimeProvider.System;
        _stale = stale ?? AdminInsightsIntervals.TabStale;
    }

    /// <summary>Raised after the view changed. May be raised from any context the loader completes on.</summary>
    public event Action? Changed;

    /// <summary>The key last requested, or null.</summary>
    public string? Key => _key;

    public AdminInsightsReadView<T> View
    {
        get
        {
            if (_disposed || _key == null || !_entries.TryGetValue(_key, out var entry)) return AdminInsightsReadView<T>.Idle;
            var fetching = entry.InFlight != null;
            if (entry.Data != null) return new(entry.Data, false, false, fetching, null, entry.Error);
            if (entry.Error != null && !fetching) return new(null, false, false, false, entry.Error, null);
            return _keepPrevious && _placeholder != null
                ? new(_placeholder, true, false, true, null, null)
                : new(null, false, true, true, null, null);
        }
    }

    /// <summary>
    /// Shows <paramref name="key"/> and reads it when it has no result, a stale one, or <paramref name="force"/> asks (Refresh, Retry).
    /// The key carries everything the read depends on except the authority, so a different window, scope or sort is a different key.
    /// The returned task completes when this key has been read (at once when nothing needed reading); it never throws. <see cref="Changed"/> is
    /// raised only when something visible changed (another key, or a read started): a caller that draws from <see cref="Changed"/> and asks again
    /// while drawing (the title panel) would otherwise ask itself in a loop.
    /// </summary>
    public Task Request(string key, Func<CancellationToken, Task<T>> load, bool force = false)
    {
        if (_disposed || !_lease.IsCurrent) return Task.CompletedTask;
        var keyChanged = _key != key;
        if (_key != null && keyChanged) _placeholder = _keepPrevious ? View.Data : null;
        _key = key;
        if (!_entries.TryGetValue(key, out var entry))
        {
            Evict();
            _entries[key] = entry = new Entry();
        }
        if (entry.Data != null) _placeholder = null;
        var fresh = (entry.Data != null || entry.Error != null) && _time.GetUtcNow() - entry.AttemptedAt < _stale;
        if (entry.InFlight != null && !force)
        {
            if (keyChanged) Raise();
            return entry.InFlight;
        }
        if (fresh && !force)
        {
            if (keyChanged) Raise();
            return Task.CompletedTask;
        }
        var fetch = Fetch(key, entry, load);
        Raise();
        return fetch;
    }

    private Task Fetch(string key, Entry entry, Func<CancellationToken, Task<T>> load)
    {
        var generation = ++entry.Generation;
        entry.Cts?.Cancel();
        var cts = new CancellationTokenSource();
        entry.Cts = cts;
        var run = RunAsync();
        entry.InFlight = run.IsCompleted ? null : run;
        return run;

        async Task RunAsync()
        {
            T? data = null;
            Exception? error = null;
            try
            {
                data = await load(cts.Token);
            }
            catch (OperationCanceledException) when (_disposed || !_lease.IsCurrent || generation != entry.Generation)
            {
                return;
            }
            catch (Exception exception)
            {
                error = exception;
            }
            if (_disposed || !_lease.IsCurrent || generation != entry.Generation) return;
            entry.InFlight = null;
            entry.AttemptedAt = _time.GetUtcNow();
            if (error == null)
            {
                entry.Data = data;
                entry.Error = null;
                if (_key == key) _placeholder = null;
            }
            else entry.Error = error;
            if (_key == key) Raise();
        }
    }

    private void Evict()
    {
        if (_entries.Count < MaxEntries) return;
        var oldest = _entries.Where(pair => pair.Value.InFlight == null && pair.Key != _key)
            .OrderBy(pair => pair.Value.AttemptedAt).Select(pair => pair.Key).FirstOrDefault();
        if (oldest != null) _entries.Remove(oldest);
    }

    private void Raise()
    {
        if (!_disposed) Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries.Values) entry.Cts?.Cancel();
        _entries.Clear();
        _placeholder = null;
        Changed = null;
    }
}
