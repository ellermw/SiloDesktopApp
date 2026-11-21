using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>
/// One WebUI <c>buildAdminPluginNavItems</c> entry: an enabled installation's navigable
/// <c>admin</c> route. Label and plugin id are plugin data, already reduced to bounded plain text.
/// Upstream uses the Puzzle icon for every Admin plugin app.
/// </summary>
public sealed record AdminPluginApp(int InstallationId, string PluginId, string RoutePath, string Label)
{
    public string Icon => "puzzle";
}

/// <summary>
/// The <c>silo_plugin_access</c> launch cookie exactly as the server issued it. The value is a
/// short-lived plugin access token: it is handed only to the plugin web view's cookie store
/// (HttpOnly, so plugin script cannot read it) and is never logged or formatted.
/// </summary>
public sealed class AdminPluginLaunchCookie
{
    internal AdminPluginLaunchCookie(string value, string path, int maxAgeSeconds, bool secure)
    { Value = value; Path = path; MaxAgeSeconds = maxAgeSeconds; Secure = secure; }

    public const string CookieName = "silo_plugin_access";
    public string Name => CookieName;
    public string Value { get; }
    public string Path { get; }
    public int MaxAgeSeconds { get; }
    public bool Secure { get; }
    public override string ToString() => CookieName + " (redacted)";
}

/// <summary>Plugin-supplied text shown in native UI: plain, single line, bounded, no control or bidi formatting characters.</summary>
public static class AdminPluginText
{
    public const int MaxLabelLength = 80;

    public static string Plain(string? text, int maxLength = MaxLabelLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(Math.Min(text.Length, maxLength * 2));
        var space = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            // Tabs and line breaks become one space; the label stays a single line.
            if (Rune.IsWhiteSpace(rune) || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                space = builder.Length > 0;
                continue;
            }
            // Format (Cf) covers every bidi embedding/override/isolate/mark and zero-width character.
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned) continue;
            if (space) { builder.Append(' '); space = false; }
            builder.Append(rune.ToString());
            if (builder.Length > maxLength * 4) break;
        }
        var plain = builder.ToString();
        var elements = StringInfo.GetTextElementEnumerator(plain);
        var length = 0;
        var cut = -1;
        while (elements.MoveNext())
        {
            if (++length > maxLength) { cut = elements.ElementIndex; break; }
        }
        return cut < 0 ? plain : plain[..cut].TrimEnd() + "…";
    }
}

/// <summary>
/// The Admin "Plugin Apps" group: the admin installations inventory (not the user-filtered
/// settings inventory) and the WebUI plugin launch that precedes opening a plugin page.
/// </summary>
public sealed class AdminPluginAppsApi
{
    private const int PageLimit = 100;
    private const int PageCap = 100;
    private const int MaxRoutesPerInstallation = 256;
    private const int MaxApps = 2048;
    private readonly SiloApiClient _client;
    private readonly HttpClient _launchTransport;

    public AdminPluginAppsApi(SiloApiClient client) : this(client, null) { }

