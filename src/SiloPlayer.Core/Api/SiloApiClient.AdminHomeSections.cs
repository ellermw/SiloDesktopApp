using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    /// <summary>Current-admin transport; success and problem bodies are both bounded to 4 MiB. Writes have one attempt.</summary>
    internal async Task<ApiResponse<T>> AdminHomeSectionsSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body, string? etag, CancellationToken ct)
    {
        lease.ThrowIfRetired(); if (etag != null) AdminValidators.RequireStrong(etag);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, method, path, body,
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode) await AdminLibraryThrowBoundedErrorAsync(response, request, lease, linked.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK && (method == HttpMethod.Get || response.StatusCode != HttpStatusCode.Created))
            throw new InvalidDataException("The server did not confirm the section operation.");
        var bytes = await AdminSubtitleReadBoundedAsync(response.Content, lease, 4 * 1024 * 1024, linked.Token).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty section response.");
        lease.ThrowIfRetired(); return new(value, response.Headers.ETag?.ToString());
    }

    internal async Task AdminHomeSectionsDeleteAsync(MonitorAuthorityLease lease, string path, string etag, CancellationToken ct)
    {
        lease.ThrowIfRetired(); AdminValidators.RequireStrong(etag);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Delete, path, null,
            new Dictionary<string, string> { ["If-Match"] = etag });
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode) await AdminLibraryThrowBoundedErrorAsync(response, request, lease, linked.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NoContent) throw new InvalidDataException("The server did not confirm section deletion.");
        lease.ThrowIfRetired();
    }
}
