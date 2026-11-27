using Microsoft.UI.Xaml.Automation.Peers;
using SiloPlayer.Controls;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersFormKit;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>What the Access card editors read from the page: who is editing, and the groups, libraries and defaults that say what a row inherits.</summary>
internal sealed record AccessEditorContext(JsonObject Viewer, bool PolicyAllowed, IReadOnlyList<JsonObject> Groups, IReadOnlyList<JsonObject> Libraries, JsonObject? Defaults,
    int? ProfilesUsed, JsonObject? ExternalCapabilities = null)
{
    internal bool ViewerIsOwner => Viewer.Flag("is_owner");
}

/// <summary>
/// An Access &amp; limits card as a dialog (official <c>EditableCard</c> in edit mode): its rows over a line that counts the unsaved changes. A row that is
/// Default sends nothing; Custom sends the value; Save is off until something changed and while a Custom row has nothing valid to replace a saved value.
/// Rows the admin left alone take the newer value after "Reload and review".
/// </summary>
internal abstract class AdminUsersCardEditor
{
    private readonly List<(Border Row, Func<JsonObject, bool> Changed)> _tracked = [];
    protected readonly StackPanel Root = new() { Spacing = 0 };
    protected readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal);
    protected JsonObject Saved { get; private set; }
    protected AccessEditorContext Context { get; }
    /// <summary>The record the form started from (or last reloaded).</summary>
    internal JsonObject Current => Saved;

    internal FrameworkElement Content => Root;
    internal event Action? Changed;

    protected AdminUsersCardEditor(JsonObject saved, AccessEditorContext context) { Saved = saved; Context = context; }

    /// <summary>Builds the rows into <see cref="Root"/> from the saved account with the admin's pending changes laid over it.</summary>
    protected abstract void Render(JsonObject start);
    /// <summary>The body of the rows that changed (a row with nothing valid is left out).</summary>
    protected abstract JsonObject Collect();
    /// <summary>Rows whose Custom value cannot be stored yet and that replace a saved value: Save waits for them.</summary>
    protected virtual bool HasIncomplete() => false;
    /// <summary>Checks the text rows on Save and shows what is wrong at its field; true when the form can be sent.</summary>
    internal virtual bool Validate() => true;

    protected void Start() => Render(Saved.Copy());

    protected void Raise()
    {
        Refresh();
        Changed?.Invoke();
    }

    protected void Track(Border row, params string[] fields) => _tracked.Add((row, body => fields.Any(body.ContainsKey)));
    protected void Track(Border row, Func<JsonObject, bool> changed) => _tracked.Add((row, changed));

    internal JsonObject Body() => Collect();

    internal IReadOnlyList<string> ChangedLabels() => LabelsOf(Collect()).Distinct().ToList();

    /// <summary>The names of the rows a body changes; a form whose rows share a field names them itself.</summary>
    protected virtual IEnumerable<string> LabelsOf(JsonObject body) => body.Select(pair => Labels.GetValueOrDefault(pair.Key)).Where(label => label != null).Cast<string>();

    internal bool CanSave => ChangedLabels().Count > 0 && !HasIncomplete();

    internal string Footnote()
    {
        var rows = ChangedLabels();
        return rows.Count > 0 ? T("web.admin.users.detail.access.card.unsavedWithRows", ("count", rows.Count), ("rows", string.Join(", ", rows)))
            : T("web.admin.users.detail.access.card.unsaved", ("count", 0));
    }

    protected void Refresh()
    {
        var body = Collect();
        foreach (var (row, changed) in _tracked) SetChanged(row, changed(body));
    }

    internal void Rebase(JsonObject fresh)
    {
        var pending = Collect();
        Saved = fresh;
        var start = fresh.Copy();
        foreach (var pair in pending) start[pair.Key] = pair.Value?.DeepClone();
        Root.Children.Clear(); _tracked.Clear();
        Render(start);
        Raise();
    }

    protected static TextBlock Problem()
    {
        var block = Text("", 12); block.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); block.Visibility = Visibility.Collapsed;
        block.HorizontalAlignment = HorizontalAlignment.Right; block.TextAlignment = TextAlignment.Right;
        AutomationProperties.SetLiveSetting(block, AutomationLiveSetting.Assertive);
        return block;
    }

    protected static void Say(TextBlock block, string? message) { block.Text = message ?? ""; block.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible; }

    protected JsonObject RowsBody(IEnumerable<AccessRow> rows)
    {
        var body = new JsonObject();
        foreach (var row in rows)
        {
            if (!row.Valid) continue;
            var values = row.Values();
            for (var index = 0; index < row.Fields.Length; index++)
                if (!JsonNode.DeepEquals(Saved[row.Fields[index]], values[index])) body[row.Fields[index]] = values[index]?.DeepClone();
        }
        return body;
    }

    protected bool IncompleteRows(IEnumerable<AccessRow> rows) => rows.Any(row => !row.Valid && row.Fields.Any(field => Saved[field] != null));

}

