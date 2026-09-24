using SiloPlayer.Core.Models.MediaMaintenance;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Permission-gated media maintenance available from an item's detail page.
/// This intentionally exposes no server-administration surface.
/// </summary>
public sealed class MediaMaintenanceApi(SiloApiClient client)
{
    public Task<ItemMatchSearchResponse> SearchMatchesAsync(
        string itemId,
        ItemMatchSearchRequest request,
        CancellationToken ct = default)
    {
        EnsureSecureBaseUrl();
        return client.PostAsync<ItemMatchSearchResponse>(
            $"/api/v2/admin/items/{Uri.EscapeDataString(itemId)}/match/search",
            V2Json.Body(request),
            ct);
    }

    public Task ApplyMatchAsync(
        string itemId,
        ItemMatchApplyRequest request,
        CancellationToken ct = default)
    {
        EnsureSecureBaseUrl();
        return client.PostNoContentAsync(
            $"/api/v2/admin/items/{Uri.EscapeDataString(itemId)}/match/apply",
            V2Json.Body(request),
            ct);
    }

    public Task<MetadataRefreshReceipt> RefreshMetadataAsync(
        string itemId,
        string mode = "quick",
        CancellationToken ct = default)
    {
        if (mode is not ("quick" or "complete"))
            throw new ArgumentOutOfRangeException(nameof(mode));

        EnsureSecureBaseUrl();
        return client.PostAsync<MetadataRefreshReceipt>(
            $"/api/v2/admin/items/{Uri.EscapeDataString(itemId)}/refresh-metadata",
            new Dictionary<string, object?> { ["mode"] = mode },
            ct);
    }

    private void EnsureSecureBaseUrl()
    {
        if (!Uri.TryCreate(client.BaseUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Media maintenance requires an HTTPS Silo server connection.");
        }
    }
}
