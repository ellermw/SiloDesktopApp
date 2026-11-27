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

/// <summary>What the invitation form needs from the page: the viewer, what the server can do, and the groups and libraries it lists.</summary>
internal sealed record InvitationContext(bool ViewerIsOwner, bool DefaultProfile, bool EmailDelivery, IReadOnlyList<JsonObject> Groups, IReadOnlyList<JsonObject> Libraries);

/// <summary>Invite someone (official <c>CreateInvitationForm</c>): how they get the link, who they are, what they can use, a note, and whether to make their first profile.</summary>
internal sealed class AdminUsersInvitationForm
{
    private readonly InvitationContext _context;
    private readonly RadioButton _link = new() { GroupName = "invitation-delivery-" + Guid.NewGuid().ToString("N") };
    private readonly RadioButton _email = new();
    private readonly Border _linkCard, _emailCard;
    private readonly TextBox _address = new() { PlaceholderText = "them@example.com", InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.EmailSmtpAddress) } } };
    private readonly TextBlock _addressProblem = Problem();
    private readonly StackPanel _addressField;
    private readonly ComboBox _group = new() { HorizontalAlignment = HorizontalAlignment.Stretch }, _role = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch _inherit = Switch(true), _createProfile = Switch(), _tour = Switch(true);
    private readonly Dictionary<string, ToggleSwitch> _libraryToggles = new(StringComparer.Ordinal);
    private readonly StackPanel _libraryList = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBox _note = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 72, MaxLength = 4096 };
    private readonly TextBlock _noteLabel = Text("", 14), _noteHint = Text("", 12, true), _profileHint = Text("", 12, true);
    private bool _syncing;

    internal FrameworkElement Content { get; }
    internal event Action? Changed;
    internal bool IsEmail => _email.IsChecked == true;
    /// <summary>The server cannot make a default profile: Create their first profile stays off and the form cannot be sent with it on.</summary>
    internal bool Blocked => !_context.DefaultProfile && _createProfile.IsOn;

    private static string S(string key, string fallback, params (string Name, object? Value)[] args) =>
        AppStrings.Get(key, args.Length == 0 ? null : args.ToDictionary(pair => pair.Name, pair => pair.Value), fallback);

    internal AdminUsersInvitationForm(InvitationContext context)
    {
        _context = context;
        var root = new StackPanel { Spacing = 16 };
        if (!context.DefaultProfile) root.Children.Add(Text(T("web.admin.settings.invitations.create.noDefaultProfile"), 13, true));

        // How they get it
        var delivery = new StackPanel { Spacing = 8 };
        delivery.Children.Add(Text(T("web.admin.settings.invitations.create.deliveryQuestion"), 14));
        _linkCard = ChoiceCard(_link, T("web.admin.settings.invitations.create.linkOption"), T("web.admin.settings.invitations.create.linkOptionBody"));
        _emailCard = ChoiceCard(_email, T("web.admin.settings.invitations.create.emailOption"),
            T(context.EmailDelivery ? "web.admin.settings.invitations.create.emailOptionBody" : "web.admin.settings.invitations.create.emailOptionUnavailable"), context.EmailDelivery);
        _email.GroupName = _link.GroupName;
        AutomationProperties.SetAutomationId(_link, "InvitationDeliveryLink"); AutomationProperties.SetAutomationId(_email, "InvitationDeliveryEmail");
        var cards = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        cards.Children.Add(_linkCard); cards.Children.Add(_emailCard);
        AdaptiveColumns(cards);
        delivery.Children.Add(cards);
        if (!context.EmailDelivery) delivery.Children.Add(Text(T("web.admin.settings.invitations.create.emailSetup"), 13, true));
        root.Children.Add(delivery);
        (context.EmailDelivery ? _email : _link).IsChecked = true;
        _link.Checked += (_, _) => DeliveryChanged(); _email.Checked += (_, _) => DeliveryChanged();

        // Their address
        AutomationProperties.SetAutomationId(_address, "InvitationEmail"); AutomationProperties.SetName(_address, T("web.admin.settings.invitations.create.emailLabel"));
        _address.TextChanged += (_, _) => { Say(_addressProblem, null); Changed?.Invoke(); };
        _addressField = Field(T("web.admin.settings.invitations.create.emailLabel"), _address, _addressProblem, Text(T("web.admin.settings.invitations.create.emailHint"), 12, true));
        root.Children.Add(_addressField);

        // Group and role
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        var defaultGroup = context.Groups.FirstOrDefault(group => group.Flag("is_default"));
        _group.Items.Add(new ComboBoxItem { Content = defaultGroup != null ? T("web.admin.settings.invitations.create.defaultGroup", ("name", defaultGroup.Text("name"))) : T("web.admin.settings.invitations.create.serverDefault"), Tag = "default" });
        foreach (var group in context.Groups.Where(group => !group.Flag("is_default"))) _group.Items.Add(new ComboBoxItem { Content = group.Text("name"), Tag = group["id"]?.ToString() ?? "" });
        _group.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(_group, "InvitationGroup"); AutomationProperties.SetName(_group, T("web.admin.settings.invitations.create.accessGroup"));
        _group.SelectionChanged += (_, _) => Changed?.Invoke();
        grid.Children.Add(Field(T("web.admin.settings.invitations.create.accessGroup"), _group));
        _role.Items.Add(new ComboBoxItem { Content = T("web.admin.settings.invitations.create.roleUser"), Tag = "user" });
        _role.Items.Add(new ComboBoxItem { Content = T("web.admin.settings.invitations.create.roleAdmin"), Tag = "admin", IsEnabled = context.ViewerIsOwner });
        _role.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(_role, "InvitationRole"); AutomationProperties.SetName(_role, T("web.admin.settings.invitations.create.role"));
        _role.SelectionChanged += (_, _) => { _group.IsEnabled = Role != "admin"; Changed?.Invoke(); };
        UIElement[] roleNote = context.ViewerIsOwner ? [] : [Text(T("web.admin.settings.invitations.create.ownerOnly"), 12, true)];
        grid.Children.Add(Field(T("web.admin.settings.invitations.create.role"), _role, roleNote));
        AdaptiveColumns(grid);
        root.Children.Add(grid);

        root.Children.Add(LibraryAccess());

        // Note
        AutomationProperties.SetAutomationId(_note, "InvitationNote"); AutomationProperties.SetName(_note, T("web.admin.settings.invitations.create.noteLabel"));
        _note.PlaceholderText = T("web.admin.settings.invitations.create.notePlaceholder");
        _note.TextChanged += (_, _) => Changed?.Invoke();
        var note = new StackPanel { Spacing = 6 };
        note.Children.Add(_noteLabel); note.Children.Add(_note); note.Children.Add(_noteHint);
        root.Children.Add(note);

        // Switches
        AutomationProperties.SetAutomationId(_createProfile, "InvitationCreateProfile"); AutomationProperties.SetAutomationId(_tour, "InvitationTour");
        _createProfile.IsOn = context.DefaultProfile; _createProfile.IsEnabled = context.DefaultProfile || _createProfile.IsOn;
        _createProfile.Toggled += (_, _) => Changed?.Invoke(); _tour.Toggled += (_, _) => Changed?.Invoke();
        var profileTexts = new StackPanel { Spacing = 2 };
        profileTexts.Children.Add(Text(T("web.admin.settings.invitations.create.profileLabel"), 14)); profileTexts.Children.Add(_profileHint);
        AutomationProperties.SetName(_createProfile, T("web.admin.settings.invitations.create.profileLabel"));
        var tourTexts = new StackPanel { Spacing = 2 };
        tourTexts.Children.Add(Text(T("web.admin.settings.invitations.create.tourLabel"), 14)); tourTexts.Children.Add(Text(T("web.admin.settings.invitations.create.tourHint"), 12, true));
        AutomationProperties.SetName(_tour, T("web.admin.settings.invitations.create.tourLabel"));
        root.Children.Add(Card(profileTexts, _createProfile));
        root.Children.Add(Card(tourTexts, _tour));
        root.Children.Add(Text(T("web.admin.settings.invitations.create.expiry"), 12, true));
        Content = root;
        DeliveryChanged();
    }

    private string Role => (_role.SelectedItem as ComboBoxItem)?.Tag as string ?? "user";

    private FrameworkElement LibraryAccess()
    {
        var box = new StackPanel { Spacing = 8 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Text(S("web.profiles.libraryAccess.label", "Library Access"), 14));
        var all = Text(T("web.admin.settings.invitations.create.inheritGroup"), 12, true); all.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(all, 1); header.Children.Add(all);
        Grid.SetColumn(_inherit, 2); header.Children.Add(_inherit);
        AutomationProperties.SetAutomationId(_inherit, "InvitationLibrariesInherit"); AutomationProperties.SetName(_inherit, T("web.admin.settings.invitations.create.inheritGroup"));
        var hint = Text(T("web.admin.settings.invitations.create.libraryHint"), 12, true);
        box.Children.Add(header); box.Children.Add(hint); box.Children.Add(_libraryList);
        foreach (var library in _context.Libraries)
        {
            var id = library["id"]?.ToString() ?? "";
            var name = Text(library.Text("name"), 14);
            var toggle = Switch(false);
            AutomationProperties.SetName(toggle, library.Text("name")); AutomationProperties.SetAutomationId(toggle, "InvitationLibrary_" + id);
            toggle.Toggled += (_, _) => { if (!_syncing) Changed?.Invoke(); };
            _libraryToggles[id] = toggle; _libraryList.Children.Add(Card(name, toggle));
        }
        if (_context.Libraries.Count == 0) _libraryList.Children.Add(Text(S("web.profiles.noLibraries", "No libraries yet."), 12, true));
        _inherit.Toggled += (_, _) =>
        {
            _syncing = true; foreach (var toggle in _libraryToggles.Values) toggle.IsOn = false; _syncing = false;
            _libraryList.Visibility = _inherit.IsOn ? Visibility.Collapsed : Visibility.Visible; hint.Visibility = _inherit.IsOn ? Visibility.Visible : Visibility.Collapsed; Changed?.Invoke();
        };
        return box;
    }

    private void DeliveryChanged()
    {
        PaintChoice(_linkCard, _link.IsChecked == true); PaintChoice(_emailCard, _email.IsChecked == true);
        var email = IsEmail;
        _addressField.Visibility = email ? Visibility.Visible : Visibility.Collapsed;
        _noteLabel.Text = T(email ? "web.admin.settings.invitations.create.personalNoteLabel" : "web.admin.settings.invitations.create.noteLabel");
        _noteHint.Text = T(email ? "web.admin.settings.invitations.create.noteHintEmail" : "web.admin.settings.invitations.create.noteHintLink");
        _profileHint.Text = T(email ? "web.admin.settings.invitations.create.profileHintEmail" : "web.admin.settings.invitations.create.profileHintLink");
        Changed?.Invoke();
    }

    internal string PrimaryText => T(IsEmail ? "web.admin.settings.invitations.create.submitEmail" : "web.admin.settings.invitations.create.submitLink");
    internal string SavingText => T(IsEmail ? "web.admin.settings.invitations.create.sending" : "web.admin.settings.invitations.create.creating");

    internal bool Validate()
    {
        Say(_addressProblem, null);
        if (IsEmail && !AdminUserCreatePolicy.IsValidEmail(_address.Text)) { Say(_addressProblem, S("web.auth.invalidEmail", "Enter a valid email address.")); _address.Focus(FocusState.Programmatic); return false; }
        return true;
    }

    internal JsonObject Body()
    {
        var group = (_group.SelectedItem as ComboBoxItem)?.Tag as string;
        IReadOnlyList<string>? libraries = _inherit.IsOn ? null : _context.Libraries.Select(row => row["id"]?.ToString() ?? "").Where(id => _libraryToggles[id].IsOn).ToArray();
        return AdminUserFormBodies.Invitation(IsEmail ? "email" : "link", _address.Text, Role, group is null or "default" ? null : group, libraries, _createProfile.IsOn, _tour.IsOn, _note.Text);
    }
}

