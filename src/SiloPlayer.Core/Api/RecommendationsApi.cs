using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Api;

public class RecommendationsApi(SiloApiClient client)
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

    public Task<TasteSeedItemsPage> GetTasteSeedItemsAsync(int limit = 30, int offset = 0,
        CancellationToken ct = default)
        => client.GetAsync<TasteSeedItemsPage>(
            $"/api/v1/recommendations/taste-seed/items?limit={Math.Clamp(limit, 1, 60)}&offset={Math.Max(0, offset)}",
            ct);

    public Task<TasteSeedSubmitResponse> SubmitTasteSeedAsync(IEnumerable<string> itemIds,
        CancellationToken ct = default)
        => client.PostAsync<TasteSeedSubmitResponse>("/api/v1/recommendations/taste-seed",
            new Dictionary<string, object?> { ["item_ids"] = itemIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray() },
            ct);

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

public sealed class TasteSeedItemsPage
{
    public List<MediaItem> Items { get; set; } = [];
    public int? NextOffset { get; set; }
}

public sealed class TasteSeedSubmitResponse
{
    public int Added { get; set; }
}
