using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed class AdminAutoscanOutcomeUnknownException() : Exception("Autoscan operation could not be confirmed. Reload before submitting again.");

public sealed class AdminAutoscanSettingsApi(SiloApiClient client)
{
    private const string Root = "/api/v2/admin/autoscan";
    private const int MaxBytes = 1024 * 1024;

    public async Task<AdminAutoscanDefaults> GetSettingsAsync(MonitorAuthorityLease owner) => Defaults(await Read(owner, Root + "/settings").ConfigureAwait(false));

    public async Task<AdminAutoscanSettingsReceipt> SaveSettingsAsync(MonitorAuthorityLease owner, AdminAutoscanDefaults value)
    {
        if (value.DefaultPollIntervalSeconds <= 0 || value.DebounceSeconds < 0) throw new ArgumentException("Invalid polling defaults.");
        var body = new { enabled = value.Enabled, default_poll_interval_seconds = value.DefaultPollIntervalSeconds, debounce_seconds = value.DebounceSeconds };
        var result = await Write(owner, HttpMethod.Put, Root + "/settings", body).ConfigureAwait(false);
        try
        {
            var state = Required(result, "reschedule_state");
            if (state is not ("applied" or "failed" or "not_configured")) throw new InvalidDataException();
            return new(Defaults(result.GetProperty("settings")), state);
        }
        catch { throw new AdminAutoscanOutcomeUnknownException(); }
    }

    public async Task<IReadOnlyList<AdminAutoscanSavedConnection>> GetConnectionsAsync(MonitorAuthorityLease owner)
    {
        var rows = await Pages(owner, Root + "/connections", 100, 10).ConfigureAwait(false);
        return Array.AsReadOnly(rows.Select(Connection).ToArray());
    }

