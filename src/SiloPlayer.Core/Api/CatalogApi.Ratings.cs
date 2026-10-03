using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    private sealed record RatingCapabilityCache(ApiRequestContext Context, int Generation, DateTime LoadedAt, IReadOnlySet<string> Sources);
    private RatingCapabilityCache? _ratingCapability;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(ApiRequestContext Context, int Generation), SemaphoreSlim> _ratingCapabilityGates = new();
    private int _ratingCapabilityGeneration;
    private readonly object _ratingCapabilityStateGate = new();
    private static readonly IReadOnlySet<string> NoRatingSources = new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlySet<string> CachedShownRatingSources
    {
        get
        {
            var cached = _ratingCapability;
            return cached != null && cached.Context == client.CaptureContext() && cached.Generation == Volatile.Read(ref _ratingCapabilityGeneration)
                && DateTime.UtcNow - cached.LoadedAt < TimeSpan.FromMinutes(1) ? cached.Sources : NoRatingSources;
        }
    }
    public void InvalidateRatingCapability()
    {
        lock (_ratingCapabilityStateGate)
        {
            Interlocked.Increment(ref _ratingCapabilityGeneration);
            _ratingCapability = null;
            _ratingCapabilityGates.Clear();
        }
    }
    public async Task<IReadOnlySet<string>> GetShownRatingSourcesAsync(CancellationToken ct = default)
    {
        var context = client.CaptureContext(); var generation = Volatile.Read(ref _ratingCapabilityGeneration);
        var key = (context, generation);
        var gate = _ratingCapabilityGates.GetOrAdd(key, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!client.IsCurrentContext(context) || generation != Volatile.Read(ref _ratingCapabilityGeneration)) throw new OperationCanceledException("Rating capability authority changed.", ct);
            var cached = _ratingCapability;
            if (cached != null && cached.Context == context && cached.Generation == generation && DateTime.UtcNow - cached.LoadedAt < TimeSpan.FromMinutes(1)) return cached.Sources;
            var capability = await client.SendRequestAsync<RatingsCapability>(context, HttpMethod.Get, "/api/v2/capabilities/ratings", null, ct);
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(context) || generation != Volatile.Read(ref _ratingCapabilityGeneration)) throw new OperationCanceledException("Rating capability authority changed.", ct);
            IReadOnlySet<string> sources = capability.State == "available"
                ? capability.Sources.Where(source => !string.IsNullOrWhiteSpace(source.Source)).Select(source => source.Source).ToHashSet(StringComparer.Ordinal)
                : NoRatingSources;
            lock (_ratingCapabilityStateGate)
            {
                if (!client.IsCurrentContext(context) || generation != Volatile.Read(ref _ratingCapabilityGeneration))
                    throw new OperationCanceledException("Rating capability authority changed.", ct);
                _ratingCapability = new(context, generation, DateTime.UtcNow, sources);
            }
            return sources;
        }
        finally
        {
            gate.Release();
            foreach (var entry in _ratingCapabilityGates)
                if (entry.Key.Context != client.CaptureContext() || entry.Key.Generation != Volatile.Read(ref _ratingCapabilityGeneration))
                    _ratingCapabilityGates.TryRemove(entry.Key, out _);
        }
    }
}
