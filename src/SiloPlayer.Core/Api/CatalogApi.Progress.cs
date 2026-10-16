namespace SiloPlayer.Core.Api;

public sealed class ProgressCapabilities
{
    public bool ClearResumePosition { get; set; }
}

public partial class CatalogApi
{
    public async Task<ProgressCapabilities> GetProgressCapabilitiesAsync(CancellationToken ct = default)
    {
        try { return await client.GetAsync<ProgressCapabilities>("/api/v2/progress/capabilities", ct); }
        catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501) { return new(); }
    }

    // Non-retryable: replay could erase progress recorded after the first attempt.
    public Task ClearResumePositionAsync(string contentId, CancellationToken ct = default)
        => client.SendNoContentRequestWithoutRefreshAsync(client.CaptureContext(), HttpMethod.Delete,
            $"/api/v2/progress/{Uri.EscapeDataString(contentId)}/position", null, ct);
}
