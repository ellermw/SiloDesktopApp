using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    internal async Task<AdminUsersDocument> AdminUsersSendAsync(MonitorAuthorityLease lease, HttpMethod method, string path,
        JsonObject? body = null, string? etag = null, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        if (!path.StartsWith("/api/v2/", StringComparison.Ordinal) || path.Contains('#')) throw new ArgumentException("Invalid U1 resource.");
        if (etag != null) AdminValidators.RequireStrong(etag);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(TimeSpan.FromSeconds(30));
        using var payload = body == null ? null : JsonDocument.Parse(body.ToJsonString());
        using var request = CreateContextBoundRequest(lease.Context, method, path, payload?.RootElement,
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        using var content = response.Content;
        lease.ThrowIfRetired();
        linked.Token.ThrowIfCancellationRequested();
        lease.ThrowIfRetired();
        const int maximum = 4 * 1024 * 1024;
        if (content.Headers.ContentLength is > maximum) throw new InvalidDataException("U1 response exceeded its byte bound.");
        await using var stream = await content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            linked.Token.ThrowIfCancellationRequested();
            if (count == 0) break;
            if (bytes.Length + count > maximum) throw new InvalidDataException("U1 response exceeded its byte bound.");
            bytes.Write(buffer, 0, count);
        }
        lease.ThrowIfRetired();
        var responseBytes = bytes.ToArray();
        if (!response.IsSuccessStatusCode)
        {
            var bounded = new ByteArrayContent(responseBytes);
            foreach (var header in content.Headers) bounded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = bounded;
            await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
        }
        var value = bytes.Length == 0 ? null : JsonNode.Parse(responseBytes) as JsonObject ?? throw new InvalidDataException("Expected a U1 object response.");
        return new(value, response.Headers.ETag?.ToString());
    }
}
