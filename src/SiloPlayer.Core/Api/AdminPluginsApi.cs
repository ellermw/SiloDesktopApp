using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>The body of PUT .../auth-binding. callback_mode is left out so the stored mode is preserved, as the official page does.</summary>
public sealed class AdminPluginAuthBindingWrite
{
    public string CapabilityId { get; set; } = "";
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }
    public bool AutoProvision { get; set; } = true;
    public bool DefaultLogin { get; set; }
}

/// <summary>
/// Acting-admin plugin management at official 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e. Every write is sent once: no
/// automatic or authentication replay. Bodies are typed models with snake-case names, never JSON nodes.
/// </summary>
public sealed partial class AdminPluginsApi(SiloApiClient client)
{
    public const int MaxResponseBytes = 4 * 1024 * 1024;
    public const int PageSize = 100;
    public const int MaxPages = 20;
    public const int MaxProvisioningRows = 500;
    public const long MaxUploadBytes = 256L * 1024 * 1024;
    public static JsonSerializerOptions WireOptions => AdminPluginWire.Options;
    private const string Root = "/api/v2/admin/plugins/";
    private sealed class Envelope<T> { public List<T>? Items { get; set; } public Continuation? Page { get; set; } }
    private sealed class Continuation { [System.Text.Json.Serialization.JsonRequired] public bool HasMore { get; set; } public string? NextCursor { get; set; } }
    private sealed class Capabilities { public bool EnvDeclarations { get; set; } }

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
        // The service keeps one installation per plugin ID (an install replaces in place).
        if (rows.Select(p => p.PluginId).Distinct(StringComparer.Ordinal).Count() != rows.Count) throw Invalid();
        return rows;
    }

    public Task<IReadOnlyList<AdminPlugin>> CatalogAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        ReadAsync<AdminPlugin>(lease, "catalog", row => { ValidatePlugin(row, false); return row.PluginId + "@" + row.Version + "@" + row.RepositoryId; }, ct);

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

    /// <summary>
    /// Provisioning status as the official page reads it: the capability document first (a 404 means the server has no such
    /// feature), then the status list only when the server declares environment provisioning.
    /// </summary>
    public async Task<IReadOnlyList<AdminPluginProvisionStatus>> ProvisioningStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        try
        {
            var capability = (await client.AdminPluginSendAsync<Capabilities>(lease, HttpMethod.Get, Root + "capabilities", ct: ct).ConfigureAwait(false)).Body;
            if (!capability.EnvDeclarations) return [];
        }
        catch (ApiException error) when (error.StatusCode == 404) { return []; }
        var response = (await client.AdminPluginSendAsync<Envelope<AdminPluginProvisionStatus>>(lease, HttpMethod.Get, Root + "provisioning-status", ct: ct).ConfigureAwait(false)).Body;
        lease.ThrowIfRetired();
        if (response.Items == null || response.Items.Count > MaxProvisioningRows) throw Invalid();
        foreach (var row in response.Items) if (row == null || string.IsNullOrEmpty(row.PluginId) || row.State == null || row.Version == null || row.Detail == null) throw Invalid();
        return response.Items.AsReadOnly();
    }

    public async Task SetCatalogSettingsAsync(MonitorAuthorityLease lease, AdminVersioned<AdminPluginCatalogSettings> reviewed, bool include, CancellationToken ct = default)
    {
        AdminValidators.RequireStrong(reviewed.ETag);
        var result = await client.AdminPluginSendAsync<AdminPluginCatalogSettings>(lease, HttpMethod.Put, Root + "catalog-settings",
            new AdminPluginCatalogSettings { IncludeApprovedCommunityPlugins = include }, reviewed.ETag, ct: ct).ConfigureAwait(false);
        AdminValidators.RequireStrong(result.ETag);
        if (result.Body.IncludeApprovedCommunityPlugins != include) throw Invalid();
    }

    /// <summary>Installs one catalog version. Sent once; the receipt must name the plugin and version that were asked for.</summary>
    public async Task<AdminPlugin> InstallAsync(MonitorAuthorityLease lease, AdminPluginInstall input, CancellationToken ct = default)
    {
        RequireId(input.RepositoryId); RequireKey(input.PluginId); RequireKey(input.Version);
        var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, HttpMethod.Post, Root + "installations", input, expected: HttpStatusCode.Created, ct: ct).ConfigureAwait(false)).Body;
        ValidatePlugin(row, true);
        if (row.PluginId != input.PluginId || row.Version != input.Version) throw Invalid();
        return row;
    }

    /// <summary>Enable, disable, apply the recorded update, or restart. Uninstall has its own 204 method.</summary>
    public async Task<AdminPlugin> CommandAsync(MonitorAuthorityLease lease, string id, AdminPluginCommand command, CancellationToken ct = default)
    {
        RequireId(id);
        if (command == AdminPluginCommand.Uninstall) throw new ArgumentException("Use UninstallAsync for its 204 receipt.");
        var suffix = command == AdminPluginCommand.Update ? "/update" : command == AdminPluginCommand.Restart ? "/restart" : "";
        object? body = command switch { AdminPluginCommand.Enable => new { enabled = true }, AdminPluginCommand.Disable => new { enabled = false }, _ => null };
        var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, suffix.Length == 0 ? HttpMethod.Put : HttpMethod.Post,
            Root + "installations/" + id + suffix, body, ct: ct).ConfigureAwait(false)).Body;
        ValidatePlugin(row, true);
        if (row.Id != id) throw Invalid();
        if (command == AdminPluginCommand.Enable && !row.Enabled || command == AdminPluginCommand.Disable && row.Enabled) throw Invalid();
        return row;
    }

    /// <summary>Sets the update policy to auto, notify or off, the three choices the official page offers.</summary>
    public async Task<AdminPlugin> SetUpdatePolicyAsync(MonitorAuthorityLease lease, string id, string policy, CancellationToken ct = default)
    {
        RequireId(id);
        if (policy is not ("auto" or "notify" or "off")) throw new ArgumentException("Choose auto, notify or off.");
        var row = (await client.AdminPluginSendAsync<AdminPlugin>(lease, HttpMethod.Put, Root + "installations/" + id, new { update_policy = policy }, ct: ct).ConfigureAwait(false)).Body;
        ValidatePlugin(row, true);
        if (row.Id != id || row.UpdatePolicy != policy) throw Invalid();
        return row;
    }

    public async Task UninstallAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        RequireId(id);
        await client.AdminPluginSendAsync<object>(lease, HttpMethod.Delete, Root + "installations/" + id, expected: HttpStatusCode.NoContent, ct: ct).ConfigureAwait(false);
    }

    /// <summary>Adds a repository (enabled) or, with <paramref name="reviewed"/>, changes only whether it is enabled. The official page edits nothing else.</summary>
    public async Task<AdminPluginRepository> AddRepositoryAsync(MonitorAuthorityLease lease, string name, string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url) || name.Length > 1024 || url.Length > 8192) throw new ArgumentException("Repository name and URL are required.");
        var row = (await client.AdminPluginSendAsync<AdminPluginRepository>(lease, HttpMethod.Post, Root + "repositories",
            new { display_name = name.Trim(), url = url.Trim(), enabled = true }, expected: HttpStatusCode.Created, ct: ct).ConfigureAwait(false)).Body;
        RequireId(row.Id); RequireSource(row.SourceKind);
        return row;
    }

    public async Task<AdminPluginRepository> SetRepositoryEnabledAsync(MonitorAuthorityLease lease, string id, bool enabled, CancellationToken ct = default)
    {
        RequireId(id);
        var row = (await client.AdminPluginSendAsync<AdminPluginRepository>(lease, HttpMethod.Put, Root + "repositories/" + id, new { enabled }, ct: ct).ConfigureAwait(false)).Body;
        RequireId(row.Id); RequireSource(row.SourceKind);
        if (row.Id != id || row.Enabled != enabled) throw Invalid();
        return row;
    }

    public async Task RemoveRepositoryAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        RequireId(id);
        await client.AdminPluginSendAsync<object>(lease, HttpMethod.Delete, Root + "repositories/" + id, expected: HttpStatusCode.NoContent, ct: ct).ConfigureAwait(false);
    }

    public async Task SaveConfigAsync(MonitorAuthorityLease lease, string id, AdminPluginConfigWrite input, CancellationToken ct = default)
    {
        RequireId(id); RequireKey(input.Key);
        await client.AdminPluginSendAsync<object>(lease, HttpMethod.Put, Root + "installations/" + id + "/config", input, expected: HttpStatusCode.NoContent, ct: ct).ConfigureAwait(false);
    }

    public async Task<AdminPluginCheck> TestConfigAsync(MonitorAuthorityLease lease, string id, AdminPluginConfigWrite input, CancellationToken ct = default)
    {
        RequireId(id); RequireKey(input.Key);
        var result = (await client.AdminPluginSendAsync<AdminPluginCheck>(lease, HttpMethod.Post, Root + "installations/" + id + "/config/test", input, ct: ct).ConfigureAwait(false)).Body;
        return result.Message == null ? throw Invalid() : result;
    }

    public async Task AuthBindingAsync(MonitorAuthorityLease lease, string id, AdminPluginAuthBindingWrite input, CancellationToken ct = default)
    {
        RequireId(id); RequireKey(input.CapabilityId);
        if (input.DisplayOrder is < 0 or > 100000) throw new ArgumentException("Invalid display order.");
        await client.AdminPluginSendAsync<object>(lease, HttpMethod.Put, Root + "installations/" + id + "/auth-binding", input, expected: HttpStatusCode.NoContent, ct: ct).ConfigureAwait(false);
    }

    /// <summary>Returns whether the server says a restart is required to apply the binding.</summary>
    public async Task<bool> TaskBindingAsync(MonitorAuthorityLease lease, string id, string capabilityId, bool enabled, IReadOnlyDictionary<string, JsonElement> trigger, CancellationToken ct = default)
    {
        RequireId(id); RequireKey(capabilityId);
        var result = (await client.AdminPluginSendAsync<AdminPluginRestartResult>(lease, HttpMethod.Put,
            Root + "installations/" + id + "/task-bindings/" + Uri.EscapeDataString(capabilityId), new { enabled, trigger }, ct: ct).ConfigureAwait(false)).Body;
        return result.RestartRequired;
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
            if (row.RepositoryId != null) RequireId(row.RepositoryId);
        }
        else RequireId(row.RepositoryId);
        if (row.GlobalConfigSchema == null || row.GlobalConfigs == null || row.Capabilities == null || row.AuthBindings == null || row.TaskBindings == null || row.Routes == null) throw Invalid();
        if (row.GlobalConfigSchema.Any(s => s == null || string.IsNullOrWhiteSpace(s.Key) || s.JsonSchema == null) ||
            row.Capabilities.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Type) || c.Subscriptions == null) ||
            row.GlobalConfigs.Any(c => c == null || c.Value == null || c.ConfiguredSecrets == null)) throw Invalid();
        if (row.GlobalConfigSchema.Count > 100 || row.Capabilities.Count > 200 || row.GlobalConfigSchema.Sum(s => s.AdminForm?.Fields?.Count ?? 0) > 400 || row.GlobalConfigSchema.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() != row.GlobalConfigSchema.Count) throw Invalid();
        if (row.GlobalConfigs.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != row.GlobalConfigs.Count ||
            row.AuthBindings.Any(b => b == null || string.IsNullOrWhiteSpace(b.CapabilityId)) || row.TaskBindings.Any(b => b == null || string.IsNullOrWhiteSpace(b.CapabilityId) || b.Trigger == null) ||
            row.Routes.Any(route => route == null) || row.Routes.Count > 1000) throw Invalid();
        if (row.EnvManaged is { } ownership && (ownership.ConfigFields == null || ownership.BindingFields == null)) throw Invalid();
        foreach (var schema in row.GlobalConfigSchema)
        {
            if (schema.AdminForm is not { } form) continue;
            if (form.Fields == null || form.Sections == null || form.Fields.Count > 200 || form.Sections.Count > 100) throw Invalid();
            if (form.Fields.Any(f => f == null || string.IsNullOrWhiteSpace(f.Key) || f.Options == null || f.ShowWhen == null || f.Options.Count > 500 || f.Options.Any(o => o == null || o.Value == null) || f.ShowWhen.Any(c => c == null || c.Field == null || c.Values == null)) ||
                form.Fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != form.Fields.Count ||
                form.Sections.Any(s => s == null || s.Key == null || s.FieldKeys == null || s.ShowWhen == null || s.ShowWhen.Any(c => c == null || c.Field == null || c.Values == null))) throw Invalid();
        }
    }

    private static InvalidDataException Invalid() => new("Invalid plugin management response. Reload before editing.");
}

public sealed class AdminPluginsInventoryLimitException(int limit) : IOException("The plugin inventory exceeds the bounded client limit.")
{
    public int Limit { get; } = limit;
}
