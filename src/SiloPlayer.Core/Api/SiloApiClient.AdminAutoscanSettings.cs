using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    /// <summary>
    /// GET for the Autoscan Settings page. A read is idempotent, so a 401 may refresh the access token once and be replayed
    /// under the same request generations (as the Autoscan Sources reads are); writes and DELETE stay single-attempt.
    /// </summary>
    internal async Task<JsonElement> AdminAutoscanSettingsReadAsync(MonitorAuthorityLease owner, string requestPath, int maxBytes, CancellationToken cancellationToken)
    {
        owner.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken);
        using var request = CreateContextBoundRequest(owner.Context, HttpMethod.Get, requestPath, null);
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: true).ConfigureAwait(false);
            owner.ThrowIfRetired();
            // Problem bodies are untrusted too; apply the same bound before parsing.
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, owner, maxBytes, linked.Token).ConfigureAwait(false);
            owner.ThrowIfRetired();
            if (!response.IsSuccessStatusCode)
            {
                response.Content.Dispose(); response.Content = new ByteArrayContent(bytes);
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            }
            return JsonSerializer.Deserialize<JsonElement>(bytes, JsonOptions);
        }
        finally { owner.ThrowIfRetired(); }
    }

    /// <summary>
    /// DELETE of one Autoscan connection (official internal/apiv2/admin_autoscan_connection_delete.go at
    /// Silo-Server/silo-server 66b836f82): one attempt, no token-refresh replay, and only 204 No Content acknowledges it.
    /// </summary>
    internal async Task AdminAutoscanDeleteAsync(MonitorAuthorityLease owner, string requestPath, CancellationToken cancellationToken)
    {
        owner.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken);
        using var request = CreateContextBoundRequest(owner.Context, HttpMethod.Delete, requestPath, null);
        using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
        owner.ThrowIfRetired();
        if (!response.IsSuccessStatusCode)
        {
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, owner, 1024 * 1024, linked.Token).ConfigureAwait(false);
            response.Content.Dispose(); response.Content = new ByteArrayContent(bytes);
            await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
        }
        owner.ThrowIfRetired();
        if (response.StatusCode != HttpStatusCode.NoContent) throw new InvalidDataException("Connection deletion was not acknowledged.");
    }
}