/// <summary>Sign-in &amp; role: username, email, role, can sign in and profiles allowed (official <c>SignInCard</c>).</summary>
internal sealed class SignInEditor : AdminUsersCardEditor
{
    private TextBox _username = null!, _email = null!;
    private ComboBox _role = null!;
    private ToggleSwitch _enabled = null!;
    private NumberAccessRow _profiles = null!;
    private TextBlock _usernameProblem = null!, _emailProblem = null!;
    private string _roleValue = "user";

    internal SignInEditor(JsonObject saved, AccessEditorContext context) : base(saved, context)
    {
        Labels["username"] = T("web.admin.users.detail.access.signIn.username"); Labels["email"] = T("web.admin.users.detail.access.signIn.email");
        Labels["role"] = T("web.admin.users.detail.access.signIn.role"); Labels["access_group_id"] = Labels["role"];
        Labels["enabled"] = T("web.admin.users.detail.access.signIn.canSignIn"); Labels["max_profiles"] = T("web.admin.users.detail.access.signIn.profilesAllowed");
        Start();
    }

    private string ViewerId => Context.Viewer.Text("id");
    private bool OwnAccount => Saved.Text("id") == ViewerId;

    protected override void Render(JsonObject start)
    {
        var isOwner = Saved.Flag("is_owner");
        _roleValue = start.Text("role", "user");
        var adminRoleLocked = !Context.ViewerIsOwner && Saved.Text("role") != "admin";
        _username = new() { Width = 256, MaxLength = 255, Text = start.Text("username") };
        _email = new() { Width = 256, MaxLength = 320, Text = start.Text("email"), InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.EmailSmtpAddress) } } };
        AutomationProperties.SetAutomationId(_username, "AccessUsername"); AutomationProperties.SetName(_username, Labels["username"]);
        AutomationProperties.SetAutomationId(_email, "AccessEmail"); AutomationProperties.SetName(_email, Labels["email"]);
        _usernameProblem = Problem(); _emailProblem = Problem();
        _username.TextChanged += (_, _) => { Say(_usernameProblem, null); Raise(); };
        _email.TextChanged += (_, _) => { Say(_emailProblem, null); Raise(); };
        _role = new() { Width = 160 };
        AutomationProperties.SetAutomationId(_role, "AccessRole"); AutomationProperties.SetName(_role, Labels["role"]);
        if (isOwner) _role.Items.Add(new ComboBoxItem { Content = T("web.admin.users.detail.access.role.owner"), Tag = "owner" });
        else
        {
            _role.Items.Add(new ComboBoxItem { Content = T("web.admin.users.detail.access.role.user"), Tag = "user" });
            _role.Items.Add(new ComboBoxItem { Content = T("web.admin.users.detail.access.role.admin"), Tag = "admin", IsEnabled = !adminRoleLocked });
        }
        _role.SelectedItem = _role.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (isOwner ? "owner" : _roleValue));
        _role.IsEnabled = !isOwner && !OwnAccount;
        var roleHelp = Text("", 12, true);
        _role.SelectionChanged += (_, _) =>
        {
            if (_role.SelectedItem is not ComboBoxItem { Tag: string tag } || isOwner) return;
            _roleValue = tag; roleHelp.Text = RoleDescription(); _profiles?.SetInherited(InheritedProfiles());
            Raise();
        };
        _enabled = Switch(start.Flag("enabled"));
        AutomationProperties.SetAutomationId(_enabled, "AccessEnabled"); AutomationProperties.SetName(_enabled, Labels["enabled"]);
        _enabled.IsEnabled = !isOwner && !OwnAccount;
        _enabled.Toggled += (_, _) => Raise();

        var rows = new List<Border>();
        var usernameBox = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        usernameBox.Children.Add(_username); usernameBox.Children.Add(_usernameProblem);
        var emailBox = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        emailBox.Children.Add(_email); emailBox.Children.Add(_emailProblem);
        var usernameRow = EditRow(Labels["username"], null, usernameBox, first: true);
        var emailRow = EditRow(Labels["email"], null, emailBox);
        Track(usernameRow, "username"); Track(emailRow, "email");
        rows.Add(usernameRow); rows.Add(emailRow);
        roleHelp.Text = RoleDescription();
        var roleRow = EditRow(Labels["role"], null, _role);
        // The role help sits under the label like every row's description.
        if (roleRow.Child is SplitRow split && split.Children[0] is StackPanel startPanel) { roleHelp.Opacity = 0.8; roleHelp.LineHeight = 19.5; startPanel.Children.Add(roleHelp); }
        Track(roleRow, "role", "access_group_id");
        rows.Add(roleRow);
        if (!Saved.Flag("password_login"))
            rows.Add(EditRow(T("web.admin.users.detail.access.signIn.password"), null, Text(T("web.admin.users.detail.access.signIn.externalPassword"), 14, true)));
        var enabledHelp = isOwner ? T("web.admin.users.detail.access.signIn.ownerStaysEnabled") : OwnAccount ? T("web.admin.users.detail.access.signIn.cannotDisableSelf") : null;
        var enabledRow = EditRow(Labels["enabled"], enabledHelp, _enabled);
        Track(enabledRow, "enabled");
        rows.Add(enabledRow);
        _profiles = new NumberAccessRow(Labels["max_profiles"], Context.ProfilesUsed is { } used ? T("web.admin.users.detail.access.signIn.profilesUsed", ("used", used)) : null,
            "AccessProfiles", "max_profiles", 1, start["max_profiles"], InheritedProfiles(), value => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "", T("web.admin.users.detail.access.signIn.profilesMinHint"), Raise, Context.PolicyAllowed);
        Track(_profiles.Row, "max_profiles");
        rows.Add(_profiles.Row);
        foreach (var row in rows) Root.Children.Add(row);
    }

    private string RoleDescription()
    {
        if (OwnAccount) return T("web.admin.users.detail.access.signIn.ownRole");
        if (!Context.ViewerIsOwner && Saved.Text("role") != "admin") return T("web.admin.users.detail.access.signIn.ownerGrantsAdminRole");
        if (_roleValue == "admin" && Saved.Text("role") != "admin" && Saved["access_group_id"] != null)
        {
            var group = Context.Groups.FirstOrDefault(candidate => candidate["id"]?.ToString() == Saved["access_group_id"]?.ToString())?.Text("name");
            return T("web.admin.users.detail.access.signIn.adminLeavesGroup", ("group", string.IsNullOrEmpty(group) ? "#" + Saved["access_group_id"] : group));
        }
        return T("web.admin.users.detail.access.signIn.ownerGrantsAdmin");
    }

    /// <summary>
    /// The profile limit "Default" resolves to once the draft saves. A role change moves the account out of or into a group: an admin is never grouped, and a
    /// demoted admin joins the default group, or no group when none is the default.
    /// </summary>
    private long? InheritedProfiles()
    {
        var defaults = Context.Defaults;
        long? From(JsonObject? source) => source?["max_profiles"] is JsonValue value && UserJson.TryInteger(value, out var number) ? number : null;
        if (_roleValue == Saved.Text("role")) return UserPolicyHints_Profiles();
        if (_roleValue == "admin") return From(defaults?["admin"] as JsonObject);
        var defaultGroup = Context.Groups.FirstOrDefault(group => group.Flag("is_default"));
        return defaultGroup != null ? From(defaultGroup) : From(defaults?["ungrouped"] as JsonObject);
    }

    private long? UserPolicyHints_Profiles() => AdminUserAccountEdit.InheritHints(Saved, Saved["access_group_id"]?.ToString(), Context.Groups, Context.Defaults).MaxProfiles;

    protected override JsonObject Collect()
    {
        if (_profiles == null) return new JsonObject();
        var values = _profiles.Valid ? _profiles.Values()[0] : null;
        var limit = values is JsonValue value && UserJson.TryInteger(value, out var number) ? number : (long?)null;
        var body = AdminUserAccountEdit.SignInBody(Saved, _username.Text, _email.Text.Trim(), _role.SelectedItem is ComboBoxItem { Tag: string role } && role != "owner" ? role : Saved.Text("role"),
            _enabled.IsOn, _profiles.Valid, limit);
        // An empty username is not a change a save can send; it is reported at the field.
        if (body.ContainsKey("username") && body.Text("username").Trim().Length == 0) body.Remove("username");
        return body;
    }

    protected override bool HasIncomplete() => IncompleteRows([_profiles]);

    internal override bool Validate()
    {
        Say(_usernameProblem, null); Say(_emailProblem, null);
        if (_username.Text.Trim().Length == 0) { Say(_usernameProblem, T("web.admin.users.detail.access.signIn.enterUsername")); _username.Focus(FocusState.Programmatic); return false; }
        if (!AdminUserCreatePolicy.IsValidEmail(_email.Text)) { Say(_emailProblem, AppStrings.Get("web.auth.invalidEmail", fallback: "Enter a valid email address.")); _email.Focus(FocusState.Programmatic); return false; }
        return true;
    }
}

