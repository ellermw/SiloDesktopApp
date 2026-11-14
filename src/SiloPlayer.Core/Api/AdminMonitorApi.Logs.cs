using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminMonitorApi
{
    public async Task<ApiCollectionPage<LogsOperationalEntry>> LogsGetOperationalAsync(MonitorAuthorityLease lease, LogsQuery query, string? cursor = null, int limit = 50, CancellationToken ct = default)
    {
        var response = await client.MonitorLogsSendAsync<ApiCollectionPage<LogsOperationalEntry>>(lease, HttpMethod.Get, "/api/v2/admin/logs/app" + query.ToQuery("app", cursor, limit), ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return LogsValidatePage(response.Body, cursor, 200);
    }

    public async Task<ApiCollectionPage<LogsAuditEntry>> LogsGetAuditAsync(MonitorAuthorityLease lease, LogsQuery query, string? cursor = null, int limit = 50, CancellationToken ct = default)
    {
        var response = await client.MonitorLogsSendAsync<ApiCollectionPage<LogsAuditEntry>>(lease, HttpMethod.Get, "/api/v2/admin/logs/audit" + query.ToQuery("audit", cursor, limit), ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return LogsValidatePage(response.Body, cursor, 200);
    }

    public async Task<LogsSocketTicket> LogsCreateSocketTicketAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var response = await client.MonitorLogsSendAsync<LogsSocketTicket>(lease, HttpMethod.Post, "/api/v2/admin/logs/ws-ticket", ct: timeout.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        var ticket = response.Body;
        if (ticket.Protocol != "silo.admin-logs.v2" || !Regex.IsMatch(ticket.Ticket, "^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant) || ticket.ExpiresIn is < 1 or > 30 || ticket.MaxConnectionSeconds is < 1 or > 300)
            throw new InvalidDataException("Invalid log stream credential.");
        return ticket;
    }

    public async Task<DiagnosticsCapabilities> DiagnosticsGetCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var response = await client.MonitorLogsSendAsync<DiagnosticsCapabilities>(lease, HttpMethod.Get, "/api/v2/diagnostics/capabilities", ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (response.Body.Status is not ("available" or "disabled" or "storage_unavailable")) throw new InvalidDataException("Diagnostics availability is unknown.");
        return response.Body;
    }

    public async Task<ApiCollectionPage<DiagnosticsReportSummary>> DiagnosticsListReportsAsync(MonitorAuthorityLease lease, DiagnosticsQuery query, string? cursor = null, CancellationToken ct = default)
    {
        var response = await client.MonitorLogsSendAsync<ApiCollectionPage<DiagnosticsReportSummary>>(lease, HttpMethod.Get, "/api/v2/admin/diagnostics/reports" + query.ToQuery(cursor), ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return LogsValidatePage(response.Body, cursor, 200);
    }

    public async Task<DiagnosticsReportDetail> DiagnosticsGetReportAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        var response = await client.MonitorLogsSendAsync<DiagnosticsReportDetail>(lease, HttpMethod.Get, "/api/v2/admin/diagnostics/reports/" + LogsPathId(id), ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return response.Body;
    }

    public Task DiagnosticsDeleteReportAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default) =>
        client.MonitorLogsDeleteAsync(lease, "/api/v2/admin/diagnostics/reports/" + LogsPathId(id), ct);

    public Task DiagnosticsDownloadReportAsync(MonitorAuthorityLease lease, string id, Stream destination, long maxBytes, CancellationToken ct = default) =>
        client.MonitorLogsDownloadAsync(lease, "/api/v2/admin/diagnostics/reports/" + LogsPathId(id) + "/download", destination, maxBytes, ct);

    public async Task<LoggingSettingsSnapshot> LoggingGetSettingsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var response = await client.MonitorLogsSendAsync<Dictionary<string, JsonElement>>(lease, HttpMethod.Get, "/api/v2/admin/settings/effective", ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        LoggingDraft.RequireStrongETag(response.ETag);
        return new(LoggingProjectSettings(response.Body, includeDiagnostics: true), response.ETag!);
    }

    public async Task<LoggingRestartKeys> LoggingGetRestartKeysAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var response = await client.MonitorLogsSendAsync<LoggingRestartKeys>(lease, HttpMethod.Get, "/api/v2/admin/settings/restart-keys", ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return response.Body;
    }

    public async Task<ApiResponse<LoggingSettingsResult>> LoggingSaveAsync(MonitorAuthorityLease lease, LoggingSaveIntent intent, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        LoggingDraft.Validate(intent.Values);
        if (intent.Values.Count == 0) throw new ArgumentException("There are no Logging changes to save.", nameof(intent));
        var response = await client.MonitorLogsSendAsync<LoggingSettingsResponse>(lease, HttpMethod.Put, "/api/v2/admin/settings", new { values = intent.Values }, intent.ETag, ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        LoggingDraft.RequireStrongETag(response.ETag);
        return new(new LoggingSettingsResult
        {
            Values = LoggingProjectSettings(response.Body.Values),
            RestartRequired = response.Body.RestartRequired,
            RestartRequiredKeys = response.Body.RestartRequiredKeys
        }, response.ETag);
    }

    public async Task<ApiResponse<LoggingSettingValue>> DiagnosticsSetUploadsEnabledAsync(MonitorAuthorityLease lease, bool enabled, string etag, CancellationToken ct = default)
    {
        var response = await client.MonitorLogsSendAsync<LoggingSettingValue>(lease, HttpMethod.Put, "/api/v2/admin/settings/diagnostics.uploads_enabled", new { value = enabled ? "true" : "false" }, etag, ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return response;
    }

    private sealed record LoggingSettingsResponse
    {
        public LoggingSettingsResponse() { }
        public Dictionary<string, JsonElement> Values { get; init; } = [];
        public bool RestartRequired { get; init; }
        public string[] RestartRequiredKeys { get; init; } = [];
    }

    private static Dictionary<string, string> LoggingProjectSettings(Dictionary<string, JsonElement> values, bool includeDiagnostics = false)
        => ProjectEffectiveSettings(values, key => LoggingDraft.Keys.Contains(key, StringComparer.Ordinal) ||
            (includeDiagnostics && key == "diagnostics.uploads_enabled"));

    private static ApiCollectionPage<T> LogsValidatePage<T>(ApiCollectionPage<T> page, string? cursor, int maxItems)
    {
        if (page.Items == null || page.Items.Count > maxItems || page.Page == null || (page.Page.HasMore && (string.IsNullOrWhiteSpace(page.Page.NextCursor) || page.Page.NextCursor == cursor)))
            throw new InvalidDataException("Monitor response is missing valid pagination metadata.");
        if (!page.Page.HasMore) page.Page.NextCursor = null;
        return page;
    }

    private static string LogsPathId(string id) => string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A report ID is required.", nameof(id)) : Uri.EscapeDataString(id);
}
