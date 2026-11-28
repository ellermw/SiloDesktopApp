using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Keeps noisy library-scan notifications from continuously rebuilding Home
/// while allowing item, playback, and user-state changes to remain responsive.
/// Item-level catalog events (a scanning server sends one per file it touches)
/// refresh as the WebUI does (realtimeCatalogInvalidation.ts,
/// CATALOG_INVALIDATION_WINDOW_MS): the first after a quiet window at once,
/// later ones folded into one refresh per window.
/// </summary>
public sealed class HomeRealtimeRefreshGate
{
    /// <summary>The WebUI catalog invalidation window.</summary>
    public static readonly TimeSpan DefaultCatalogWindow = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _debounce;
    private readonly TimeSpan _catalogBurstCooldown;
    private readonly TimeSpan _catalogWindow;
    private readonly object _sync = new();
    private DateTimeOffset? _lastCatalogBurstRefresh;
    private DateTimeOffset? _lastCatalogItemRefresh;

    public HomeRealtimeRefreshGate(TimeSpan debounce, TimeSpan catalogBurstCooldown, TimeSpan? catalogWindow = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(catalogBurstCooldown, TimeSpan.Zero);
        _debounce = debounce;
        _catalogBurstCooldown = catalogBurstCooldown;
        _catalogWindow = catalogWindow ?? DefaultCatalogWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(_catalogWindow, TimeSpan.Zero);
    }

    public TimeSpan GetDelay(string reason, DateTimeOffset now)
    {
        if (IsCatalogItemChange(reason))
        {
            lock (_sync)
            {
                if (_lastCatalogItemRefresh is null) return _debounce;
                var windowEnd = _lastCatalogItemRefresh.Value + _catalogWindow;
                return windowEnd - now <= _debounce ? _debounce : windowEnd - now;
            }
        }
        if (!IsCatalogBurst(reason))
            return _debounce;

        lock (_sync)
        {
            if (_lastCatalogBurstRefresh is null)
                return _debounce;
            var nextAllowed = _lastCatalogBurstRefresh.Value + _catalogBurstCooldown;
            return nextAllowed <= now ? _debounce : nextAllowed - now;
        }
    }

    public void MarkRefreshed(string reason, DateTimeOffset now)
    {
        if (IsCatalogBurst(reason))
            lock (_sync)
                _lastCatalogBurstRefresh = now;
        else if (IsCatalogItemChange(reason))
            lock (_sync)
                _lastCatalogItemRefresh = now;
    }

    /// <summary>A catalog event that is not a scan burst: an item added, changed or re-matched.</summary>
    public static bool IsCatalogItemChange(string reason) =>
        reason.StartsWith("catalog:", StringComparison.OrdinalIgnoreCase) && !IsCatalogBurst(reason);

    public static bool IsCatalogBurst(string reason) =>
        string.Equals(reason, "catalog:library.changed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reason, "catalog:catalog.library.changed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reason, "catalog:metadata.updated", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reason, "catalog:catalog.metadata.updated", StringComparison.OrdinalIgnoreCase);

    public static string ClassifyCatalogEvent(string eventName, JsonElement data)
    {
        var reason = $"catalog:{eventName}";
        if (!string.Equals(reason, "catalog:library.changed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(reason, "catalog:catalog.library.changed", StringComparison.OrdinalIgnoreCase))
            return reason;

        return HasPositiveCount(data, "new")
            || HasPositiveCount(data, "updated")
            || HasPositiveCount(data, "missing")
            ? $"{reason}:content_changed"
            : reason;
    }

    private static bool HasPositiveCount(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var count)
        && count > 0;
}
