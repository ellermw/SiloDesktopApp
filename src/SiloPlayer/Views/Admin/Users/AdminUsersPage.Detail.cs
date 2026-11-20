using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private string DetailTab => new[] { "overview", "profiles", "access", "sign-in", "activity", "downloads", "preferences" }.Contains(_location.Get("tab")) ? _location.Get("tab")! : "overview";
    private async Task ReadDetailAsync(long generation)
    {
        var root = AdminUsersApi.UserPath(_location.Segment(0)!);
        if (!AdminUsersRules.Available(_review?.Capabilities)) return;
        switch (DetailTab)
        {
            case "overview":
                await RowsAsync("profiles", root + "/profiles", generation, true);
                if (Feature("request_usage")) await DocumentAsync("usage", "/api/v2/admin/request-users/" + AdminUsersApi.Segment(_location.Segment(0)!) + "/usage", generation);
                if (Feature("watch_summary")) await DocumentAsync("watch-summary", root + "/watch-summary?days=30", generation);
                await RowsAsync("watching", "/api/v2/admin/sessions?user_id=" + AdminUsersApi.Segment(_location.Segment(0)!), generation);
                await RowsAsync("history", HistoryPath(), generation, limit: 25);
                break;
            case "profiles":
                await RowsAsync("profiles", root + "/profiles", generation, true);
                await DocumentAsync("profile-flags", "/api/v2/profiles", generation);
                break;
            case "access":
                if (Feature("access_groups")) await RowsAsync("groups", "/api/v2/admin/access-groups", generation);
                if (Feature("policy_defaults")) await DocumentAsync("defaults", "/api/v2/admin/users/policy-defaults", generation);
                await DocumentAsync("quota", "/api/v2/admin/request-users/" + AdminUsersApi.Segment(_location.Segment(0)!) + "/limit", generation);
                await DocumentAsync("request-defaults", "/api/v2/admin/request-settings", generation);
                if (_review!.User.Text("role") != "admin" && _review.User.Text("access_group_id").Length > 0) await DocumentAsync("group-quota", "/api/v2/admin/request-groups/" + AdminUsersApi.Segment(_review.User.Text("access_group_id")) + "/limit", generation);
                if (Feature("request_usage")) await DocumentAsync("usage", "/api/v2/admin/request-users/" + AdminUsersApi.Segment(_location.Segment(0)!) + "/usage", generation);
                break;
            case "sign-in":
                await DocumentAsync("external-capabilities", "/api/v2/auth/external-sign-in/capabilities", generation);
                if (AdminUsersRules.Feature(Document("external-capabilities"), "admin_identities"))
                {
                    await RowsAsync("identities", root + "/identities", generation, true);
                    await RowsAsync("providers", "/api/v2/admin/plugins/installations", generation);
                }
                if (Feature("login_sessions"))
                {
                    await DocumentAsync("sessions-capabilities", "/api/v2/auth/sessions/capabilities", generation);
                    if (AdminUsersRules.Feature(Document("sessions-capabilities"), "admin_management")) await RowsAsync("login-sessions", root + "/login-sessions", generation);
                }
                break;
            case "activity":
                if (Feature("account_devices")) await RowsAsync("devices", root + "/devices", generation, true);
                await RowsAsync("profiles", root + "/profiles", generation, true);
                await RowsAsync("ips", root + "/ips?days=" + _historyDays, generation);
                await RowsAsync("history", HistoryPath(), generation);
                break;
            case "downloads":
                if (!Feature("account_downloads")) return;
                await DocumentAsync("download-summary", root + "/downloads/summary", generation);
                await RowsAsync("downloads", root + "/downloads", generation);
                await RowsAsync("subscriptions", root + "/download-subscriptions", generation);
                await RowsAsync("profiles", root + "/profiles", generation, true);
                if (Feature("account_devices")) await RowsAsync("devices", root + "/devices", generation, true);
                break;
            case "preferences": await ReadPreferencesAsync(generation, root); break;
        }
    }
    private string HistoryPath()
    {
        var after = DateTimeOffset.UtcNow.AddDays(-int.Parse(_historyDays, CultureInfo.InvariantCulture)).ToString("O", CultureInfo.InvariantCulture);
        return "/api/v2/admin/playback-history?user_id=" + AdminUsersApi.Segment(_location.Segment(0)!) + "&ended_after=" + Uri.EscapeDataString(after) +
            (_historyProfile.Length == 0 ? "" : "&profile_id=" + AdminUsersApi.Segment(_historyProfile));
    }
    private void RenderDetail()
    {
        var user = _review!.User;
        var header = Panel();
        header.Children.Add(Action("All users", () => { Navigate(null, "users"); return Task.CompletedTask; }));
        header.Children.Add(new PersonPicture { DisplayName = user.Text("username"), Width = 64, Height = 64 });
        header.Children.Add(Text(user.Text("username"), 28));
        header.Children.Add(Text($"{(user.Flag("is_owner") ? "Owner" : Label(user.Text("role")))} · {(user.Flag("enabled") ? "Enabled" : "Disabled")} · {user.Text("email")} · Group {user["access_group_id"].Display()}"));
        header.Children.Add(Text($"Created {user.Text("created_at")} · Last active {user["last_active_at"].Display()}"));
        if (!Manage) header.Children.Add(Text("Read only: this account is protected by the server Owner rules."));
        var actions = Inline();
        if (AdminUsersRules.CanViewAs(user, _review.Viewer.Text("id"), _review.Viewer.Flag("is_owner"))) actions.Children.Add(Action("View as", BeginViewAsAsync));
        if (Manage)
        {
            if (PasswordManage) actions.Children.Add(Action("Set password", () => SetPasswordAsync()));
            if (user.Flag("password_login") && Feature("password_reset_link")) actions.Children.Add(Action("Password reset link", () => ConfirmAsync("Issue password reset link", "This replaces any earlier reset link. Share the generated link privately.", new(AdminUsersOperation.ResetLink), sensitiveResult: true)));
            if (user.Flag("password_login") && Feature("password_reset_email")) actions.Children.Add(Action("Email password reset", () => ConfirmAsync("Email password reset", "This replaces the earlier reset link and attempts delivery to the account address.", new(AdminUsersOperation.ResetEmail), sensitiveResult: true)));
            if (!user.Flag("is_owner") && user.Text("id") != _review.Viewer.Text("id"))
            {
                actions.Children.Add(Action(user.Flag("enabled") ? "Disable account" : "Enable account", () => ConfirmAsync("Change account availability", "Disabling ends sign-ins and playback authority. Review the exact account before confirming.",
                    new(AdminUsersOperation.UpdateAccount, new JsonObject { ["enabled"] = !user.Flag("enabled") }))));
                actions.Children.Add(Action("Delete account", () => ConfirmAsync("Delete " + user.Text("username"), "Permanently delete this account, its profiles and account data. This cannot be undone.", new(AdminUsersOperation.DeleteAccount))));
            }
        }
        if (Feature("ownership_transfer") && AdminUsersRules.CanTransfer(user, _review.Viewer.Text("id"), _review.Viewer.Flag("is_owner")))
            actions.Children.Add(Action("Transfer ownership", () => ConfirmAsync("Transfer server ownership", "The selected enabled admin becomes Owner; you remain an admin and lose Owner-only authority.", new(AdminUsersOperation.Transfer))));
        header.Children.Add(actions); _body.Children.Add(header);
        var tabs = Inline();
        foreach (var tab in new[] { "overview", "profiles", "access", "sign-in", "activity", "downloads", "preferences" })
            tabs.Children.Add(Action(Label(tab), () => { Navigate(user.Text("id"), tab); return Task.CompletedTask; }));
        _body.Children.Add(tabs);
        if (!AdminUsersRules.Available(_review.Capabilities)) { _body.Children.Add(Text("This server does not currently allow account management.")); return; }
        switch (DetailTab)
        {
            case "overview": RenderOverview(); break;
            case "profiles": RenderProfiles(); break;
            case "access": RenderAccess(); break;
            case "sign-in": RenderSignIn(); break;
            case "activity": RenderActivity(); break;
            case "downloads": RenderDownloads(); break;
            case "preferences": RenderPreferences(); break;
        }
    }
    private void ObjectCard(string title, JsonObject? row, IEnumerable<string>? keys = null)
    {
        var panel = Panel();
        if (row == null) panel.Children.Add(Text("Not supported or not loaded."));
        else foreach (var key in keys ?? row.Select(pair => pair.Key)) panel.Children.Add(Text(Label(key) + ": " + row[key].Display()));
        _body.Children.Add(Card(title, panel));
    }
    private void RenderOverview()
    {
        var stats = Panel();
        var effective = _review!.User["effective_policy"];
        var watching = Rows("watching");
        stats.Children.Add(Text("Streams now: " + watching.Count + " / " + effective.Count("max_streams") + " (0: no cap)"));
        stats.Children.Add(Text("Video transcodes now: " + (effective.Flag("transcode_allowed") ? watching.Count(row => AdminUsersPresentation.IsVideoTranscode(row)).ToString() : "Off") + " / " + effective.Count("max_transcodes")));
        stats.Children.Add(Text("Profiles: " + Rows("profiles").Count + " / " + effective.Count("max_profiles")));
        if (_rows.GetValueOrDefault("watching") is not { Complete: true }) stats.Children.Add(Text("Live statistics are partial or unavailable, not complete account counts."));
        _body.Children.Add(Card("Account usage now", stats));
        if (Feature("request_usage")) ObjectCard("Requests in current window", Document("usage"));
        if (Feature("watch_summary")) ObjectCard("Last 30 days", Document("watch-summary"));
        ObjectCard("Details", _review!.User, ["id", "email", "role", "is_owner", "enabled", "created_at", "last_active_at"]);
        PolicyCard("Access summary", ["library_ids", "max_profiles", "max_streams", "max_transcodes", "max_playback_quality", "download_allowed", "requests_allowed"]);
        AddRows("profiles", "Profiles", ["name", "is_primary", "is_child", "has_pin", "last_seen_at"]);
        AddRows("watching", "Currently watching", ["profile_id", "profile_name", "media_title", "media_type", "play_method", "started_at"]);
        AddRows("history", "Recently watched", ["profile_id", "profile_name", "media_title", "series_title", "started_at", "ended_at", "watched_seconds", "completed"]);
    }
    private void RenderProfiles()
    {
        var limit = (_review!.User["effective_policy"] as JsonObject).Count("max_profiles");
        _body.Children.Add(Text($"{Rows("profiles").Count} profiles · effective limit {limit}. Primary profiles cannot be deleted or library restricted."));
        if (ProfilesManage && _rows.ContainsKey("profiles")) _body.Children.Add(Action("Add profile", () => ProfileAsync(null), limit < 1 || Rows("profiles").Count < limit));
        else _body.Children.Add(Text("Profiles/PIN are read only: profile_management support and the server Owner rule are required."));
        AddRows("profiles", "Household profiles", ["id", "name", "is_primary", "is_child", "has_pin", "avatar", "avatar_source", "last_seen_at"], (panel, profile) =>
        {
            if (!ProfilesManage) return;
            panel.Children.Add(Action("Edit profile", () => ProfileAsync(profile.Text("id"))));
            panel.Children.Add(Action(profile.Flag("has_pin") ? "Change PIN" : "Set PIN", () => ProfilePinAsync(profile, false)));
            if (profile.Flag("has_pin")) panel.Children.Add(Action("Remove PIN", () => ProfilePinAsync(profile, true)));
            if (!profile.Flag("is_primary") && Rows("profiles").Count > 1) panel.Children.Add(Action("Delete profile", () => DeleteProfileAsync(profile)));
        });
    }
    private void PolicyCard(string title, string[] fields, bool editable = false)
    {
        var user = _review!.User;
        var effective = user["effective_policy"] as JsonObject;
        var panel = Panel();
        foreach (var field in fields)
        {
            var source = user[field] != null ? "Account override" : user.Text("role") != "admin" && user.Text("access_group_id").Length > 0 ? "Access group " + user.Text("access_group_id") : "Server default";
            var value = effective?[field];
            var text = field == "library_ids" ? value == null ? "All libraries" : value is JsonArray { Count: 0 } ? "No libraries" : value.Display() : value.Display();
            panel.Children.Add(Text(Label(field) + ": " + text + " · " + source + " · stored " + user[field].Display()));
        }
        if (editable && Policy) panel.Children.Add(Action("Edit " + title, () => EditAsync(title, "AdminAccountUpdateBody", user, AdminUsersOperation.UpdatePolicy, fields: fields)));
        _body.Children.Add(Card(title, panel));
    }
    private string[] AccountLockedFields()
    {
        var user = _review!.User;
        var locked = new List<string>();
        if (!_review.Viewer.Flag("is_owner") || user.Flag("is_owner") || user.Text("id") == _review.Viewer.Text("id")) locked.Add("role");
        if (user.Flag("is_owner") || user.Text("id") == _review.Viewer.Text("id")) locked.Add("enabled");
        if (!Policy) locked.Add("max_profiles");
        return locked.ToArray();
    }
    private void RenderAccess()
    {
        var user = _review!.User;
        ObjectCard("Sign-in / account access", user, ["username", "email", "role", "enabled", "max_profiles"]);
        if (Manage) _body.Children.Add(Action("Edit account", () => EditAsync("Account access", "AdminAccountUpdateBody", user, AdminUsersOperation.UpdateAccount,
            fields: ["username", "email", "role", "enabled", "max_profiles"], locked: AccountLockedFields())));
        PolicyCard("Library access", ["access_group_id", "library_ids", "permissions"], true);
        PolicyCard("Playback", ["max_playback_quality", "max_streams", "max_transcodes", "transcode_allowed", "audio_transcode_allowed", "max_remote_stream_bitrate_kbps", "max_local_stream_bitrate_kbps"], true);
        PolicyCard("Downloads policy", ["download_allowed", "download_transcode_allowed"], true);
        PolicyCard("Requests", ["requests_allowed"], true);
        ObjectCard("Account request quota", Document("quota"));
        ObjectCard("Inherited group request quota", Document("group-quota"));
        ObjectCard("Server request defaults", Document("request-defaults"), ["requests_enabled", "global_max_requests", "global_window_days", "global_auto_approval_enabled"]);
        _body.Children.Add(Text("Effective request terms: " + AdminUserRequestTerms.Describe(user, Document("quota"), Document("group-quota"), Document("request-defaults"))));
        _body.Children.Add(Text("After inheritance: " + AdminUserRequestTerms.Describe(user, Document("quota"), Document("group-quota"), Document("request-defaults"), true)));
        ObjectCard("Request usage", Document("usage"));
        if (Policy)
        {
            _body.Children.Add(Action("Edit request quota", QuotaAsync));
            _body.Children.Add(Action("Use inherited request terms", () => ResetQuotaAsync(false)));
            if (Document("quota").Text("limit_mode") == "blocked" || Document("quota").Text("approval_mode") == "blocked")
                _body.Children.Add(Action("Clear legacy request block", () => ResetQuotaAsync(true)));
        }
        if (!Policy) _body.Children.Add(Text("This access policy is read only. Only Owner may change an admin policy, including their own."));
    }
    private void RenderActivity()
    {
        var filters = Inline();
        var profiles = Choice("Watch profile", new[] { ("", "All profiles") }.Concat(Rows("profiles").Select(row => (row.Text("id"), row.Text("name")))), _historyProfile);
        var period = Choice("Days", new[] { "7", "30", "90", "365" }.Select(value => (value, value)), _historyDays);
        filters.Children.Add(profiles); filters.Children.Add(period);
        filters.Children.Add(Action("Apply activity filters", () => { _historyProfile = Selected(profiles); _historyDays = Selected(period); return LoadAsync(_location); }));
        _body.Children.Add(filters);
        AddRows("devices", "Devices and profile references", ["device_id", "device_name", "device_platform", "last_seen_at", "last_updated", "override_count", "profiles"], DeviceLink);
        AddRows("ips", "IP addresses", ["client_ip", "first_seen", "last_seen", "request_count", "location"]);
        AddRows("history", "Watch history", ["profile_id", "profile_name", "media_item_id", "media_title", "series_title", "season_number", "episode_number", "play_method", "started_at", "ended_at", "watched_seconds", "duration_seconds", "completed"]);
    }
    private void DeviceLink(StackPanel panel, JsonObject row)
    {
        var device = row.Text("device_id");
        if (device.Length == 0) return;
        panel.Children.Add(Action("Open exact account/device scope", () =>
        {
            var request = new AdminRouteRequest(AdminRoute.Devices) { Segments = [_review!.User.Text("id"), device] };
            if (!AdminShellPage.Open(request)) _notice.Text = "The device lane is not registered yet. Scope: " + request.Href;
            return Task.CompletedTask;
        }));
    }
    private void RenderDownloads()
    {
        if (!Feature("account_downloads")) { _body.Children.Add(Text("Account downloads are unsupported by this server.")); return; }
        ObjectCard("Download summary", Document("download-summary"));
        var filters = Inline();
        var device = Choice("Device", new[] { ("", "All devices") }.Concat(Rows("downloads").Select(row => row.Text("device_id")).Distinct().Select(id => (id, Rows("devices").FirstOrDefault(row => row.Text("device_id") == id)?.Text("device_name") ?? id))), _downloadDevice);
        var profile = Choice("Profile", new[] { ("", "All profiles") }.Concat(Rows("profiles").Select(row => (row.Text("id"), row.Text("name")))), _downloadProfile);
        var status = Choice("Status", new[] { "", "on_device", "requested", "in_progress", "failed", "revoked", "stale", "paused", "completed" }.Select(value => (value, value.Length == 0 ? "All statuses" : Label(value))), _downloadStatus);
        filters.Children.Add(device); filters.Children.Add(profile); filters.Children.Add(status);
        filters.Children.Add(Action("Apply download filters", () => { _downloadDevice = Selected(device); _downloadProfile = Selected(profile); _downloadStatus = Selected(status); Render(); return Task.CompletedTask; }));
        _body.Children.Add(filters);
        bool Matches(JsonObject row) => (_downloadDevice.Length == 0 || row.Text("device_id") == _downloadDevice) && (_downloadProfile.Length == 0 || row.Text("profile_id") == _downloadProfile);
        bool Android(JsonObject row) => Rows("devices").FirstOrDefault(item => item.Text("device_id") == row.Text("device_id"))?.Text("device_platform").Contains("android", StringComparison.OrdinalIgnoreCase) == true;
        bool Status(JsonObject row) => AdminUsersPresentation.MatchesDownload(row, _downloadStatus, Android(row));
        RenderDeviceCopies(Rows("downloads").Where(row => Matches(row) && Status(row)));
        AddRows("subscriptions", "Monitored series / subscriptions", ["series_title", "profile_id", "device_id", "mode", "season_numbers", "target_season", "delete_watched", "max_storage_bytes", "active", "on_device", "in_progress", "removed_episodes", "updated_at"], (panel, row) =>
        {
            if (!row.Flag("active")) panel.Children.Add(Text("Paused monitor — existing copies are not revoked by pausing."));
            DeviceLink(panel, row);
        }, Rows("subscriptions").Where(Matches));
        _body.Children.Add(Action("Open Download Management for this account", () =>
        {
            var request = new AdminRouteRequest(AdminRoute.PreparedFiles, new Dictionary<string, string> { ["user_id"] = _review!.User.Text("id") });
            if (!AdminShellPage.Open(request)) _notice.Text = "Download Management is not registered yet. Scope: " + request.Href;
            return Task.CompletedTask;
        }));
    }
}
