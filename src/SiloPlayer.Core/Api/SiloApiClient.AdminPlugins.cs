using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    // Single-send transport. Even naturally idempotent plugin writes deliberately disable auth replay.
    internal async Task<ApiResponse<T>> AdminPluginSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body = null, string? etag = null, HttpStatusCode expected = HttpStatusCode.OK,
        HttpContent? content = null, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        if (etag != null) AdminValidators.RequireStrong(etag);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        deadline.CancelAfter(method == HttpMethod.Get ? TimeSpan.FromSeconds(45) : TimeSpan.FromMinutes(3));
        using var request = CreateContextBoundRequest(lease.Context, method, path, body,
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        if (content != null) request.Content = content;
        using var response = await SendWithRetryAsync(request, deadline.Token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (response.IsSuccessStatusCode && response.StatusCode != expected) throw new InvalidDataException("Unexpected plugin administration receipt; reload to reconcile the outcome.");
        if (response.StatusCode == HttpStatusCode.NoContent && expected == HttpStatusCode.NoContent) return new(default!, null);
        const int limit = AdminPluginsApi.MaxResponseBytes;
        if (response.Content.Headers.ContentLength is > limit) throw new InvalidDataException("Plugin response exceeds its size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await source.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (count == 0) break;
            if (bytes.Length + count > limit) throw new InvalidDataException("Plugin response exceeds its size limit.");
            bytes.Write(buffer, 0, count);
        }
        if (!response.IsSuccessStatusCode)
        {
            response.Content.Dispose(); response.Content = new ByteArrayContent(bytes.ToArray());
            await ThrowApiException(response, request, deadline.Token).ConfigureAwait(false);
        }
        var value = JsonSerializer.Deserialize<T>(bytes.ToArray(), AdminPluginsApi.WireOptions) ?? throw new InvalidDataException("Empty plugin response.");
        lease.ThrowIfRetired();
        return new(value, response.Headers.ETag?.ToString());
    }
}
