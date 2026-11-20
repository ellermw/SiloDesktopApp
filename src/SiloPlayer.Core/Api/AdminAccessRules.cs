using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

internal static class AdminAccessRules
{
    internal static string Id(string id) => AdminSubtitleIds.IsId(id) ? id : throw new ArgumentException("Invalid resource identity.");
    internal static string Cursor(string? cursor) => cursor == null ? "" : AdminPaging.IsValidCursor(cursor) ? "&cursor=" + Uri.EscapeDataString(cursor) : throw new ArgumentException("Invalid cursor.");
    internal static AdminAccessPage<T> Page<T>(AdminAccessPage<T> page, string? cursor, int size = 50)
    {
        if (page.Items == null || page.Items.Count > size || page.Page == null || page.Page.HasMore &&
            (page.Items.Count == 0 || !AdminPaging.IsValidCursor(page.Page.NextCursor) || page.Page.NextCursor == cursor) ||
            !page.Page.HasMore && !string.IsNullOrEmpty(page.Page.NextCursor)) throw new AdminAccessException("invalid_response");
        return page;
    }
    internal static async Task<AdminAccessPage<AdminAccessAccount>> AccountsAsync(SiloApiClient client, MonitorAuthorityLease owner, string? cursor)
    {
        var response = await client.AdminAccessSendAsync<AdminAccessPage<AdminAccessAccount>>(owner, HttpMethod.Get, "/api/v2/admin/users?limit=50" + Cursor(cursor)).ConfigureAwait(false);
        var page = Page(response.Body, cursor); foreach (var account in page.Items) Id(account.Id); return page;
    }
    internal static async Task RequireManageAsync(SiloApiClient client, MonitorAuthorityLease owner, string targetId)
    {
        Id(targetId); Id(owner.AccountId);
        var viewer = (await client.AdminAccessSendAsync<AdminAccessAccount>(owner, HttpMethod.Get, "/api/v2/admin/users/" + owner.AccountId).ConfigureAwait(false)).Body;
        var target = targetId == owner.AccountId ? viewer : (await client.AdminAccessSendAsync<AdminAccessAccount>(owner, HttpMethod.Get, "/api/v2/admin/users/" + targetId).ConfigureAwait(false)).Body;
        owner.ThrowIfRetired();
        if (viewer.Id != owner.AccountId || viewer.Role != "admin" || !viewer.Enabled || target.Id != targetId || !target.CanManage(viewer.Id, viewer.IsOwner))
            throw new AdminAccessException("permission_denied", 403);
    }
}
