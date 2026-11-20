using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.ApplicationModel.DataTransfer;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminSignInSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed record ProviderField(AdminSignInProvider Provider, AdminSignInSchema Schema, AdminSignInField Field, AdminSettingsRow Row, Control Input, Button? Clear);
    private readonly AdminAccessPageUi _ui;
    private readonly Dictionary<string, ToggleSwitch> _settings = [];
    private readonly Dictionary<string, AdminSettingsRow> _settingRows = [];
    private readonly Dictionary<string, AdminSettingsGroup> _groups = [];
    private readonly List<ProviderField> _providerFields = [];
    private readonly Dictionary<string, ToggleSwitch> _auto = [];
    private readonly Dictionary<string, Button> _binding = [], _test = [];
    private readonly Dictionary<string, TextBlock> _results = [];
    private readonly Dictionary<string, TextBox> _usernames = [];
    private readonly AdminSettingsSaveBar _saveBar = new("SignIn");
    private IReadOnlyList<AdminSignInProvider>? _builtProviders;
    private IReadOnlyList<AdminAccessAccount>? _builtAccounts;
    private bool _built, _sync;
    private string? _pickedProvider;
    public AdminSignInController Controller { get; }
    public FrameworkElement ScrollContent => _ui.ScrollContent;
    public bool RequiresLeaveConfirmation => Controller.Current && Controller.DirtyCount > 0;
    public AdminSignInSettingsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        Controller = App.Services.GetRequiredService<AdminSignInController>();
        _ui = new(this, Controller, T("page.title"), "", Render) { Reopen = Controller.OpenAsync, RequiresDiscard = () => RequiresLeaveConfirmation };
        _ui.Body.MaxWidth = 1120; _ui.Body.HorizontalAlignment = HorizontalAlignment.Left; _ui.Body.Padding = new(0, 0, 0, 112);
        _ui.Root.Children.Add(_saveBar); SizeChanged += (_, _) => _saveBar.SetAvailableWidth(ActualWidth);
        _saveBar.Discard.Click += (_, _) => Controller.Discard();
        _saveBar.Save.Click += async (_, _) =>
        {
            var owner = Controller.Owner;
            if (await _ui.ConfirmAsync("Save sign-in settings?", "These changes can affect who can sign in and how accounts are created. Review the staged settings before saving.", "Save") && ReferenceEquals(owner, Controller.Owner)) await Controller.SaveAsync(true);
        };
        Loaded += async (_, _) => await Controller.OpenAsync(); Unloaded += (_, _) => Close();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Close(); base.OnNavigatedFrom(e); }
    public void SuspendAdminPage() => Close();
    private void Close() { _ui.Close(); Controller.Dispose(); }
    public async Task<bool> CanLeaveAsync()
    {
        if (!RequiresLeaveConfirmation) return true; var owner = Controller.Owner;
        var discard = await _ui.ConfirmDiscardAsync(); if (!ReferenceEquals(owner, Controller.Owner) || owner?.IsCurrent != true) return true;
        if (discard) Controller.Discard(); return discard;
    }
    private void Render()
    {
        if (!Controller.Current) { _built = false; _builtProviders = null; _builtAccounts = null; _saveBar.State(0, false, false); return; }
        if (Controller.Loaded && (!_built || !ReferenceEquals(_builtProviders, Controller.Providers) || !ReferenceEquals(_builtAccounts, Controller.Accounts))) Build();
        var editable = Controller.Loaded && !Controller.Busy && !Controller.ReviewRequired;
        _saveBar.State(Controller.DirtyCount, Controller.Busy, editable); _saveBar.SetAvailableWidth(ActualWidth);
        _sync = true;
        try
        {
            foreach (var (key, control) in _settings)
            {
                control.IsOn = key == AdminSignInController.Local ? Controller.Value(key) != "false" : Controller.Value(key) == "true";
                control.IsEnabled = editable && !Controller.Managed.Contains(key) && (key != AdminSignInController.Local || Controller.AccountsKnown && Controller.ProvidersKnown);
                _settingRows[key].State(Controller.Dirty(key), Controller.RestartKeys.Has(key));
            }
            foreach (var entry in _providerFields)
            {
                var values = Controller.Values(entry.Provider, entry.Schema); var value = values[entry.Field.Key];
                var text = value is JsonArray array ? string.Join("\n", array.Select(n => n?.ToString() ?? "")) : value?.ToString() ?? "";
                entry.Row.Visibility = AdminSignInSchemaRules.Visible(entry.Schema.AdminForm!, entry.Field, values) ? Visibility.Visible : Visibility.Collapsed;
                entry.Input.IsEnabled = editable;
                entry.Row.State(!entry.Field.IsSecret && Controller.FieldDirty(entry.Provider, entry.Schema.Key, entry.Field.Key), false);
                if (entry.Input is TextBox input && input.Text != text) input.Text = text;
                else if (entry.Input is PasswordBox password && password.Password != text) password.Password = text;
                else if (entry.Input is ToggleSwitch toggle) toggle.IsOn = text == "true";
                else if (entry.Input is ComboBox select) AdminAccessPageUi.Select(select, text);
                else if (entry.Input is ListView list && value is JsonArray selected)
                {
                    list.SelectedItems.Clear(); foreach (var item in list.Items.OfType<ListViewItem>()) if (selected.Any(n => n?.ToString() == item.Tag as string)) list.SelectedItems.Add(item);
                }
                if (entry.Clear != null)
                {
                    entry.Clear.Content = T(Controller.Clearing(entry.Provider, entry.Schema.Key, entry.Field.Key) ? "configField.keep" : "configField.clear"); entry.Clear.IsEnabled = editable;
                }
            }
            foreach (var provider in Controller.Providers)
            {
                if (_auto.TryGetValue(provider.Id, out var auto)) { auto.IsOn = Controller.AutoProvision(provider); auto.IsEnabled = editable; }
                var on = provider.Binding?.Enabled == true;
                if (_binding.TryGetValue(provider.Id, out var button))
                {
                    button.IsEnabled = editable && Controller.Capabilities is { Available: true, LiveProviderChanges: true } && (on || provider.Enabled && !Controller.ProviderDirty(provider) && AdminSignInSchemaRules.Missing(provider).Count == 0 &&
                        (provider.Network || !Controller.Providers.Any(p => !p.Network && p.Id != provider.Id && p.Binding?.Enabled == true)));
                }
                if (_test.TryGetValue(provider.Id, out var test)) test.IsEnabled = editable && provider.Enabled;
                if (_results.TryGetValue(provider.Id, out var result))
                {
                    result.Text = Controller.TestResults.TryGetValue(provider.Id, out var feedback)
                        ? T(feedback.Ok ? "provider.test.allPassed" : "provider.test.didNotPass") + "\n" + string.Join("\n", feedback.Steps.Select(s => (s.Ok ? "✓ " : "× ") + s.Label + (s.Message?.Length > 0 ? " — " + s.Message : ""))) : "";
                }
            }
            if (_groups.TryGetValue("passwords", out var passwords)) passwords.State(new[] { AdminSignInController.Local, "signup.enabled", "password_reset.self_service_enabled" }.Any(Controller.Dirty), "");
            if (_groups.TryGetValue("sso", out var sso)) sso.State(Controller.Dirty(AdminSignInController.EmailMatch) || Controller.Providers.Any(p => !p.Network && Controller.ProviderDirty(p)), T("page.ssoDescription"));
            if (_groups.TryGetValue("keys", out var keys)) keys.State(Controller.Dirty(AdminSignInController.UserKeys), "");
        }
        finally { _sync = false; }
    }
    private void Build()
    {
        _sync = true;
        try
        {
            AdminAccessPageUi.ClearPrivateControls(_ui.Fields); _ui.Fields.Children.Clear(); _settings.Clear(); _settingRows.Clear(); _groups.Clear(); _providerFields.Clear(); _auto.Clear(); _binding.Clear(); _test.Clear(); _results.Clear(); _usernames.Clear();
            _built = true; _builtProviders = Controller.Providers; _builtAccounts = Controller.Accounts;
            if (Controller.ProviderError != null) _ui.Fields.Children.Add(U.Text(Controller.ProviderError, 13));
            if (Controller.Capabilities is { Available: false }) _ui.Fields.Children.Add(U.Text(T("page.externalUnavailable"), 13, true));
            var passwords = new AdminSettingsGroup(T("page.passwordsGroup")); _groups["passwords"] = passwords; _ui.Fields.Children.Add(passwords);
            AddSetting(passwords, AdminSignInController.Local, "allowPassword");
            var admins = Controller.Accounts.Where(a => a.Role == "admin" && a.Enabled && a.BreakGlass && a.PasswordLogin).ToArray();
            passwords.Add(U.Text(admins.Length > 0 ? T("page.breakGlassAdmins", new { count = admins.Length, names = string.Join(", ", admins.Select(a => a.Username)) }) : T("page.noBreakGlassStatus"), 12, true));
            var users = U.Action(T("page.manageInviteCodes"), "SignInInviteCodes"); users.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/users?tab=invite-codes"); passwords.Add(users);
            AddSetting(passwords, "signup.enabled", "publicSignups"); AddSetting(passwords, "password_reset.self_service_enabled", "passwordReset");
            passwords.Add(new Expander { Header = T("page.lockedOutTitle"), Content = U.Text(T("page.lockedOutNote", new { login_path = "/login?local=1", command = "silo auth local-login enable" }), 12, true), HorizontalAlignment = HorizontalAlignment.Stretch });
            if (Controller.AccountCursor != null)
            {
                var more = U.Action("Load more accounts", "SignInMoreAccounts"); more.Click += async (_, _) => { await Controller.MoreAccountsAsync(); _built = false; Render(); }; passwords.Add(more);
            }
            var sso = new AdminSettingsGroup(T("page.ssoGroup"), T("page.ssoDescription")); _groups["sso"] = sso; _ui.Fields.Children.Add(sso);
            var providers = Controller.Providers.Where(p => !p.Network).ToArray();
            if (providers.Length == 0)
            {
                sso.Add(U.Text(T("provider.slot.noPlugins"), 13, true));
                var catalog = U.Action("Plugin catalog", "SignInPluginCatalog"); catalog.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/plugins?tab=catalog"); sso.Add(catalog);
            }
            if (providers.Length > 0)
            {
                var fallback = providers.FirstOrDefault(p => p.Binding?.Enabled == true) ?? providers.FirstOrDefault(p => p.GlobalConfigs.Count > 0) ?? providers[0];
                if (_pickedProvider != "none" && !providers.Any(p => p.Id == _pickedProvider)) _pickedProvider = fallback.Id;
                var choices = new RadioButtons { Header = T("provider.slot.groupLabel"), MaxColumns = 3 };
                foreach (var p in providers) choices.Items.Add(new RadioButton { Content = ProviderTitle(p, providers), Tag = p.Id });
                choices.Items.Add(new RadioButton { Content = T("provider.slot.noneTitle"), Tag = "none" });
                choices.SelectedIndex = choices.Items.OfType<RadioButton>().Select((b, i) => (b, i)).First(pair => pair.b.Tag as string == _pickedProvider).i;
                choices.SelectionChanged += (_, _) =>
                {
                    if (_sync || Controller.Busy) return;
                    _pickedProvider = (choices.SelectedItem as RadioButton)?.Tag as string; _built = false; Render();
                };
                sso.Add(choices);
                var active = providers.FirstOrDefault(p => p.Binding?.Enabled == true);
                if (active != null && providers.Length > 1) sso.Add(U.Text(T("provider.slot.oneAtATime", new { name = ProviderTitle(active, providers) }), 12, true));
                if (providers.FirstOrDefault(p => p.Id == _pickedProvider) is { } selected) sso.Add(ProviderPanel(selected));
                else if (active != null)
                {
                    var off = U.Action(T("provider.none.turnOffActive", new { name = active.Name }), "SignInNoneTurnOff");
                    sso.Add(U.Text(T("provider.none.activeOn", new { name = active.Name }), 13)); sso.Add(off);
                    off.Click += async (_, _) =>
                    {
                        var owner = Controller.Owner;
                        if (await _ui.ConfirmAsync(T("provider.binding.turnOffTitle", new { name = active.Name }), T(Controller.SavedValue(AdminSignInController.Local) == "false" ? "provider.binding.turnOffDescriptionBreakGlass" : "provider.binding.turnOffDescription", new { name = active.Name }), T("provider.binding.turnOffConfirm")) && ReferenceEquals(owner, Controller.Owner)) await Controller.SetBindingAsync(active, false, null, true);
                    };
                }
                else sso.Add(U.Text(T("provider.none.description"), 13, true));
            }
            if (!Controller.ProvidersKnown || providers.Length > 0 || Controller.Value(AdminSignInController.EmailMatch) == "true" || Controller.Dirty(AdminSignInController.EmailMatch))
            {
                AddSetting(sso, AdminSignInController.EmailMatch, "emailMatch"); sso.Add(U.Text(T("page.emailMatchWarning"), 12, true));
            }
            var network = Controller.Providers.Where(p => p.Network).ToArray();
            if (network.Length > 0)
            {
                var group = new AdminSettingsGroup(T("network.group"), T("network.description")); foreach (var provider in network) group.Add(ProviderPanel(provider)); _ui.Fields.Children.Add(group);
            }
            var apiKeys = new AdminSettingsGroup(T("page.apiKeysGroup")); _groups["keys"] = apiKeys; AddSetting(apiKeys, AdminSignInController.UserKeys, "userApiKeys"); _ui.Fields.Children.Add(apiKeys);
        }
        finally { _sync = false; }
    }
    private void AddSetting(AdminSettingsGroup group, string key, string label)
    {
        var toggle = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
        AutomationProperties.SetAutomationId(toggle, "SignIn_" + key); AutomationProperties.SetName(toggle, T("page." + label + "Label"));
        _settings[key] = toggle; var row = new AdminSettingsRow(T("page." + label + "Label"), T("page." + label + "Description"), toggle);
        _settingRows[key] = row;
        row.State(Controller.Dirty(key), Controller.RestartKeys.Has(key)); group.Add(row);
        if (Controller.Managed.Contains(key)) row.AddStatus(U.Text("Managed by environment", 12, true));
        toggle.Toggled += (_, _) => { if (!_sync) Controller.Set(key, toggle.IsOn); };
    }
    private FrameworkElement ProviderPanel(AdminSignInProvider provider)
    {
        var capability = provider.Auth!; var panel = new StackPanel { Spacing = 12 };
        var missing = AdminSignInSchemaRules.Missing(provider);
        var title = ProviderTitle(provider, Controller.Providers);
        var state = !provider.Enabled ? "pluginOff" : provider.Binding?.Enabled == true ? missing.Count > 0 ? "onNeedsSetup" : "on" : missing.Count > 0 ? "needsSetup" : "off";
        panel.Children.Add(U.Text(title + " · " + T("provider.state." + state), 16));
        if (missing.Count > 0) panel.Children.Add(U.Text(T(provider.Binding?.Enabled == true ? "provider.missing.enabled" : "provider.missing.disabled", new { fields = string.Join(", ", missing) }), 12, true));
        var binding = U.Action(provider.Binding?.Enabled == true ? T("provider.binding.turnOffConfirm") : "Turn on", "SignInBinding_" + provider.Id); _binding[provider.Id] = binding;
        binding.Click += async (_, _) =>
        {
            var on = provider.Binding?.Enabled != true; var owner = Controller.Owner;
            var text = on ? $"People will be able to sign in with {provider.Name}." : T(Controller.SavedValue(AdminSignInController.Local) == "false" ? "provider.binding.turnOffDescriptionBreakGlass" : "provider.binding.turnOffDescription", new { name = provider.Name });
            if (await _ui.ConfirmAsync(on ? "Turn on " + provider.Name + "?" : T("provider.binding.turnOffTitle", new { name = provider.Name }), text, on ? "Turn on" : T("provider.binding.turnOffConfirm")) && ReferenceEquals(owner, Controller.Owner)) await Controller.SetBindingAsync(provider, on, null, true);
        };
        panel.Children.Add(binding);
        var pluginPage = U.Action(T("provider.header.pluginPage"), "SignInPlugin_" + provider.Id); pluginPage.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/plugins/" + Uri.EscapeDataString(provider.PluginId)); panel.Children.Add(pluginPage);
        if (!provider.Enabled) panel.Children.Add(U.Text(T("provider.blocked.pluginOff"), 12, true));
        if (capability.SignInMode == "oauth")
        {
            panel.Children.Add(U.Text(T("provider.register.title"), 14)); panel.Children.Add(U.Text(T("provider.register.description"), 12, true));
            if (string.IsNullOrWhiteSpace(Controller.PublicUrl)) panel.Children.Add(U.Text(T("provider.register.publicUrlMissing"), 12, true));
            CopyUrl(panel, T("provider.register.redirectUri"), capability.CallbackUrl ?? provider.Binding?.CallbackUrl);
            CopyUrl(panel, T("provider.register.postLogoutUri"), capability.PostLogoutRedirectUrl ?? provider.Binding?.PostLogoutRedirectUrl);
        }
        foreach (var schema in provider.GlobalConfigSchema)
        {
            if (schema.AdminForm == null)
            {
                panel.Children.Add(U.Text(schema.Title + ": " + T("provider.unsupported"), 12, true));
                var configure = U.Action("Configure plugin", "SignInConfigure_" + provider.Id + "_" + schema.Key); configure.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/plugins/" + Uri.EscapeDataString(provider.PluginId)); panel.Children.Add(configure); continue;
            }
            var form = schema.AdminForm; var testField = capability.Metadata["connection_test_username_field"]?.ToString();
            if (schema.Key == "display_name" && form.Fields.Count == 1 && form.Fields[0].Key == "value")
            {
                panel.Children.Add(U.Text(T("provider.loginName.title"), 14));
                panel.Children.Add(FieldRow(provider, schema, form.Fields[0]));
                if (capability.SignInMode == "oauth") panel.Children.Add(U.Text(T("provider.loginName.previewButton", new { name = Controller.Values(provider, schema)["value"]?.ToString() ?? provider.Name }), 13));
                continue;
            }
            var sectioned = form.Sections.SelectMany(s => s.FieldKeys).ToHashSet();
            var loose = form.Fields.Where(f => !sectioned.Contains(f.Key) && schema.Key + "." + f.Key != testField).ToArray();
            if (loose.Length > 0) { panel.Children.Add(U.Text(schema.Title, 14)); if (schema.Description.Length > 0) panel.Children.Add(U.Text(schema.Description, 12, true)); foreach (var field in loose) panel.Children.Add(FieldRow(provider, schema, field)); }
            foreach (var section in form.Sections)
            {
                var fields = form.Fields.Where(f => section.FieldKeys.Contains(f.Key) && schema.Key + "." + f.Key != testField).ToArray(); if (fields.Length == 0) continue;
                var body = new StackPanel { Spacing = 8 }; if (section.Description.Length > 0) body.Children.Add(U.Text(section.Description, 12, true)); foreach (var field in fields) body.Children.Add(FieldRow(provider, schema, field));
                if (section.Collapsible) panel.Children.Add(new Expander { Header = section.Title, Content = body, IsExpanded = !section.CollapsedDefault || fields.Any(f => missing.Contains(f.Label)), HorizontalAlignment = HorizontalAlignment.Stretch });
                else { panel.Children.Add(U.Text(section.Title, 14)); panel.Children.Add(body); }
            }
        }
        var auto = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 }; _auto[provider.Id] = auto; AutomationProperties.SetName(auto, T(provider.Network ? "network.autoProvisionLabel" : "provider.autoProvision.label"));
        panel.Children.Add(new AdminSettingsRow(T(provider.Network ? "network.autoProvisionLabel" : "provider.autoProvision.label"), T(provider.Network ? "network.autoProvisionDescription" : "provider.autoProvision.description", new { name = provider.Name }), auto));
        auto.Toggled += async (_, _) =>
        {
            if (_sync) return;
            if (!provider.Network) { Controller.SetAutoProvision(provider, auto.IsOn); return; }
            var value = auto.IsOn; var owner = Controller.Owner;
            if (await _ui.ConfirmAsync(T("network.autoProvisionLabel"), T("network.autoProvisionDescription", new { name = provider.Name }), "Apply") && ReferenceEquals(owner, Controller.Owner)) await Controller.SetBindingAsync(provider, provider.Binding?.Enabled == true, value, true); else Render();
        };
        if (capability.Metadata["connection_test"]?.ToString() == "true" && Controller.Capabilities?.ConnectionTest == true)
        {
            panel.Children.Add(U.Text(T("provider.test.title"), 14)); panel.Children.Add(U.Text(T("provider.test.descriptionUnsaved"), 12, true));
            TextBox? username = null;
            if (capability.Metadata["connection_test_username_field"] != null)
            {
                username = U.Input(T("provider.test.lookUpLabel"), "SignInLookup_" + provider.Id, T("provider.test.usernamePlaceholder")); _usernames[provider.Id] = username;
                panel.Children.Add(username); panel.Children.Add(U.Text(T("provider.test.lookUpNote"), 12, true));
            }
            var test = U.Action(T("provider.test.run"), "SignInTest_" + provider.Id); _test[provider.Id] = test;
            test.Click += async (_, _) => await Controller.TestAsync(provider, username?.Text); panel.Children.Add(test);
            var result = U.Text("", 12); AutomationProperties.SetLiveSetting(result, AutomationLiveSetting.Polite); _results[provider.Id] = result; panel.Children.Add(result);
        }
        if (provider.Network)
        {
            var how = AdminAccessPageUi.Stack(U.Text(T("network.howWho", new { name = provider.Name, capability = "siloserver.org/cap/silo" }), 12, true),
                U.Text(T("network.howRoles", new { capability = "siloserver.org/cap/silo", admin_role = "{\"role\":\"admin\"}", user_role = "{\"role\":\"user\"}" }), 12, true),
                U.Text("\"grants\": [\n  {\"src\": [\"group:family-admins\"], \"dst\": [\"tag:silo\"],\n   \"app\": {\"siloserver.org/cap/silo\": [{\"role\": \"admin\"}]}},\n  {\"src\": [\"autogroup:member\", \"autogroup:shared\"], \"dst\": [\"tag:silo\"],\n   \"app\": {\"siloserver.org/cap/silo\": [{\"role\": \"user\"}]}}\n]", 12, true),
                U.Text(T("network.howSharedTv", new { name = provider.Name }), 12, true));
            panel.Children.Add(new Expander { Header = T("network.howTitle"), Content = how, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        return new Border { Child = panel, Padding = new(16), CornerRadius = new(16), Background = U.Brush("SecondaryBrush") };
    }
    private FrameworkElement FieldRow(AdminSignInProvider provider, AdminSignInSchema schema, AdminSignInField field)
    {
        var id = "SignIn_" + provider.Id + "_" + schema.Key + "_" + field.Key;
        Control input;
        if (field.IsSecret) input = U.Secret(field.Label, id);
        else if (field.Control == "SWITCH") input = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
        else if (field.Control == "SELECT") input = U.Select(field.Label, id, field.Options.Select(o => (o.Value, o.Label)));
        else if (field.Control == "MULTI_SELECT")
        {
            var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 180, MinWidth = 200 };
            foreach (var option in field.Options) list.Items.Add(new ListViewItem { Content = option.Label, Tag = option.Value }); input = list;
        }
        else if (field.Control == "TEXTAREA" || field.Multiline || AdminSignInSchemaRules.FieldType(schema, field) == "array") input = AdminAccessPageUi.Multiline(field.Label, id, "", Math.Clamp(field.Rows, 3, 10) * 24);
        else input = U.Input(field.Label, id, field.Placeholder, field.Control == "NUMBER");
        AutomationProperties.SetAutomationId(input, id); AutomationProperties.SetName(input, field.Label); AutomationProperties.SetHelpText(input, field.Description);
        var length = field.Validation is { MaxLength: > 0 } validation ? Math.Min(validation.MaxLength, 65536) : 65536;
        if (input is TextBox boundedText) boundedText.MaxLength = length;
        if (input is PasswordBox boundedSecret) boundedSecret.MaxLength = length;
        var row = new AdminSettingsRow(field.Label, field.Description, input); Button? clear = null;
        var savedSecret = provider.GlobalConfigs.FirstOrDefault(c => c.Key == schema.Key)?.ConfiguredSecrets.Contains(field.Key) == true;
        if (input is PasswordBox password) password.PlaceholderText = savedSecret ? T("configField.secretSavedPlaceholder") : field.Placeholder;
        if (field.IsSecret && savedSecret && !field.Required)
        {
            clear = U.Action(T("configField.clear"), id + "_clear"); clear.Click += (_, _) => Controller.ToggleClear(provider, schema, field); row.AddStatus(clear);
        }
        if (input is TextBox text) text.TextChanging += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, text.Text); };
        else if (input is PasswordBox secret) secret.PasswordChanged += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, secret.Password); };
        else if (input is ToggleSwitch toggle) toggle.Toggled += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, toggle.IsOn ? "true" : "false"); };
        else if (input is ComboBox select) select.SelectionChanged += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, AdminAccessPageUi.Selected(select)); };
        else if (input is ListView list) list.SelectionChanged += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, string.Join("\n", list.SelectedItems.OfType<ListViewItem>().Select(i => i.Tag))); };
        _providerFields.Add(new(provider, schema, field, row, input, clear)); return row;
    }
    private void CopyUrl(StackPanel panel, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        // Registration URLs are unsigned public addresses. Refuse credentials or signed query strings.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return;
        var text = U.Text(value, 12); text.IsTextSelectionEnabled = true; var copy = U.Action(T("provider.copy.button"), "SignInCopy_" + label);
        copy.Click += (_, _) => { try { var data = new DataPackage(); data.SetText(value); Clipboard.SetContent(data); } catch { _ui.Feedback.Text = T("provider.copy.failed"); } };
        panel.Children.Add(AdminAccessPageUi.Stack(U.Text(label, 12, true), text, copy));
    }
    private static string T(string suffix, object? values = null) => AdminText.T("web.admin.settings.signIn." + suffix, values);
    private static string ProviderTitle(AdminSignInProvider provider, IEnumerable<AdminSignInProvider> all) =>
        !provider.Network && all.Count(p => p.Auth?.SignInMode == provider.Auth?.SignInMode) == 1
            ? provider.Auth?.SignInMode switch { "oauth" => "OpenID Connect", "credentials" => "LDAP", _ => provider.Name } : provider.Name;
}
