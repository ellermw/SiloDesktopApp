using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Pinned native v2 key contract. Only creation contains the credential; no write is retried.</summary>
public sealed class AdminApiKeysApi(SiloApiClient client)
{
    private const string Root = "/api/v2/admin/api-keys";
    public async Task<AdminApiKeyCapabilities> CapabilitiesAsync(MonitorAuthorityLease owner) =>
        (await client.AdminAccessSendAsync<AdminApiKeyCapabilities>(owner, HttpMethod.Get, Root + "/capabilities").ConfigureAwait(false)).Body;
    public Task<AdminAccessPage<AdminAccessAccount>> AccountsAsync(MonitorAuthorityLease owner, string? cursor = null) => AdminAccessRules.AccountsAsync(client, owner, cursor);
    public Task RequireManageAsync(MonitorAuthorityLease owner, string id) => AdminAccessRules.RequireManageAsync(client, owner, id);
    public async Task<AdminAccessAccount> GetAccountAsync(MonitorAuthorityLease owner, string id)
    {
        var account = (await client.AdminAccessSendAsync<AdminAccessAccount>(owner, HttpMethod.Get, "/api/v2/admin/users/" + AdminAccessRules.Id(id)).ConfigureAwait(false)).Body;
        if (account.Id != id) throw new AdminAccessException("invalid_response"); return account;
    }
    public async Task<IReadOnlyDictionary<string, string>> RateHintsAsync(MonitorAuthorityLease owner)
    {
        var result = (await client.AdminAccessSendAsync<JsonElement>(owner, HttpMethod.Get, "/api/v2/admin/rate-limits/config").ConfigureAwait(false)).Body;
        var hints = new Dictionary<string, string>();
        if (!result.TryGetProperty("tiers", out var tiers) || tiers.ValueKind != JsonValueKind.Object) return hints;
        foreach (var name in new[] { "standard", "elevated" })
            if (tiers.TryGetProperty(name, out var tier) && tier.TryGetProperty("requests_per_second", out var second) && tier.TryGetProperty("requests_per_minute", out var minute) && second.TryGetInt32(out var perSecond) && minute.TryGetInt32(out var perMinute) && perSecond >= 0 && perMinute >= 0)
                hints[name] = AdminText.T("web.admin.apiKeys.rateLimit.values", new { per_second = perSecond, per_minute = perMinute });
        return hints;
    }
    public async Task<AdminAccessPage<AdminApiKey>> ListAsync(MonitorAuthorityLease owner, string? cursor = null)
    {
        var response = await client.AdminAccessSendAsync<AdminAccessPage<AdminApiKey>>(owner, HttpMethod.Get, Root + "?limit=50" + AdminAccessRules.Cursor(cursor)).ConfigureAwait(false);
        var page = AdminAccessRules.Page(response.Body, cursor); var seen = new HashSet<string>();
        foreach (var row in page.Items) { Validate(row); if (!seen.Add(row.Id)) throw new AdminAccessException("invalid_response"); }
        return page;
    }
    public async Task<AdminApiKeyEditor> GetAsync(MonitorAuthorityLease owner, string id)
    {
        var response = await client.AdminAccessSendAsync<AdminApiKey>(owner, HttpMethod.Get, Root + "/" + AdminAccessRules.Id(id)).ConfigureAwait(false);
        Validate(response.Body);
        if (response.Body.Id != id || !AdminValidators.IsStrong(response.ETag)) throw new AdminAccessException("invalid_response");
        return new(response.Body, response.ETag!);
    }
    public async Task<AdminApiKeyCreated> CreateAsync(MonitorAuthorityLease owner, string label, string userId, IReadOnlyList<string> scopes)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 4096) throw new ArgumentException("A bounded label is required.");
        AdminAccessRules.Id(userId);
        var result = (await client.AdminAccessSendAsync<AdminApiKeyCreated>(owner, HttpMethod.Post, Root,
            new Dictionary<string, object?> { ["label"] = label, ["user_id"] = userId, ["scopes"] = scopes }, expected: HttpStatusCode.Created).ConfigureAwait(false)).Body;
        if (result.UserId != userId || !AdminSubtitleIds.IsId(result.Id) || string.IsNullOrEmpty(result.Key) || result.Key.Length > 4096)
        { result.Key = ""; throw new AdminAccessException("invalid_response", uncertain: true); }
        return result;
    }
    public async Task<AdminApiKeyEditor> UpdateTierAsync(MonitorAuthorityLease owner, AdminApiKeyEditor editor, string tier)
    {
        if (tier is not ("standard" or "elevated")) throw new ArgumentException("Unsupported rate tier.");
        AdminValidators.RequireStrong(editor.ETag); Validate(editor.Value);
        var response = await client.AdminAccessSendAsync<AdminApiKey>(owner, HttpMethod.Put, Root + "/" + editor.Value.Id + "/tier", new Dictionary<string, object?> { ["rate_tier"] = tier }, editor.ETag).ConfigureAwait(false);
        Validate(response.Body);
        if (response.Body.Id != editor.Value.Id || response.Body.UserId != editor.Value.UserId || response.Body.RateTier != tier || !AdminValidators.IsStrong(response.ETag)) throw new AdminAccessException("invalid_response", uncertain: true);
        return new(response.Body, response.ETag!);
    }
    public async Task RevokeAsync(MonitorAuthorityLease owner, AdminApiKeyEditor editor)
    {
        AdminValidators.RequireStrong(editor.ETag); Validate(editor.Value);
        await client.AdminAccessSendAsync<object>(owner, HttpMethod.Delete, Root + "/" + editor.Value.Id, etag: editor.ETag, expected: HttpStatusCode.NoContent).ConfigureAwait(false);
    }
    private static void Validate(AdminApiKey key)
    {
        if (!AdminSubtitleIds.IsId(key.Id) || !AdminSubtitleIds.IsId(key.UserId) || key.Scopes == null || key.RateTier is not ("standard" or "elevated")) throw new AdminAccessException("invalid_response");
    }
}
