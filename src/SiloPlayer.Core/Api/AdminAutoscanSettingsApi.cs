using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>The write may or may not have been applied (timeout, transport loss, 5xx or a malformed acknowledgement).</summary>
public sealed class AdminAutoscanOutcomeUnknownException() : Exception("Autoscan operation could not be confirmed. Reload before submitting again.");

/// <summary>A definite refusal (4xx other than 408): nothing was applied and the admin may correct and retry. The message is redacted and bounded.</summary>
public sealed class AdminAutoscanRefusedException(string detail, int statusCode) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
}

// Official source: Silo-Server/silo-server 66b836f82, internal/apiv2/admin_autoscan_{inspection,settings_update,connections,
// connection_create,connection_update,connection_delete,connection_check}.go.
public sealed class AdminAutoscanSettingsApi(SiloApiClient client)
{
    private const string Root = "/api/v2/admin/autoscan";
    private const int MaxBytes = 1024 * 1024;
    private const int MaxDetail = 500;

    public async Task<AdminAutoscanDefaults> GetSettingsAsync(MonitorAuthorityLease owner) => Defaults(await Read(owner, Root + "/settings").ConfigureAwait(false));

    public async Task<AdminAutoscanSettingsReceipt> SaveSettingsAsync(MonitorAuthorityLease owner, AdminAutoscanDefaults value)
    {
        if (value.DefaultPollIntervalSeconds <= 0 || value.DebounceSeconds < 0) throw new ArgumentException("Invalid polling defaults.");
        // A Dictionary body is serialized as JSON; any other object would be reflected as its own properties.
        var body = new Dictionary<string, object?>
        {
            ["enabled"] = value.Enabled,
            ["default_poll_interval_seconds"] = value.DefaultPollIntervalSeconds,
            ["debounce_seconds"] = value.DebounceSeconds,
        };
        var result = await Write(owner, HttpMethod.Put, Root + "/settings", body).ConfigureAwait(false);
        try
        {
            var state = Required(result, "reschedule_state", 32);
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
            var id = Required(summary, "id", AdminAutoscanLimits.Id);
            var row = await Read(owner, "/api/v2/admin/request-integrations/" + Uri.EscapeDataString(id), deadline.Token).ConfigureAwait(false);
            if (Required(row, "id", AdminAutoscanLimits.Id) != id) throw new InvalidDataException("Request server identity changed.");
            // The arr kind comes only from plugin_config.service_kind (official integrationKind); no credential field is read.
            if (row.TryGetProperty("plugin_config", out var config) && config.ValueKind == JsonValueKind.Object
                && config.TryGetProperty("service_kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() is "sonarr" or "radarr")
                result.Add(new(id, Required(row, "name", AdminAutoscanLimits.Name), kind.GetString()!));
        }
        owner.ThrowIfRetired();
        return Array.AsReadOnly(result.ToArray());
    }

    /// <param name="body">The create or update body; its <c>api_key_ref</c>, when present, is the only secret and is redacted from any refusal text.</param>
    public async Task<AdminAutoscanSavedConnection> SaveConnectionAsync(MonitorAuthorityLease owner, string? id, Dictionary<string, string> body)
    {
        var result = await Write(owner, id == null ? HttpMethod.Post : HttpMethod.Put,
            Root + "/connections" + (id == null ? "" : "/" + Uri.EscapeDataString(id)), Body(body), body.GetValueOrDefault("api_key_ref")).ConfigureAwait(false);
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
        catch (ApiException error) when (IsRefusal(error)) { throw Refusal(error); }
        catch { throw new AdminAutoscanOutcomeUnknownException(); }
    }

    public async Task<AdminAutoscanConnectionCheck> TestConnectionAsync(MonitorAuthorityLease owner, Dictionary<string, string> body)
    {
        var secret = body.GetValueOrDefault("api_key_ref");
        var result = await Write(owner, HttpMethod.Post, Root + "/connections/test", Body(body), secret).ConfigureAwait(false);
        try
        {
            var ok = result.GetProperty("ok").GetBoolean();
            var version = Optional(result, "version", 64);
            if (version != null && version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_' or '+'))) version = null;
            var error = Optional(result, "error", 4096);
            return new(ok, version, error == null ? null : Safe(error, secret));
        }
        catch { throw new AdminAutoscanOutcomeUnknownException(); }
    }

    private static Dictionary<string, object?> Body(Dictionary<string, string> body) => body.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);

    private async Task<JsonElement> Read(MonitorAuthorityLease owner, string requestPath, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { return (await client.MonitorLogsSendAsync<JsonElement>(owner, HttpMethod.Get, requestPath, ct: timeout.Token, maxBytes: MaxBytes).ConfigureAwait(false)).Body; }
        catch (ApiException error) { throw new ApiException("autoscan_read_failed", "Autoscan data could not be loaded.", error.StatusCode); }
    }

    private async Task<JsonElement> Write(MonitorAuthorityLease owner, HttpMethod method, string requestPath, Dictionary<string, object?> body, string? secret = null)
    {
        owner.ThrowIfRetired();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try { return (await client.MonitorLogsSendAsync<JsonElement>(owner, method, requestPath, body, ct: timeout.Token, maxBytes: MaxBytes).ConfigureAwait(false)).Body; }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch (LoggingSettingsConflictException) { throw new AdminAutoscanRefusedException("Autoscan data changed. Reload and review before submitting again.", 412); }
        catch (ApiException error) when (IsRefusal(error)) { throw Refusal(error, secret); }
        catch { throw new AdminAutoscanOutcomeUnknownException(); }
    }

    private static bool IsRefusal(ApiException error) => error.StatusCode is >= 400 and < 500 and not 408;

    /// <summary>The server's own problem detail (or field errors), redacted first and only then truncated.</summary>
    private static AdminAutoscanRefusedException Refusal(ApiException error, string? secret = null)
    {
        var parts = error.FieldErrors.Where(field => !string.IsNullOrWhiteSpace(field.Detail)).Take(5).Select(field => field.Detail!.Trim()).ToList();
        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(error.ProblemDetail)) parts.Add(error.ProblemDetail.Trim());
        else if (parts.Count == 0 && !string.IsNullOrWhiteSpace(error.ProblemTitle)) parts.Add(error.ProblemTitle.Trim());
        var text = parts.Count == 0 ? "Autoscan operation was rejected. Review the values and try again." : string.Join(" ", parts);
        return new(Safe(text, secret), error.StatusCode);
    }

    /// <summary>Replaces the submitted secret and any recognizable credential, then bounds the text for display.</summary>
    internal static string Safe(string text, string? secret)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "<redacted>", StringComparison.Ordinal);
        text = LocalLog.RedactDiagnosticText(text);
        if (text.Length <= MaxDetail) return text;
        var cut = char.IsHighSurrogate(text[MaxDetail - 1]) ? MaxDetail - 1 : MaxDetail;
        return text[..cut] + "…";
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
                if (!ids.Add(Required(row, "id", AdminAutoscanLimits.Id))) throw new InvalidDataException("Duplicate Autoscan row.");
                result.Add(row);
            }
            var info = value.GetProperty("page");
            if (!info.GetProperty("has_more").GetBoolean()) { owner.ThrowIfRetired(); return result; }
            cursor = Required(info, "next_cursor", 8192);
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
    private static AdminAutoscanSavedConnection Connection(JsonElement value) =>
        new(Required(value, "id", AdminAutoscanLimits.Id), Required(value, "name", AdminAutoscanLimits.Name), Required(value, "kind", AdminAutoscanLimits.Kind),
            Optional(value, "base_url", AdminAutoscanLimits.BaseUrl), Optional(value, "request_integration_id", AdminAutoscanLimits.Id), value.GetProperty("has_api_key").GetBoolean());
    private static string Required(JsonElement value, string key, int max) => Optional(value, key, max) is { Length: > 0 } text ? text : throw new InvalidDataException("Incomplete Autoscan response.");
    private static string? Optional(JsonElement value, string key, int max)
    {
        if (!value.TryGetProperty(key, out var item) || item.ValueKind == JsonValueKind.Null) return null;
        if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text || text.Length > max) throw new InvalidDataException("Invalid Autoscan text.");
        return text;
    }
}
