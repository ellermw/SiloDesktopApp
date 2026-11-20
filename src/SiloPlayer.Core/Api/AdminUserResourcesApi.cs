using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Manage › Users' three resource leaves. Official 217ff25; independent of the Users page.</summary>
public sealed partial class AdminUserResourcesApi(SiloApiClient client)
{
    public const int PageSize = 100;
    public const int MaxConfigurationRows = 2000;
    private const int MaxUnpagedRows = 2000;
    private const string Groups = "/api/v2/admin/access-groups";
    private const string Users = "/api/v2/admin/users";

    public Task<JsonElement> GroupCapabilitiesAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => ReadAsync(owner, Users + "/capabilities", ct);
    public Task<AdminResourcePage> ListGroupsAsync(MonitorAuthorityLease owner, string? cursor = null, CancellationToken ct = default) => PageAsync(owner, Groups, cursor, Id, ct);
    public Task<AdminResourceDocument> GetGroupAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default) => DocumentAsync(owner, Groups + "/" + NumericId(id), id, ct);
    public async Task<JsonElement> CreateGroupAsync(MonitorAuthorityLease owner, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 512) throw new ArgumentException("Enter a group name.");
        var result = await SendAsync(owner, HttpMethod.Post, Groups, new { name = name.Trim() }, ct: ct).ConfigureAwait(false);
        try { NumericId(result.Body.Text("id")); } catch { throw new AdminResourceOutcomeUnknownException(); }
        return result.Body;
    }
    public Task<AdminResourceDocument> SaveGroupAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, JsonElement body, CancellationToken ct = default)
    {
        var path = Groups + "/" + NumericId(editor.Value.Text("id")); editor.RequireOwner(owner, path);
        ValidateGroup(body);
        return SaveDocumentAsync(owner, editor, path, body, ct);
    }
    public Task DeleteGroupAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, CancellationToken ct = default)
    {
        var path = Groups + "/" + NumericId(editor.Value.Text("id")); editor.RequireOwner(owner, path);
        if (editor.Value.Flag("is_default")) throw new InvalidOperationException("Make another group the default before deleting this group.");
        return SendAsync(owner, HttpMethod.Delete, path, etag: editor.ETag, ct: ct);
    }
    public Task<AdminResourceDocument> GetGroupLimitAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default) =>
        DocumentAsync(owner, "/api/v2/admin/request-groups/" + NumericId(id) + "/limit", id, ct, "group_id");
    public Task<AdminResourceDocument> SaveGroupLimitAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, JsonElement body, CancellationToken ct = default)
    {
        var path = "/api/v2/admin/request-groups/" + NumericId(editor.Value.Text("group_id")) + "/limit";
        editor.RequireOwner(owner, path);
        if (body.Text("limit_mode") is not ("inherit" or "custom" or "unlimited") || body.Text("approval_mode") is not ("inherit" or "manual" or "auto") ||
            body.Text("limit_mode") == "custom" && (body.Number("max_requests", -1) < 0 || body.Number("window_days") < 1)) throw new ArgumentException("Review request limits.");
        return SaveDocumentAsync(owner, editor, path, body, ct, "group_id");
    }
    // Member assignment is a minimal account projection for group workflows, never an alternate Users editor.
    public Task<AdminResourcePage> ListAccountsAsync(MonitorAuthorityLease owner, string? cursor = null, CancellationToken ct = default) => PageAsync(owner, Users, cursor, Id, ct);
    public Task<AdminResourcePage> ListProfilesAsync(MonitorAuthorityLease owner, string account, string? cursor = null, CancellationToken ct = default) => PageAsync(owner, Users + "/" + NumericId(account) + "/profiles", cursor, Id, ct);
    public Task<AdminResourceDocument> GetMemberAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default) => DocumentAsync(owner, Users + "/" + NumericId(id), id, ct);
    public async Task MoveMemberAsync(MonitorAuthorityLease owner, JsonElement confirmed, AdminResourceDocument fresh, string groupId, CancellationToken ct = default)
    {
        var path = Users + "/" + NumericId(confirmed.Text("id")); fresh.RequireOwner(owner, path);
        if (fresh.Value.Text("role") == "admin" || fresh.Value.Text("access_group_id") != confirmed.Text("access_group_id") ||
            fresh.Value.Member("requests_allowed")?.GetRawText() != confirmed.Member("requests_allowed")?.GetRawText())
            throw new InvalidOperationException("The account policy changed. Review the move again.");
        await SendAsync(owner, HttpMethod.Put, path, new { access_group_id = NumericId(groupId) }, fresh.ETag, ct).ConfigureAwait(false);
    }
    public Task<AdminResourcePage> ListLibrariesAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => PageAsync(owner, "/api/v2/libraries", null, Id, ct, 0);
    public Task<JsonElement> RequestDefaultsAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => ReadAsync(owner, "/api/v2/admin/request-settings", ct);
    public async Task<(bool DownloadsOff, bool ConversionOff)> DownloadSwitchesAsync(MonitorAuthorityLease owner)
    {
        var settings = await new AdminServerSettingsApi(client).GetEffectiveAsync(owner, ["download.enabled", "download.transcode_enabled"], [] ).ConfigureAwait(false);
        return (settings.Values.GetValueOrDefault("download.enabled") != "true", settings.Values.GetValueOrDefault("download.transcode_enabled") != "true");
    }

    internal async Task<AdminResourcePage> PageAsync(MonitorAuthorityLease owner, string path, string? cursor, Func<JsonElement, string> identity, CancellationToken ct, int limit = PageSize)
    {
        var body = await ReadAsync(owner, limit > 0 ? AdminPaging.WithPage(path, limit, cursor) : path, ct).ConfigureAwait(false);
        if (body.Member("items") is not { ValueKind: JsonValueKind.Array } items || body.Member("page") is not { ValueKind: JsonValueKind.Object } page ||
            page.Member("has_more")?.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Invalid resource page.");
        var rows = items.EnumerateArray().ToArray();
        if (rows.Length > (limit > 0 ? limit : MaxUnpagedRows)) throw new InvalidDataException("Oversized resource page.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = identity(row);
            if (string.IsNullOrWhiteSpace(id) || !identities.Add(id)) throw new InvalidDataException("Invalid or duplicate resource identity.");
        }
        var next = page.Text("next_cursor");
        if (page.Flag("has_more") ? !AdminPaging.IsValidCursor(next) || next == cursor : next.Length > 0)
            throw new InvalidDataException("Invalid resource continuation.");
        owner.ThrowIfRetired();
        return new(Array.AsReadOnly(rows), page.Flag("has_more") ? next : null, body.Number("revision", -1) is var revision && revision > 0 ? revision : null);
    }
    /// <summary>Small configuration set only. A cap/repeated continuation fails closed; never returns a subset for whole-set actions.</summary>
    public async Task<IReadOnlyList<JsonElement>> ConfigurationAsync(MonitorAuthorityLease owner, Func<string?, Task<AdminResourcePage>> fetch, int maxPages = 20, int maxRows = MaxConfigurationRows, string? tooManyMessage = null)
    {
        var result = new List<JsonElement>(); var cursors = new HashSet<string>(StringComparer.Ordinal); var identities = new HashSet<string>(StringComparer.Ordinal); string? cursor = null;
        for (var count = 0; count < maxPages; count++)
        {
            owner.ThrowIfRetired(); var page = await fetch(cursor).ConfigureAwait(false); owner.ThrowIfRetired();
            if (result.Count + page.Items.Count > maxRows) throw tooManyMessage != null ? new AdminResourceTooManyException(tooManyMessage) : new InvalidDataException("Configuration exceeds the desktop safety bound.");
            foreach (var row in page.Items)
            {
                var id = row.Text("id");
                if (id.Length == 0) id = row.Text("key").Length > 0 ? SettingIdentity(row) : row.Text("user_id") + "/" + row.Text("device_id");
                if (!identities.Add(id)) throw new InvalidDataException("Duplicate configuration identity across pages.");
                result.Add(row);
            }
            if (page.NextCursor == null) return result.AsReadOnly();
            if (!cursors.Add(page.NextCursor)) throw new InvalidDataException("Repeated configuration continuation.");
            cursor = page.NextCursor;
        }
        throw tooManyMessage != null ? new AdminResourceTooManyException(tooManyMessage) : new InvalidDataException("Configuration exceeds the desktop page bound.");
    }
    private async Task<AdminResourceDocument> DocumentAsync(MonitorAuthorityLease owner, string path, string expected, CancellationToken ct, string key = "id")
    {
        var response = await SendAsync(owner, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        if (response.Body.Text(key) != expected) throw new InvalidDataException("Resource identity changed.");
        return Bind(owner, response, path);
    }
    private async Task<AdminResourceDocument> SaveDocumentAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, string path, object body, CancellationToken ct, string key = "id")
    {
        editor.RequireOwner(owner, path);
        var response = await SendAsync(owner, HttpMethod.Put, path, body, editor.ETag, ct).ConfigureAwait(false);
        if (response.Body.Text(key) != editor.Value.Text(key) || !AdminValidators.IsStrong(response.ETag)) throw new AdminResourceOutcomeUnknownException();
        return Bind(owner, response, path);
    }
    private static AdminResourceDocument Bind(MonitorAuthorityLease owner, AdminResourceResponse response, string path) => new(response.Body, response.ETag, path, owner.Epoch, owner.AccountId) { Authority = owner.Owner };
    private async Task<JsonElement> ReadAsync(MonitorAuthorityLease owner, string path, CancellationToken ct) => (await SendAsync(owner, HttpMethod.Get, path, ct: ct).ConfigureAwait(false)).Body;
    private Task<AdminResourceResponse> SendAsync(MonitorAuthorityLease owner, HttpMethod method, string path, object? body = null, string? etag = null, CancellationToken ct = default) => client.AdminResourceSendAsync(owner, method, path, body, etag, ct);
    private static string Id(JsonElement row) => row.Text("id");
    public static string NumericId(string id)
    {
        if (id.Length is < 1 or > 19 || id[0] == '0' || id.Any(c => c is < '0' or > '9') || !long.TryParse(id, out var n) || n <= 0) throw new ArgumentException("Invalid resource identifier.");
        return id;
    }
    private static string Segment(string id, int max = 256)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > max || id is "." or ".." || id.Any(char.IsControl)) throw new ArgumentException("Invalid resource identifier.");
        return Uri.EscapeDataString(id);
    }
    private static void ValidateGroup(JsonElement body)
    {
        if (string.IsNullOrWhiteSpace(body.Text("name")) || body.Text("name").Length > 512) throw new ArgumentException("Enter a group name.");
        foreach (var key in new[] { "max_streams", "max_transcodes", "max_remote_stream_bitrate_kbps", "max_local_stream_bitrate_kbps" })
            if (body.Member(key) != null && body.Number(key, -1) < 0) throw new ArgumentException("Limits must be whole numbers of at least zero.");
        if (body.Member("max_profiles") != null && body.Number("max_profiles") < 1) throw new ArgumentException("At least one profile is required.");
        if (body.Member("library_ids") is { ValueKind: JsonValueKind.Array } ids) foreach (var id in ids.EnumerateArray()) NumericId(id.GetString() ?? "");
    }
}
