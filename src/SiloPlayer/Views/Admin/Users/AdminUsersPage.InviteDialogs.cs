using SiloPlayer.Controls;
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
    private bool FormBlocked()
    {
        if (_dialog == null && !_workspace.NeedsReview) return false;
        _notice.Text = T("admin.native.users.refreshedNotice");
        return true;
    }

    /// <summary>
    /// Invite someone (official <c>CreateInvitationForm</c>). After it is sent the same window shows what happened and the link, which is readable only now; Done closes it.
    /// An invitation whose outcome cannot be confirmed is not sent a second time from this window.
    /// </summary>
    private async Task CreateInvitationAsync()
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (FormBlocked()) { await LoadAsync(_location); return; }
        var capability = Document("invitations-capabilities");
        var review = await FreshReviewAsync(generation);
        await LoadChoicesAsync(generation, true, true);
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;
        var form = new AdminUsersInvitationForm(new(review.Viewer.Flag("is_owner"), capability.Flag("default_profile"), capability.Flag("email_delivery"),
            Feature("access_groups") ? Rows("groups") : [], Rows("library-choices")));
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(Text(T("web.admin.settings.invitations.invite.description"), 14, true)); body.Children.Add(form.Content);
        var spec = new FormDialogSpec
        {
            Title = T("web.admin.settings.invitations.invite.title"), Body = body, Review = review, MaxWidth = 672, AutomationId = "InvitationForm", Created = true,
            Primary = form.PrimaryText, PrimaryLabel = () => form.PrimaryText, SavingLabel = () => form.SavingText,
            UnconfirmedText = T("web.admin.settings.invitations.create.reloadBeforeRetry"), FailureText = T("web.admin.settings.invitations.create.failed"),
            CanSave = () => !form.Blocked, OnChanged = callback => form.Changed += callback,
            Build = _ => form.Validate() ? new AdminUsersWrite(AdminUsersOperation.CreateInvitation, form.Body()) : null,
            ShowResult = result => InvitationResult(result.Body),
        };
        var created = await FormDialogAsync(spec, generation);
        if (created != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }

    /// <summary>What the invitation did and its link (official <c>deliveryMessage</c> and <c>ClaimLinkBox</c>), with the copy button the link needs.</summary>
    private FrameworkElement InvitationResult(JsonObject? result)
    {
        var stack = new StackPanel { Spacing = 16 };
        if (result == null) return stack;
        var (key, email) = AdminUserFormBodies.InvitationDelivery(result);
        stack.Children.Add(Text(email == null ? T(key) : T(key, ("email", email)), 14));
        var url = result.Text("claim_url");
        if (url.Length > 0)
        {
            var link = new TextBlock { Text = url, FontFamily = AdminMonitorUi.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            AutomationProperties.SetAutomationId(link, "InvitationLink");
            stack.Children.Add(new Border { Child = link, Padding = new(10), CornerRadius = new(6), Background = Tint("MutedBrush", 0.5) });
            stack.Children.Add(Text(T(result.Text("delivery_status") == "not_requested" ? "web.admin.settings.invitations.create.finePrintLink" : "web.admin.settings.invitations.create.finePrintEmail"), 12, true));
            var copy = ActionButton(T("web.admin.settings.invitations.claimLink.copy"), "copy");
            copy.HorizontalAlignment = HorizontalAlignment.Right;
            copy.Click += async (_, _) => await CopySensitiveAsync(url);
            stack.Children.Add(copy);
        }
        return stack;
    }

    /// <summary>Create invite code, Edit invite code and Add uses: one small form each, saved under the code list's own read.</summary>
    private async Task CodeFormAsync(JsonObject? item, bool topUp)
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (FormBlocked()) { await LoadAsync(_location); return; }
        var review = await FreshReviewAsync(generation);
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;
        var form = new AdminUsersCodeForm(item, topUp);
        var body = new StackPanel { Spacing = 16 };
        if (topUp) body.Children.Add(Text(T("web.admin.settings.inviteCodes.topUp.description", ("code", item!.Text("code")), ("used", item.Count("use_count")), ("max", item.Count("max_uses"))), 14, true));
        body.Children.Add(form.Content);
        var operation = topUp ? AdminUsersOperation.TopUpCode : item == null ? AdminUsersOperation.CreateCode : AdminUsersOperation.UpdateCode;
        var readbacks = item == null ? null : Baselines("codes");
        var spec = new FormDialogSpec
        {
            Title = T(topUp ? "web.admin.settings.inviteCodes.topUp.title" : item == null ? "web.admin.settings.inviteCodes.create.title" : "admin.native.users.codes.editTitle"),
            Body = body, Review = review, MaxWidth = 448, AutomationId = "InviteCodeForm", Created = item == null && !topUp,
            Primary = topUp ? T("web.admin.settings.inviteCodes.topUpForm.submit") : item == null ? T("web.admin.settings.inviteCodes.form.create") : AppStrings.Get("common.save", fallback: "Save"),
            Saving = T(topUp ? "web.admin.settings.inviteCodes.topUpForm.adding" : item == null ? "web.admin.settings.inviteCodes.form.creating" : "web.admin.users.detail.access.card.saving"),
            UnconfirmedText = T("admin.native.users.codes.unconfirmed"), FailureText = T("admin.native.users.codes.saveFailed"),
            CanSave = () => form.HasChange, OnChanged = callback => form.Changed += callback,
            Build = _ => form.Validate() ? new AdminUsersWrite(operation, form.Body(), item?.Text("id"), Readbacks: readbacks) : null,
        };
        var result = await FormDialogAsync(spec, generation);
        if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }

    private Task CreateCodeAsync() => CodeFormAsync(null, false);
    private Task EditCodeAsync(JsonObject item) => CodeFormAsync(item, false);
    private Task TopUpCodeAsync(JsonObject item) => CodeFormAsync(item, true);

    /// <summary>Connect a sign-in identity (official <c>LinkIdentityDialog</c>) for the account being viewed.</summary>
    private async Task LinkIdentityAsync()
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (FormBlocked()) { await LoadAsync(_location); return; }
        var review = await FreshReviewAsync(generation);
        var providers = Rows("providers").Select(installation =>
        {
            var capability = (installation["capabilities"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(entry => entry.Text("type") == "auth_provider.v1");
            var bindings = (installation["auth_bindings"] as JsonArray)?.OfType<JsonObject>().Where(binding => binding.Text("capability_id") == capability?.Text("id")).ToArray() ?? [];
            return (installation, capability, bindings);
        }).Where(entry => entry.capability != null && entry.bindings.Length > 0)
            .Select(entry => new AdminUsersIdentityForm.Provider(entry.installation.Text("id"), entry.installation["metadata"].Text("name", entry.installation.Text("plugin_id")),
                entry.bindings.Any(binding => binding.Flag("enabled")), entry.capability!.Text("sign_in_mode") == "network")).ToArray();
        var ordered = providers.OrderByDescending(provider => provider.Enabled).ToArray();
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;
        var form = new AdminUsersIdentityForm(review.User, ordered);
        var baselines = Baselines("providers");
        var spec = new FormDialogSpec
        {
            Title = T("web.admin.common.adminUserSignIn.link.title"), Body = form.Content, Review = review, MaxWidth = 512, AutomationId = "IdentityForm",
            Primary = T("web.admin.common.adminUserSignIn.link.connect"), Saving = T("web.admin.common.adminUserSignIn.link.connect"),
            FailureText = T("web.admin.common.adminUserSignIn.link.failed"), CanSave = () => form.HasProviders, OnChanged = callback => form.Changed += callback,
            Build = _ => form.Validate() ? new AdminUsersWrite(AdminUsersOperation.LinkIdentity, form.Body(), Readbacks: baselines) : null,
            PlaceFieldError = refused =>
            {
                if (refused.StatusCode != 409) return Task.FromResult(false);
                form.Show(T("web.admin.common.adminUserSignIn.link.alreadyConnected", ("username", review.User.Text("username"))));
                return Task.FromResult(true);
            },
        };
        var result = await FormDialogAsync(spec, generation);
        if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }
}
