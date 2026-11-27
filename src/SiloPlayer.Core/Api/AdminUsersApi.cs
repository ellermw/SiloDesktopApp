using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Core.Api;

public sealed record AdminUsersDocument(JsonObject? Body, string? ETag)
{
    public string? ViewAsBaseUrl { get; internal init; }
    internal Func<string, Task>? RevokeViewAs { get; init; }
    public Task DiscardViewAsAsync()
    {
        if (RevokeViewAs == null || Body == null) return Task.CompletedTask;
        var access = Body.Text("access_token");
        Body.Remove("access_token"); Body.Remove("refresh_token");
        return access.Length == 0 ? Task.CompletedTask : RevokeViewAs(access);
    }
}
public sealed record AdminUsersCollectionPage(IReadOnlyList<JsonObject> Items, string? NextCursor, JsonObject Envelope);
public sealed record AdminUserReview(JsonObject User, JsonObject Viewer, JsonObject Capabilities, string? ETag, long Epoch);
public sealed record AdminUsersReadback(string Path, JsonObject Body, string? ETag = null, bool Strong = false);
public enum AdminUsersOperation
{
    CreateUser, UpdateAccount, UpdatePolicy, DeleteAccount, ViewAs, Transfer, ResetLink, ResetEmail,
    CreateProfile, UpdateProfile, DeleteProfile, LinkIdentity, UnlinkIdentity, RevokeSession, RevokeSessions,
    Quota, SetSetting, ResetSetting, SaveSections, ResetSections, CreateInvitation, ResendInvitation,
    RevokeInvitation, CreateCode, UpdateCode, TopUpCode, DeleteCode
}
public sealed record AdminUsersWrite(AdminUsersOperation Operation, JsonObject? Body = null, string? ResourceId = null,
    AdminUserSettingIdentity? Identity = null, string? SettingKey = null, IReadOnlyList<AdminUsersReadback>? Readbacks = null);
public sealed class AdminUsersReviewRequiredException() : InvalidOperationException("This data changed or the write outcome is unknown. Reload locally, review the current values, and explicitly choose the action again. Nothing was replayed.");

