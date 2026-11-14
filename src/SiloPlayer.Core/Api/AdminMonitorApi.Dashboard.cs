using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Dashboard reads and the few explicit dashboard actions. Every member takes
/// the caller's captured <see cref="MonitorAuthorityLease"/>: it is checked
/// before the request is created, the request is bound to the lease's API
/// context, and it is checked again after every response and page. Nothing
/// here recaptures the current account, and no mutation replays on refresh.
/// </summary>
public sealed partial class AdminMonitorApi
{
    public const int DashboardMaxInsightHours = 744;
    public const int DashboardMaxApiClientHours = 720;
    public const int DashboardMaxEndpointHours = 24;
    public const int DashboardMaxTopDays = 30;
    public const int DashboardMaxDrainPages = 100;
    public const string DashboardHiddenAttentionKey = "ui.dashboard.hidden_attention";

    /// <summary>The settings keys the attention notice reads; everything else in the map is dropped.</summary>
    public static readonly IReadOnlySet<string> DashboardAttentionKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "s3.public_bucket", "s3.private_bucket", "database.max_connections", "redis.url",
        "playback.transcode_enabled", "playback.hw_accel", "catalog.search.provider",
        "email.enabled", "email.smtp_host", "email.from_address", "server.public_url",
        DashboardHiddenAttentionKey,
    };

    public Task<DashboardCapabilities> GetDashboardCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardCapabilities>(lease, "/api/v2/admin/dashboard/capabilities", ct);

    public Task<DashboardStats> GetDashboardStatsAsync(MonitorAuthorityLease lease, bool refresh, CancellationToken ct = default)
        => DashboardReadAsync<DashboardStats>(lease, "/api/v2/admin/stats?refresh=" + (refresh ? "true" : "false"), ct);

    public Task<DashboardTimeseries> GetDashboardTimeseriesAsync(MonitorAuthorityLease lease, int hours, CancellationToken ct = default)
        => DashboardReadAsync<DashboardTimeseries>(lease,
            "/api/v2/admin/stats/timeseries?hours=" + Clamp(hours, 1, DashboardMaxInsightHours, 24), ct);

    public Task<DashboardPlaybackActivity> GetDashboardPlaybackActivityAsync(MonitorAuthorityLease lease, int hours, CancellationToken ct = default)
        => DashboardReadAsync<DashboardPlaybackActivity>(lease,
            "/api/v2/admin/stats/playback-activity?hours=" + Clamp(hours, 1, DashboardMaxInsightHours, 24), ct);

    public Task<DashboardTopActivity> GetDashboardTopActivityAsync(MonitorAuthorityLease lease, int days, CancellationToken ct = default)
        => DashboardReadAsync<DashboardTopActivity>(lease,
            "/api/v2/admin/stats/top-activity?days=" + Clamp(days, 1, DashboardMaxTopDays, 7), ct);

    public Task<DashboardDownloadsStats> GetDashboardDownloadsStatsAsync(MonitorAuthorityLease lease, int limit = 10, CancellationToken ct = default)
        => DashboardReadAsync<DashboardDownloadsStats>(lease,
            "/api/v2/admin/stats/downloads?limit=" + Clamp(limit, 1, 25, 10), ct);

    public Task<DashboardApiClients> GetDashboardApiClientsAsync(MonitorAuthorityLease lease, int hours, int limit = 10, CancellationToken ct = default)
        => DashboardReadAsync<DashboardApiClients>(lease,
            $"/api/v2/admin/stats/api-clients?hours={Clamp(hours, 1, DashboardMaxApiClientHours, 24)}&limit={Clamp(limit, 1, 25, 10)}", ct);

    /// <summary>A 404 here means the session has no recorded routes in the window: an empty detail, not a failure.</summary>
    public async Task<DashboardApiClientEndpoints> GetDashboardApiClientEndpointsAsync(MonitorAuthorityLease lease,
        string sessionId, int hours, CancellationToken ct = default)
    {
        var window = Math.Min(Clamp(hours, 1, DashboardMaxApiClientHours, 24), DashboardMaxEndpointHours);
        try
        {
            return await DashboardReadAsync<DashboardApiClientEndpoints>(lease,
                $"/api/v2/admin/stats/api-clients/{Uri.EscapeDataString(sessionId)}/endpoints?hours={window}", ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            lease.ThrowIfRetired();
            return new DashboardApiClientEndpoints { SessionId = sessionId, Hours = window, Endpoints = [] };
        }
    }

    public Task<DashboardServerStatus> GetDashboardServerStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardServerStatus>(lease, "/api/v2/admin/server/status", ct);

    public Task<DashboardBuildInfo> GetDashboardBuildInfoAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardBuildInfo>(lease, "/api/v2/admin/system/build", ct);

    public Task<DashboardResourceCapabilities> GetDashboardResourceCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardResourceCapabilities>(lease, "/api/v2/admin/system/resources/capabilities", ct);

    public Task<DashboardSystemResources> GetDashboardResourcesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardSystemResources>(lease, "/api/v2/admin/system/resources", ct);

    public Task<IReadOnlyList<DashboardNode>> ListDashboardNodesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DrainDashboardAsync<DashboardNode>(lease, "/api/v2/admin/nodes?limit=200", n => n.Id, ct);

    public Task<IReadOnlyList<DashboardUser>> ListDashboardUsersAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DrainDashboardAsync<DashboardUser>(lease, "/api/v2/admin/users?limit=200", u => u.Id, ct);

    /// <summary>The admin full library read (GET /libraries), not the user's visible-library projection.</summary>
    public Task<IReadOnlyList<DashboardLibrary>> ListDashboardLibrariesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DrainDashboardAsync<DashboardLibrary>(lease, "/api/v2/libraries", l => l.Id, ct);

    public Task<DashboardAutoscanStatus> GetDashboardAutoscanStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardAutoscanStatus>(lease, "/api/v2/admin/autoscan/status", ct);

    public Task<DashboardScanHistoryPage> ListDashboardScanHistoryAsync(MonitorAuthorityLease lease, int limit = 8, CancellationToken ct = default)
        => DashboardReadAsync<DashboardScanHistoryPage>(lease, "/api/v2/admin/autoscan/scans?limit=" + Clamp(limit, 1, 200, 8), ct);

    public async Task<IReadOnlyList<DashboardLogEntry>> ListDashboardRecentErrorsAsync(MonitorAuthorityLease lease, int limit = 8, CancellationToken ct = default)
    {
        var page = await DashboardReadAsync<DashboardCollection<DashboardLogEntry>>(lease,
            "/api/v2/admin/logs/app?level=" + Uri.EscapeDataString("error,warn") + "&limit=" + Clamp(limit, 1, 200, 8), ct).ConfigureAwait(false);
        return page.Items ?? [];
    }

    public async Task<DashboardLayoutRead> GetDashboardLayoutAsync(MonitorAuthorityLease lease, string? ifNoneMatch, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var linked = Link(lease, ct);
        var result = await client.GetDashboardLayoutAsync(lease.Context, "/api/v2/admin/dashboard/layout", ifNoneMatch, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return result;
    }

    /// <summary>Stores the layout document under the captured validator and returns the committed validator.</summary>
    public async Task<string> SaveDashboardLayoutAsync(MonitorAuthorityLease lease, string layoutJson, string expectedETag, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var linked = Link(lease, ct);
        var etag = await client.PutDashboardLayoutAsync(lease.Context, "/api/v2/admin/dashboard/layout", layoutJson, expectedETag, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return etag;
    }

    /// <summary>Deletes the stored layout. An absent layout is success; there is no validator.</summary>
    public async Task ResetDashboardLayoutAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var linked = Link(lease, ct);
        try
        {
            await client.SendDashboardActionAsync<object>(lease.Context, HttpMethod.Delete, "/api/v2/admin/dashboard/layout", null, linked.Token).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 404) { }
        lease.ThrowIfRetired();
    }

    public Task<DashboardScanStart?> StartDashboardLibraryScanAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
        => DashboardActionAsync<DashboardScanStart>(lease, HttpMethod.Post, "/api/v2/scan",
            new Dictionary<string, object?> { ["library_id"] = libraryId }, ct);

    public Task<DashboardScanCancel?> CancelDashboardLibraryScansAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
        => DashboardActionAsync<DashboardScanCancel>(lease, HttpMethod.Post, "/api/v2/scan/cancel",
            new Dictionary<string, object?> { ["library_id"] = libraryId }, ct);

    /// <summary>Runs only the scan_libraries task (Scan All); not a general task runner.</summary>
    public Task<DashboardTask?> ScanAllDashboardLibrariesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardActionAsync<DashboardTask>(lease, HttpMethod.Post, "/api/v2/admin/tasks/scan_libraries/run", null, ct);

    /// <summary>Signs out exactly one login session of one account after confirmation.</summary>
    public async Task RevokeDashboardLoginSessionAsync(MonitorAuthorityLease lease, string userId, string sessionId, CancellationToken ct = default)
        => await DashboardActionAsync<object>(lease, HttpMethod.Delete,
            $"/api/v2/admin/users/{Uri.EscapeDataString(userId)}/login-sessions/{Uri.EscapeDataString(sessionId)}", null, ct).ConfigureAwait(false);

    /// <summary>Effective settings projected to the attention keys only, with the strong validator.</summary>
    public async Task<DashboardAttentionSettings> GetDashboardAttentionSettingsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var linked = Link(lease, ct);
        var (values, etag) = await client.GetDashboardJsonWithETagAsync<Dictionary<string, JsonElement>>(
            lease.Context, "/api/v2/admin/settings/effective", linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        var projected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
            if (DashboardAttentionKeys.Contains(key) && value.ValueKind == JsonValueKind.String)
                projected[key] = value.GetString() ?? "";
        return new DashboardAttentionSettings(projected, etag);
    }

    public Task<DashboardSensitiveStatus> GetDashboardSensitiveStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardSensitiveStatus>(lease, "/api/v2/admin/settings/sensitive-status", ct);

    public Task<DashboardBranding> GetDashboardBrandingAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardBranding>(lease, "/api/v2/theme/branding", ct);

    public Task<DashboardSearchStatus> GetDashboardSearchStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => DashboardReadAsync<DashboardSearchStatus>(lease, "/api/v2/admin/catalog/search/status", ct);

    /// <summary>Writes only the hidden-notices setting, under the displayed settings validator.</summary>
    public async Task<DashboardSettingValue> SetDashboardHiddenAttentionAsync(MonitorAuthorityLease lease, string value,
        string expectedETag, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var linked = Link(lease, ct);
        var result = await client.PutDashboardSettingAsync<DashboardSettingValue>(lease.Context,
            "/api/v2/admin/settings/" + Uri.EscapeDataString(DashboardHiddenAttentionKey),
            new Dictionary<string, object?> { ["value"] = value }, expectedETag, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return result;
    }

    private async Task<T> DashboardReadAsync<T>(MonitorAuthorityLease lease, string path, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        using var linked = Link(lease, ct);
        var value = await client.GetDashboardJsonAsync<T>(lease.Context, path, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return value;
    }

    private async Task<T?> DashboardActionAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body, CancellationToken ct) where T : class
    {
        lease.ThrowIfRetired();
        using var linked = Link(lease, ct);
        var value = await client.SendDashboardActionAsync<T>(lease.Context, method, path, body, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return value;
    }

    private async Task<IReadOnlyList<T>> DrainDashboardAsync<T>(MonitorAuthorityLease lease, string path,
        Func<T, string> identity, CancellationToken ct)
    {
        var result = new List<T>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pagePath = path;
        for (var page = 0; ; page++)
        {
            if (page >= DashboardMaxDrainPages) throw new InvalidDataException("Too many pages.");
            var body = await DashboardReadAsync<DashboardCollection<T>>(lease, pagePath, ct).ConfigureAwait(false);
            if (body.Items == null) throw new InvalidDataException("Invalid collection page.");
            foreach (var item in body.Items)
            {
                if (item == null) continue;
                var id = identity(item);
                if (string.IsNullOrEmpty(id) || seenIds.Add(id)) result.Add(item);
            }
            if (body.Page?.HasMore != true) return result;
            var cursor = body.Page.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
                throw new InvalidDataException("Invalid pagination cursor.");
            pagePath = path + (path.Contains('?') ? "&" : "?") + "cursor=" + Uri.EscapeDataString(cursor);
        }
    }

    private static CancellationTokenSource Link(MonitorAuthorityLease lease, CancellationToken ct)
        => ct.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct)
            : CancellationTokenSource.CreateLinkedTokenSource(lease.Token);

    /// <summary>Mirrors the server clamps so equal windows share one request shape.</summary>
    internal static int Clamp(int value, int min, int max, int fallback)
        => value == int.MinValue ? fallback : Math.Min(max, Math.Max(min, value));
}
