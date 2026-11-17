using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private async Task ReadListAsync(long generation)
    {
        switch (_location.Get("tab"))
        {
            case "invitations":
                await DocumentAsync("invitations-capabilities", "/api/v2/admin/invitations/capabilities", generation);
                if (AdminUsersRules.Available(Document("invitations-capabilities"))) await RowsAsync("invitations", "/api/v2/admin/invitations", generation);
                break;
            case "invite-codes":
                await DocumentAsync("codes-capabilities", "/api/v2/admin/invite-codes/capabilities", generation);
                if (AdminUsersRules.Available(Document("codes-capabilities"))) await RowsAsync("codes", "/api/v2/admin/invite-codes", generation);
                break;
            default:
                if (!AdminUsersRules.Available(_review?.Capabilities)) return;
                var path = "/api/v2/admin/users";
                if (Feature("exact_identity_filter") && !string.IsNullOrWhiteSpace(_location.Get("identity"))) path += "?identity=" + Uri.EscapeDataString(_location.Get("identity")!.Trim());
                await RowsAsync("users", path, generation, limit: _pageSize);
                if (Feature("access_groups")) await RowsAsync("groups", "/api/v2/admin/access-groups", generation);
                break;
        }
    }
    private void RenderList()
    {
        _body.Children.Add(Text("Users", 28));
        var tabs = Inline();
        foreach (var entry in new[] { ("users", "Users"), ("invitations", "Invitations"), ("invite-codes", "Invite Codes") })
            tabs.Children.Add(Action(entry.Item2, () => { _displayPage = 0; Navigate(null, entry.Item1); return Task.CompletedTask; }));
        _body.Children.Add(tabs);
        switch (_location.Get("tab"))
        {
            case "invitations": RenderInvitations(); return;
            case "invite-codes": RenderCodes(); return;
        }
        if (!AdminUsersRules.Available(_review?.Capabilities)) { _body.Children.Add(Text("Account management is unavailable for this authority.")); return; }
        _body.Children.Add(Action("Create user", CreateUserAsync));
        var filters = Inline();
        var search = new TextBox { Header = "Search loaded users, email or group", Text = _search, MinWidth = 260, MaxLength = 320 };
        search.TextChanged += (_, _) => _search = search.Text;
        filters.Children.Add(search);
        var groups = Choice("Group", new[] { ("", "All groups") }.Concat(Rows("groups").Select(group => (group.Text("id"), group.Text("name")))), _groupFilter);
        groups.SelectionChanged += (_, _) => _groupFilter = Selected(groups);
        filters.Children.Add(groups);
        if (_rows.GetValueOrDefault("groups") is { Complete: false } groupState)
            filters.Children.Add(Action("Load next bounded group page", () => MoreAsync("groups"), groupState.Items.Count < AdminUsersApi.MaxRows && groupState.Seen.Count < 9));
        var sort = Choice("Sort", new[] { "username", "email", "role", "enabled", "access_group_id", "last_active_at", "created_at" }.Select(key => (key, Label(key))), _sort);
        sort.SelectionChanged += (_, _) => _sort = Selected(sort);
        filters.Children.Add(sort);
        var descending = new CheckBox { Content = "Descending", IsChecked = _descending };
        descending.Checked += (_, _) => _descending = true;
        descending.Unchecked += (_, _) => _descending = false;
        filters.Children.Add(descending);
        var size = Choice("Rows", new[] { "25", "50", "100" }.Select(value => (value, value)), _pageSize.ToString());
        size.SelectionChanged += (_, _) => { _pageSize = int.Parse(Selected(size)); _displayPage = 0; };
        filters.Children.Add(size);
        filters.Children.Add(Action("Apply loaded filters", () => { _displayPage = 0; Render(); return Task.CompletedTask; }));
        _body.Children.Add(filters);
        if (Feature("exact_identity_filter"))
        {
            var exact = new TextBox { Header = "Exact username/email (server identity filter)", Text = _location.Get("identity") ?? "", MaxLength = 320, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
            _body.Children.Add(exact);
            _body.Children.Add(Action("Apply exact identity", () =>
            {
                _displayPage = 0;
                var request = new SiloPlayer.Core.Services.Admin.AdminRouteRequest(SiloPlayer.Core.Services.Admin.AdminRoute.Users,
                    new Dictionary<string, string> { ["tab"] = "users", ["identity"] = exact.Text.Trim() });
                AdminShellPage.TryReplace(this, request);
                return LoadAsync(request);
            }));
        }
        string Group(JsonObject user) => Rows("groups").FirstOrDefault(group => group.Text("id") == user.Text("access_group_id"))?.Text("name") ?? user.Text("access_group_id", "Default");
        string Value(JsonObject user) => _sort == "role" && user.Flag("is_owner") ? "Owner" : _sort == "access_group_id" ? Group(user) : user[_sort].Display();
        var users = Rows("users").Where(user => (_groupFilter.Length == 0 || user.Text("access_group_id") == _groupFilter) &&
            (_search.Length == 0 || new[] { user.Text("username"), user.Text("email"), Group(user) }.Any(value => value.Contains(_search, StringComparison.CurrentCultureIgnoreCase))));
        users = _descending ? users.OrderByDescending(Value, StringComparer.CurrentCultureIgnoreCase) : users.OrderBy(Value, StringComparer.CurrentCultureIgnoreCase);
        var list = users.ToList();
        _displayPage = Math.Clamp(_displayPage, 0, Math.Max(0, (list.Count - 1) / _pageSize));
        var shown = list.Skip(_displayPage * _pageSize).Take(_pageSize).ToArray();
        if (_compact)
            AddRows("users", $"Users · {list.Count} matching loaded records · page {_displayPage + 1}",
                ["username", "email", "role", "is_owner", "enabled", "access_group_id", "last_active_at", "created_at"],
                (panel, user) => { panel.Children.Add(Text("Group: " + Group(user))); panel.Children.Add(Action("Open " + user.Text("username"), () => { Navigate(user.Text("id"), "overview"); return Task.CompletedTask; })); }, shown);
        else RenderUsersTable(shown, Group, list.Count);
        if (_rows.GetValueOrDefault("groups") is { Complete: false }) _body.Children.Add(Action("Load next group page", () => MoreAsync("groups")));
        var paging = Inline();
        paging.Children.Add(Action("Previous display page", () => { _displayPage--; Render(); return Task.CompletedTask; }, _displayPage > 0));
        paging.Children.Add(Action("Next display page", () => { _displayPage++; Render(); return Task.CompletedTask; }, (_displayPage + 1) * _pageSize < list.Count));
        _body.Children.Add(paging);
    }
    private void RenderUsersTable(IEnumerable<JsonObject> users, Func<JsonObject, string> group, int count)
    {
        var table = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        foreach (var weight in new[] { 1.5, 1.1, 1.0, 1.0, 1.3, 1.0 }) table.ColumnDefinitions.Add(new() { Width = new GridLength(weight, GridUnitType.Star) });
        var rowIndex = 0;
        void Row(IReadOnlyList<FrameworkElement> cells)
        {
            table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var column = 0; column < cells.Count; column++) { Grid.SetRow(cells[column], rowIndex); Grid.SetColumn(cells[column], column); table.Children.Add(cells[column]); }
            rowIndex++;
        }
        Row(new FrameworkElement[] { Text("User / email"), Text("Role / status"), Text("Group"), Text("Last activity"), Text("Created"), Text("Action") });
        foreach (var user in users) Row(new FrameworkElement[]
        {
            Text(user.Text("username") + "\n" + user.Text("email")),
            Text((user.Flag("is_owner") ? "Owner" : Label(user.Text("role"))) + "\n" + (user.Flag("enabled") ? "Enabled" : "Disabled")),
            Text(group(user)), Text(user["last_active_at"].Display()), Text(user.Text("created_at")),
            Action("Open " + user.Text("username"), () => { Navigate(user.Text("id"), "overview"); return Task.CompletedTask; })
        });
        var content = Panel(); content.Children.Add(table);
        if (rowIndex == 1) content.Children.Add(Text(_rows.ContainsKey("users") ? "No matching users." : "No successful user snapshot is loaded."));
        if (_rows.GetValueOrDefault("users") is { Complete: false } state) content.Children.Add(Action("Load next bounded user page", () => MoreAsync("users"), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
        _body.Children.Add(Card($"Users · {count} matching loaded records · page {_displayPage + 1}", content));
    }
    private void RenderInvitations()
    {
        var capability = Document("invitations-capabilities");
        if (!AdminUsersRules.Available(capability)) { _body.Children.Add(Text("Invitations unavailable: " + capability.Text("state", "unknown"))); return; }
        _body.Children.Add(Text("Expiry is chosen by the server and shown on each invitation. Link delivery never sends an email. Email delivery must be configured."));
        _body.Children.Add(Action("Create invitation", async () =>
        {
            var initial = new JsonObject { ["delivery"] = "link", ["role"] = "user", ["create_profile"] = capability!.Flag("default_profile"), ["show_tour"] = true };
            await EditAsync("Create invitation", "AdminInvitationCreateInputBody", initial, AdminUsersOperation.CreateInvitation, create: true,
                locked: (_review!.Viewer.Flag("is_owner") ? Array.Empty<string>() : new[] { "role" })
                    .Concat(capability.Flag("default_profile") && capability.Flag("profileless") ? [] : new[] { "create_profile" }).ToArray(), sensitiveResult: true);
        }));
        AddRows("invitations", "Invitation history", ["email", "delivery", "role", "access_group_id", "library_ids", "create_profile", "show_tour", "note", "status", "expires_at", "invited_by_name", "created_at"], (panel, row) =>
        {
            if (row.Text("status") is "accepted" or "revoked") return;
            panel.Children.Add(Action("Replace with a link", () => ConfirmAsync("Replace invitation link", "Any earlier link stops working. This does not email the recipient.",
                new(AdminUsersOperation.ResendInvitation, new JsonObject { ["delivery"] = "link" }, row.Text("id")), sensitiveResult: true)));
            if (capability!.Flag("email_delivery") && row.Text("email").Length > 0)
                panel.Children.Add(Action("Resend email", () => ConfirmAsync("Resend invitation email", "This replaces the earlier link and attempts email delivery.",
                    new(AdminUsersOperation.ResendInvitation, new JsonObject { ["delivery"] = "email" }, row.Text("id")), sensitiveResult: true)));
            panel.Children.Add(Action("Revoke", () => ConfirmAsync("Revoke invitation", "The invitation will no longer be usable.", new(AdminUsersOperation.RevokeInvitation, ResourceId: row.Text("id")))));
        });
    }
    private void RenderCodes()
    {
        var capability = Document("codes-capabilities");
        if (!AdminUsersRules.Available(capability)) { _body.Children.Add(Text("Invite Codes unavailable: " + capability.Text("state", "unknown"))); return; }
        _body.Children.Add(Text("Invite codes are sensitive. Copies are explicit; no generated values are logged."));
        if (capability!.Flag("client_selected_code")) _body.Children.Add(Action("Create invite code", () => EditAsync("Create invite code", "AdminInviteCodeCreateInputBody",
            new JsonObject { ["code"] = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(), ["label"] = "", ["max_uses"] = 10 }, AdminUsersOperation.CreateCode, create: true)));
        AddRows("codes", "Invite codes", ["code", "label", "enabled", "use_count", "max_uses", "created_at"], (panel, row) =>
        {
            panel.Children.Add(Action("Copy code", () => CopySensitiveAsync(row.Text("code"))));
            panel.Children.Add(Action("Edit", () => EditAsync("Edit invite code", "AdminInviteCodeUpdateInputBody", row, AdminUsersOperation.UpdateCode, row.Text("id"))));
            panel.Children.Add(Action("Top up", () => EditAsync("Add uses", "AdminInviteCodeTopUpInputBody", new JsonObject { ["additional_uses"] = 1 }, AdminUsersOperation.TopUpCode, row.Text("id"), create: true)));
            panel.Children.Add(Action(row.Flag("enabled") ? "Disable" : "Enable", () => ConfirmAsync("Change invite code availability", "Review the code and usage before changing availability.",
                new(AdminUsersOperation.UpdateCode, new JsonObject { ["enabled"] = !row.Flag("enabled") }, row.Text("id")))));
            panel.Children.Add(Action("Delete", () => ConfirmAsync("Delete invite code", "Existing accounts remain. This code can no longer be used.", new(AdminUsersOperation.DeleteCode, ResourceId: row.Text("id")))));
        });
    }
}

