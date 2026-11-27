using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private static readonly string[] DetailTabs = ["overview", "profiles", "access", "sign-in", "activity", "downloads", "preferences"];
    private string DetailTab => DetailTabs.Contains(_location.Get("tab")) ? _location.Get("tab")! : "overview";
    private async Task ReadDetailAsync(long generation)
    {
        var root = AdminUsersApi.UserPath(_location.Segment(0)!);
        if (!AdminUsersRules.Available(_review?.Capabilities)) return;
        var id = AdminUsersApi.Segment(_location.Segment(0)!);
        var requestUser = "/api/v2/admin/request-users/" + id;
        var group = _review!.User.Text("access_group_id");
        var grouped = _review.User.Text("role") != "admin" && group.Length > 0;
        var historyFilter = _historyDays + "/" + _historyProfile;
        switch (DetailTab)
        {
            case "overview":
                await Task.WhenAll(
                    RowsAsync("profiles", root + "/profiles", generation, true),
                    Feature("request_usage") ? DocumentAsync("usage", requestUser + "/usage", generation) : Task.CompletedTask,
                    Feature("watch_summary") ? DocumentAsync("watch-summary", root + "/watch-summary?days=30", generation) : Task.CompletedTask,
                    RowsAsync("watching", "/api/v2/admin/sessions?user_id=" + id, generation),
                    RowsAsync("overview-history", HistoryPath(), generation, limit: 25, filter: historyFilter),
                    Feature("access_groups") && grouped ? RowsAsync("groups", "/api/v2/admin/access-groups", generation) : Task.CompletedTask);
                break;
            case "profiles":
                await Task.WhenAll(RowsAsync("profiles", root + "/profiles", generation, true),
                    DocumentAsync("profile-flags", "/api/v2/profiles", generation));
                break;
            case "access":
                await Task.WhenAll(
                    Feature("access_groups") ? RowsAsync("groups", "/api/v2/admin/access-groups", generation) : Task.CompletedTask,
                    Feature("policy_defaults") ? DocumentAsync("defaults", "/api/v2/admin/users/policy-defaults", generation) : Task.CompletedTask,
                    DocumentAsync("quota", requestUser + "/limit", generation),
                    DocumentAsync("request-defaults", "/api/v2/admin/request-settings", generation),
                    grouped ? DocumentAsync("group-quota", "/api/v2/admin/request-groups/" + AdminUsersApi.Segment(group) + "/limit", generation) : Task.CompletedTask,
                    Feature("request_usage") ? DocumentAsync("usage", requestUser + "/usage", generation) : Task.CompletedTask);
                break;
            case "sign-in":
                await Task.WhenAll(ReadIdentitiesAsync(), ReadLoginSessionsAsync());
                break;
            case "activity":
                await Task.WhenAll(
                    Feature("account_devices") ? RowsAsync("devices", root + "/devices", generation, true) : Task.CompletedTask,
                    RowsAsync("profiles", root + "/profiles", generation, true),
                    RowsAsync("ips", root + "/ips?days=" + _historyDays, generation),
                    RowsAsync("history", HistoryPath(), generation, filter: historyFilter));
                break;
            case "downloads":
                if (!Feature("account_downloads")) return;
                await Task.WhenAll(
                    DocumentAsync("download-summary", root + "/downloads/summary", generation),
                    RowsAsync("downloads", root + "/downloads", generation),
                    RowsAsync("subscriptions", root + "/download-subscriptions", generation),
                    RowsAsync("profiles", root + "/profiles", generation, true),
                    Feature("account_devices") ? RowsAsync("devices", root + "/devices", generation, true) : Task.CompletedTask);
                break;
            case "preferences": await ReadPreferencesAsync(generation, root); break;
        }

        async Task ReadIdentitiesAsync()
        {
            await DocumentAsync("external-capabilities", "/api/v2/auth/external-sign-in/capabilities", generation);
            if (!_workspace.IsCurrent(generation) || _loadErrors.ContainsKey("external-capabilities") ||
                !AdminUsersRules.Feature(Document("external-capabilities"), "admin_identities")) return;
            await Task.WhenAll(RowsAsync("identities", root + "/identities", generation, true),
                RowsAsync("providers", "/api/v2/admin/plugins/installations", generation));
        }

        async Task ReadLoginSessionsAsync()
        {
            if (!Feature("login_sessions")) return;
            await DocumentAsync("sessions-capabilities", "/api/v2/auth/sessions/capabilities", generation);
            if (_workspace.IsCurrent(generation) && !_loadErrors.ContainsKey("sessions-capabilities") &&
                AdminUsersRules.Feature(Document("sessions-capabilities"), "admin_management"))
                await RowsAsync("login-sessions", root + "/login-sessions", generation);
        }
    }
    private string HistoryPath()
    {
        var after = DateTimeOffset.UtcNow.AddDays(-int.Parse(_historyDays, CultureInfo.InvariantCulture)).ToString("O", CultureInfo.InvariantCulture);
        return "/api/v2/admin/playback-history?user_id=" + AdminUsersApi.Segment(_location.Segment(0)!) + "&ended_after=" + Uri.EscapeDataString(after) +
            (_historyProfile.Length == 0 ? "" : "&profile_id=" + AdminUsersApi.Segment(_historyProfile));
    }

    // ── Header, tab strip ────────────────────────────────────────────────────────────────────────────────

    private void RenderDetail()
    {
        var user = _review!.User;
        var id = user.Text("id"); var username = user.Text("username");
        var shell = new StackPanel { Spacing = 24 };

        // Breadcrumb: Admin › Users › name
        var crumbs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        TextBlock Crumb(string text) { var block = Text(text, 14, true); block.IsTextSelectionEnabled = false; block.TextWrapping = TextWrapping.NoWrap; return block; }
        var admin = AdminMonitorUi.LinkButton(Crumb(T("web.admin.users.detail.header.breadcrumbAdmin")), T("web.admin.users.detail.header.breadcrumbAdmin"), AdminMonitorUi.Muted);
        Wire(admin, () => { AdminShellPage.Open(new(AdminRoute.Dashboard)); return Task.CompletedTask; });
        var users = AdminMonitorUi.LinkButton(Crumb(T("web.admin.users.detail.header.breadcrumbUsers")), T("web.admin.users.detail.header.breadcrumbUsers"), AdminMonitorUi.Muted);
        Wire(users, () => { Navigate(null, "users"); return Task.CompletedTask; });
        crumbs.Children.Add(admin); crumbs.Children.Add(MutedIcon("chevron-right", 14)); crumbs.Children.Add(users); crumbs.Children.Add(MutedIcon("chevron-right", 14));
        var current = Truncated(username, 14, medium: true); current.MaxWidth = 360; crumbs.Children.Add(current);
        AutomationProperties.SetName(crumbs, T("web.admin.users.detail.header.breadcrumb"));

        // Identity: avatar, name with badges, meta line, notices
        var manageable = Manage;
        var identity = new Grid { ColumnSpacing = 16 };
        identity.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); identity.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var avatar = Avatar(username, long.TryParse(id, out var seed) ? seed : 0, large: true, size: _phone ? 56 : 64); avatar.VerticalAlignment = VerticalAlignment.Top;
        identity.Children.Add(avatar);
        var copy = new StackPanel { Spacing = 8 };
        var title = new LogsWrapPanel { Spacing = 8, RowSpacing = 4 };
        var heading = new TextBlock
        {
            Text = username, FontSize = _phone ? 28 : 32, FontWeight = FontWeights.Bold, CharacterSpacing = -35, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            Foreground = Brush("PrimaryTextBrush"), VerticalAlignment = VerticalAlignment.Center, LineHeight = (_phone ? 28 : 32) * 1.15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        title.Children.Add(heading);
        title.Children.Add(RoleBadge(user));
        title.Children.Add(user.Flag("enabled") ? ActiveBadge(T("web.admin.users.detail.header.status.active")) : AdminLibrariesUi.Badge(T("web.admin.users.detail.header.status.disabled"), AdminLibrariesUi.BadgeTone.Destructive));
        if (user.Flag("password_change_required")) title.Children.Add(TintedBadge(T("web.admin.users.detail.header.temporaryPassword"), AdminMonitorUi.Amber500, AdminMonitorUi.Amber300));
        copy.Children.Add(title);
        var meta = new List<string>();
        if (user.Text("email").Length > 0) meta.Add(user.Text("email"));
        if (user.Text("role") != "admin" && user.Text("access_group_id").Length > 0 && Rows("groups").FirstOrDefault(group => group.Text("id") == user.Text("access_group_id"))?.Text("name") is { Length: > 0 } groupName)
            meta.Add(T("web.admin.users.detail.header.group", ("name", groupName)));
        if (!_phone) meta.Add(T("web.admin.users.detail.header.joined", ("date", AccountDate(user.Text("created_at")))));
        meta.Add(user.Text("last_active_at").Length > 0 ? T("web.admin.users.detail.header.lastActive", ("when", LastSeen(user.Text("last_active_at")))) : T("web.admin.users.detail.header.noActivity"));
        if (!_phone) meta.Add(user.Flag("password_login") ? T("web.admin.users.detail.header.passwordSignIn") : T("web.admin.users.detail.header.externalSignIn"));
        var metaLine = Text(string.Join("  ·  ", meta), 14, true); metaLine.IsTextSelectionEnabled = true;
        copy.Children.Add(metaLine);
        if (!manageable)
            copy.Children.Add(Notice("info", T(user.Flag("is_owner") ? "web.admin.users.detail.header.ownerNotice" : "web.admin.users.detail.header.adminNotice"), AdminMonitorUi.ToneColor(ActivityTone.Info)));
        var enableAccount = () => ConfirmAsync("Change account availability", "Disabling ends sign-ins and playback authority. Review the exact account before confirming.",
            new(AdminUsersOperation.UpdateAccount, new JsonObject { ["enabled"] = !user.Flag("enabled") }));
        var changeable = !user.Flag("is_owner") && id != _review.Viewer.Text("id");
        if (!user.Flag("enabled"))
        {
            var disabled = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
            disabled.Children.Add(Text(T("web.admin.users.detail.header.disabledNotice"), 14));
            if (manageable && changeable) disabled.Children.Add(Wire(ActionButton(T("web.admin.users.detail.header.enableAccount")), enableAccount));
            copy.Children.Add(new Border
            {
                Child = disabled, Padding = new(12, 10, 12, 10), CornerRadius = new(AdminLibrariesUi.RadiusXl), BorderThickness = new(1),
                Background = AdminMonitorUi.Solid(AdminMonitorUi.Destructive, 0.10), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Destructive, 0.30),
            });
        }
        Grid.SetColumn(copy, 1); identity.Children.Add(copy);

        // Actions: View as user, Reset / Set password, and the More menu
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        var canViewAs = AdminUsersRules.CanViewAs(user, _review.Viewer.Text("id"), _review.Viewer.Flag("is_owner"));
        var viewAs = Wire(ActionButton(T("web.admin.users.detail.header.viewAs")), BeginViewAsAsync, canViewAs);
        if (!canViewAs)
            ToolTipService.SetToolTip(viewAs, T(!user.Flag("enabled") ? "web.admin.users.detail.header.viewAsReason.enableFirst"
                : user.Flag("is_owner") ? "web.admin.users.detail.header.viewAsReason.owner"
                : id == _review.Viewer.Text("id") ? "web.admin.users.detail.header.viewAsReason.ownAccount" : "web.admin.users.detail.header.viewAsReason.adminOwnerOnly"));
        actions.Children.Add(viewAs);
        if (manageable)
        {
            var reset = new List<Button>();
            if (user.Flag("password_login") && Feature("password_reset_link"))
                reset.Add(Wire(MenuButton(T("admin.native.users.header.passwordLink")), () => ResetPasswordAsync("link")));
            if (user.Flag("password_login") && Feature("password_reset_email"))
                reset.Add(Wire(MenuButton(T("admin.native.users.header.passwordEmail")), () => ResetPasswordAsync("email")));
            if (PasswordManage) reset.Add(Wire(MenuButton(T("web.admin.users.detail.header.setPassword")), () => SetPasswordAsync()));
            if (!user.Flag("password_login"))
            {
                if (PasswordManage) actions.Children.Add(Wire(ActionButton(T("web.admin.users.detail.header.setPassword")), () => SetPasswordAsync(), user.Flag("enabled")));
            }
            else if (reset.Count > 0)
            {
                actions.Children.Add(MenuTrigger(reset, T("web.admin.users.detail.header.resetPassword"), label: T("web.admin.users.detail.header.resetPassword")));
            }
        }
        var more = new List<Button>();
        if (Feature("ownership_transfer") && AdminUsersRules.CanTransfer(user, _review.Viewer.Text("id"), _review.Viewer.Flag("is_owner")))
            more.Add(Wire(MenuButton(T("web.admin.users.detail.header.makeOwner"), hint: T("web.admin.users.detail.header.makeOwnerHint")),
                () => ConfirmAsync("Transfer server ownership", "The selected enabled admin becomes Owner; you remain an admin and lose Owner-only authority.", new(AdminUsersOperation.Transfer))));
        if (manageable && changeable)
        {
            more.Add(Wire(MenuButton(user.Flag("enabled") ? T("web.admin.users.detail.header.disableAccount") : T("web.admin.users.detail.header.enableAccount"),
                hint: user.Flag("enabled") ? T("web.admin.users.detail.header.disableHint") : null), enableAccount));
            more.Add(Wire(MenuButton(T("web.admin.users.detail.header.deleteAccount"), destructive: true),
                () => ConfirmAsync("Delete " + username, "Permanently delete this account, its profiles and account data. This cannot be undone.", new(AdminUsersOperation.DeleteAccount))));
        }
        if (more.Count > 0) actions.Children.Add(MenuTrigger(more, T("web.admin.users.detail.header.moreActions")));
        actions.Children.Add(Wire(IconAction("refresh-cw", T("admin.native.users.reloadName")), () => LoadAsync(_location)));

        var top = new StackPanel { Spacing = 20 };
        top.Children.Add(crumbs);
        top.Children.Add(new SplitRow(identity, actions) { MinStart = 300, Gap = 16, RowGap = 16 });
        shell.Children.Add(top);
        AddLoadFailures();
        if (_loadErrors.Count > 0) { var alert = _body.Children[^1]; _body.Children.RemoveAt(_body.Children.Count - 1); shell.Children.Add(alert); }

        if (!AdminUsersRules.Available(_review.Capabilities)) { shell.Children.Add(Text(T("admin.native.users.protectedNote"), 14)); _body.Children.Add(shell); return; }
        var tabs = new StackPanel { Spacing = 16 };
        tabs.Children.Add(TabStrip(DetailTabItems(user), DetailTab, value => { if (!_actionBusy) Navigate(id, value); }, "UserTab_"));
        tabs.Children.Add(DetailTab switch
        {
            "profiles" => ProfilesTab(), "access" => AccessTab(), "sign-in" => SignInTab(), "activity" => ActivityTab(), "downloads" => DownloadsTab(), "preferences" => PreferencesTab(),
            _ => OverviewTab(),
        });
        shell.Children.Add(tabs);
        _body.Children.Add(shell);
    }

    private IReadOnlyList<(string Value, string Label, UIElement? Extra)> DetailTabItems(JsonObject user)
    {
        string[] customFields = ["library_ids", "permissions", "max_playback_quality", "max_streams", "max_transcodes", "transcode_allowed", "audio_transcode_allowed", "max_remote_stream_bitrate_kbps",
            "max_local_stream_bitrate_kbps", "download_allowed", "download_transcode_allowed", "requests_allowed", "max_profiles"];
        var custom = customFields.Count(field => user[field] != null);
        var items = new List<(string, string, UIElement?)>
        {
            ("overview", T("web.admin.users.detail.tabs.overview"), null),
            ("profiles", T("web.admin.users.detail.tabs.profiles"), _rows.ContainsKey("profiles") ? TabCount(Rows("profiles").Count.ToString(CultureInfo.CurrentCulture)) : null),
            ("access", T("web.admin.users.detail.tabs.access"), custom > 0 ? TabCount(T("web.admin.users.detail.tabs.customCount", ("count", custom))) : null),
            ("sign-in", T("web.admin.users.detail.tabs.signIn"), null),
            ("activity", T("web.admin.users.detail.tabs.activity"), Rows("watching").Count > 0 ? WatchingDot() : null),
            ("downloads", T("web.admin.users.detail.tabs.downloads"), null),
            ("preferences", T("web.admin.users.detail.tabs.preferences"), _rows.ContainsKey("settings") ? TabCount(Rows("settings").Count.ToString(CultureInfo.CurrentCulture)) : null),
        };
        return items;
    }

    private static FrameworkElement WatchingDot()
    {
        var dot = new Microsoft.UI.Xaml.Controls.Border { Width = 8, Height = 8, Background = AdminMonitorUi.Solid(AdminMonitorUi.ToneColor(ActivityTone.Success)), VerticalAlignment = VerticalAlignment.Center };
        SiloPlayer.Helpers.RoundedBadge.SetRadius(dot, 999);
        AutomationProperties.SetName(dot, T("web.admin.users.detail.common.watchingNow"));
        return dot;
    }

    /// <summary>A bordered notice with a tone: the info box under the header's meta line.</summary>
    private static Border Notice(string icon, string text, Windows.UI.Color tone)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var glyph = AdminMonitorUi.Icon(icon, 16, AdminMonitorUi.Solid(tone)); glyph.VerticalAlignment = VerticalAlignment.Top; glyph.Margin = new(0, 2, 0, 0);
        row.Children.Add(glyph);
        var label = Text(text, 14, true); Grid.SetColumn(label, 1); row.Children.Add(label);
        return new Border
        {
            Child = row, Padding = new(12, 10, 12, 10), CornerRadius = new(AdminLibrariesUi.RadiusXl), BorderThickness = new(1),
            Background = AdminMonitorUi.Solid(tone, 0.05), BorderBrush = AdminMonitorUi.Solid(tone, 0.25),
        };
    }

    /// <summary>Two columns from lg up (left 1.6fr / right 1fr, or equal), one column in reading order below it.</summary>
    private FrameworkElement Columns(IReadOnlyList<UIElement> left, IReadOnlyList<UIElement> right, double leftWeight = 1, double rightWeight = 1)
    {
        if (!_wide)
        {
            var single = new StackPanel { Spacing = 16 };
            foreach (var element in left.Concat(right)) single.Children.Add(element);
            return single;
        }
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(leftWeight, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(rightWeight, GridUnitType.Star) });
        var first = new StackPanel { Spacing = 16 }; var second = new StackPanel { Spacing = 16 };
        foreach (var element in left) first.Children.Add(element);
        foreach (var element in right) second.Children.Add(element);
        Grid.SetColumn(second, 1); grid.Children.Add(first); grid.Children.Add(second);
        return grid;
    }

    private void ObjectCard(string title, JsonObject? row, IEnumerable<string>? keys = null)
    {
        var panel = Panel();
        if (row == null) panel.Children.Add(Text("Not supported or not loaded."));
        else foreach (var key in keys ?? row.Select(pair => pair.Key)) panel.Children.Add(Text(Label(key) + ": " + row[key].Display()));
        _body.Children.Add(Card(title, panel));
    }

    // ── Overview ───────────────────────────────────────────────────────────────────────────────────────

    private static readonly string[] KnownMethods = ["direct", "remux", "direct_stream", "transcode"];

    /// <summary>The official play-method label ("Direct Play", "Remux", "Direct Stream", "Transcode"); a value the app does not know reads as itself, title-cased.</summary>
    private static string PlayMethod(string? method)
    {
        var raw = (method ?? "").Trim();
        if (raw == "direct_play") raw = "direct";
        if (raw.Length == 0 || KnownMethods.Contains(raw)) return AdminActivityPresentation.MethodMeta(raw).Label;
        return string.Join(" ", raw.Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static FrameworkElement MethodBadge(string? method)
    {
        var raw = (method ?? "").Trim();
        if (raw == "direct_play") raw = "direct";
        var tone = AdminActivityPresentation.MethodMeta(KnownMethods.Contains(raw) ? raw : "").Tone;
        return TintedBadge(PlayMethod(method), AdminMonitorUi.ToneColor(tone), AdminMonitorUi.LabelColor(tone), pill: true);
    }

    private static string PlayTitle(JsonObject play)
    {
        var series = play.Text("series_title");
        if (series.Length > 0)
        {
            var code = EpisodeCode(play["season_number"] is JsonValue s && s.TryGetValue<long>(out var season) ? season : null, play["episode_number"] is JsonValue e && e.TryGetValue<long>(out var episode) ? episode : null);
            return code.Length > 0 ? series + " · " + code : series;
        }
        return play.Text("media_title", T("web.admin.users.detail.common.unknownTitle"));
    }

    private static double WatchedFraction(JsonObject play, bool live = false)
    {
        if (!live && play.Flag("completed")) return 1;
        var watched = play["end_position_seconds"] is JsonValue a && a.TryGetValue<double>(out var end) ? end : play["watched_seconds"] is JsonValue b && b.TryGetValue<double>(out var seconds) ? seconds : play["position_seconds"] is JsonValue c && c.TryGetValue<double>(out var position) ? position : 0;
        var duration = play["duration_seconds"] is JsonValue d && d.TryGetValue<double>(out var length) ? length : play["file_duration"] is JsonValue f && f.TryGetValue<double>(out var file) ? file : 0;
        return duration > 0 && double.IsFinite(watched) ? Math.Clamp(watched / duration, 0, 1) : 0;
    }

    private FrameworkElement OverviewTab()
    {
        var effective = _review!.User["effective_policy"] as JsonObject;
        var panel = new StackPanel { Spacing = 16 };
        // Stat tiles
        var tiles = new List<Border>();
        var watching = Rows("watching");
        var liveKnown = _rows.ContainsKey("watching");
        void Against(string label, string value, long max, double count)
        {
            tiles.Add(StatTile(label, value, max > 0 ? max.ToString(CultureInfo.CurrentCulture) : null, max > 0 ? count / max : null));
        }
        if (!liveKnown) tiles.Add(StatTile(T("web.admin.users.detail.overview.stats.streamsNow"), "—"));
        else Against(T("web.admin.users.detail.overview.stats.streamsNow"), watching.Count.ToString(CultureInfo.CurrentCulture), effective.Count("max_streams"), watching.Count);
        if (!effective.Flag("transcode_allowed")) tiles.Add(StatTile(T("web.admin.users.detail.overview.stats.transcodesNow"), T("web.admin.users.detail.overview.stats.off")));
        else if (!liveKnown) tiles.Add(StatTile(T("web.admin.users.detail.overview.stats.transcodesNow"), "—"));
        else { var count = watching.Count(row => AdminUsersPresentation.IsVideoTranscode(row)); Against(T("web.admin.users.detail.overview.stats.transcodesNow"), count.ToString(CultureInfo.CurrentCulture), effective.Count("max_transcodes"), count); }
        if (!_rows.ContainsKey("profiles")) tiles.Add(StatTile(T("web.admin.users.detail.overview.stats.profiles"), "—"));
        else { var max = effective.Count("max_profiles"); tiles.Add(StatTile(T("web.admin.users.detail.overview.stats.profiles"), Rows("profiles").Count.ToString(CultureInfo.CurrentCulture), max.ToString(CultureInfo.CurrentCulture), max > 0 ? Rows("profiles").Count / (double)max : 0)); }
        if (Feature("request_usage"))
        {
            var usage = Document("usage");
            var days = usage.Count("window_days");
            var label = usage != null ? T("web.admin.users.detail.overview.stats.requestsWindow", ("days", days), ("days_text", days)) : T("web.admin.users.detail.overview.stats.requests");
            if (usage == null) tiles.Add(StatTile(label, "—"));
            else if (!usage.Flag("requests_enabled") || !usage.Flag("allowed")) tiles.Add(StatTile(label, T("web.admin.users.detail.overview.stats.off")));
            else if (usage.Flag("unlimited")) tiles.Add(StatTile(label, T("web.admin.users.detail.overview.stats.unlimited")));
            else tiles.Add(StatTile(label, usage.Count("used").ToString(CultureInfo.CurrentCulture), usage.Count("max_requests").ToString(CultureInfo.CurrentCulture), usage.Count("max_requests") > 0 ? usage.Count("used") / (double)usage.Count("max_requests") : 0));
        }
        if (Feature("watch_summary"))
        {
            var summary = Document("watch-summary");
            tiles.Add(summary == null ? StatTile(T("web.admin.users.detail.overview.stats.watched"), "—")
                : StatTile(T("web.admin.users.detail.overview.stats.watched"), WatchTime(summary["watched_seconds"] is JsonValue value && value.TryGetValue<double>(out var seconds) ? seconds : 0),
                    detail: T("web.admin.users.detail.overview.stats.plays", ("count", summary.Count("plays")), ("count_text", summary.Count("plays").ToString("N0", CultureInfo.CurrentCulture)))));
        }
        panel.Children.Add(StatStrip(tiles));
        if (liveKnown && _rows["watching"] is { Complete: false }) panel.Children.Add(Text(T("admin.native.users.overview.partialLive"), 13, true));

        var left = new List<UIElement>(); var right = new List<UIElement>();
        if (watching.Count > 0) left.Add(WatchingNowCard(watching));
        left.Add(RecentlyWatchedCard());
        right.Add(AccessSummaryCard());
        right.Add(OverviewProfilesCard(watching));
        right.Add(DetailsCard());
        panel.Children.Add(Columns(left, right, 1.6, 1));
        return panel;
    }

    private FrameworkElement WatchingNowCard(IReadOnlyList<JsonObject> sessions)
    {
        var rows = sessions.Select(session =>
        {
            var meta = new[] { session.Text("profile_name").Length > 0 ? T("web.admin.users.detail.overview.watching.profile", ("name", session.Text("profile_name"))) : "",
                session.Text("client_label", session.Text("client_name")), session.Text("client_ip") }.Where(part => part.Length > 0);
            var duration = session["file_duration"] is JsonValue d && d.TryGetValue<double>(out var length) ? length : 0;
            var position = session["position_seconds"] is JsonValue p && p.TryGetValue<double>(out var at) ? at : 0;
            var row = ListRow(null, PlayTitle(session), string.Join(" · ", meta),
                duration > 0 ? Trailing(T("web.admin.users.detail.overview.watching.position", ("position", Math.Max(0, (long)(position / 60))), ("duration", Math.Round(duration / 60)))) : null);
            if (duration > 0 && row.Child is Grid grid && grid.Children.OfType<StackPanel>().FirstOrDefault() is { } copy)
            {
                var bar = Bar(position / duration, 208); bar.Margin = new(0, 6, 0, 0); copy.Children.Add(bar);
            }
            return row;
        });
        return DetailCard(T("web.admin.users.detail.common.watchingNow"), null, MethodBadge(sessions[0].Text("effective_play_method", sessions[0].Text("play_method"))), RowStack(rows), id: "UsersWatchingNow");
    }

    private FrameworkElement RecentlyWatchedCard()
    {
        var plays = Rows("overview-history").Take(4).ToList();
        var all = Wire(ActionButton(T("web.admin.users.detail.overview.recent.allActivity"), null, "GhostButtonStyle", 28), () => { Navigate(_review!.User.Text("id"), "activity"); return Task.CompletedTask; });
        UIElement body;
        if (!_rows.ContainsKey("overview-history")) body = Failure(T("web.admin.users.detail.overview.recent.failed"), Wire(ActionButton(T("common.retry")), () => LoadAsync(_location)));
        else if (plays.Count == 0) body = Empty(T("web.admin.users.detail.overview.recent.empty"));
        else body = RowStack(plays.Select(play =>
        {
            var progress = play.Flag("completed") ? T("web.admin.users.detail.overview.recent.finished") : T("web.admin.users.detail.common.percent", ("percent", Math.Round(WatchedFraction(play) * 100)));
            var meta = string.Join(" · ", new[] { play.Text("profile_name"), PlayMethod(play.Text("play_method")), progress }.Where(part => part.Length > 0));
            return ListRow(null, PlayTitle(play), meta, Trailing(DayTime(play.Text("ended_at"))));
        }));
        return DetailCard(T("web.admin.users.detail.overview.recent.title"), null, all, body);
    }

    private FrameworkElement OverviewProfilesCard(IReadOnlyList<JsonObject> watching)
    {
        var max = (_review!.User["effective_policy"] as JsonObject).Count("max_profiles");
        var watchingIds = new HashSet<string>(watching.Select(row => row.Text("profile_id")), StringComparer.Ordinal);
        var profiles = Rows("profiles");
        UIElement body;
        if (!_rows.ContainsKey("profiles")) body = Failure(T("web.admin.users.detail.common.loadProfilesFailed"), Wire(ActionButton(T("common.retry")), () => LoadAsync(_location)));
        else if (profiles.Count == 0) body = Empty(T("web.admin.users.detail.common.noProfiles"));
        else body = RowStack(profiles.Select((profile, index) => ListRow(Avatar(profile.Text("name"), index), profile.Text("name"), null,
            Trailing(watchingIds.Contains(profile.Text("id")) ? T("web.admin.users.detail.common.watchingNow") : profile.Text("last_seen_at").Length > 0 ? LastSeen(profile.Text("last_seen_at")) : T("web.admin.users.detail.common.noDeviceActivity")))));
        var manage = Wire(ActionButton(T("web.admin.users.detail.overview.profiles.manage"), null, "GhostButtonStyle", 28), () => { Navigate(_review.User.Text("id"), "profiles"); return Task.CompletedTask; });
        return DetailCard(T("web.admin.users.detail.overview.profiles.title"), _rows.ContainsKey("profiles") ? T("web.admin.users.detail.common.profilesAllowed", ("count", profiles.Count), ("max", max)) : null, manage, body);
    }

    private FrameworkElement DetailsCard()
    {
        var user = _review!.User;
        var rows = new List<Border>
        {
            KeyValueRow(T("web.admin.users.detail.overview.details.userId"), null, user.Text("id"), mono: true),
            KeyValueRow(T("web.admin.users.detail.overview.details.created"), null, AccountDate(user.Text("created_at"))),
            KeyValueRow(T("web.admin.users.detail.overview.details.lastChanged"), null, user.Text("updated_at").Length > 0 ? AccountDate(user.Text("updated_at")) : "—"),
        };
        return DetailCard(T("web.admin.users.detail.overview.details.title"), null, null, RowStack(rows));
    }

    private static string Quality(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "any" or "original" or "auto" => T("web.admin.users.detail.access.value.anyQuality"), "4k" or "2160p" => "4K", "standard" or "1080p" => "1080p", var other => other,
    };

    private string LibrariesText(JsonObject? effective) => effective?["library_ids"] switch
    {
        null => T("web.admin.users.detail.access.value.allLibraries"),
        JsonArray { Count: 0 } => T("web.admin.users.detail.access.value.noLibraries"),
        JsonArray array => array.Count.ToString(CultureInfo.CurrentCulture) + (array.Count == 1 ? " library" : " libraries"),
        var other => other.Display(),
    };

    private static string Bitrate(long kbps) => kbps <= 0 ? T("web.admin.users.detail.access.value.noCap") : kbps >= 1000 ? (kbps / 1000d).ToString("0.#", CultureInfo.CurrentCulture) + " Mbps" : kbps + " kbps";

    private FrameworkElement AccessSummaryCard()
    {
        var user = _review!.User;
        var effective = user["effective_policy"] as JsonObject;
        var custom = new[] { "library_ids", "permissions", "max_playback_quality", "max_streams", "max_transcodes", "transcode_allowed", "audio_transcode_allowed", "max_remote_stream_bitrate_kbps",
            "max_local_stream_bitrate_kbps", "download_allowed", "download_transcode_allowed", "requests_allowed", "max_profiles" }.Count(field => user[field] != null);
        var grouped = user.Text("role") != "admin" && user.Text("access_group_id").Length > 0;
        // An account in a group never reads "Server default": a group whose name is not loaded is named only as an access group.
        var source = !grouped ? T("web.admin.users.detail.overview.access.serverDefault")
            : Rows("groups").FirstOrDefault(group => group.Text("id") == user.Text("access_group_id"))?.Text("name") is { Length: > 0 } name
                ? T("web.admin.users.detail.overview.access.groupSource", ("name", name)) : T("web.admin.users.detail.access.library.accessGroup");
        var permissions = (effective?["permissions"] as JsonArray)?.Select(item => item?.GetValue<string>()).ToArray() ?? [];
        FrameworkElement Line(string icon, string heading, string? detail)
        {
            var tile = new Border { Width = 28, Height = 28, CornerRadius = new(6), Background = Brush("AccentBackgroundBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0), Child = MutedIcon(icon, 14) };
            ((FrameworkElement)tile.Child).HorizontalAlignment = HorizontalAlignment.Center; ((FrameworkElement)tile.Child).VerticalAlignment = VerticalAlignment.Center;
            var copy = new StackPanel();
            copy.Children.Add(Medium(heading, 14));
            if (!string.IsNullOrEmpty(detail)) { var line = Text(detail, 12, true); line.LineHeight = 19.5; copy.Children.Add(line); }
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.Children.Add(tile); Grid.SetColumn(copy, 1); grid.Children.Add(copy);
            return grid;
        }
        Border Wrap(FrameworkElement line) => new() { Child = line, Padding = new(Gutter, 12, Gutter, 12), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.5) };
        var maxStreams = effective.Count("max_streams");
        var quality = Quality(effective?["max_playback_quality"]?.GetValue<string>());
        var video = !effective.Flag("transcode_allowed") ? T("web.admin.users.detail.overview.access.noVideoTranscoding")
            : effective.Count("max_transcodes") == 0 ? T("web.admin.users.detail.overview.access.unlimitedVideoTranscodes")
            : T("web.admin.users.detail.overview.access.videoTranscodeLimit", ("count", effective.Count("max_transcodes")), ("count_text", effective.Count("max_transcodes")));
        var remote = effective.Count("max_remote_stream_bitrate_kbps");
        var lines = new List<Border>
        {
            Wrap(Line("layout-grid", LibrariesText(effective), T("web.admin.users.detail.overview.access.markersCurate", ("marker", permissions.Contains("marker_edit") ? "yes" : "no"), ("curate", permissions.Contains("metadata_curation") ? "yes" : "no")))),
            Wrap(Line("play", (quality == T("web.admin.users.detail.access.value.anyQuality") ? T("web.admin.users.detail.overview.access.anyQuality") : T("web.admin.users.detail.overview.access.quality", ("quality", quality))) + " · " +
                (maxStreams == 0 ? T("web.admin.users.detail.overview.access.unlimitedStreams") : T("web.admin.users.detail.overview.access.streams", ("count", maxStreams), ("count_text", maxStreams))),
                video + " · " + (remote == 0 ? T("web.admin.users.detail.overview.access.noBitrateCap") : T("web.admin.users.detail.overview.access.remoteCap", ("bitrate", Bitrate(remote)))))),
            Wrap(Line("download", effective.Flag("download_allowed") ? T("web.admin.users.detail.overview.access.downloadsAllowed") : T("web.admin.users.detail.overview.access.downloadsNotAllowed"),
                effective.Flag("download_transcode_allowed") ? T("web.admin.users.detail.overview.access.serverCopiesAllowed") : T("web.admin.users.detail.overview.access.originalFilesOnly"))),
            Wrap(Line("mail", effective.Flag("requests_allowed") ? T("web.admin.users.detail.overview.access.requests") : T("web.admin.users.detail.overview.access.cantRequest"), null)),
        };
        var open = Wire(ActionButton(T("web.admin.users.detail.overview.access.openAccess"), null, "OutlineButtonStyle", 28), () => { Navigate(user.Text("id"), "access"); return Task.CompletedTask; });
        return DetailCard(T("web.admin.users.detail.overview.access.title"), custom > 0 ? T("web.admin.users.detail.overview.access.withLimits", ("count", custom), ("source", source)) : source, open, RowStack(lines));
    }

    // ── Profiles ───────────────────────────────────────────────────────────────────────────────────────

    private FrameworkElement ProfilesTab()
    {
        var limit = (_review!.User["effective_policy"] as JsonObject).Count("max_profiles");
        var profiles = Rows("profiles");
        var panel = new StackPanel { Spacing = 16 };
        UIElement? add = null;
        if (ProfilesManage && _rows.ContainsKey("profiles"))
        {
            var button = Wire(ActionButton(T("web.admin.users.detail.profiles.add"), "plus"), () => ProfileAsync(null), limit < 1 || profiles.Count < limit);
            if (limit >= 1 && profiles.Count >= limit) ToolTipService.SetToolTip(button, T("web.admin.users.detail.profiles.atLimit"));
            add = button;
        }
        UIElement body;
        if (!_rows.ContainsKey("profiles")) body = Failure(T("web.admin.users.detail.common.loadProfilesFailed"), Wire(ActionButton(T("common.retry")), () => LoadAsync(_location)));
        else if (profiles.Count == 0) body = Empty(T("web.admin.users.detail.common.noProfiles"));
        else body = RowStack(profiles.Select((profile, index) => ProfileRow(profile, index, profiles.Count)));
        if (!ProfilesManage) panel.Children.Add(Text(T(Manage ? "web.admin.users.detail.profiles.readOnly.unsupported" : "web.admin.users.detail.profiles.readOnly.ownerOnly"), 14, true));
        panel.Children.Add(DetailCard(T("web.admin.users.detail.profiles.title"), _rows.ContainsKey("profiles") ? T("web.admin.users.detail.common.profilesAllowed", ("count", profiles.Count), ("max", limit)) : null, add, body));
        return panel;
    }

    private Border ProfileRow(JsonObject profile, int index, int count)
    {
        var name = profile.Text("name");
        var badges = new LogsWrapPanel { Spacing = 6, RowSpacing = 4 };
        if (profile.Flag("is_primary")) badges.Children.Add(AdminLibrariesUi.Badge(T("web.admin.users.detail.profiles.badge.primary"), AdminLibrariesUi.BadgeTone.Secondary));
        if (profile.Flag("is_child")) badges.Children.Add(AdminLibrariesUi.Badge(T("web.admin.users.detail.profiles.badge.kids"), AdminLibrariesUi.BadgeTone.Secondary));
        if (profile.Flag("has_pin")) badges.Children.Add(AdminLibrariesUi.Badge(T("web.admin.users.detail.profiles.badge.pin"), AdminLibrariesUi.BadgeTone.Outline));
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (!_phone) trailing.Children.Add(Trailing(profile.Text("last_seen_at").Length > 0 ? LastSeen(profile.Text("last_seen_at")) : T("web.admin.users.detail.common.noDeviceActivity")));
        if (ProfilesManage)
        {
            var items = new List<Button>
            {
                Wire(MenuButton(T("web.admin.users.detail.profiles.editProfile")), () => ProfileAsync(profile.Text("id"))),
                Wire(MenuButton(profile.Flag("has_pin") ? T("web.admin.users.detail.profiles.changePin") : T("web.admin.users.detail.profiles.setPin")), () => ProfilePinAsync(profile, false)),
            };
            if (profile.Flag("has_pin")) items.Add(Wire(MenuButton(T("web.admin.users.detail.profiles.removePin")), () => ProfilePinAsync(profile, true)));
            if (!profile.Flag("is_primary") && count > 1) items.Add(Wire(MenuButton(T("web.admin.users.detail.profiles.deleteProfile"), destructive: true), () => DeleteProfileAsync(profile)));
            trailing.Children.Add(MenuTrigger(items, T("web.admin.users.detail.profiles.actionsFor", ("name", name))));
        }
        var row = ListRow(Avatar(name, index), name, null, trailing);
        if (row.Child is Grid grid && grid.Children.OfType<StackPanel>().FirstOrDefault() is { } copy && badges.Children.Count > 0)
        {
            // The badges sit beside the name (official ProfileRow): a wrapping row holding the name first.
            copy.Children.Insert(1, badges); badges.Margin = new(0, 4, 0, 0);
        }
        return row;
    }

    // ── Access & limits ──────────────────────────────────────────────────────────────────────────────

    private string PolicySource(params string[] fields)
    {
        var user = _review!.User;
        if (fields.Any(field => user[field] != null)) return "custom";
        return user.Text("role") != "admin" && user.Text("access_group_id").Length > 0 ? "group" : "default";
    }

    private static string YesNo(bool value) => value ? T("web.admin.users.detail.access.value.yes") : T("web.admin.users.detail.access.value.no");
    private static string Allowed(bool value) => value ? T("web.admin.users.detail.access.value.allowed") : T("web.admin.users.detail.access.value.notAllowed");

    private Border PolicyRow(string label, string? description, string value, params string[] fields) => KeyValueRow(label, description, value, null, SourceTag(PolicySource(fields)));

    private FrameworkElement EditAction(string card, Func<Task> run, bool editable)
    {
        if (!editable)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(MutedIcon("lock", 12)); row.Children.Add(Text(T("web.admin.users.detail.access.card.viewOnly"), 12, true));
            return row;
        }
        return Wire(ActionButton(T("web.admin.users.detail.access.card.edit"), null, "OutlineButtonStyle", 28, T("web.admin.users.detail.access.card.editAria", ("card", card))), run);
    }

    private FrameworkElement AccessTab()
    {
        var user = _review!.User;
        var effective = user["effective_policy"] as JsonObject;
        var panel = new StackPanel { Spacing = 16 };
        if (Manage && !Policy) panel.Children.Add(Text(T("web.admin.users.detail.access.tab.ownerOnlyNote"), 14, true));

        // Sign-in & role
        var signInTitle = T("web.admin.users.detail.common.cardTitle.signin");
        var signIn = DetailCard(signInTitle, null, EditAction(signInTitle, () => EditAsync("Account access", "AdminAccountUpdateBody", user, AdminUsersOperation.UpdateAccount,
            fields: ["username", "email", "role", "enabled", "max_profiles"], locked: AccountLockedFields()), Manage), RowStack(
        [
            KeyValueRow(T("web.admin.users.detail.access.signIn.username"), null, user.Text("username")),
            KeyValueRow(T("web.admin.users.detail.access.signIn.email"), null, user.Text("email").Length > 0 ? user.Text("email") : "—"),
            KeyValueRow(T("web.admin.users.detail.access.signIn.role"), null, user.Flag("is_owner") ? T("web.admin.users.detail.access.role.owner") : user.Text("role") == "admin" ? T("web.admin.users.detail.access.role.admin") : T("web.admin.users.detail.access.role.user")),
            KeyValueRow(T("web.admin.users.detail.access.signIn.canSignIn"), null, YesNo(user.Flag("enabled"))),
            KeyValueRow(T("web.admin.users.detail.access.signIn.profilesAllowed"), null, effective.Count("max_profiles").ToString(CultureInfo.CurrentCulture),
                _rows.ContainsKey("profiles") ? T("web.admin.users.detail.access.signIn.profilesUsed", ("used", Rows("profiles").Count)) : null, SourceTag(user["max_profiles"] != null ? "custom" : PolicySource())),
        ]), id: "UsersAccessSignIn");

        // Library access
        var libraryTitle = T("web.admin.users.detail.common.cardTitle.library");
        var admin = user.Text("role") == "admin";
        var groupName = admin ? T("web.admin.users.list.adminGroupNone") : user.Text("access_group_id").Length == 0 ? T("web.admin.users.detail.access.library.noGroup") : GroupName(user);
        var permissions = (effective?["permissions"] as JsonArray)?.Select(item => item?.GetValue<string>()).ToArray() ?? [];
        var library = DetailCard(libraryTitle, admin ? T("web.admin.users.detail.access.library.adminDescription") : null, EditAction(libraryTitle, () => EditAsync(libraryTitle, "AdminAccountUpdateBody", user, AdminUsersOperation.UpdatePolicy,
            fields: ["access_group_id", "library_ids", "permissions"]), Policy), RowStack(
        [
            KeyValueRow(T("web.admin.users.detail.access.library.accessGroup"), null, groupName),
            PolicyRow(T("web.admin.users.detail.access.library.libraries"), null, LibrariesText(effective), "library_ids"),
            PolicyRow(T("web.admin.users.detail.access.library.markerEdit"), T("web.admin.users.detail.access.library.markerEditDescription"), YesNo(permissions.Contains("marker_edit")), "permissions"),
            PolicyRow(T("web.admin.users.detail.access.library.metadataCuration"), T("web.admin.users.detail.access.library.metadataCurationDescription"), YesNo(permissions.Contains("metadata_curation")), "permissions"),
        ]), id: "UsersAccessLibrary");

        // Playback & streaming
        var playbackTitle = T("web.admin.users.detail.common.cardTitle.playback");
        var maxTranscodes = effective.Count("max_transcodes");
        var videoText = !effective.Flag("transcode_allowed") ? T("web.admin.users.detail.access.value.off")
            : maxTranscodes == 0 ? T("web.admin.users.detail.access.value.unlimited") : T("web.admin.users.detail.access.value.upToAtATime", ("max", maxTranscodes));
        var playback = DetailCard(playbackTitle, null, EditAction(playbackTitle, () => EditAsync(playbackTitle, "AdminAccountUpdateBody", user, AdminUsersOperation.UpdatePolicy,
            fields: ["max_playback_quality", "max_streams", "max_transcodes", "transcode_allowed", "audio_transcode_allowed", "max_remote_stream_bitrate_kbps", "max_local_stream_bitrate_kbps"]), Policy), RowStack(
        [
            PolicyRow(T("web.admin.users.detail.access.playback.maxQuality"), null, Quality(effective?["max_playback_quality"]?.GetValue<string>()), "max_playback_quality"),
            PolicyRow(T("web.admin.users.detail.access.playback.streams"), T("web.admin.users.detail.access.playback.streamsHint"), effective.Count("max_streams") == 0 ? T("web.admin.users.detail.access.value.unlimited") : effective.Count("max_streams").ToString(CultureInfo.CurrentCulture), "max_streams"),
            PolicyRow(T("web.admin.users.detail.access.playback.video"), null, videoText, "transcode_allowed", "max_transcodes"),
            PolicyRow(T("web.admin.users.detail.access.playback.audio"), null, Allowed(effective.Flag("audio_transcode_allowed")), "audio_transcode_allowed"),
            PolicyRow(T("web.admin.users.detail.access.playback.remoteBitrate"), T("web.admin.users.detail.access.playback.remoteDescription"), Bitrate(effective.Count("max_remote_stream_bitrate_kbps")), "max_remote_stream_bitrate_kbps"),
            PolicyRow(T("web.admin.users.detail.access.playback.localBitrate"), null, Bitrate(effective.Count("max_local_stream_bitrate_kbps")), "max_local_stream_bitrate_kbps"),
        ]), id: "UsersAccessPlayback");

        // Downloads
        var downloadsTitle = T("web.admin.users.detail.common.cardTitle.downloads");
        var downloads = DetailCard(downloadsTitle, null, EditAction(downloadsTitle, () => EditAsync(downloadsTitle, "AdminAccountUpdateBody", user, AdminUsersOperation.UpdatePolicy,
            fields: ["download_allowed", "download_transcode_allowed"]), Policy), RowStack(
        [
            PolicyRow(T("web.admin.users.detail.access.downloads.offline"), null, Allowed(effective.Flag("download_allowed")), "download_allowed"),
            PolicyRow(T("web.admin.users.detail.access.downloads.serverPrepared"), T("web.admin.users.detail.access.downloads.serverPreparedDescription"), Allowed(effective.Flag("download_transcode_allowed")), "download_transcode_allowed"),
        ]), id: "UsersAccessDownloads");

        // Requests
        var requestsTitle = T("web.admin.users.detail.common.cardTitle.requests");
        var rows = new List<Border> { PolicyRow(T("web.admin.users.detail.access.requests.canRequest"), null, YesNo(effective.Flag("requests_allowed")), "requests_allowed") };
        var terms = AdminUserRequestTerms.Describe(user, Document("quota"), Document("group-quota"), Document("request-defaults"));
        var after = AdminUserRequestTerms.Describe(user, Document("quota"), Document("group-quota"), Document("request-defaults"), true);
        rows.Add(KeyValueRow(T("web.admin.users.detail.access.requests.limit"), null, terms));
        if (after != terms) rows.Add(KeyValueRow(T("web.admin.users.detail.access.requests.approval"), null, after));
        var usage = Document("usage");
        if (usage != null && usage.Flag("requests_enabled") && !usage.Flag("unlimited"))
            rows.Add(KeyValueRow(T("web.admin.users.detail.overview.stats.requests"), null, usage.Count("used").ToString(CultureInfo.CurrentCulture) + " / " + usage.Count("max_requests").ToString(CultureInfo.CurrentCulture),
                T("web.admin.users.detail.access.requests.used", ("used", usage.Count("used")))));
        var footer = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        if (Policy)
        {
            footer.Children.Add(Action("Edit request quota", QuotaAsync));
            footer.Children.Add(Action(T("web.admin.users.detail.access.requests.useServerDefault"), () => ResetQuotaAsync(false)));
            if (Document("quota").Text("limit_mode") == "blocked" || Document("quota").Text("approval_mode") == "blocked")
                footer.Children.Add(Action("Clear legacy request block", () => ResetQuotaAsync(true)));
        }
        var requests = DetailCard(requestsTitle, null, EditAction(requestsTitle, () => EditAsync(requestsTitle, "AdminAccountUpdateBody", user, AdminUsersOperation.UpdatePolicy, fields: ["requests_allowed"]), Policy),
            RowStack(rows), footer.Children.Count > 0 ? footer : null, "UsersAccessRequests");
        if (!Policy) panel.Children.Add(Text(T("admin.native.users.access.readOnly"), 13, true));
        panel.Children.Add(Columns([signIn, library, downloads], [playback, requests]));
        return panel;
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

    // ── Activity ───────────────────────────────────────────────────────────────────────────────────────

    private FrameworkElement ActivityTab()
    {
        var panel = new StackPanel { Spacing = 16 };
        if (Feature("account_devices")) panel.Children.Add(DevicesCard());
        panel.Children.Add(IpsCard());
        panel.Children.Add(HistoryCard());
        return panel;
    }

    private void DeviceLink(StackPanel panel, JsonObject row)
    {
        var device = row.Text("device_id");
        if (device.Length == 0) return;
        panel.Children.Add(DeviceButton(device));
    }

    /// <summary>Opens the exact account/device scope in the Devices lane (the same navigation the page always offered).</summary>
    private Button DeviceButton(string device) => Wire(IconAction("arrow-up-right", T("admin.native.users.activity.openDevice"), 28, 14), () =>
    {
        var request = new AdminRouteRequest(AdminRoute.Devices) { Segments = [_review!.User.Text("id"), device] };
        if (!AdminShellPage.Open(request)) _notice.Text = "The device lane is not registered yet. Scope: " + request.Href;
        return Task.CompletedTask;
    });

    private FrameworkElement DevicesCard()
    {
        var devices = Rows("devices");
        UIElement body;
        if (!_rows.ContainsKey("devices")) body = Failure(T("web.admin.users.detail.activity.devices.failed"), Wire(ActionButton(T("common.retry")), () => LoadAsync(_location)));
        else if (devices.Count == 0) body = Empty(T("web.admin.users.detail.activity.devices.empty", ("days", 30)));
        else body = RowStack(devices.Select(device =>
        {
            var platform = device.Text("device_platform");
            var tile = new Border { Width = 32, Height = 32, CornerRadius = new(8), Background = Brush("AccentBackgroundBrush"), Child = MutedIcon("monitor-smartphone", 16) };
            var count = device.Count("override_count");
            var meta = PlatformLabel(platform);
            if (count > 0) meta += (meta.Length > 0 ? " · " : "") + T("web.admin.users.detail.activity.devices.savedSettings", ("count", count), ("count_text", count));
            var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            trailing.Children.Add(Trailing(LastSeen(device.Text("last_seen_at", device.Text("last_updated")))));
            if (device.Text("device_id").Length > 0) trailing.Children.Add(DeviceButton(device.Text("device_id")));
            var row = ListRow(tile, device.Text("device_name", T("web.admin.users.detail.common.unnamedDevice")), meta, trailing);
            return row;
        }));
        var open = Wire(ActionButton(T("web.admin.users.detail.activity.devices.openDevices"), "arrow-up-right", "GhostButtonStyle", 28), () => { AdminShellPage.Open(new(AdminRoute.Devices)); return Task.CompletedTask; });
        return DetailCard(T("web.admin.users.detail.activity.devices.title"), T("web.admin.users.detail.activity.devices.description", ("days", 30)), open, body, id: "UsersActivityDevices");
    }

    private FrameworkElement IpsCard()
    {
        var ips = Rows("ips");
        UIElement body;
        if (!_rows.ContainsKey("ips")) body = Failure(T("web.admin.users.detail.activity.ips.failed"), Wire(ActionButton(T("web.admin.users.detail.activity.ips.reload")), () => LoadAsync(_location)));
        else if (ips.Count == 0) body = Empty(T("web.admin.users.detail.activity.ips.empty"));
        else body = RowStack(ips.Select(ip =>
        {
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var address = Mono(ip.Text("client_ip"), 13); address.TextWrapping = TextWrapping.Wrap; address.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(address);
            var note = ip.Text("location") switch { "local" => T("web.admin.users.detail.activity.ips.local"), "remote" => T("web.admin.users.detail.activity.ips.remote"), _ => "" };
            var requests = Text(T("web.admin.users.detail.activity.ips.requests", ("count", ip.Count("request_count")), ("count_text", ip.Count("request_count").ToString("N0", CultureInfo.CurrentCulture)),
                ("last_seen", LastSeen(ip.Text("last_seen")))), 12, true);
            var side = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
            if (note.Length > 0) { var tag = Text(note, 12, true); tag.TextAlignment = TextAlignment.Right; tag.HorizontalAlignment = HorizontalAlignment.Right; side.Children.Add(tag); }
            requests.TextAlignment = TextAlignment.Right; requests.HorizontalAlignment = HorizontalAlignment.Right; side.Children.Add(requests);
            Grid.SetColumn(side, 1); grid.Children.Add(side);
            return new Border { Child = grid, Padding = new(Gutter, 10, Gutter, 10), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.5) };
        }));
        var footer = _rows.GetValueOrDefault("ips") is { Complete: false } state
            ? Action(T("web.admin.users.detail.common.loadMore"), () => MoreAsync("ips"), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9) : null;
        return DetailCard(T("web.admin.users.detail.activity.ips.title"), T("web.admin.users.detail.activity.ips.description", ("days", int.Parse(_historyDays, CultureInfo.InvariantCulture))), null, body, footer, "UsersActivityIps");
    }

    private FrameworkElement HistoryCard()
    {
        var profiles = Select(T("web.admin.users.detail.common.profileFilter"), new[] { ("", T("web.admin.users.detail.common.allProfiles")) }.Concat(Rows("profiles").Select(row => (row.Text("id"), row.Text("name")))), _historyProfile, _ => { }, 144);
        var period = Select(T("web.admin.users.detail.activity.history.periodLabel"), new[] { ("7", T("web.admin.users.detail.activity.history.period.week")), ("30", T("web.admin.users.detail.activity.history.period.month")),
            ("90", T("web.admin.users.detail.activity.history.period.quarter")), ("365", T("web.admin.users.detail.activity.history.period.year")) }, _historyDays, _ => { }, 144);
        var apply = Wire(ActionButton(T("admin.native.users.activity.applyFilters")), () => { _historyProfile = LogsNativeUi.Selected(profiles); _historyDays = LogsNativeUi.Selected(period); return LoadAsync(_location); });
        var filters = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        filters.Children.Add(profiles); filters.Children.Add(period); filters.Children.Add(apply);
        var history = Rows("history");
        UIElement body;
        if (!_rows.ContainsKey("history")) body = Failure(T("web.admin.users.detail.activity.history.failed"), Wire(ActionButton(T("common.retry")), () => LoadAsync(_location)));
        else if (history.Count == 0) body = Empty(T("web.admin.users.detail.activity.history.empty"));
        else body = HistoryTable(history);
        var footer = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        footer.Children.Add(Text(T("web.admin.users.detail.activity.history.showing", ("shown", history.Count)), 12, true));
        var link = Wire(ActionButton(T("web.admin.users.detail.activity.history.openHistory"), "arrow-up-right", "GhostButtonStyle", 28), () =>
        {
            var query = new Dictionary<string, string> { ["user_id"] = _review!.User.Text("id") };
            if (_historyProfile.Length > 0) query["profile_id"] = _historyProfile;
            AdminShellPage.Open(new AdminRouteRequest(AdminRoute.PlaybackHistory, query));
            return Task.CompletedTask;
        });
        footer.Children.Add(link);
        if (_rows.GetValueOrDefault("history") is { Complete: false } state)
            footer.Children.Add(Action(T("web.admin.users.detail.common.loadMore"), () => MoreAsync("history"), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
        var card = DetailCard(T("web.admin.users.detail.activity.history.title"), null, filters, body, footer, "UsersActivityHistory");
        return card;
    }

    private FrameworkElement HistoryTable(IReadOnlyList<JsonObject> plays)
    {
        var grid = new Grid();
        // Below sm the official table keeps only Title, Method and When.
        int[] logical = _phone ? [0, 2, 4] : [0, 1, 2, 3, 4];
        (GridLength Width, double Min)[] all = [(new(1, GridUnitType.Star), 120), (GridLength.Auto, 0), (GridLength.Auto, 0), (GridLength.Auto, 0), (GridLength.Auto, 0)];
        var columns = logical.Select(index => all[index]).ToArray();
        foreach (var (width, min) in columns) grid.ColumnDefinitions.Add(new() { Width = width, MinWidth = min });
        var row = 0;
        void Place(UIElement element, int column, double vertical = 8)
        {
            var position = Array.IndexOf(logical, column);
            if (position < 0) return;
            var cell = new Border { Child = element, Padding = new(position == 0 ? Gutter : 12, vertical, position == columns.Length - 1 ? Gutter : 12, vertical), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(cell, row); Grid.SetColumn(cell, position); grid.Children.Add(cell);
        }
        void Band()
        {
            var band = new Border { BorderBrush = Tint("BorderBrush", 0.6), BorderThickness = new(0, row == 0 ? 0 : 1, 0, 0) };
            Grid.SetRow(band, row); Grid.SetColumnSpan(band, columns.Length); grid.Children.Add(band);
        }
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        string[] heads = ["title", "profile", "method", "watched", "when"];
        for (var column = 0; column < heads.Length; column++)
        {
            var head = Medium(T("web.admin.users.detail.activity.history.column." + heads[column]), 14, true); head.TextWrapping = TextWrapping.NoWrap; head.LineHeight = 20;
            Place(head, column, 12);
        }
        row++;
        foreach (var play in plays)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Band();
            var title = Medium(PlayTitle(play), 14); title.FontWeight = FontWeights.SemiBold; title.TextWrapping = TextWrapping.Wrap; title.IsTextSelectionEnabled = true;
            Place(title, 0);
            var profile = Text(play.Text("profile_name", play.Text("profile_id")), 14); profile.TextWrapping = TextWrapping.NoWrap; Place(profile, 1);
            Place(MethodBadge(play.Text("play_method")), 2);
            Place(Bar(WatchedFraction(play), 96), 3);
            var when = Text(ShortDate(play.Text("ended_at")), 14, true); when.TextWrapping = TextWrapping.NoWrap; Place(when, 4);
            row++;
        }
        return FitTable(grid);
    }
}
