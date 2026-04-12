using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Api;

public class HomeApi(ContinuumApiClient client)
{
    public Task<HomeLayoutResponse> GetLayoutAsync(CancellationToken ct = default)
        => client.GetAsync<HomeLayoutResponse>("/api/v1/home/layout", ct);

    public Task<HomeSectionsResponse> GetSectionsAsync(CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>("/api/v1/home/sections", ct);

    /// <summary>
    /// F12: fetch items for a single section. Used by the layout-first
    /// loading pipeline to batch-fetch sections with a concurrency cap
    /// after the skeleton has rendered from <see cref="GetLayoutAsync"/>.
    /// </summary>
    public Task<HomeSectionItemsResponse> GetSectionItemsAsync(string sectionId, CancellationToken ct = default)
        => client.GetAsync<HomeSectionItemsResponse>($"/api/v1/home/sections/{Uri.EscapeDataString(sectionId)}/items", ct);

    // ===== Dismissals =====

    public Task DismissItemAsync(string surface, string itemId, object body, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/home/dismissals/{Uri.EscapeDataString(surface)}/{Uri.EscapeDataString(itemId)}", body, ct);

    public Task UndoDismissalAsync(string surface, string itemId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/home/dismissals/{Uri.EscapeDataString(surface)}/{Uri.EscapeDataString(itemId)}", ct);
}

/// <summary>
/// Wrapper for <c>GET /api/v1/home/sections/{id}/items</c>. The server
/// returns the populated section under a single <c>section</c> key.
/// </summary>
public class HomeSectionItemsResponse
{
    public HomeSectionWithItems? Section { get; set; }
}
