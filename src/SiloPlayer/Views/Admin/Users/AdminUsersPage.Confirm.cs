using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersFormKit;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    /// <summary>
    /// What a confirmation says (official <c>ConfirmDialog</c>): a title that names the thing acted on, a description, optional bullets of what the action does, the confirm button's
    /// label, red when the action is destructive, "This can't be undone." when it is irreversible, and an optional name to type first.
    /// </summary>
    private sealed record ConfirmText(string Title, string Description, string Confirm, bool Destructive = false, IReadOnlyList<string>? Bullets = null, bool Irreversible = false,
        string? TypeToConfirm = null, string? TypeLabel = null, string? Hint = null, string? SecondaryText = null, AdminUsersWrite? SecondaryWrite = null);

    /// <summary>
    /// The one confirmation dialog of Admin › Users. The write is sent under the fresh account and the reads it names (an invitation or code row that changed since it was drawn is never
    /// acted on), and an outcome nobody can confirm blocks a second send from the same window. The ViewAs caller adopts its own result; a deleted account returns to the list.
    /// </summary>
    private async Task<AdminUsersDocument?> ConfirmAsync(ConfirmText text, AdminUsersWrite write, bool sensitiveResult = false, long? expectedGeneration = null)
    {
        var generation = expectedGeneration ?? _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = T("admin.native.users.refreshedNotice"); await LoadAsync(_location); return null; }
        var review = await FreshReviewAsync(generation);
        write = await GuardedAsync(write, generation);
        if (text.SecondaryWrite != null) text = text with { SecondaryWrite = await GuardedAsync(text.SecondaryWrite, generation) };
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return null;
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(Text(text.Description, 14, true));
        if (text.Hint != null) body.Children.Add(Notice("info", text.Hint, AdminMonitorUi.ToneColor(ActivityTone.Info)));
        if (text.Bullets is { Count: > 0 })
        {
            var list = new StackPanel { Spacing = 4 };
            foreach (var item in text.Bullets) list.Children.Add(Text("•  " + item, 14));
            body.Children.Add(list);
        }
        TextBox? typed = null;
        if (text.TypeToConfirm != null)
        {
            typed = new TextBox { MaxLength = 256 };
            AutomationProperties.SetAutomationId(typed, "ConfirmTyped"); AutomationProperties.SetName(typed, text.TypeLabel ?? text.TypeToConfirm);
            body.Children.Add(Field(text.TypeLabel ?? text.TypeToConfirm, typed));
        }
        var spec = new FormDialogSpec
        {
            Title = text.Title, Body = body, Review = review, MaxWidth = 512, AutomationId = "ConfirmDialog", Destructive = text.Destructive,
            Primary = text.Confirm, Saving = text.Confirm, FailureText = T("admin.native.users.confirmFailed"),
            Footnote = () => text.Irreversible ? AppStrings.Get("web.shell.confirmDialog.irreversible", fallback: "This can't be undone.") : "",
            CanSave = typed == null ? null : () => typed.Text == text.TypeToConfirm,
            OnChanged = typed == null ? null : callback => typed.TextChanged += (_, _) => callback(),
            Build = _ => write,
            SecondaryText = text.SecondaryText, SecondaryBuild = text.SecondaryWrite == null ? null : _ => text.SecondaryWrite,
            ShowResult = sensitiveResult ? result => InvitationResult(result.Body) : null,
        };
        var result = await FormDialogAsync(spec, generation);
        if (result == null) return null;
        // The ViewAs caller owns adoption or revocation, including stale receipts.
        if (write.Operation == AdminUsersOperation.ViewAs) return result;
        RequireGeneration(generation);
        if (write.Operation == AdminUsersOperation.DeleteAccount)
        {
            _listData.Rows.Remove("users");
            Navigate(null, "users", force: true);
        }
        else await LoadAsync(_location);
        return result;
    }

    /// <summary>Reads what the write must find unchanged: an invitation or code row as it was drawn.</summary>
    private async Task<AdminUsersWrite> GuardedAsync(AdminUsersWrite write, long generation)
    {
        if (write.Operation is AdminUsersOperation.UpdateCode or AdminUsersOperation.DeleteCode) write = write with { Readbacks = Baselines("codes") };
        if (write.Operation is AdminUsersOperation.ResendInvitation or AdminUsersOperation.RevokeInvitation)
        {
            var path = "/api/v2/admin/invitations/" + AdminUsersApi.Segment(write.ResourceId!);
            var current = await ReadResourceAsync(path, generation);
            var rendered = Rows("invitations").SingleOrDefault(row => row.Text("id") == write.ResourceId);
            if (!JsonNode.DeepEquals(rendered, current.Body)) throw new AdminUsersReviewRequiredException();
            write = write with { Readbacks = [new(path, current.Body!)] };
        }
        return write;
    }

    /// <summary>
    /// A change that the web makes the moment it is clicked (enable an account, show or hide a Home row, switch an invite code on or off, clear a legacy request block): the same
    /// guarded write, with no window. Something that goes wrong is said on the page in plain words, and the page is read again.
    /// </summary>
    private async Task QuickWriteAsync(AdminUsersWrite write, string failure, long? expectedGeneration = null)
    {
        var generation = expectedGeneration ?? _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = T("admin.native.users.refreshedNotice"); await LoadAsync(_location); return; }
        var review = await FreshReviewAsync(generation);
        write = await GuardedAsync(write, generation);
        RequireGeneration(generation);
        try
        {
            var result = await _workspace.WriteAsync(generation, _api, review, write);
            await result.DiscardViewAsAsync();
        }
        catch (OperationCanceledException) when (_workspace.IsCurrent(generation))
        {
            _notice.Text = _workspace.OutcomeUnknown || _workspace.NeedsReview ? UnconfirmedWriteText(false) : T("admin.native.users.requestUnfinished");
        }
        catch (Exception exception) when (_workspace.IsCurrent(generation))
        {
            _notice.Text = _workspace.OutcomeUnknown || _workspace.NeedsReview ? UnconfirmedWriteText(false) : RefusalText(exception, failure);
        }
        RequireGeneration(generation);
        await LoadAsync(_location);
    }

    private string AccountName => _review!.User.Text("username");

    /// <summary>The official confirmation for each action on this page, by name.</summary>
    private Task<AdminUsersDocument?> ConfirmDisableAsync() => ConfirmAsync(new(T("web.admin.users.page.disableTitle", ("username", AccountName)), T("web.admin.users.page.disableDescription"),
        T("web.admin.users.page.disableConfirm"), Destructive: true), new(AdminUsersOperation.UpdateAccount, new JsonObject { ["enabled"] = false }));

    private Task EnableAccountAsync() => QuickWriteAsync(new(AdminUsersOperation.UpdateAccount, new JsonObject { ["enabled"] = true }), T("admin.native.users.enableFailed"));

    private Task<AdminUsersDocument?> ConfirmTransferAsync() => ConfirmAsync(new(T("web.admin.users.page.transferTitle", ("username", AccountName)),
        T("web.admin.users.page.transferDescription", ("username", AccountName)), T("web.admin.users.page.transferConfirm")), new(AdminUsersOperation.Transfer));

    private Task<AdminUsersDocument?> ConfirmDeleteAccountAsync()
    {
        var profiles = _rows.ContainsKey("profiles") ? Rows("profiles").Count : (int?)null;
        var enabled = _review!.User.Flag("enabled");
        return ConfirmAsync(new(T("web.admin.users.deleteDialog.title", ("username", AccountName)),
            profiles == null ? T("web.admin.users.deleteDialog.description") : T("web.admin.users.deleteDialog.descriptionCounted", ("count", profiles.Value), ("count_text", profiles.Value)),
            T("web.admin.users.deleteDialog.confirm"), Destructive: true, TypeToConfirm: AccountName, TypeLabel: T("web.admin.users.deleteDialog.confirmLabel", ("username", AccountName)),
            Hint: enabled ? T("web.admin.users.deleteDialog.disableHint") : null,
            SecondaryText: enabled ? T("web.admin.users.deleteDialog.disableInstead") : null,
            SecondaryWrite: enabled ? new(AdminUsersOperation.UpdateAccount, new JsonObject { ["enabled"] = false }) : null), new(AdminUsersOperation.DeleteAccount));
    }
}
