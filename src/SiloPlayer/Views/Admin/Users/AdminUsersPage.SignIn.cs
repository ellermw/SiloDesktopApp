using System.Text.Json.Nodes;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private void RenderSignIn()
    {
        var user = _review!.User;
        ObjectCard("Password sign-in", user, ["password_login", "password_change_required", "break_glass"]);
        _body.Children.Add(Text("Setting a password re-enables local password sign-in. Linking a non-network external provider normally turns it off unless this is a break-glass admin. The server refuses removal of the last viable sign-in method or break-glass admin."));
        if (Manage)
        {
            if (PasswordManage) _body.Children.Add(Action(user.Text("id") == _review.Viewer.Text("id") ? "Set my account password" : "Set account password", SetPasswordAsync));
            else _body.Children.Add(Text("Only the Owner can re-enable their own disabled local password sign-in. Use the active external method or ask the Owner to recover this account."));
            if (_review.Viewer.Flag("is_owner") && user.Text("role") == "admin" && AdminUsersRules.Feature(Document("external-capabilities"), "break_glass"))
                _body.Children.Add(Action("Configure break-glass", () => EditAsync("Break-glass password access", "AdminAccountUpdateBody", user, AdminUsersOperation.UpdateAccount, fields: ["break_glass"])));
        }
        if (AdminUsersRules.Feature(Document("external-capabilities"), "admin_identities"))
        {
            if (Manage) _body.Children.Add(Action("Link external identity", LinkIdentityAsync));
            if (_rows.GetValueOrDefault("providers") is { Complete: false }) _body.Children.Add(Action("Load next provider page", () => MoreAsync("providers")));
            AddRows("identities", "External identities", ["provider_name", "username", "email", "display_name", "external_subject", "issuer", "linked_at", "last_sign_in_at", "last_check_status", "last_checked_at"], (panel, row) =>
            {
                if (Manage) panel.Children.Add(Action("Unlink identity", async () =>
                {
                    var generation = _generation;
                    var path = AdminUsersApi.UserPath(user.Text("id")) + "/identities";
                    var baseline = await ReadResourceAsync(path, generation);
                    await ConfirmAsync("Unlink " + row.Text("provider_name"), "Only this exact identity is removed. The server protects the last viable sign-in method.",
                        new(AdminUsersOperation.UnlinkIdentity, ResourceId: row.Text("id"), Readbacks: [new(path, baseline.Body!)]), expectedGeneration: generation);
                }));
            });
        }
        else _body.Children.Add(Text("External identity management is unsupported or disallowed for this authority."));
        if (Feature("login_sessions") && AdminUsersRules.Feature(Document("sessions-capabilities"), "admin_management"))
        {
            if (Manage) _body.Children.Add(Action("Sign out all login sessions", () => ConfirmAsync("Sign out everywhere", "This ends all login sessions for this account. If it is your account, this also signs you out; it is not a playback-device action.",
                new(AdminUsersOperation.RevokeSessions, Readbacks: Baselines("login-sessions")))));
            AddRows("login-sessions", "Login sessions", ["id", "device_name", "device_platform", "ip_address", "created_at", "expires_at", "last_seen_at", "current"], (panel, row) =>
            {
                if (Manage) panel.Children.Add(Action("Sign out this login session", () => ConfirmAsync("Revoke login session", "Revoke only session #" + row.Text("id") + (row.Flag("current") ? ". This is the current sign-in." : "."),
                    new(AdminUsersOperation.RevokeSession, ResourceId: row.Text("id"), Readbacks: Baselines("login-sessions")))));
            });
        }
        else _body.Children.Add(Text("Login-session administration is unsupported or disallowed. Device registration is a different capability."));
    }
    private Task LinkIdentityAsync()
    {
        var candidates = Rows("providers").Where(installation =>
        {
            var capability = (installation["capabilities"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(item => item.Text("type") == "auth_provider.v1");
            return capability != null && (installation["auth_bindings"] as JsonArray)?.OfType<JsonObject>().Any(binding => binding.Text("capability_id") == capability.Text("id")) == true;
        }).Select(installation => (installation.Text("id"), installation["metadata"].Text("name", installation.Text("plugin_id")))).ToArray();
        if (candidates.Length == 0) { _notice.Text = "No loaded auth-provider installation has a sign-in binding. Load the next bounded provider page if available."; return Task.CompletedTask; }
        IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Name)>> choices = new Dictionary<string, IReadOnlyList<(string Id, string Name)>> { ["installation_id"] = candidates };
        return EditAsync("Link an exact external identity", "AdminUserIdentityCreateInputBody", new JsonObject { ["installation_id"] = candidates[0].Item1, ["external_subject"] = "" },
            AdminUsersOperation.LinkIdentity, create: true, choices: choices, readbacks: Baselines("providers"));
    }
}
