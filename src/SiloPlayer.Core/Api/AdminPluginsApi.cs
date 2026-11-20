using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Acting-admin plugin management, pinned to official 217ff25. No public host or provider-settings dependencies.</summary>
public sealed partial class AdminPluginsApi(SiloApiClient client)
{
    public const int MaxResponseBytes = 4 * 1024 * 1024;
    public const int PageSize = 100;
    public const int MaxPages = 10;
    public const long MaxUploadBytes = 256L * 1024 * 1024;
    public static JsonSerializerOptions WireOptions { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, MaxDepth = 48 };
    private const string Root = "/api/v2/admin/plugins/";
    private sealed class Envelope<T> { public List<T>? Items { get; set; } public Continuation? Page { get; set; } }
    private sealed class Continuation { [System.Text.Json.Serialization.JsonRequired] public bool HasMore { get; set; } public string? NextCursor { get; set; } }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(MonitorAuthorityLease lease, string collection, Func<T, string> identity, CancellationToken ct)
    {
        var rows = new List<T>(); var ids = new HashSet<string>(StringComparer.Ordinal); var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var path = Root + collection + "?limit=" + PageSize + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var response = (await client.AdminPluginSendAsync<Envelope<T>>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false)).Body;
            lease.ThrowIfRetired();
            if (response.Items == null || response.Page == null || response.Items.Count > PageSize) throw Invalid();
            foreach (var row in response.Items)
            {
                if (row == null) throw Invalid();
                string id;
                try { id = identity(row); } catch (ArgumentException) { throw Invalid(); }
                if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw Invalid();
                rows.Add(row);
            }
            var next = response.Page.NextCursor;
            if (!response.Page.HasMore)
            {
                if (!string.IsNullOrEmpty(next)) throw Invalid();
                return rows.AsReadOnly();
            }
            if (response.Items.Count == 0 || !AdminPaging.IsValidCursor(next) || !cursors.Add(next!)) throw Invalid();
            cursor = next;
        }
        throw new AdminPluginsInventoryLimitException(MaxPages * PageSize);
    }

    public async Task<IReadOnlyList<AdminPlugin>> InstallationsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var rows = await ReadAsync<AdminPlugin>(lease, "installations", row => { ValidatePlugin(row, true); return row.Id; }, ct).ConfigureAwait(false);
        if (rows.Select(p => p.PluginId).Distinct(StringComparer.Ordinal).Count() != rows.Count) throw Invalid();
        return rows;
    }
    public Task<IReadOnlyList<AdminPlugin>> CatalogAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        ReadAsync<AdminPlugin>(lease, "catalog", row => { ValidatePlugin(row, false); return row.PluginId + "@" + row.Version; }, ct);
    public Task<IReadOnlyList<AdminPluginRepository>> RepositoriesAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        ReadAsync<AdminPluginRepository>(lease, "repositories", row => { RequireId(row.Id); RequireSource(row.SourceKind); return row.Id; }, ct);

    public async Task<AdminVersioned<AdminPluginCatalogSettings>> CatalogSettingsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var result = await client.AdminPluginSendAsync<AdminPluginCatalogSettings>(lease, HttpMethod.Get, Root + "catalog-settings", ct: ct).ConfigureAwait(false);
        AdminValidators.RequireStrong(result.ETag);
        return new(result.Body, result.ETag);
    }
    public async Task<AdminPluginCatalogStatus> CatalogStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var status = (await client.AdminPluginSendAsync<AdminPluginCatalogStatus>(lease, HttpMethod.Get, Root + "catalog-status", ct: ct).ConfigureAwait(false)).Body;
        if (status.ApprovedCommunityPluginCount < 0 || status.InstalledCommunityPluginCount < 0 || status.MigratedPluginCount < 0) throw Invalid();
        return status;
    }
    public async Task SetCatalogSettingsAsync(MonitorAuthorityLease lease, AdminVersioned<AdminPluginCatalogSettings> reviewed, bool include, CancellationToken ct = default)
    {
        AdminValidators.RequireStrong(reviewed.ETag);
        var result = await client.AdminPluginSendAsync<AdminPluginCatalogSettings>(lease, HttpMethod.Put, Root + "catalog-settings",
            new AdminPluginCatalogSettings { IncludeApprovedCommunityPlugins = include }, reviewed.ETag, ct: ct).ConfigureAwait(false);
        AdminValidators.RequireStrong(result.ETag);
        if (result.Body.IncludeApprovedCommunityPlugins != include) throw Invalid();
    }

    public async Task<AdminPlugin> InstallAsync(MonitorAuthorityLease lease, AdminPluginInstall input, CancellationToken ct = default)
    {
        if (input.RepositoryId != null) { RequireId(input.RepositoryId); RequireKey(input.PluginId); RequireKey(input.Version); }
        else if (string.IsNullOrWhiteSpace(input.ArchiveUrl) || input.ArchiveUrl.Length > 8192) throw new ArgumentException("Supply a catalog version or archive URL.");
        var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, HttpMethod.Post, Root + "installations", input, expected: HttpStatusCode.Created, ct: ct).ConfigureAwait(false)).Body;
        ValidatePlugin(row, true);
        if (input.PluginId != null && (row.PluginId != input.PluginId || row.Version != input.Version)) throw Invalid();
        return row;
    }

    public async Task<AdminPlugin> InstallationAsync(MonitorAuthorityLease lease, string id, AdminPluginCommand command, CancellationToken ct = default)
    {
        RequireId(id);
        if (command == AdminPluginCommand.Uninstall) throw new ArgumentException("Use UninstallAsync for its 204 receipt.");
        var suffix = command == AdminPluginCommand.Update ? "/update" : command == AdminPluginCommand.Restart ? "/restart" : "";
        object? body = command switch
        {
            AdminPluginCommand.Enable => new { enabled = true }, AdminPluginCommand.Disable => new { enabled = false },
            AdminPluginCommand.PolicyAuto => new { update_policy = "auto" }, AdminPluginCommand.PolicyNotify => new { update_policy = "notify" },
            AdminPluginCommand.PolicyOff => new { update_policy = "off" }, _ => null,
        };
        var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, suffix.Length == 0 ? HttpMethod.Put : HttpMethod.Post,
            Root + "installations/" + id + suffix, body, ct: ct).ConfigureAwait(false)).Body;
        ValidatePlugin(row, true); if (row.Id != id) throw Invalid();
        if (command == AdminPluginCommand.Enable && !row.Enabled || command == AdminPluginCommand.Disable && row.Enabled ||
            command == AdminPluginCommand.PolicyAuto && row.UpdatePolicy != "auto" || command == AdminPluginCommand.PolicyNotify && row.UpdatePolicy != "notify" || command == AdminPluginCommand.PolicyOff && row.UpdatePolicy != "off") throw Invalid();
        return row;
    }
    public async Task UninstallAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        RequireId(id); await client.AdminPluginSendAsync<object>(lease, HttpMethod.Delete, Root + "installations/" + id, expected: HttpStatusCode.NoContent, ct: ct).ConfigureAwait(false);
    }
    public async Task RepositoryAsync(MonitorAuthorityLease lease, AdminPluginRepository? reviewed, string name, string url, bool enabled, bool delete = false, CancellationToken ct = default)
    {
        if (reviewed?.Managed == true) throw new ArgumentException("Managed repositories are controlled by catalog settings.");
        if (reviewed != null) RequireId(reviewed.Id);
        if (delete)
        {
            if (reviewed == null) throw new ArgumentException("Select a repository.");
            await client.AdminPluginSendAsync<object>(lease, HttpMethod.Delete, Root + "repositories/" + reviewed.Id, expected: HttpStatusCode.NoContent, ct: ct).ConfigureAwait(false);
            return;
        }
        if (name.Length > 1024 || url.Length > 8192 || reviewed == null && (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))) throw new ArgumentException("Repository name and URL are required.");
        var row = (await client.AdminPluginSendAsync<AdminPluginRepository>(lease, reviewed == null ? HttpMethod.Post : HttpMethod.Put,
            Root + "repositories" + (reviewed == null ? "" : "/" + reviewed.Id), new { display_name = name, url, enabled },
            expected: reviewed == null ? HttpStatusCode.Created : HttpStatusCode.OK, ct: ct).ConfigureAwait(false)).Body;
        RequireId(row.Id); RequireSource(row.SourceKind);
        if (reviewed != null && reviewed.Id != row.Id) throw Invalid();
    }
    public async Task SaveConfigAsync(MonitorAuthorityLease lease, string id, AdminPluginConfigWrite input, CancellationToken ct = default)
    {
        RequireId(id); RequireKey(input.Key);
        await client.AdminPluginSendAsync<object>(lease, HttpMethod.Put, Root + "installations/" + id + "/config", input, expected: HttpStatusCode.NoContent, ct: ct).ConfigureAwait(false);
    }
    public async Task<AdminPluginCheck> TestConfigAsync(MonitorAuthorityLease lease, string id, AdminPluginConfigWrite input, CancellationToken ct = default)
    {
        RequireId(id); RequireKey(input.Key);
        return (await client.AdminPluginSendAsync<AdminPluginCheck>(lease, HttpMethod.Post, Root + "installations/" + id + "/config/test", input, ct: ct).ConfigureAwait(false)).Body;
    }
    public async Task AuthBindingAsync(MonitorAuthorityLease lease, string id, AdminPluginAuthBinding input, CancellationToken ct = default)
    {
        RequireId(id); RequireKey(input.CapabilityId);
        await client.AdminPluginSendAsync<object>(lease, HttpMethod.Put, Root + "installations/" + id + "/auth-binding", input, expected: HttpStatusCode.NoContent, ct: ct).ConfigureAwait(false);
    }
    public async Task<bool> TaskBindingAsync(MonitorAuthorityLease lease, string id, AdminPluginTaskBinding input, CancellationToken ct = default)
    {
        RequireId(id); RequireKey(input.CapabilityId);
        return (await client.AdminPluginSendAsync<AdminPluginRestartResult>(lease, HttpMethod.Put, Root + "installations/" + id + "/task-bindings/" + Uri.EscapeDataString(input.CapabilityId),
            new { input.Enabled, input.Trigger }, ct: ct).ConfigureAwait(false)).Body.RestartRequired;
    }
    public async Task<JsonElement> UpdateTaskAsync(MonitorAuthorityLease lease, bool run, CancellationToken ct = default)
    {
        var body = (await client.AdminPluginSendAsync<JsonElement>(lease, run ? HttpMethod.Post : HttpMethod.Get,
            "/api/v2/admin/tasks/check_plugin_updates" + (run ? "/run" : ""), ct: ct).ConfigureAwait(false)).Body;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String || key.GetString() != "check_plugin_updates" ||
            !body.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String || state.GetString() is not ("idle" or "running" or "cancelling")) throw Invalid();
        return body;
    }

    internal static void RequireId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 19 || id[0] == '0' || !id.All(char.IsAsciiDigit) || !long.TryParse(id, out var n) || n <= 0) throw new ArgumentException("A positive plugin resource ID is required.");
    }
    private static void RequireKey(string? key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(char.IsControl)) throw new ArgumentException("Invalid plugin key."); }
    private static void RequireSource(string source) { if (source is not ("silo" or "approved_community" or "external")) throw Invalid(); }
    private static void ValidatePlugin(AdminPlugin row, bool installed)
    {
        RequireKey(row.PluginId); RequireKey(row.Version); RequireSource(row.SourceKind);
        if (installed)
        {
            RequireId(row.Id);
            if (row.Runtime == null || row.Runtime.State is not ("stopped" or "starting" or "running" or "backoff" or "failed") || row.Signature == null) throw Invalid();
        }
        if (!installed || row.RepositoryId != null) RequireId(row.RepositoryId);
        if (row.GlobalConfigSchema == null || row.GlobalConfigs == null || row.Capabilities == null || row.AuthBindings == null || row.TaskBindings == null) throw Invalid();
        if (row.RepositoryName == null || row.RepoUrl == null || row.UpdatePolicy == null || row.GlobalConfigSchema.Any(s => s == null || string.IsNullOrWhiteSpace(s.Key) || s.Title == null || s.Description == null || s.JsonSchema == null) ||
            row.Capabilities.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Type) || c.Description == null || c.DisplayName == null || c.Subscriptions == null) ||
            row.GlobalConfigs.Any(c => c == null || c.Value == null || c.ConfiguredSecrets == null)) throw Invalid();
        if (row.GlobalConfigSchema.Count > 100 || row.Capabilities.Count > 200 || row.GlobalConfigSchema.Sum(s => s.AdminForm?.Fields?.Count ?? 0) > 400 || row.GlobalConfigSchema.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() != row.GlobalConfigSchema.Count) throw Invalid();
        if (row.GlobalConfigs.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != row.GlobalConfigs.Count ||
            row.AuthBindings.Any(b => b == null || string.IsNullOrWhiteSpace(b.CapabilityId)) || row.TaskBindings.Any(b => b == null || string.IsNullOrWhiteSpace(b.CapabilityId) || b.Trigger == null)) throw Invalid();
        if (row.Presentation is { } info && (info.DisplayName == null || info.Summary == null || info.SetupMarkdown == null || info.DescriptionMarkdown == null || info.PublisherName == null || info.LicenseSpdx == null ||
            info.SourceUrl == null || info.SupportUrl == null || info.ChangelogUrl == null || info.HomepageUrl == null || info.PublisherUrl == null)) throw Invalid();
        foreach (var schema in row.GlobalConfigSchema)
        {
            if (schema.AdminForm is not { } form) continue;
            if (form.Fields == null || form.Sections == null || form.Fields.Count > 200 || form.Sections.Count > 100) throw Invalid();
            if (form.Fields.Any(f => f == null || string.IsNullOrWhiteSpace(f.Key) || f.Label == null || f.Description == null || f.Control == null || f.Options == null || f.ShowWhen == null || f.Validation is { Pattern: null } || f.Options.Count > 500 || f.Options.Any(o => o == null || o.Value == null || o.Label == null) || f.ShowWhen.Any(c => c == null || c.Field == null || c.Equals == null)) ||
                form.Fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != form.Fields.Count ||
                form.Sections.Any(s => s == null || s.Key == null || s.Title == null || s.Description == null || s.FieldKeys == null || s.ShowWhen == null || s.ShowWhen.Any(c => c == null || c.Field == null || c.Equals == null))) throw Invalid();
        }
    }
    private static InvalidDataException Invalid() => new("Invalid plugin management response. Reload before editing.");
}

public sealed class AdminPluginsInventoryLimitException(int limit) : IOException("The plugin inventory exceeds the bounded client limit.")
{
    public int Limit { get; } = limit;
}
