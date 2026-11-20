using System.Net;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
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

