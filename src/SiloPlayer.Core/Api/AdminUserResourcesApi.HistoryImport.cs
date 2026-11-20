using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminUserResourcesApi
{
    private const string Import = "/api/v2/admin/history-imports";
    private const string ImportSources = "/api/v2/admin/history-import-sources";
    public Task<JsonElement> ImportCapabilitiesAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => ReadAsync(owner, Import + "/capabilities", ct);
    public Task<AdminResourcePage> ListImportSourcesAsync(MonitorAuthorityLease owner, string? cursor = null, CancellationToken ct = default) => PageAsync(owner, ImportSources, cursor, Id, ct);
    public Task<AdminResourceDocument> GetImportSourceAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default) => DocumentAsync(owner, ImportSources + "/" + NumericId(id), id, ct);
    public async Task<AdminResourceDocument> CreateImportSourceAsync(MonitorAuthorityLease owner, JsonElement body, CancellationToken ct = default)
    {
        ValidateSource(body, true);
        var result = await SendAsync(owner, HttpMethod.Post, ImportSources, body, ct: ct).ConfigureAwait(false);
        string id; try { id = NumericId(result.Body.Text("id")); } catch { throw new AdminResourceOutcomeUnknownException(); }
        if (!Services.Admin.AdminValidators.IsStrong(result.ETag)) throw new AdminResourceOutcomeUnknownException();
        return Bind(owner, result, ImportSources + "/" + id);
    }
    public Task<AdminResourceDocument> SaveImportSourceAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, JsonElement body, CancellationToken ct = default)
    {
        var path = ImportSources + "/" + NumericId(editor.Value.Text("id")); editor.RequireOwner(owner, path); ValidateSource(body, false);
        if (editor.Value.Flag("has_admin_token") && (body.Text("base_url", editor.Value.Text("base_url")) != editor.Value.Text("base_url") || body.Text("system_id", editor.Value.Text("system_id")) != editor.Value.Text("system_id")) && body.Member("admin_token") == null)
            throw new ArgumentException("Replace or explicitly clear the saved credential when changing the source address.");
        return SaveDocumentAsync(owner, editor, path, body, ct);
    }
    public Task DeleteImportSourceAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, CancellationToken ct = default)
    {
        var path = ImportSources + "/" + NumericId(editor.Value.Text("id")); editor.RequireOwner(owner, path); return SendAsync(owner, HttpMethod.Delete, path, etag: editor.ETag, ct: ct);
    }
    public async Task<AdminResourceDocument> SetImportTokenAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, string token, CancellationToken ct = default)
    {
        var id = NumericId(editor.Value.Text("id")); editor.RequireOwner(owner, ImportSources + "/" + id);
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64 * 1024) throw new ArgumentException("Enter the replacement credential.");
        var result = await SendAsync(owner, HttpMethod.Put, Import + "/sources/" + id + "/token", new { token }, editor.ETag, ct).ConfigureAwait(false);
        if (result.Body.Text("id") != id || !Services.Admin.AdminValidators.IsStrong(result.ETag)) throw new AdminResourceOutcomeUnknownException();
        return Bind(owner, result, editor.Path);
    }
    public async Task ClearImportTokenAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, CancellationToken ct = default)
    {
        var id = NumericId(editor.Value.Text("id")); editor.RequireOwner(owner, ImportSources + "/" + id);
        await SendAsync(owner, HttpMethod.Delete, Import + "/sources/" + id + "/token", etag: editor.ETag, ct: ct).ConfigureAwait(false);
    }
    public async Task<string> ImportPlexLoginAsync(MonitorAuthorityLease owner, string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new ArgumentException("Enter Plex sign-in details.");
        var result = await SendAsync(owner, HttpMethod.Post, Import + "/plex/login", new { username, password }, ct: ct).ConfigureAwait(false);
        var token = result.Body.Text("token"); if (token.Length == 0) throw new AdminResourceOutcomeUnknownException(); return token;
    }
    public Task<AdminResourcePage> ListImportMappingsAsync(MonitorAuthorityLease owner, string source, string? cursor = null, CancellationToken ct = default) =>
        PageAsync(owner, Import + "/mappings?source_id=" + NumericId(source), cursor, Id, ct);
    public Task<AdminResourceDocument> GetImportMappingAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default) => DocumentAsync(owner, Import + "/mappings/" + NumericId(id), id, ct);
    public async Task<JsonElement> CreateImportMappingAsync(MonitorAuthorityLease owner, string source, string external, string externalName, string user, string profile, CancellationToken ct = default)
    {
        NumericId(source); NumericId(user); Segment(external); Segment(profile);
        var result = await SendAsync(owner, HttpMethod.Post, Import + "/mappings", new { source_id = source, external_user_id = external, external_user_name = externalName, silo_user_id = user, silo_profile_id = profile }, ct: ct).ConfigureAwait(false);
        if (result.Body.Text("source_id") != source || result.Body.Text("external_user_id") != external || result.Body.Text("silo_user_id") != user || result.Body.Text("silo_profile_id") != profile) throw new AdminResourceOutcomeUnknownException();
        return result.Body;
    }
    public Task DeleteImportMappingAsync(MonitorAuthorityLease owner, AdminResourceDocument editor, CancellationToken ct = default)
    {
        var path = Import + "/mappings/" + NumericId(editor.Value.Text("id")); editor.RequireOwner(owner, path); return SendAsync(owner, HttpMethod.Delete, path, etag: editor.ETag, ct: ct);
    }
    public Task<AdminResourcePage> DiscoverImportUsersAsync(MonitorAuthorityLease owner, string source, string? cursor = null, CancellationToken ct = default) =>
        PageAsync(owner, Import + "/sources/" + NumericId(source) + "/users", cursor, Id, ct);
    public static object? RunBody(string sourceType, int? lookbackHours, bool supported, int maximum)
    {
        if (lookbackHours == null) return null;
        if (sourceType != "jellyfin" || !supported || lookbackHours < 1 || lookbackHours > Math.Min(maximum, 8760)) throw new ArgumentException("Lookback requires a supported Jellyfin source and an allowed whole-hour window.");
        return new { lookback_hours = lookbackHours.Value };
    }
    public async Task<AdminImportReceipt> StartImportAsync(MonitorAuthorityLease owner, string mapping, string sourceType, int? hours, bool lookbackSupported, int maximum, CancellationToken ct = default)
    {
        var body = RunBody(sourceType, hours, lookbackSupported, maximum);
        var result = await SendAsync(owner, HttpMethod.Post, Import + "/mappings/" + NumericId(mapping) + "/run", body, ct: ct).ConfigureAwait(false);
        return Receipt(result, requireLocation: true);
    }
    public async Task<JsonElement> BulkImportAsync(MonitorAuthorityLease owner, string source, string sourceType, int? hours, bool lookbackSupported, int maximum, CancellationToken ct = default)
    {
        var body = RunBody(sourceType, hours, lookbackSupported, maximum);
        var result = await SendAsync(owner, HttpMethod.Post, Import + "/sources/" + NumericId(source) + "/bulk-run", body, ct: ct).ConfigureAwait(false);
        var outcomes = result.Body.Rows("outcomes");
        if (result.Body.Member("outcomes")?.ValueKind != JsonValueKind.Array || outcomes.Count > 200 || outcomes.Select(x => x.Text("mapping_id")).Distinct().Count() != outcomes.Count ||
            outcomes.Any(x => string.IsNullOrEmpty(x.Text("mapping_id")) || x.Text("status") is not ("accepted" or "active" or "failed")) ||
            outcomes.Count(x => x.Text("status") == "accepted") != result.Body.Number("accepted", -1) || outcomes.Count(x => x.Text("status") == "active") != result.Body.Number("active", -1) || outcomes.Count(x => x.Text("status") == "failed") != result.Body.Number("failed", -1)) throw new AdminResourceOutcomeUnknownException();
        foreach (var outcome in outcomes.Where(x => x.Text("status") is "accepted" or "active"))
            if (outcome.Member("run") is not { ValueKind: JsonValueKind.Object } run || outcome.Text("location") != RunPath(run.Text("id"))) throw new AdminResourceOutcomeUnknownException();
        return result.Body;
    }
    public Task<AdminResourcePage> ListImportRunsAsync(MonitorAuthorityLease owner, string source, string? cursor = null, CancellationToken ct = default) =>
        PageAsync(owner, Import + "/runs?source_id=" + NumericId(source), cursor, Id, ct, 50);
    public async Task<AdminImportReceipt> GetImportRunAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default)
    {
        var result = await SendAsync(owner, HttpMethod.Get, RunPath(id), ct: ct).ConfigureAwait(false);
        if (result.Body.Text("id") != id) throw new InvalidDataException("Import run identity changed.");
        return Receipt(result, false);
    }
    public async Task<AdminImportReceipt> CancelImportRunAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default)
    {
        var result = await SendAsync(owner, HttpMethod.Post, RunPath(id) + "/cancel", ct: ct).ConfigureAwait(false);
        if (result.Body.Text("id") != id) throw new AdminResourceOutcomeUnknownException(); return Receipt(result, true);
    }
    private static string RunPath(string id) => Import + "/runs/" + Segment(id);
    private static AdminImportReceipt Receipt(AdminResourceResponse response, bool requireLocation)
    {
        string path;
        try { path = RunPath(response.Body.Text("id")); } catch { throw new AdminResourceOutcomeUnknownException(); }
        if ((requireLocation || response.Location != null) && response.Location != path || response.Body.Member("terminal")?.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || response.Body.Member("cancelable")?.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new AdminResourceOutcomeUnknownException();
        return new(response.Body, path, response.RetryAfter);
    }
    private static void ValidateSource(JsonElement body, bool creating)
    {
        if (string.IsNullOrWhiteSpace(body.Text("name")) || !Uri.TryCreate(body.Text("base_url"), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            throw new ArgumentException("Enter a name and an HTTP(S) source address without credentials, query or fragment.");
        if (creating && body.Text("source_type") is not ("emby" or "jellyfin" or "plex")) throw new ArgumentException("Choose Jellyfin, Emby or Plex.");
    }
}