    /// <summary>A test handler is owned by its fixture, never by this service.</summary>
    internal AdminPluginAppsApi(SiloApiClient client, HttpMessageHandler? launchHandler)
    {
        _client = client;
        if (launchHandler != null) { _launchTransport = new HttpClient(launchHandler, disposeHandler: false); return; }
        var handler = SiloHttpClientFactory.CreateHandler();
        // The launch credential is copied into the isolated plugin web view only; the
        // API client's own cookie jar never stores it.
        handler.UseCookies = false;
        _launchTransport = new HttpClient(handler) { DefaultRequestVersion = HttpVersion.Version20, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
    }

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
            var page = (await _client.MonitorLogsSendAsync<InventoryPage>(lease, HttpMethod.Get, path,
                ct: timeout.Token, maxBytes: 4 * 1024 * 1024).ConfigureAwait(false)).Body;
            lease.ThrowIfRetired();
            timeout.Token.ThrowIfCancellationRequested();
            if (page.Items == null || page.Page == null || page.Items.Count > PageLimit)
                throw new InvalidDataException("Invalid plugin installation page.");
            foreach (var installation in page.Items)
            {
                // WebUI fetchPluginInstallations rejects a duplicate installation outright.
                if (installation == null || installation.Id <= 0 || !ids.Add(installation.Id))
                    throw new InvalidDataException("Invalid plugin installation inventory.");
                if (!installation.Enabled) continue;
                var pluginId = AdminPluginText.Plain(installation.PluginId);
                foreach (var route in (installation.Routes ?? []).Take(MaxRoutesPerInstallation))
                {
                    if (route == null || !route.Navigable || route.NavigationKind != "admin") continue;
                    // A route the desktop cannot address without leaving the installation's
                    // content space is never offered (the WebUI composes it verbatim).
                    if (!SafePath(route.Path, pattern: true)) continue;
                    var label = AdminPluginText.Plain(route.NavigationLabel);
                    if (label.Length == 0) label = pluginId;
                    if (label.Length == 0) continue;
                    if (apps.Any(app => app.InstallationId == installation.Id && app.RoutePath == route.Path)) continue;
                    if (apps.Count == MaxApps) throw new InvalidDataException("Too many plugin application routes.");
                    apps.Add(new AdminPluginApp(installation.Id, pluginId, route.Path, label));
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

    /// <summary>
    /// <c>POST /api/v2/auth/plugin-launch</c>, then the issued cookie, validated so it can never be
    /// broader than the server's plugin-content parent path on the connected host.
    /// </summary>
    public async Task<AdminPluginLaunchCookie> LaunchAsync(MonitorAuthorityLease lease, AdminPluginContentScope scope, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var cookies = await _client.AdminPluginLaunchAsync(lease, _launchTransport, timeout.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        foreach (var header in cookies)
            if (ParseLaunchCookie(header, scope) is { } cookie) return cookie;
        throw new InvalidDataException("The plugin launch did not issue a usable plugin access cookie.");
    }

    /// <summary>The launch cookie, or null when the header is another cookie or would widen its scope.</summary>
    internal static AdminPluginLaunchCookie? ParseLaunchCookie(string? header, AdminPluginContentScope scope)
    {
        if (string.IsNullOrEmpty(header) || header.Length > 16 * 1024) return null;
        var parts = header.Split(';');
        var pair = parts[0];
        var equals = pair.IndexOf('=');
        if (equals <= 0 || !string.Equals(pair[..equals].Trim(), AdminPluginLaunchCookie.CookieName, StringComparison.Ordinal)) return null;
        var value = pair[(equals + 1)..].Trim();
        if (value.Length is 0 or > 8192 || value.Any(c => c <= 0x20 || c >= 0x7f || c is '"' or ',' or ';' or '\\')) return null;
        string? path = null;
        int? maxAge = null;
        var secure = false;
        foreach (var raw in parts.Skip(1))
        {
            var attribute = raw.Trim();
            var split = attribute.IndexOf('=');
            var name = (split < 0 ? attribute : attribute[..split]).Trim();
            var argument = split < 0 ? "" : attribute[(split + 1)..].Trim();
            if (name.Equals("Path", StringComparison.OrdinalIgnoreCase)) path = argument;
            else if (name.Equals("Max-Age", StringComparison.OrdinalIgnoreCase))
                maxAge = int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : -1;
            else if (name.Equals("Secure", StringComparison.OrdinalIgnoreCase)) secure = true;
            // The official cookie is host-only. A Domain attribute would widen it to other hosts.
            else if (name.Equals("Domain", StringComparison.OrdinalIgnoreCase)) return null;
        }
        if (path == null || !scope.IsLaunchCookiePath(path)) return null;
        if (maxAge is not { } age || age <= 0 || age > 3600) return null;
        return new AdminPluginLaunchCookie(value, path, age, secure);
    }

    internal static bool SafePath(string? path, bool pattern = false)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 4096 || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        var trimmed = pattern && path.EndsWith("/*", StringComparison.Ordinal) ? path[..^2] : path;
        if (trimmed.Contains('*')) return false;
        foreach (var segment in trimmed.Split('/'))
        {
            // Any percent escape is refused: encoded separators, dots or a second decoding
            // pass could otherwise address another installation or a server API.
            if (segment.Contains('%') || segment is "." or "..") return false;
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
