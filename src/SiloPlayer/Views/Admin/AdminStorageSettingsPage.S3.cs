using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using R = SiloPlayer.Core.Services.Admin.AdminStorageRules;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminStorageSettingsPage
{
    /// <summary>
    /// Official SecretField: the Clear/Keep action sits before a masked input. Typing replaces, emptying keeps the saved
    /// value, and the saved credential itself is never loaded. <see cref="Synced"/> is the text last exchanged with the
    /// controller; PasswordChanged arrives asynchronously, so a box that differs from it holds typing not yet seen.
    /// </summary>
    private sealed class SecretEditor
    {
        internal readonly string Key;
        internal readonly PasswordBox Box;
        internal readonly Button Action;
        internal readonly AdminSettingsRow Row;
        internal readonly string Hint;
        internal string Synced = "";

        internal SecretEditor(AdminStorageSettingsPage page, string key, string label, string hint)
        {
            Key = key; Hint = hint;
            Box = U.Secret(label, "Storage_" + key);
            Action = U.Action(T("web.settings.secretField.clear"), "Storage_" + key + "_Action");
            Action.VerticalAlignment = VerticalAlignment.Center; Action.Visibility = Visibility.Collapsed;
            var editor = new Grid { ColumnSpacing = 12 };
            editor.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            editor.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            editor.Children.Add(Action); Grid.SetColumn(Box, 1); editor.Children.Add(Box);
            Row = new AdminSettingsRow(label, hint, editor, nameEditor: false);
            AutomationProperties.SetName(Box, label);
            AutomationProperties.SetLiveSetting(Row.Description, AutomationLiveSetting.Polite);
            Row.LayoutChanged += wide => Box.Width = wide ? 240 : double.NaN;
            Box.PasswordChanged += (_, _) =>
            {
                if (page._syncing || page._controller?.State is not { Loaded: true } state) return;
                Synced = Box.Password;
                // PasswordChanged also arrives later for programmatic updates; ignore that echo.
                if (Box.Password == state.Get(Key)) return;
                page._controller.SetSecret(Key, Box.Password);
            };
            Action.Click += (_, _) =>
            {
                if (page._controller?.State is not { Loaded: true } state) return;
                if (state.IsDirty(Key)) page._controller.KeepSecret(Key); else page._controller.ClearSecret(Key);
            };
        }

        internal void Render(AdminStorageSettingsState state, bool enabled, bool managed)
        {
            var value = state.Get(Key);
            if (Box.Password != value && Box.Password == Synced) { Box.Password = value; Synced = value; }
            Box.IsEnabled = enabled;
            var configured = state.Configured.Contains(Key);
            var clearStaged = state.IsDirty(Key) && value.Length == 0;
            Action.Visibility = configured && enabled && value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var label = T(clearStaged ? "web.settings.secretField.keep" : "web.settings.secretField.clear");
            if (Action.Content as string != label) { Action.Content = label; AutomationProperties.SetName(Action, label); }
            Box.PlaceholderText = clearStaged ? T("web.settings.secretField.willClear") : configured ? "••••••••••••" : T("web.settings.secretField.notConfigured");
            var description = managed ? T("configure.native.settings.managedByEnvironment")
                : clearStaged ? T("web.settings.secretField.clearedDescription")
                : configured ? T("web.settings.secretField.configuredDescription") : Hint;
            if (Row.Description.Text != description) { Row.Description.Text = description; AutomationProperties.SetName(Row.Description, description); }
            Row.Description.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(Box, description);
        }

        /// <summary>Retirement always clears private text, including a PasswordChanged event still in flight.</summary>
        internal void Clear() { Box.Password = ""; Synced = ""; Box.IsEnabled = false; Action.Visibility = Visibility.Collapsed; }
    }

    /// <summary>Official S3Group for public or private storage.</summary>
    private sealed class S3View
    {
        private readonly AdminStorageSettingsPage _page;
        private readonly bool _isPublic;
        private readonly IReadOnlyList<string> _keys;
        private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _descriptions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SecretEditor> _secrets = new(StringComparer.Ordinal);
        private readonly Border _note;
        private readonly TextBlock _noteText = U.Text("", 13, true);
        private readonly Button _check;
        private readonly TextBlock _checkResult = U.Text("", 14);
        private readonly Border _checkRow;
        private readonly ComboBox? _urlAuth;
        private readonly Button? _disclosure;
        private readonly Border? _rail;
        private readonly StackPanel? _advanced;
        private LoggingDisclosurePreferences? _preferences;
        private bool _persistedOpen, _wasForced, _open, _hover;
        private bool? _override;
        internal AdminSettingsGroup Group { get; }

        internal S3View(AdminStorageSettingsPage page, bool isPublic)
        {
            _page = page; _isPublic = isPublic;
            _keys = isPublic ? R.PublicS3Keys : R.PrivateS3Keys;
            var scope = isPublic ? "public" : "private";
            Group = new(T(isPublic ? "web.admin.settings.storage.publicGroup" : "web.admin.settings.storage.privateGroup"),
                T(isPublic ? "web.admin.settings.storage.publicGroupDescription" : "web.admin.settings.storage.privateGroupDescription"));
            AutomationProperties.SetAutomationId(Group, "Storage_" + scope);
            AddText(Key("endpoint"), "web.admin.settings.storage.s3.endpoint", "", "https://s3.us-east-1.amazonaws.com");
            AddText(Key("bucket"), "web.admin.settings.storage.s3.bucket", "");
            AddText(Key("key_prefix"), "web.admin.settings.storage.s3.keyPrefix", T("web.admin.settings.storage.s3.keyPrefixDescription"));

            // Location-change note (official settings-field-note: amber 20%/5%, 16 padding, 12 vertical margin).
            var title = U.Text(T("web.admin.settings.storage.s3.locationChange"), 13); title.FontWeight = FontWeights.Medium;
            title.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Amber500);
            AutomationProperties.SetAutomationId(_noteText, "Storage_" + scope + "_LocationNote");
            AutomationProperties.SetLiveSetting(_noteText, AutomationLiveSetting.Polite);
            var noteStack = new StackPanel { Spacing = 4 }; noteStack.Children.Add(title); noteStack.Children.Add(_noteText);
            var noteGrid = new Grid { ColumnSpacing = 12 };
            noteGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); noteGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var icon = AdminMonitorUi.Icon("alert-triangle", 16, AdminMonitorUi.Amber500); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 2, 0, 0);
            noteGrid.Children.Add(icon); Grid.SetColumn(noteStack, 1); noteGrid.Children.Add(noteStack);
            _note = new Border
            {
                Child = noteGrid, Padding = new(16), Margin = new(0, 12, 0, 12), CornerRadius = new(16), BorderThickness = new(1),
                BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, .2), Background = AdminMonitorUi.Solid(AdminMonitorUi.Amber500, .05),
                Visibility = Visibility.Collapsed,
            };
            Group.Add(_note);

            AddText(Key("region"), "web.admin.settings.storage.s3.region", T("web.admin.settings.storage.s3.regionDescription"));
            var pathStyle = new ToggleSwitch { OffContent = "", OnContent = "", MinWidth = 0 };
            Add(Key("path_style"), "web.admin.settings.storage.s3.pathStyle", pathStyle, T("web.admin.settings.storage.s3.pathStyleDescription"));
            pathStyle.Toggled += (_, _) => _page.SetText(Key("path_style"), pathStyle.IsOn ? "true" : "false");
            // Both keys carry Clear: staging the pair hands the bucket back to anonymous or instance-role access.
            AddSecret(Key("access_key"), "web.admin.settings.storage.s3.accessKey", "");
            AddSecret(Key("secret_key"), "web.admin.settings.storage.s3.secretKey", "");

            _check = U.Action(T("web.admin.common.connectionCheck.check"), "Storage_" + scope + "_Check", secondary: true);
            _check.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetLiveSetting(_checkResult, AutomationLiveSetting.Polite);
            AutomationProperties.SetAutomationId(_checkResult, "Storage_" + scope + "_CheckResult");
            _checkRow = new Border { Child = new AdminSettingsActionContent(_check, _checkResult), Padding = new(0, 14, 0, 14) };
            Group.Add(_checkRow);
            _check.Click += async (_, _) => { if (_page._controller != null) await _page._controller.CheckConnectionAsync(scope); };

            if (!isPublic) return;
            _urlAuth = U.Select(T("web.admin.settings.storage.s3.urlAuth"), "Storage_" + R.UrlAuth,
            [
                ("presigned", T("web.admin.settings.storage.s3.urlAuthPresigned")),
                ("public", T("web.admin.settings.storage.s3.urlAuthPublic")),
                ("cloudflare_token", T("web.admin.settings.storage.s3.urlAuthCloudflare")),
            ]);
            Add(R.UrlAuth, "web.admin.settings.storage.s3.urlAuth", _urlAuth, T("web.admin.settings.storage.s3.urlAuthDescription"));
            _urlAuth.SelectionChanged += (_, _) => { if ((_urlAuth.SelectedItem as ComboBoxItem)?.Tag is string value) _page.SetText(R.UrlAuth, value); };

            _advanced = new StackPanel();
            _disclosure = NewDisclosure();
            var railFields = new StackPanel();
            _rail = new Border
            {
                Child = railFields, BorderThickness = new(2, 0, 0, 0), Margin = new(6, 0, 0, 0), Padding = new(15, 0, 0, 0),
                BorderBrush = AdminMonitorUi.Solid(Windows.UI.Color.FromArgb(255, 167, 139, 250), .32), Visibility = Visibility.Collapsed,
            };
            _disclosure.Click += (_, _) => ToggleAdvanced();
            _advanced.Children.Add(_disclosure); _advanced.Children.Add(_rail);
            Group.Add(_advanced);
            AddText("s3.public_read_endpoint", "web.admin.settings.storage.s3.readEndpoint", "", "https://cdn.example.com", railFields);
            // Cloudflare token auth requires this secret, so a clear saves only alongside a switch to another mode.
            AddSecret("s3.public_token_secret", "web.admin.settings.storage.s3.tokenSecret", T("web.admin.settings.storage.s3.tokenSecretHint"), railFields);
            AddText(R.TokenParam, "web.admin.settings.storage.s3.tokenParam", T("web.admin.settings.storage.s3.tokenParamDescription"), parent: railFields);
            AddText(R.TokenTtl, "web.admin.settings.storage.s3.tokenTtl", "", parent: railFields, number: true, unit: T("web.admin.settings.storage.unitSeconds"));
        }

        private string Key(string suffix) => $"s3.{(_isPublic ? "public" : "private")}_{suffix}";

        private void AddText(string key, string labelKey, string description, string hint = "", StackPanel? parent = null, bool number = false, string? unit = null)
        {
            var label = T(labelKey);
            var input = U.Input(label, "Storage_" + key, hint, number);
            Add(key, labelKey, input, description, parent, unit);
            input.TextChanging += (_, _) => _page.SetText(key, input.Text);
            input.LostFocus += (_, _) => _page.QueueRender();
        }

        private void Add(string key, string labelKey, Control input, string description, StackPanel? parent = null, string? unit = null)
        {
            AutomationProperties.SetAutomationId(input, "Storage_" + key);
            var row = new AdminSettingsRow(T(labelKey), description, input, unit);
            _rows[key] = row; _controls[key] = input; _descriptions[key] = description;
            if (parent != null) parent.Children.Add(row); else Group.Add(row);
        }

        private void AddSecret(string key, string labelKey, string hint, StackPanel? parent = null)
        {
            var editor = new SecretEditor(_page, key, T(labelKey), hint);
            _secrets[key] = editor; _rows[key] = editor.Row;
            if (parent != null) parent.Children.Add(editor.Row); else Group.Add(editor.Row);
        }

        internal void LoadDisclosure(LoggingDisclosurePreferences preferences)
        {
            if (!_isPublic) return;
            _preferences = preferences;
            try { _persistedOpen = preferences.Read("storage.s3.public"); } catch { _persistedOpen = false; }
            _override = null; _wasForced = false;
        }

        private void ToggleAdvanced()
        {
            var next = !_open;
            _override = next; _persistedOpen = next;
            try { _preferences?.Write("storage.s3.public", next); } catch { /* A disclosure preference is a convenience only. */ }
            if (_page._controller is { } controller) _page.Render(controller.State);
        }

        internal void Render(AdminStorageSettingsState state, AdminArtworkStorageStatus status, bool visible, string? lockedBackend, bool publicBackendChangePending, bool editable)
        {
            Group.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            bool Managed(string key) => state.ManagedByEnv.Contains(key);
            var identity = _isPublic ? R.PublicS3IdentityKeys : R.PrivateS3IdentityKeys;
            _page._syncing = true;
            try
            {
                foreach (var (key, control) in _controls)
                {
                    var raw = state.Get(key);
                    var value = DisplayValue(key, raw);
                    if (control is TextBox text)
                    {
                        // Keep a focused raw draft (including empty text) until editing ends. A changed
                        // controller value still wins, so Discard and Reload can replace the draft.
                        var typing = text.FocusState != FocusState.Unfocused && text.Text == raw;
                        if (text.Text != value && !typing) text.Text = value;
                    }
                    else if (control is ToggleSwitch toggle) toggle.IsOn = value == "true";
                    else if (control is ComboBox select) SetSelected(select, value);
                    var enabled = editable && !Managed(key) && (!identity.Contains(key) || (!state.Saving && state.StatusKnown));
                    control.IsEnabled = enabled;
                    Describe(_rows[key], control, Managed(key) ? T("configure.native.settings.managedByEnvironment") : _descriptions[key]);
                }
                foreach (var (key, secret) in _secrets) secret.Render(state, editable && !state.Saving && !Managed(key), Managed(key));
            }
            finally { _page._syncing = false; }

            var noteVisible = identity.Any(state.IsDirty);
            _note.Visibility = noteVisible ? Visibility.Visible : Visibility.Collapsed;
            if (noteVisible) U.UpdateLiveText(_noteText, T(R.LocationNoteKey(_isPublic, lockedBackend, publicBackendChangePending)));

            var allRestart = _keys.All(state.RestartKeys.Has);
            var description = T(_isPublic ? "web.admin.settings.storage.publicGroupDescription" : "web.admin.settings.storage.privateGroupDescription");
            Group.State(_keys.Any(state.IsDirty), allRestart ? T("web.admin.settings.fields.restartAllWithDescription", new { description }) : description);
            foreach (var (key, row) in _rows) row.State(!_secrets.ContainsKey(key) && state.IsDirty(key), !allRestart && state.RestartKeys.Has(key));

            var checking = _isPublic ? state.CheckingPublic : state.CheckingPrivate;
            var result = _isPublic ? state.PublicCheck : state.PrivateCheck;
            _check.Content = T(checking ? "web.admin.common.connectionCheck.checking" : "web.admin.common.connectionCheck.check");
            AutomationProperties.SetName(_check, _check.Content.ToString());
            _check.IsEnabled = editable && !state.Saving && !checking;
            U.UpdateLiveText(_checkResult, result?.Message ?? "");
            _checkResult.Foreground = AdminMonitorUi.Solid(result?.Success == true ? Green600 : Red600);

            if (_isPublic) RenderAdvanced(state);
            Group.RefreshRules();
        }

        private void RenderAdvanced(AdminStorageSettingsState state)
        {
            var urlAuth = R.DisplayUrlAuth(state.Get(R.UrlAuth));
            var count = R.PublicAdvancedCount(urlAuth);
            var forced = R.CountDirty(state, R.PublicS3AdvancedKeys) > 0;
            // Official AdvancedSection: a newly forced reason clears the session override.
            if (forced != _wasForced) { _wasForced = forced; if (forced) _override = null; }
            _open = _override ?? (_persistedOpen || forced);
            _advanced!.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _rail!.Visibility = count > 0 && _open ? Visibility.Visible : Visibility.Collapsed;
            var cloudflare = urlAuth == "cloudflare_token";
            foreach (var key in new[] { "s3.public_token_secret", R.TokenParam, R.TokenTtl }) _rows[key].Visibility = cloudflare ? Visibility.Visible : Visibility.Collapsed;
            RenderDisclosure(count);
        }

        private Button NewDisclosure()
        {
            var template = (ControlTemplate)XamlReader.Load("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                  <Border x:Name="DisclosureRoot" Background="Transparent" Padding="{TemplateBinding Padding}"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <ContentPresenter Content="{TemplateBinding Content}" HorizontalContentAlignment="Stretch" VerticalAlignment="Center"/>
                  </Border>
                </ControlTemplate>
                """);
            var button = new Button
            {
                Template = template, MinWidth = 0, MinHeight = 0, Padding = new(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, UseSystemFocusVisuals = true,
                FocusVisualPrimaryBrush = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("FocusVisualPrimaryBrush", Microsoft.UI.Colors.White), .5),
                FocusVisualPrimaryThickness = new(3), FocusVisualSecondaryBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                CornerRadius = new(6),
            };
            AutomationProperties.SetAutomationId(button, "StorageAdvanced");
            button.PointerEntered += (_, _) => { _hover = true; RenderDisclosure(R.PublicAdvancedCount(R.DisplayUrlAuth(_page._controller?.State.Get(R.UrlAuth) ?? ""))); };
            button.PointerExited += (_, _) => { _hover = false; RenderDisclosure(R.PublicAdvancedCount(R.DisplayUrlAuth(_page._controller?.State.Get(R.UrlAuth) ?? ""))); };
            return button;
        }

        private void RenderDisclosure(int count)
        {
            if (_disclosure == null) return;
            var title = T("web.settings.advancedSection.title");
            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var chevron = AdminMonitorUi.Icon(_open ? "chevron-down" : "chevron-right", 14, _open ? U.Accent : U.Brush("SecondaryTextBrush"));
            chevron.VerticalAlignment = VerticalAlignment.Center; content.Children.Add(chevron);
            var label = U.Text(title, 13); label.FontWeight = FontWeights.Medium; label.TextWrapping = TextWrapping.NoWrap;
            label.Foreground = _open || _hover ? U.Brush("PrimaryTextBrush") : U.Brush("SecondaryTextBrush");
            Grid.SetColumn(label, 1); content.Children.Add(label);
            var number = U.Text(AdminText.FormatNumber(count), 12, true); number.TextWrapping = TextWrapping.NoWrap;
            AutomationProperties.SetAccessibilityView(number, AccessibilityView.Raw);
            Grid.SetColumn(number, 2); content.Children.Add(number);
            _disclosure.Content = content;
            AutomationProperties.SetName(_disclosure, T("web.settings.advancedSection.accessibleLabel",
                new Dictionary<string, object?> { ["title"] = title, ["count"] = (long)count, ["count_text"] = AdminText.FormatNumber(count) }));
            AutomationProperties.SetItemStatus(_disclosure, _open ? "expanded" : "collapsed");
        }

        internal void ClearSecrets() { foreach (var secret in _secrets.Values) secret.Clear(); }

        internal void Clear()
        {
            foreach (var control in _controls.Values)
            {
                control.IsEnabled = false;
                if (control is TextBox text) text.Text = ""; else if (control is ToggleSwitch toggle) toggle.IsOn = false; else if (control is ComboBox select) select.SelectedIndex = -1;
            }
            ClearSecrets();
            _checkResult.Text = ""; _note.Visibility = Visibility.Collapsed;
            _override = null; _wasForced = false; _open = false;
            if (_rail != null) _rail.Visibility = Visibility.Collapsed;
            Group.Visibility = Visibility.Collapsed;
        }
    }
}
