using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer.Core.Api;

public sealed class WebhookSyncApi(SiloApiClient client)
{
    private const string Base = "/api/v2/webhook-sync/connections";

    public async Task<List<WebhookSyncConnection>> GetConnectionsAsync(CancellationToken ct = default)
    {
        var connections = await client.GetAllItemsAsync<WebhookSyncConnection>(Base, ct);
        foreach (var connection in connections) NormalizeReceiver(connection);
        return connections;
    }

    public async Task<CreateWebhookSyncConnectionResponse> CreateConnectionAsync(Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var created = await client.PostAsync<CreateWebhookSyncConnectionResponse>(Base, V2Json.Body(body), ct);
        NormalizeReceiver(created.Connection);
        created.WebhookUrl = ResolveReceiver(created.WebhookUrl) ?? "";
        return created;
    }

    public async Task<WebhookSyncConnection> UpdateConnectionAsync(string id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var connection = await client.PutAsync<WebhookSyncConnection>($"{Base}/{Uri.EscapeDataString(id)}", V2Json.Body(body), ct);
        NormalizeReceiver(connection);
        return connection;
    }

    public Task DeleteConnectionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"{Base}/{Uri.EscapeDataString(id)}", ct);

    public async Task<RotateWebhookSyncResponse> RotateWebhookAsync(string id, CancellationToken ct = default)
    {
        var rotated = await client.SendRequestAsync<RotateWebhookSyncResponse>(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(id)}/webhook/rotate", null, null, ct);
        rotated.WebhookUrl = ResolveReceiver(rotated.WebhookUrl) ?? "";
        return rotated;
    }

    private void NormalizeReceiver(WebhookSyncConnection connection)
        => connection.WebhookUrl = ResolveReceiver(connection.WebhookUrl);

    private string? ResolveReceiver(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new Uri(new Uri(client.BaseUrl), value).AbsoluteUri;

    public Task<WebhookSyncProfileMappingsResponse> GetProfileMappingsAsync(string id, CancellationToken ct = default)
        => client.GetAsync<WebhookSyncProfileMappingsResponse>($"{Base}/{Uri.EscapeDataString(id)}/profile-mappings", ct);

    public Task<WebhookSyncProfileMappingsResponse> UpdateProfileMappingsAsync(string id, object body, CancellationToken ct = default)
        => client.PutAsync<WebhookSyncProfileMappingsResponse>($"{Base}/{Uri.EscapeDataString(id)}/profile-mappings", V2Json.Body(body), ct);

    public async Task<List<WebhookSyncEventLog>> GetEventsAsync(string id, int limit = 200, CancellationToken ct = default)
        => (await client.GetAsync<ApiCollectionPage<WebhookSyncEventLog>>($"{Base}/{Uri.EscapeDataString(id)}/events?limit={Math.Clamp(limit, 1, 200)}", ct)).Items;
}
