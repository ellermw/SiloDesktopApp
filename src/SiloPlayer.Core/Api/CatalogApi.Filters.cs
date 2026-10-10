namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    public async Task<IReadOnlyList<string>> SearchFacetAsync(string facet, string query, int? libraryId = null, string? type = null, CancellationToken ct = default)
    {
        var context = client.CaptureContext(); var ranked = false;
        try { ranked = (await GetSearchRuleCapabilitiesAsync(context, ct)).FacetValueSearch; }
        catch (OperationCanceledException) { throw; }
        catch { /* Older servers search typed values without a library restriction. */ }
        if (!ranked && string.IsNullOrWhiteSpace(query)) return [];
        var path = $"/api/v2/catalog/filters/search?source=query&facet={Uri.EscapeDataString(facet)}&q={Uri.EscapeDataString(query.Trim())}&limit=50";
        if (ranked && libraryId is > 0) path += $"&library_ids={libraryId.Value}";
        if (!string.IsNullOrWhiteSpace(type) && type != "all") path += "&type=" + Uri.EscapeDataString(type);
        var response = await client.SendRequestAsync<FacetMatches>(context, HttpMethod.Get, path, null, ct);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Facet value context changed.", ct);
        return ranked ? response.Values.Select(value => value.Value).ToArray() : response.Matches;
    }
    private sealed class FacetMatches { public List<string> Matches { get; set; } = []; public List<FacetValue> Values { get; set; } = []; }
    private sealed class FacetValue { public string Value { get; set; } = ""; }

    public async Task<SiloPlayer.Core.Models.Catalog.CatalogFiltersResponse> GetRuleLanguagesAsync(int? libraryId, string? type, bool includeTechnical, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var capability = await GetSearchRuleCapabilitiesAsync(context, ct);
        var path = "/api/v2/catalog/filters?source=query";
        if (capability.FacetValueSearch && libraryId is > 0) path += $"&library_ids={libraryId.Value}";
        if (!string.IsNullOrWhiteSpace(type) && type != "all") path += "&type=" + Uri.EscapeDataString(type);
        if (!includeTechnical) path += "&skip_technical=true";
        var result = await client.SendRequestAsync<SiloPlayer.Core.Models.Catalog.CatalogFiltersResponse>(context, HttpMethod.Get, path, null, ct);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Language value context changed.", ct);
        return result;
    }
}
