using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Api;

public class CatalogApi(ContinuumApiClient client)
{
    public Task<List<Library>> GetLibrariesAsync(CancellationToken ct = default)
        => client.GetAsync<List<Library>>("/api/v1/user/libraries", ct);

    public Task<CatalogResponse> GetCatalogAsync(
        string source, string sourceId,
        string? type = null, string? sort = null, string? order = null,
        string? genre = null, string? studio = null, string? contentRating = null,
        string? q = null, int limit = 20, int offset = 0,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/catalog?source={Uri.EscapeDataString(source)}&source_id={Uri.EscapeDataString(sourceId)}&limit={limit}&offset={offset}";
        if (type != null) query += $"&type={Uri.EscapeDataString(type)}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(sort)}";
        if (order != null) query += $"&order={Uri.EscapeDataString(order)}";
        if (genre != null) query += $"&genre={Uri.EscapeDataString(genre)}";
        if (studio != null) query += $"&studio={Uri.EscapeDataString(studio)}";
        if (contentRating != null) query += $"&content_rating={Uri.EscapeDataString(contentRating)}";
        if (q != null) query += $"&q={Uri.EscapeDataString(q)}";
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    public Task<CatalogFiltersResponse> GetFiltersAsync(string source, string sourceId, CancellationToken ct = default)
        => client.GetAsync<CatalogFiltersResponse>($"/api/v1/catalog/filters?source={Uri.EscapeDataString(source)}&source_id={Uri.EscapeDataString(sourceId)}", ct);

    public Task<CatalogResponse> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
        => GetCatalogAsync("search", "", q: query, limit: limit, ct: ct);

    public Task<MediaItem> GetItemDetailAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<MediaItem>($"/api/v1/catalog/items/{contentId}", ct);
}
