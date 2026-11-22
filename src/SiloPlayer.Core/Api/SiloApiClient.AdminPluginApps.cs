using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    /// <summary>
    /// WebUI <c>navigateToPluginRoute</c>: <c>POST /api/v2/auth/plugin-launch</c> with the current
    /// login session and profile. The server answers with the five-minute HttpOnly
    /// <c>silo_plugin_access</c> cookie on the plugin-content parent path; only its
    /// Set-Cookie values are returned. The transport must not keep cookies or follow redirects,
    /// so the launch credential never enters the API client's own cookie jar.
    /// </summary>
    internal async Task<IReadOnlyList<string>> AdminPluginLaunchAsync(MonitorAuthorityLease lease, HttpClient transport, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        using var request = CreateContextBoundRequest(lease.Context, HttpMethod.Post, "/api/v2/auth/plugin-launch", null);
        try
        {
            using var response = await SendWithRetryAsync(request, linked.Token, allowTokenRefresh: false, transport).ConfigureAwait(false);
            lease.ThrowIfRetired();
            if (!response.IsSuccessStatusCode)
            {
                var errorBytes = await MonitorLogsReadBoundedAsync(response.Content, lease, 64 * 1024, linked.Token).ConfigureAwait(false);
                response.Content.Dispose();
                response.Content = new ByteArrayContent(errorBytes);
                await ThrowApiException(response, request, linked.Token).ConfigureAwait(false);
            }
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, lease, 64 * 1024, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            using (var document = JsonDocument.Parse(bytes))
            {
                // PluginLaunch receipt: {"expires_in": seconds}.
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("expires_in", out var expires) ||
                    !expires.TryGetInt64(out var seconds) || seconds <= 0 || seconds > 3600)
                    throw new InvalidDataException("Invalid plugin launch receipt.");
            }
            return response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.ToArray() : [];
        }
        finally { lease.ThrowIfRetired(); }
    }
}
