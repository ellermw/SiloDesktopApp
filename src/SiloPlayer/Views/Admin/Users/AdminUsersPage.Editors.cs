using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;
using Windows.ApplicationModel.DataTransfer;

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
    private async Task EditAsync(string title, string schemaName, JsonObject initial, AdminUsersOperation operation, string? resource = null,
        bool create = false, string[]? fields = null, string[]? locked = null, IReadOnlyList<AdminUsersReadback>? readbacks = null, bool sensitiveResult = false,
        IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Name)>>? choices = null, long? expectedGeneration = null)
    {
        var generation = expectedGeneration ?? _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = "Close the active editor and reload/review before another change."; return; }
        var review = await FreshReviewAsync(generation);
        if (operation is AdminUsersOperation.UpdateAccount or AdminUsersOperation.UpdatePolicy)
        {
            var sensitive = initial.Where(pair => pair.Key is "password" or "require_password_change").ToArray();
            initial = review.User.Copy();
            foreach (var pair in sensitive) initial[pair.Key] = pair.Value?.DeepClone();
        }
        if (operation is AdminUsersOperation.UpdateCode or AdminUsersOperation.TopUpCode) readbacks ??= Baselines("codes");
        fields ??= AdminUsersContract.Schema(schemaName)["properties"]!.AsObject().Select(pair => pair.Key).ToArray();
        fields = fields.Where(key => key != "access_group_id" || Feature("access_groups")).Where(key => key != "max_profiles" || Feature("profile_limits")).ToArray();
        if (operation == AdminUsersOperation.CreateUser && !Feature("default_profile")) locked = (locked ?? []).Append("create_default_profile").ToArray();
        choices = await FormChoicesAsync(fields, choices, generation);
        RequireGeneration(generation);
        using var form = new AdminUsersForm(AdminUsersContract.Schema(schemaName), initial, create, fields, locked, choices,
            (reference, cancellation) => _workspace.ReadAsync(generation, async (lease, token) =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
                var images = App.Services.GetRequiredService<AdminUserAvatarImages>();
                var bytes = await images.ReadAsync(reference, new Uri(lease.Context.BaseUrl), linked.Token);
                lease.ThrowIfRetired();
                return bytes;
            }), _workspace.Token);
        AddChoiceContinuation(form, fields);
        var editsPassword = fields.Contains("password");
        if (editsPassword)
            form.ShowPasswordPolicy(await ReadPasswordPolicyAsync(generation));
        var result = await MutationDialogAsync(title, form.Content, review,
            () => new(operation, form.Read(), resource, Readbacks: readbacks), create && operation != AdminUsersOperation.Quota, generation, editsPassword ? form : null);
        if (result == null) return;
        RequireGeneration(generation);
        if (sensitiveResult) await SensitiveResultAsync(result.Body!, generation);
        RequireGeneration(generation);
        await LoadAsync(_location);
    }
    private Task<PasswordPolicy> ReadPasswordPolicyAsync(long generation) =>
        _workspace.ReadAsync(generation, (lease, ct) =>
            App.Services.GetRequiredService<AuthApi>().GetPasswordPolicyAsync(lease.Context.BaseUrl, ct));

    private async Task<AdminUsersDocument?> ConfirmAsync(string title, string description, AdminUsersWrite write, bool sensitiveResult = false, long? expectedGeneration = null)
    {
        var generation = expectedGeneration ?? _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = "Reload/review before another change."; return null; }
        var review = await FreshReviewAsync(generation);
        if (write.Operation is AdminUsersOperation.UpdateCode or AdminUsersOperation.DeleteCode) write = write with { Readbacks = Baselines("codes") };
        if (write.Operation is AdminUsersOperation.ResendInvitation or AdminUsersOperation.RevokeInvitation)
        {
            var path = "/api/v2/admin/invitations/" + AdminUsersApi.Segment(write.ResourceId!);
            var current = await ReadResourceAsync(path, generation);
            var rendered = Rows("invitations").SingleOrDefault(row => row.Text("id") == write.ResourceId);
            if (!JsonNode.DeepEquals(rendered, current.Body)) throw new AdminUsersReviewRequiredException();
            write = write with { Readbacks = [new(path, current.Body!)] };
        }
        var panel = Panel(); panel.Children.Add(Text(description));
        panel.Children.Add(Text($"Account #{review.User.Text("id")}: {review.User.Text("username")} · {review.User.Text("role")} · owner={review.User.Flag("is_owner")} · enabled={review.User.Flag("enabled")}"));
        var reconcile = write.Operation is AdminUsersOperation.ViewAs or AdminUsersOperation.Transfer or AdminUsersOperation.ResetLink or AdminUsersOperation.ResetEmail or AdminUsersOperation.ResendInvitation;
        var result = await MutationDialogAsync(title, panel, review, () => write, reconcile, generation);
        if (result == null) return null;
        // The ViewAs caller owns adoption or revocation, including stale receipts.
        if (write.Operation == AdminUsersOperation.ViewAs) return result;
        RequireGeneration(generation);
        if (sensitiveResult && result.Body != null) await SensitiveResultAsync(result.Body, generation);
        RequireGeneration(generation);
        if (write.Operation == AdminUsersOperation.DeleteAccount)
        {
            _listData.Rows.Remove("users");
            Navigate(null, "users", force: true);
        }
        else await LoadAsync(_location);
        return result;
    }
    private async Task<AdminUsersDocument?> MutationDialogAsync(string title, StackPanel editor, AdminUserReview reviewed, Func<AdminUsersWrite> build, bool create, long generation, AdminUsersForm? passwordForm = null)
    {
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return null;
        var error = Text("");
        var current = Text("");
        var acknowledge = new CheckBox { Content = "I reviewed the fresh server values against this retained draft", Visibility = Visibility.Collapsed };
        var content = Panel(); content.Children.Add(editor); content.Children.Add(error); content.Children.Add(current); content.Children.Add(acknowledge);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = content, MaxHeight = 620 },
            PrimaryButtonText = "Apply", SecondaryButtonText = "Read back / review", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        _dialog = dialog; _dialogDirty = true;
        AdminUsersDocument? result = null;
        var dialogCompleted = false;
        IReadOnlyList<AdminUsersReadback>? refreshed = null;
        var readbackReady = false;
        acknowledge.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = readbackReady && !create && !_workspace.OutcomeUnknown;
        acknowledge.Unchecked += (_, _) => { if (readbackReady) dialog.IsPrimaryButtonEnabled = false; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            passwordForm?.ShowPasswordError(null);
            if (passwordForm?.PasswordProblem() is { } validation)
            {
                passwordForm.ShowPasswordError(validation);
                return;
            }
            var submittedPassword = passwordForm?.PasswordValue;
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false; editor.IsHitTestVisible = false;
            try
            {
                if (!_workspace.IsCurrent(generation)) return;
                if (_workspace.NeedsReview)
                {
                    if (!readbackReady || acknowledge.IsChecked != true || create || _workspace.OutcomeUnknown) throw new AdminUsersReviewRequiredException();
                    _workspace.AcceptReviewedReadback(generation);
                }
                var write = build();
                if (refreshed != null) write = write with { Readbacks = refreshed };
                result = await _workspace.WriteAsync(generation, _api, reviewed, write);
                if (dialogCompleted || !_workspace.IsCurrent(generation)) { await result.DiscardViewAsAsync(); result = null; return; }
                dialog.Hide();
            }
            catch (OperationCanceledException)
            {
                if (!_workspace.IsCurrent(generation)) dialog.Hide();
                else
                {
                    error.Text = _workspace.OutcomeUnknown
                        ? "The write outcome is unknown. Cancel and reload to read back/reconcile before choosing a separate action. Do not retry this draft."
                        : "The request was cancelled or timed out before a write was sent. Review the draft before trying again.";
                    dialog.IsPrimaryButtonEnabled = !_workspace.NeedsReview;
                }
            }
            catch (ApiException exception) when (passwordForm != null && NewPassword.FieldProblem(exception, "body.password") is not null)
            {
                if (!dialogCompleted && _workspace.IsCurrent(generation))
                {
                    error.Text = "";
                    if (submittedPassword == passwordForm.PasswordValue)
                        passwordForm.ShowPasswordError(NewPassword.FieldProblem(exception, "body.password"));
                    try
                    {
                        var policy = await ReadPasswordPolicyAsync(generation);
                        if (!dialogCompleted && _workspace.IsCurrent(generation)) passwordForm.ShowPasswordPolicy(policy);
                    }
                    catch (OperationCanceledException) { }
                    dialog.IsPrimaryButtonEnabled = !_workspace.NeedsReview;
                }
            }
            catch (Exception exception)
            {
                if (_workspace.IsCurrent(generation))
                {
                    error.Text = exception is ArgumentException or InvalidOperationException ? exception.Message : "Write refused or outcome unknown. Read back/review; never press Apply as an automatic retry.";
                    dialog.IsPrimaryButtonEnabled = !_workspace.NeedsReview;
                }
            }
            finally { editor.IsHitTestVisible = true; dialog.IsSecondaryButtonEnabled = true; deferral.Complete(); }
        };
        dialog.SecondaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false;
            try
            {
                if (!_workspace.IsCurrent(generation)) return;
                var write = build();
                reviewed = await FreshReviewAsync(generation);
                var fresh = new List<AdminUsersReadback>();
                foreach (var baseline in write.Readbacks ?? [])
                {
                    var value = await ReadResourceAsync(baseline.Path, generation);
                    fresh.Add(new(baseline.Path, value.Body!, value.ETag, baseline.Strong));
                }
                if (!_workspace.IsCurrent(generation)) return;
                refreshed = fresh;
                current.Text = "Fresh account readback (draft is not applied):\n" + reviewed.User.ToJsonString() +
                    string.Join("", fresh.Select(value => "\nResource " + value.Path + ": " + value.Body.ToJsonString()));
                readbackReady = _workspace.NeedsReview; acknowledge.IsChecked = false;
                acknowledge.Visibility = readbackReady && !create && !_workspace.OutcomeUnknown ? Visibility.Visible : Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = !readbackReady;
                error.Text = create || _workspace.OutcomeUnknown ? "Cancel and reload the current list or account to reconcile the outcome. Start a separate explicit action only if still needed. This draft cannot be replayed." : "Review the current values, then explicitly approve applying this retained draft.";
            }
            catch (OperationCanceledException)
            {
                if (!_workspace.IsCurrent(generation)) dialog.Hide();
                else error.Text = "Readback timed out or was cancelled. The draft remains blocked; cancel and reload to reconcile.";
            }
            catch (Exception exception) { error.Text = exception is ArgumentException ? exception.Message : "Readback failed. The draft remains blocked."; }
            finally { dialog.IsSecondaryButtonEnabled = true; deferral.Complete(); }
        };
        var handedOff = false;
        try
        {
            await dialog.ShowAsync();
            if (!_workspace.IsCurrent(generation)) return null;
            handedOff = true;
            return result;
        }
        finally
        {
            dialogCompleted = true;
            if (ReferenceEquals(_dialog, dialog)) { _dialog = null; _dialogDirty = false; }
            if (!handedOff && result != null) await result.DiscardViewAsAsync();
        }
    }
    private async Task ProfileAsync(string? id)
    {
        var generation = _generation;
        RequireGeneration(generation);
        var root = AdminUsersApi.UserPath(_review!.User.Text("id"));
        var fields = AdminUsersContract.Schema(id == null ? "ProfileCreate" : "ProfileUpdate")["properties"]!.AsObject().Select(pair => pair.Key).ToList();
        if (!Feature("profile_avatars")) fields.Remove("avatar");
        if (!Document("profile-flags").Flag("max_advisory_age_supported")) fields.Remove("max_advisory_age");
        if (!Document("profile-flags").Flag("require_advisory_age_supported")) fields.Remove("require_advisory_age");
        if (!Document("profile-flags").Flag("requests_allowed_supported")) fields.Remove("requests_allowed");
        JsonObject initial;
        var readbacks = new List<AdminUsersReadback>(Baselines("profiles"));
        if (id != null)
        {
            var path = root + "/profiles/" + AdminUsersApi.Segment(id);
            var full = await ReadResourceAsync(path, generation); initial = full.Body!;
            readbacks.Add(new(path, initial)); fields.Remove("pin");
        }
        else
        {
            var list = await ReadResourceAsync(root + "/profiles", generation);
            if (!JsonNode.DeepEquals(list.Body, readbacks.Single().Body)) throw new AdminUsersReviewRequiredException();
            initial = new JsonObject { ["name"] = "", ["is_child"] = false };
        }
        var primary = initial.Flag("is_primary") || id == null && Rows("profiles").Count == 0;
        if (primary) { fields.Remove("library_restrictions_enabled"); fields.Remove("allowed_library_ids"); }
        await EditAsync(id == null ? "Add profile" : "Edit profile", id == null ? "ProfileCreate" : "ProfileUpdate", initial,
            id == null ? AdminUsersOperation.CreateProfile : AdminUsersOperation.UpdateProfile, id, id == null, fields.ToArray(), readbacks: readbacks, expectedGeneration: generation);
    }
    private async Task ProfilePinAsync(JsonObject row, bool remove)
    {
        var generation = _generation;
        RequireGeneration(generation);
        var path = AdminUsersApi.UserPath(_review!.User.Text("id")) + "/profiles/" + AdminUsersApi.Segment(row.Text("id"));
        var full = await ReadResourceAsync(path, generation);
        var baseline = new[] { new AdminUsersReadback(path, full.Body!) };
        if (remove) await ConfirmAsync("Remove PIN for " + row.Text("name"), "This ends profile tokens issued for the old PIN and clears wrong-attempt lockout.",
            new(AdminUsersOperation.UpdateProfile, new JsonObject { ["pin"] = null }, row.Text("id"), Readbacks: baseline), expectedGeneration: generation);
        else await EditAsync("Set PIN for " + row.Text("name"), "ProfileUpdate", new JsonObject { ["pin"] = "" }, AdminUsersOperation.UpdateProfile, row.Text("id"), fields: ["pin"], readbacks: baseline, expectedGeneration: generation);
    }
    private async Task DeleteProfileAsync(JsonObject row)
    {
        var generation = _generation;
        RequireGeneration(generation);
        var path = AdminUsersApi.UserPath(_review!.User.Text("id")) + "/profiles/" + AdminUsersApi.Segment(row.Text("id"));
        var full = await ReadResourceAsync(path, generation);
        await ConfirmAsync("Delete profile " + row.Text("name"), "Permanently remove this profile and its per-profile data. Primary profiles cannot be deleted.",
            new(AdminUsersOperation.DeleteProfile, ResourceId: row.Text("id"), Readbacks: Baselines("profiles").Append(new AdminUsersReadback(path, full.Body!)).ToArray()), expectedGeneration: generation);
    }
    private async Task QuotaAsync()
    {
        var generation = _generation;
        RequireGeneration(generation);
        var path = "/api/v2/admin/request-users/" + AdminUsersApi.Segment(_review!.User.Text("id")) + "/limit";
        var full = await ReadResourceAsync(path, generation);
        var initial = AdminUserRequestTerms.ClearLegacy(full.Body!);
        var form = new AdminUsersForm(AdminUserRequestTerms.EditorSchema(), initial, true);
        var review = await FreshReviewAsync(generation);
        var result = await MutationDialogAsync("Request quota (independent ETag; blocked is legacy, not a new editor choice)", form.Content, review,
            () => new(AdminUsersOperation.Quota, form.Read(), Readbacks: [new(path, full.Body!, full.ETag, true)]), false, generation);
        if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }
    private async Task ResetQuotaAsync(bool legacyOnly)
    {
        var generation = _generation;
        RequireGeneration(generation);
        var path = "/api/v2/admin/request-users/" + AdminUsersApi.Segment(_review!.User.Text("id")) + "/limit";
        var full = await ReadResourceAsync(path, generation);
        if (!JsonNode.DeepEquals(full.Body, Document("quota"))) throw new AdminUsersReviewRequiredException();
        var body = legacyOnly ? AdminUserRequestTerms.ClearLegacy(full.Body!) : new JsonObject { ["limit_mode"] = "inherit", ["approval_mode"] = "inherit", ["max_requests"] = null, ["window_days"] = null };
        await ConfirmAsync(legacyOnly ? "Clear legacy block" : "Inherit request terms", "Only this account quota record changes. Library/playback policy is saved independently.",
            new(AdminUsersOperation.Quota, body, Readbacks: [new(path, full.Body!, full.ETag, true)]), expectedGeneration: generation);
    }
    private Task CopySensitiveAsync(string value)
    {
        if (!_workspace.IsCurrent(_generation)) return Task.CompletedTask;
        var package = new DataPackage(); package.SetText(value); Clipboard.SetContent(package);
        return Task.CompletedTask;
    }
    private async Task SensitiveResultAsync(JsonObject result, long generation)
    {
        RequireGeneration(generation);
        var content = Panel();
        foreach (var key in new[] { "delivery", "delivery_status", "expires_at" }) if (result.ContainsKey(key)) content.Children.Add(Text(Label(key) + ": " + result[key].Display()));
        if (result["invitation"] is JsonObject invitation) content.Children.Add(Text("Invitation expiry: " + invitation.Text("expires_at")));
        foreach (var key in new[] { "claim_url", "reset_url" })
        {
            var secret = result.Text(key);
            if (secret.Length == 0) continue;
            content.Children.Add(new TextBox { Text = secret, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(Action("Copy " + Label(key), () => CopySensitiveAsync(secret), nested: true));
        }
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Private result — share only with the recipient", Content = content, CloseButtonText = "Close" };
        _dialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { content.Children.Clear(); if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
}
