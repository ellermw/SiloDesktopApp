using System.Net;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    /// <summary>
    /// A node command that answers only after its work. Like Backups it uses the client without an HttpClient timeout,
    /// so the caller's deadline is the only limit. Never refresh/replay a write.
    /// </summary>
    internal Task<ApiResponse<T>> AdminPlaybackLongSendAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path, object? body, string? etag, CancellationToken ct, int maxBytes) =>
        MonitorLogsSendAsync<T>(lease, method, path, body, etag, ct, maxBytes, _backupsHttp);
    /// <summary>The guarded node delete has no JSON acknowledgement. Never refresh/replay a write.</summary>
    internal async Task AdminPlaybackDeleteAsync(MonitorAuthorityLease lease, string path, string etag, CancellationToken ct)
    {
        AdminSettingsForm.RequireStrongETag(etag); lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct); linked.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Delete, path, null, new Dictionary<string, string> { ["If-Match"] = etag });
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (response.StatusCode == HttpStatusCode.PreconditionFailed) throw new AdminSettingsConflictException();
        if (!response.IsSuccessStatusCode)
        {
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, lease, 64 * 1024, linked.Token).ConfigureAwait(false);
            response.Content.Dispose(); response.Content = new ByteArrayContent(bytes);
            await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
        }
        lease.ThrowIfRetired();
        if (response.StatusCode != HttpStatusCode.NoContent) throw new InvalidDataException("Node deletion was not acknowledged.");
    }
}
