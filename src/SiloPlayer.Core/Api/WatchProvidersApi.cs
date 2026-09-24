using SiloPlayer.Core.Models.WatchProviders;

namespace SiloPlayer.Core.Api;

public sealed class WatchProvidersApi(SiloApiClient client)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ApiRequestContext Context, string ETag)> _revisions = new();
    public async Task<WatchProvidersResponse> GetProvidersAsync(CancellationToken ct = default)
        => new() { Providers = await client.GetAllItemsAsync<WatchProviderSummary>("/api/v2/watch-providers", ct) };

    public async Task<WatchProviderConnection> GetConnectionAsync(string provider, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var path = $"/api/v2/watch-providers/{Uri.EscapeDataString(provider)}/connection";
        var connection = await client.GetAsync<WatchProviderConnection>(path, ct);
        var settings = await client.GetWithETagAsync<System.Text.Json.JsonElement>(path + "/settings", ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Provider context changed.", ct);
        _revisions[provider] = (context, settings.ETag ?? throw new InvalidDataException("Missing provider settings revision."));
        ApplySettings(connection, settings.Body);
        return connection;
    }

    private static void ApplySettings(WatchProviderConnection connection, System.Text.Json.JsonElement settings)
    {
        // Display exactly the values protected by the saved settings revision.
        var values = System.Text.Json.JsonSerializer.Deserialize<WatchProviderConnection>(settings, V2Json.Options)!;
        connection.ImportWatchedEnabled = values.ImportWatchedEnabled;
        connection.ImportProgressEnabled = values.ImportProgressEnabled;
        connection.ExportWatchedEnabled = values.ExportWatchedEnabled;
        connection.ExportUnwatchedEnabled = values.ExportUnwatchedEnabled;
        connection.ImportFavoritesEnabled = values.ImportFavoritesEnabled;
        connection.ExportFavoritesEnabled = values.ExportFavoritesEnabled;
        connection.SyncFavoriteRemovalsEnabled = values.SyncFavoriteRemovalsEnabled;
        connection.ImportWatchlistEnabled = values.ImportWatchlistEnabled;
        connection.ExportWatchlistEnabled = values.ExportWatchlistEnabled;
        connection.SyncWatchlistRemovalsEnabled = values.SyncWatchlistRemovalsEnabled;
        connection.SyncWatchlistOrderEnabled = values.SyncWatchlistOrderEnabled;
        connection.ScrobbleEnabled = values.ScrobbleEnabled;
    }

    public async Task<WatchProviderDeviceAuthSession> StartDeviceAuthAsync(string provider, CancellationToken ct = default)
    {
        var session = await client.SendRequestAsync<WatchProviderDeviceAuthSession>(HttpMethod.Post,
            $"/api/v2/watch-providers/{Uri.EscapeDataString(provider)}/auth/device-code",
            null, null,
            ct);
        return session.Normalize(provider);
    }

    public async Task<WatchProviderConnection> PollDeviceAuthAsync(string provider, string authSessionId, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        await client.PostAsync<WatchProviderConnection>(
            $"/api/v2/watch-providers/{Uri.EscapeDataString(provider)}/auth/poll",
            new Dictionary<string, object?> { ["auth_session_id"] = authSessionId },
            ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Provider context changed.", ct);
        return await GetConnectionAsync(provider, ct);
    }

    public async Task<WatchProviderConnection> ConnectApiKeyAsync(string provider, string apiKey, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        await client.PostAsync<WatchProviderConnection>(
            $"/api/v2/watch-providers/{Uri.EscapeDataString(provider)}/auth/api-key",
            new Dictionary<string, object?> { ["api_key"] = apiKey },
            ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Provider context changed.", ct);
        return await GetConnectionAsync(provider, ct);
    }

    public async Task<WatchProviderConnection> UpdateConnectionAsync(string provider, IDictionary<string, object?> updates, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        if (!_revisions.TryGetValue(provider, out var revision) || revision.Context != context)
            throw new InvalidOperationException("Reload the provider connection before saving its settings.");
        var path = $"/api/v2/watch-providers/{Uri.EscapeDataString(provider)}/connection";
        var saved = await client.PatchWithETagResponseAsync<System.Text.Json.JsonElement>(path, updates, revision.ETag, ct);
        _revisions.TryRemove(provider, out _);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Provider context changed.", ct);
        if (saved.ETag != null) _revisions[provider] = (context, saved.ETag);
        // PATCH returns only settings, not the connection shown by the UI.
        return await GetConnectionAsync(provider, ct);
    }

    public Task DeleteConnectionAsync(string provider, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/watch-providers/{Uri.EscapeDataString(provider)}/connection", ct);

    public Task<WatchProviderManualSyncResponse> TriggerSyncAsync(string provider, CancellationToken ct = default)
        => client.SendRequestAsync<WatchProviderManualSyncResponse>(HttpMethod.Post,
            $"/api/v2/watch-providers/{Uri.EscapeDataString(provider)}/sync",
            null, null,
            ct);

    public async Task<WatchProviderSyncRunsResponse> GetSyncRunsAsync(string provider, CancellationToken ct = default)
        => new() { Runs = await client.GetAllItemsAsync<WatchProviderSyncRun>($"/api/v2/watch-providers/{Uri.EscapeDataString(provider)}/sync-runs", ct) };
}
