using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Keeps noisy library-scan notifications from continuously rebuilding Home
/// while allowing item, playback, and user-state changes to remain responsive.
/// </summary>
public sealed class HomeRealtimeRefreshGate
{
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _catalogBurstCooldown;
    private readonly object _sync = new();
    private DateTimeOffset? _lastCatalogBurstRefresh;

    public HomeRealtimeRefreshGate(TimeSpan debounce, TimeSpan catalogBurstCooldown)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(debounce, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(catalogBurstCooldown, TimeSpan.Zero);
        _debounce = debounce;
        _catalogBurstCooldown = catalogBurstCooldown;
    }

    public TimeSpan GetDelay(string reason, DateTimeOffset now)
    {
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
    }

    public static bool IsCatalogBurst(string reason) =>
        string.Equals(reason, "catalog:library.changed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reason, "catalog:catalog.library.changed", StringComparison.OrdinalIgnoreCase);

    public static string ClassifyCatalogEvent(string eventName, JsonElement data)
    {
        var reason = $"catalog:{eventName}";
        if (!IsCatalogBurst(reason))
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
