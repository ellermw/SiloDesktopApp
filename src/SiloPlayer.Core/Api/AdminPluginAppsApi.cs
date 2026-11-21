using System.Globalization;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>Plugin-authored label and route; upstream uses Puzzle for every Admin app.</summary>
public sealed record AdminPluginApp(int InstallationId, string PluginId, string RoutePath, string Label)
{
    public string Icon => "puzzle";
}

/// <summary>Admin-only inventory, independent of the user-filtered settings inventory.</summary>
public sealed class AdminPluginAppsApi(SiloApiClient client)
{
    private const int PageLimit = 100;
    private const int PageCap = 100;
    private const int MaxApps = 2048;

    public async Task<IReadOnlyList<AdminPluginApp>> GetAppsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var apps = new List<AdminPluginApp>();
        var ids = new HashSet<int>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var pageNumber = 0; pageNumber < PageCap; pageNumber++)
        {
            var path = "/api/v2/admin/plugins/installations?limit=" + PageLimit.ToString(CultureInfo.InvariantCulture);
            if (cursor != null) path += "&cursor=" + Uri.EscapeDataString(cursor);
            var page = (await client.MonitorLogsSendAsync<InventoryPage>(lease, HttpMethod.Get, path,
                ct: timeout.Token, maxBytes: 4 * 1024 * 1024).ConfigureAwait(false)).Body;
            lease.ThrowIfRetired();
            timeout.Token.ThrowIfCancellationRequested();
            if (page.Items == null || page.Page == null || page.Items.Count > PageLimit)
                throw new InvalidDataException("Invalid plugin installation page.");
            foreach (var installation in page.Items)
            {
                if (installation == null || installation.Id <= 0 || !ids.Add(installation.Id) ||
                    string.IsNullOrWhiteSpace(installation.PluginId) || installation.PluginId.Length > 256 ||
                    installation.PluginId.Any(char.IsControl) || installation.Routes?.Count > 256)
                    throw new InvalidDataException("Invalid plugin installation inventory.");
                if (!installation.Enabled) continue;
                foreach (var route in installation.Routes ?? [])
                {
                    if (route == null || !route.Navigable || route.NavigationKind != "admin" || !SafePath(route.Path, pattern: true)) continue;
                    var label = string.IsNullOrEmpty(route.NavigationLabel) ? installation.PluginId : route.NavigationLabel;
                    if (label.Length > 256 || label.Any(char.IsControl)) continue;
                    if (apps.Any(app => app.InstallationId == installation.Id && app.RoutePath == route.Path)) continue;
                    if (apps.Count == MaxApps) throw new InvalidDataException("Too many plugin application routes.");
                    apps.Add(new AdminPluginApp(installation.Id, installation.PluginId, route.Path, label));
                }
            }
            if (!page.Page.HasMore) return apps.AsReadOnly();
            var next = page.Page.NextCursor;
            if (page.Items.Count == 0 || string.IsNullOrWhiteSpace(next) || next.Length > 8192 || !cursors.Add(next))
                throw new InvalidDataException("Invalid plugin installation continuation.");
            cursor = next;
        }
        throw new InvalidDataException("Plugin installation inventory exceeds the page limit.");
    }

    internal static bool SafePath(string? path, bool pattern = false)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 4096 || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Any(char.IsControl)) return false;
        var trimmed = pattern && path.EndsWith("/*", StringComparison.Ordinal) ? path[..^2] : path;
        if (trimmed.Contains('*')) return false;
        foreach (var segment in trimmed.Split('/'))
        {
            var decoded = Uri.UnescapeDataString(segment);
            if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\') || decoded.Contains('%') || decoded.Any(char.IsControl)) return false;
        }
        return true;
    }

    private sealed class InventoryPage
    {
        public List<Installation?>? Items { get; set; }
        public ApiPageInfo? Page { get; set; }
    }
    private sealed class Installation
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Id { get; set; }
        public string PluginId { get; set; } = "";
        public bool Enabled { get; set; }
        public List<PluginRoute?>? Routes { get; set; }
    }
}
