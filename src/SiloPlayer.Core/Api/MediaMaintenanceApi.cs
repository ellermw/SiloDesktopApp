using SiloPlayer.Core.Models.MediaMaintenance;
using System.Text.Json;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Permission-gated media maintenance available from an item's detail page.
/// This intentionally exposes no server-administration surface.
/// </summary>
public sealed class MediaMaintenanceApi(SiloApiClient client)
{
    public Task<SiloPlayer.Core.Models.Catalog.MediaItemDetail> UpdateMetadataAsync(string itemId,
        IReadOnlyDictionary<string, object?> changes, CancellationToken ct = default)
    {
        EnsureSecureBaseUrl();
        return client.PatchAsync<SiloPlayer.Core.Models.Catalog.MediaItemDetail>(
            $"/api/v2/admin/items/{Uri.EscapeDataString(itemId)}/metadata", changes, ct);
    }

    public Task<JsonElement> GetMetadataAiCapabilityAsync(CancellationToken ct = default)
        => client.GetAsync<JsonElement>("/api/v2/capabilities/metadata-ai", ct);
    public Task<JsonElement> TranslateMetadataAsync(string id, string language, bool children, bool force, CancellationToken ct = default)
        => client.PostAsync<JsonElement>($"/api/v2/admin/items/{Uri.EscapeDataString(id)}/metadata-translation",
            new { target_language = language, include_children = children, force }, ct);
    public Task<JsonElement> GetTranslationJobsAsync(string id, CancellationToken ct = default)
        => client.GetAsync<JsonElement>($"/api/v2/admin/items/{Uri.EscapeDataString(id)}/metadata-translation/jobs", ct);
    public Task<JsonElement> ApplyImageAsync(string id, string url, string type, string provider, CancellationToken ct = default)
        => client.PostAsync<JsonElement>($"/api/v2/admin/items/{Uri.EscapeDataString(id)}/images/apply",
            new { original_url = url, type, provider_id = provider }, ct);
    public async Task<(List<JsonElement> Items, JsonElement Current, Dictionary<string, string> Errors)> GetImagesAsync(string id, CancellationToken ct = default)
    {
        var context = client.CaptureContext(); var images = new List<JsonElement>();
        var errors = new Dictionary<string, string>(); JsonElement current = default;
        string? cursor = null; var seen = new HashSet<string>();
        do
        {
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Image request context changed.", ct);
            var page = await client.GetAsync<JsonElement>($"/api/v2/admin/items/{Uri.EscapeDataString(id)}/images?limit=200" +
                (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), ct);
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Image request context changed.", ct);
            images.AddRange(page.GetProperty("items").EnumerateArray().Select(value => value.Clone()));
            current = page.GetProperty("current").Clone();
            if (page.TryGetProperty("provider_errors", out var warnings))
                foreach (var property in warnings.EnumerateObject()) errors[property.Name] = property.Value.GetString() ?? "";
            var pagination = page.GetProperty("page");
            if (!pagination.GetProperty("has_more").GetBoolean()) break;
            cursor = pagination.GetProperty("next_cursor").GetString();
            if (string.IsNullOrEmpty(cursor) || !seen.Add(cursor)) throw new InvalidOperationException("Image choices changed. Reload to try again.");
        } while (true);
        return (images, current, errors);
    }

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
