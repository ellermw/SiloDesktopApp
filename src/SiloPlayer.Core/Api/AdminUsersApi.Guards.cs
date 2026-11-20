using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminUsersApi
{
    private static void RequireCompleteChain(string path, IReadOnlyList<AdminUsersReadback> pages)
    {
        if (pages.Count is < 1 or > 10) throw new InvalidDataException("A complete bounded readback chain is required.");
        string? cursor = null; var seen = new HashSet<string>(StringComparer.Ordinal); var count = 0;
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            if (page.Path != AdminPaging.WithPage(path, path.EndsWith("/settings/values", StringComparison.Ordinal) ? 200 : 100, cursor)) throw new InvalidDataException("Readback pages do not describe one exact collection.");
            if (page.Body["items"] is not JsonArray items || items.Any(item => item is not JsonObject)) throw new InvalidDataException("Invalid readback rows.");
            count += items.Count;
            if (count > MaxRows || page.Body["page"] is not JsonObject state || state["has_more"] is not JsonValue more || !more.TryGetValue<bool>(out _)) throw new InvalidDataException("Invalid bounded readback collection.");
            if (!state.Flag("has_more"))
            {
                if (index != pages.Count - 1 || state.Text("next_cursor").Length > 0) throw new InvalidDataException("Unexpected readback continuation.");
                return;
            }
            cursor = state.Text("next_cursor");
            if (!AdminPaging.IsValidCursor(cursor) || !seen.Add(cursor) || page.Body.Items().Count == 0) throw new InvalidDataException("Invalid readback continuation.");
        }
        throw new InvalidDataException("The readback collection is partial.");
    }

    private async Task GuardNestedWriteAsync(MonitorAuthorityLease lease, AdminUserReview fresh, AdminUsersWrite write, JsonObject? body, CancellationToken token)
    {
        var root = UserPath(fresh.User.Text("id"));
        var operation = write.Operation;
        void Require(bool permitted)
        {
            if (!permitted) throw new InvalidOperationException("The exact resource, capability or reviewed identity does not permit this action.");
        }
        async Task<JsonObject> Read(string path) => (await ReadAsync(lease, path, token).ConfigureAwait(false)).Body!;
        IReadOnlyList<AdminUsersReadback> Baseline(string prefix)
        {
            var rows = (write.Readbacks ?? []).Where(row => row.Path == prefix || row.Path.StartsWith(prefix + "?", StringComparison.Ordinal)).ToArray();
            Require(rows.Length > 0);
            return rows;
        }
        if (operation is AdminUsersOperation.CreateUser or AdminUsersOperation.UpdateAccount or AdminUsersOperation.UpdatePolicy)
        {
            if (body!.ContainsKey("access_group_id")) Require(AdminUsersRules.Feature(fresh.Capabilities, "access_groups"));
            if (body.ContainsKey("max_profiles")) Require(AdminUsersRules.Feature(fresh.Capabilities, "profile_limits"));
        }
        if (operation is AdminUsersOperation.CreateProfile or AdminUsersOperation.UpdateProfile or AdminUsersOperation.DeleteProfile)
        {
            var flags = await Read("/api/v2/profiles");
            if (body?.ContainsKey("avatar") == true) Require(AdminUsersRules.Feature(fresh.Capabilities, "profile_avatars"));
            foreach (var flag in new[] { "max_advisory_age", "require_advisory_age", "requests_allowed" })
                if (body?.ContainsKey(flag) == true) Require(flags.Flag(flag + "_supported"));
            if (operation == AdminUsersOperation.CreateProfile)
            {
                var profiles = Baseline(root + "/profiles").Single().Body.Items();
                var limit = fresh.User["effective_policy"].Count("max_profiles");
                Require(limit < 1 || profiles.Count < limit);
                AdminUsersRules.ValidateProfile(body!, profiles.Count == 0);
            }
            if (operation == AdminUsersOperation.DeleteProfile) Require(Baseline(root + "/profiles").Single().Body.Items().Count > 1);
        }
        if (operation == AdminUsersOperation.LinkIdentity)
        {
            var installations = Baseline("/api/v2/admin/plugins/installations").SelectMany(row => row.Body.Items()).OfType<JsonObject>();
            var selected = installations.SingleOrDefault(row => row.Text("id") == body!.Text("installation_id"));
            Require(selected != null);
            var providers = (selected!["capabilities"] as JsonArray ?? []).OfType<JsonObject>().Where(row => row.Text("type") == "auth_provider.v1");
            Require(providers.Any(capability => (selected["auth_bindings"] as JsonArray ?? []).OfType<JsonObject>().Any(binding => binding.Text("capability_id") == capability.Text("id"))));
            body!["external_subject"] = body.Text("external_subject").Trim();
            Require(body.Text("external_subject").Length > 0);
        }
        if (operation == AdminUsersOperation.UnlinkIdentity)
            Require(Baseline(root + "/identities").SelectMany(row => row.Body.Items()).Any(row => row.Text("id") == write.ResourceId));
        if (operation is AdminUsersOperation.RevokeSession or AdminUsersOperation.RevokeSessions)
        {
            var sessions = Baseline(root + "/login-sessions");
            if (operation == AdminUsersOperation.RevokeSession) Require(sessions.SelectMany(row => row.Body.Items()).Any(row => row.Text("id") == write.ResourceId));
            else RequireCompleteChain(root + "/login-sessions", sessions);
        }
        if (operation is AdminUsersOperation.SetSetting or AdminUsersOperation.ResetSetting)
        {
            var identity = write.Identity ?? throw new ArgumentException("Missing exact preference identity.");
            identity.Validate();
            var capabilities = await Read("/api/v2/settings/contract/capabilities");
            Require(AdminUsersRules.Available(capabilities));
            Require((capabilities["scopes"] as JsonArray ?? []).Any(scope => scope?.GetValue<string>() == identity.Scope));
            if (identity.ClientFamily != null) Require((capabilities["client_families"] as JsonArray ?? []).Any(family => family?.GetValue<string>() == identity.ClientFamily));
            if (identity.ProfileId != null) Require((await Read(root + "/profiles/" + Segment(identity.ProfileId))).Text("id") == identity.ProfileId);
            var baseline = Baseline(root + "/settings/values");
            RequireCompleteChain(root + "/settings/values", baseline);
            if (operation == AdminUsersOperation.SetSetting)
            {
                var manifest = await Read("/api/v2/settings/contract");
                var definition = (manifest["definitions"] as JsonArray ?? []).OfType<JsonObject>().SingleOrDefault(row => row.Text("key") == write.SettingKey);
                Require(definition != null && AdminUserPreferences.Supports(definition, identity) && definition.Count("introduced_in") <= capabilities.Count("manifest_revision"));
                Require(body is { Count: 1 } && body.ContainsKey("value"));
                AdminUsersContract.ValidateValue(write.SettingKey!, AdminUserPreferences.ValueDefinition(definition!), body!["value"]);
            }
            else Require(baseline.SelectMany(row => row.Body.Items()).OfType<JsonObject>().Any(row => row.Text("key") == write.SettingKey && AdminUserSettingIdentity.FromRow(row) == identity));
        }
        if (operation is AdminUsersOperation.SaveSections or AdminUsersOperation.ResetSections)
        {
            Require((await Read(root + "/profiles/" + Segment(write.ResourceId!))).Text("id") == write.ResourceId);
            if (operation == AdminUsersOperation.SaveSections)
            {
                Require(body is { Count: 1 } && body["overrides"] is JsonArray { Count: <= 500 });
                foreach (var row in body!["overrides"]!.AsArray())
                    AdminUsersContract.Validate(AdminUsersContract.Schema("SectionOverrideWrite"), row?.AsObject() ?? throw new ArgumentException("Invalid section override."));
            }
        }
        if (operation is AdminUsersOperation.CreateInvitation or AdminUsersOperation.ResendInvitation)
        {
            var capability = await Read("/api/v2/admin/invitations/capabilities");
            if (operation == AdminUsersOperation.CreateInvitation)
            {
                var profile = !body!.ContainsKey("create_profile") || body.Flag("create_profile");
                Require(profile ? capability.Flag("default_profile") : capability.Flag("profileless"));
                Require(body.Text("delivery") is "link" or "email");
                Require(body.Text("delivery") == "link" ? !body.ContainsKey("email") : body.Text("email").Trim().Length > 0 && capability.Flag("email_delivery"));
            }
            else
            {
                var invitation = Baseline("/api/v2/admin/invitations/" + Segment(write.ResourceId!)).Single().Body;
                Require(invitation.Text("status") is "pending" or "expired");
                Require(!invitation.Flag("create_profile") || capability.Flag("default_profile"));
                Require(body?.Text("delivery") != "email" || invitation.Text("email").Length > 0 && capability.Flag("email_delivery"));
            }
        }
        if (operation == AdminUsersOperation.RevokeInvitation) Baseline("/api/v2/admin/invitations/" + Segment(write.ResourceId!));
        if (operation is AdminUsersOperation.UpdateCode or AdminUsersOperation.TopUpCode or AdminUsersOperation.DeleteCode)
            Require(Baseline("/api/v2/admin/invite-codes").SelectMany(row => row.Body.Items()).Any(row => row.Text("id") == write.ResourceId));
    }
}
