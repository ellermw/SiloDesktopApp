using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;
using Windows.ApplicationModel.DataTransfer;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private void RequireGeneration(long generation)
    {
        if (!_activePage || _generation != generation || !_workspace.IsCurrent(generation)) throw new OperationCanceledException();
    }
    private async Task<AdminUserReview> FreshReviewAsync(long generation)
    {
        RequireGeneration(generation);
        var accountId = _review!.User.Text("id");
        var review = await _workspace.ReadAsync(generation, (lease, token) => _api.ReviewAsync(lease, accountId, token));
        RequireGeneration(generation);
        return review;
    }
    private async Task<AdminUsersDocument> ReadResourceAsync(string path, long generation)
    {
        RequireGeneration(generation);
        var result = await _workspace.ReadAsync(generation, (lease, token) => _api.ReadAsync(lease, path, token));
        RequireGeneration(generation);
        return result;
    }
    private Task<PasswordPolicy> ReadPasswordPolicyAsync(long generation) =>
        _workspace.ReadAsync(generation, (lease, ct) =>
            App.Services.GetRequiredService<AuthApi>().GetPasswordPolicyAsync(lease.Context.BaseUrl, ct));

    /// <summary>Remove PIN (official <c>RemovePinDialog</c>).</summary>
    private async Task ProfilePinAsync(JsonObject row, bool remove)
    {
        if (!remove) { await SetPinAsync(row); return; }
        var generation = _generation;
        RequireGeneration(generation);
        var path = AdminUsersApi.UserPath(_review!.User.Text("id")) + "/profiles/" + AdminUsersApi.Segment(row.Text("id"));
        var full = await ReadResourceAsync(path, generation);
        await ConfirmAsync(new(T("web.admin.users.detail.profiles.removePinTitle", ("name", row.Text("name"))),
            T(row.Flag("is_primary") ? "web.admin.users.detail.profiles.removePinPrimary" : "web.admin.users.detail.profiles.removePinDescription"), T("web.admin.users.detail.profiles.removePin")),
            new(AdminUsersOperation.UpdateProfile, new JsonObject { ["pin"] = null }, row.Text("id"), Readbacks: [new AdminUsersReadback(path, full.Body!)]), expectedGeneration: generation);
    }

    /// <summary>Delete profile (official <c>DeleteProfileDialog</c>).</summary>
    private async Task DeleteProfileAsync(JsonObject row)
    {
        var generation = _generation;
        RequireGeneration(generation);
        var path = AdminUsersApi.UserPath(_review!.User.Text("id")) + "/profiles/" + AdminUsersApi.Segment(row.Text("id"));
        var full = await ReadResourceAsync(path, generation);
        await ConfirmAsync(new(T("web.admin.users.detail.profiles.deleteTitle", ("name", row.Text("name"))), T("web.admin.users.detail.profiles.deleteDescription"),
            T("web.admin.users.detail.profiles.deleteProfile"), Destructive: true, Irreversible: true),
            new(AdminUsersOperation.DeleteProfile, ResourceId: row.Text("id"), Readbacks: Baselines("profiles").Append(new AdminUsersReadback(path, full.Body!)).ToArray()), expectedGeneration: generation);
    }

    /// <summary>
    /// Takes the account's own request approval and limit back to the group or server default. Clearing a legacy block is the one-click "Reset to default" of the web card; the plain reset
    /// asks first, since it removes what the admin set.
    /// </summary>
    private async Task ResetQuotaAsync(bool legacyOnly)
    {
        var generation = _generation;
        RequireGeneration(generation);
        var path = "/api/v2/admin/request-users/" + AdminUsersApi.Segment(_review!.User.Text("id")) + "/limit";
        var full = await ReadResourceAsync(path, generation);
        if (!JsonNode.DeepEquals(full.Body, Document("quota"))) throw new AdminUsersReviewRequiredException();
        var body = legacyOnly ? AdminUserRequestTerms.ClearLegacy(full.Body!) : new JsonObject { ["limit_mode"] = "inherit", ["approval_mode"] = "inherit", ["max_requests"] = null, ["window_days"] = null };
        var write = new AdminUsersWrite(AdminUsersOperation.Quota, body, Readbacks: [new(path, full.Body!, full.ETag, true)]);
        if (legacyOnly) { await QuickWriteAsync(write, T("admin.native.users.confirmFailed"), generation); return; }
        var grouped = _review.User.Text("role") != "admin" && _review.User.Text("access_group_id").Length > 0;
        await ConfirmAsync(new(T("admin.native.users.quota.resetTitle", ("username", AccountName)), T(grouped ? "admin.native.users.quota.resetGroup" : "admin.native.users.quota.resetServer"),
            T(grouped ? "web.admin.users.detail.access.requests.useGroupDefault" : "web.admin.users.detail.access.requests.useServerDefault")), write, expectedGeneration: generation);
    }

    private Task CopySensitiveAsync(string value)
    {
        if (!_workspace.IsCurrent(_generation)) return Task.CompletedTask;
        var package = new DataPackage(); package.SetText(value); Clipboard.SetContent(package);
        return Task.CompletedTask;
    }
}