    public async Task<IReadOnlyList<AdminAutoscanRequestServer>> GetRequestServersAsync(MonitorAuthorityLease owner)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var summaries = await Pages(owner, "/api/v2/admin/request-integrations", 50, 4, deadline.Token).ConfigureAwait(false);
        var result = new List<AdminAutoscanRequestServer>();
        foreach (var summary in summaries)
        {
            owner.ThrowIfRetired();
            var id = Required(summary, "id");
            var row = await Read(owner, "/api/v2/admin/request-integrations/" + Uri.EscapeDataString(id), deadline.Token).ConfigureAwait(false);
            if (Required(row, "id") != id) throw new InvalidDataException("Request server identity changed.");
            if (row.TryGetProperty("plugin_config", out var config) && config.ValueKind == JsonValueKind.Object
                && config.TryGetProperty("service_kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() is "sonarr" or "radarr")
                result.Add(new(id, Required(row, "name"), kind.GetString()!));
        }
        owner.ThrowIfRetired();
        return Array.AsReadOnly(result.ToArray());
    }

    public async Task<AdminAutoscanSavedConnection> SaveConnectionAsync(MonitorAuthorityLease owner, string? id, Dictionary<string, string> body)
    {
        var result = await Write(owner, id == null ? HttpMethod.Post : HttpMethod.Put,
            Root + "/connections" + (id == null ? "" : "/" + Uri.EscapeDataString(id)), body).ConfigureAwait(false);
        try
        {
            var row = Connection(result);
            if (id != null && row.Id != id) throw new InvalidDataException();
            return row;
        }
        catch { throw new AdminAutoscanOutcomeUnknownException(); }
    }

    public async Task DeleteConnectionAsync(MonitorAuthorityLease owner, string id)
    {
        owner.ThrowIfRetired();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { await client.AdminAutoscanDeleteAsync(owner, Root + "/connections/" + Uri.EscapeDataString(id), timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode < 500 && error.StatusCode != 408) { throw new ApiException("autoscan_rejected", "Connection deletion was rejected. Reload connections and check source bindings.", error.StatusCode); }
        catch { throw new AdminAutoscanOutcomeUnknownException(); }
    }

    public async Task<AdminAutoscanConnectionCheck> TestConnectionAsync(MonitorAuthorityLease owner, Dictionary<string, string> body)
    {
        var result = await Write(owner, HttpMethod.Post, Root + "/connections/test", body).ConfigureAwait(false);
        try
        {
            var ok = result.GetProperty("ok").GetBoolean();
            var version = Optional(result, "version");
            if (version != null && (version.Length > 64 || version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))) version = null;
            return new(ok, version);
        }
        catch { throw new AdminAutoscanOutcomeUnknownException(); }
    }

    private async Task<JsonElement> Read(MonitorAuthorityLease owner, string requestPath, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { return (await client.MonitorLogsSendAsync<JsonElement>(owner, HttpMethod.Get, requestPath, ct: timeout.Token, maxBytes: MaxBytes).ConfigureAwait(false)).Body; }
        catch (ApiException error) { throw new ApiException("autoscan_read_failed", "Autoscan data could not be loaded.", error.StatusCode); }
    }

    private async Task<JsonElement> Write(MonitorAuthorityLease owner, HttpMethod method, string requestPath, object body)
    {
        owner.ThrowIfRetired();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try { return (await client.MonitorLogsSendAsync<JsonElement>(owner, method, requestPath, body, ct: timeout.Token, maxBytes: MaxBytes).ConfigureAwait(false)).Body; }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch (LoggingSettingsConflictException) { throw new ApiException("autoscan_conflict", "Autoscan data changed. Reload and review before submitting again.", 412); }
        catch (ApiException error) when (error.StatusCode < 500 && error.StatusCode != 408) { throw new ApiException("autoscan_rejected", "Autoscan operation was rejected. Reload and review before submitting again.", error.StatusCode); }
        catch { throw new AdminAutoscanOutcomeUnknownException(); }
    }

    private async Task<IReadOnlyList<JsonElement>> Pages(MonitorAuthorityLease owner, string requestPath, int pageSize, int maxPages, CancellationToken cancellationToken = default)
    {
        var result = new List<JsonElement>(); var ids = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        for (var page = 0; page < maxPages; page++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var value = await Read(owner, requestPath + "?limit=" + pageSize + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), deadline.Token).ConfigureAwait(false);
            var items = value.GetProperty("items");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > pageSize) throw new InvalidDataException("Invalid Autoscan page.");
            foreach (var row in items.EnumerateArray())
            {
                if (!ids.Add(Required(row, "id"))) throw new InvalidDataException("Duplicate Autoscan row.");
                result.Add(row);
            }
            var info = value.GetProperty("page");
            if (!info.GetProperty("has_more").GetBoolean()) { owner.ThrowIfRetired(); return result; }
            cursor = Required(info, "next_cursor");
            if (!cursors.Add(cursor)) throw new InvalidDataException("Repeated Autoscan continuation.");
        }
        throw new InvalidDataException("Autoscan list exceeds its display bound.");
    }

    private static AdminAutoscanDefaults Defaults(JsonElement value)
    {
        var settings = new AdminAutoscanDefaults(value.GetProperty("enabled").GetBoolean(), value.GetProperty("default_poll_interval_seconds").GetInt32(), value.GetProperty("debounce_seconds").GetInt32());
        if (settings.DefaultPollIntervalSeconds <= 0 || settings.DebounceSeconds < 0) throw new InvalidDataException("Invalid polling defaults.");
        return settings;
    }
    private static AdminAutoscanSavedConnection Connection(JsonElement value)
    {
        var url = Optional(value, "base_url");
        return new(Required(value, "id"), Required(value, "name"), Required(value, "kind"),
            url != null && AdminAutoscanConnectionEditor.IsSafeUrl(url) ? url : null, Optional(value, "request_integration_id"), value.GetProperty("has_api_key").GetBoolean());
    }
    private static string Required(JsonElement value, string key) => Optional(value, key) is { Length: > 0 } text ? text : throw new InvalidDataException("Incomplete Autoscan response.");
    private static string? Optional(JsonElement value, string key)
    {
        if (!value.TryGetProperty(key, out var item) || item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: <= 4096 } text) throw new InvalidDataException("Invalid Autoscan text.");
        return text;
    }
}

