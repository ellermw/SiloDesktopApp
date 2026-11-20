using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed class AdminSignInApi(SiloApiClient client)
{
    private sealed class Inventory { public List<AdminSignInProvider> Items { get; set; } = []; }
    public async Task<AdminSignInCapabilities> CapabilitiesAsync(MonitorAuthorityLease owner) =>
        (await client.AdminAccessSendAsync<AdminSignInCapabilities>(owner, HttpMethod.Get, "/api/v2/auth/external-sign-in/capabilities").ConfigureAwait(false)).Body;
    public async Task<IReadOnlyList<AdminSignInProvider>> GetInstallationsAsync(MonitorAuthorityLease owner)
    {
        var result = (await client.AdminAccessSendAsync<Inventory>(owner, HttpMethod.Get, "/api/v2/admin/plugins/installations").ConfigureAwait(false)).Body;
        if (result.Items == null || result.Items.Count > 500) throw new AdminAccessException("invalid_response");
        foreach (var provider in result.Items)
        {
            AdminAccessRules.Id(provider.Id);
            foreach (var schema in provider.GlobalConfigSchema)
            {
                schema.AdminForm = AdminSignInSchemaRules.InferForm(schema);
                var config = provider.GlobalConfigs.FirstOrDefault(c => c.Key == schema.Key);
                if (config == null) continue;
                foreach (var key in config.ConfiguredSecrets) config.Value.Remove(key);
                foreach (var field in schema.AdminForm?.Fields.Where(f => f.IsSecret) ?? []) config.Value.Remove(field.Key);
            }
        }
        return result.Items.Where(p => p.Auth != null).ToArray();
    }
    public Task<AdminAccessPage<AdminAccessAccount>> AccountsAsync(MonitorAuthorityLease owner, string? cursor = null) => AdminAccessRules.AccountsAsync(client, owner, cursor);
    public async Task<AdminAccessAccount> AccountAsync(MonitorAuthorityLease owner, string id)
    {
        var account = (await client.AdminAccessSendAsync<AdminAccessAccount>(owner, HttpMethod.Get, "/api/v2/admin/users/" + AdminAccessRules.Id(id)).ConfigureAwait(false)).Body;
        if (account.Id != id) throw new AdminAccessException("invalid_response"); return account;
    }
    public async Task SaveConfigAsync(MonitorAuthorityLease owner, string id, AdminSignInConfigWrite config) =>
        await client.AdminAccessSendAsync<object>(owner, HttpMethod.Put, Root(id) + "/config", new Dictionary<string, object?> { ["key"] = config.Key, ["value"] = config.Value, ["clear_secrets"] = config.ClearSecrets }, expected: HttpStatusCode.NoContent).ConfigureAwait(false);
    public async Task SaveBindingAsync(MonitorAuthorityLease owner, string id, AdminSignInBinding binding) =>
        await client.AdminAccessSendAsync<object>(owner, HttpMethod.Put, Root(id) + "/auth-binding", new Dictionary<string, object?> { ["capability_id"] = binding.CapabilityId, ["enabled"] = binding.Enabled, ["display_order"] = binding.DisplayOrder, ["auto_provision"] = binding.AutoProvision, ["default_login"] = binding.DefaultLogin }, expected: HttpStatusCode.NoContent).ConfigureAwait(false);
    public async Task<AdminSignInTestResult> TestAsync(MonitorAuthorityLease owner, string id, string capability, IReadOnlyList<AdminSignInConfigWrite> config)
    {
        var result = (await client.AdminAccessSendAsync<AdminSignInTestResult>(owner, HttpMethod.Post, Root(id) + "/auth-binding/test", new Dictionary<string, object?> { ["capability_id"] = capability, ["config"] = config }).ConfigureAwait(false)).Body;
        if (result.Steps == null || result.Steps.Count > 100) throw new AdminAccessException("invalid_response");
        // A provider may echo a staged secret in any result field. Exact submitted values are removed
        // before any result is retained or displayed; common credential/URL patterns are also redacted.
        var sensitive = config.SelectMany(c => c.Value.Select(p => p.Value is JsonValue v && v.TryGetValue<string>(out var value) ? value : null)).Where(v => !string.IsNullOrEmpty(v)).Cast<string>().OrderByDescending(v => v.Length).ToArray();
        string Safe(string? text)
        {
            var safe = LocalLog.RedactDiagnosticText(text ?? ""); foreach (var value in sensitive) safe = safe.Replace(value, "<redacted>", StringComparison.Ordinal);
            return safe.Length > 1000 ? safe[..1000] : safe;
        }
        foreach (var step in result.Steps) { step.Label = Safe(step.Label); step.Message = Safe(step.Message); }
        return result;
    }
    private static string Root(string id) => "/api/v2/admin/plugins/installations/" + AdminAccessRules.Id(id);
}
