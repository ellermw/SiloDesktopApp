using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    internal async Task<JsonElement> AdminNotificationReadAsync(MonitorAuthorityLease owner, string path, CancellationToken ct)
    {
        owner.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, ct);
        using var request = CreateContextBoundRequest(owner.Context, HttpMethod.Get, path, null);
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: true).ConfigureAwait(false);
            owner.ThrowIfRetired();
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, owner, 512 * 1024, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                response.Content.Dispose(); response.Content = new ByteArrayContent(bytes);
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            }
            owner.ThrowIfRetired();
            return JsonSerializer.Deserialize<JsonElement>(bytes, JsonOptions);
        }
        finally { owner.ThrowIfRetired(); }
    }

    /// <summary>Notification DELETEs can acknowledge with no JSON. Bound even problem bodies,
    /// preserve captured authority and never refresh authentication or replay the request.</summary>
    internal async Task AdminNotificationDeleteAsync(MonitorAuthorityLease owner, string path, CancellationToken ct, bool acceptMissing)
    {
        owner.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, ct);
        using var request = CreateContextBoundRequest(owner.Context, HttpMethod.Delete, path, null);
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false).ConfigureAwait(false);
            owner.ThrowIfRetired();
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, owner, 512 * 1024, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && !(acceptMissing && response.StatusCode == HttpStatusCode.NotFound))
            {
                response.Content.Dispose(); response.Content = new ByteArrayContent(bytes);
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            }
            if (response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NoContent) throw new InvalidDataException("Invalid notification deletion acknowledgement.");
            owner.ThrowIfRetired();
        }
        finally { owner.ThrowIfRetired(); }
    }
}
