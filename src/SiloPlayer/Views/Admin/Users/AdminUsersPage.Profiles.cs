using SiloPlayer.Core.Api;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersFormKit;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private static string P(string key, string fallback, params (string Name, object? Value)[] args) =>
        AppStrings.Get(key, args.Length == 0 ? null : args.ToDictionary(pair => pair.Name, pair => pair.Value), fallback);

    /// <summary>
    /// Add profile and Edit profile (official <c>ProfileEditorDialog</c>). The profile is read fresh for an edit, the account's own list for an add, and the write is sent under
    /// those reads: a profile or a list that changed meanwhile is never overwritten.
    /// </summary>
    private async Task ProfileAsync(string? id)
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = T("admin.native.users.refreshedNotice"); await LoadAsync(_location); return; }
        var review = await FreshReviewAsync(generation);
        await LoadChoicesAsync(generation, false, true);
        var root = AdminUsersApi.UserPath(review.User.Text("id"));
        var readbacks = new List<AdminUsersReadback>(Baselines("profiles"));
        JsonObject? saved = null;
        if (id != null)
        {
            var path = root + "/profiles/" + AdminUsersApi.Segment(id);
            var full = await ReadResourceAsync(path, generation); saved = full.Body!;
            readbacks.Add(new(path, saved));
        }
        else
        {
            var list = await ReadResourceAsync(root + "/profiles", generation);
            if (!JsonNode.DeepEquals(list.Body, readbacks.Single().Body)) throw new AdminUsersReviewRequiredException();
        }
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;
        var flags = Document("profile-flags");
        var support = new AdminProfileSupport(flags.Flag("max_advisory_age_supported"), flags.Flag("require_advisory_age_supported"), flags.Flag("requests_allowed_supported"), Feature("profile_avatars"));
        var primary = saved?.Flag("is_primary") == true || id == null && Rows("profiles").Count == 0;
        // The libraries this account can see are the ones a profile can be limited to.
        var allowed = (review.User["effective_policy"]?["library_ids"] as JsonArray)?.Select(node => node?.ToString()).ToHashSet();
        var libraries = Rows("library-choices").Where(row => allowed == null || allowed.Contains(row["id"]?.ToString())).ToArray();
        var form = new AdminUsersProfileForm(saved, primary, libraries, support, support.Avatars ? (reference, cancellation) => _workspace.ReadAsync(generation, async (lease, token) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
            var bytes = await App.Services.GetRequiredService<AdminUserAvatarImages>().ReadAsync(reference, new Uri(lease.Context.BaseUrl), linked.Token);
            lease.ThrowIfRetired();
            return bytes;
        }) : null, _workspace.Token);
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(Text(P("web.profiles.editor.description", ""), 14, true)); body.Children.Add(form.Content);
        try
        {
            var spec = new FormDialogSpec
            {
                Title = id == null ? P("web.profiles.editor.titleNew", "New profile") : P("web.profiles.editor.titleEdit", "Edit profile"), Body = body, Review = review, MaxWidth = 768,
                Primary = P("web.profiles.editor.save", "Save profile"), Saving = P("web.profiles.editor.saving", "Saving..."), AutomationId = "ProfileEditorForm", Created = id == null,
                FailureText = T("admin.native.users.profileSaveFailed"),
                CanSave = () => id == null || form.Body().Count > 0, OnChanged = callback => form.Changed += callback,
                Build = _ => !form.Validate() ? null : new AdminUsersWrite(id == null ? AdminUsersOperation.CreateProfile : AdminUsersOperation.UpdateProfile, form.Body(), id, Readbacks: readbacks),
            };
            var result = await FormDialogAsync(spec, generation);
            if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
        }
        finally { form.Dispose(); }
    }

    /// <summary>Set PIN / Change PIN (official <c>SetPinDialog</c>): one four-digit PIN, saved under the profile's own read.</summary>
    private async Task SetPinAsync(JsonObject row)
    {
        var generation = _generation;
        RequireGeneration(generation);
        if (_dialog != null || _workspace.NeedsReview) { _notice.Text = T("admin.native.users.refreshedNotice"); await LoadAsync(_location); return; }
        var review = await FreshReviewAsync(generation);
        var path = AdminUsersApi.UserPath(review.User.Text("id")) + "/profiles/" + AdminUsersApi.Segment(row.Text("id"));
        var full = await ReadResourceAsync(path, generation);
        RequireGeneration(generation);
        if (!_workspace.IsCurrent(generation) || XamlRoot == null) return;
        var baseline = new[] { new AdminUsersReadback(path, full.Body!) };
        var hasPin = row.Flag("has_pin");
        var primary = row.Flag("is_primary") ? "yes" : "no";
        var pin = new PasswordBox { MaxLength = 4, PlaceholderText = T("web.admin.users.detail.profiles.pinPlaceholder") };
        AutomationProperties.SetAutomationId(pin, "ProfileNewPin"); AutomationProperties.SetName(pin, T("web.admin.users.detail.profiles.newPin"));
        var problem = Problem();
        var syncing = false;
        pin.PasswordChanged += (_, _) =>
        {
            if (syncing) return;
            var digits = new string(pin.Password.Where(char.IsDigit).Take(4).ToArray());
            if (digits != pin.Password) { syncing = true; pin.Password = digits; syncing = false; }
            Say(problem, null);
        };
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(Text(T(hasPin ? "web.admin.users.detail.profiles.changePinDescription" : "web.admin.users.detail.profiles.setPinDescription", ("primary", primary)), 14, true));
        body.Children.Add(Field(T("web.admin.users.detail.profiles.newPin"), pin, problem));
        var spec = new FormDialogSpec
        {
            Title = T(hasPin ? "web.admin.users.detail.profiles.changePinTitle" : "web.admin.users.detail.profiles.setPinTitle", ("name", row.Text("name"))), Body = body, Review = review, MaxWidth = 384,
            Primary = T("web.admin.users.detail.profiles.savePin"), Saving = T("web.admin.users.detail.common.saving"), AutomationId = "ProfilePinForm",
            Build = _ =>
            {
                if (!AdminProfileDraft.PinOk(pin.Password)) { Say(problem, T("web.admin.users.detail.profiles.pinInvalid")); pin.Focus(FocusState.Programmatic); return null; }
                return new AdminUsersWrite(AdminUsersOperation.UpdateProfile, new JsonObject { ["pin"] = pin.Password }, row.Text("id"), Readbacks: baseline);
            },
        };
        var result = await FormDialogAsync(spec, generation);
        if (result != null) { RequireGeneration(generation); await LoadAsync(_location); }
    }
}
