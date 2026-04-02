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

    public Task<SimilarResponse> GetPopularAsync(int? days = null, CancellationToken ct = default)
    {
        var path = "/api/v1/recommendations/popular";
        if (days.HasValue) path += $"?days={days.Value}";
        return client.GetAsync<SimilarResponse>(path, ct);
    }

    public Task<SimilarResponse> GetRecentlyAddedAsync(CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>("/api/v1/recommendations/recently-added", ct);
}
