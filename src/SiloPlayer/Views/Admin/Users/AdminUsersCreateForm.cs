using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;
using SiloPlayer.Helpers;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>What the Create user form needs from the page: the viewer, what the server supports and the groups and libraries it can pick from.</summary>
internal sealed record AdminUsersCreateContext(bool ViewerIsOwner, bool DefaultProfileAvailable, bool GroupsSupported, bool ProfileLimitsSupported,
    IReadOnlyList<JsonObject> Groups, IReadOnlyList<JsonObject> Libraries, PasswordPolicy PasswordPolicy);

/// <summary>
/// The Create user form, after the official <c>CreateUserForm.tsx</c> and <c>UserPolicyFields.tsx</c>: a default-profile checkbox over Account,
/// Access and Limits tabs. Account has username, email, password with its rules and a require-change switch, and the role; Access has the group,
/// the marker and metadata permissions, library access and the download and request gates; Limits has the stream and bitrate ceilings, the
/// transcoding gates, the quality ceiling and the profile limit. A policy field that is not overridden inherits from the group (or the admin or
/// server default) and is not sent.
/// </summary>
internal sealed class AdminUsersCreateForm
{
    private const string Inherit = "inherit";
    private readonly AdminUsersCreateContext _context;
    private readonly UserPolicyState _policy = new();
    private readonly List<string> _permissions = [.. AdminUserCreatePolicy.DefaultPermissions];
    private readonly Dictionary<string, FrameworkElement> _tabs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _tabButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> _tabUnderline = new(StringComparer.Ordinal);
    private readonly List<Action> _hintRefreshers = [];
    private readonly Dictionary<string, string> _invalidDrafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _revealInvalid = new(StringComparer.Ordinal);
    private readonly CheckBox _defaultProfile = new() { IsChecked = true, MinHeight = 32 };
    private readonly TextBox _username = new() { MaxLength = 255 };
    private readonly TextBox _email = new() { MaxLength = 320, InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.EmailSmtpAddress) } } };
    private readonly PasswordBox _password = new();
    private readonly ToggleSwitch _requireChange = Switch();
    private readonly ComboBox _role = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _group = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _groupHelp = Text("", 12, true);
    private NewPasswordField? _passwordField;
    private string _tab = "account";
    private string? _pickedGroup;
    private bool _groupPicked;
    private bool _syncing;

    internal FrameworkElement Content { get; }
    internal bool DefaultProfileChecked => _defaultProfile.IsChecked == true;

    /// <summary>A default profile that the server cannot create blocks saving until the box is cleared (official: "unavailable; uncheck to create only the account").</summary>
    internal bool SaveBlocked => DefaultProfileChecked && !_context.DefaultProfileAvailable;
    internal string PasswordValue => _password.Password;
    internal event Action? Changed;

    private string Role => (_role.SelectedItem as ComboBoxItem)?.Tag as string ?? "user";
    private string? SelectedGroupId => _groupPicked ? _pickedGroup : AdminUserCreatePolicy.DefaultGroupId(_context.Groups);
    private string? EffectiveGroupId => AdminUserCreatePolicy.EffectiveAccessGroupId(Role, SelectedGroupId);

    private static string S(string key, string fallback, params (string Name, object? Value)[] args) =>
        AppStrings.Get(key, args.Length == 0 ? null : args.ToDictionary(pair => pair.Name, pair => pair.Value), fallback);

    private static ToggleSwitch Switch(bool on = false) => new() { IsOn = on, OnContent = "", OffContent = "", MinWidth = 0, Width = 44, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

    internal AdminUsersCreateForm(AdminUsersCreateContext context)
    {
        _context = context;
        var root = new StackPanel { Spacing = 12 };
        _defaultProfile.Content = Text(context.DefaultProfileAvailable ? T("web.admin.users.list.create.defaultProfile") : T("web.admin.users.list.create.defaultProfileUnavailable"), 14);
        _defaultProfile.Checked += (_, _) => Changed?.Invoke(); _defaultProfile.Unchecked += (_, _) => Changed?.Invoke();
        AutomationProperties.SetAutomationId(_defaultProfile, "CreateUserDefaultProfile");
        root.Children.Add(_defaultProfile);

        // Tab strip
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (key, label) in new[] { ("account", "web.admin.users.list.create.tab.account"), ("access", "web.admin.users.list.create.tab.access"), ("limits", "web.admin.users.list.create.tab.limits") })
        {
            var line = new Border { Height = 2, Margin = new(8, 0, 8, 0), Background = Brush("AccentBrush"), Visibility = Visibility.Collapsed };
            var button = new Button { Content = Text(T(label), 14), Padding = new(8, 6, 8, 6), MinHeight = 36, CornerRadius = new(8), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            AutomationProperties.SetAutomationId(button, "CreateUserTab_" + key);
            AutomationProperties.SetName(button, T(label));
            var tab = key;
            button.Click += (_, _) => ShowTab(tab);
            var cell = new StackPanel { Spacing = 0 };
            cell.Children.Add(button); cell.Children.Add(line);
            strip.Children.Add(cell);
            _tabButtons[key] = button; _tabUnderline[key] = line;
        }
        var stripHost = new Border { Child = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Auto, VerticalScrollMode = ScrollMode.Disabled },
            BorderBrush = Tint("BorderBrush", 1), BorderThickness = new(0, 0, 0, 1) };
        root.Children.Add(stripHost);

        _tabs["account"] = AccountTab();
        _tabs["access"] = AccessTab();
        _tabs["limits"] = LimitsTab();
        foreach (var tab in _tabs.Values) root.Children.Add(tab);
        ShowTab("account");
        RefreshHints();
        Content = root;
    }

    internal void ShowTab(string tab)
    {
        _tab = tab;
        foreach (var (key, element) in _tabs) element.Visibility = key == tab ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (key, line) in _tabUnderline) line.Visibility = key == tab ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (key, button) in _tabButtons) AutomationProperties.SetItemStatus(button, key == tab ? "selected" : "");
    }

    // ── Account ─────────────────────────────────────────────────────────────────────────────────

    private static StackPanel Field(string label, UIElement control, params UIElement[] extra)
    {
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(Text(label, 14));
        field.Children.Add(control);
        foreach (var element in extra) field.Children.Add(element);
        return field;
    }

    private FrameworkElement AccountTab()
    {
        var panel = new StackPanel { Spacing = 16 };
        AutomationProperties.SetAutomationId(_username, "CreateUserUsername"); AutomationProperties.SetName(_username, T("web.admin.users.list.create.username"));
        AutomationProperties.SetAutomationId(_email, "CreateUserEmail"); AutomationProperties.SetName(_email, T("web.admin.users.list.create.email"));
        AutomationProperties.SetAutomationId(_password, "CreateUserPassword"); AutomationProperties.SetName(_password, T("web.admin.users.list.create.password"));
        AutomationProperties.SetAutomationId(_role, "CreateUserRole"); AutomationProperties.SetName(_role, T("web.admin.users.list.create.role"));
        AutomationProperties.SetAutomationId(_requireChange, "CreateUserRequireChange");
        AutomationProperties.SetName(_requireChange, T("web.admin.users.list.create.requirePasswordChange"));
        panel.Children.Add(Field(T("web.admin.users.list.create.username"), _username));
        panel.Children.Add(Field(T("web.admin.users.list.create.email"), _email));

        var passwordField = new StackPanel { Spacing = 6 };
        passwordField.Children.Add(Text(T("web.admin.users.list.create.password"), 14));
        passwordField.Children.Add(_password);
        _passwordField = new NewPasswordField(passwordField, _password);
        _passwordField.ShowPolicy(_context.PasswordPolicy);
        var requireRow = new Grid { ColumnSpacing = 8 };
        requireRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); requireRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        requireRow.Children.Add(_requireChange);
        var requireLabel = Text(T("web.admin.users.list.create.requirePasswordChange"), 12); requireLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(requireLabel, 1); requireRow.Children.Add(requireLabel);
        _requireChange.IsEnabled = false;
        _password.PasswordChanged += (_, _) =>
        {
            _requireChange.IsEnabled = _password.Password.Length > 0;
            if (_password.Password.Length == 0) _requireChange.IsOn = false;
            Changed?.Invoke();
        };
        passwordField.Children.Add(requireRow);
        panel.Children.Add(passwordField);

        _role.Items.Add(new ComboBoxItem { Content = T("web.admin.users.list.create.role.user"), Tag = "user" });
        _role.Items.Add(new ComboBoxItem { Content = T("web.admin.users.list.create.role.admin"), Tag = "admin", IsEnabled = _context.ViewerIsOwner });
        _role.SelectedIndex = 0;
        _role.SelectionChanged += (_, _) => { if (!_syncing) { SyncGroup(); RefreshHints(); Changed?.Invoke(); } };
        UIElement[] roleExtra = _context.ViewerIsOwner ? [] : [Text(T("web.admin.users.list.create.adminOwnerOnly"), 12, true)];
        panel.Children.Add(Field(T("web.admin.users.list.create.role"), _role, roleExtra));
        _username.TextChanged += (_, _) => Changed?.Invoke();
        _email.TextChanged += (_, _) => Changed?.Invoke();
        return panel;
    }

    // ── Access ──────────────────────────────────────────────────────────────────────────────────

    private FrameworkElement AccessTab()
    {
        var panel = new StackPanel { Spacing = 16 };
        if (_context.GroupsSupported)
        {
            AutomationProperties.SetAutomationId(_group, "CreateUserGroup"); AutomationProperties.SetName(_group, T("web.admin.users.list.create.group"));
            _group.SelectionChanged += (_, _) =>
            {
                if (_syncing || _group.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
                _pickedGroup = tag == "none" ? null : tag; _groupPicked = true;
                RefreshHints(); Changed?.Invoke();
            };
            SyncGroup();
            panel.Children.Add(Field(T("web.admin.users.list.create.group"), _group, _groupHelp));
        }
        panel.Children.Add(PermissionCard("CreateUserMarkerEdit", T("web.admin.users.list.create.markerEdit"), T("web.admin.users.list.create.markerEditHelp"), AdminUserCreatePolicy.MarkerEdit));
        panel.Children.Add(PermissionCard("CreateUserMetadataCuration", T("web.admin.users.list.create.metadataCuration"), T("web.admin.users.list.create.metadataCurationHelp"), AdminUserCreatePolicy.MetadataCuration));
        panel.Children.Add(LibraryAccess());
        var gates = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        gates.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); gates.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var downloads = BooleanRow("CreateUserDownloads", S("web.profiles.userPolicy.downloads", "Downloads"), null, () => _policy.DownloadAllowed, value => _policy.DownloadAllowed = value, hints => hints?.DownloadAllowed);
        var downloadTranscodes = BooleanRow("CreateUserDownloadTranscodes", S("web.profiles.userPolicy.downloadTranscodes", "Download Transcodes"), null, () => _policy.DownloadTranscodeAllowed, value => _policy.DownloadTranscodeAllowed = value, hints => hints?.DownloadTranscodeAllowed);
        Grid.SetColumn(downloadTranscodes, 1);
        gates.Children.Add(downloads); gates.Children.Add(downloadTranscodes);
        AdaptiveColumns(gates);
        panel.Children.Add(gates);
        panel.Children.Add(BooleanRow("CreateUserRequests", S("web.profiles.userPolicy.mediaRequests", "Media Requests"), S("web.profiles.userPolicy.mediaRequestsHint", "Request new movies and series when requests are enabled."),
            () => _policy.RequestsAllowed, value => _policy.RequestsAllowed = value, hints => hints?.RequestsAllowed));
        return panel;
    }

    /// <summary>Two columns from 480 px, one below: the official <c>sm:grid-cols-2</c>.</summary>
    private static void AdaptiveColumns(Grid grid)
    {
        var applied = -1;
        void Apply()
        {
            var columns = grid.ActualWidth > 0 && grid.ActualWidth < 520 ? 1 : 2;
            if (columns == applied) return;
            applied = columns;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var row = 0; row < (grid.Children.Count + columns - 1) / columns; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var index = 0; index < grid.Children.Count; index++)
            {
                var child = (FrameworkElement)grid.Children[index];
                Grid.SetRow(child, index / columns); Grid.SetColumn(child, index % columns);
            }
        }
        grid.SizeChanged += (_, _) => Apply();
        Apply();
    }

    private void SyncGroup()
    {
        _syncing = true;
        try
        {
            var selected = SelectedGroupId;
            _group.Items.Clear();
            var admin = Role == "admin";
            // Creating an account can't choose "no group" when the server has a default group (it places the account there); offer it only without one.
            var offerNone = AdminUserCreatePolicy.DefaultGroupId(_context.Groups) is null;
            if (offerNone) _group.Items.Add(new ComboBoxItem { Content = T("web.admin.users.list.noGroup"), Tag = "none" });
            if (selected != null && !_context.Groups.Any(group => group["id"]?.ToString() == selected))
                _group.Items.Add(new ComboBoxItem { Content = T("web.admin.users.list.create.missingGroup", ("id", selected)), Tag = selected });
            foreach (var group in _context.Groups)
                _group.Items.Add(new ComboBoxItem { Content = group["name"]?.ToString() ?? "", Tag = group["id"]?.ToString() ?? "" });
            _group.SelectedItem = _group.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == (admin || selected == null ? "none" : selected))
                ?? _group.Items.OfType<ComboBoxItem>().FirstOrDefault();
            _group.IsEnabled = !admin;
            _groupHelp.Text = admin ? T("web.admin.users.list.create.adminNoGroups") : "";
            _groupHelp.Visibility = admin ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncing = false; }
    }

    private Border PermissionCard(string id, string label, string help, string permission)
    {
        var toggle = Switch(_permissions.Contains(permission));
        AutomationProperties.SetAutomationId(toggle, id); AutomationProperties.SetName(toggle, label);
        toggle.Toggled += (_, _) =>
        {
            _permissions.RemoveAll(entry => entry == permission);
            if (toggle.IsOn) _permissions.Add(permission);
        };
        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(Text(label, 14)); texts.Children.Add(Text(help, 12, true));
        return Card(texts, toggle);
    }

    /// <summary>A bordered row: text at the start, a control at the end. A drop-down moves under the text when the row is too narrow for both.</summary>
    private static Border Card(UIElement left, UIElement right, bool stackWhenNarrow = false)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var control = (FrameworkElement)right;
        grid.Children.Add(left); grid.Children.Add(control);
        Grid.SetColumn(control, 1);
        if (stackWhenNarrow)
        {
            var stacked = false;
            grid.SizeChanged += (_, _) =>
            {
                var narrow = grid.ActualWidth > 0 && grid.ActualWidth < 360;
                if (narrow == stacked) return;
                stacked = narrow;
                grid.RowDefinitions.Clear();
                if (narrow)
                {
                    grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    Grid.SetColumnSpan((FrameworkElement)left, 2); Grid.SetRow(control, 1); Grid.SetColumn(control, 0); Grid.SetColumnSpan(control, 2);
                    control.HorizontalAlignment = HorizontalAlignment.Stretch; control.MaxWidth = double.PositiveInfinity;
                }
                else
                {
                    Grid.SetColumnSpan((FrameworkElement)left, 1); Grid.SetRow(control, 0); Grid.SetColumn(control, 1); Grid.SetColumnSpan(control, 1);
                    control.HorizontalAlignment = HorizontalAlignment.Right; control.MaxWidth = 260;
                }
            };
        }
        return new Border { Child = grid, Padding = new(12, 8, 12, 8), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") };
    }

    private static string Count(int count) => count.ToString("N0", AppStrings.FormatCulture);

    private FrameworkElement LibraryAccess()
    {
        var panel = new StackPanel { Spacing = 8 };
        var allLabelText = Text("", 12, true);
        var all = Switch(true);
        AutomationProperties.SetAutomationId(all, "CreateUserLibrariesInherit");
        var hint = Text("", 12, true);
        var list = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Text(S("web.profiles.libraryAccess.label", "Library Access"), 14));
        Grid.SetColumn(allLabelText, 1); allLabelText.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(allLabelText);
        Grid.SetColumn(all, 2); header.Children.Add(all);
        panel.Children.Add(header); panel.Children.Add(hint); panel.Children.Add(list);

        var libraries = _context.Libraries.Select(row => (Id: row["id"]?.ToString() ?? "", Name: row["name"]?.ToString() ?? "", Type: row["type"]?.ToString() ?? "",
            Enabled: row["enabled"] is not JsonValue flag || !flag.TryGetValue<bool>(out var on) || on)).ToArray();
        var toggles = new Dictionary<string, ToggleSwitch>(StringComparer.Ordinal);
        foreach (var library in libraries)
        {
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(Text(library.Name, 14));
            if (!library.Enabled) name.Children.Add(Text(S("web.profiles.libraryAccess.disabled", "Disabled"), 11, true));
            if (library.Type.Length > 0) name.Children.Add(Text(Label(library.Type), 12, true));
            var toggle = Switch(false);
            AutomationProperties.SetName(toggle, library.Name);
            toggle.Toggled += (_, _) => { if (!_syncing) CommitLibraries(); };
            toggles[library.Id] = toggle;
            list.Children.Add(Card(name, toggle));
        }
        void CommitLibraries() => _policy.LibraryIds = all.IsOn ? null : libraries.Where(library => toggles[library.Id].IsOn).Select(library => library.Id).ToList();
        all.Toggled += (_, _) =>
        {
            _syncing = true;
            foreach (var toggle in toggles.Values) toggle.IsOn = !all.IsOn;
            _syncing = false;
            CommitLibraries();
            list.Visibility = all.IsOn ? Visibility.Collapsed : Visibility.Visible;
            hint.Visibility = all.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (!all.IsOn && libraries.Length == 0) { list.Visibility = Visibility.Visible; }
        };
        if (libraries.Length == 0) list.Children.Add(Text(S("web.profiles.noLibraries", "No libraries yet."), 12, true));
        _hintRefreshers.Add(() =>
        {
            var source = AdminUserCreatePolicy.DefaultSource(Role, EffectiveGroupId);
            allLabelText.Text = SourceText(source, "allLibraries");
            AutomationProperties.SetName(all, allLabelText.Text);
            var hints = Hints();
            string? effective = hints is { LibraryIdsKnown: true } ? hints.LibraryIds == null ? S("web.profiles.libraryAccess.allLibraries", "All libraries")
                : S("web.profiles.libraries.count", $"{hints.LibraryIds.Count} libraries", ("count", hints.LibraryIds.Count), ("count_text", Count(hints.LibraryIds.Count))) : null;
            hint.Text = DefaultHint(source, effective);
        });
        return panel;
    }

    // ── Limits ──────────────────────────────────────────────────────────────────────────────────

    private FrameworkElement LimitsTab()
    {
        var panel = new StackPanel { Spacing = 16 };
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        UIElement[] fields =
        [
            LimitField("maxStreams", S("web.profiles.userPolicy.maxStreams", "Max Streams"), 0, () => _policy.MaxStreams, value => _policy.MaxStreams = value, hints => hints?.MaxStreams),
            LimitField("maxTranscodes", S("web.profiles.userPolicy.maxTranscodes", "Max Transcodes"), 0, () => _policy.MaxTranscodes, value => _policy.MaxTranscodes = value, hints => hints?.MaxTranscodes),
            BitrateField("maxRemoteBitrate", S("web.profiles.userPolicy.maxRemoteBitrate", "Max remote stream bitrate"), () => _policy.MaxRemoteStreamBitrateKbps, value => _policy.MaxRemoteStreamBitrateKbps = value, hints => hints?.MaxRemoteStreamBitrateKbps),
            BitrateField("maxLocalBitrate", S("web.profiles.userPolicy.maxLocalBitrate", "Max local stream bitrate"), () => _policy.MaxLocalStreamBitrateKbps, value => _policy.MaxLocalStreamBitrateKbps = value, hints => hints?.MaxLocalStreamBitrateKbps),
        ];
        foreach (var field in fields) grid.Children.Add(field);
        AdaptiveColumns(grid);
        panel.Children.Add(grid);

        var gates = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        gates.Children.Add(BooleanRow("CreateUserVideoTranscoding", S("web.profiles.userPolicy.videoTranscoding", "Video Transcoding"), null, () => _policy.TranscodeAllowed, value => _policy.TranscodeAllowed = value, hints => hints?.TranscodeAllowed));
        gates.Children.Add(BooleanRow("CreateUserAudioTranscoding", S("web.profiles.userPolicy.audioTranscoding", "Audio Transcoding"), S("web.profiles.userPolicy.audioTranscodingHint", "Audio conversion without video encoding."),
            () => _policy.AudioTranscodeAllowed, value => _policy.AudioTranscodeAllowed = value, hints => hints?.AudioTranscodeAllowed));
        AdaptiveColumns(gates);
        panel.Children.Add(gates);
        panel.Children.Add(QualityField());
        if (_context.ProfileLimitsSupported)
            panel.Children.Add(LimitField("maxProfiles", S("web.profiles.userPolicy.maxProfiles", "Max Profiles"), 1, () => _policy.MaxProfiles, value => _policy.MaxProfiles = value, hints => hints?.MaxProfiles));
        return panel;
    }

    // ── Policy field pieces ─────────────────────────────────────────────────────────────────────

    private UserPolicyHints? Hints() => AdminUserCreatePolicy.InheritHints(Role, EffectiveGroupId, _context.Groups);

    private static string SourceText(UserPolicyDefaultSource source, string kind) => (source, kind) switch
    {
        (UserPolicyDefaultSource.Group, "allLibraries") => S("web.profiles.userPolicy.groupAllLibraries", "Inherit from group"),
        (UserPolicyDefaultSource.Admin, "allLibraries") => S("web.profiles.userPolicy.adminDefault", "Admin default"),
        (_, "allLibraries") => S("web.profiles.userPolicy.serverDefault", "Server default"),
        (UserPolicyDefaultSource.Group, "limitHelp") => S("web.profiles.userPolicy.groupLimitHelp", "Enter a whole number, or turn Override off to inherit."),
        (UserPolicyDefaultSource.Admin, "limitHelp") => S("web.profiles.userPolicy.adminLimitHelp", "Enter a whole number, or turn Override off to use the admin default."),
        (_, "limitHelp") => S("web.profiles.userPolicy.serverLimitHelp", "Enter a whole number, or turn Override off to use the server default."),
        (UserPolicyDefaultSource.Group, "quality") => S("web.profiles.userPolicy.groupQuality", "Uses the access group's quality ceiling."),
        (UserPolicyDefaultSource.Admin, "quality") => S("web.profiles.userPolicy.adminQuality", "Uses the admin default quality ceiling."),
        _ => S("web.profiles.userPolicy.serverQuality", "Uses the server default quality ceiling."),
    };

    private string SourceLimitHelpMin(UserPolicyDefaultSource source, long min) => source switch
    {
        UserPolicyDefaultSource.Group => S("web.profiles.userPolicy.groupLimitHelpMin", $"Enter a whole number of at least {min}, or turn Override off to inherit.", ("min", min)),
        UserPolicyDefaultSource.Admin => S("web.profiles.userPolicy.adminLimitHelpMin", $"Enter a whole number of at least {min}, or turn Override off to use the admin default.", ("min", min)),
        _ => S("web.profiles.userPolicy.serverLimitHelpMin", $"Enter a whole number of at least {min}, or turn Override off to use the server default.", ("min", min)),
    };

    private static string DefaultHint(UserPolicyDefaultSource source, string? effective) => source switch
    {
        UserPolicyDefaultSource.Group => effective == null ? S("web.profiles.userPolicy.groupUnknown", "Inherited from group") : S("web.profiles.userPolicy.groupValue", $"Inherited: {effective}", ("value", effective)),
        UserPolicyDefaultSource.Admin => effective == null ? S("web.profiles.userPolicy.adminDefault", "Admin default") : S("web.profiles.userPolicy.adminValue", $"Admin default: {effective}", ("value", effective)),
        _ => effective == null ? S("web.profiles.userPolicy.serverDefault", "Server default") : S("web.profiles.userPolicy.serverValue", $"Server default: {effective}", ("value", effective)),
    };

    private UserPolicyDefaultSource Source => AdminUserCreatePolicy.DefaultSource(Role, EffectiveGroupId);

    private void RefreshHints() { foreach (var refresh in _hintRefreshers) refresh(); }

    /// <summary>A yes/no policy gate: inherit (named for where it inherits from), Allowed, or Not allowed.</summary>
    private Border BooleanRow(string id, string label, string? description, Func<bool?> get, Action<bool?> set, Func<UserPolicyHints?, bool?> effective)
    {
        var select = new ComboBox { MinWidth = 160, MaxWidth = 260, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(select, id); AutomationProperties.SetName(select, label);
        select.Items.Add(new ComboBoxItem { Tag = Inherit });
        select.Items.Add(new ComboBoxItem { Content = S("web.profiles.userPolicy.allowed", "Allowed"), Tag = "allowed" });
        select.Items.Add(new ComboBoxItem { Content = S("web.profiles.userPolicy.notAllowed", "Not allowed"), Tag = "blocked" });
        select.SelectedIndex = get() is { } current ? current ? 1 : 2 : 0;
        select.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            set(((select.SelectedItem as ComboBoxItem)?.Tag as string) switch { "allowed" => true, "blocked" => false, _ => null });
        };
        _hintRefreshers.Add(() =>
        {
            var value = effective(Hints());
            var text = DefaultHint(Source, value == null ? null : value.Value ? S("web.profiles.userPolicy.allowed", "Allowed") : S("web.profiles.userPolicy.notAllowed", "Not allowed"));
            _syncing = true;
            var index = select.SelectedIndex;
            ((ComboBoxItem)select.Items[0]).Content = text;
            select.SelectedIndex = -1; select.SelectedIndex = index;
            _syncing = false;
        });
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Text(label, 14));
        if (description != null) texts.Children.Add(Text(description, 12, true));
        return Card(texts, select, stackWhenNarrow: true);
    }

    /// <summary>A label with its Override switch; while it is off the field shows what it inherits instead of a control.</summary>
    private FrameworkElement OverrideField(string label, string id, bool overridden, Action<bool> overrideChanged, Func<UserPolicyHints?, string?> defaultText, UIElement control, out TextBlock defaultNote, out ToggleSwitch toggle)
    {
        var field = new StackPanel { Spacing = 4 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Text(label, 14));
        var overrideText = Text(S("web.profiles.userPolicy.override", "Override"), 12, true); overrideText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(overrideText, 1); header.Children.Add(overrideText);
        toggle = Switch(overridden);
        AutomationProperties.SetAutomationId(toggle, id + "Override"); AutomationProperties.SetName(toggle, label + " " + overrideText.Text);
        Grid.SetColumn(toggle, 2); header.Children.Add(toggle);
        field.Children.Add(header);
        var note = Text("", 14, true);
        var noteBorder = new Border { Child = note, Padding = new(12, 8, 12, 8), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.8) };
        defaultNote = note;
        field.Children.Add(noteBorder); field.Children.Add(control);
        var toggleRef = toggle;
        void Sync() { noteBorder.Visibility = toggleRef.IsOn ? Visibility.Collapsed : Visibility.Visible; control.Visibility = toggleRef.IsOn ? Visibility.Visible : Visibility.Collapsed; }
        toggle.Toggled += (_, _) => { Sync(); overrideChanged(toggleRef.IsOn); };
        Sync();
        var noteRef = note;
        _hintRefreshers.Add(() => noteRef.Text = DefaultHint(Source, defaultText(Hints())));
        return field;
    }

    private FrameworkElement LimitField(string key, string label, long min, Func<long?> get, Action<long?> set, Func<UserPolicyHints?, long?> effective)
    {
        var id = "CreateUser_" + key;
        var input = new TextBox { InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } }, Text = get()?.ToString() ?? "" };
        AutomationProperties.SetAutomationId(input, id); AutomationProperties.SetName(input, label);
        var help = Text("", 12, true);
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(input); body.Children.Add(help);
        ToggleSwitch toggle = null!;
        void Update()
        {
            var draftValue = AdminUserCreatePolicy.ParseLimit(input.Text, min);
            help.Text = draftValue == null ? (min > 0 ? SourceLimitHelpMin(Source, min) : SourceText(Source, "limitHelp")) : min == 0 ? S("web.profiles.userPolicy.zeroUnlimited", "0 = unlimited") : "";
            help.Visibility = help.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        _hintRefreshers.Add(Update);
        input.TextChanged += (_, _) =>
        {
            var parsed = AdminUserCreatePolicy.ParseLimit(input.Text, min);
            if (parsed == null) _invalidDrafts[key] = input.Text; else _invalidDrafts.Remove(key);
            if (parsed != null) set(parsed);
            Update();
        };
        var field = OverrideField(label, id, get() != null, on =>
        {
            if (!on) { input.Text = ""; set(null); _invalidDrafts.Remove(key); return; }
            // Seed what the field already resolves to; with no hint the box starts empty and the field keeps inheriting until a value is typed.
            var seeded = effective(Hints());
            input.Text = seeded?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            set(seeded);
            if (seeded == null) _invalidDrafts[key] = ""; else _invalidDrafts.Remove(key);
        }, hints => effective(hints) is { } value ? value == 0 ? S("web.profiles.userPolicy.unlimited", "Unlimited") : value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, body, out _, out toggle);
        var overrideToggle = toggle;
        _revealInvalid[key] = () => { ShowTab("limits"); overrideToggle.IsOn = true; input.Focus(FocusState.Programmatic); };
        return field;
    }

    private FrameworkElement BitrateField(string key, string label, Func<long?> get, Action<long?> set, Func<UserPolicyHints?, long?> effective)
    {
        var id = "CreateUser_" + key;
        const string Unlimited = "0", Custom = "custom";
        var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(choice, id); AutomationProperties.SetName(choice, label);
        choice.Items.Add(new ComboBoxItem { Content = S("web.player.bitrateLimit.unlimited", "Unlimited"), Tag = Unlimited });
        foreach (var kbps in AdminUserCreatePolicy.StreamBitratePresetsKbps)
            choice.Items.Add(new ComboBoxItem { Content = FormatBitrate(kbps), Tag = kbps.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        choice.Items.Add(new ComboBoxItem { Content = S("web.player.bitrateLimit.custom", "Custom"), Tag = Custom });
        var custom = new TextBox { PlaceholderText = S("web.player.bitrateLimit.customPlaceholder", "e.g. 12.5"), MinWidth = 100, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(custom, S("web.player.bitrateLimit.customAria", $"{label}, custom Mbps", ("label", label)));
        var unit = Text(S("web.player.bitrateLimit.unit", "Mbps"), 14, true); unit.VerticalAlignment = VerticalAlignment.Center; unit.Visibility = Visibility.Collapsed;
        var warning = Text("", 12, true); warning.Visibility = Visibility.Collapsed;
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(choice); Grid.SetColumn(custom, 1); row.Children.Add(custom); Grid.SetColumn(unit, 2); row.Children.Add(unit);
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(row); body.Children.Add(warning);
        var syncing = false;
        void SetChoice(long? kbps)
        {
            syncing = true;
            if (kbps == null) choice.SelectedIndex = -1;
            else choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == kbps.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) ?? choice.Items[choice.Items.Count - 1];
            syncing = false;
            var isCustom = (choice.SelectedItem as ComboBoxItem)?.Tag as string == Custom;
            custom.Visibility = unit.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        }
        void ShowWarning(long? kbps)
        {
            warning.Text = kbps is { } value && AdminUserCreatePolicy.IsLowStreamBitrate(value)
                ? S("web.player.bitrateLimit.lowWarning", $"Below {AdminUserCreatePolicy.LowStreamBitrateKbps / 1000} Mbps forces very low-quality transcodes.", ("mbps", AdminUserCreatePolicy.LowStreamBitrateKbps / 1000))
                : custom.Visibility == Visibility.Visible && AdminUserCreatePolicy.ParseStreamBitrateMbps(custom.Text) == null
                    ? S("web.player.bitrateLimit.invalid", "Enter a positive number of Mbps with up to three decimals.", ("places", 3), ("min", 0)) : "";
            warning.Visibility = warning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        choice.SelectionChanged += (_, _) =>
        {
            if (syncing || choice.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
            if (tag != Custom)
            {
                custom.Visibility = unit.Visibility = Visibility.Collapsed;
                _invalidDrafts.Remove(key); set(long.Parse(tag, System.Globalization.CultureInfo.InvariantCulture)); ShowWarning(null);
                return;
            }
            custom.Visibility = unit.Visibility = Visibility.Visible;
            var seed = get() is > 0 and var current ? AdminUserCreatePolicy.StreamBitrateMbpsText(current) : "";
            custom.Text = seed;
            CommitCustom();
        };
        void CommitCustom()
        {
            var parsed = AdminUserCreatePolicy.ParseStreamBitrateMbps(custom.Text);
            if (parsed == null) _invalidDrafts[key] = custom.Text; else { _invalidDrafts.Remove(key); set(parsed); }
            ShowWarning(parsed);
        }
        custom.TextChanged += (_, _) => { if (!syncing) CommitCustom(); };
        var initial = get();
        SetChoice(initial);
        if (initial != null && (choice.SelectedItem as ComboBoxItem)?.Tag as string == Custom) custom.Text = AdminUserCreatePolicy.StreamBitrateMbpsText(initial.Value);
        ToggleSwitch toggle = null!;
        var field = OverrideField(label, id, initial != null, on =>
        {
            if (!on) { set(null); _invalidDrafts.Remove(key); SetChoice(null); custom.Text = ""; ShowWarning(null); return; }
            var seeded = effective(Hints());
            set(seeded); SetChoice(seeded);
            if (seeded == null) _invalidDrafts[key] = ""; else _invalidDrafts.Remove(key);
        }, hints => effective(hints) is { } value ? FormatBitrate(value) : null, body, out _, out toggle);
        var overrideToggle = toggle;
        _revealInvalid[key] = () => { ShowTab("limits"); overrideToggle.IsOn = true; (custom.Visibility == Visibility.Visible ? (Control)custom : choice).Focus(FocusState.Programmatic); };
        return field;
    }

    private static string FormatBitrate(long kbps) => kbps == 0 ? S("web.player.bitrateLimit.unlimited", "Unlimited")
        : S("web.player.bitrateLimit.value", $"{AdminUserCreatePolicy.StreamBitrateMbpsText(kbps)} Mbps", ("mbps", AdminUserCreatePolicy.StreamBitrateMbpsText(kbps)));

    private FrameworkElement QualityField()
    {
        var panel = new StackPanel { Spacing = 6 };
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(select, "CreateUserMaxPlaybackQuality");
        AutomationProperties.SetName(select, S("web.profiles.userPolicy.maxPlaybackQuality", "Max Playback Quality"));
        var options = new (string Tag, string Label, string Description)[]
        {
            ("any", S("web.profiles.helpers.quality.any", "Any"), S("web.profiles.helpers.quality.anyDescription", "No limit on playback quality.")),
            ("standard", S("web.profiles.helpers.quality.standard", "Standard"), S("web.profiles.helpers.quality.standardDescription", "Up to 1080p.")),
            ("4k", "4K", S("web.profiles.helpers.quality.fourKDescription", "Up to 4K.")),
        };
        select.Items.Add(new ComboBoxItem { Tag = Inherit });
        foreach (var option in options) select.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Tag });
        select.SelectedIndex = 0;
        var help = Text("", 12, true);
        string? PresetOf(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "any" => "any", "standard" or "480p" or "720p" or "1080p" => "standard", "4k" or "uhd" or "2160p" or "4320p" => "4k", _ => "any",
        };
        void Describe()
        {
            var tag = (select.SelectedItem as ComboBoxItem)?.Tag as string;
            help.Text = tag == Inherit ? SourceText(Source, "quality") : options.First(option => option.Tag == tag).Description;
        }
        select.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            _policy.MaxPlaybackQuality = ((select.SelectedItem as ComboBoxItem)?.Tag as string) switch { "standard" => "1080p", "4k" => "2160p", "any" => "", _ => null };
            Describe();
        };
        _hintRefreshers.Add(() =>
        {
            var hints = Hints();
            string? effective = hints is { MaxPlaybackQualityKnown: true } ? options.First(option => option.Tag == PresetOf(hints.MaxPlaybackQuality)).Label : null;
            _syncing = true;
            var index = select.SelectedIndex;
            ((ComboBoxItem)select.Items[0]).Content = DefaultHint(Source, effective);
            select.SelectedIndex = -1; select.SelectedIndex = index;
            _syncing = false;
            Describe();
        });
        panel.Children.Add(Text(S("web.profiles.userPolicy.maxPlaybackQuality", "Max Playback Quality"), 14));
        panel.Children.Add(select); panel.Children.Add(help);
        return panel;
    }

    // ── Reading the form ────────────────────────────────────────────────────────────────────────

    /// <summary>The password rule at its field (revealing the Account tab and focusing it), or null when the password is fine.</summary>
    internal string? PasswordProblem() => NewPassword.Problem(_password.Password, _context.PasswordPolicy);

    internal void ShowPasswordError(string? message, bool focus = true)
    {
        _passwordField?.ShowError(message);
        if (message == null) return;
        ShowTab("account");
        if (focus) _password.Focus(FocusState.Programmatic);
    }

    internal void ShowPasswordPolicy(PasswordPolicy policy) => _passwordField?.ShowPolicy(policy);

    internal bool EmailIsValid => AdminUserCreatePolicy.IsValidEmail(_email.Text);

    internal void FocusEmail() { ShowTab("account"); _email.Focus(FocusState.Programmatic); }

    /// <summary>A limit box that holds no valid number blocks saving; its tab is shown and the box focused. True when one did.</summary>
    internal bool RevealInvalidLimit()
    {
        var key = _invalidDrafts.Keys.FirstOrDefault();
        if (key == null) return false;
        if (_revealInvalid.TryGetValue(key, out var reveal)) reveal();
        return true;
    }

    internal bool HasUsername => _username.Text.Trim().Length > 0;

    internal void FocusUsername() { ShowTab("account"); _username.Focus(FocusState.Programmatic); }

    internal JsonObject Build() => AdminUserCreatePolicy.BuildBody(_username.Text, _email.Text.Trim(), _password.Password, _requireChange.IsOn && _password.Password.Length > 0, Role,
        _permissions, DefaultProfileChecked, _policy, SelectedGroupId, sendGroup: _context.GroupsSupported, sendMaxProfiles: _context.ProfileLimitsSupported);

    private static string Label(string key) => string.Join(" ", key.Split('_', '-').Where(part => part.Length > 0).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
