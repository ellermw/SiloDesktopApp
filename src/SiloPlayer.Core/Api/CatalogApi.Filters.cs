namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    public async Task<IReadOnlyList<string>> SearchFacetAsync(string facet, string query, int? libraryId = null, string? type = null, CancellationToken ct = default)
    {
        var path = $"/api/v2/catalog/filters/search?source=query&facet={Uri.EscapeDataString(facet)}&q={Uri.EscapeDataString(query.Trim())}&limit=20";
        if (libraryId is > 0) path += $"&library_id={libraryId.Value}";
        if (!string.IsNullOrWhiteSpace(type)) path += "&type=" + Uri.EscapeDataString(type);
        return (await client.GetAsync<FacetMatches>(path, ct)).Matches;
    }
    private sealed class FacetMatches { public List<string> Matches { get; set; } = []; }
}
