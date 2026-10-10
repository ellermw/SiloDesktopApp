namespace SiloPlayer.Core.Api;

public sealed record QueryEditorCapabilities(bool ExtendedRules, IReadOnlySet<string> ShownRatingSources);

public partial class CatalogApi
{
    private sealed class SearchRuleCapabilities { public bool ExtendedQueryRules { get; set; } public bool FacetValueSearch { get; set; } }
    private sealed record SearchRuleCache(ApiRequestContext Context, DateTime LoadedAt, SearchRuleCapabilities Value);
    private SearchRuleCache? _searchRuleCache;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ApiRequestContext, SemaphoreSlim> _searchRuleGates = new();

    private async Task<SearchRuleCapabilities> GetSearchRuleCapabilitiesAsync(ApiRequestContext context, CancellationToken ct)
    {
        var gate = _searchRuleGates.GetOrAdd(context, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Query capability context changed.", ct);
            if (_searchRuleCache is {} cached && cached.Context == context && DateTime.UtcNow - cached.LoadedAt < TimeSpan.FromMinutes(5)) return cached.Value;
            var response = await client.SendRequestAsync<SearchRuleCapabilities>(context, HttpMethod.Get, "/api/v2/catalog/search/capabilities", null, ct);
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Query capability context changed.", ct);
            _searchRuleCache = new(context, DateTime.UtcNow, response);
            return response;
        }
        finally
        {
            gate.Release();
            foreach (var entry in _searchRuleGates)
                if (entry.Key != client.CaptureContext()) _searchRuleGates.TryRemove(entry.Key, out _);
        }
    }

    public async Task<QueryEditorCapabilities> GetQueryEditorCapabilitiesAsync(CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        async Task<bool> ExtendedAsync()
        {
            try
            {
                return (await GetSearchRuleCapabilitiesAsync(context, ct)).ExtendedQueryRules;
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
        }
        async Task<IReadOnlySet<string>> RatingsAsync()
        {
            try { return await GetShownRatingSourcesAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch { return NoRatingSources; }
        }
        var extended = ExtendedAsync(); var ratings = RatingsAsync();
        await Task.WhenAll(extended, ratings);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Query capability context changed.", ct);
        return new(await extended, await ratings);
    }
}
