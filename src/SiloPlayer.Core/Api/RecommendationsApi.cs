using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Api;

public class RecommendationsApi(SiloApiClient client)
{
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public async Task<RecommendationsResponse> GetForYouMainAsync(CancellationToken ct = default)
        => new() { Rows = [await client.GetAsync<RecommendationRow>("/api/v2/recommendations/for-you/main", ct)] };

    public async Task<RecommendationsResponse> GetForYouRowsAsync(CancellationToken ct = default)
        => new() { Rows = (await client.GetAsync<BrowseCollection<RecommendationRow>>("/api/v2/recommendations/for-you/rows", ct)).Items };

    public Task<SimilarResponse> GetSimilarAsync(string itemId, CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>($"/api/v2/recommendations/similar/{Uri.EscapeDataString(itemId)}", ct);

    public Task<TasteProfileResponse> GetTasteProfileAsync(CancellationToken ct = default)
        => client.GetAsync<TasteProfileResponse>("/api/v2/recommendations/taste-profile", ct);

    public async Task<DiscoverResponse> GetDiscoverAsync(CancellationToken ct = default)
        => new() { Rows = (await client.GetAsync<BrowseCollection<DiscoverRow>>("/api/v2/recommendations/discover", ct)).Items };

    public Task<RecommendationSectionResponse> GetSectionAsync(string kind, string? key = null, CancellationToken ct = default)
    {
        var path = $"/api/v2/recommendations/section/{Uri.EscapeDataString(kind)}";
        if (!string.IsNullOrWhiteSpace(key))
            path += $"?key={Uri.EscapeDataString(key)}";
        return client.GetAsync<RecommendationSectionResponse>(path, ct);
    }

    public async Task<TasteSeedItemsPage> GetTasteSeedItemsAsync(int limit = 30, int offset = 0,
        CancellationToken ct = default)
    {
        var page = await BrowseV2.WindowAsync<MediaItem>(client, "/api/v2/recommendations/taste-seed/items", Math.Clamp(limit, 1, 60), offset, ct);
        return new() { Items = page.Items, NextOffset = page.Page?.HasMore == true ? Math.Max(0, offset) + page.Items.Count : null };
    }

    public Task<TasteSeedSubmitResponse> SubmitTasteSeedAsync(IEnumerable<string> itemIds,
        CancellationToken ct = default)
        => client.PostAsync<TasteSeedSubmitResponse>("/api/v2/recommendations/taste-seed",
            new Dictionary<string, object?> { ["item_ids"] = itemIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray() },
            ct);

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
        var query = new List<string> { $"mode={Uri.EscapeDataString(mode)}", $"limit={Math.Clamp(limit, 1, 20)}" };
        if (genres != null)
            foreach (var g in genres.OrderBy(x => x, StringComparer.Ordinal))
                query.Add($"genres={Uri.EscapeDataString(g)}");
        if (excludeIds != null)
            foreach (var id in excludeIds)
                query.Add($"exclude_ids={Uri.EscapeDataString(id)}");
        return client.GetAsync<SwipeCardsPage>(
            $"/api/v2/recommendations/watch-tonight/cards?{string.Join("&", query)}", ct);
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
