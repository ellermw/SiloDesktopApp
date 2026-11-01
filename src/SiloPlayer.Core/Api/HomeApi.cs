using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Api;

public class HomeApi(SiloApiClient client)
{
    private static readonly TimeSpan CapabilityProbeTimeout = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _capabilityGate = new(1);
    private ApiRequestContext? _capabilityContext;
    private HomeCapabilities? _capabilities;
    private bool _capabilitiesTransient;
    private long _layoutGeneration;
    private long _capabilityLayoutGeneration;

    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public ApiIdentityContext CaptureIdentityContext() => client.CaptureIdentityContext();
    public bool IsCurrentIdentityContext(ApiIdentityContext context) => client.IsCurrentIdentityContext(context);
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public long LanguageGeneration => client.LocalizedReads.Generation;
    public bool IsCurrentReadContext(ApiRequestContext context, long language)
        => client.IsCurrentContext(context) && LanguageGeneration == language;

    private void RequireCurrentRead(ApiRequestContext context, long language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Home owner changed.", ct);
        client.LocalizedReads.ThrowIfChanged(language);
    }
    public Task<HomeLayoutResponse> GetLayoutAsync(CancellationToken ct = default)
    {
        // Share optional discovery failures across this layout's initial and
        // queued rows. A later layout refresh can discover recovered support.
        Interlocked.Increment(ref _layoutGeneration);
        return client.GetAsync<HomeLayoutResponse>("/api/v2/home/layout", ct);
    }

    public Task<HomeSectionsResponse> GetSectionsAsync(CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>("/api/v2/home/sections", ct);

    public Task<SeasonalThemeCatalog> GetSeasonalThemesAsync(CancellationToken ct = default)
        => client.GetAsync<SeasonalThemeCatalog>("/api/v2/sections/seasonal-themes", ct);

    /// <summary>
    /// F12: fetch items for a single section. Used by the layout-first
    /// loading pipeline to batch-fetch sections with a concurrency cap
    /// after the skeleton has rendered from <see cref="GetLayoutAsync"/>.
    /// </summary>
    public Task<HomeSectionItemsResponse> GetSectionItemsAsync(string sectionId, CancellationToken ct = default)
        => GetSectionItemsAsync(client.CaptureContext(), LanguageGeneration, sectionId, ct);

    public Task<HomeSectionItemsResponse> GetSectionItemsAsync(ApiRequestContext owner, long language, string sectionId, CancellationToken ct = default)
        => ReadSectionItemsAsync(owner, language, sectionId, null, ct);

    /// <summary>Completion and expanded non-featured refreshes request the negotiated whole-row budget; featured reads stay uncapped.</summary>
    public async Task<HomeSectionItemsResponse> GetWholeSectionItemsAsync(ApiRequestContext owner, long language,
        string sectionId, int itemLimit, CancellationToken ct = default)
    {
        var capabilities = await GetCapabilitiesAsync(owner, language, ct).ConfigureAwait(false);
        // The recipe's display itemLimit must not cap completion at the
        // initial window. Stay within both advertised query ceilings.
        var limit = capabilities.SectionItemLimitMax is > 0
            ? BoundedItemLimit(capabilities, capabilities.SectionItemLimitMax.Value) : null;
        return await ReadSectionItemsAsync(owner, language, sectionId, limit, ct).ConfigureAwait(false);
    }

    private static int? BoundedItemLimit(HomeCapabilities capabilities, int desired)
        => capabilities.RowItemLimitMax is > 0
            ? Math.Min(desired, Math.Min(capabilities.RowItemLimitMax.Value,
                capabilities.SectionItemLimitMax is > 0 ? capabilities.SectionItemLimitMax.Value : int.MaxValue))
            : null;

    private async Task<HomeSectionItemsResponse> ReadSectionItemsAsync(ApiRequestContext owner, long language,
        string sectionId, int? itemLimit, CancellationToken ct)
    {
        RequireCurrentRead(owner, language, ct);
        var path = $"/api/v2/home/sections/{Uri.EscapeDataString(sectionId)}/items";
        if (itemLimit is > 0) path += $"?item_limit={itemLimit.Value}";
        var section = await client.SendRequestAsync<HomeSectionWithItems>(owner, HttpMethod.Get,
            path, null, ct).ConfigureAwait(false);
        RequireCurrentRead(owner, language, ct);
        if (section != null)
        {
            section.ReadLanguageGeneration = language;
            section.HasReadWhole = !section.HasMore;
        }
        return new() { Section = section };
    }

    /// <summary>Read the first two screens only when this server advertises the row contract.</summary>
    public Task<HomeSectionItemsResponse> GetInitialSectionItemsAsync(string sectionId, CancellationToken ct = default)
        => GetInitialSectionItemsAsync(client.CaptureContext(), LanguageGeneration, sectionId, ct);

    public async Task<HomeSectionItemsResponse> GetInitialSectionItemsAsync(ApiRequestContext context, long language, string sectionId, CancellationToken ct = default)
    {
        RequireCurrentRead(context, language, ct);
        var capabilities = await GetCapabilitiesAsync(context, language, ct).ConfigureAwait(false);
        var bounded = capabilities.RowItemLimitMax >= Services.HomeRowWindow.InitialItemLimit;
        return await ReadSectionItemsAsync(context, language, sectionId,
            bounded ? BoundedItemLimit(capabilities, Services.HomeRowWindow.InitialItemLimit) : null, ct).ConfigureAwait(false);
    }

    public Task<HomeCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
        => GetCapabilitiesAsync(client.CaptureContext(), LanguageGeneration, ct);

    private async Task<HomeCapabilities> GetCapabilitiesAsync(ApiRequestContext context, long language, CancellationToken ct)
    {
        RequireCurrentRead(context, language, ct);
        await _capabilityGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RequireCurrentRead(context, language, ct);
            var layoutGeneration = Volatile.Read(ref _layoutGeneration);
            if (_capabilityContext == context && _capabilities != null
                && (!_capabilitiesTransient || _capabilityLayoutGeneration == layoutGeneration)) return _capabilities;
            HomeCapabilities? capabilities;
            var transient = false;
            using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCancellation.CancelAfter(CapabilityProbeTimeout);
            try
            {
                capabilities = await client.SendRequestAsync<HomeCapabilities>(context, HttpMethod.Get, "/api/v2/home/capabilities", null, probeCancellation.Token).ConfigureAwait(false);
                transient = capabilities == null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && IsCurrentReadContext(context, language))
            {
                // The dedicated probe deadline (or a shorter HttpClient timeout)
                // must not cancel the row consumer. Caller/owner/language
                // cancellation still propagates instead of becoming fallback.
                capabilities = new();
                transient = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (ApiException ex) when (IsCurrentReadContext(context, language) && ex.StatusCode is 404 or 405 or 501)
            {
                capabilities = new();
            }
            catch (Exception) when (IsCurrentReadContext(context, language))
            {
                // The optimization is optional. Older/unavailable capability endpoints
                // must never cause an unadvertised item_limit to be sent.
                capabilities = new();
                transient = true;
            }
            RequireCurrentRead(context, language, ct);
            _capabilityContext = context;
            _capabilitiesTransient = transient;
            _capabilityLayoutGeneration = layoutGeneration;
            return _capabilities = capabilities ?? new();
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
