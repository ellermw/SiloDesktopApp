using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Api;

public class HomeApi(ContinuumApiClient client)
{
    public Task<HomeLayoutResponse> GetLayoutAsync(CancellationToken ct = default)
        => client.GetAsync<HomeLayoutResponse>("/api/v1/home/layout", ct);

    public Task<HomeSectionsResponse> GetSectionsAsync(CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>("/api/v1/home/sections", ct);

    // ===== Dismissals =====

    public Task DismissItemAsync(string surface, string itemId, object body, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/home/dismissals/{Uri.EscapeDataString(surface)}/{Uri.EscapeDataString(itemId)}", body, ct);

    public Task UndoDismissalAsync(string surface, string itemId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/home/dismissals/{Uri.EscapeDataString(surface)}/{Uri.EscapeDataString(itemId)}", ct);
}
