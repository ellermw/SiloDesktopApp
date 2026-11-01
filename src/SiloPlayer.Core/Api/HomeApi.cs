using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Api;

public class HomeApi(SiloApiClient client)
{
    private readonly SemaphoreSlim _capabilityGate = new(1);
    private ApiRequestContext? _capabilityContext;
    private HomeCapabilities? _capabilities;

    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public ApiIdentityContext CaptureIdentityContext() => client.CaptureIdentityContext();
    public bool IsCurrentIdentityContext(ApiIdentityContext context) => client.IsCurrentIdentityContext(context);
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public Task<HomeLayoutResponse> GetLayoutAsync(CancellationToken ct = default)
        => client.GetAsync<HomeLayoutResponse>("/api/v2/home/layout", ct);

    public Task<HomeSectionsResponse> GetSectionsAsync(CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>("/api/v2/home/sections", ct);

    public Task<SeasonalThemeCatalog> GetSeasonalThemesAsync(CancellationToken ct = default)
        => client.GetAsync<SeasonalThemeCatalog>("/api/v2/sections/seasonal-themes", ct);

    /// <summary>
    /// F12: fetch items for a single section. Used by the layout-first
    /// loading pipeline to batch-fetch sections with a concurrency cap
    /// after the skeleton has rendered from <see cref="GetLayoutAsync"/>.
    /// </summary>
    public async Task<HomeSectionItemsResponse> GetSectionItemsAsync(string sectionId, CancellationToken ct = default)
        => new() { Section = await client.GetAsync<HomeSectionWithItems>($"/api/v2/home/sections/{Uri.EscapeDataString(sectionId)}/items", ct) };

    /// <summary>Read the first two screens only when this server advertises the row contract.</summary>
    public async Task<HomeSectionItemsResponse> GetInitialSectionItemsAsync(string sectionId, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var language = client.LocalizedReads.Generation;
        var capabilities = await GetCapabilitiesAsync(ct).ConfigureAwait(false);
        client.LocalizedReads.ThrowIfChanged(language);
        var path = $"/api/v2/home/sections/{Uri.EscapeDataString(sectionId)}/items";
        var bounded = capabilities.RowItemLimitMax >= Services.HomeRowWindow.InitialItemLimit;
        if (bounded) path += $"?item_limit={Services.HomeRowWindow.InitialItemLimit}";
        var section = await client.SendRequestAsync<HomeSectionWithItems>(context, HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        client.LocalizedReads.ThrowIfChanged(language);
        if (!bounded) section.HasMore = false;
        return new() { Section = section };
    }

    public async Task<HomeCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        await _capabilityGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Home owner changed.", ct);
            if (_capabilityContext == context && _capabilities != null) return _capabilities;
            HomeCapabilities capabilities;
            try
            {
                capabilities = await client.SendRequestAsync<HomeCapabilities>(context, HttpMethod.Get, "/api/v2/home/capabilities", null, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (ApiException ex) when (client.IsCurrentContext(context) && ex.StatusCode is 404 or 405 or 501)
            {
                _capabilityContext = context;
                return _capabilities = new();
            }
            catch (Exception) when (client.IsCurrentContext(context))
            {
                // The optimization is optional. Older/unavailable capability endpoints
                // must never cause an unadvertised item_limit to be sent.
                return new();
            }
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Home owner changed.", ct);
            _capabilityContext = context;
            return _capabilities = capabilities;
        }
        finally { _capabilityGate.Release(); }
    }

    // ===== Dismissals =====

    public Task DismissItemAsync(string surface, string itemId, object body, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/home/dismissals/{Uri.EscapeDataString(surface)}/{Uri.EscapeDataString(itemId)}", body, ct);

    public Task UndoDismissalAsync(string surface, string itemId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/home/dismissals/{Uri.EscapeDataString(surface)}/{Uri.EscapeDataString(itemId)}", ct);
}

public sealed class HomeCapabilities
{
    public int? RowItemLimitMax { get; set; }
    public int? SectionItemLimitMax { get; set; }
}

/// <summary>
/// Desktop wrapper for the direct section resource returned by
/// <c>GET /api/v2/home/sections/{id}/items</c>.
/// </summary>
public class HomeSectionItemsResponse
{
    public HomeSectionWithItems? Section { get; set; }
}