/// <summary>Library access: the access group, which libraries, and the marker and metadata permissions (official <c>LibraryAccessCard</c>).</summary>
internal sealed class LibraryEditor : AdminUsersCardEditor
{
    private ComboBox _group = null!;
    private string? _groupId;
    private JsonArray? _libraries;
    private JsonArray _permissions = new();
    private readonly Dictionary<string, ToggleSwitch> _permissionSwitches = new(StringComparer.Ordinal);
    private TextBlock _inheritedText = null!, _groupSummary = null!, _noLibraries = null!;
    private RadioButton _inherit = null!, _choose = null!;
    private StackPanel _chips = null!;
    private readonly Dictionary<string, CheckBox> _chipBoxes = new(StringComparer.Ordinal);
    private bool _syncing;

    private static readonly (string Permission, string Label, string Description)[] Permissions =
    [
        (AdminUserCreatePolicy.MarkerEdit, "web.admin.users.detail.access.library.markerEdit", "web.admin.users.detail.access.library.markerEditDescription"),
        (AdminUserCreatePolicy.MetadataCuration, "web.admin.users.detail.access.library.metadataCuration", "web.admin.users.detail.access.library.metadataCurationDescription"),
    ];

    internal LibraryEditor(JsonObject saved, AccessEditorContext context) : base(saved, context)
    {
        Labels["access_group_id"] = T("web.admin.users.detail.access.library.accessGroup"); Labels["library_ids"] = T("web.admin.users.detail.access.library.libraries");
        Start();
    }

