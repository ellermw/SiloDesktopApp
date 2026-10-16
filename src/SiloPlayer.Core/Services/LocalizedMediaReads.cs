using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>Retires localized reads without retiring playback identity, settings writes or permissions.</summary>
public sealed class LocalizedMediaReads
{
    private readonly object _gate = new();
    private CancellationTokenSource _pending = new();
    private long _generation;
    public long Generation { get { lock (_gate) return _generation; } }
    public event Action<long>? Invalidated;

    public void NotifySettingChanged(string? key)
    {
        if (key is not ("catalog.metadata_language" or "catalog.metadata_language_overrides")) return;
        CancellationTokenSource retired;
        long generation;
        lock (_gate)
        {
            retired = _pending;
            _pending = new();
            generation = ++_generation;
        }
        try { retired.Cancel(); }
        catch (AggregateException ex) { LocalLog.AppendException("catalog_errors.txt", ex, "localized_read_cancellation"); }
        finally { retired.Dispose(); }
        if (Invalidated is { } listeners)
            foreach (Action<long> listener in listeners.GetInvocationList())
                try { listener(generation); }
                catch (Exception ex) { LocalLog.AppendException("catalog_errors.txt", ex, "localized_read_invalidation"); }
    }

    public void NotifyRealtime(string channel, string eventName, JsonElement data, string? selectedProfileId)
    {
        if (channel != "user_settings" || eventName != "user_settings.changed" || data.ValueKind != JsonValueKind.Object) return;
        if (data.TryGetProperty("profile_id", out var profile) && profile.ValueKind == JsonValueKind.String
            && profile.GetString() is { Length: > 0 } changed && !string.IsNullOrEmpty(selectedProfileId) && changed != selectedProfileId) return;
        if (data.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String)
            NotifySettingChanged(key.GetString());
    }

    public void ThrowIfChanged(long generation)
    {
        if (Generation != generation) throw new OperationCanceledException("The metadata language changed.");
    }

    internal Lease? Begin(HttpRequestMessage request, CancellationToken consumer)
    {
        if (!IsLocalizedRead(request.Method, request.RequestUri?.AbsolutePath ?? "")) return null;
        lock (_gate) return new(this, _generation, CancellationTokenSource.CreateLinkedTokenSource(consumer, _pending.Token));
    }

    private static bool IsLocalizedRead(HttpMethod method, string path)
    {
        if (method == HttpMethod.Post) return path is "/api/v2/catalog/query" or "/api/v2/catalog/items/query";
        if (method != HttpMethod.Get) return false;
        string[] roots = ["/api/v2/catalog", "/api/v2/home", "/api/v2/library", "/api/v2/libraries", "/api/v2/collections",
            "/api/v2/recommendations", "/api/v2/calendar", "/api/v2/people", "/api/v2/search", "/api/v2/sections",
            "/api/v2/favorites", "/api/v2/watchlist", "/api/v2/history", "/api/v2/progress", "/api/v2/shuffles"];
        if (roots.Any(root => path == root || path.StartsWith(root + "/", StringComparison.Ordinal))) return true;
        return path.StartsWith("/api/v2/watch-together/", StringComparison.Ordinal)
            && (path.Contains("/picker", StringComparison.Ordinal) || path.Contains("/search", StringComparison.Ordinal)
                || path.Contains("/recently-added", StringComparison.Ordinal));
    }

    internal sealed class Lease(LocalizedMediaReads owner, long generation, CancellationTokenSource cancellation) : IDisposable
    {
        public CancellationToken Token => cancellation.Token;
        public void EnsureCurrent() { Token.ThrowIfCancellationRequested(); owner.ThrowIfChanged(generation); }
        public void Dispose() => cancellation.Dispose();
    }
}