public sealed partial class AdminUsersApi(SiloApiClient client)
{
    public const int MaxRows = 1000;
    public static string Segment(string value) => string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Length > 256
        ? throw new ArgumentException("A complete resource identity is required.") : Uri.EscapeDataString(value);
    public static string UserPath(string id) => "/api/v2/admin/users/" + Segment(id);
    public Task<AdminUsersDocument> ReadAsync(MonitorAuthorityLease lease, string path, CancellationToken ct = default) => ReadCoreAsync(lease, path, ct);
    private async Task<AdminUsersDocument> ReadCoreAsync(MonitorAuthorityLease lease, string path, CancellationToken ct)
    {
        var response = await client.AdminUsersSendAsync(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        if (response.Body == null) throw new InvalidDataException("Empty U1 read response.");
        return response;
    }
    public async Task<AdminUsersCollectionPage> PageAsync(MonitorAuthorityLease lease, string path, int limit = 100, string? cursor = null,
        bool unpaged = false, CancellationToken ct = default)
    {
        limit = AdminPaging.ClampLimit(limit, 200);
        var response = await ReadAsync(lease, unpaged ? path : AdminPaging.WithPage(path, limit, cursor), ct).ConfigureAwait(false);
        var body = response.Body!;
        if (body["items"] is not JsonArray items || items.Count > (unpaged ? MaxRows : limit) || items.Any(item => item is not JsonObject))
            throw new InvalidDataException("Invalid or oversized U1 collection.");
        string? next = null;
        if (body["page"] is JsonObject page)
        {
            if (page["has_more"] is not JsonValue hasMore || !hasMore.TryGetValue<bool>(out var more)) throw new InvalidDataException("Missing continuation state.");
            var token = page.Text("next_cursor");
            if (more)
            {
                if (unpaged || items.Count == 0 || !AdminPaging.IsValidCursor(token) || token == cursor) throw new InvalidDataException("Invalid continuation.");
                next = token;
            }
            else if (token.Length != 0) throw new InvalidDataException("Unexpected continuation.");
        }
        else if (!unpaged) throw new InvalidDataException("Missing page state.");
        return new(items.Cast<JsonObject>().Select(item => item.Copy()).ToArray(), next, body);
    }
    public async Task<AdminUserReview> ReviewAsync(MonitorAuthorityLease lease, string? id, CancellationToken ct = default)
    {
        var caps = (await ReadAsync(lease, "/api/v2/admin/users/capabilities", ct).ConfigureAwait(false)).Body!;
        var viewer = (await ReadAsync(lease, UserPath(lease.AccountId), ct).ConfigureAwait(false)).Body!;
        if (viewer.Text("id") != lease.AccountId) throw new InvalidDataException("The viewer identity did not match.");
        var target = id == null || id == lease.AccountId ? new AdminUsersDocument(viewer, null) : await ReadAsync(lease, UserPath(id), ct).ConfigureAwait(false);
        if (target.Body!.Text("id") != (id ?? lease.AccountId)) throw new InvalidDataException("The target account identity did not match.");
        if (id == lease.AccountId) target = await ReadAsync(lease, UserPath(id), ct).ConfigureAwait(false);
        if (target.Body!.Text("id") != (id ?? lease.AccountId)) throw new InvalidDataException("The reviewed account changed identity.");
        return new(target.Body!, viewer, caps, target.ETag, lease.Epoch);
    }
    public async Task<AdminUsersDocument> WriteAsync(MonitorAuthorityLease lease, AdminUserReview reviewed, AdminUsersWrite write, CancellationToken ct = default, Action? writeAttempted = null)
    {
        lease.ThrowIfRetired();
        if (reviewed.Epoch != lease.Epoch || reviewed.Viewer.Text("id") != lease.AccountId) throw new MonitorAuthorityRetiredException();
        var fresh = await ReviewAsync(lease, reviewed.User.Text("id"), ct).ConfigureAwait(false);
        if (fresh.ETag != reviewed.ETag || !JsonNode.DeepEquals(fresh.User, reviewed.User)) throw new AdminUsersReviewRequiredException();
        var operation = write.Operation;
        var owner = fresh.Viewer.Flag("is_owner");
        var target = fresh.User;
        var manage = AdminUsersRules.CanManage(target, lease.AccountId, owner);
        var policy = AdminUsersRules.CanChangePolicy(target, lease.AccountId, owner);
        var root = UserPath(target.Text("id"));
        var body = write.Body?.Copy();
        string? tag = null;
        var path = root;
        var method = HttpMethod.Post;
        void Require(bool allowed) { if (!allowed) throw new InvalidOperationException("This exact action is not permitted by the current owner, identity or capability."); }
        void Feature(string feature) => Require(AdminUsersRules.Feature(fresh.Capabilities, feature));
        void Validate(string schema) => AdminUsersContract.Validate(AdminUsersContract.Schema(schema), body ?? new JsonObject());
        async Task<JsonObject> Capability(string resource) => (await ReadAsync(lease, resource, ct).ConfigureAwait(false)).Body!;
        var invitation = operation is AdminUsersOperation.CreateInvitation or AdminUsersOperation.ResendInvitation or AdminUsersOperation.RevokeInvitation;
        var code = operation is AdminUsersOperation.CreateCode or AdminUsersOperation.UpdateCode or AdminUsersOperation.TopUpCode or AdminUsersOperation.DeleteCode;
        if (!invitation && !code) Require(AdminUsersRules.Available(fresh.Capabilities));
        if (operation is AdminUsersOperation.UpdateAccount or AdminUsersOperation.UpdatePolicy or AdminUsersOperation.DeleteAccount)
        {
            Feature("guarded_configuration"); Require(manage); tag = AdminValidators.RequireStrong(fresh.ETag);
        }
        await GuardNestedWriteAsync(lease, fresh, write, body, ct).ConfigureAwait(false);
        switch (operation)
        {
            case AdminUsersOperation.CreateUser:
                path = "/api/v2/admin/users"; Validate("AdminAccountCreateBody");
                Require(body!.Text("role") != "admin" || owner);
                if (body.Flag("create_default_profile")) Feature("default_profile");
                break;
            case AdminUsersOperation.UpdateAccount:
            case AdminUsersOperation.UpdatePolicy:
                method = HttpMethod.Put; Validate("AdminAccountUpdateBody");
                var policyFields = new[] { "access_group_id", "permissions", "library_ids", "max_profiles", "max_streams", "max_transcodes", "max_playback_quality", "max_remote_stream_bitrate_kbps", "max_local_stream_bitrate_kbps", "transcode_allowed", "audio_transcode_allowed", "download_allowed", "download_transcode_allowed", "requests_allowed" };
                if (operation == AdminUsersOperation.UpdatePolicy || policyFields.Any(key => body!.ContainsKey(key))) Require(policy);
                if (body!.ContainsKey("role")) Require(owner && !target.Flag("is_owner"));
                if ((target.Flag("is_owner") || target.Text("id") == lease.AccountId) && body.ContainsKey("enabled")) Require(body.Flag("enabled"));
                if (body.Text("role") == "admin") body["access_group_id"] = null;
                if (body.ContainsKey("password") && target.Text("id") == lease.AccountId && !target.Flag("password_login")) Require(owner);
                if (body.ContainsKey("break_glass")) Require(owner && AdminUsersRules.Feature(await Capability("/api/v2/auth/external-sign-in/capabilities"), "break_glass") && target.Text("role") == "admin");
                break;
            case AdminUsersOperation.DeleteAccount:
                Require(!target.Flag("is_owner") && target.Text("id") != lease.AccountId); method = HttpMethod.Delete; break;
            case AdminUsersOperation.ViewAs:
                Require(AdminUsersRules.CanViewAs(target, lease.AccountId, owner)); path += "/impersonate"; break;
            case AdminUsersOperation.Transfer:
                Feature("ownership_transfer"); Require(AdminUsersRules.CanTransfer(target, lease.AccountId, owner)); path += "/transfer-ownership"; break;
            case AdminUsersOperation.ResetLink:
            case AdminUsersOperation.ResetEmail:
                Require(manage && target.Flag("password_login")); Feature(operation == AdminUsersOperation.ResetLink ? "password_reset_link" : "password_reset_email");
                path += "/password-reset"; body = new JsonObject { ["delivery"] = operation == AdminUsersOperation.ResetLink ? "link" : "email" }; break;
            case AdminUsersOperation.CreateProfile:
            case AdminUsersOperation.UpdateProfile:
            case AdminUsersOperation.DeleteProfile:
                Feature("profile_management"); Require(AdminUsersRules.CanManageProfiles(target, lease.AccountId, owner)); path += "/profiles";
                if (operation != AdminUsersOperation.CreateProfile)
                {
                    path += "/" + Segment(write.ResourceId!);
                    Require(write.Readbacks?.Any(readback => readback.Path == path && readback.Body.Text("id") == write.ResourceId) == true);
                    var profile = write.Readbacks!.First(readback => readback.Path == path).Body;
                    if (operation == AdminUsersOperation.DeleteProfile) { Require(!profile.Flag("is_primary")); method = HttpMethod.Delete; }
                    else { method = HttpMethod.Patch; Validate("ProfileUpdate"); AdminUsersRules.ValidateProfile(body!, profile.Flag("is_primary")); }
                }
                else { Validate("ProfileCreate"); AdminUsersRules.ValidateProfile(body!, false); }
                break;
            case AdminUsersOperation.LinkIdentity:
            case AdminUsersOperation.UnlinkIdentity:
                Require(manage && AdminUsersRules.Feature(await Capability("/api/v2/auth/external-sign-in/capabilities"), "admin_identities")); path += "/identities";
                if (operation == AdminUsersOperation.LinkIdentity) Validate("AdminUserIdentityCreateInputBody");
                else { path += "/" + Segment(write.ResourceId!); method = HttpMethod.Delete; }
                break;
            case AdminUsersOperation.RevokeSession:
            case AdminUsersOperation.RevokeSessions:
                Feature("login_sessions"); Require(manage && AdminUsersRules.Feature(await Capability("/api/v2/auth/sessions/capabilities"), "admin_management"));
                path += "/login-sessions"; method = HttpMethod.Delete;
                if (operation == AdminUsersOperation.RevokeSession) path += "/" + Segment(write.ResourceId!);
                break;
            case AdminUsersOperation.Quota:
                Require(policy); path = "/api/v2/admin/request-users/" + Segment(target.Text("id")) + "/limit"; method = HttpMethod.Put;
                AdminUsersRules.ValidateQuota(body!);
                tag = AdminValidators.RequireStrong(write.Readbacks?.FirstOrDefault(readback => readback.Path == path && readback.Strong)?.ETag); break;
            case AdminUsersOperation.SetSetting:
            case AdminUsersOperation.ResetSetting:
                Require(manage); write.Identity!.Validate(); path += "/settings/values/" + Segment(write.SettingKey!) + write.Identity.Query();
                method = operation == AdminUsersOperation.SetSetting ? HttpMethod.Put : HttpMethod.Delete;
                Require(write.Readbacks?.Any(readback => readback.Path == root + "/settings/values?limit=200") == true); break;
            case AdminUsersOperation.SaveSections:
            case AdminUsersOperation.ResetSections:
                Require(manage); Feature("profile_sections"); path += "/profiles/" + Segment(write.ResourceId!) + "/sections?scope=home";
                method = operation == AdminUsersOperation.SaveSections ? HttpMethod.Put : HttpMethod.Delete;
                Require(write.Readbacks?.Any(readback => readback.Path == path) == true); break;
            default:
                if (invitation)
                {
                    var caps = await Capability("/api/v2/admin/invitations/capabilities"); Require(AdminUsersRules.Available(caps)); path = "/api/v2/admin/invitations";
                    if (operation == AdminUsersOperation.CreateInvitation)
                    {
                        Validate("AdminInvitationCreateInputBody"); Require(body!.Text("role") != "admin" || owner);
                        Require(body.Text("delivery") != "email" || caps.Flag("email_delivery"));
                        Require(!body.Flag("create_profile") || caps.Flag("default_profile"));
                        if (body.Text("delivery") == "link") Require(!body.ContainsKey("email"));
                        if (body.Text("role") == "admin") body.Remove("access_group_id");
                    }
                    else { path += "/" + Segment(write.ResourceId!); if (operation == AdminUsersOperation.ResendInvitation) { path += "/resend"; Require(body?.Text("delivery") != "email" || caps.Flag("email_delivery")); } else method = HttpMethod.Delete; }
                }
                else if (code)
                {
                    var caps = await Capability("/api/v2/admin/invite-codes/capabilities"); Require(AdminUsersRules.Available(caps)); path = "/api/v2/admin/invite-codes";
                    if (operation == AdminUsersOperation.CreateCode) { Require(caps.Flag("client_selected_code")); Validate("AdminInviteCodeCreateInputBody"); }
                    else { path += "/" + Segment(write.ResourceId!); if (operation == AdminUsersOperation.DeleteCode) method = HttpMethod.Delete;
                        else if (operation == AdminUsersOperation.UpdateCode) { method = HttpMethod.Put; Validate("AdminInviteCodeUpdateInputBody"); }
                        else { path += "/top-up"; Validate("AdminInviteCodeTopUpInputBody"); } }
                }
                else throw new ArgumentOutOfRangeException(nameof(write));
                break;
        }
        foreach (var readback in write.Readbacks ?? [])
        {
            var current = await ReadAsync(lease, readback.Path, ct).ConfigureAwait(false);
            if (readback.Strong) AdminValidators.RequireStrong(current.ETag);
            if ((readback.Strong && current.ETag != readback.ETag) || !JsonNode.DeepEquals(current.Body, readback.Body)) throw new AdminUsersReviewRequiredException();
        }
        lease.ThrowIfRetired();
        ct.ThrowIfCancellationRequested();
        var result = await client.AdminUsersSendAsync(lease, method, path, body, tag, ct, writeAttempted).ConfigureAwait(false);
        if (write.Operation == AdminUsersOperation.Quota) client.InvalidateRequestStatus();
        try
        {
            if (operation == AdminUsersOperation.ViewAs && (result.Body?["user"] is not JsonObject user || user.Text("id") != target.Text("id") || result.Body.Text("access_token").Length == 0))
                throw new InvalidDataException("The temporary sign-in result did not identify the reviewed account. Read back/review; nothing was replayed.");
            return result;
        }
        catch
        {
            await result.DiscardViewAsAsync().ConfigureAwait(false);
            throw;
        }
    }
}
