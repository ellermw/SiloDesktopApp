using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Routes remote catalog item changes (another device's metadata save, a
/// refresh or rescan) to the shared detail cache and to open detail pages.
/// <para>
/// The shell feeds every realtime event to <see cref="Receive"/> from the socket
/// thread. Work there is bounded: a matching event drops that item's cached
/// detail (never recording anything for an uncached item) and signals the
/// matching open pages. No read starts here.
/// </para>
/// <para>
/// An open page owns a <see cref="DetailMetadataWatch"/> for its visit. Signals
/// coalesce into one reload after <see cref="CoalesceWindow"/>, with at most one
/// reload in flight and one trailing reload for signals during it. The page may
/// hold a reload (editor open, writes pending, playback, first paint); a held
/// reload stays pending until the page resumes it or another change arrives.
/// An identity change drops pending work, and disposing the watch at navigation
/// cancels it.
/// </para>
/// </summary>
public sealed class DetailMetadataChangeHub
{
    public static readonly TimeSpan DefaultCoalesceWindow = TimeSpan.FromSeconds(1);

    // The official WebUI's CATALOG_ITEM_CHANGED_EVENTS (RealtimeEventsProvider).
    private static readonly HashSet<string> ItemEvents = new(StringComparer.Ordinal)
    {
        "catalog.item.changed",
        "library.item_added",
        "metadata.updated",
    };

    private readonly object _gate = new();
    private readonly ItemDetailPrefetchCache? _cache;
    private readonly List<DetailMetadataWatch> _watches = [];

    public DetailMetadataChangeHub(
        ItemDetailPrefetchCache? cache,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? coalesceWindow = null)
    {
        _cache = cache;
        Delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
        CoalesceWindow = coalesceWindow ?? DefaultCoalesceWindow;
    }

    public TimeSpan CoalesceWindow { get; }
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; }

    internal int WatchCount { get { lock (_gate) return _watches.Count; } }

    /// <summary>The item a catalog event names, or null for any other event.</summary>
    public static string? ChangedItemId(string channel, string eventName, JsonElement data)
    {
        if (!string.Equals(channel, "catalog", StringComparison.OrdinalIgnoreCase) || !ItemEvents.Contains(eventName)
            || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("content_id", out var id)
            || id.ValueKind != JsonValueKind.String) return null;
        var contentId = id.GetString();
        return string.IsNullOrWhiteSpace(contentId) ? null : contentId;
    }

    /// <summary>Handles one realtime event. The payload is read before returning; nothing keeps it.</summary>
    public bool Receive(string channel, string eventName, JsonElement data)
    {
        if (ChangedItemId(channel, eventName, data) is not { } contentId) return false;
        NotifyItemChanged(contentId);
        return true;
    }

    /// <summary>Drops the item's cached detail and signals every open page showing it.</summary>
    public void NotifyItemChanged(string contentId, bool structural = false)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return;
        _cache?.InvalidateCached(contentId);
        DetailMetadataWatch[] matches;
        lock (_gate)
            matches = _watches.Where(watch => string.Equals(watch.ContentId, contentId, StringComparison.Ordinal)).ToArray();
        foreach (var watch in matches) watch.Signal(structural);
    }

    /// <summary>
    /// Starts watching one page visit's item.
    /// <paramref name="reload"/> receives whether a structural change (files,
    /// versions) is pending, and returns false to hold the reload.
    /// </summary>
    public DetailMetadataWatch Watch(
        string contentId,
        Func<bool> isIdentityCurrent,
        Func<bool, CancellationToken, Task<bool>> reload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        var watch = new DetailMetadataWatch(this, contentId, isIdentityCurrent, reload);
        lock (_gate) _watches.Add(watch);
        return watch;
    }

    internal void Remove(DetailMetadataWatch watch)
    {
        lock (_gate) _watches.Remove(watch);
    }
}

/// <summary>One detail page visit's coalesced reload. See <see cref="DetailMetadataChangeHub"/>.</summary>
public sealed class DetailMetadataWatch : IDisposable
{
    private readonly object _gate = new();
    private readonly DetailMetadataChangeHub _hub;
    private readonly Func<bool> _isIdentityCurrent;
    private readonly Func<bool, CancellationToken, Task<bool>> _reload;
    // Cancelled, never disposed: a pump may still read its token after Dispose.
    private readonly CancellationTokenSource _lifetime = new();
    private bool _pending, _pendingStructural, _pumping, _held, _disposed, _retired;

    internal DetailMetadataWatch(
        DetailMetadataChangeHub hub,
        string contentId,
        Func<bool> isIdentityCurrent,
        Func<bool, CancellationToken, Task<bool>> reload)
    {
        _hub = hub;
        ContentId = contentId;
        _isIdentityCurrent = isIdentityCurrent ?? throw new ArgumentNullException(nameof(isIdentityCurrent));
        _reload = reload ?? throw new ArgumentNullException(nameof(reload));
    }

    public string ContentId { get; }

    /// <summary>False once the visit ended or its identity changed.</summary>
    public bool IsActive { get { lock (_gate) return !_disposed && !_retired; } }

    /// <summary>True while a change waits for the page to resume it.</summary>
    public bool IsHeld { get { lock (_gate) return _held && _pending; } }

    /// <summary>Records a change and schedules one coalesced reload.</summary>
    public void Signal(bool structural = false)
    {
        lock (_gate)
        {
            if (_disposed || _retired) return;
            _pending = true;
            _pendingStructural |= structural;
            if (_pumping) return;
            _held = false;
            _pumping = true;
        }
        _ = PumpAsync();
    }

    /// <summary>The page's hold ended: run a held reload, if any.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed || _retired || !_held || !_pending || _pumping) return;
            _held = false;
            _pumping = true;
        }
        _ = PumpAsync();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = false;
            _pendingStructural = false;
        }
        _hub.Remove(this);
        // Outside the gate: cancellation can run the pump's continuation inline.
        _lifetime.Cancel();
    }

    private async Task PumpAsync()
    {
        var token = _lifetime.Token;
        try
        {
            while (true)
            {
                await _hub.Delay(_hub.CoalesceWindow, token).ConfigureAwait(false);
                bool structural;
                lock (_gate)
                {
                    if (_disposed || _retired) { _pumping = false; return; }
                    structural = _pendingStructural;
                    _pending = false;
                    _pendingStructural = false;
                }
                if (!_isIdentityCurrent())
                {
                    // Another account or profile now owns the client: this visit's work is void.
                    lock (_gate)
                    {
                        _retired = true;
                        _pending = false;
                        _pendingStructural = false;
                        _pumping = false;
                    }
                    return;
                }

                bool applied;
                try { applied = await _reload(structural, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { applied = true; }
                catch { applied = true; } // the reload owner reports its own failures

                lock (_gate)
                {
                    if (_disposed || _retired) { _pumping = false; return; }
                    if (!applied)
                    {
                        _pending = true;
                        _pendingStructural |= structural;
                        _held = true;
                        _pumping = false;
                        return;
                    }
                    if (!_pending) { _pumping = false; return; }
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate) _pumping = false;
        }
    }
}
