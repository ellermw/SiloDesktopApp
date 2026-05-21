using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.Core.Api;

public class RecommendationsApi(ContinuumApiClient client)
{
    public Task<RecommendationsResponse> GetForYouMainAsync(CancellationToken ct = default)
        => client.GetAsync<RecommendationsResponse>("/api/v1/recommendations/for-you/main", ct);

    public Task<RecommendationsResponse> GetForYouRowsAsync(CancellationToken ct = default)
        => client.GetAsync<RecommendationsResponse>("/api/v1/recommendations/for-you/rows", ct);

    public Task<SimilarResponse> GetBecauseWatchedAsync(string itemId, CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>($"/api/v1/recommendations/because-watched/{Uri.EscapeDataString(itemId)}", ct);

    public Task<SimilarResponse> GetSimilarAsync(string itemId, CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>($"/api/v1/recommendations/similar/{Uri.EscapeDataString(itemId)}", ct);

    public Task<SimilarResponse> GetSimilarUsersAsync(CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>("/api/v1/recommendations/similar-users", ct);

    public Task<TasteProfileResponse> GetTasteProfileAsync(CancellationToken ct = default)
        => client.GetAsync<TasteProfileResponse>("/api/v1/recommendations/taste-profile", ct);

    public Task<DiscoverResponse> GetDiscoverAsync(CancellationToken ct = default)
        => client.GetAsync<DiscoverResponse>("/api/v1/recommendations/discover", ct);

    public Task<RecommendationSectionResponse> GetSectionAsync(string kind, string? key = null, CancellationToken ct = default)
    {
        var path = $"/api/v1/recommendations/section/{Uri.EscapeDataString(kind)}";
        if (!string.IsNullOrWhiteSpace(key))
            path += $"/{Uri.EscapeDataString(key)}";
        return client.GetAsync<RecommendationSectionResponse>(path, ct);
    }

    public Task<SimilarResponse> GetPopularAsync(int? days = null, CancellationToken ct = default)
    {
        var path = "/api/v1/recommendations/popular";
        if (days.HasValue) path += $"?days={days.Value}";
        return client.GetAsync<SimilarResponse>(path, ct);
    }

    public Task<SimilarResponse> GetRecentlyAddedAsync(CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>("/api/v1/recommendations/recently-added", ct);

    /// <summary>
    /// Fetch the "Watch Tonight" quick-picks list — a short mixed set of
    /// continue_watching / next_up / recommendation items.
    /// </summary>
    public Task<WatchTonightResponse> GetWatchTonightAsync(CancellationToken ct = default)
        => client.GetAsync<WatchTonightResponse>("/api/v1/recommendations/watch-tonight", ct);

    /// <summary>
    /// Fetch the swipe-deck "Watch Tonight" cards. Supports "continue" (pick up
    /// where you left off) and "discover" (find something new) modes, with
    /// optional genre filtering and an exclude list for infinite paging.
    /// </summary>
    public Task<SwipeCardsPage> GetWatchTonightCardsAsync(
        string mode,
        IReadOnlyList<string>? genres = null,
        IReadOnlyList<string>? excludeIds = null,
        int limit = 12,
        CancellationToken ct = default)
    {
        var query = new List<string> { $"mode={Uri.EscapeDataString(mode)}", $"limit={limit}" };
        if (genres != null)
            foreach (var g in genres.OrderBy(x => x, StringComparer.Ordinal))
                query.Add($"genres[]={Uri.EscapeDataString(g)}");
        if (excludeIds != null)
            foreach (var id in excludeIds)
                query.Add($"exclude_ids[]={Uri.EscapeDataString(id)}");
        return client.GetAsync<SwipeCardsPage>(
            $"/api/v1/recommendations/watch-tonight/cards?{string.Join("&", query)}", ct);
    }
}