    private bool Admin => Saved.Text("role") == "admin";

    protected override IEnumerable<string> LabelsOf(JsonObject body)
    {
        foreach (var pair in body)
        {
            if (pair.Key != "permissions") { if (Labels.TryGetValue(pair.Key, out var label)) yield return label; continue; }
            foreach (var (permission, label, _) in Permissions)
                if (AdminUserAccountEdit.HasPermission(pair.Value, permission) != AdminUserAccountEdit.HasPermission(Saved["permissions"], permission)) yield return T(label);
        }
    }

    protected override void Render(JsonObject start)
    {
        _groupId = start["access_group_id"]?.ToString();
        _libraries = start["library_ids"] is JsonArray ids ? (JsonArray)ids.DeepClone() : null;
        _permissions = AdminUserAccountEdit.SetPermission(start["permissions"], "", false);
        _permissionSwitches.Clear(); _chipBoxes.Clear();
        var first = true;
        if (!Admin)
        {
            _group = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(_group, "AccessGroup"); AutomationProperties.SetName(_group, Labels["access_group_id"]);
            FillGroups();
            _group.SelectionChanged += (_, _) =>
            {
                if (_syncing || _group.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
                _groupId = tag == "none" ? null : tag;
                DescribeGroup(); RefreshPermissions(); DescribeInherited(); Raise();
            };
            _groupSummary = Text("", 12, true);
            var box = new StackPanel { Spacing = 8, Padding = new(0, 12, 0, 12) };
            box.Children.Add(Text(Labels["access_group_id"], 14, true)); box.Children.Add(_group); box.Children.Add(_groupSummary);
            var groupRow = new Border { Child = box };
            Track(groupRow, "access_group_id");
            Root.Children.Add(groupRow);
            DescribeGroup();
            first = false;
        }

        // Libraries
        var libraries = new StackPanel { Spacing = 8, Padding = new(0, 12, 0, 12) };
        libraries.Children.Add(Text(Labels["library_ids"], 14, true));
        _inheritedText = Text("", 12, true);
        _inherit = new RadioButton { GroupName = "libs-" + Guid.NewGuid().ToString("N"), MinWidth = 0, IsChecked = _libraries == null };
        AutomationProperties.SetAutomationId(_inherit, "AccessLibrariesInherit");
        var inheritText = new StackPanel { Spacing = 0 };
        var inheritTitle = Text("", 14); inheritTitle.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        inheritText.Children.Add(inheritTitle); inheritText.Children.Add(_inheritedText);
        _inherit.Content = inheritText;
        _choose = new RadioButton { GroupName = _inherit.GroupName, MinWidth = 0, IsChecked = _libraries != null };
        AutomationProperties.SetAutomationId(_choose, "AccessLibrariesChoose");
        var chooseTitle = Text(T("web.admin.users.detail.access.library.chooseForAccount"), 14); chooseTitle.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        _chips = new StackPanel { Spacing = 6, Visibility = _libraries == null ? Visibility.Collapsed : Visibility.Visible, Margin = new(28, 4, 0, 0) };
        foreach (var library in Context.Libraries)
        {
            var id = library["id"]?.ToString() ?? "";
            var chip = new CheckBox { Content = library.Text("name"), MinWidth = 0, MinHeight = 32, IsChecked = _libraries?.Any(node => node?.ToString() == id) == true };
            AutomationProperties.SetAutomationId(chip, "AccessLibrary_" + id);
            _chipBoxes[id] = chip;
            chip.Click += (_, _) => { ReadChips(); Raise(); };
            _chips.Children.Add(chip);
        }
        _noLibraries = Text(T("web.admin.users.detail.access.library.noLibraryWarning"), 12, true); _noLibraries.Margin = new(28, 0, 0, 0);
        _noLibraries.Visibility = _libraries is { Count: 0 } ? Visibility.Visible : Visibility.Collapsed;
        _inherit.Checked += (_, _) =>
        {
            if (_syncing) return;
            _libraries = null; _chips.Visibility = Visibility.Collapsed; _noLibraries.Visibility = Visibility.Collapsed; Raise();
        };
        _choose.Checked += (_, _) =>
        {
            if (_syncing) return;
            // Start from what the account can see now.
            _libraries = Saved["effective_policy"]?["library_ids"] is JsonArray current ? (JsonArray)current.DeepClone()
                : new JsonArray(Context.Libraries.Select(row => (JsonNode?)JsonValue.Create(row["id"]?.ToString() ?? "")).ToArray());
            _syncing = true;
            foreach (var pair in _chipBoxes) pair.Value.IsChecked = _libraries.Any(node => node?.ToString() == pair.Key);
            _syncing = false;
            _chips.Visibility = Visibility.Visible; _noLibraries.Visibility = _libraries.Count == 0 ? Visibility.Visible : Visibility.Collapsed; Raise();
        };
        libraries.Children.Add(Box(_inherit, _libraries == null));
        var chooseStack = new StackPanel(); chooseStack.Children.Add(Wrap(_choose, chooseTitle)); chooseStack.Children.Add(_chips); chooseStack.Children.Add(_noLibraries);
        libraries.Children.Add(chooseStack);
        var libraryRow = new Border { Child = libraries, BorderThickness = new(0, first ? 0 : 1, 0, 0), BorderBrush = Tint("BorderBrush", 0.5) };
        Track(libraryRow, "library_ids");
        Root.Children.Add(libraryRow);
        inheritTitle.Tag = null;
        _inheritTitle = inheritTitle;
        DescribeInherited();

        // Permissions
        foreach (var (permission, label, description) in Permissions)
        {
            var toggle = Switch(false);
            AutomationProperties.SetAutomationId(toggle, "Access_" + permission); AutomationProperties.SetName(toggle, T(label));
            _permissionSwitches[permission] = toggle;
            var help = Text(T(description), 12, true); help.Opacity = 0.8;
            var lockNote = Text("", 12, true); lockNote.Visibility = Visibility.Collapsed;
            var remove = ActionButton(T("web.admin.users.detail.access.library.removeFromAccount"), style: "GhostButtonStyle", height: 28); remove.Visibility = Visibility.Collapsed;
            var end = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            end.Children.Add(lockNote); end.Children.Add(remove); end.Children.Add(toggle);
            var row = EditRow(T(label), T(description), end);
            _permissionRows[permission] = (row, toggle, lockNote, remove, help);
            toggle.Toggled += (_, _) =>
            {
                if (_syncing) return;
                _permissions = AdminUserAccountEdit.SetPermission(_permissions, permission, toggle.IsOn); Raise();
            };
            remove.Click += (_, _) => { _permissions = AdminUserAccountEdit.SetPermission(_permissions, permission, false); RefreshPermissions(); Raise(); };
            Track(row, body => body.ContainsKey("permissions") && AdminUserAccountEdit.HasPermission(body["permissions"], permission) != AdminUserAccountEdit.HasPermission(Saved["permissions"], permission));
            Root.Children.Add(row);
        }
        RefreshPermissions();
    }

    private TextBlock _inheritTitle = null!;
    private readonly Dictionary<string, (Border Row, ToggleSwitch Toggle, TextBlock Note, Button Remove, TextBlock Help)> _permissionRows = new(StringComparer.Ordinal);

    private Border Box(RadioButton radio, bool on) => new() { Child = radio, Padding = new(12, 8, 12, 8), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.7) };
    private static FrameworkElement Wrap(RadioButton radio, TextBlock title) { radio.Content = title; return new Border { Child = radio, Padding = new(12, 8, 12, 8), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.7) }; }

    private void FillGroups()
    {
        _syncing = true;
        _group.Items.Clear();
        _group.Items.Add(new ComboBoxItem { Content = T("web.admin.users.detail.access.library.noGroup"), Tag = "none" });
        if (_groupId != null && !Context.Groups.Any(group => group["id"]?.ToString() == _groupId))
            _group.Items.Add(new ComboBoxItem { Content = "#" + _groupId, Tag = _groupId });
        foreach (var group in Context.Groups) _group.Items.Add(new ComboBoxItem { Content = group.Text("name"), Tag = group["id"]?.ToString() ?? "" });
        _group.SelectedItem = _group.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (_groupId ?? "none"));
        _syncing = false;
    }

    private void DescribeGroup()
    {
        var group = _groupId == null ? null : Context.Groups.FirstOrDefault(candidate => candidate["id"]?.ToString() == _groupId);
        if (group == null) { _groupSummary.Text = ""; _groupSummary.Visibility = Visibility.Collapsed; return; }
        var libraries = LibraryText(group["library_ids"] as JsonArray, group.ContainsKey("library_ids"));
        var quality = AdminUserAccountEdit.QualityPreset(group.Text("max_playback_quality")) switch { "standard" => "1080p", "4k" => "4K", _ => T("web.admin.users.detail.access.value.anyQuality") };
        var streams = group.Count("max_streams");
        var parts = new[] { libraries, quality, T("web.admin.users.detail.access.library.summaryStreams", ("count", streams), ("count_text", streams)) }.Where(part => part.Length > 0);
        _groupSummary.Text = T("web.admin.users.detail.access.library.groupSummary", ("group", group.Text("name")), ("summary", string.Join(" · ", parts)));
        _groupSummary.Visibility = Visibility.Visible;
    }

    private string LibraryText(JsonArray? ids, bool known)
    {
        if (!known) return "";
        if (ids == null) return T("web.admin.users.detail.access.value.allLibraries");
        if (ids.Count == 0) return T("web.admin.users.detail.access.value.noLibraries");
        return string.Join(", ", ids.Select(node => Context.Libraries.FirstOrDefault(row => row["id"]?.ToString() == node?.ToString())?.Text("name") is { Length: > 0 } name ? name : "#" + node));
    }

    private void DescribeInherited()
    {
        var hints = AdminUserAccountEdit.InheritHints(Saved, _groupId, Context.Groups, Context.Defaults);
        var effectiveGroup = AdminUserCreatePolicy.EffectiveAccessGroupId(Saved.Text("role"), _groupId);
        _inheritTitle.Text = effectiveGroup == null ? T("web.admin.users.detail.access.library.useServerDefault") : T("web.admin.users.detail.access.library.useGroupLibraries");
        var text = hints.LibraryIdsKnown ? LibraryText(hints.LibraryIds == null ? null : new JsonArray(hints.LibraryIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()), true) : "";
        _inheritedText.Text = text; _inheritedText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ReadChips()
    {
        if (_syncing) return;
        _libraries = new JsonArray(Context.Libraries.Select(row => row["id"]?.ToString() ?? "").Where(id => _chipBoxes[id].IsChecked == true).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        _noLibraries.Visibility = _libraries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshPermissions()
    {
        _syncing = true;
        var draftAccount = Saved.Copy(); draftAccount["access_group_id"] = _groupId;
        foreach (var (permission, _, _) in Permissions)
        {
            var (row, toggle, note, remove, _) = _permissionRows[permission];
            var (locked, groupName) = AdminUserAccountEdit.PermissionLock(draftAccount, AdminUserCreatePolicy.EffectiveAccessGroupId(Saved.Text("role"), _groupId), Context.Groups, permission);
            var assigned = AdminUserAccountEdit.HasPermission(_permissions, permission);
            toggle.IsOn = assigned && !locked; toggle.IsEnabled = !locked;
            note.Text = locked ? T("web.admin.users.detail.access.library.groupDisallows", ("group", groupName ?? "")) : "";
            note.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            remove.Visibility = locked && assigned ? Visibility.Visible : Visibility.Collapsed;
        }
        _syncing = false;
    }

    protected override JsonObject Collect()
    {
        if (_inherit == null) return new JsonObject();
        return AdminUserAccountEdit.LibraryBody(Saved, Admin ? Saved["access_group_id"]?.ToString() : _groupId, _libraries, _permissions);
    }
}

/// <summary>A card of Default / Custom rows: Playback &amp; streaming, Downloads and the requests switch.</summary>
internal sealed class PolicyRowsEditor : AdminUsersCardEditor
{
    private readonly Func<PolicyRowsEditor, AccessEditorContext, JsonObject, IReadOnlyList<AccessRow>> _build;
    private readonly string? _description;
    private IReadOnlyList<AccessRow> _rows = [];

    internal PolicyRowsEditor(JsonObject saved, AccessEditorContext context, string? description, IReadOnlyDictionary<string, string> labels,
        Func<PolicyRowsEditor, AccessEditorContext, JsonObject, IReadOnlyList<AccessRow>> build) : base(saved, context)
    {
        _build = build; _description = description;
        foreach (var pair in labels) Labels[pair.Key] = pair.Value;
        Start();
    }

    protected override void Render(JsonObject start)
    {
        if (_description != null) { var note = Text(_description, 13, true); note.Margin = new(0, 0, 0, 8); Root.Children.Add(note); }
        _rows = _build(this, Context, start);
        var first = true;
        foreach (var row in _rows)
        {
            if (first) row.Row.BorderThickness = new(0);
            first = false;
            var fields = row.Fields;
            Track(row.Row, fields);
            Root.Children.Add(row.Row);
        }
    }

    /// <summary>The hook the row builders use to tell the editor a row changed.</summary>
    internal void Touch() => Raise();
    protected override JsonObject Collect() => _rows.Count == 0 ? new JsonObject() : RowsBody(_rows);
    protected override bool HasIncomplete() => IncompleteRows(_rows);
    internal override bool Validate() => true;
    internal IReadOnlyList<AccessRow> Rows => _rows;
}

/// <summary>Break-glass: the switch that keeps password sign-in for an admin while the server turns it off (official <c>AdminUserSignIn</c> break-glass panel).</summary>
internal sealed class BreakGlassEditor : AdminUsersCardEditor
{
    private ToggleSwitch _switch = null!;

    internal BreakGlassEditor(JsonObject saved, AccessEditorContext context) : base(saved, context)
    {
        Labels["break_glass"] = T("web.admin.common.adminUserSignIn.breakGlass.label");
        Start();
    }

    protected override void Render(JsonObject start)
    {
        _switch = Switch(start.Flag("break_glass"));
        AutomationProperties.SetAutomationId(_switch, "BreakGlassSwitch"); AutomationProperties.SetName(_switch, Labels["break_glass"]);
        _switch.IsEnabled = Context.ViewerIsOwner;
        _switch.Toggled += (_, _) => Raise();
        var row = EditRow(Labels["break_glass"], T("web.admin.common.adminUserSignIn.breakGlass.hint"), _switch, first: true);
        Track(row, "break_glass");
        Root.Children.Add(row);
        var notes = new StackPanel { Spacing = 6, Margin = new(0, 4, 0, 0) };
        if (Saved.Flag("is_owner")) notes.Children.Add(Text(T("web.admin.common.adminUserSignIn.breakGlass.ownerDefault"), 12, true));
        if (!Context.ViewerIsOwner) notes.Children.Add(Text(T("web.admin.common.adminUserSignIn.breakGlass.ownerOnly"), 12, true));
        var noPassword = Text(T("web.admin.common.adminUserSignIn.breakGlass.noPassword"), 12); noPassword.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber500);
        noPassword.Visibility = start.Flag("break_glass") && !Saved.Flag("password_login") ? Visibility.Visible : Visibility.Collapsed;
        _switch.Toggled += (_, _) => noPassword.Visibility = _switch.IsOn && !Saved.Flag("password_login") ? Visibility.Visible : Visibility.Collapsed;
        notes.Children.Add(noPassword);
        Root.Children.Add(notes);
    }

    protected override JsonObject Collect()
    {
        var body = new JsonObject();
        if (_switch != null && _switch.IsOn != Saved.Flag("break_glass")) body["break_glass"] = _switch.IsOn;
        return body;
    }
}
