using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using SiloPlayer.Controls;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    /// <summary>The words an admin sees when a write may or may not have happened and must not be sent again.</summary>
    private static string UnconfirmedWriteText(bool created) => created
        ? "We couldn't confirm that the account was created. Close this window and check the Users list before trying again."
        : "We couldn't confirm that this change was saved. Close this window and check the account before trying again.";

    private static TextBlock AlertText()
    {
        var text = Text("", 14); text.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); text.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Assertive);
        return text;
    }

    private static void Show(TextBlock block, string? message)
    {
        block.Text = message ?? ""; block.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The server's own words for a refused write, else the page's plain fallback (never a status code or developer text).</summary>
    private static string RefusalText(Exception error, string fallback) =>
        error is ApiException { Message: { Length: > 0 } message } && !message.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase) && !message.Contains("Exception", StringComparison.Ordinal) ? message : fallback;

    private ContentDialog NewFormDialog(string title, FrameworkElement content, string primary, string close)
    {
        var maxHeight = Math.Max(320, (XamlRoot?.Size.Height ?? 800) - 240);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = content, MaxHeight = maxHeight, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 12, 0) },
            PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 672.0;
        dialog.Resources["ContentDialogMinWidth"] = 320.0;
        return dialog;
    }

    /// <summary>
    /// Create user (official <c>CreateUserForm</c>): the default-profile box over Account, Access and Limits tabs, Save and Cancel. A write that was refused
    /// can be corrected and saved again; one whose outcome is unknown cannot be sent a second time from this window.
    /// </summary>
    private async Task CreateUserAsync()
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = "Close the active editor and reload/review before another change."; return; }
        var review = await FreshReviewAsync(generation);
        var groupsSupported = Feature("access_groups");
        if (groupsSupported)
        {
            if (!_rows.ContainsKey("groups")) await RowsAsync("groups", "/api/v2/admin/access-groups", generation);
            RequireGeneration(generation);
            if (!_rows.ContainsKey("groups")) throw new InvalidOperationException("Access-group choices failed to load. Reload before creating a user.");
        }
        if (!_rows.ContainsKey("library-choices")) await RowsAsync("library-choices", "/api/v2/libraries", generation, true);
        RequireGeneration(generation);
        if (!_rows.ContainsKey("library-choices")) throw new InvalidOperationException("Library choices failed to load. Reload before creating a user.");
        var policy = await ReadPasswordPolicyAsync(generation);
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;

        var form = new AdminUsersCreateForm(new(_review!.Viewer.Flag("is_owner"), Feature("default_profile"), groupsSupported, Feature("profile_limits"),
            groupsSupported ? Rows("groups") : [], Rows("library-choices"), policy));
        var error = AlertText();
        // Wide enough to use the room a window has (the dialog is as wide as its content), at most the official sm:max-w-2xl.
        var content = new StackPanel { Spacing = 12 };
        double DialogWidth() => Math.Max(280, Math.Min(624, (XamlRoot?.Size.Width ?? 800) - 96));
        content.MinWidth = DialogWidth();
        var root = XamlRoot;
        void Resized(XamlRoot sender, XamlRootChangedEventArgs args) => content.MinWidth = DialogWidth();
        if (root != null) root.Changed += Resized;
        content.Children.Add(error); content.Children.Add(form.Content);
        var dialog = NewFormDialog(T("web.admin.users.list.createDialogTitle"),
            content, AppStrings.Get("common.save", fallback: "Save"), AppStrings.Get("common.cancel", fallback: "Cancel"));
        AutomationProperties.SetAutomationId(content, "CreateUserForm");
        _dialog = dialog; _dialogDirty = true;
        var busy = false;
        var dialogCompleted = false;
        var blocked = false;
        void SyncSave() => dialog.IsPrimaryButtonEnabled = !busy && !blocked && !form.SaveBlocked;
        form.Changed += SyncSave;
        SyncSave();
        dialog.Closing += (_, args) => { if (busy) args.Cancel = true; };
        AdminUsersDocument? result = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (busy || blocked) return;
            Show(error, null); form.ShowPasswordError(null);
            if (!form.HasUsername) { Show(error, AppStrings.Get("error.required", fallback: "Fill in the username.")); form.FocusUsername(); return; }
            if (!form.EmailIsValid) { Show(error, AppStrings.Get("web.auth.invalidEmail", fallback: "Enter a valid email address.")); form.FocusEmail(); return; }
            if (form.PasswordProblem() is { } passwordProblem) { form.ShowPasswordError(passwordProblem); return; }
            // A cleared or half-typed limit is not in the policy, which keeps the last valid value: saving now would send that instead.
            if (form.RevealInvalidLimit()) return;
            JsonObject body;
            try
            {
                body = form.Build();
                AdminUsersContract.Validate(AdminUsersContract.Schema("AdminAccountCreateBody"), body);
            }
            catch (ArgumentException exception) { Show(error, exception.Message); return; }
            var submittedPassword = form.PasswordValue;
            var deferral = args.GetDeferral();
            busy = true; SyncSave();
            dialog.PrimaryButtonText = T("web.admin.users.list.create.saving");
            content.IsHitTestVisible = false;
            try
            {
                if (!_workspace.IsCurrent(generation)) return;
                result = await _workspace.WriteAsync(generation, _api, review, new(AdminUsersOperation.CreateUser, body));
                if (dialogCompleted || !_workspace.IsCurrent(generation)) { await result.DiscardViewAsAsync(); result = null; return; }
                busy = false;
                dialog.Hide();
            }
            catch (OperationCanceledException)
            {
                if (!_workspace.IsCurrent(generation)) { busy = false; dialog.Hide(); }
                else { blocked = _workspace.NeedsReview; Show(error, blocked ? UnconfirmedWriteText(true) : "The request didn't finish. Check your connection and try again."); }
            }
            catch (AdminUsersReviewRequiredException) { blocked = true; Show(error, UnconfirmedWriteText(true)); }
            catch (ApiException exception) when (NewPassword.FieldProblem(exception, "body.password") is { } fieldProblem)
            {
                // A policy that changed since the form opened: the server's rule goes at the field, unless the admin has already changed the password it refused.
                blocked = _workspace.NeedsReview;
                if (!dialogCompleted && _workspace.IsCurrent(generation))
                {
                    if (submittedPassword == form.PasswordValue) form.ShowPasswordError(fieldProblem);
                    try { var fresh = await ReadPasswordPolicyAsync(generation); if (!dialogCompleted && _workspace.IsCurrent(generation)) form.ShowPasswordPolicy(fresh); }
                    catch (OperationCanceledException) { }
                }
            }
            catch (Exception exception)
            {
                if (_workspace.IsCurrent(generation))
                {
                    blocked = _workspace.NeedsReview || _workspace.OutcomeUnknown;
                    Show(error, blocked ? UnconfirmedWriteText(true) : RefusalText(exception, T("web.admin.users.list.create.saveFailed")));
                }
            }
            finally
            {
                content.IsHitTestVisible = true;
                busy = false;
                if (!dialogCompleted) { dialog.PrimaryButtonText = AppStrings.Get("common.save", fallback: "Save"); SyncSave(); }
                deferral.Complete();
            }
        };
        var handedOff = false;
        try
        {
            await dialog.ShowAsync();
            if (!_workspace.IsCurrent(generation)) return;
            handedOff = true;
            if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
        }
        finally
        {
            dialogCompleted = true;
            if (root != null) root.Changed -= Resized;
            if (ReferenceEquals(_dialog, dialog)) { _dialog = null; _dialogDirty = false; }
            if (!handedOff && result != null) await result.DiscardViewAsAsync();
        }
    }
}
