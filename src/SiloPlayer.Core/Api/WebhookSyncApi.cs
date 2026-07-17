using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer.Core.Api;

public sealed class WebhookSyncApi(SiloApiClient client)
{
    private const string Base = "/api/v1/webhook-sync/connections";

    public Task<List<WebhookSyncConnection>> GetConnectionsAsync(CancellationToken ct = default)
        => client.GetAsync<List<WebhookSyncConnection>>(Base, ct);

    public Task<CreateWebhookSyncConnectionResponse> CreateConnectionAsync(Dictionary<string, object?> body, CancellationToken ct = default)
        => client.PostAsync<CreateWebhookSyncConnectionResponse>(Base, body, ct);

    public Task<WebhookSyncConnection> UpdateConnectionAsync(string id, Dictionary<string, object?> body, CancellationToken ct = default)
        => client.PutAsync<WebhookSyncConnection>($"{Base}/{Uri.EscapeDataString(id)}", body, ct);

    public Task DeleteConnectionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"{Base}/{Uri.EscapeDataString(id)}", ct);

    public Task<RotateWebhookSyncResponse> RotateWebhookAsync(string id, CancellationToken ct = default)
        => client.PostAsync<RotateWebhookSyncResponse>($"{Base}/{Uri.EscapeDataString(id)}/webhook/rotate", new Dictionary<string, object?>(), ct);

    public Task<WebhookSyncProfileMappingsResponse> GetProfileMappingsAsync(string id, CancellationToken ct = default)
        => client.GetAsync<WebhookSyncProfileMappingsResponse>($"{Base}/{Uri.EscapeDataString(id)}/profile-mappings", ct);

    public Task<WebhookSyncProfileMappingsResponse> UpdateProfileMappingsAsync(string id, object body, CancellationToken ct = default)
        => client.PutAsync<WebhookSyncProfileMappingsResponse>($"{Base}/{Uri.EscapeDataString(id)}/profile-mappings", body, ct);

    public Task<List<WebhookSyncEventLog>> GetEventsAsync(string id, int limit = 200, CancellationToken ct = default)
        => client.GetAsync<List<WebhookSyncEventLog>>($"{Base}/{Uri.EscapeDataString(id)}/events?limit={Math.Clamp(limit, 1, 200)}", ct);
}
