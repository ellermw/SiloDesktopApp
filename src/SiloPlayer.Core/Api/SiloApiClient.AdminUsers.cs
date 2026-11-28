using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    internal async Task<AdminUsersDocument> AdminUsersSendAsync(MonitorAuthorityLease lease, HttpMethod method, string path,
        JsonObject? body = null, string? etag = null, CancellationToken ct = default, Action? writeAttempted = null)
    {
        lease.ThrowIfRetired();
        if (!path.StartsWith("/api/v2/", StringComparison.Ordinal) || path.Contains('#')) throw new ArgumentException("Invalid U1 resource.");
        if (etag != null) AdminValidators.RequireStrong(etag);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(TimeSpan.FromSeconds(30));
        using var payload = body == null ? null : JsonDocument.Parse(body.ToJsonString());
        var context = lease.Context;
        using var request = CreateContextBoundRequest(context, method, path, payload?.RootElement,
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        linked.Token.ThrowIfCancellationRequested();
        writeAttempted?.Invoke();
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
        using var content = response.Content;
        var viewAs = method == HttpMethod.Post && path.EndsWith("/impersonate", StringComparison.Ordinal) && response.IsSuccessStatusCode;
        // Once headers arrive, receive the temporary credential even if its owner retires,
        // so a known session can be revoked. This receipt read has its own deadline.
        using var receiptDeadline = viewAs ? new CancellationTokenSource(TimeSpan.FromSeconds(30)) : null;
        var readToken = receiptDeadline?.Token ?? linked.Token;
        byte[] responseBytes;
        try
        {
            if (!viewAs) { lease.ThrowIfRetired(); linked.Token.ThrowIfCancellationRequested(); }
            const int maximum = 4 * 1024 * 1024;
            if (content.Headers.ContentLength is > maximum) throw new InvalidDataException("U1 response exceeded its byte bound.");
            await using var stream = await content.ReadAsStreamAsync(readToken).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[16384];
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(), readToken).ConfigureAwait(false);
                if (!viewAs) lease.ThrowIfRetired();
                readToken.ThrowIfCancellationRequested();
                if (count == 0) break;
                if (bytes.Length + count > maximum) throw new InvalidDataException("U1 response exceeded its byte bound.");
                bytes.Write(buffer, 0, count);
            }
            if (!viewAs) lease.ThrowIfRetired();
            responseBytes = bytes.ToArray();
        }
        catch (Exception error) when (method == HttpMethod.Get && !response.IsSuccessStatusCode &&
            (error is IOException or HttpRequestException or OperationCanceledException or InvalidOperationException or TimeoutException or NotSupportedException || error is InvalidDataException))
        {
            // A failed error body must not turn a received refusal into a transient transport failure.
            throw new ApiException("request_failed", "The server refused the read and its response body could not be read.", (int)response.StatusCode);
        }
        if (!response.IsSuccessStatusCode)
        {
            var bounded = new ByteArrayContent(responseBytes);
            foreach (var header in content.Headers) bounded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = bounded;
            // GET error bytes are already bounded and in memory; keep the status even if the read deadline has elapsed.
            await ThrowApiException(response, request, method == HttpMethod.Get ? CancellationToken.None : linked.Token).ConfigureAwait(false);
        }
        var value = responseBytes.Length == 0 ? null : JsonNode.Parse(responseBytes) as JsonObject ?? throw new InvalidDataException("Expected a U1 object response.");
        var origin = context.BaseUrl;
        var result = new AdminUsersDocument(value, response.Headers.ETag?.ToString())
        {
            ViewAsBaseUrl = viewAs ? origin : null,
            RevokeViewAs = viewAs ? access => RevokeAdminViewAsAsync(origin, access) : null
        };
        try { lease.ThrowIfRetired(); linked.Token.ThrowIfCancellationRequested(); }
        catch
        {
            await result.DiscardViewAsAsync().ConfigureAwait(false);
            throw;
        }
        return result;
    }

    private async Task RevokeAdminViewAsAsync(string origin, string access)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Post, AdminUserViewAs.ServerUri(origin, "/api/v2/auth/logout"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            // No admin context, profile headers, token refresh or replay.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception) { /* Best effort; local credentials have already been discarded. */ }
    }
}
