using ContinuumPlayer.Core.Models.WatchProviders;

namespace ContinuumPlayer.Core.Api;

public sealed class WatchProvidersApi(ContinuumApiClient client)
{
    public Task<WatchProvidersResponse> GetProvidersAsync(CancellationToken ct = default)
        => client.GetAsync<WatchProvidersResponse>("/api/v1/watch-providers", ct);

    public Task<WatchProviderConnection> GetConnectionAsync(string provider, CancellationToken ct = default)
        => client.GetAsync<WatchProviderConnection>($"/api/v1/watch-providers/{Uri.EscapeDataString(provider)}/connection", ct);

    public async Task<WatchProviderDeviceAuthSession> StartDeviceAuthAsync(string provider, CancellationToken ct = default)
    {
        var session = await client.PostAsync<WatchProviderDeviceAuthSession>(
            $"/api/v1/watch-providers/{Uri.EscapeDataString(provider)}/auth/device-code",
            new Dictionary<string, object?>(),
            ct);
        return session.Normalize(provider);
    }

    public Task<WatchProviderConnection> PollDeviceAuthAsync(string provider, string authSessionId, CancellationToken ct = default)
        => client.PostAsync<WatchProviderConnection>(
            $"/api/v1/watch-providers/{Uri.EscapeDataString(provider)}/auth/poll",
            new Dictionary<string, object?> { ["auth_session_id"] = authSessionId },
            ct);

    public Task<WatchProviderConnection> ConnectApiKeyAsync(string provider, string apiKey, CancellationToken ct = default)
        => client.PostAsync<WatchProviderConnection>(
            $"/api/v1/watch-providers/{Uri.EscapeDataString(provider)}/auth/api-key",
            new Dictionary<string, object?> { ["api_key"] = apiKey },
            ct);

    public Task<WatchProviderConnection> UpdateConnectionAsync(string provider, IDictionary<string, object?> updates, CancellationToken ct = default)
        => client.PatchAsync<WatchProviderConnection>(
            $"/api/v1/watch-providers/{Uri.EscapeDataString(provider)}/connection",
            updates,
            ct);

    public Task DeleteConnectionAsync(string provider, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/watch-providers/{Uri.EscapeDataString(provider)}/connection", ct);

    public Task<WatchProviderManualSyncResponse> TriggerSyncAsync(string provider, CancellationToken ct = default)
        => client.PostAsync<WatchProviderManualSyncResponse>(
            $"/api/v1/watch-providers/{Uri.EscapeDataString(provider)}/sync",
            new Dictionary<string, object?>(),
            ct);

    public Task<WatchProviderSyncRunsResponse> GetSyncRunsAsync(string provider, CancellationToken ct = default)
        => client.GetAsync<WatchProviderSyncRunsResponse>($"/api/v1/watch-providers/{Uri.EscapeDataString(provider)}/sync-runs", ct);
}
