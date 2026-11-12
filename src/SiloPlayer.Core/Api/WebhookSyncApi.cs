using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer.Core.Api;

public sealed class WebhookSyncApi(SiloApiClient client)
{
    private const string Base = "/api/v2/webhook-sync/connections";
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext owner) => client.IsCurrentContext(owner);
    private void RequireCurrent(ApiRequestContext owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(owner)) throw new OperationCanceledException("Webhook Sync authority changed.", ct);
    }
    // Transport catch-up retires reads only. A write the server has already
    // committed stays publishable while its manager and access are unchanged.
    private void RequireCommitted(ApiIdentityContext identity, ApiRequestContext owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentIdentityContext(identity) || client.CaptureContext().AccessGeneration != owner.AccessGeneration)
            throw new OperationCanceledException("Webhook Sync authority changed.", ct);
    }
    private async Task<T> Send<T>(ApiRequestContext owner, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        // Captured before the owner check: identity cannot change without retiring the owner.
        var identity = client.CaptureIdentityContext();
        RequireCurrent(owner, ct);
        var result = await client.SendRequestAsync<T>(owner, method, path, body, ct).ConfigureAwait(false);
        if (method == HttpMethod.Get) RequireCurrent(owner, ct); else RequireCommitted(identity, owner, ct);
        return result;
    }

    public Task<List<WebhookSyncConnection>> GetConnectionsAsync(CancellationToken ct = default)
        => GetConnectionsAsync(client.CaptureContext(), ct);
    public async Task<List<WebhookSyncConnection>> GetConnectionsAsync(ApiRequestContext owner, CancellationToken ct = default)
    {
        var result = new List<WebhookSyncConnection>(); var path = Base; var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await Send<ApiCollectionPage<WebhookSyncConnection>>(owner, HttpMethod.Get, path, null, ct).ConfigureAwait(false);
            foreach (var connection in page.Items) NormalizeReceiver(owner, connection);
            result.AddRange(page.Items);
            if (page.Page?.HasMore != true) return result;
            var cursor = page.Page.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seen.Add(cursor)) throw new InvalidDataException("Invalid API pagination cursor.");
            path = Base + "?cursor=" + Uri.EscapeDataString(cursor);
        }
    }

    public Task<CreateWebhookSyncConnectionResponse> CreateConnectionAsync(Dictionary<string, object?> body, CancellationToken ct = default)
        => CreateConnectionAsync(client.CaptureContext(), body, ct);
    public async Task<CreateWebhookSyncConnectionResponse> CreateConnectionAsync(ApiRequestContext owner, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var created = await Send<CreateWebhookSyncConnectionResponse>(owner, HttpMethod.Post, Base, V2Json.Body(body), ct).ConfigureAwait(false);
        NormalizeReceiver(owner, created.Connection);
        created.WebhookUrl = ResolveReceiver(owner, created.WebhookUrl) ?? "";
        return created;
    }

    public Task<WebhookSyncConnection> UpdateConnectionAsync(string id, Dictionary<string, object?> body, CancellationToken ct = default)
        => UpdateConnectionAsync(client.CaptureContext(), id, body, ct);
    public async Task<WebhookSyncConnection> UpdateConnectionAsync(ApiRequestContext owner, string id, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var connection = await Send<WebhookSyncConnection>(owner, HttpMethod.Put, $"{Base}/{Uri.EscapeDataString(id)}", V2Json.Body(body), ct).ConfigureAwait(false);
        NormalizeReceiver(owner, connection);
        return connection;
    }

    public Task DeleteConnectionAsync(string id, CancellationToken ct = default)
        => DeleteConnectionAsync(client.CaptureContext(), id, ct);
    public async Task DeleteConnectionAsync(ApiRequestContext owner, string id, CancellationToken ct = default)
    {
        var identity = client.CaptureIdentityContext();
        RequireCurrent(owner, ct);
        await client.SendNoContentRequestAsync(owner, HttpMethod.Delete, $"{Base}/{Uri.EscapeDataString(id)}", null, ct).ConfigureAwait(false);
        RequireCommitted(identity, owner, ct);
    }

    public Task<RotateWebhookSyncResponse> RotateWebhookAsync(string id, CancellationToken ct = default)
        => RotateWebhookAsync(client.CaptureContext(), id, ct);
    public async Task<RotateWebhookSyncResponse> RotateWebhookAsync(ApiRequestContext owner, string id, CancellationToken ct = default)
    {
        var rotated = await Send<RotateWebhookSyncResponse>(owner, HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(id)}/webhook/rotate", null, ct).ConfigureAwait(false);
        rotated.WebhookUrl = ResolveReceiver(owner, rotated.WebhookUrl) ?? "";
        return rotated;
    }

    private static void NormalizeReceiver(ApiRequestContext owner, WebhookSyncConnection connection)
        => connection.WebhookUrl = ResolveReceiver(owner, connection.WebhookUrl);
    private static string? ResolveReceiver(ApiRequestContext owner, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new Uri(new Uri(owner.BaseUrl), value).AbsoluteUri;

    public Task<WebhookSyncProfileMappingsResponse> GetProfileMappingsAsync(string id, CancellationToken ct = default)
        => GetProfileMappingsAsync(client.CaptureContext(), id, ct);
    public Task<WebhookSyncProfileMappingsResponse> GetProfileMappingsAsync(ApiRequestContext owner, string id, CancellationToken ct = default)
        => Send<WebhookSyncProfileMappingsResponse>(owner, HttpMethod.Get, $"{Base}/{Uri.EscapeDataString(id)}/profile-mappings", null, ct);
    public Task<WebhookSyncProfileMappingsResponse> UpdateProfileMappingsAsync(string id, object body, CancellationToken ct = default)
        => UpdateProfileMappingsAsync(client.CaptureContext(), id, body, ct);
    public Task<WebhookSyncProfileMappingsResponse> UpdateProfileMappingsAsync(ApiRequestContext owner, string id, object body, CancellationToken ct = default)
        => Send<WebhookSyncProfileMappingsResponse>(owner, HttpMethod.Put, $"{Base}/{Uri.EscapeDataString(id)}/profile-mappings", V2Json.Body(body), ct);
    public Task<List<WebhookSyncEventLog>> GetEventsAsync(string id, int limit = 200, CancellationToken ct = default)
        => GetEventsAsync(client.CaptureContext(), id, limit, ct);
    public async Task<List<WebhookSyncEventLog>> GetEventsAsync(ApiRequestContext owner, string id, int limit = 200, CancellationToken ct = default)
        => (await Send<ApiCollectionPage<WebhookSyncEventLog>>(owner, HttpMethod.Get, $"{Base}/{Uri.EscapeDataString(id)}/events?limit={Math.Clamp(limit, 1, 200)}", null, ct).ConfigureAwait(false)).Items;
}
