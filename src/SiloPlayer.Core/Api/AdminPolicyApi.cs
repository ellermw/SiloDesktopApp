using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Official native policy storage/editor operations; validation and simulation never activate or save.</summary>
public sealed class AdminPolicyApi(SiloApiClient client)
{
    private const string Root = "/api/v2/admin/policy";
    private sealed class VendorCollection { public List<AdminPolicyVendor> Items { get; set; } = []; }
    public async Task<AdminPolicyCapability> CapabilityAsync(MonitorAuthorityLease owner) =>
        (await client.AdminAccessSendAsync<AdminPolicyCapability>(owner, HttpMethod.Get, "/api/v2/policy/capability").ConfigureAwait(false)).Body;
    public async Task<AdminAccessPage<AdminPolicyDocument>> DocumentsAsync(MonitorAuthorityLease owner, string? cursor = null)
    {
        var page = AdminAccessRules.Page((await client.AdminAccessSendAsync<AdminAccessPage<AdminPolicyDocument>>(owner, HttpMethod.Get, Root + "/documents?limit=50" + AdminAccessRules.Cursor(cursor)).ConfigureAwait(false)).Body, cursor);
        foreach (var document in page.Items) Validate(document); return page;
    }
    public async Task<AdminPolicySnapshot> GetAsync(MonitorAuthorityLease owner, string id)
    {
        var result = await client.AdminAccessSendAsync<AdminPolicyDocument>(owner, HttpMethod.Get, Path(id)).ConfigureAwait(false); Validate(result.Body);
        if (result.Body.Id != id || !AdminValidators.IsStrong(result.ETag)) throw new AdminAccessException("invalid_response");
        return new(result.Body, result.ETag!);
    }
    public async Task<AdminPolicyDocument> CreateAsync(MonitorAuthorityLease owner, string domain, string name)
    {
        Domain(domain); name = name.Trim(); if (name.Length is < 1 or > 500) throw new ArgumentException("Policy name length.");
        var document = (await client.AdminAccessSendAsync<AdminPolicyDocument>(owner, HttpMethod.Post, Root + "/documents", new { domain, name }, expected: HttpStatusCode.Created).ConfigureAwait(false)).Body;
        Validate(document); return document;
    }
    public async Task<AdminAccessPage<AdminPolicyVersion>> VersionsAsync(MonitorAuthorityLease owner, string id, string? cursor = null)
    {
        var page = AdminAccessRules.Page((await client.AdminAccessSendAsync<AdminAccessPage<AdminPolicyVersion>>(owner, HttpMethod.Get, Path(id) + "/versions?limit=50" + AdminAccessRules.Cursor(cursor)).ConfigureAwait(false)).Body, cursor);
        foreach (var version in page.Items) if (version.DocumentId != id || !AdminSubtitleIds.IsId(version.Id)) throw new AdminAccessException("invalid_response"); return page;
    }
    public async Task<AdminPolicyVersion> VersionAsync(MonitorAuthorityLease owner, string id, string version)
    {
        var result = (await client.AdminAccessSendAsync<AdminPolicyVersion>(owner, HttpMethod.Get, Path(id) + "/versions/" + AdminAccessRules.Id(version)).ConfigureAwait(false)).Body;
        if (result.DocumentId != id || result.Id != version || result.Source == null || result.Source.Length > 262144) throw new AdminAccessException("invalid_response"); return result;
    }
    public async Task<AdminPolicyVersion> SaveVersionAsync(MonitorAuthorityLease owner, string id, string source, string? comment = null)
    {
        Source(source); if (comment?.Length > 10000) throw new ArgumentException("Policy comment length.");
        var body = new Dictionary<string, object?> { ["source"] = source }; if (!string.IsNullOrWhiteSpace(comment)) body["comment"] = comment.Trim();
        var result = (await client.AdminAccessSendAsync<AdminPolicyVersion>(owner, HttpMethod.Post, Path(id) + "/versions", body, expected: HttpStatusCode.Created).ConfigureAwait(false)).Body;
        if (result.DocumentId != id || !AdminSubtitleIds.IsId(result.Id) || result.Source != source) throw new AdminAccessException("invalid_response", uncertain: true);
        return result;
    }
    public async Task<AdminPolicyValidation> ValidateAsync(MonitorAuthorityLease owner, string domain, string source)
    {
        Domain(domain); Source(source);
        return (await client.AdminAccessSendAsync<AdminPolicyValidation>(owner, HttpMethod.Post, Root + "/validate", new { domain, source }).ConfigureAwait(false)).Body;
    }
    public async Task<AdminPolicySimulationResult> SimulateAsync(MonitorAuthorityLease owner, string domain, string source, string input)
    {
        Domain(domain); Source(source); if (input.Length > 262144) throw new ArgumentException("Simulation input length.");
        using var json = JsonDocument.Parse(input);
        var body = new Dictionary<string, object?> { ["domain"] = domain, ["input"] = json.RootElement.Clone() };
        if (!string.IsNullOrWhiteSpace(source)) body["source"] = source;
        return (await client.AdminAccessSendAsync<AdminPolicySimulationResult>(owner, HttpMethod.Post, Root + "/simulate", body).ConfigureAwait(false)).Body;
    }
    public async Task<AdminPolicyApplyResult> ActivateAsync(MonitorAuthorityLease owner, AdminPolicySnapshot reviewed, string version)
    {
        var result = await ApplyAsync(owner, reviewed, HttpMethod.Put, "/active-version", new { version_id = AdminAccessRules.Id(version) }).ConfigureAwait(false);
        if (result.Document.ActiveVersionId != version) throw new AdminAccessException("invalid_response", uncertain: true); return result;
    }
    public async Task<AdminPolicyApplyResult> EnableAsync(MonitorAuthorityLease owner, AdminPolicySnapshot reviewed, bool enabled)
    {
        var result = await ApplyAsync(owner, reviewed, HttpMethod.Patch, "", new { enabled }).ConfigureAwait(false);
        if (result.Document.Enabled != enabled) throw new AdminAccessException("invalid_response", uncertain: true); return result;
    }
    private async Task<AdminPolicyApplyResult> ApplyAsync(MonitorAuthorityLease owner, AdminPolicySnapshot reviewed, HttpMethod method, string suffix, object body)
    {
        AdminValidators.RequireStrong(reviewed.ETag);
        var result = (await client.AdminAccessSendAsync<AdminPolicyApplyResult>(owner, method, Path(reviewed.Value.Id) + suffix, body, reviewed.ETag).ConfigureAwait(false)).Body;
        if (!result.Persisted || result.Document.Id != reviewed.Value.Id || result.Document.Domain != reviewed.Value.Domain || result.Application == null) throw new AdminAccessException("invalid_response", uncertain: true); return result;
    }
    public async Task DeleteAsync(MonitorAuthorityLease owner, AdminPolicySnapshot reviewed)
    {
        if (reviewed.Value.ActiveVersionId != null) throw new ArgumentException("A document with an active version cannot be deleted.");
        await client.AdminAccessSendAsync<object>(owner, HttpMethod.Delete, Path(reviewed.Value.Id), etag: reviewed.ETag, expected: HttpStatusCode.NoContent).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<AdminPolicyVendor>> VendorAsync(MonitorAuthorityLease owner)
    {
        var collection = (await client.AdminAccessSendAsync<VendorCollection>(owner, HttpMethod.Get, Root + "/vendor").ConfigureAwait(false)).Body;
        if (collection.Items == null || collection.Items.Count > 100) throw new AdminAccessException("invalid_response"); return collection.Items;
    }
    public async Task<AdminAccessPage<AdminPolicyDecision>> DecisionsAsync(MonitorAuthorityLease owner, AdminPolicyDecisionQuery query, string? cursor = null)
    {
        if (query.Allowed is not (null or "true" or "false")) throw new ArgumentException("Invalid decision filter.");
        var path = Root + "/decisions?limit=25" + AdminAccessRules.Cursor(cursor);
        foreach (var (key, value) in new[] { ("decision_name", query.DecisionName), ("user_id", query.UserId), ("allowed", query.Allowed), ("from", query.From), ("to", query.To) })
            if (!string.IsNullOrEmpty(value)) path += "&" + key + "=" + Uri.EscapeDataString(value);
        return AdminAccessRules.Page((await client.AdminAccessSendAsync<AdminAccessPage<AdminPolicyDecision>>(owner, HttpMethod.Get, path).ConfigureAwait(false)).Body, cursor, 25);
    }
    public async Task<AdminPolicyDecision> DecisionAsync(MonitorAuthorityLease owner, string id)
    {
        var decision = (await client.AdminAccessSendAsync<AdminPolicyDecision>(owner, HttpMethod.Get, Root + "/decisions/" + AdminAccessRules.Id(id)).ConfigureAwait(false)).Body;
        if (decision.Id != id) throw new AdminAccessException("invalid_response"); return decision;
    }
    private static string Path(string id) => Root + "/documents/" + AdminAccessRules.Id(id);
    private static void Validate(AdminPolicyDocument document) { AdminAccessRules.Id(document.Id); Domain(document.Domain); }
    private static void Domain(string domain) { if (domain is not ("scope" or "permission" or "action")) throw new ArgumentException("Unsupported policy domain."); }
    private static void Source(string source) { if (source.Length > 262144) throw new ArgumentException("Policy source length."); }
}
