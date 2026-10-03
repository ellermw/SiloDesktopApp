using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Api;

public class HomeApi(SiloApiClient client)
{
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public Task<HomeLayoutResponse> GetLayoutAsync(CancellationToken ct = default)
        => client.GetAsync<HomeLayoutResponse>("/api/v2/home/layout", ct);

    public Task<HomeSectionsResponse> GetSectionsAsync(CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>("/api/v2/home/sections", ct);

    /// <summary>
    /// F12: fetch items for a single section. Used by the layout-first
    /// loading pipeline to batch-fetch sections with a concurrency cap
    /// after the skeleton has rendered from <see cref="GetLayoutAsync"/>.
    /// </summary>
    public async Task<HomeSectionItemsResponse> GetSectionItemsAsync(string sectionId, CancellationToken ct = default)
        => new() { Section = await client.GetAsync<HomeSectionWithItems>($"/api/v2/home/sections/{Uri.EscapeDataString(sectionId)}/items", ct) };

    // ===== Dismissals =====

    public Task DismissItemAsync(string surface, string itemId, object body, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/home/dismissals/{Uri.EscapeDataString(surface)}/{Uri.EscapeDataString(itemId)}", body, ct);

    public Task UndoDismissalAsync(string surface, string itemId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/home/dismissals/{Uri.EscapeDataString(surface)}/{Uri.EscapeDataString(itemId)}", ct);
}

/// <summary>
/// Desktop wrapper for the direct section resource returned by
/// <c>GET /api/v2/home/sections/{id}/items</c>.
/// </summary>
public class HomeSectionItemsResponse
{
    public HomeSectionWithItems? Section { get; set; }
}