/// <summary>Create invite code, Edit invite code and Add uses (official invite-code forms).</summary>
internal sealed class AdminUsersCodeForm
{
    private readonly TextBox _code = new() { MaxLength = 128 }, _label = new() { MaxLength = 512 }, _uses = new() { InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
    private readonly TextBlock _codeProblem = Problem(), _usesProblem = Problem();
    private readonly bool _showCode;
    private readonly string _usesLabel;
    private readonly JsonObject? _saved;
    private readonly bool _topUp;

    internal FrameworkElement Content { get; }
    internal event Action? Changed;

    /// <summary>The code and label are typed for a new code; an edit changes the label and the uses; adding uses asks only for the uses.</summary>
    internal AdminUsersCodeForm(JsonObject? saved, bool topUp)
    {
        _saved = saved; _topUp = topUp; _showCode = saved == null && !topUp;
        _usesLabel = T(topUp ? "web.admin.settings.inviteCodes.topUpForm.additionalUses" : "web.admin.settings.inviteCodes.form.maxUses");
        var stack = new StackPanel { Spacing = 16 };
        if (_showCode)
        {
            _code.PlaceholderText = T("web.admin.settings.inviteCodes.form.codePlaceholder");
            AutomationProperties.SetAutomationId(_code, "InviteCode"); AutomationProperties.SetName(_code, T("web.admin.settings.inviteCodes.form.code"));
            stack.Children.Add(Field(T("web.admin.settings.inviteCodes.form.code"), _code, _codeProblem));
        }
        if (!topUp)
        {
            _label.Text = saved?.Text("label") ?? ""; _label.PlaceholderText = T("web.admin.settings.inviteCodes.form.labelPlaceholder");
            AutomationProperties.SetAutomationId(_label, "InviteLabel"); AutomationProperties.SetName(_label, T("web.admin.settings.inviteCodes.form.label"));
            stack.Children.Add(Field(T("web.admin.settings.inviteCodes.form.label"), _label));
        }
        _uses.Text = topUp ? "1" : saved != null ? saved.Count("max_uses").ToString() : "10";
        AutomationProperties.SetAutomationId(_uses, "InviteUses"); AutomationProperties.SetName(_uses, _usesLabel);
        stack.Children.Add(Field(_usesLabel, _uses, _usesProblem));
        _code.TextChanged += (_, _) => { Say(_codeProblem, null); Changed?.Invoke(); };
        _label.TextChanged += (_, _) => Changed?.Invoke();
        _uses.TextChanged += (_, _) => { Say(_usesProblem, null); Changed?.Invoke(); };
        Content = stack;
    }

    internal bool HasChange => _topUp || _saved == null || AdminUserFormBodies.PositiveUses(_uses.Text) is not { } uses || AdminUserFormBodies.CodeUpdate(_saved, _label.Text, uses).Count > 0;

    internal bool Validate()
    {
        Say(_codeProblem, null); Say(_usesProblem, null);
        if (_showCode && _code.Text.Trim().Length == 0) { Say(_codeProblem, AppStrings.Get("error.required", fallback: "Fill in this field.")); _code.Focus(FocusState.Programmatic); return false; }
        if (AdminUserFormBodies.PositiveUses(_uses.Text) == null)
        {
            Say(_usesProblem, T(_topUp ? "web.admin.settings.inviteCodes.topUpForm.invalid" : "web.admin.settings.inviteCodes.form.maxUsesInvalid")); _uses.Focus(FocusState.Programmatic); return false;
        }
        return true;
    }

    internal JsonObject Body()
    {
        var uses = AdminUserFormBodies.PositiveUses(_uses.Text)!.Value;
        return _topUp ? AdminUserFormBodies.CodeTopUp(uses) : _saved == null ? AdminUserFormBodies.CodeCreate(_code.Text, _label.Text, uses) : AdminUserFormBodies.CodeUpdate(_saved, _label.Text, uses);
    }
}

/// <summary>Connect a sign-in identity (official <c>LinkIdentityDialog</c>): the provider, the provider's exact subject for the person, and optional names.</summary>
internal sealed class AdminUsersIdentityForm
{
    internal sealed record Provider(string Id, string Name, bool Enabled, bool Network);
    private readonly IReadOnlyList<Provider> _providers;
    private readonly ComboBox _provider = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _subject = new() { FontFamily = AdminMonitorUi.Mono, FontSize = 12, MaxLength = 1024, IsSpellCheckEnabled = false };
    private readonly TextBox _username = new() { MaxLength = 255 }, _email = new() { MaxLength = 320 }, _name = new() { MaxLength = 255 };
    private readonly TextBlock _problem = Problem(), _description = Text("", 14, true);
    private readonly JsonObject _user;

