using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Reads behind the global Server Activity indicator and the admin shell:
/// running tasks (hidden workers included), libraries for scan names, the
/// download-preparation queue, the build card and restart status. Scans have no
/// REST list; they come from the <c>scans</c> realtime channel
/// (<see cref="ParseIndicatorScanRuns"/>). No count endpoint is invented.
/// </summary>
public partial class AdminMonitorApi
{
    /// <summary>Items read per preparation request; counts always cover the whole queue.</summary>
    public const int IndicatorPreparationsLimit = 200;

    private static CancellationToken IndicatorToken(MonitorAuthorityLease lease, CancellationToken ct, out CancellationTokenSource? linked)
    {
        linked = ct.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct) : null;
        return linked?.Token ?? lease.Token;
    }

    private async Task<T> IndicatorGetAsync<T>(MonitorAuthorityLease lease, string path, CancellationToken ct)
    {
        var token = IndicatorToken(lease, ct, out var linked);
        using var linkedScope = linked;
        lease.ThrowIfRetired();
        var value = await client.SendRequestAsync<T>(lease.Context, HttpMethod.Get, path, null, token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return value;
    }

    public async Task<IReadOnlyList<AdminIndicatorTask>> ListIndicatorTasksAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => (await IndicatorGetAsync<ApiCollectionPage<AdminIndicatorTask>>(lease, "/api/v2/admin/tasks?include_hidden=true", ct)
            .ConfigureAwait(false))?.Items ?? [];

    /// <summary>Full admin library read (<c>/libraries</c>, not the user projection), used to name scans.</summary>
    public async Task<IReadOnlyList<AdminIndicatorLibrary>> ListIndicatorLibrariesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => (await IndicatorGetAsync<ApiCollectionPage<AdminIndicatorLibrary>>(lease, "/api/v2/libraries", ct)
            .ConfigureAwait(false))?.Items ?? [];

    /// <summary>
    /// The preparation queue, or null when the server has no queue (it answers
    /// 503); the indicator then hides that section rather than showing an error.
    /// </summary>
    public async Task<AdminIndicatorPreparationList?> ListIndicatorPreparationsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        try
        {
            return await IndicatorGetAsync<AdminIndicatorPreparationList>(lease,
                $"/api/v2/admin/downloads/preparations?limit={IndicatorPreparationsLimit}", ct).ConfigureAwait(false);
        }
        catch (ApiException error) when (error.StatusCode == 503)
        {
            lease.ThrowIfRetired();
            return null;
        }
    }

    public Task<AdminIndicatorBuildInfo> GetIndicatorBuildInfoAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => IndicatorGetAsync<AdminIndicatorBuildInfo>(lease, "/api/v2/admin/system/build", ct);

    public Task<AdminIndicatorServerStatus> GetIndicatorServerStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
        => IndicatorGetAsync<AdminIndicatorServerStatus>(lease, "/api/v2/admin/server/status", ct);

    /// <summary>
    /// Parses a <c>scans</c> snapshot (an array). Rows without id or status are
    /// skipped; library ids are kept as text so the bridge's numeric ids match
    /// the REST library's string ids.
    /// </summary>
    public static IReadOnlyList<AdminIndicatorScanRun> ParseIndicatorScanRuns(JsonElement snapshot)
    {
        if (snapshot.ValueKind != JsonValueKind.Array) return [];
        var runs = new List<AdminIndicatorScanRun>();
        foreach (var item in snapshot.EnumerateArray())
            if (ParseIndicatorScanRun(item) is { } run) runs.Add(run);
        return runs;
    }

    public static AdminIndicatorScanRun? ParseIndicatorScanRun(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = IndicatorText(item, "id");
        var status = IndicatorText(item, "status");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(status)) return null;
        AdminIndicatorScanResult? result = null;
        if (item.TryGetProperty("result", out var raw) && raw.ValueKind == JsonValueKind.Object)
            result = new AdminIndicatorScanResult(IndicatorNumber(raw, "total_files"), IndicatorNumber(raw, "files_processed"),
                IndicatorText(raw, "message"));
        return new AdminIndicatorScanRun(id, IndicatorText(item, "library_id") ?? "", IndicatorText(item, "mode") ?? "",
            IndicatorText(item, "path"), status, result);
    }

    private static string? IndicatorText(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static long? IndicatorNumber(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }
}
