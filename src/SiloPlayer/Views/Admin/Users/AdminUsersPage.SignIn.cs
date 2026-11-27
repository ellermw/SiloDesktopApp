using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin.Users;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    /// <summary>A card body that holds free text rather than rows: the text and actions with the card's padding.</summary>
    private static Border Body(params UIElement[] children)
    {
        var stack = new StackPanel { Spacing = 12 };
        foreach (var child in children) stack.Children.Add(child);
        return new Border { Child = stack, Padding = new(Gutter, 14, Gutter, 16) };
    }

    private FrameworkElement SignInTab()
    {
        var user = _review!.User;
        var username = user.Text("username");
        var panel = new StackPanel { Spacing = 24 };

        // Password sign-in
        var password = new List<UIElement>
        {
            Text(T(user.Flag("password_login") ? "web.admin.common.adminUserSignIn.password.on" : "web.admin.common.adminUserSignIn.password.off", ("username", username)), 14),
            Text(T(user.Flag("password_login") ? "web.admin.common.adminUserSignIn.password.connectingTurnsOff" : "web.admin.common.adminUserSignIn.password.settingTurnsOn"), 12, true),
        };
        if (user.Flag("password_change_required"))
            password.Add(TintedBadge(T("web.admin.users.detail.header.temporaryPassword"), AdminMonitorUi.Amber500, AdminMonitorUi.Amber300));
        var passwordActions = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        if (Manage)
        {
            if (PasswordManage) passwordActions.Children.Add(Wire(ActionButton(user.Text("id") == _review.Viewer.Text("id") ? T("admin.native.users.signin.setMyPassword") : T("admin.native.users.signin.setPassword")), SetPasswordAsync));
            else password.Add(Text(T("admin.native.users.signin.ownerOnly"), 12, true));
            if (_review.Viewer.Flag("is_owner") && user.Text("role") == "admin" && AdminUsersRules.Feature(Document("external-capabilities"), "break_glass"))
                passwordActions.Children.Add(Wire(ActionButton(T("admin.native.users.signin.breakGlass")), EditBreakGlassAsync));
        }
        if (passwordActions.Children.Count > 0) password.Add(passwordActions);
        password.Add(Text(T("admin.native.users.signin.passwordNote"), 12, true));
        var passwordRows = new List<Border>();
        if (user.Flag("break_glass") || user.Text("role") == "admin")
            passwordRows.Add(KeyValueRow(T("web.admin.common.adminUserSignIn.breakGlass.title"), null, YesNo(user.Flag("break_glass"))));
        var passwordBody = new StackPanel();
        passwordBody.Children.Add(Body(password.ToArray()));
        if (passwordRows.Count > 0)
        {
            var extra = RowStack(passwordRows);
            ((Border)extra.Children[0]).BorderThickness = new(0, 1, 0, 0);
            passwordBody.Children.Add(extra);
        }
        var passwordCard = DetailCard(T("web.admin.common.adminUserSignIn.password.title"), null, null, passwordBody, null, "UsersSignInPassword");

        // External identities
        FrameworkElement identitiesCard;
        if (AdminUsersRules.Feature(Document("external-capabilities"), "admin_identities"))
        {
            var identities = Rows("identities");
            UIElement list;
            if (!_rows.ContainsKey("identities")) list = Failure(T("web.admin.common.adminUserSignIn.identities.loadFailed"), Wire(ActionButton(T("common.retry")), () => LoadAsync(_location)));
            else if (identities.Count == 0) list = Empty(T("web.admin.common.adminUserSignIn.identities.empty"));
            else list = RowStack(identities.Select(identity =>
            {
                var provider = identity.Text("provider_name", identity.Text("installation_id"));
                var details = new List<string>();
                if (identity.Text("username").Length > 0) details.Add(identity.Text("username"));
                if (identity.Text("email").Length > 0) details.Add(identity.Text("email"));
                if (identity.Text("display_name").Length > 0) details.Add(identity.Text("display_name"));
                if (identity.Text("linked_at").Length > 0)
                    details.Add(T("web.admin.common.adminUserSignIn.identity.connected", ("signed_in", identity.Text("last_sign_in_at").Length > 0 ? "yes" : "no"), ("linked", AccountDate(identity.Text("linked_at"))), ("last_sign_in", AccountDate(identity.Text("last_sign_in_at")))));
                if (identity.Text("last_check_status").Length > 0)
                    details.Add(T("web.admin.common.adminUserSignIn.identity.lastCheck", ("checked", identity.Text("last_checked_at").Length > 0 ? "yes" : "no"), ("status", Label(identity.Text("last_check_status"))), ("checked_at", DateTimeShort(identity.Text("last_checked_at")))));
                var row = ListRow(null, provider, string.Join(" · ", details), Manage ? Wire(ActionButton(T("web.admin.common.adminUserSignIn.unlink"), null, "OutlineButtonStyle", 28, T("web.admin.common.adminUserSignIn.identity.unlinkLabel", ("provider", provider))), async () =>
                {
                    var generation = _generation;
                    var path = AdminUsersApi.UserPath(user.Text("id")) + "/identities";
                    var baseline = await ReadResourceAsync(path, generation);
                    var lastWay = !user.Flag("password_login") && !user.Flag("break_glass") && Rows("identities").Count == 1;
                    await ConfirmAsync(new(identity.Text("provider_name").Length > 0 ? T("web.admin.common.adminUserSignIn.unlinkConfirm.title", ("provider", identity.Text("provider_name"))) : T("web.admin.common.adminUserSignIn.unlinkConfirm.titleUnknownProvider"),
                        T("web.admin.common.adminUserSignIn.unlinkConfirm.description", ("username", username), ("last_way", lastWay ? "yes" : "no")), T("web.admin.common.adminUserSignIn.unlink"), Destructive: true),
                        new(AdminUsersOperation.UnlinkIdentity, ResourceId: identity.Text("id"), Readbacks: [new(path, baseline.Body!)]), expectedGeneration: generation);
                }) : null);
                if (identity.Text("external_subject").Length > 0 && row.Child is Grid grid && grid.Children.OfType<StackPanel>().FirstOrDefault() is { } copy)
                {
                    var subject = Mono(T("web.admin.common.adminUserSignIn.identity.subject", ("subject", identity.Text("external_subject"))), 12, true); subject.TextWrapping = TextWrapping.Wrap; subject.IsTextSelectionEnabled = true; copy.Children.Add(subject);
                }
                return row;
            }));
            var footer = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
            if (_rows.GetValueOrDefault("providers") is { Complete: false })
                footer.Children.Add(Action(T("admin.native.users.signin.loadProviders"), () => MoreAsync("providers")));
            identitiesCard = DetailCard(T("web.admin.common.adminUserSignIn.identities.title"), null,
                Manage ? Wire(ActionButton(T("web.admin.common.adminUserSignIn.identities.connect"), "plus", "OutlineButtonStyle", 28, T("admin.native.users.signin.linkIdentity")), LinkIdentityAsync) : null,
                list, footer.Children.Count > 0 ? footer : null, "UsersSignInIdentities");
        }
        else identitiesCard = DetailCard(T("web.admin.common.adminUserSignIn.identities.title"), null, null, Body(Text(T("admin.native.users.signin.identitiesUnsupported"), 14, true)), null, "UsersSignInIdentities");

        var top = new Grid { ColumnSpacing = 24, RowSpacing = 24 };
        if (_wide)
        {
            top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            top.Children.Add(passwordCard); Grid.SetColumn(identitiesCard, 1); top.Children.Add(identitiesCard);
        }
        else
        {
            top.RowDefinitions.Add(new() { Height = GridLength.Auto }); top.RowDefinitions.Add(new() { Height = GridLength.Auto });
            top.Children.Add(passwordCard); Grid.SetRow(identitiesCard, 1); top.Children.Add(identitiesCard);
        }
        panel.Children.Add(top);

        // Login sessions
        if (Feature("login_sessions") && AdminUsersRules.Feature(Document("sessions-capabilities"), "admin_management"))
        {
            var sessions = Rows("login-sessions");
            UIElement list;
            if (!_rows.ContainsKey("login-sessions")) list = Failure(T("web.settings.sessions.loadFailed"), Wire(ActionButton(T("common.retry")), () => LoadAsync(_location)));
            else if (sessions.Count == 0) list = Empty(T("web.settings.sessions.admin.empty"));
            else list = RowStack(sessions.Select(session =>
            {
                var device = session.Text("device_name", T("web.settings.sessions.device.unknown"));
                var details = new List<string>();
                if (session.Text("device_platform").Length > 0) details.Add(PlatformLabel(session.Text("device_platform")));
                details.Add(LastSeenText(session.Text("last_seen_at")));
                if (session.Text("created_at").Length > 0) details.Add(T("web.settings.sessions.row.signedIn") + " " + DateTimeShort(session.Text("created_at")));
                if (session.Text("expires_at").Length > 0) details.Add(T("web.settings.sessions.row.expires") + " " + DateTimeShort(session.Text("expires_at")));
                var trailing = new LogsWrapPanel { Spacing = 4, RowSpacing = 4 };
                if (session.Flag("current")) trailing.Children.Add(AdminLibrariesUi.Badge(T("web.settings.sessions.row.thisBrowser"), AdminLibrariesUi.BadgeTone.Secondary));
                if (Manage)
                {
                    var label = T("web.settings.sessions.row.signOutDevice", ("device", device));
                    var button = ActionButton(label, height: 28, name: T("admin.native.users.signin.signOutOne"));
                    LoginSessionAction.AllowWrapping(button, label);
                    trailing.Children.Add(Wire(button,
                        () => ConfirmAsync(new(T("web.settings.sessions.confirm.deviceTitle", ("device", device)), T("web.settings.sessions.confirm.adminClientDescription", ("username", username)) + " " + T("web.settings.sessions.confirm.note"), T("web.settings.sessions.row.signOutDevice", ("device", device)), Destructive: true),
                            new(AdminUsersOperation.RevokeSession, ResourceId: session.Text("id"), Readbacks: Baselines("login-sessions")))));
                }

                var row = ListRow(Avatar(device, sessions.ToList().IndexOf(session)), device, string.Join(" · ", details), null);
                var grid = (Grid)row.Child;
                var copy = grid.Children.OfType<StackPanel>().Single();
                var title = (TextBlock)copy.Children[0];
                title.TextWrapping = TextWrapping.Wrap;
                title.TextTrimming = TextTrimming.None;
                title.MaxLines = 0;
                if (session.Text("ip_address").Length > 0)
                {
                    var ip = Mono(T("web.settings.sessions.row.ipAddress") + ": " + session.Text("ip_address"), 12, true);
                    ip.TextWrapping = TextWrapping.Wrap;
                    copy.Children.Add(ip);
                }
                row.Child = null;
                row.Child = new SplitRow(grid, trailing.Children.Count == 0 ? null : trailing)
                {
                    MinStart = 156, CenterVertically = true
                };
                return row;
            }));
            var signOutAll = Manage ? Wire(ActionButton(T("web.settings.sessions.signOutEverywhere"), null, "OutlineButtonStyle", 32, T("admin.native.users.signin.signOutAll")), () => ConfirmAsync(new(T("web.settings.sessions.confirm.allTitle", ("username", username)), T("web.settings.sessions.confirm.allDescription", ("username", username)) + " " + T("web.settings.sessions.confirm.note"), T("web.settings.sessions.signOutEverywhere"), Destructive: true),
                new(AdminUsersOperation.RevokeSessions, Readbacks: Baselines("login-sessions")))) : null;
            if (signOutAll is Button all)
                LoginSessionAction.AllowWrapping(all, T("web.settings.sessions.signOutEverywhere"));
            LogsWrapPanel? more = null;
            if (_rows.GetValueOrDefault("login-sessions") is { Complete: false } state)
            {
                more = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
                more.Children.Add(Text(T("admin.native.users.partial", ("count", state.Items.Count)), 13, true));
                more.Children.Add(Action(T("web.settings.sessions.loadMore"), () => MoreAsync("login-sessions"), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
            }
            panel.Children.Add(DetailCard(T("web.settings.sessions.title"), T("web.settings.sessions.adminDescription", ("username", username)), signOutAll, list,
                more, "UsersSignInSessions"));
        }
        else panel.Children.Add(Text(T("admin.native.users.signin.sessionsUnsupported"), 14, true));
        return panel;
    }

    private static string LastSeenText(string value)
    {
        var at = Parse(value);
        if (at == null) return T("web.settings.sessions.lastSeen.never");
        var minutes = (long)Math.Floor((DateTimeOffset.UtcNow - at.Value).TotalMinutes);
        if (minutes < 1) return T("web.settings.sessions.lastSeen.active");
        if (minutes < 60) return T("web.settings.sessions.lastSeen.minutes", ("count", minutes), ("count_text", minutes));
        var hours = minutes / 60;
        if (hours < 24) return T("web.settings.sessions.lastSeen.hours", ("count", hours), ("count_text", hours));
        var days = hours / 24;
        return T("web.settings.sessions.lastSeen.days", ("count", days), ("count_text", days));
    }

}