    internal FrameworkElement Content { get; }
    internal event Action? Changed;
    internal bool HasProviders => _providers.Count > 0;

    internal AdminUsersIdentityForm(JsonObject user, IReadOnlyList<Provider> providers)
    {
        _user = user; _providers = providers;
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(_description);
        stack.Children.Add(_problem);
        if (providers.Count == 0) stack.Children.Add(Text(T("web.admin.common.adminUserSignIn.link.noProviders"), 14, true));
        else
        {
            foreach (var provider in providers)
                _provider.Items.Add(new ComboBoxItem { Content = provider.Enabled ? provider.Name : T("web.admin.common.adminUserSignIn.link.providerOff", ("provider", provider.Name)), Tag = provider.Id });
            _provider.SelectedIndex = Math.Max(0, providers.ToList().FindIndex(provider => provider.Enabled));
            AutomationProperties.SetAutomationId(_provider, "IdentityProvider"); AutomationProperties.SetName(_provider, T("web.admin.common.adminUserSignIn.link.provider"));
            _provider.SelectionChanged += (_, _) => { Describe(); Changed?.Invoke(); };
            stack.Children.Add(Field(T("web.admin.common.adminUserSignIn.link.provider"), _provider));
        }
        _subject.PlaceholderText = T("web.admin.common.adminUserSignIn.link.subjectPlaceholder");
        AutomationProperties.SetAutomationId(_subject, "IdentitySubject"); AutomationProperties.SetName(_subject, T("web.admin.common.adminUserSignIn.link.subject"));
        _subject.TextChanged += (_, _) => { Say(_problem, null); Changed?.Invoke(); };
        stack.Children.Add(Field(T("web.admin.common.adminUserSignIn.link.subject"), _subject, Text(T("web.admin.common.adminUserSignIn.link.subjectHint"), 12, true)));
        var names = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        foreach (var (label, box, id) in new[] { (T("web.admin.common.adminUserSignIn.link.username"), _username, "IdentityUsername"), (T("web.admin.common.adminUserSignIn.link.email"), _email, "IdentityEmail"), (T("web.admin.common.adminUserSignIn.link.name"), _name, "IdentityName") })
        {
            AutomationProperties.SetAutomationId(box, id); AutomationProperties.SetName(box, label);
            names.Children.Add(Field(label, box));
        }
        names.SizeChanged += (_, _) =>
        {
            var columns = names.ActualWidth > 0 && names.ActualWidth < 520 ? 1 : 3;
            if (names.ColumnDefinitions.Count == columns) return;
            names.ColumnDefinitions.Clear(); names.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++) names.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var row = 0; row < (3 + columns - 1) / columns; row++) names.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var index = 0; index < 3; index++) { var child = (FrameworkElement)names.Children[index]; Grid.SetRow(child, index / columns); Grid.SetColumn(child, index % columns); }
        };
        stack.Children.Add(names);
        stack.Children.Add(Text(T("web.admin.common.adminUserSignIn.link.optionalHint"), 12, true));
        foreach (var box in new[] { _username, _email, _name }) box.TextChanged += (_, _) => Changed?.Invoke();
        Content = stack;
        Describe();
    }

    internal string ProviderName => SelectedProvider?.Name ?? "";
    private Provider? SelectedProvider => _provider.SelectedItem is ComboBoxItem { Tag: string id } ? _providers.FirstOrDefault(provider => provider.Id == id) : null;

    private void Describe()
    {
        var note = _user.Flag("break_glass") ? "breakGlass" : SelectedProvider is { Network: true } ? (_user.Flag("password_login") ? "keeps" : "none") : "turnsOff";
        _description.Text = T("web.admin.common.adminUserSignIn.link.description", ("username", _user.Text("username")), ("note", note));
    }

    /// <summary>A problem for the person to fix, shown above the fields.</summary>
    internal void Show(string message) { Say(_problem, message); }

    internal bool Validate()
    {
        Say(_problem, null);
        if (SelectedProvider == null) { Say(_problem, T("web.admin.common.adminUserSignIn.link.chooseProvider")); return false; }
        if (_subject.Text.Trim().Length == 0) { Say(_problem, T("web.admin.common.adminUserSignIn.link.enterSubject")); _subject.Focus(FocusState.Programmatic); return false; }
        return true;
    }

    internal JsonObject Body() => AdminUserFormBodies.Identity(SelectedProvider!.Id, _subject.Text, _username.Text, _email.Text, _name.Text);
}
