using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    internal Task<byte[]> AdminCollectionImageAsync(MonitorAuthorityLease lease, string url, CancellationToken ct) =>
        AdminLibraryWithinDeadlineAsync(lease, ct, async token =>
        {
            var origin = new Uri(lease.Context.BaseUrl.TrimEnd('/') + "/");
            if (!Uri.TryCreate(origin, url, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
                throw new InvalidDataException("Invalid collection artwork.");
            var sameOrigin = uri.Scheme == origin.Scheme && uri.Authority.Equals(origin.Authority, StringComparison.OrdinalIgnoreCase);
            // Public artwork may be a signed asset-storage URL. It gets no Silo credentials or diagnostic URL logging.
            using var request = sameOrigin ? CreateContextBoundRequest(lease.Context, HttpMethod.Get, "", null) : new HttpRequestMessage(HttpMethod.Get, uri);
            request.RequestUri = uri;
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode) throw new InvalidDataException("Collection artwork is unavailable.");
            return await AdminLibraryReadBoundedAsync(response.Content, lease, 2 * 1024 * 1024, token).ConfigureAwait(false);
        });
    // Share the existing bounded admin transport/deadline, but retain response validators.
    // Writes never refresh authentication or replay after an uncertain response.
    internal Task<AdminCollectionSnapshot> AdminCollectionSendAsync(MonitorAuthorityLease lease, HttpMethod method,
        string path, JsonObject? body = null, string? etag = null, CancellationToken ct = default,
        HttpContent? form = null) => AdminLibraryWithinDeadlineAsync(lease, ct, async token =>
    {
        lease.ThrowIfRetired();
        using var request = CreateContextBoundRequest(lease.Context, method, path, body);
        if (etag != null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (form != null) request.Content = form;
        using var response = await SendWithRetryAsync(request, token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
            await AdminLibraryThrowBoundedErrorAsync(response, request, lease, token).ConfigureAwait(false);
        var value = new JsonObject();
        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            var bytes = await AdminLibraryReadBoundedAsync(response.Content, lease, AdminLibraryJsonLimit, token).ConfigureAwait(false);
            value = JsonNode.Parse(bytes, documentOptions: new() { MaxDepth = 64 }) as JsonObject
                ?? throw new InvalidDataException("Empty collection administration response.");
        }
        lease.ThrowIfRetired();
        return new AdminCollectionSnapshot(value, response.Headers.ETag?.ToString() ?? "", lease, path);
    });
}
