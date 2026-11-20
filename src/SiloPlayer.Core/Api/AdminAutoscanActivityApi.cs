using System.Globalization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public sealed class AdminAutoscanActivityApi(SiloApiClient client)
{
    public const int MaxHistoryPages = 100;
    public const int MaxActiveScans = 500;
    /// <summary>The server lists every run an event created; the inspector pages them client-side, so the bound is the 4 MiB transport cap plus this count.</summary>
    public const int MaxEventRuns = 5000;
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    public static readonly IReadOnlyList<int> PageSizes = [25, 50, 100];
    public static bool EventStatus(string value) => value is "running" or "success" or "error" or "unresolved";
    public static bool ScanStatus(string value) => value is "accepted" or "running" or "completed" or "failed" or "cancelled";
    public static bool NumericId(string? value) => !string.IsNullOrEmpty(value) && value[0] is >= '1' and <= '9' &&
        value.All(char.IsAsciiDigit) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed <= 9007199254740991;

    public static string HistoryPath(AdminAutoscanHistory history, AdminAutoscanActivityFilter filter, string? cursor = null)
    {
        if (filter.Limit is < 1 or > 200 || filter.Query.Length > 1024 ||
            filter.Status.Length > 0 && !(history == AdminAutoscanHistory.Scans ? ScanStatus(filter.Status) : EventStatus(filter.Status)))
            throw new ArgumentException("Scan history page is outside the supported range.");
        var path = AdminPaging.WithPage("/api/v2/admin/autoscan/" + (history == AdminAutoscanHistory.Scans ? "scans" : "events"), filter.Limit, cursor);
        if (filter.Status.Length > 0) path += "&status=" + Uri.EscapeDataString(filter.Status);
        if (filter.Query.Trim().Length > 0) path += "&q=" + Uri.EscapeDataString(filter.Query.Trim());
        return path;
    }

    public async Task<AdminAutoscanActivityWindow<AdminAutoscanActivityScan>> ReadScansAsync(MonitorAuthorityLease lease,
        AdminAutoscanActivityFilter filter, string? cursor = null, CancellationToken ct = default)
    {
        var page = await client.AdminLibrarySendAsync<AdminAutoscanActivityPage<AdminAutoscanActivityScan>>(lease, HttpMethod.Get,
            HistoryPath(AdminAutoscanHistory.Scans, filter, cursor), ct: ct).ConfigureAwait(false);
        var window = Validate(page, filter.Limit, cursor, row => row?.Id);
        foreach (var scan in window.Items)
        {
            ValidateRun(scan);
            if (scan.AutoscanEventId != null && !NumericId(scan.AutoscanEventId) ||
                !string.IsNullOrEmpty(scan.EventStatus) && !EventStatus(scan.EventStatus)) throw Invalid();
        }
        return window;
    }

    public async Task<AdminAutoscanActivityWindow<AdminAutoscanActivityEvent>> ReadEventsAsync(MonitorAuthorityLease lease,
        AdminAutoscanActivityFilter filter, string? cursor = null, CancellationToken ct = default)
    {
        var page = await client.AdminLibrarySendAsync<AdminAutoscanActivityPage<AdminAutoscanActivityEvent>>(lease, HttpMethod.Get,
            HistoryPath(AdminAutoscanHistory.Polls, filter, cursor), ct: ct).ConfigureAwait(false);
        var window = Validate(page, filter.Limit, cursor, row => row?.Id);
        foreach (var poll in window.Items)
        {
            if (!NumericId(poll.Id) || !EventStatus(poll.Status) ||
                !string.IsNullOrEmpty(poll.DeliveryMode) && poll.DeliveryMode is not ("poll" or "webhook") ||
                poll.ScanRuns == null || poll.ScanRuns.Count > MaxEventRuns || poll.Changes?.Count > 50 ||
                new[] { poll.DurationMs, poll.ChangesReturned, poll.ChangesResolved, poll.TargetsClaimed, poll.ScansCreated, poll.ScansReused, poll.ScansSuppressed }.Any(count => count < 0)) throw Invalid();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var run in poll.ScanRuns)
            {
                ValidateRun(run);
                if (!ids.Add(run.Id)) throw Invalid();
            }
            foreach (var change in poll.Changes ?? [])
                if (change == null || change.LibraryId != null && !NumericId(change.LibraryId)) throw Invalid();
        }
        return window;
    }

    private static AdminAutoscanActivityWindow<T> Validate<T>(AdminAutoscanActivityPage<T>? page, int limit, string? cursor, Func<T, string?> identity)
    {
        if (page?.Page == null || page.Items == null || page.Total is < 0 or > 9007199254740991 || page.Items.Count > limit ||
            page.Page.HasMore && (!AdminPaging.IsValidCursor(page.Page.NextCursor) || page.Page.NextCursor == cursor)) throw Invalid();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in page.Items)
        {
            var id = identity(item);
            if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw Invalid();
        }
        return new(page.Items.AsReadOnly(), page.Total, page.Page.HasMore ? page.Page.NextCursor : null);
    }

    private static void ValidateRun(AdminAutoscanActivityRun? run)
    {
        if (run == null || string.IsNullOrEmpty(run.Id) || !NumericId(run.LibraryId) ||
            run.Mode is not ("library" or "subtree" or "file") || !ScanStatus(run.Status)) throw Invalid();
        if (run.Result is { } result && new[] { result.New, result.Updated, result.Unchanged, result.Missing,
            result.MissingSkippedProtected, result.FilesDeleted, result.ItemsDeleted, result.MembershipsRemoved, result.Errors, result.Skipped }.Any(count => count < 0)) throw Invalid();
    }

    public async Task<AdminAutoscanActivityStatus> ReadStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var status = await client.AdminLibrarySendAsync<AdminAutoscanActivityStatus>(lease, HttpMethod.Get, "/api/v2/admin/autoscan/status", ct: ct).ConfigureAwait(false);
        if (status.Sources == null || status.RunningPolls == null || status.Sources.Count > 1000 || status.RunningPolls.Count > 500 ||
            status.ActiveScans < 0 || status.AcceptedScans < 0 || status.RunningScans < 0) throw Invalid();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var poll in status.RunningPolls)
            if (poll == null || !NumericId(poll.Id) || !ids.Add(poll.Id) || poll.ElapsedMs < 0) throw Invalid();
        return status;
    }

    public async Task<AdminAutoscanActivityLabels> ReadLabelsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var libraries = await new AdminLibrariesApi(client).ListAsync(lease, ct).ConfigureAwait(false);
        var sources = await Lookup<AdminAutoscanActivitySource>(lease, "sources", row => row.Id, ct).ConfigureAwait(false);
        var connections = await Lookup<AdminAutoscanActivityConnection>(lease, "connections", row => row.Id, ct).ConfigureAwait(false);
        var plugins = await Lookup<AdminAutoscanActivityPlugin>(lease, "scan-source-plugins", row => row.PluginId + ":" + row.CapabilityId, ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return new(libraries.Items.ToDictionary(row => row.Id, row => row.Name), sources.Items.ToDictionary(row => row.Id),
            connections.Items.ToDictionary(row => row.Id, row => row.Name), plugins.Items.ToDictionary(row => row.PluginId + ":" + row.CapabilityId, row => row.DisplayName),
            !libraries.IsComplete || !sources.IsComplete || !connections.IsComplete || !plugins.IsComplete);
    }

    private Task<AdminListRead<T>> Lookup<T>(MonitorAuthorityLease lease, string name, Func<T, string?> id, CancellationToken ct) =>
        AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("autoscan-activity-" + name), new AdminWalkLimits(100, 5, 500),
            async (cursor, limit, token) => await client.AdminLibrarySendAsync<ApiCollectionPage<T>>(lease, HttpMethod.Get,
                AdminPaging.WithPage("/api/v2/admin/autoscan/" + name, limit, cursor), ct: token).ConfigureAwait(false), id, ct);

    public async Task CancelAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        if (!NumericId(libraryId)) throw new ArgumentException("Invalid library identity.", nameof(libraryId));
        var receipt = await client.AdminLibrarySendAsync<AdminScanCancelReceipt>(lease, HttpMethod.Post, "/api/v2/scan/cancel",
            new Dictionary<string, object?> { ["library_id"] = libraryId }, ct).ConfigureAwait(false);
        if (receipt.LibraryId != libraryId || receipt.Cancelled < 0) throw Invalid();
    }

    private static InvalidDataException Invalid() => new("Invalid autoscan activity response. Reload before continuing.");
}
