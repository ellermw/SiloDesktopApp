using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    private static readonly HttpRequestOptionsKey<bool> AdminRequestsSensitiveOption = new("SiloPlayer.AdminRequestsSensitive");
    /// <summary>Requests administration uses one attempt for every mutation, including probes. No credential appears in diagnostics.</summary>
    internal async Task<ApiResponse<JsonObject>> AdminRequestsSendAsync(MonitorAuthorityLease owner, HttpMethod method,
        string path, JsonObject? body = null, string? etag = null, CancellationToken ct = default)
    {
        owner.ThrowIfRetired();
        if (etag != null) AdminValidators.RequireStrong(etag);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, ct);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = CreateContextBoundRequest(owner.Context, method, path, body == null ? null : System.Text.Json.JsonSerializer.SerializeToElement(body),
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        request.Options.Set(AdminRequestsSensitiveOption, true);
        using var response = await SendWithRetryAsync(request, cancellation.Token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        owner.ThrowIfRetired();
        var bytes = await AdminRequestsReadBoundedAsync(response.Content, owner, cancellation.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var originalContent = response.Content;
            var mediaType = originalContent.Headers.ContentType;
            response.Content = new ByteArrayContent(bytes);
            originalContent.Dispose();
            response.Content.Headers.ContentType = mediaType;
            await ThrowApiException(response, request, cancellation.Token).ConfigureAwait(false);
            owner.ThrowIfRetired();
        }
        if (method == HttpMethod.Delete)
        {
            if (response.StatusCode != HttpStatusCode.NoContent) throw new InvalidDataException("Deletion was not confirmed. Reload before another attempt.");
            return new(new JsonObject(), null);
        }
        owner.ThrowIfRetired();
        var value = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Invalid request administration response.");
        return new(value, response.Headers.ETag?.ToString());
    }
    private static async Task<byte[]> AdminRequestsReadBoundedAsync(HttpContent content, MonitorAuthorityLease owner, CancellationToken ct)
    {
        const int maximum = 4 * 1024 * 1024;
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("Request administration response is too large.");
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            owner.ThrowIfRetired();
            if (count == 0) break;
            if (bytes.Length + count > maximum) throw new InvalidDataException("Request administration response is too large.");
            bytes.Write(buffer, 0, count);
        }
        owner.ThrowIfRetired();
        return bytes.ToArray();
    }
}
