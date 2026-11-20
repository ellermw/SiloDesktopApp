using System.Net;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Lease-bound Autoscan operations. Writes use the existing single-send transport.</summary>
public sealed class AdminAutoscanApi(SiloApiClient client)
{
    internal AdminLibrariesApi Libraries { get; } = new(client);
    private const string Root = "/api/v2/admin/autoscan";
    private static readonly AdminWalkLimits Limits = new(100, 10, 1000);

    private Task<AdminListRead<T>> Walk<T>(MonitorAuthorityLease lease, string suffix, Func<T, string?> identity, CancellationToken ct) =>
        AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("autoscan-" + suffix), Limits,
            (cursor, limit, token) => client.AdminLibrarySendAsync<ApiCollectionPage<T>>(lease, HttpMethod.Get,
                AdminPaging.WithPage(Root + "/" + suffix, limit, cursor), ct: token)!, identity, ct);

    public Task<AdminListRead<AdminAutoscanSource>> ListSourcesAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        Walk(lease, "sources", s => s.Id, ct);
    public Task<AdminListRead<AdminAutoscanAvailableSource>> ListAvailableSourcesAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        Walk(lease, "scan-source-plugins", s => string.IsNullOrEmpty(s.PluginId) || string.IsNullOrEmpty(s.CapabilityId) ? null : s.PluginId + "\n" + s.CapabilityId, ct);
    public Task<AdminListRead<AdminAutoscanConnection>> ListConnectionsAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        Walk(lease, "connections", c => c.Id, ct);
    public async Task<IReadOnlyList<AdminAutoscanRequestIntegration>> ReusableIntegrationsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        const string path = "/api/v2/admin/request-integrations";
        // A picker is bounded to 100 servers; no request-queue or title reads.
        var list = await AdminCollectionWalk.ReadAsync(lease, new("autoscan-request-server-picker"), new(50, 2, 100),
            (cursor, limit, token) => client.AdminLibrarySendAsync<ApiCollectionPage<AdminAutoscanRequestIntegration>>(lease, HttpMethod.Get, AdminPaging.WithPage(path, limit, cursor), ct: token)!, row => row.Id, ct).ConfigureAwait(false);
        if (!list.IsComplete) throw new InvalidDataException("Too many request servers for this picker.");
        var result = new List<AdminAutoscanRequestIntegration>();
        // Use canonical row reads, as the official picker does; sequential and cancelable.
        foreach (var row in list.Items)
        {
            ValidateId(row.Id);
            var detail = await client.AdminLibrarySendAsync<AdminAutoscanRequestIntegration>(lease, HttpMethod.Get, path + "/" + Uri.EscapeDataString(row.Id), ct: ct).ConfigureAwait(false);
            if (detail.Id != row.Id) throw new InvalidDataException("Request server identity changed.");
            // Only the service kind is needed. Retain no other provider config.
            var kind = detail.PluginConfig.GetValueOrDefault("service_kind"); detail.PluginConfig.Clear();
            if (kind.ValueKind == System.Text.Json.JsonValueKind.String) detail.PluginConfig["service_kind"] = kind;
            result.Add(detail);
        }
        return result;
    }
    public async Task<AdminAutoscanSettings> GetSettingsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var settings = await client.AdminAutoscanSendAsync<AdminAutoscanSettings>(lease, HttpMethod.Get, Root + "/settings", HttpStatusCode.OK, ct: ct).ConfigureAwait(false);
        if (settings.DefaultPollIntervalSeconds < 1 || settings.DebounceSeconds < 0) throw new InvalidDataException("Invalid Autoscan settings.");
        return settings;
    }
    public Task<AdminAutoscanSettingsResult> UpdateSettingsAsync(MonitorAuthorityLease lease, AdminAutoscanSettings body, CancellationToken ct = default)
    {
        if (body.DefaultPollIntervalSeconds < 1 || body.DebounceSeconds < 0) throw new ArgumentOutOfRangeException(nameof(body));
        return SaveSettingsAsync(lease, body, ct);
    }
    private async Task<AdminAutoscanSettingsResult> SaveSettingsAsync(MonitorAuthorityLease lease, AdminAutoscanSettings body, CancellationToken ct)
    {
        var result = await client.AdminAutoscanSendAsync<AdminAutoscanSettingsResult>(lease, HttpMethod.Put, Root + "/settings", HttpStatusCode.OK, body, ct).ConfigureAwait(false);
        if (result.Settings == null || result.Settings.DefaultPollIntervalSeconds < 1 || result.Settings.DebounceSeconds < 0 || result.RescheduleState is not ("applied" or "failed" or "not_configured"))
            throw new InvalidDataException("Invalid settings acknowledgement.");
        return result;
    }
    public async Task<AdminAutoscanTriggerReceipt> TriggerAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var result = await client.AdminAutoscanSendAsync<AdminAutoscanTriggerReceipt>(lease, HttpMethod.Post, Root + "/trigger", HttpStatusCode.OK, ct: ct).ConfigureAwait(false);
        if (result.Key != "autoscan_poll" || result.ExecutionScope != "process" || result.State is not ("idle" or "running" or "cancelling"))
            throw new InvalidDataException("Invalid Autoscan task acknowledgement.");
        return result;
    }

    public async Task<AdminAutoscanSource> CreateSourceAsync(MonitorAuthorityLease lease, string pluginId, string capabilityId, AdminAutoscanSourceWrite body, CancellationToken ct = default)
    {
        ValidateId(pluginId); ValidateId(capabilityId); Validate(body);
        var payload = SourceBody(body);
        payload["plugin_id"] = pluginId; payload["capability_id"] = capabilityId;
        var result = await client.AdminAutoscanSendAsync<AdminAutoscanSource>(lease, HttpMethod.Post, Root + "/sources", HttpStatusCode.Created, payload, ct).ConfigureAwait(false);
        ValidateId(result.Id);
        if (result.PluginId != pluginId || result.CapabilityId != capabilityId) throw new InvalidDataException("The server returned a different source type.");
        return result;
    }
    public async Task<AdminAutoscanSource> UpdateSourceAsync(MonitorAuthorityLease lease, string id, AdminAutoscanSourceWrite body, CancellationToken ct = default)
    {
        ValidateId(id); Validate(body);
        var result = await client.AdminAutoscanSendAsync<AdminAutoscanSource>(lease, HttpMethod.Put, Root + "/sources/" + Uri.EscapeDataString(id), HttpStatusCode.OK, SourceBody(body), ct).ConfigureAwait(false);
        if (!string.Equals(id, result.Id, StringComparison.Ordinal)) throw new InvalidDataException("The server returned a different source identity.");
        return result;
    }
    public Task DeleteSourceAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        ValidateId(id);
        return client.AdminAutoscanSendAsync<object>(lease, HttpMethod.Delete, Root + "/sources/" + Uri.EscapeDataString(id), HttpStatusCode.NoContent, ct: ct);
    }
    public async Task<AdminAutoscanSource> WebhookAsync(MonitorAuthorityLease lease, string id, bool rotate, CancellationToken ct = default)
    {
        ValidateId(id);
        var result = await client.AdminAutoscanSendAsync<AdminAutoscanSource>(lease, HttpMethod.Post,
            Root + "/sources/" + Uri.EscapeDataString(id) + (rotate ? "/webhook/rotate" : "/webhook"), HttpStatusCode.OK, ct: ct).ConfigureAwait(false);
        if (!string.Equals(id, result.Id, StringComparison.Ordinal)) throw new InvalidDataException("The server returned a different source identity.");
        return result;
    }
    public Task<AdminAutoscanRewriteSuggestions> SuggestionsAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        ValidateId(id);
        return client.AdminAutoscanSendAsync<AdminAutoscanRewriteSuggestions>(lease, HttpMethod.Get,
            Root + "/sources/" + Uri.EscapeDataString(id) + "/rewrite-suggestions", HttpStatusCode.OK, ct: ct);
    }
    public async Task<AdminAutoscanConnection> CreateConnectionAsync(MonitorAuthorityLease lease, AdminAutoscanConnectionCreate body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 1024 || body.Kind.Length > 256 || body.BaseUrl.Length > 8192 || body.ApiKeyRef.Length > 8192)
            throw new ArgumentException("Check the connection details.");
        var payload = new Dictionary<string, object?> { ["name"] = body.Name, ["kind"] = body.Kind };
        if (!string.IsNullOrEmpty(body.RequestIntegrationId)) payload["request_integration_id"] = body.RequestIntegrationId;
        else { payload["base_url"] = body.BaseUrl; if (body.ApiKeyRef.Length > 0) payload["api_key_ref"] = body.ApiKeyRef; }
        var result = await client.AdminAutoscanSendAsync<AdminAutoscanConnection>(lease, HttpMethod.Post, Root + "/connections", HttpStatusCode.Created, payload, ct).ConfigureAwait(false);
        ValidateId(result.Id);
        return result;
    }
    public Task<AdminAutoscanConnectionCheck> TestConnectionAsync(MonitorAuthorityLease lease, AdminAutoscanConnectionTest body, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(body.ConnectionId)) payload["connection_id"] = body.ConnectionId;
        else if (!string.IsNullOrEmpty(body.RequestIntegrationId)) payload["request_integration_id"] = body.RequestIntegrationId;
        else { payload["base_url"] = body.BaseUrl; if (body.ApiKeyRef.Length > 0) payload["api_key_ref"] = body.ApiKeyRef; }
        return client.AdminAutoscanSendAsync<AdminAutoscanConnectionCheck>(lease, HttpMethod.Post, Root + "/connections/test", HttpStatusCode.OK, payload, ct);
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256) throw new ArgumentException("Invalid Autoscan identity.", nameof(id));
    }
    private static void Validate(AdminAutoscanSourceWrite body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.DeliveryMode is not ("poll" or "webhook") || body.PollIntervalSeconds is <= 0 || body.PathRewrites.Count > 1000 || body.Label.Length > 1024)
            throw new ArgumentException("Invalid source configuration.", nameof(body));
        if (body.ConnectionId != null) ValidateId(body.ConnectionId);
    }
    private static Dictionary<string, object?> SourceBody(AdminAutoscanSourceWrite body) => new()
    {
        ["connection_id"] = body.ConnectionId, ["enabled"] = body.Enabled, ["delivery_mode"] = body.DeliveryMode,
        ["poll_interval_seconds"] = body.PollIntervalSeconds, ["path_rewrites"] = body.PathRewrites,
        ["source_config"] = body.SourceConfig, ["label"] = body.Label,
    };
}
