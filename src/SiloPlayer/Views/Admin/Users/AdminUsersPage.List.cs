using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Services.Admin.Users;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminUsersPage
{
    private ContentControl? _usersResults;

    private async Task ReadListAsync(long generation)
    {
        switch (_location.Get("tab"))
        {
            case "invitations":
                await DocumentAsync("invitations-capabilities", "/api/v2/admin/invitations/capabilities", generation);
                if (_workspace.IsCurrent(generation) && !_loadErrors.ContainsKey("invitations-capabilities") &&
                    AdminUsersRules.Available(Document("invitations-capabilities"))) await RowsAsync("invitations", "/api/v2/admin/invitations", generation);
                break;
            case "invite-codes":
                await DocumentAsync("codes-capabilities", "/api/v2/admin/invite-codes/capabilities", generation);
                if (_workspace.IsCurrent(generation) && !_loadErrors.ContainsKey("codes-capabilities") &&
                    AdminUsersRules.Available(Document("codes-capabilities"))) await RowsAsync("codes", "/api/v2/admin/invite-codes", generation);
                break;
            default:
                if (!AdminUsersRules.Available(_review?.Capabilities)) return;
                _lookupText = ""; _lookupRows = [];
                var path = "/api/v2/admin/users";
                if (Feature("exact_identity_filter") && !string.IsNullOrWhiteSpace(_location.Get("identity"))) path += "?identity=" + Uri.EscapeDataString(_location.Get("identity")!.Trim());
                await Task.WhenAll(RowsAsync("users", path, generation, limit: _pageSize),
                    Feature("access_groups") ? RowsAsync("groups", "/api/v2/admin/access-groups", generation) : Task.CompletedTask);
                break;
        }
    }

    // ── The list page: header, tab strip, one tab ─────────────────────────────────────────────────────

    private void RenderList()
    {
        var available = AdminUsersRules.Available(_review?.Capabilities);
        var actions = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        actions.Children.Add(Wire(ActionButton(T("web.admin.users.list.accessGroupsLink"), "settings-2"), () => { AdminShellPage.Open(new(AdminRoute.AccessGroups)); return Task.CompletedTask; }));
        actions.Children.Add(Wire(ActionButton(T("web.admin.users.list.addUser"), "plus", "AccentButtonStyle"), CreateUserAsync, available));
        _body.Children.Add(LogsNativeUi.Header(T("web.admin.users.list.title"), T("web.admin.users.list.subtitle"), actions));
        AddLoadFailures();
        if (!available) _body.Children.Add(Text(T("web.admin.users.list.unavailable"), 14));
        var tab = _location.Get("tab") is "invitations" or "invite-codes" ? _location.Get("tab")! : "users";
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(TabStrip(
        [
            ("users", T("web.admin.users.list.tab.users"), null),
            ("invitations", T("web.admin.users.list.tab.invitations"), null),
            ("invite-codes", T("web.admin.users.list.tab.inviteCodes"), null),
        ], tab, value => { if (!_actionBusy) Navigate(null, value); }, "UsersTab_"));
        stack.Children.Add(tab switch { "invitations" => InvitationsTab(), "invite-codes" => CodesTab(), _ => available ? UsersTab() : new Border() });
        _body.Children.Add(stack);
    }

    // ── Users ─────────────────────────────────────────────────────────────────────────────────────────────

    private string GroupName(JsonObject user) => Rows("groups").FirstOrDefault(group => group.Text("id") == user.Text("access_group_id"))?.Text("name") ?? T("web.admin.users.list.unknownGroup");

    private FrameworkElement UsersTab()
    {
        var panel = new StackPanel { Spacing = 16 };
        var groups = Select(T("web.admin.users.list.groupFilterLabel"),
            new[] { ("", T("web.admin.users.list.groupFilterAll")) }.Concat(Rows("groups").Select(group => (group.Text("id"), group.Text("name")))).Append(("none", T("web.admin.users.list.noGroup"))),
            _groupFilter, value => { _groupFilter = value; _displayPage = 0; RefreshUsers(); }, _phone ? double.NaN : 224);
        var search = SearchBox(T("web.admin.users.list.searchPlaceholder"), T("web.admin.users.list.clearSearch"), _search, value => { _search = value; _displayPage = 0; RefreshUsers(); QueueExactLookup(); }, out _);
        var sort = Sorter();
        var filters = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        if (_wideTable)
        {
            filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            filters.Children.Add(search); Grid.SetColumn(groups, 1); filters.Children.Add(groups);
        }
        else
        {
            filters.ColumnDefinitions.Add(_phone ? new() { Width = new(1, GridUnitType.Star) } : new() { Width = GridLength.Auto });
            if (!_phone) filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            filters.RowDefinitions.Add(new() { Height = GridLength.Auto }); filters.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetColumnSpan(search, _phone ? 1 : 2); filters.Children.Add(search);
            Grid.SetRow(groups, 1); filters.Children.Add(groups);
            if (_phone) { filters.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetRow(sort, 2); }
            else { Grid.SetRow(sort, 1); Grid.SetColumn(sort, 1); }
            filters.Children.Add(sort);
        }
        panel.Children.Add(filters);
        _usersResults = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
        panel.Children.Add(_usersResults);
        RefreshUsers();
        return panel;
    }

    /// <summary>The Sort control shown below lg: a field select and a direction button.</summary>
    private FrameworkElement Sorter()
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var fields = new[] { "username", "email", "role", "enabled", "created_at", "last_active_at" };
        var select = Select(T("admin.native.users.sort"), fields.Select(field => (field, SortLabel(field))), _sort, value => { if (value != _sort) HandleSort(value); });
        var direction = new Button
        {
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"], Width = 36, Height = 36, MinWidth = 36, MinHeight = 36, Padding = new(0),
            CornerRadius = new(AdminLibrariesUi.RadiusMd), Content = AdminMonitorUi.Icon(_descending ? "chevron-down" : "chevron-up", 16, Brush("PrimaryTextBrush")),
        };
        var name = _descending ? T("admin.native.users.descending") : "Ascending";
        AutomationProperties.SetName(direction, name); ToolTipService.SetToolTip(direction, name);
        direction.Click += (_, _) => { _descending = !_descending; _displayPage = 0; Render(); };
        row.Children.Add(select); Grid.SetColumn(direction, 1); row.Children.Add(direction);
        return row;
    }

    private static string SortLabel(string field) => field switch
    {
        "username" => T("web.admin.users.list.column.username"), "email" => T("web.admin.users.list.column.email"), "role" => T("web.admin.users.list.column.role"),
        "enabled" => T("web.admin.users.list.column.status"), "created_at" => T("web.admin.users.list.column.created"), "last_active_at" => T("web.admin.users.list.column.lastActive"),
        _ => Label(field),
    };

    /// <summary>Choosing a sort field: a new field starts newest first for dates and ascending otherwise; the current field flips direction.</summary>
    private void HandleSort(string field)
    {
        _displayPage = 0;
        if (field == _sort) _descending = !_descending;
        else { _sort = field; _descending = field is "created_at" or "last_active_at"; }
        Render();
    }

    private List<JsonObject> FilteredUsers(bool withLookup = true)
    {
        var users = (withLookup ? Rows("users").Concat(ExactExtras()) : Rows("users")).Where(user =>
        {
            if (_search.Length > 0 && !(user.Text("username").Contains(_search, StringComparison.CurrentCultureIgnoreCase) || user.Text("email").Contains(_search, StringComparison.CurrentCultureIgnoreCase)
                || (user.Text("role") != "admin" && user.Text("access_group_id").Length > 0 && GroupName(user).Contains(_search, StringComparison.CurrentCultureIgnoreCase)))) return false;
            if (_groupFilter.Length == 0) return true;
            if (user.Text("role") == "admin") return false;
            return _groupFilter == "none" ? user.Text("access_group_id").Length == 0 : user.Text("access_group_id") == _groupFilter;
        });
        string RoleText(JsonObject user) => user.Flag("is_owner") ? "Owner" : user.Text("role") == "admin" ? "Admin" : "User";
        long Time(JsonObject user, string key) => Parse(user.Text(key)) is { } at ? at.ToUnixTimeMilliseconds() : long.MinValue;
        var ordered = _sort switch
        {
            "email" => users.OrderBy(user => user.Text("email"), StringComparer.CurrentCultureIgnoreCase),
            "role" => users.OrderBy(RoleText, StringComparer.CurrentCultureIgnoreCase),
            "enabled" => users.OrderBy(user => user.Flag("enabled") ? "active" : "disabled", StringComparer.CurrentCultureIgnoreCase),
            "created_at" => users.OrderBy(user => Time(user, "created_at")),
            "last_active_at" => users.OrderBy(user => Time(user, "last_active_at")),
            _ => users.OrderBy(user => user.Text("username"), StringComparer.CurrentCultureIgnoreCase),
        };
        var list = ordered.ThenBy(user => user.Text("username"), StringComparer.CurrentCultureIgnoreCase).ToList();
        if (_descending) list.Reverse();
        return list;
    }

    /// <summary>Rebuilds only the results (table or compact list and its pager), so the search field keeps focus while it filters.</summary>
    private void RefreshUsers()
    {
        if (_usersResults == null) return;
        var list = FilteredUsers();
        _displayPage = Math.Clamp(_displayPage, 0, Math.Max(0, (list.Count - 1) / _pageSize));
        var shown = list.Skip(_displayPage * _pageSize).Take(_pageSize).ToArray();
        var stack = new StackPanel();
        stack.Children.Add(_wideTable ? UsersTable(shown) : UsersCompact(shown));
        if (shown.Length == 0 && !_loadErrors.ContainsKey("users"))
            stack.Children.Add(Empty(_loading && !_rows.ContainsKey("users") ? "Loading Users…" : T("web.admin.users.list.noMatches")));
        if (list.Count > _pageSize) stack.Children.Add(Pager(list.Count));
        if (_rows.GetValueOrDefault("users") is { Complete: false } state)
        {
            var more = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
            more.Children.Add(Text(T("admin.native.users.partial", ("count", state.Items.Count)), 13, true));
            more.Children.Add(Action(T("admin.native.users.loadMore"), () => MoreAsync("users"), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
            stack.Children.Add(new Border { Child = more, Padding = new(Gutter, 12, Gutter, 12), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.6) });
        }
        if (_rows.GetValueOrDefault("groups") is { Complete: false } groupState)
        {
            var more = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
            more.Children.Add(Action(T("admin.native.users.loadMoreGroups"), () => MoreAsync("groups"), groupState.Items.Count < AdminUsersApi.MaxRows && groupState.Seen.Count < 9));
            stack.Children.Add(new Border { Child = more, Padding = new(Gutter, 12, Gutter, 12), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.6) });
        }
        _usersResults.Content = Surface(stack);
    }

    private FrameworkElement Pager(int total)
    {
        var from = _displayPage * _pageSize + 1; var to = Math.Min((_displayPage + 1) * _pageSize, total);
        var size = Select(T("admin.native.users.rows"), new[] { 25, 50, 100 }.Select(count => (count.ToString(), T("web.admin.users.list.pageSizeOption", ("count", count)))), _pageSize.ToString(),
            value => { _pageSize = int.Parse(value); _displayPage = 0; RefreshUsers(); }, 128);
        var previous = Action(T("web.admin.users.list.previousPage"), () => { _displayPage--; RefreshUsers(); return Task.CompletedTask; }, _displayPage > 0);
        var next = Action(T("web.admin.users.list.nextPage"), () => { _displayPage++; RefreshUsers(); return Task.CompletedTask; }, (_displayPage + 1) * _pageSize < total);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(previous); buttons.Children.Add(next);
        var start = new LogsWrapPanel { Spacing = 16, RowSpacing = 8 };
        start.Children.Add(Text(T("web.admin.users.list.pageRange", ("from", from), ("to", to), ("total", total)), 14, true));
        start.Children.Add(size);
        return new Border { Child = new SplitRow(start, buttons) { MinStart = 192, Gap = 12, RowGap = 12, CenterVertically = true }, Padding = new(16, 16, 16, 16), BorderThickness = new(0, 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.6) };
    }

    private bool RowManage(JsonObject user) => AdminUsersRules.CanManage(user, _review!.Viewer.Text("id"), _review.Viewer.Flag("is_owner"));

    private void OpenHistory(JsonObject user) =>
        AdminShellPage.Open(new AdminRouteRequest(AdminRoute.PlaybackHistory, new Dictionary<string, string> { ["user_id"] = user.Text("id") }));

    private TextBlock GroupCell(JsonObject user)
    {
        var none = user.Text("role") == "admin" ? T("web.admin.users.list.adminGroupNone") : user.Text("access_group_id").Length == 0 ? T("web.admin.users.list.noGroup") : null;
        var text = none != null ? Wrapped(none, 14, true) : Wrapped(GroupName(user), 14);
        return text;
    }

    private FrameworkElement UsersTable(IReadOnlyList<JsonObject> users)
    {
        var grid = new Grid();
        (GridLength Width, double Min)[] columns =
        [
            (new(1.0, GridUnitType.Star), 76 + 2 * CellPad), (new(1.2, GridUnitType.Star), 76 + 2 * CellPad), (GridLength.Auto, 0), (new(0.9, GridUnitType.Star), 96 + 2 * CellPad), (GridLength.Auto, 0), (new(1.8, GridUnitType.Star), 80 + 2 * CellPad), (new(0.9, GridUnitType.Star), 90 + 2 * CellPad), (GridLength.Auto, 0),
        ];
        foreach (var (width, min) in columns) grid.ColumnDefinitions.Add(new() { Width = width, MinWidth = min });
        var row = 0;
        void NewRow() => grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Border Band(bool header)
        {
            var band = new Border { BorderBrush = Tint("BorderBrush", header ? 1 : 0.6), BorderThickness = new(0, 0, 0, 1), Background = new SolidColorBrush(Colors.Transparent) };
            Grid.SetRow(band, row); Grid.SetColumnSpan(band, columns.Length); grid.Children.Add(band);
            if (!header)
            {
                band.PointerEntered += (_, _) => band.Background = Tint("MutedBrush", 0.5);
                band.PointerExited += (_, _) => band.Background = new SolidColorBrush(Colors.Transparent);
            }
            return band;
        }
        void Place(UIElement element, int column, HorizontalAlignment alignment = HorizontalAlignment.Left, double vertical = 8)
        {
            var cell = new Border { Child = element, Padding = new(CellPad, vertical, CellPad, vertical), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment };
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); grid.Children.Add(cell);
        }
        NewRow(); Band(true);
        void Head(string field, string label, int column)
        {
            var active = field == _sort;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var text = Medium(label, 14, !active); text.TextWrapping = TextWrapping.NoWrap; text.LineHeight = 20;
            content.Children.Add(text);
            var chevron = AdminMonitorUi.Icon(_descending ? "chevron-down" : "chevron-up", 12, Brush(active ? "PrimaryTextBrush" : "SecondaryTextBrush")); chevron.Opacity = active ? 1 : 0;
            content.Children.Add(chevron);
            var button = AdminMonitorUi.TextRoom(AdminMonitorUi.Plain(content, label));
            AutomationProperties.SetAutomationId(button, "UsersSort_" + field);
            AutomationProperties.SetName(button, label + (active ? ", " + (_descending ? "descending" : "ascending") : ""));
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => HandleSort(field);
            Place(button, column, vertical: 12);
        }
        Head("username", T("web.admin.users.list.column.username"), 0);
        Head("email", T("web.admin.users.list.column.email"), 1);
        Head("role", T("web.admin.users.list.column.role"), 2);
        var group = Medium(T("web.admin.users.list.column.group"), 14, true); group.TextWrapping = TextWrapping.NoWrap; group.LineHeight = 20; Place(group, 3, vertical: 12);
        Head("enabled", T("web.admin.users.list.column.status"), 4);
        Head("created_at", T("web.admin.users.list.column.created"), 5);
        Head("last_active_at", T("web.admin.users.list.column.lastActive"), 6);
        var actionsHead = Medium(T("web.admin.users.list.column.actions"), 14, true); actionsHead.TextWrapping = TextWrapping.NoWrap; actionsHead.LineHeight = 20; Place(actionsHead, 7, vertical: 12);
        row++;
        foreach (var user in users)
        {
            NewRow(); Band(false);
            var name = Wrapped(user.Text("username"), 14, medium: true);
            var link = AdminMonitorUi.LinkButton(name, user.Text("username"), AdminMonitorUi.Foreground);
            AutomationProperties.SetAutomationId(link, "UsersOpen_" + user.Text("id"));
            AutomationProperties.SetName(link, user.Text("username"));
            link.HorizontalAlignment = HorizontalAlignment.Left;
            var id = user.Text("id");
            Wire(link, () => { Navigate(id, "overview"); return Task.CompletedTask; });
            Place(link, 0);
            Place(Wrapped(user.Text("email"), 14, email: true), 1);
            Place(RoleBadge(user), 2);
            var groupCell = GroupCell(user);
            if (user.Text("role") != "admin" && user.Text("access_group_id").Length > 0)
            {
                var groupId = user.Text("access_group_id");
                var groupLink = AdminMonitorUi.LinkButton(groupCell, GroupName(user), AdminMonitorUi.Foreground);
                groupLink.HorizontalAlignment = HorizontalAlignment.Left;
                Wire(groupLink, () => { AdminShellPage.Open(new AdminRouteRequest(AdminRoute.AccessGroups) { Segments = [groupId] }); return Task.CompletedTask; });
                Place(groupLink, 3);
            }
            else Place(groupCell, 3);
            Place(StatusBadge(user), 4);
            var created = Text(DateTimeShort(user.Text("created_at")), 14); ToolTipService.SetToolTip(created, FullDateTime(user.Text("created_at")));
            Place(created, 5);
            var last = Text(Relative(user.Text("last_active_at"), T("web.admin.users.list.lastActiveNever")), 14); ToolTipService.SetToolTip(last, FullDateTime(user.Text("last_active_at")));
            Place(last, 6);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var username = user.Text("username");
            buttons.Children.Add(Wire(IconAction("history", T("web.admin.users.list.historyAria", ("username", username)), 28, 12), () => { OpenHistory(user); return Task.CompletedTask; }));
            if (RowManage(user))
                buttons.Children.Add(Wire(IconAction("pencil", T("web.admin.users.list.editAria", ("username", username)), 28, 12), () => { Navigate(id, "access"); return Task.CompletedTask; }));
            Place(buttons, 7);
            row++;
        }
        return FitTable(grid);
    }

    private FrameworkElement UsersCompact(IReadOnlyList<JsonObject> users)
    {
        var list = new StackPanel();
        var first = true;
        foreach (var user in users)
        {
            var id = user.Text("id"); var username = user.Text("username");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var body = new StackPanel { Spacing = 2 };
            var top = new Grid { ColumnSpacing = 6, MinHeight = 24 };
            top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            top.Children.Add(Wrapped(username, 14, medium: true));
            var role = RoleBadge(user); Grid.SetColumn(role, 1); top.Children.Add(role);
            var status = StatusBadge(user); Grid.SetColumn(status, 2); top.Children.Add(status);
            body.Children.Add(top);
            if (user.Text("email").Length > 0) body.Children.Add(Wrapped(user.Text("email"), 14, true, email: true));
            var meta = new LogsWrapPanel { Spacing = 4, RowSpacing = 2 };
            if (user.Text("role") != "admin")
            {
                var groupText = user.Text("access_group_id").Length == 0 ? T("web.admin.users.list.noGroup") : GroupName(user);
                var groupLine = Wrapped(groupText, 12, true);
                meta.Children.Add(groupLine); meta.Children.Add(Text("·", 12, true));
            }
            var lastActive = Text(Relative(user.Text("last_active_at"), T("web.admin.users.list.lastActiveNever")), 12, true); lastActive.TextWrapping = TextWrapping.NoWrap;
            ToolTipService.SetToolTip(lastActive, FullDateTime(user.Text("last_active_at")));
            meta.Children.Add(lastActive);
            body.Children.Add(meta);
            var link = AdminMonitorUi.Plain(body, username, new Thickness(16, 10, 4, 10));
            link.CornerRadius = new(0); link.HorizontalAlignment = HorizontalAlignment.Stretch; link.VerticalAlignment = VerticalAlignment.Stretch;
            AutomationProperties.SetAutomationId(link, "UsersOpen_" + id);
            Wire(link, () => { Navigate(id, "overview"); return Task.CompletedTask; });
            grid.Children.Add(link);
            var menuItems = new List<Button> { Wire(MenuButton(T("web.admin.users.list.historyAria", ("username", username))), () => { OpenHistory(user); return Task.CompletedTask; }) };
            if (RowManage(user)) menuItems.Add(Wire(MenuButton(T("web.admin.users.list.editAria", ("username", username))), () => { Navigate(id, "access"); return Task.CompletedTask; }));
            var menu = MenuTrigger(menuItems, T("web.admin.users.list.moreActions", ("username", username)), size: 40);
            menu.Margin = new(0, 2, 4, 0); menu.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(menu, 1); grid.Children.Add(menu);
            list.Children.Add(new Border { Child = grid, BorderThickness = new(0, first ? 0 : 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.6) });
            first = false;
        }
        return list;
    }

    // ── Invitations ───────────────────────────────────────────────────────────────────────────────────────

    private FrameworkElement InvitationsTab()
    {
        var panel = new StackPanel { Spacing = 16 };
        var capability = Document("invitations-capabilities");
        if (!AdminUsersRules.Available(capability))
        {
            panel.Children.Add(Text(T("admin.native.users.invitations.unavailable", ("state", capability.Text("state", "unknown"))), 14));
            return panel;
        }
        var intro = Text(T("web.admin.settings.invitations.intro"), 14, true); intro.MaxWidth = 576;
        var invite = Wire(ActionButton(T("web.admin.settings.invitations.invite.button"), "mail-plus", "AccentButtonStyle", name: "Create invitation"), async () =>
        {
            var initial = new JsonObject { ["delivery"] = "link", ["role"] = "user", ["create_profile"] = capability!.Flag("default_profile"), ["show_tour"] = true };
            await EditAsync("Create invitation", "AdminInvitationCreateInputBody", initial, AdminUsersOperation.CreateInvitation, create: true,
                locked: (_review!.Viewer.Flag("is_owner") ? Array.Empty<string>() : new[] { "role" })
                    .Concat(capability.Flag("default_profile") && capability.Flag("profileless") ? [] : new[] { "create_profile" }).ToArray(), sensitiveResult: true);
        });
        panel.Children.Add(new SplitRow(intro, invite) { MinStart = 240, Gap = 16, RowGap = 16 });
        var items = Rows("invitations");
        if (items.Count == 0) panel.Children.Add(Text(_rows.ContainsKey("invitations") ? T("web.admin.settings.invitations.empty") : T("admin.native.users.noSnapshot"), 14, true));
        else panel.Children.Add(_wide ? InvitationTable(items, capability!) : InvitationCards(items, capability!));
        AddMoreButton(panel, "invitations", T("web.admin.settings.invitations.loadMore"));
        return panel;
    }

    private void AddMoreButton(Panel panel, string key, string label)
    {
        if (_rows.GetValueOrDefault(key) is not { Complete: false } state) return;
        var more = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
        more.Children.Add(Text(T("admin.native.users.partial", ("count", state.Items.Count)), 13, true));
        more.Children.Add(Action(label, () => MoreAsync(key), state.Items.Count < AdminUsersApi.MaxRows && state.Seen.Count < 9));
        panel.Children.Add(more);
    }

    private sealed record InvitationAction(string Key, string Label, string Icon, bool Destructive, Func<Task> Run);

    private IReadOnlyList<InvitationAction> InvitationActions(JsonObject row, JsonObject capability)
    {
        var actions = new List<InvitationAction>();
        if (row.Text("status") is "accepted" or "revoked") return actions;
        var link = row.Text("email").Length == 0;
        actions.Add(new("link", link ? T("web.admin.settings.invitations.row.replaceLink") : T("web.admin.settings.invitations.row.copyNewLink"), link ? "rotate-cw" : "link-2", false,
            () => ConfirmAsync("Replace invitation link", "Any earlier link stops working. This does not email the recipient.",
                new(AdminUsersOperation.ResendInvitation, new JsonObject { ["delivery"] = "link" }, row.Text("id")), sensitiveResult: true)));
        if (capability.Flag("email_delivery") && row.Text("email").Length > 0)
            actions.Add(new("email", T("web.admin.settings.invitations.row.emailNewLink"), "mail-plus", false,
                () => ConfirmAsync("Resend invitation email", "This replaces the earlier link and attempts email delivery.",
                    new(AdminUsersOperation.ResendInvitation, new JsonObject { ["delivery"] = "email" }, row.Text("id")), sensitiveResult: true)));
        actions.Add(new("revoke", T("web.admin.settings.invitations.row.revokeLink"), "trash-2", true,
            () => ConfirmAsync("Revoke invitation", "The invitation will no longer be usable.", new(AdminUsersOperation.RevokeInvitation, ResourceId: row.Text("id")))));
        return actions;
    }

    private static FrameworkElement StatusPill(string status)
    {
        var label = status switch
        {
            "pending" => T("web.admin.settings.invitations.status.pending"), "accepted" => T("web.admin.settings.invitations.status.accepted"),
            "expired" => T("web.admin.settings.invitations.status.expired"), "revoked" => T("web.admin.settings.invitations.status.revoked"), _ => Label(status),
        };
        return status == "pending" ? PrimaryBadge(label) : AdminLibrariesUi.Badge(label, AdminLibrariesUi.BadgeTone.Outline);
    }

    private static FrameworkElement DeliveryLabel(string delivery)
    {
        var (icon, text, amber) = delivery switch
        {
            "link" => ("link-2", T("web.admin.settings.invitations.deliveryLabel.link"), false),
            "email_sent" => ("mail", T("web.admin.settings.invitations.deliveryLabel.emailSent"), false),
            "email_unconfirmed" => ("mail", T("web.admin.settings.invitations.deliveryLabel.emailUnconfirmed"), true),
            _ => ("info", delivery.Length == 0 ? "—" : Label(delivery), false),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var tone = amber ? AdminMonitorUi.Solid(AdminMonitorUi.Amber500) : Brush("PrimaryTextBrush");
        row.Children.Add(AdminMonitorUi.Icon(icon, 14, tone));
        var block = Text(text, 14); block.TextWrapping = TextWrapping.NoWrap; if (amber) block.Foreground = tone;
        row.Children.Add(block);
        if (amber) ToolTipService.SetToolTip(row, T("web.admin.settings.invitations.deliveryLabel.emailUnconfirmedTitle"));
        return row;
    }

    private string InvitationDetails(JsonObject row)
    {
        var link = row.Text("email").Length == 0;
        var shared = row.Text("delivery") == "link";
        var parts = new List<string>();
        if (!link && row.Text("status") == "accepted" && shared) parts.Add(T("web.admin.settings.invitations.row.joinedByLink"));
        if (shared && row.Text("note").Length > 0) parts.Add(T("web.admin.settings.invitations.row.note", ("note", row.Text("note"))));
        if (row.Text("invited_by_name").Length > 0) parts.Add(T("web.admin.settings.invitations.row.invitedBy", ("name", row.Text("invited_by_name"))));
        return string.Join(" · ", parts);
    }

    private FrameworkElement InvitationTable(IReadOnlyList<JsonObject> items, JsonObject capability)
    {
        var grid = new Grid();
        (GridLength Width, double Min)[] columns = [(new(1.6, GridUnitType.Star), 160), (GridLength.Auto, 0), (new(1, GridUnitType.Star), 160), (GridLength.Auto, 0), (GridLength.Auto, 0), (new(120), 120)];
        foreach (var (width, min) in columns) grid.ColumnDefinitions.Add(new() { Width = width, MinWidth = min });
        var row = 0;
        void Place(UIElement element, int column, HorizontalAlignment alignment = HorizontalAlignment.Left, double vertical = 8)
        {
            var cell = new Border { Child = element, Padding = new(12, vertical, 12, vertical), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment };
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); grid.Children.Add(cell);
        }
        void Band(bool header)
        {
            var band = new Border { BorderBrush = Tint("BorderBrush", header ? 1 : 0.6), BorderThickness = new(0, 0, 0, 1), Background = new SolidColorBrush(Colors.Transparent) };
            Grid.SetRow(band, row); Grid.SetColumnSpan(band, columns.Length); grid.Children.Add(band);
            if (!header)
            {
                band.PointerEntered += (_, _) => band.Background = Tint("MutedBrush", 0.5);
                band.PointerExited += (_, _) => band.Background = new SolidColorBrush(Colors.Transparent);
            }
        }
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Band(true);
        string[] heads = ["recipient", "role", "status", "delivery", "created"];
        for (var column = 0; column < heads.Length; column++)
        {
            var head = Medium(T("web.admin.settings.invitations.table." + heads[column]), 14, true); head.TextWrapping = TextWrapping.NoWrap; head.LineHeight = 20;
            Place(head, column, vertical: 12);
        }
        row++;
        foreach (var item in items)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Band(false);
            var link = item.Text("email").Length == 0;
            var recipient = new StackPanel { Spacing = 0 };
            recipient.Children.Add(Truncated(link ? T("web.admin.settings.invitations.row.linkInvitation") : item.Text("email"), 14, medium: true));
            var details = InvitationDetails(item);
            if (details.Length > 0) { var detail = Truncated(details, 12, true); recipient.Children.Add(detail); }
            Place(recipient, 0);
            var roleText = Text(Label(item.Text("role")), 14, true); roleText.TextWrapping = TextWrapping.NoWrap; Place(roleText, 1);
            var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            status.Children.Add(StatusPill(item.Text("status")));
            if (item.Text("status") == "pending" && item.Text("expires_at").Length > 0)
                status.Children.Add(Text(T("web.admin.settings.invitations.row.expires", ("date", AccountDate(item.Text("expires_at")))), 12, true));
            Place(status, 2);
            Place(DeliveryLabel(item.Text("delivery")), 3);
            var created = Text(AccountDate(item.Text("created_at")), 14, true); created.TextWrapping = TextWrapping.NoWrap; Place(created, 4);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var action in InvitationActions(item, capability)) buttons.Children.Add(Wire(IconAction(action.Icon, action.Label, 32), action.Run));
            Place(buttons, 5, HorizontalAlignment.Right);
            row++;
        }
        return Surface(FitTable(grid));
    }

    private FrameworkElement InvitationCards(IReadOnlyList<JsonObject> items, JsonObject capability)
    {
        var list = new StackPanel { Spacing = 12 };
        foreach (var item in items)
        {
            var link = item.Text("email").Length == 0;
            var actions = InvitationActions(item, capability);
            var card = new StackPanel { Spacing = 12 };
            var head = new Grid { ColumnSpacing = 12 };
            head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var copy = new StackPanel { Spacing = 2 };
            var title = Medium(link ? T("web.admin.settings.invitations.row.linkInvitation") : item.Text("email"), 14); title.IsTextSelectionEnabled = true;
            copy.Children.Add(title);
            var details = InvitationDetails(item);
            if (details.Length > 0) copy.Children.Add(Text(details, 12, true));
            head.Children.Add(copy);
            var useMenu = actions.Count > 2;
            if (useMenu)
            {
                var menuItems = actions.Select(action => Wire(MenuButton(action.Label, action.Destructive), action.Run)).ToList();
                var menu = MenuTrigger(menuItems, T("web.admin.libraries.list.moreActions", ("name", link ? T("web.admin.settings.invitations.row.linkInvitation") : item.Text("email"))), size: 40);
                Grid.SetColumn(menu, 1); head.Children.Add(menu);
            }
            card.Children.Add(head);
            var badges = new LogsWrapPanel { Spacing = 6, RowSpacing = 6 };
            badges.Children.Add(AdminLibrariesUi.Badge(Label(item.Text("role")), AdminLibrariesUi.BadgeTone.Secondary));
            badges.Children.Add(StatusPill(item.Text("status")));
            if (item.Text("status") == "pending" && item.Text("expires_at").Length > 0)
                badges.Children.Add(Text(T("web.admin.settings.invitations.row.expires", ("date", AccountDate(item.Text("expires_at")))), 12, true));
            card.Children.Add(badges);
            card.Children.Add(Fields(
                (T("web.admin.settings.invitations.table.delivery"), DeliveryLabel(item.Text("delivery"))),
                (T("web.admin.settings.invitations.table.created"), Text(AccountDate(item.Text("created_at")), 14))));
            if (!useMenu && actions.Count > 0)
            {
                var buttons = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
                foreach (var action in actions)
                    buttons.Children.Add(Wire(ActionButton(action.Label, action.Icon, "OutlineButtonStyle", 40), action.Run));
                card.Children.Add(buttons);
            }
            list.Children.Add(CardBox(card));
        }
        return list;
    }

    /// <summary>One bordered card of a compact list (<c>DiagnosticsCard</c>): a surface with 16px padding.</summary>
    private static Border CardBox(UIElement child) => new()
    {
        Child = child, Padding = new(16), CornerRadius = new(AdminLibrariesUi.Radius2Xl), Background = Tint("SurfaceBrush", 0.9), BorderThickness = new(0),
    };

    /// <summary>Label and value pairs of a card (<c>CardFields</c>): the label muted at the start, the value beside it.</summary>
    private static Grid Fields(params (string Label, FrameworkElement Value)[] fields)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(96) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < fields.Length; index++)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var caption = Text(fields[index].Label, 12, true); Grid.SetRow(caption, index); caption.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(caption);
            fields[index].Value.VerticalAlignment = VerticalAlignment.Center; fields[index].Value.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(fields[index].Value, index); Grid.SetColumn(fields[index].Value, 1); grid.Children.Add(fields[index].Value);
        }
        return grid;
    }

    // ── Invite codes ──────────────────────────────────────────────────────────────────────────────────────

    private FrameworkElement CodesTab()
    {
        var panel = new StackPanel { Spacing = 16 };
        var capability = Document("codes-capabilities");
        if (!AdminUsersRules.Available(capability))
        {
            panel.Children.Add(Text(T("admin.native.users.codes.unavailable", ("state", capability.Text("state", "unknown"))), 14));
            return panel;
        }
        var note = Text(T("admin.native.users.codes.note"), 14, true); note.MaxWidth = 576;
        if (capability!.Flag("client_selected_code"))
        {
            var create = Wire(ActionButton(T("web.admin.settings.inviteCodes.create.button"), "plus", "AccentButtonStyle", name: "Create invite code"), () => EditAsync("Create invite code", "AdminInviteCodeCreateInputBody",
                new JsonObject { ["code"] = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(), ["label"] = "", ["max_uses"] = 10 }, AdminUsersOperation.CreateCode, create: true));
            panel.Children.Add(new SplitRow(note, create) { MinStart = 240, Gap = 16, RowGap = 16 });
        }
        else panel.Children.Add(note);
        var items = Rows("codes");
        if (items.Count == 0) panel.Children.Add(Text(_rows.ContainsKey("codes") ? T("web.admin.settings.inviteCodes.empty") : T("admin.native.users.noSnapshot"), 14, true));
        else panel.Children.Add(_wide ? CodeTable(items) : CodeCards(items));
        AddMoreButton(panel, "codes", T("web.admin.settings.inviteCodes.loadMore"));
        return panel;
    }

    private FrameworkElement CodeChip(string code, bool block)
    {
        var text = Mono(code, block ? 14 : 12); text.IsTextSelectionEnabled = true; text.TextWrapping = block ? TextWrapping.Wrap : TextWrapping.NoWrap;
        return new Border { Child = text, Padding = new(block ? 8 : 6, block ? 4 : 2, block ? 8 : 6, block ? 4 : 2), CornerRadius = new(4), Background = Tint("MutedBrush", 0.6), HorizontalAlignment = HorizontalAlignment.Left };
    }

    private FrameworkElement CodeStatus(JsonObject row)
    {
        var enabled = row.Flag("enabled");
        var toggle = new ToggleSwitch { IsOn = enabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, T("web.admin.settings.inviteCodes.table.status"));
        var busy = false;
        var run = Guarded(() => ConfirmAsync("Change invite code availability", "Review the code and usage before changing availability.",
            new(AdminUsersOperation.UpdateCode, new JsonObject { ["enabled"] = !enabled }, row.Text("id"))));
        toggle.Toggled += async (_, _) =>
        {
            if (busy) return;
            busy = true; toggle.IsOn = enabled; busy = false;   // the switch shows the saved state until the reviewed change is read back
            await run();
        };
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        pair.Children.Add(toggle);
        pair.Children.Add(enabled ? AdminLibrariesUi.Badge(T("web.admin.settings.inviteCodes.status.active"), AdminLibrariesUi.BadgeTone.Outline)
            : AdminLibrariesUi.Badge(T("web.admin.settings.inviteCodes.status.disabled"), AdminLibrariesUi.BadgeTone.Secondary));
        return pair;
    }

    private FrameworkElement CodeUsage(JsonObject row)
    {
        var full = row.Count("max_uses") > 0 && row.Count("use_count") >= row.Count("max_uses");
        var text = Text(T("web.admin.settings.inviteCodes.usageCount", ("used", row.Count("use_count")), ("max", row.Count("max_uses"))), 14);
        text.TextWrapping = TextWrapping.NoWrap;
        if (full) text.Foreground = Brush("ErrorBrush");
        return text;
    }

    private FrameworkElement CodeLabelText(JsonObject row) => row.Text("label").Length > 0 ? Text(row.Text("label"), 14) : Text("-", 14, true);

    private FrameworkElement CodeTable(IReadOnlyList<JsonObject> items)
    {
        var grid = new Grid();
        (GridLength Width, double Min)[] columns = [(GridLength.Auto, 0), (new(1, GridUnitType.Star), 100), (GridLength.Auto, 0), (GridLength.Auto, 0), (GridLength.Auto, 0), (new(152), 152)];
        foreach (var (width, min) in columns) grid.ColumnDefinitions.Add(new() { Width = width, MinWidth = min });
        var row = 0;
        void Place(UIElement element, int column, HorizontalAlignment alignment = HorizontalAlignment.Left, double vertical = 8)
        {
            var cell = new Border { Child = element, Padding = new(12, vertical, 12, vertical), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment };
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); grid.Children.Add(cell);
        }
        void Band(bool header)
        {
            var band = new Border { BorderBrush = Tint("BorderBrush", header ? 1 : 0.6), BorderThickness = new(0, 0, 0, 1), Background = new SolidColorBrush(Colors.Transparent) };
            Grid.SetRow(band, row); Grid.SetColumnSpan(band, columns.Length); grid.Children.Add(band);
            if (!header)
            {
                band.PointerEntered += (_, _) => band.Background = Tint("MutedBrush", 0.5);
                band.PointerExited += (_, _) => band.Background = new SolidColorBrush(Colors.Transparent);
            }
        }
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Band(true);
        string[] heads = ["code", "label", "usage", "status", "created", "actions"];
        for (var column = 0; column < heads.Length; column++)
        {
            var head = Medium(T("web.admin.settings.inviteCodes.table." + heads[column]), 14, true); head.TextWrapping = TextWrapping.NoWrap; head.LineHeight = 20;
            Place(head, column, vertical: 12);
        }
        row++;
        foreach (var item in items)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); Band(false);
            var code = item.Text("code");
            var codeCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            codeCell.Children.Add(CodeChip(code, false));
            codeCell.Children.Add(Wire(IconAction("copy", T("admin.native.users.codes.copy"), 28, 12), () => CopySensitiveAsync(code)));
            Place(codeCell, 0);
            Place(CodeLabelText(item), 1);
            Place(CodeUsage(item), 2);
            Place(CodeStatus(item), 3);
            var created = Text(AccountDate(item.Text("created_at")), 12, true); created.TextWrapping = TextWrapping.NoWrap; Place(created, 4);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            AddCodeActions(buttons.Children, item, 28, 12);
            Place(buttons, 5);
            row++;
        }
        return Surface(FitTable(grid));
    }

    private void AddCodeActions(UIElementCollection target, JsonObject item, double size, double iconSize)
    {
        var id = item.Text("id"); var code = item.Text("code");
        target.Add(Wire(IconAction("pencil", T("admin.native.users.codes.edit"), size, iconSize), () => EditAsync("Edit invite code", "AdminInviteCodeUpdateInputBody", item, AdminUsersOperation.UpdateCode, id)));
        target.Add(Wire(IconAction("circle-plus", T("web.admin.settings.inviteCodes.topUpLabel", ("code", code)), size, iconSize),
            () => EditAsync("Add uses", "AdminInviteCodeTopUpInputBody", new JsonObject { ["additional_uses"] = 1 }, AdminUsersOperation.TopUpCode, id, create: true)));
        target.Add(Wire(IconAction("trash-2", T("web.admin.settings.inviteCodes.deleteLabel", ("code", code)), size, iconSize),
            () => ConfirmAsync("Delete invite code", "Existing accounts remain. This code can no longer be used.", new(AdminUsersOperation.DeleteCode, ResourceId: id))));
    }

    private FrameworkElement CodeCards(IReadOnlyList<JsonObject> items)
    {
        var list = new StackPanel { Spacing = 12 };
        foreach (var item in items)
        {
            var code = item.Text("code"); var id = item.Text("id");
            var card = new StackPanel { Spacing = 12 };
            var head = new Grid { ColumnSpacing = 8 };
            head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            head.Children.Add(CodeChip(code, true));
            var copy = Wire(IconAction("copy", T("admin.native.users.codes.copy"), 40, 14), () => CopySensitiveAsync(code)); Grid.SetColumn(copy, 1); head.Children.Add(copy);
            card.Children.Add(head);
            card.Children.Add(Fields(
                (T("web.admin.settings.inviteCodes.table.label"), CodeLabelText(item)),
                (T("web.admin.settings.inviteCodes.table.usage"), CodeUsage(item)),
                (T("web.admin.settings.inviteCodes.table.status"), CodeStatus(item)),
                (T("web.admin.settings.inviteCodes.table.created"), Text(AccountDate(item.Text("created_at")), 14))));
            var buttons = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
            buttons.Children.Add(Wire(ActionButton(T("admin.native.users.codes.edit"), "pencil", "OutlineButtonStyle", 40), () => EditAsync("Edit invite code", "AdminInviteCodeUpdateInputBody", item, AdminUsersOperation.UpdateCode, id)));
            buttons.Children.Add(Wire(ActionButton(T("web.admin.settings.inviteCodes.topUpForm.submit"), "circle-plus", "OutlineButtonStyle", 40),
                () => EditAsync("Add uses", "AdminInviteCodeTopUpInputBody", new JsonObject { ["additional_uses"] = 1 }, AdminUsersOperation.TopUpCode, id, create: true)));
            buttons.Children.Add(Wire(ActionButton(T("web.admin.settings.inviteCodes.delete.confirm"), "trash-2", "OutlineButtonStyle", 40, T("web.admin.settings.inviteCodes.deleteLabel", ("code", code))),
                () => ConfirmAsync("Delete invite code", "Existing accounts remain. This code can no longer be used.", new(AdminUsersOperation.DeleteCode, ResourceId: id))));
            card.Children.Add(buttons);
            list.Children.Add(CardBox(card));
        }
        return list;
    }
}
