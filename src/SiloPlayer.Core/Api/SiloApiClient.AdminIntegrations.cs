using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    internal async Task<ApiResponse<T>> AdminIntegrationsSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method,
        string path, object? body, int maxBytes, CancellationToken ct, string? etag = null)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        if (etag != null) AdminSettingsForm.RequireStrongETag(etag);
        using var request = CreateContextBoundRequest(lease.Context, method, path, body,
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: method == HttpMethod.Get).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (response.StatusCode == HttpStatusCode.PreconditionFailed) throw new AdminSettingsConflictException();
            if (!response.IsSuccessStatusCode)
            {
                var errorBytes = await MonitorLogsReadBoundedAsync(response.Content, lease, maxBytes, linked.Token).ConfigureAwait(false);
                response.Content.Dispose();
                response.Content = new ByteArrayContent(errorBytes);
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
                lease.ThrowIfRetired();
            }
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, lease, maxBytes, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            var result = JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty integrations response.");
            lease.ThrowIfRetired();
            return new(result, response.Headers.ETag?.ToString());
        }
        finally { lease.ThrowIfRetired(); }
    }

    internal async Task AdminIntegrationsCompleteAsync(MonitorAuthorityLease lease, string path, object body, int maxBytes, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Post, path, body);
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode)
            {
                var bytes = await MonitorLogsReadBoundedAsync(response.Content, lease, maxBytes, linked.Token).ConfigureAwait(false);
                response.Content.Dispose(); response.Content = new ByteArrayContent(bytes);
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            }
            if (response.StatusCode != HttpStatusCode.NoContent) throw new InvalidDataException("Invalid callback acknowledgement.");
        }
        finally { lease.ThrowIfRetired(); }
    }

    internal async Task AdminIntegrationsDeleteAsync(MonitorAuthorityLease lease, string path, int maxBytes, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Delete, path, null);
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode)
            {
                var errorBytes = await MonitorLogsReadBoundedAsync(response.Content, lease, maxBytes, linked.Token).ConfigureAwait(false);
                response.Content.Dispose();
                response.Content = new ByteArrayContent(errorBytes);
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
                lease.ThrowIfRetired();
            }
            if (response.StatusCode != HttpStatusCode.NoContent)
                throw new InvalidDataException("The server did not acknowledge provider deletion.");
        }
        finally { lease.ThrowIfRetired(); }
    }
}
