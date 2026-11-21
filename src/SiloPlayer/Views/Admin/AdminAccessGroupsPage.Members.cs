using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Official AccessGroupMembers: move selected members, add users, and the confirmation that names what changes for them.</summary>
public sealed partial class AdminAccessGroupsPage
{
    private IReadOnlyList<JsonElement> _accounts = [];
    private bool _accountsReady, _accountsFailed, _failedAdding;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal), _addRetry = new(StringComparer.Ordinal);
    private readonly List<string> _memberFailures = [];
    private readonly List<Button> _memberWrites = [];
    private string _memberQuery = "", _moveTarget = "", _memberNotice = "";
    private int _memberVisible = 10;
    private const int MemberPage = 10, PickerPage = 50;

    private async Task LoadMembersAsync(MonitorAuthorityLease? pageOwner)
    {
        var accounts = await Session.ReadAsync("members", owner => _api.ConfigurationAsync(owner, cursor => _api.ListAccountsAsync(owner, cursor)));
        if (!Session.IsCurrent(pageOwner) || _editor == null) return;
        _accounts = accounts ?? []; _accountsReady = accounts != null; _accountsFailed = accounts == null;
        RenderMembers();
    }
    private async Task ReloadGroupsAsync(MonitorAuthorityLease? pageOwner)
    {
        var groups = await Session.ReadAsync("groups", owner => _api.ConfigurationAsync(owner, cursor => _api.ListGroupsAsync(owner, cursor)));
        if (!Session.IsCurrent(pageOwner) || groups == null) return;
        _groups = groups.OrderBy(x => x.Text("name").ToLowerInvariant(), StringComparer.CurrentCulture).ThenBy(x => long.TryParse(x.Text("id"), out var id) ? id : long.MaxValue).ToArray();
    }
    private static bool Matches(JsonElement user, string needle) => needle.Length == 0 || user.Text("username").Contains(needle, StringComparison.CurrentCultureIgnoreCase) || user.Text("email").Contains(needle, StringComparison.CurrentCultureIgnoreCase);
    private Button MemberButton(string label, string id, Action command, Func<bool>? enabled = null)
    {
        var owner = Session.Owner;
        var button = Button(label, id, () => { if (Session.IsCurrent(owner) && !Session.Busy && !Session.NeedsReview) command(); });
        button.Tag = enabled; _memberWrites.Add(button); return button;
    }

    private void RenderMembers()
    {
        _members.Children.Clear(); _memberWrites.Clear();
        if (_editor == null) return;
        var groupId = _editor.Value.Text("id"); var regular = _accounts.Where(x => x.Text("role") != "admin").ToArray();
        var members = regular.Where(x => x.Text("access_group_id") == groupId).OrderBy(x => x.Text("username"), StringComparer.CurrentCultureIgnoreCase).ToArray();
        var head = Inline(8); head.Children.Add(Text(G("members.heading"), 16)); if (_accountsReady && members.Length > 0) head.Children.Add(Chip(members.Length.ToString()));
        var top = new Grid { ColumnSpacing = 12 }; top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titles = Stack(2); titles.Children.Add(head); titles.Children.Add(Text(G("members.manageHelp"), 12, true));
        var add = MemberButton(G("members.addUsers"), "GroupAddMembers", () => _ = AddMembersAsync(), () => _accountsReady); Grid.SetColumn(add, 1);
        top.Children.Add(titles); top.Children.Add(add); _members.Children.Add(top);
        if (_accountsFailed) { _members.Children.Add(Text(G("members.loadFailed"), 12)); _members.Children.Add(MemberButton(T("common.retry"), "GroupRetryMembers", () => _ = LoadMembersAsync(Session.Owner))); UpdateActions(); return; }
        if (!_accountsReady) { _members.Children.Add(Text(G("members.loading"), 12, true)); UpdateActions(); return; }
        if (_memberNotice.Length > 0) _members.Children.Add(Text(_memberNotice, 12));
        if (_memberFailures.Count > 0)
        {
            var failures = Stack(2); failures.Children.Add(Text(G(_failedAdding ? "members.addFailed" : "members.moveFailed"), 12));
            foreach (var failure in _memberFailures) { var line = Text("• " + failure, 12); line.Foreground = Brush("ErrorBrush"); failures.Children.Add(line); }
            _members.Children.Add(failures);
        }
        if (members.Length == 0) _members.Children.Add(Text(G("members.empty"), 12, true));
        var searchable = members.Length > MemberPage; var needle = searchable ? _memberQuery.Trim() : "";
        if (searchable)
        {
            var search = Input(G("members.search"), "GroupMemberSearch", _memberQuery);
            search.TextChanged += (_, _) => { _memberQuery = search.Text; _memberVisible = MemberPage; RenderMemberRows(); };
            _members.Children.Add(search);
        }
        _memberRows = Stack(2); _members.Children.Add(_memberRows); RenderMemberRows();
        var others = _groups.Where(x => x.Text("id") != groupId).ToArray();
        if (members.Length > 0 && others.Length > 0)
        {
            var target = Select(G("members.moveTarget"), others.Select(x => (x.Text("id"), x.Text("name"))), _moveTarget, value => { _moveTarget = value; UpdateActions(); }); Id(target, "GroupMoveTarget");
            target.PlaceholderText = G("members.moveTargetPlaceholder"); target.MaxWidth = 360; target.HorizontalAlignment = HorizontalAlignment.Left;
            var selectedCount = members.Count(x => _selected.Contains(x.Text("id")));
            var move = MemberButton(selectedCount > 0 ? G("members.moveSelectedCount", ("count", selectedCount.ToString())) : G("members.moveSelected"), "GroupMoveMembers",
                () => _ = MoveSelectedAsync(), () => _accountsReady && _moveTarget.Length > 0 && SelectedMembers().Length > 0);
            _members.Children.Add(target); _members.Children.Add(move);
        }
        UpdateActions();
    }
    private StackPanel? _memberRows;
    private JsonElement[] SelectedMembers()
    {
        var groupId = _editor?.Value.Text("id") ?? "";
        return _accounts.Where(x => x.Text("role") != "admin" && x.Text("access_group_id") == groupId && _selected.Contains(x.Text("id"))).ToArray();
    }
    private void RenderMemberRows()
    {
        if (_memberRows == null || _editor == null) return; _memberRows.Children.Clear();
        var groupId = _editor.Value.Text("id"); var needle = _memberQuery.Trim();
        var members = _accounts.Where(x => x.Text("role") != "admin" && x.Text("access_group_id") == groupId).OrderBy(x => x.Text("username"), StringComparer.CurrentCultureIgnoreCase).ToArray();
        var matches = (members.Length > MemberPage ? members.Where(x => Matches(x, needle)).ToArray() : members);
        if (members.Length > 0 && matches.Length == 0) _memberRows.Children.Add(Text(G("members.noMatches"), 12, true));
        var shown = matches.Take(_memberVisible).ToArray(); var shownIds = shown.Select(x => x.Text("id")).ToHashSet(StringComparer.Ordinal);
        foreach (var member in shown)
        {
            var id = member.Text("id"); var username = member.Text("username");
            var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var check = Toggle("", _selected.Contains(id), on => { if (on) _selected.Add(id); else _selected.Remove(id); RenderMemberSelection(); }); check.MinWidth = 0;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, G("members.selectMember", ("username", username))); Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(check, "GroupMember_" + id);
            var identity = Stack(0); identity.Children.Add(Text(username, 14)); if (member.Text("email").Length > 0) identity.Children.Add(Text(member.Text("email"), 12, true));
            FrameworkElement name = identity;
            if (AdminShellPage.CanOpen(AdminRouteRequest.UserDetail(id)))
            {
                var link = Id(new Button { Content = identity, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), Padding = new(8, 4, 8, 4) }, "GroupMemberOpen_" + id);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link, username); link.Click += (_, _) => AdminShellPage.Open(AdminRouteRequest.UserDetail(id)); name = link;
            }
            Grid.SetColumn(name, 1); row.Children.Add(check); row.Children.Add(name); _memberRows.Children.Add(row);
        }
        if (matches.Length > shown.Length) _memberRows.Children.Add(Button(T("web.admin.downloads.deviceCopies.showMore") is { Length: > 0 } more && more != "web.admin.downloads.deviceCopies.showMore" ? more : "Show more", "GroupMoreMembers", () => { _memberVisible += MemberPage; RenderMemberRows(); }));
        var hidden = SelectedMembers().Count(x => !shownIds.Contains(x.Text("id")));
        if (hidden > 0) _memberRows.Children.Add(Text(G("members.selectedHidden", ("count", hidden)), 12, true));
        UpdateActions();
    }
    /// <summary>A selection change only refreshes what depends on it; the rows keep their focus.</summary>
    private void RenderMemberSelection()
    {
        UpdateActions();
        if (_memberWrites.FirstOrDefault(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(x) == "GroupMoveMembers") is { } move)
        {
            var count = SelectedMembers().Length; var label = count > 0 ? G("members.moveSelectedCount", ("count", count.ToString())) : G("members.moveSelected");
            move.Content = label; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(move, label);
        }
    }

    // ---------- add users ----------
    private async Task AddMembersAsync()
    {
        if (_editor == null || !_accountsReady) return; var owner = Session.Owner; var group = _editor.Value; var groupId = group.Text("id");
        var eligible = _accounts.Where(x => x.Text("role") != "admin" && x.Text("access_group_id") != groupId).OrderBy(x => x.Text("username"), StringComparer.CurrentCultureIgnoreCase).ToArray();
        var chosen = new HashSet<string>(_addRetry.Where(id => eligible.Any(x => x.Text("id") == id)), StringComparer.Ordinal);
        var names = _groups.ToDictionary(x => x.Text("id"), x => x.Text("name"), StringComparer.Ordinal);
        var panel = Stack(8); panel.Children.Add(Text(G("addUsers.description"), 12, true));
        var search = Input(G("addUsers.search"), "GroupAddSearch", ""); search.PlaceholderText = G("addUsers.searchPlaceholder");
        var rows = Stack(2); var summary = Text("", 12, true); var visible = PickerPage;
        panel.Children.Add(search); panel.Children.Add(rows); panel.Children.Add(summary);
        void Render()
        {
            rows.Children.Clear(); var needle = search.Text.Trim(); var matches = eligible.Where(x => Matches(x, needle)).ToArray();
            if (eligible.Length == 0) rows.Children.Add(Text(G("addUsers.none"), 12, true));
            else if (matches.Length == 0) rows.Children.Add(Text(G("addUsers.noMatches"), 12, true));
            foreach (var user in matches.Take(visible))
            {
                var id = user.Text("id"); var current = user.Text("access_group_id");
                var line = new Grid { ColumnSpacing = 12 }; line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var check = Toggle(user.Text("username"), chosen.Contains(id), on => { if (on) chosen.Add(id); else chosen.Remove(id); Summarize(); });
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(check, "GroupAddUser_" + id);
                var where = Text(current.Length == 0 ? G("addUsers.noGroup") : names.TryGetValue(current, out var name) ? name : G("addUsers.unknownGroup"), 12, true); Grid.SetColumn(where, 1);
                line.Children.Add(check); line.Children.Add(where); rows.Children.Add(line);
            }
            if (matches.Length > visible) rows.Children.Add(Button("Show more", "GroupAddMore", () => { visible += PickerPage; Render(); }));
            Summarize();
        }
        void Summarize() => summary.Text = chosen.Count > 0 ? G("addUsers.addSelectedCount", ("count", chosen.Count.ToString())) : "";
        search.TextChanged += (_, _) => { visible = PickerPage; Render(); }; Render();
        var accepted = await _host.DialogAsync(G("addUsers.title", ("group", group.Text("name"))), new ScrollViewer { Content = panel, MaxHeight = 520 }, G("addUsers.addSelected"), () => chosen.Count > 0);
        if (!accepted || !Session.IsCurrent(owner)) return;
        await ConfirmMoveAsync(eligible.Where(x => chosen.Contains(x.Text("id"))).ToArray(), groupId, isAdd: true);
    }

    private async Task MoveSelectedAsync()
    {
        var users = SelectedMembers(); if (users.Length == 0 || _moveTarget.Length == 0) return;
        await ConfirmMoveAsync(users, _moveTarget, isAdd: false);
    }

    // ---------- confirmation and apply ----------
    private async Task ConfirmMoveAsync(IReadOnlyList<JsonElement> users, string targetId, bool isAdd)
    {
        var owner = Session.Owner; if (users.Count == 0 || !_accountsReady) return;
        var target = _groups.FirstOrDefault(x => x.Text("id") == targetId); if (target.ValueKind != JsonValueKind.Object) return;
        var bySource = users.GroupBy(user => user.Text("access_group_id")).ToArray();
        var unresolved = bySource.Any(group => group.Key.Length > 0 && _groups.All(x => x.Text("id") != group.Key));
        // Request limits and approval are stored per group apart from the group's policy. An account override can allow requests even when the target does not.
        var termsMatter = users.Any(user => user.Member("requests_allowed") is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.ValueKind == JsonValueKind.True : target.Flag("requests_allowed"));
        AdminRequestServerDefaults? server = null; var layers = new Dictionary<string, AdminRequestLayer>(StringComparer.Ordinal); var termsFailed = false;
        if (termsMatter)
        {
            var defaults = await Session.ReadAsync("move-defaults", lease => _api.RequestDefaultsAsync(lease));
            if (!Session.IsCurrent(owner)) return;
            if (defaults.ValueKind == JsonValueKind.Object) server = AdminRequestServerDefaults.From(defaults); else termsFailed = true;
            foreach (var id in bySource.Select(group => group.Key).Append(targetId).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal))
            {
                var limit = await Session.ReadAsync("move-limit-" + id, lease => _api.GetGroupLimitAsync(lease, id));
                if (!Session.IsCurrent(owner)) return;
                if (limit == null) termsFailed = true; else layers[id] = AdminRequestLayer.From(limit.Value);
            }
        }
        var names = _libraries.GroupBy(x => x.Text("id")).ToDictionary(x => x.Key, x => x.First().Text("name"), StringComparer.Ordinal);
        var panel = Stack(12); panel.Children.Add(Text(G("moveConfirm.description"), 12, true));
        foreach (var source in bySource)
        {
            var count = source.Count().ToString(); var key = source.Key; var sourceGroup = _groups.FirstOrDefault(x => x.Text("id") == key);
            var who = key.Length == 0 ? G("moveConfirm.fromNoGroup", ("count", count)) : sourceGroup.ValueKind == JsonValueKind.Object ? G("moveConfirm.fromGroup", ("count", count), ("group", sourceGroup.Text("name"))) : G("moveConfirm.fromUnknownGroup", ("count", count), ("id", key));
            var block = Stack(2); block.Children.Add(Text(who, 14));
            if (key.Length > 0 && sourceGroup.ValueKind != JsonValueKind.Object) block.Children.Add(Text(G("moveConfirm.sourceUnknown"), 12, true));
            else if (termsFailed) block.Children.Add(Text(G("moveConfirm.termsFailed"), 12, true));
            else
            {
                var changes = AdminAccessGroupBody.PolicyChanges(key.Length == 0 ? AdminAccessGroupBody.NoGroupPolicy : sourceGroup, target, names).ToList();
                if (termsMatter && server is { RequestsEnabled: true }) changes.AddRange(AdminRequestAccess.TermsChanges(layers.GetValueOrDefault(key), layers.GetValueOrDefault(targetId), server));
                if (changes.Count == 0) block.Children.Add(Text(G("moveConfirm.noChanges"), 12, true));
                foreach (var change in changes) block.Children.Add(Text("• " + G("moveConfirm.change", ("label", change.Label), ("from", change.From), ("to", change.To)), 12, true));
            }
            panel.Children.Add(block);
        }
        var accepted = await _host.DialogAsync(G("moveConfirm.title", ("count", users.Count), ("group", target.Text("name"))), new ScrollViewer { Content = panel, MaxHeight = 520 }, G("moveConfirm.move"), () => !unresolved && !termsFailed);
        if (!accepted || !Session.IsCurrent(owner)) return;
        await ApplyMoveAsync(users, targetId, target.Text("name"), isAdd);
    }

    /// <summary>
    /// One account at a time, each re-read first and refused if its group or request switch changed since the confirmation. A failure is reported
    /// for that account and never repeated; an unknown outcome stops the rest, which are listed as not attempted.
    /// </summary>
    private async Task ApplyMoveAsync(IReadOnlyList<JsonElement> users, string targetId, string targetName, bool isAdd)
    {
        var owner = Session.Owner; var failures = new List<string>(); var failedIds = new HashSet<string>(StringComparer.Ordinal); var moved = 0;
        foreach (var user in users)
        {
            var id = user.Text("id"); var username = user.Text("username");
            if (!Session.IsCurrent(owner)) return;
            if (Session.NeedsReview) { failedIds.Add(id); failures.Add(G("members.failure", ("username", username), ("reason", "not attempted; reload and review first"))); continue; }
            var ok = await Session.WriteAsync(async lease => { var fresh = await _api.GetMemberAsync(lease, id); await _api.MoveMemberAsync(lease, user, fresh, targetId); });
            if (!Session.IsCurrent(owner)) return;
            if (ok) { moved++; continue; }
            failedIds.Add(id);
            var reason = Session.Error is { Length: > 0 } text ? text : G(isAdd ? "members.couldNotAdd" : "members.couldNotMove");
            failures.Add(G("members.failure", ("username", username), ("reason", reason)));
        }
        // Keep the accounts that failed ready for a retry: members moving out stay selected; users being added are pre-selected when Add users reopens.
        if (isAdd) _addRetry.Clear(); else { _selected.Clear(); _addRetry.Clear(); }
        foreach (var id in failedIds) (isAdd ? _addRetry : _selected).Add(id);
        if (!isAdd && failedIds.Count == 0) _moveTarget = "";
        _failedAdding = isAdd; _memberFailures.Clear(); _memberFailures.AddRange(failures);
        _memberNotice = moved > 0 ? G(isAdd ? "members.added" : "members.moved", ("count", moved), ("group", targetName)) : "";
        if (!Session.NeedsReview)
        {
            await ReloadGroupsAsync(owner); if (!Session.IsCurrent(owner)) return;
            var accounts = await Session.ReadAsync("members", lease => _api.ConfigurationAsync(lease, cursor => _api.ListAccountsAsync(lease, cursor)));
            if (!Session.IsCurrent(owner)) return;
            if (accounts != null) _accounts = accounts; else _accountsFailed = true;
        }
        RenderMembers(); UpdateActions();
    }
}
