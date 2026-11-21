using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Windows.UI.ViewManagement;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminSignInSettingsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private sealed record ProviderField(AdminSignInProvider Provider, AdminSignInSchema Schema, AdminSignInField Field, AdminSettingsRow Row, Control Input, Button? Clear, TextBlock? Status, bool Managed);
    private readonly AdminAccessPageUi _ui;
    private readonly Dictionary<string, ToggleSwitch> _settings = [];
    private readonly Dictionary<string, AdminSettingsRow> _settingRows = [];
    private readonly Dictionary<string, AdminSettingsGroup> _groups = [];
    private readonly List<ProviderField> _providerFields = [];
    private readonly Dictionary<string, ToggleSwitch> _auto = [], _networkEnabled = [];
    private readonly Dictionary<string, TextBox> _usernames = [];
    /// <summary>What depends on the staged edits or the last answer (the Turn on button, the unsaved note, the test step) is brought up to date here on every render; nothing is recreated.</summary>
    private readonly List<Action<bool>> _syncs = [];
    private readonly AdminSettingsSaveBar _saveBar = new("SignIn");
    private IReadOnlyList<AdminSignInProvider>? _builtProviders;
    private AdminSignInCapabilities? _builtCapabilities;
    private string? _builtProviderError;
    private bool _builtProvidersKnown;
    private TextBlock? _breakGlass;
    private Button? _loadMore;
    private bool _built, _sync;
    private string? _pickedProvider, _testingProvider;
    private const string Capability = "siloserver.org/cap/silo", AdminRole = "{\"role\":\"admin\"}", UserRole = "{\"role\":\"user\"}";
    private const string GrantExample = "\"grants\": [\n  {\"src\": [\"group:family-admins\"], \"dst\": [\"tag:silo\"],\n   \"app\": {\"siloserver.org/cap/silo\": [{\"role\": \"admin\"}]}},\n  {\"src\": [\"autogroup:member\", \"autogroup:shared\"], \"dst\": [\"tag:silo\"],\n   \"app\": {\"siloserver.org/cap/silo\": [{\"role\": \"user\"}]}}\n]";
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
            if (await ConfirmAsync(N("saveTitle"), N("saveBody"), AdminText.T("common.save")) && ReferenceEquals(owner, Controller.Owner)) await Controller.SaveAsync(true);
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
    private bool PageRtl => FlowDirection == FlowDirection.RightToLeft;

    private void Render()
    {
        if (!Controller.Current) { _built = false; _builtProviders = null; _builtCapabilities = null; _builtProviderError = null; _saveBar.State(0, false, false); return; }
        // Only the first load or a new answer from the provider read (its list, the capabilities, its error, whether the list is known) rebuilds the form: those land on Open
        // and after a write, while inputs are disabled. A new account list updates just what depends on it, so a box being typed in is never replaced.
        if (Controller.Loaded && (!_built || !ReferenceEquals(_builtProviders, Controller.Providers) || !ReferenceEquals(_builtCapabilities, Controller.Capabilities) || _builtProviderError != Controller.ProviderError || _builtProvidersKnown != Controller.ProvidersKnown)) Build();
        else if (_built) SyncAccounts();
        var editable = Controller.Loaded && !Controller.Busy && !Controller.ReviewRequired;
        _saveBar.State(Controller.DirtyCount, Controller.Busy, editable); _saveBar.SetAvailableWidth(ActualWidth);
        _sync = true;
        try
        {
            foreach (var (key, control) in _settings)
            {
                control.IsOn = key == AdminSignInController.Local ? Controller.Value(key) != "false" : Controller.Value(key) == "true";
                control.IsEnabled = editable && !Controller.Managed.Contains(key) && (key != AdminSignInController.Local || Controller.ProvidersKnown);
                _settingRows[key].State(Controller.Dirty(key), Controller.RestartKeys.Has(key));
            }
            foreach (var entry in _providerFields)
            {
                var values = Controller.Values(entry.Provider, entry.Schema); var value = values[entry.Field.Key];
                var text = value is JsonArray array ? string.Join("\n", array.Select(n => n?.ToString() ?? "")) : value?.ToString() ?? "";
                var clearing = Controller.Clearing(entry.Provider, entry.Schema.Key, entry.Field.Key);
                entry.Row.Visibility = AdminSignInSchemaRules.Visible(entry.Schema.AdminForm!, entry.Field, values) ? Visibility.Visible : Visibility.Collapsed;
                entry.Input.IsEnabled = editable && !entry.Managed;
                entry.Row.State(!entry.Managed && !entry.Field.IsSecret && Controller.FieldDirty(entry.Provider, entry.Schema.Key, entry.Field.Key), false);
                // Text being typed stays as typed while it still means the staged value ("admins" + Enter, "Silo ", "1."), or is not a value yet ("-") in the focused box.
                if (entry.Input is TextBox input && AdminAccessPageUi.Lines(input.Text) != text && !(AdminSignInSchemaRules.Means(entry.Schema, entry.Field, input.Text, value) ?? input.FocusState != FocusState.Unfocused)) input.Text = text;
                else if (entry.Input is PasswordBox password && password.Password != text) password.Password = text;
                else if (entry.Input is ToggleSwitch toggle) toggle.IsOn = text == "true";
                else if (entry.Input is ComboBox select) AdminAccessPageUi.Select(select, text);
                else if (entry.Input is ListView list && value is JsonArray selected)
                {
                    list.SelectedItems.Clear(); foreach (var item in list.Items.OfType<ListViewItem>()) if (selected.Any(n => n?.ToString() == item.Tag as string)) list.SelectedItems.Add(item);
                }
                if (entry.Input is PasswordBox secret)
                {
                    var saved = entry.Provider.GlobalConfigs.FirstOrDefault(c => c.Key == entry.Schema.Key)?.ConfiguredSecrets.Contains(entry.Field.Key) == true;
                    // Official: the saved-secret hint shows while the secret is kept or managed, not while it is staged to be cleared.
                    secret.PlaceholderText = saved && (entry.Managed || !clearing) ? AdminText.T("web.admin.settings.signIn.configField.secretSavedPlaceholder") : entry.Field.Placeholder;
                }
                if (entry.Input is TextBox shown) ApplyDirection(shown, shown.Text, entry.Field.Placeholder, false);
                if (entry.Clear != null)
                {
                    entry.Clear.Content = T(clearing ? "configField.keep" : "configField.clear"); entry.Clear.IsEnabled = editable;
                    if (entry.Status != null) entry.Status.Text = T(clearing ? "configField.willClear" : "configField.saved");
                }
            }
            foreach (var provider in Controller.Providers)
                if (_auto.TryGetValue(provider.Id, out var auto)) { auto.IsOn = Controller.AutoProvision(provider); auto.IsEnabled = editable && !provider.BindingFieldManaged("auto_provision"); }
            foreach (var sync in _syncs) sync(editable);
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
            AdminAccessPageUi.ClearPrivateControls(_ui.Fields); _ui.Fields.Children.Clear(); _settings.Clear(); _settingRows.Clear(); _groups.Clear(); _providerFields.Clear(); _auto.Clear(); _networkEnabled.Clear(); _usernames.Clear(); _syncs.Clear(); _breakGlass = null; _loadMore = null;
            // An unavailable or failed read leaves the provider list the same empty instance, so what Build draws from it is recorded beside the list.
            _built = true; _builtProviders = Controller.Providers; _builtCapabilities = Controller.Capabilities; _builtProviderError = Controller.ProviderError; _builtProvidersKnown = Controller.ProvidersKnown;
            if (Controller.ProviderError != null) _ui.Fields.Children.Add(U.Text(Controller.ProviderError, 13));
            if (Controller.Capabilities is { Available: false }) _ui.Fields.Children.Add(U.Text(T("page.externalUnavailable"), 13, true));
            var passwords = new AdminSettingsGroup(T("page.passwordsGroup")); _groups["passwords"] = passwords; _ui.Fields.Children.Add(passwords);
            var invite = U.Action(T("page.manageInviteCodes"), "SignInInviteCodes"); invite.BorderThickness = new(0); invite.Height = 28; invite.Padding = new(8, 0, 8, 0); invite.FontSize = 12;
            invite.Foreground = U.Brush("SecondaryTextBrush"); invite.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = T("page.manageInviteCodes"), FontSize = 12 }, WebUiIcon.Create("arrow-right", 12, U.Brush("SecondaryTextBrush")) } };
            invite.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/users?tab=invite-codes"); passwords.SetAction(invite);
            AddSetting(passwords, AdminSignInController.Local, "allowPassword");
            AddSetting(passwords, "signup.enabled", "publicSignups"); AddSetting(passwords, "password_reset.self_service_enabled", "passwordReset");
            passwords.Add(new Expander { Header = T("page.lockedOutTitle"), Content = U.Text(T("page.lockedOutNote", new { login_path = "/login?local=1", command = "silo auth local-login enable" }), 12, true), HorizontalAlignment = HorizontalAlignment.Stretch });
            SyncAccounts();
            var sso = new AdminSettingsGroup(T("page.ssoGroup"), T("page.ssoDescription")); _groups["sso"] = sso; _ui.Fields.Children.Add(sso);
            var providers = Controller.Providers.Where(p => !p.Network).ToArray();
            if (!Controller.ProvidersKnown && Controller.ProviderError == null)
            {
                // The installed plugins are still being read (official: a skeleton): say so instead of claiming that none is installed.
                var loading = U.Text(T("page.loading"), 13, true); loading.Margin = new(0, 14, 0, 14); AutomationProperties.SetAutomationId(loading, "SignInProvidersLoading"); sso.Add(loading);
            }
            else if (Controller.ProviderError != null && !Controller.ProvidersKnown) sso.Add(ReadFailed());
            else if (providers.Length == 0)
            {
                var none = Linked(T("provider.slot.noPlugins"), N("link.catalog"), () => AdminShellPage.TryOpenHref("/admin/plugins?tab=catalog"), "SignInPluginCatalog", 13); none.Margin = new(0, 14, 0, 14); sso.Add(none);
            }
            else sso.Add(Slot(providers));
            if (!Controller.ProvidersKnown || providers.Length > 0 || Controller.Value(AdminSignInController.EmailMatch) == "true" || Controller.Dirty(AdminSignInController.EmailMatch))
            {
                AddSetting(sso, AdminSignInController.EmailMatch, "emailMatch"); sso.Add(U.Text(T("page.emailMatchWarning"), 12, true));
            }
            var network = Controller.Providers.Where(p => p.Network).ToArray();
            if (network.Length > 0)
            {
                var group = new AdminSettingsGroup(T("network.group"), T("network.description")); foreach (var provider in network) group.Add(NetworkRow(provider)); _ui.Fields.Children.Add(group);
            }
            var apiKeys = new AdminSettingsGroup(T("page.apiKeysGroup")); _groups["keys"] = apiKeys; AddSetting(apiKeys, AdminSignInController.UserKeys, "userApiKeys"); _ui.Fields.Children.Add(apiKeys);
        }
        finally { _sync = false; }
    }

    private FrameworkElement ReadFailed()
    {
        var retry = U.Action(N("retry"), "SignInRetry");
        retry.Click += async (_, _) =>
        {
            if (RequiresLeaveConfirmation && !await _ui.ConfirmDiscardAsync()) return;
            await Controller.OpenAsync();
        };
        var alert = U.Text(T("provider.slot.readFailed"), 13); alert.Foreground = new SolidColorBrush(AdminMonitorUi.Destructive); AutomationProperties.SetLiveSetting(alert, AutomationLiveSetting.Assertive);
        return AdminAccessPageUi.Stack(alert, retry);
    }

    /// <summary>Brings the break-glass status line and the "Load more accounts" button in line with the account list, in place: nothing else on the page is recreated.</summary>
    private void SyncAccounts()
    {
        if (!_groups.TryGetValue("passwords", out var passwords)) return;
        var fields = passwords.Fields.Children;
        // Like the official page, the status only speaks once the whole account list is known; a longer list leaves the server to decide.
        var known = Controller.AccountsKnown;
        var admins = known ? Controller.Accounts.Where(a => a.Role == "admin" && a.Enabled && a.BreakGlass && a.PasswordLogin).ToArray() : [];
        if (!known) { if (_breakGlass != null) fields.Remove(_breakGlass); _breakGlass = null; }
        else
        {
            if (_breakGlass == null) { _breakGlass = U.Text("", 13, true); _breakGlass.Margin = new(0, 0, 0, 8); fields.Insert(fields.IndexOf(_settingRows[AdminSignInController.Local]) + 1, _breakGlass); }
            _breakGlass.Inlines.Clear();
            if (admins.Length > 0)
            {
                _breakGlass.Foreground = U.Brush("SecondaryTextBrush");
                _breakGlass.Text = T("page.breakGlassAdmins", new { count = admins.Length, names = string.Join(", ", admins.Select(a => AdminSignInText.Plain(a.Username, 40))) });
            }
            else
            {
                // One sentence in one text element: the text, the link and the full stop never split into separate pieces of the layout.
                _breakGlass.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber400); var sentence = T("page.noBreakGlassStatus"); var link = N("link.userPage"); var at = sentence.IndexOf(link, StringComparison.Ordinal);
                if (at < 0) _breakGlass.Text = sentence;
                else
                {
                    _breakGlass.Inlines.Add(new Run { Text = sentence[..at] });
                    var anchor = new Hyperlink(); anchor.Inlines.Add(new Run { Text = link }); anchor.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/users"); _breakGlass.Inlines.Add(anchor);
                    _breakGlass.Inlines.Add(new Run { Text = sentence[(at + link.Length)..] });
                }
            }
            AutomationProperties.SetAutomationId(_breakGlass, "SignInBreakGlass");
        }
        if (Controller.AccountCursor == null) { if (_loadMore != null) fields.Remove(_loadMore); _loadMore = null; }
        else if (_loadMore == null)
        {
            _loadMore = U.Action(N("loadMoreAccounts"), "SignInMoreAccounts"); _loadMore.Click += async (_, _) => await Controller.MoreAccountsAsync(); passwords.Add(_loadMore);
        }
        passwords.RefreshRules();
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

    // ── Provider choice and the chosen provider's guided setup ──
    private (string Word, string Tone) State(AdminSignInProvider provider, IReadOnlyList<string> missing)
    {
        if (!provider.Enabled) return (T("provider.state.pluginOff"), "off");
        if (provider.Binding?.CallbackPending == true) return (AdminText.T("web.admin.plugins.provisioning.pending"), "warn");
        if (provider.Binding?.Enabled == true) return missing.Count > 0 ? (T("provider.state.onNeedsSetup"), "warn") : (T("provider.state.on"), "ok");
        return missing.Count > 0 ? (T("provider.state.needsSetup"), "warn") : (T("provider.state.off"), "off");
    }

    private static FrameworkElement StateDot(string tone) => new Border
    {
        Width = 8, Height = 8, CornerRadius = new(4), VerticalAlignment = VerticalAlignment.Center,
        Background = tone == "ok" ? new SolidColorBrush(Color.FromArgb(255, 0x10, 0xB9, 0x81)) : tone == "warn" ? new SolidColorBrush(Color.FromArgb(255, 0xF5, 0x9E, 0x0B)) : AdminMonitorUi.Solid(AdminMonitorUi.Muted, .4),
    };

    private FrameworkElement Slot(AdminSignInProvider[] providers)
    {
        var panel = new StackPanel { Spacing = 8 };
        var fallback = providers.FirstOrDefault(p => p.Binding?.Enabled == true) ?? providers.FirstOrDefault(p => p.GlobalConfigs.Count > 0) ?? providers[0];
        if (_pickedProvider != "none" && !providers.Any(p => p.Id == _pickedProvider)) _pickedProvider = fallback.Id;
        var choices = new RadioButtons { MaxColumns = 3 }; AutomationProperties.SetName(choices, T("provider.slot.groupLabel"));
        foreach (var p in providers)
        {
            var state = State(p, AdminSignInSchemaRules.Missing(p));
            var mode = p.Auth?.SignInMode; var detail = mode == "oauth" ? N("examples.oauth") : mode == "credentials" ? N("examples.credentials") : Plain(p.PluginName);
            var radio = new RadioButton { Content = Card(ProviderTitle(p, providers), detail, state), Tag = p.Id, MinWidth = 176, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(radio, ProviderTitle(p, providers) + ", " + state.Word); choices.Items.Add(radio);
        }
        var noneRadio = new RadioButton { Content = Card(T("provider.slot.noneTitle"), T("provider.slot.noneDetail"), null), Tag = "none", MinWidth = 176, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(noneRadio, T("provider.slot.noneTitle")); choices.Items.Add(noneRadio);
        var pickedIndex = choices.Items.OfType<RadioButton>().Select((b, i) => (b, i)).First(pair => pair.b.Tag as string == _pickedProvider).i;
        choices.SelectedIndex = pickedIndex;
        // The selection is asserted again once realized so the active provider is always shown as chosen.
        choices.Loaded += (_, _) => { var prior = _sync; _sync = true; try { choices.SelectedIndex = pickedIndex; } finally { _sync = prior; } };
        choices.SelectionChanged += (_, _) =>
        {
            // RadioButtons reports its own initial selection again once it is realized, after Build has finished. Only a different choice by the admin
            // changes the panel: answering the echo with another Build would recreate the form, and the echo of that one, without end.
            if (_sync || Controller.Busy || (choices.SelectedItem as RadioButton)?.Tag is not string picked || picked == _pickedProvider) return;
            _pickedProvider = picked; _built = false; Render();
        };
        panel.Children.Add(choices);
        var active = providers.FirstOrDefault(p => p.Binding?.Enabled == true);
        if (active != null && providers.Length > 1) panel.Children.Add(U.Text(T("provider.slot.oneAtATime", new { name = Plain(ProviderTitle(active, providers)) }), 12, true));
        if (providers.FirstOrDefault(p => p.Id == _pickedProvider) is { } selected)
            panel.Children.Add(ProviderPanel(selected, providers, active != null && active.Id != selected.Id ? Plain(ProviderTitle(active, providers)) : null));
        else panel.Children.Add(NoProviderPanel(active));
        return panel;
    }

    private static FrameworkElement Card(string title, string detail, (string Word, string Tone)? state)
    {
        var head = new Grid { ColumnSpacing = 8 }; head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var name = U.Text(title, 14); name.FontWeight = FontWeights.SemiBold; head.Children.Add(name);
        if (state is { } s)
        {
            var chip = new Border { CornerRadius = new(10), BorderThickness = new(1), BorderBrush = U.Brush("BorderBrush"), Padding = new(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
            chip.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { StateDot(s.Tone), U.Text(s.Word, 12, true) } };
            Grid.SetColumn(chip, 1); head.Children.Add(chip);
        }
        var card = new StackPanel { Spacing = 4 }; card.Children.Add(head); if (detail.Length > 0) card.Children.Add(U.Text(detail, 12, true));
        return card;
    }

    private FrameworkElement NoProviderPanel(AdminSignInProvider? active)
    {
        if (active == null) { var text = U.Text(T("provider.none.description"), 13, true); text.Margin = new(0, 20, 0, 0); return text; }
        var label = Plain(active.Name); var panel = new StackPanel { Spacing = 4, Margin = new(0, 20, 0, 0) };
        var row = new Grid { ColumnSpacing = 12 }; row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(U.Text(T("provider.none.activeOn", new { name = label }), 13, true));
        var off = U.Action(T("provider.none.turnOffActive", new { name = label }), "SignInNoneTurnOff"); Grid.SetColumn(off, 1); row.Children.Add(off); panel.Children.Add(row);
        var note = U.Text(AdminText.T("web.admin.plugins.provisioning.managed"), 13, true); note.Visibility = active.BindingFieldManaged("enabled") ? Visibility.Visible : Visibility.Collapsed; panel.Children.Add(note);
        off.Click += async (_, _) => await ConfirmTurnOff(active);
        _syncs.Add(editable => off.IsEnabled = editable && active.Binding?.Enabled == true && Controller.Capabilities is { Available: true, LiveProviderChanges: true } && !active.BindingFieldManaged("enabled"));
        return panel;
    }

    private async Task ConfirmTurnOff(AdminSignInProvider provider)
    {
        var owner = Controller.Owner; var label = Plain(provider.Name);
        var passwordsOn = Controller.SavedValue(AdminSignInController.Local) != "false";
        // Turning off the last way people sign in can lock them out: say so, and ask first.
        if (await ConfirmAsync(T("provider.binding.turnOffTitle", new { name = label }), T(passwordsOn ? "provider.binding.turnOffDescription" : "provider.binding.turnOffDescriptionBreakGlass", new { name = label }), T("provider.binding.turnOffConfirm"), destructive: true)
            && ReferenceEquals(owner, Controller.Owner)) await Controller.SetBindingAsync(provider, false, null, true);
    }

    private FrameworkElement ProviderPanel(AdminSignInProvider provider, AdminSignInProvider[] primary, string? otherActive)
    {
        var capability = provider.Auth!; var layout = AdminSignInLayout.Of(provider); var mode = capability.SignInMode;
        var missing = AdminSignInSchemaRules.Missing(provider); var title = ProviderTitle(provider, primary); var label = Plain(provider.Name);
        var state = State(provider, missing); var enabled = provider.Binding?.Enabled == true; var callbackPending = provider.Binding?.CallbackPending == true;
        var managedEnabled = provider.BindingFieldManaged("enabled");
        var panel = new StackPanel { Spacing = 0, Margin = new(0, 20, 0, 0) };

        // Header: name, state, and the two actions.
        var header = new Grid { ColumnSpacing = 12 }; header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var names = new StackPanel { Spacing = 4 };
        var heading = U.Text(title, 16); heading.FontWeight = FontWeights.SemiBold; AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3); names.Children.Add(heading);
        var stateLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var stateText = U.Text(state.Word, 13, true); AutomationProperties.SetAutomationId(stateText, "SignInState_" + provider.Id);
        stateLine.Children.Add(StateDot(state.Tone)); stateLine.Children.Add(stateText);
        if (enabled && provider.Enabled && !callbackPending)
        {
            stateLine.Children.Add(U.Text("·", 13, true));
            stateLine.Children.Add(U.Text(mode == "oauth" ? T("provider.header.oauthShown", new { name = label }) : T("provider.header.shownAs", new { name = label }), 13, true));
        }
        names.Children.Add(stateLine); header.Children.Add(names);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        var pluginPage = U.Action(T("provider.header.pluginPage"), "SignInPlugin_" + provider.Id); pluginPage.BorderThickness = new(0);
        pluginPage.Click += (_, _) => AdminShellPage.TryOpenHref("/admin/plugins/" + Uri.EscapeDataString(provider.PluginId)); actions.Children.Add(pluginPage);
        var binding = U.Action(enabled ? T("provider.header.turnOff") : T("provider.header.turnOn"), "SignInBinding_" + provider.Id, primary: !enabled);
        binding.Click += async (_, _) =>
        {
            var owner = Controller.Owner;
            if (enabled) { await ConfirmTurnOff(provider); return; }
            // Turning a provider on lets people sign in with it at once; ask first (the official page turns it on without asking).
            if (await ConfirmAsync(N("turnOnTitle", new { name = label }), N("turnOnBody", new { name = label }), T("provider.header.turnOn")) && ReferenceEquals(owner, Controller.Owner)) await Controller.SetBindingAsync(provider, true, null, true);
        };
        actions.Children.Add(binding); Grid.SetColumn(actions, 1); header.Children.Add(actions); panel.Children.Add(header);
        if (managedEnabled) { var note = U.Text(AdminText.T("web.admin.plugins.provisioning.managed"), 13, true); note.Margin = new(0, 4, 0, 0); AutomationProperties.SetAutomationId(note, "SignInManaged_" + provider.Id); panel.Children.Add(note); }
        var done = U.Text("", 13); done.Margin = new(0, 4, 0, 0); done.Visibility = Visibility.Collapsed; AutomationProperties.SetAutomationId(done, "SignInDone_" + provider.Id); AutomationProperties.SetLiveSetting(done, AutomationLiveSetting.Polite); panel.Children.Add(done);
        var blocked = U.Text("", 13, true); blocked.Margin = new(0, 4, 0, 0); AutomationProperties.SetAutomationId(blocked, "SignInBlocked_" + provider.Id); panel.Children.Add(blocked);
        if (!provider.Enabled) { var off = Linked(T("provider.blocked.pluginOff"), N("link.pluginPage"), () => AdminShellPage.TryOpenHref("/admin/plugins/" + Uri.EscapeDataString(provider.PluginId)), "SignInPluginOff_" + provider.Id, 13); off.Margin = new(0, 4, 0, 0); panel.Children.Add(off); }
        if (missing.Count > 0) { var line = U.Text(T(enabled ? "provider.missing.enabled" : "provider.missing.disabled", new { fields = string.Join(", ", missing.Select(m => Plain(m, 60))) }), 13); line.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber500); line.Margin = new(0, 4, 0, 0); panel.Children.Add(line); }

        // Numbered steps, in the official order: register, the plugin's own steps, the connection test, the login button.
        var steps = new List<(string Title, TextBlock? Description, FrameworkElement Body)>();
        CopyRow? redirect = null; var autoRow = AutoProvisionRow(provider);
        if (mode == "oauth")
        {
            var body = new StackPanel();
            if (string.IsNullOrWhiteSpace(Controller.PublicUrl) || callbackPending)
            {
                var warn = Linked(T("provider.register.publicUrlMissing"), N("link.generalSettings"), () => AdminShellPage.TryOpenHref("/admin/settings/general"), "SignInPublicUrl_" + provider.Id, 13); warn.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber400); warn.Margin = new(0, 14, 0, 14); body.Children.Add(warn);
            }
            if (provider.BindingFieldManaged("callback_mode")) { var note = U.Text(AdminText.T("web.admin.plugins.provisioning.managed"), 12, true); note.Margin = new(0, 8, 0, 8); body.Children.Add(note); }
            redirect = new CopyRow(this, T("provider.register.redirectUri"), T("provider.register.redirectUriCopy"), T("provider.register.redirectUriDescription"), "Redirect"); body.Children.Add(redirect.Row);
            var postLogout = new CopyRow(this, T("provider.register.postLogoutUri"), T("provider.register.postLogoutUriCopy"), T("provider.register.postLogoutUriDescription"), "PostLogout"); body.Children.Add(postLogout.Row);
            postLogout.Set(FirstUrl(capability.PostLogoutRedirectUrl, provider.Binding?.PostLogoutRedirectUrl));
            steps.Add((T("provider.register.title"), U.Text(T("provider.register.description"), 12, true), body));
        }
        for (var i = 0; i < layout.Steps.Count; i++)
        {
            var group = layout.Steps[i]; var body = new StackPanel();
            foreach (var field in group.Fields) body.Children.Add(FieldRow(provider, group.Schema, field));
            if (i == layout.Steps.Count - 1) body.Children.Add(autoRow);
            steps.Add((Plain(group.Title, 120), group.Description.Length > 0 ? U.Text(group.Description, 12, true) : null, body));
        }
        if (layout.Steps.Count == 0) steps.Add((T("provider.accounts.title"), null, autoRow));
        TextBlock? testDescription = null; StackPanel? testResult = null; Button? test = null;
        if (capability.Metadata["connection_test"]?.ToString() == "true" && Controller.Capabilities?.ConnectionTest == true)
        {
            var body = new StackPanel { Spacing = 12, Margin = new(0, 14, 0, 14) };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom };
            TextBox? username = null;
            if (layout.TestUsername is { } look)
            {
                var managed = provider.ConfigFieldManaged(look.Schema.Key, look.Field.Key);
                var holder = new StackPanel { Spacing = 4 }; holder.Children.Add(U.Text(T("provider.test.lookUpLabel"), 12, true));
                username = U.Input(T("provider.test.lookUpLabel"), "SignInLookup_" + provider.Id, T("provider.test.usernamePlaceholder")); username.Width = 224; username.HorizontalAlignment = HorizontalAlignment.Left; _usernames[provider.Id] = username;
                // The look-up user starts as the saved one, as on the official page; a user the deployment owns is shown read-only and never sent.
                if (provider.GlobalConfigs.FirstOrDefault(c => c.Key == look.Schema.Key)?.Value[look.Field.Key] is JsonValue saved && saved.TryGetValue<string>(out var savedName)) username.Text = savedName;
                username.IsEnabled = !managed; var lookUpBox = username; ApplyDirection(lookUpBox, lookUpBox.Text, T("provider.test.usernamePlaceholder"), false);
                lookUpBox.TextChanged += (_, _) => ApplyDirection(lookUpBox, lookUpBox.Text, T("provider.test.usernamePlaceholder"), false);
                holder.Children.Add(username); line.Children.Add(holder);
            }
            test = U.Action(T("provider.test.run"), "SignInTest_" + provider.Id, secondary: true); var testButton = test;
            test.Click += async (_, _) =>
            {
                _testingProvider = provider.Id; Render();
                try { await Controller.TestAsync(provider, username?.Text); } finally { _testingProvider = null; if (_ui.Active) Render(); }
            };
            line.Children.Add(test); body.Children.Add(line);
            if (layout.TestUsername is { } note2) body.Children.Add(U.Text(provider.ConfigFieldManaged(note2.Schema.Key, note2.Field.Key) ? AdminText.T("web.admin.plugins.provisioning.managed") : T("provider.test.lookUpNote"), 12, true));
            testResult = new StackPanel { Spacing = 8 }; AutomationProperties.SetLiveSetting(testResult, AutomationLiveSetting.Polite); body.Children.Add(testResult);
            testDescription = U.Text("", 12, true);
            steps.Add((T("provider.test.title"), testDescription, body));
            _syncs.Add(editable => testButton.IsEnabled = editable && provider.Enabled);
        }
        if (layout.LoginName is { } name)
        {
            var body = new StackPanel(); var oauth = mode == "oauth";
            var nameRow = FieldRow(provider, name.Schema, name.Field, T(mode == "credentials" ? "provider.loginName.directoryName" : "provider.loginName.providerName"), T(oauth ? "provider.loginName.oauthDescription" : "provider.loginName.description"));
            body.Children.Add(nameRow);
            if (oauth)
            {
                var pill = new Border { CornerRadius = new(10), BorderThickness = new(1), BorderBrush = U.Brush("BorderBrush"), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .1), Padding = new(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Left };
                var shown = U.Text("", 14); shown.FontWeight = FontWeights.Medium;
                pill.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { WebUiIcon.Create("lock", 16, U.Brush("PrimaryTextBrush")), shown } };
                AutomationProperties.SetAutomationId(shown, "SignInPreview_" + provider.Id);
                body.Children.Add(new AdminSettingsRow(T("provider.loginName.preview"), "", pill, nameEditor: false));
                _syncs.Add(_ =>
                {
                    var typed = Controller.Values(provider, name.Schema)[name.Field.Key]?.ToString()?.Trim();
                    var text = Plain(!string.IsNullOrEmpty(typed) ? typed : !string.IsNullOrEmpty(name.Field.Placeholder) ? name.Field.Placeholder : provider.Name, 60);
                    shown.Text = T("provider.loginName.previewButton", new { name = text }); AutomationProperties.SetName(pill, T("provider.loginName.previewSr", new { name = text }));
                });
            }
            steps.Add((T("provider.loginName.title"), null, body));
        }
        for (var i = 0; i < steps.Count; i++) panel.Children.Add(Step(i + 1, steps[i].Title, steps[i].Description, steps[i].Body));

        // Advanced: what most servers leave alone.
        if (layout.Advanced.Count > 0)
        {
            var advanced = new StackPanel { Spacing = 2, Margin = new(0, 24, 0, 0) };
            var advancedTitle = U.Text(T("provider.advanced.title"), 14); advancedTitle.FontWeight = FontWeights.SemiBold; AutomationProperties.SetHeadingLevel(advancedTitle, AutomationHeadingLevel.Level4);
            advanced.Children.Add(advancedTitle); advanced.Children.Add(U.Text(T("provider.advanced.description"), 12, true));
            foreach (var group in layout.Advanced)
            {
                var body = new StackPanel { Spacing = 0 }; if (group.Description.Length > 0) { var intro = U.Text(group.Description, 12, true); intro.Margin = new(0, 4, 0, 4); body.Children.Add(intro); }
                foreach (var field in group.Fields) body.Children.Add(FieldRow(provider, group.Schema, field));
                var attention = group.Fields.Any(f => Controller.FieldDirty(provider, group.Schema.Key, f.Key) || missing.Contains(f.Label));
                var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; head.Children.Add(U.Text(Plain(group.Title, 120), 13)); head.Children.Add(U.Text(group.Fields.Count.ToString(), 12, true));
                advanced.Children.Add(new Expander { Header = head, Content = body, IsExpanded = attention, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            panel.Children.Add(advanced);
        }
        if (layout.Unsupported.Count > 0)
        {
            var link = Linked(T("provider.unsupported"), N("link.pluginPage"), () => AdminShellPage.TryOpenHref("/admin/plugins/" + Uri.EscapeDataString(provider.PluginId)), "SignInUnsupported_" + provider.Id, 13); link.Margin = new(0, 16, 0, 0); panel.Children.Add(link);
        }

        // Everything that follows the staged edits or the last answer.
        _syncs.Add(editable =>
        {
            var unsaved = Controller.ProviderDirty(provider);
            var live = Controller.Capabilities is { Available: true, LiveProviderChanges: true };
            var turnOnBlocked = !provider.Enabled || otherActive != null || missing.Count > 0 || unsaved;
            binding.IsEnabled = editable && live && !managedEnabled && (enabled || !turnOnBlocked);
            var reason = !enabled ? !provider.Enabled ? "" : otherActive != null ? T("provider.blocked.otherActive", new { active = otherActive, provider = title }) : unsaved ? T("provider.blocked.unsaved") : "" : "";
            blocked.Text = reason; blocked.Visibility = reason.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (testDescription != null) testDescription.Text = T(unsaved ? "provider.test.descriptionUnsaved" : "provider.test.descriptionSaved");
            if (test != null) test.Content = _testingProvider == provider.Id ? T("provider.test.testing") : T("provider.test.run");
            if (testResult != null) RenderTest(testResult, Controller.TestResults.GetValueOrDefault(provider.Id), Controller.TestFailures.GetValueOrDefault(provider.Id));
            redirect?.Set(provider.Binding?.CallbackPending == true ? null : FirstUrl(capability.CallbackUrl, provider.Binding?.CallbackUrl, Controller.TestResults.GetValueOrDefault(provider.Id)?.CallbackUrl));
            if (Controller.LastChange is { } change && change.ProviderId == provider.Id)
            {
                done.Text = T(change.Enabled ? "provider.binding.turnedOn" : Controller.SavedValue(AdminSignInController.Local) == "false" ? "provider.binding.turnedOffBreakGlass" : "provider.binding.turnedOff", new { name = label });
                done.Visibility = Visibility.Visible;
            }
            else done.Visibility = Visibility.Collapsed;
        });
        return panel;
    }

    /// <summary>A numbered setup step: a 22px numbered badge and heading, an optional description, and the step's rows indented under it.</summary>
    private static FrameworkElement Step(int number, string title, TextBlock? description, FrameworkElement body)
    {
        var step = new StackPanel { Margin = new(0, 20, 0, 0) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var badge = new Border { Width = 22, Height = 22, CornerRadius = new(11), BorderThickness = new(1), BorderBrush = U.Brush("BorderBrush"), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .1), VerticalAlignment = VerticalAlignment.Center };
        var count = U.Text(number.ToString(), 12, true); count.FontWeight = FontWeights.SemiBold; count.HorizontalAlignment = HorizontalAlignment.Center; badge.Child = count;
        var heading = U.Text(title, 14); heading.FontWeight = FontWeights.SemiBold; AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level4);
        AutomationProperties.SetName(heading, AdminText.T("web.admin.settings.signIn.provider.step.heading", new { number, title }));
        head.Children.Add(badge); head.Children.Add(heading); step.Children.Add(head);
        if (description != null) { description.Margin = new(32, 4, 0, 0); step.Children.Add(description); }
        body.Margin = new(32, 0, 0, 0); step.Children.Add(body); return step;
    }

    private FrameworkElement AutoProvisionRow(AdminSignInProvider provider)
    {
        var managed = provider.BindingFieldManaged("auto_provision");
        var auto = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 }; _auto[provider.Id] = auto; AutomationProperties.SetName(auto, T("provider.autoProvision.label")); AutomationProperties.SetAutomationId(auto, "SignInAuto_" + provider.Id);
        var row = new AdminSettingsRow(T("provider.autoProvision.label"), T("provider.autoProvision.description"), auto);
        if (managed) row.AddStatus(U.Text(AdminText.T("web.admin.plugins.provisioning.managed"), 12, true));
        auto.Toggled += (_, _) => { if (!_sync && !managed) Controller.SetAutoProvision(provider, auto.IsOn); };
        _syncs.Add(_ => row.State(Controller.AutoProvision(provider) != (provider.Binding?.AutoProvision ?? true) && !managed, false));
        return row;
    }

    private FrameworkElement NetworkRow(AdminSignInProvider provider)
    {
        var label = Plain(provider.Name); var panel = new StackPanel();
        var enabled = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 }; _networkEnabled[provider.Id] = enabled; AutomationProperties.SetAutomationId(enabled, "SignInNetworkEnabled_" + provider.Id);
        var auto = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 }; _auto[provider.Id] = auto; AutomationProperties.SetAutomationId(auto, "SignInNetworkAuto_" + provider.Id);
        var enabledManaged = provider.BindingFieldManaged("enabled"); var autoManaged = provider.BindingFieldManaged("auto_provision");
        var enabledRow = new AdminSettingsRow(T("network.enableLabel", new { name = label }), T("network.enableDescription", new { name = label }), enabled);
        var autoRow = new AdminSettingsRow(T("network.autoProvisionLabel"), T("network.autoProvisionDescription", new { name = label }), auto);
        if (enabledManaged) enabledRow.AddStatus(U.Text(AdminText.T("web.admin.plugins.provisioning.managed"), 12, true));
        if (autoManaged) autoRow.AddStatus(U.Text(AdminText.T("web.admin.plugins.provisioning.managed"), 12, true));
        var done = U.Text("", 13); done.Visibility = Visibility.Collapsed; done.Margin = new(0, 8, 0, 0); AutomationProperties.SetAutomationId(done, "SignInDone_" + provider.Id); AutomationProperties.SetLiveSetting(done, AutomationLiveSetting.Polite);
        panel.Children.Add(done); panel.Children.Add(enabledRow); panel.Children.Add(autoRow);
        enabled.Toggled += async (_, _) =>
        {
            if (_sync || enabledManaged) return;
            var value = enabled.IsOn; var owner = Controller.Owner;
            // A change to who can sign in is confirmed first (the official page applies the switch at once); a declined or cancelled change puts the switch back.
            var accepted = value ? await ConfirmAsync(N("turnOnTitle", new { name = label }), N("turnOnBody", new { name = label }), T("provider.header.turnOn"))
                : await ConfirmAsync(T("provider.binding.turnOffTitle", new { name = label }), T(Controller.SavedValue(AdminSignInController.Local) == "false" ? "provider.binding.turnOffDescriptionBreakGlass" : "provider.binding.turnOffDescription", new { name = label }), T("provider.binding.turnOffConfirm"), destructive: true);
            if (accepted && ReferenceEquals(owner, Controller.Owner)) await Controller.SetBindingAsync(provider, value, null, true); else if (_ui.Active) Render();
        };
        auto.Toggled += async (_, _) =>
        {
            if (_sync || autoManaged) return;
            var value = auto.IsOn; var owner = Controller.Owner;
            if (await ConfirmAsync(T("network.autoProvisionLabel"), T("network.autoProvisionDescription", new { name = label }), N("apply")) && ReferenceEquals(owner, Controller.Owner)) await Controller.SetBindingAsync(provider, provider.Binding?.Enabled == true, value, true); else if (_ui.Active) Render();
        };
        _syncs.Add(editable =>
        {
            var live = Controller.Capabilities is { Available: true, LiveProviderChanges: true };
            enabled.IsOn = provider.Binding?.Enabled == true; enabled.IsEnabled = editable && live && provider.Enabled && !enabledManaged;
            auto.IsOn = provider.Binding?.AutoProvision ?? true; auto.IsEnabled = editable && live && !autoManaged;
            if (Controller.LastChange is { } change && change.ProviderId == provider.Id)
            {
                done.Text = T(change.AutoProvision is { } value ? value ? "network.autoProvisionOn" : "network.autoProvisionOff" : change.Enabled ? "network.turnedOn" : "network.turnedOff", new { name = label }); done.Visibility = Visibility.Visible;
            }
            else done.Visibility = Visibility.Collapsed;
        });
        var how = AdminAccessPageUi.Stack(U.Text(T("network.howWho", new { name = label, capability = Capability }), 12, true),
            U.Text(T("network.howRoles", new { capability = Capability, admin_role = AdminRole, user_role = UserRole }), 12, true),
            U.Text(GrantExample, 12, true),
            Linked(T("network.howSharedTv", new { name = label }), N("link.pluginSettings", new { name = label }), () => AdminShellPage.TryOpenHref("/admin/plugins/" + Uri.EscapeDataString(provider.PluginId)), "SignInNetworkPlugin_" + provider.Id, 12));
        ((TextBlock)how.Children[2]).FontFamily = new FontFamily("Consolas"); ((TextBlock)how.Children[2]).FlowDirection = FlowDirection.LeftToRight; ((TextBlock)how.Children[2]).IsTextSelectionEnabled = true;
        panel.Children.Add(new Expander { Header = T("network.howTitle"), Content = how, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(0, 8, 0, 0) });
        return panel;
    }

    private FrameworkElement FieldRow(AdminSignInProvider provider, AdminSignInSchema schema, AdminSignInField field, string? label = null, string? description = null)
    {
        var id = "SignIn_" + provider.Id + "_" + schema.Key + "_" + field.Key; var shownLabel = label ?? (string.IsNullOrEmpty(field.Label) ? field.Key : field.Label); var shownDescription = description ?? field.Description;
        var managed = provider.ConfigFieldManaged(schema.Key, field.Key);
        Control input;
        switch (AdminSignInSchemaRules.EditorOf(schema, field))
        {
            case AdminSignInEditor.Secret: input = U.Secret(shownLabel, id); break;
            case AdminSignInEditor.Switch: input = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 }; break;
            case AdminSignInEditor.Select: input = U.Select(shownLabel, id, field.Options.Select(o => (o.Value, o.Label))); break;
            case AdminSignInEditor.MultiSelect:
                var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 180, MinWidth = 200 };
                foreach (var option in field.Options) list.Items.Add(new ListViewItem { Content = option.Label, Tag = option.Value }); input = list; break;
            case AdminSignInEditor.Multiline: input = AdminAccessPageUi.Multiline(shownLabel, id, "", Math.Clamp(field.Rows, 3, 10) * 24); break;
            default: input = U.Input(shownLabel, id, field.Placeholder, field.Control == "NUMBER"); break;
        }
        AutomationProperties.SetAutomationId(input, id); AutomationProperties.SetName(input, shownLabel); AutomationProperties.SetHelpText(input, shownDescription);
        var length = field.Validation is { MaxLength: > 0 } validation ? Math.Min(validation.MaxLength, 65536) : 65536;
        if (input is TextBox boundedText) boundedText.MaxLength = length;
        if (input is PasswordBox boundedSecret) boundedSecret.MaxLength = length;
        var row = new AdminSettingsRow(shownLabel, shownDescription, input); Button? clear = null; TextBlock? status = null;
        var savedSecret = provider.GlobalConfigs.FirstOrDefault(c => c.Key == schema.Key)?.ConfiguredSecrets.Contains(field.Key) == true;
        if (input is PasswordBox password) { password.PlaceholderText = savedSecret ? T("configField.secretSavedPlaceholder") : field.Placeholder; password.FlowDirection = FlowDirection.LeftToRight; }
        if (managed) row.AddStatus(U.Text(AdminText.T("web.admin.plugins.provisioning.managed"), 12, true));
        else if (field.IsSecret && savedSecret && !field.Required)
        {
            status = U.Text(T("configField.saved"), 12, true); AutomationProperties.SetAutomationId(status, id + "_status");
            clear = U.Action(T("configField.clear"), id + "_clear"); clear.Height = 24; clear.FontSize = 12; clear.BorderThickness = new(0); clear.Click += (_, _) => Controller.ToggleClear(provider, schema, field);
            row.AddStatus(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { status, clear } });
        }
        if (input is TextBox text)
        {
            text.TextChanging += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, AdminAccessPageUi.Lines(text.Text)); };
            text.LostFocus += (_, _) => { if (!_sync && _ui.Active) Render(); }; // text left behind that is not a value goes back to the staged one.
            ApplyDirection(text, "", field.Placeholder, false);
        }
        else if (input is PasswordBox secret) secret.PasswordChanged += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, secret.Password); };
        else if (input is ToggleSwitch toggle) toggle.Toggled += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, toggle.IsOn ? "true" : "false"); };
        else if (input is ComboBox select) select.SelectionChanged += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, AdminAccessPageUi.Selected(select)); };
        else if (input is ListView list) list.SelectionChanged += (_, _) => { if (!_sync) Controller.SetField(provider, schema, field, string.Join("\n", list.SelectedItems.OfType<ListViewItem>().Select(i => i.Tag))); };
        _providerFields.Add(new(provider, schema, field, row, input, clear, status, managed)); return row;
    }

    /// <summary>Sign-in setup values (issuer URLs, client ids, bind DNs, filters) are machine strings: on a right-to-left page they read left to right (official <c>textDirection</c>).</summary>
    private void ApplyDirection(TextBox box, string text, string? placeholder, bool alwaysLeftToRight)
    {
        switch (AdminSignInText.DirectionFor(PageRtl, text, placeholder, alwaysLeftToRight))
        {
            case AdminSignInDirection.LeftToRight: box.FlowDirection = FlowDirection.LeftToRight; box.TextReadingOrder = TextReadingOrder.UseFlowDirection; break;
            case AdminSignInDirection.RightToLeft: box.FlowDirection = FlowDirection.RightToLeft; box.TextReadingOrder = TextReadingOrder.UseFlowDirection; break;
            // dir="auto": the first letter decides, so a URL or client id reads left to right while text typed in Arabic or Hebrew stays right to left.
            case AdminSignInDirection.Auto: box.FlowDirection = AdminSignInText.StartsRightToLeft(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; box.TextReadingOrder = TextReadingOrder.UseFlowDirection; break;
        }
    }

    private static string? FirstUrl(params string?[] candidates) => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <summary>Shows what a connection test found: a heading with the failure count and each step with its result; or why the test did not run.</summary>
    private static void RenderTest(StackPanel host, AdminSignInTestResult? result, string? failure)
    {
        host.Children.Clear();
        if (result != null)
        {
            var failed = result.Steps.Count(s => !s.Ok);
            var heading = U.Text(result.Ok ? T("provider.test.allPassed") : failed > 0 ? T("provider.test.someFailed", new { total = result.Steps.Count, failed }) : T("provider.test.didNotPass"), 13);
            heading.FontWeight = FontWeights.Medium; heading.Foreground = result.Ok ? new SolidColorBrush(Color.FromArgb(255, 0x16, 0xA3, 0x4A)) : new SolidColorBrush(AdminMonitorUi.Destructive);
            AutomationProperties.SetAutomationId(heading, "SignInTestHeading"); host.Children.Add(heading);
            var list = new StackPanel { Spacing = 6 }; AutomationProperties.SetName(list, T("provider.test.stepsLabel"));
            foreach (var step in result.Steps)
            {
                var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                var icon = WebUiIcon.Create(step.Ok ? "check" : "x", 14, step.Ok ? new SolidColorBrush(Color.FromArgb(255, 0x16, 0xA3, 0x4A)) : new SolidColorBrush(AdminMonitorUi.Destructive)); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 3, 0, 0);
                var texts = new StackPanel { Spacing = 2 }; var title = U.Text(AdminSignInText.Plain(step.Label, 200), 13); title.FontWeight = FontWeights.Medium; AutomationProperties.SetName(title, T(step.Ok ? "provider.test.stepPassed" : "provider.test.stepFailed", new { label = AdminSignInText.Plain(step.Label, 200) })); texts.Children.Add(title);
                if (!string.IsNullOrEmpty(step.Message)) texts.Children.Add(U.Text(AdminSignInText.Plain(step.Message, 1000), 12, true));
                Grid.SetColumn(texts, 1); row.Children.Add(icon); row.Children.Add(texts); list.Children.Add(row);
            }
            host.Children.Add(list);
        }
        if (failure != null)
        {
            var error = U.Text(failure, 13); error.Foreground = new SolidColorBrush(AdminMonitorUi.Destructive); AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive); AutomationProperties.SetAutomationId(error, "SignInTestError"); host.Children.Add(error);
        }
    }

    /// <summary>A read-only address the admin registers at the provider, with a Copy button. What is copied is exactly the visible text; an address with credentials or a signed query is neither shown nor copyable.</summary>
    private sealed class CopyRow
    {
        private readonly AdminSignInSettingsPage _page;
        private readonly TextBox _text;
        private readonly TextBlock _status = U.Text("", 12, true);
        internal AdminSettingsRow Row { get; }
        internal string Value => _text.Text;
        internal CopyRow(AdminSignInSettingsPage page, string label, string copyLabel, string description, string id)
        {
            _page = page;
            _text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 12, FlowDirection = FlowDirection.LeftToRight, MinWidth = 0, Width = 320, Padding = new(12, 8, 12, 8), CornerRadius = new(10), Background = U.Brush("AppBackgroundBrush"), IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
            AutomationProperties.SetAutomationId(_text, "SignInUrl_" + id); AutomationProperties.SetName(_text, label);
            _text.GotFocus += (_, _) => _text.SelectAll();
            var copy = U.Action(T("provider.copy.button"), "SignInCopy_" + id, secondary: true); AutomationProperties.SetName(copy, copyLabel); copy.Margin = new(0, 8, 0, 0);
            copy.Click += (_, _) =>
            {
                // Exactly what is visible goes to the clipboard, and is kept out of the clipboard history and the roaming clipboard.
                try { var data = new DataPackage(); data.SetText(_text.Text); Clipboard.SetContentWithOptions(data, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false }); _status.Text = T("provider.copy.copied"); }
                catch { _status.Text = T("provider.copy.failed"); }
            };
            var editor = new StackPanel { Spacing = 0 }; editor.Children.Add(_text); editor.Children.Add(copy);
            Row = new AdminSettingsRow(label, description, editor, nameEditor: false); Row.AddStatus(_status); Row.Visibility = Visibility.Collapsed;
        }
        internal void Set(string? value)
        {
            // Registration URLs are unsigned public addresses. Refuse credentials or signed query strings.
            var ok = !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
            var shown = ok ? value! : "";
            if (_text.Text != shown) { _text.Text = shown; _status.Text = ""; }
            Row.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>A sentence with one link in it, as one text element: the official rich-text messages put a link inside a sentence.</summary>
    private static TextBlock Linked(string sentence, string link, Action open, string id, double size = 12)
    {
        var block = U.Text("", size, true); AutomationProperties.SetAutomationId(block, id);
        var at = sentence.IndexOf(link, StringComparison.Ordinal);
        if (at < 0) { block.Text = sentence; return block; }
        block.Inlines.Add(new Run { Text = sentence[..at] });
        var anchor = new Hyperlink(); anchor.Inlines.Add(new Run { Text = link }); anchor.Click += (_, _) => open(); block.Inlines.Add(anchor);
        block.Inlines.Add(new Run { Text = sentence[(at + link.Length)..] });
        return block;
    }

    /// <summary>A confirmation that holds the page's lease; a destructive one (turning a sign-in method off) uses the official destructive fill.</summary>
    private async Task<bool> ConfirmAsync(string title, string description, string action, bool destructive = false)
    {
        var dialog = new ContentDialog { Title = title, Content = U.Text(description), PrimaryButtonText = action, CloseButtonText = AdminText.T("common.cancel"), DefaultButton = ContentDialogButton.Close };
        if (destructive) AdminSignInDestructiveFill.Apply(dialog);
        return await _ui.ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    private static string T(string suffix, object? values = null) => AdminText.T("web.admin.settings.signIn." + suffix, values);
    private static string N(string suffix, object? values = null) => AdminText.T("signIn.native." + suffix, values);
    private static string Plain(string? text, int max = 60) => AdminSignInText.Plain(text, max);
    /// <summary>What the page calls a sign-in plugin: its protocol ("OpenID Connect") when it is the only one installed for that protocol, else the plugin's name.</summary>
    private static string ProviderTitle(AdminSignInProvider provider, IEnumerable<AdminSignInProvider> primary)
    {
        var mode = provider.Auth?.SignInMode; var protocol = mode switch { "oauth" => N("protocol.oauth"), "credentials" => N("protocol.credentials"), _ => null };
        return protocol != null && primary.Count(p => p.Auth?.SignInMode == mode) == 1 ? protocol : Plain(provider.PluginName);
    }
}

/// <summary>
/// The official destructive fill (<c>--destructive-fill</c> #dc2626, white text, 90% on hover) for the confirm button of a dialog that turns a sign-in method off.
/// In a high-contrast theme the system colors are used, and a contrast change while the dialog is open is followed. The shared destructive style and the theme are not touched.
/// </summary>
internal static class AdminSignInDestructiveFill
{
    private static readonly Color Fill = Color.FromArgb(255, 0xDC, 0x26, 0x26);
    internal static void Apply(ContentDialog dialog)
    {
        Update(dialog);
        AccessibilitySettings? settings = null;
        void Changed(AccessibilitySettings changed, object _) => dialog.DispatcherQueue.TryEnqueue(() => Update(dialog));
        // Following a contrast change needs the system's settings object; without one (no shell attached) the dialog keeps the colors it opened with.
        try { settings = new AccessibilitySettings(); settings.HighContrastChanged += Changed; } catch { settings = null; }
        dialog.Closed += (_, _) => { try { if (settings != null) settings.HighContrastChanged -= Changed; } catch { } };
        dialog.Opened += (_, _) => Update(dialog);
    }
    internal static bool HighContrast() { try { return new AccessibilitySettings().HighContrast; } catch { return false; } }
    internal static Color Blend(Color top, Color bottom, double opacity) => Color.FromArgb(255, (byte)Math.Round(top.R * opacity + bottom.R * (1 - opacity)), (byte)Math.Round(top.G * opacity + bottom.G * (1 - opacity)), (byte)Math.Round(top.B * opacity + bottom.B * (1 - opacity)));
    private static (Color Fill, Color Text) Palette(bool highContrast)
    {
        if (highContrast)
        {
            try { var system = new UISettings(); return (system.UIElementColor(UIElementType.Highlight), system.UIElementColor(UIElementType.HighlightText)); } catch { }
        }
        return (Fill, Microsoft.UI.Colors.White);
    }
    private static void Update(ContentDialog dialog)
    {
        var highContrast = HighContrast(); var (fill, text) = Palette(highContrast);
        // Official: the fill at 90% on hover (Blend over the dialog's surface), 80% pressed; a contrast theme keeps the one system color in every state.
        var surface = AdminMonitorUi.ColorOf("SurfaceColor", Color.FromArgb(255, 0x15, 0x1E, 0x2B));
        var hover = highContrast ? fill : Blend(fill, surface, .9); var pressed = highContrast ? fill : Blend(fill, surface, .8);
        static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var template = (ControlTemplate)XamlReader.Load($$"""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="DestructiveRoot" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
                <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
                  <VisualState x:Name="Normal"/>
                  <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="DestructiveRoot.Background" Value="{{Hex(hover)}}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="DestructiveRoot.Background" Value="{{Hex(pressed)}}"/></VisualState.Setters></VisualState>
                  <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="DestructiveRoot.Opacity" Value="0.5"/></VisualState.Setters></VisualState>
                </VisualStateGroup></VisualStateManager.VisualStateGroups>
                <ContentPresenter Content="{TemplateBinding Content}" Foreground="{TemplateBinding Foreground}" FontSize="{TemplateBinding FontSize}" FontWeight="{TemplateBinding FontWeight}"
                  HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate>
            """);
        // A page-local style: the shared destructive style and the theme are not touched.
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(fill)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(text)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(fill)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 8, 16, 8)));
        style.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty, true));
        dialog.PrimaryButtonStyle = style;
    }
}
