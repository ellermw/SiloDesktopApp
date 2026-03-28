using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Api;

public class CatalogApi(ContinuumApiClient client)
{
    public Task<List<Library>> GetLibrariesAsync(CancellationToken ct = default)
        => client.GetAsync<List<Library>>("/api/v1/user/libraries", ct);

    public Task<HomeSectionsResponse> GetLibrarySectionsAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>($"/api/v1/library/{libraryId}/sections", ct);

    public Task<CatalogResponse> GetCatalogAsync(
        int libraryId,
        string? sort = null, string? order = null,
        string? genre = null, string? studio = null, string? contentRating = null,
        string? q = null, int limit = 40, int offset = 0,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/catalog?library_id={libraryId}&limit={limit}&offset={offset}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(sort)}";
        if (order != null) query += $"&order={Uri.EscapeDataString(order)}";
        if (genre != null) query += $"&genre={Uri.EscapeDataString(genre)}";
        if (studio != null) query += $"&studio={Uri.EscapeDataString(studio)}";
        if (contentRating != null) query += $"&content_rating={Uri.EscapeDataString(contentRating)}";
        if (q != null) query += $"&q={Uri.EscapeDataString(q)}";
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    public Task<CatalogFiltersResponse> GetFiltersAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<CatalogFiltersResponse>($"/api/v1/catalog/filters?library_id={libraryId}", ct);

    public Task<CatalogResponse> SearchAsync(string query, int limit = 40, CancellationToken ct = default)
    {
        return client.PostAsync<CatalogResponse>("/api/v1/catalog/query", new { q = query, limit }, ct);
    }

    public Task<MediaItemDetail> GetItemDetailAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<MediaItemDetail>($"/api/v1/catalog/items/{contentId}", ct);

    public Task<ItemListResponse> GetFavoritesAsync(CancellationToken ct = default)
        => client.GetAsync<ItemListResponse>("/api/v1/favorites", ct);

    public Task<ItemListResponse> GetWatchlistAsync(CancellationToken ct = default)
        => client.GetAsync<ItemListResponse>("/api/v1/watchlist", ct);

    public Task<ProgressResponse> GetProgressAsync(CancellationToken ct = default)
        => client.GetAsync<ProgressResponse>("/api/v1/progress?status=in_progress&limit=50", ct);

    public Task<RecommendationsResponse> GetRecommendationsAsync(CancellationToken ct = default)
        => client.GetAsync<RecommendationsResponse>("/api/v1/recommendations/for-you/rows", ct);

    public Task AddFavoriteAsync(string contentId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/favorites/{contentId}", null, ct);

    public Task RemoveFavoriteAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/favorites/{contentId}", ct);

    public Task AddToWatchlistAsync(string contentId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/watchlist/{contentId}", null, ct);

    public Task RemoveFromWatchlistAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/watchlist/{contentId}", ct);

    public Task<SeasonsResponse> GetSeasonsAsync(string seriesId, CancellationToken ct = default)
        => client.GetAsync<SeasonsResponse>($"/api/v1/catalog/series/{seriesId}/seasons", ct);

    public Task<EpisodesResponse> GetEpisodesAsync(string seriesId, int seasonNumber, CancellationToken ct = default)
        => client.GetAsync<EpisodesResponse>($"/api/v1/catalog/series/{seriesId}/seasons/{seasonNumber}/episodes", ct);

    // ===== Watched State =====

    public Task MarkWatchedAsync(string contentId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/watched/{contentId}", new { }, ct);

    public Task MarkUnwatchedAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/watched/{contentId}", ct);

    // ===== Ratings =====

    public async Task<int?> GetRatingAsync(string contentId, CancellationToken ct = default)
    {
        try
        {
            var r = await client.GetAsync<RatingResponse>($"/api/v1/ratings/{contentId}", ct);
            return r.Rating;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public Task SetRatingAsync(string contentId, int rating, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/ratings/{contentId}", new { rating }, ct);

    public Task DeleteRatingAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/ratings/{contentId}", ct);

    // ===== Recommendations =====

    public Task<SimilarResponse> GetSimilarAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>($"/api/v1/recommendations/similar/{contentId}", ct);
}
