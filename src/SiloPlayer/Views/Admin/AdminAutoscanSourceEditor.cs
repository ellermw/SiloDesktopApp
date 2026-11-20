using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminAutoscanPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>Descriptor-driven add flow and persistent edit tabs. No animation, saved secrets or write replay.</summary>
internal sealed partial class AdminAutoscanSourceEditor : Grid
{
    private readonly AdminAutoscanSourcesPage _page;
    private readonly AdminAutoscanController _controller;
    private readonly MonitorAuthorityLease _owner;
    private readonly AdminAutoscanSource? _baseline;
    private readonly Grid _layout = new() { TabFocusNavigation = KeyboardNavigationMode.Cycle };
    private readonly Border _panel = new() { CornerRadius = new(12), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _content = new() { Spacing = 20, Padding = new(24) };
    private readonly StackPanel _sections = new() { Spacing = 20 };
    // A Panel has no IsEnabled; the host disables every section control while a write or read is in flight.
    private readonly ContentControl _sectionsHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _footer = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(24, 12, 24, 16), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _title = U.Text("", 20);
    private readonly TextBlock _description = U.Text("", 13, true);
    private readonly TextBlock _error = U.Text("", 13);
    private readonly TextBlock _trail = U.Text("", 12, true);
    private readonly Button _save = U.Action("", "AutoscanEditorSave", primary: true);
    private readonly Button _cancel = U.Action("Cancel", "AutoscanEditorCancel");
    private readonly List<PasswordBox> _secrets = [];
    private readonly List<(string Key, FrameworkElement Body)> _tabBodies = [];
    private readonly CancellationTokenSource _reads = new();
    private AdminAutoscanAvailableSource? _plugin;
    private AdminAutoscanEditorDraft? _draft;
    private string? _createdId, _createdProvider;
    private bool _closed, _saving, _uncertain;
    private long _generation;
    private Control? _opener;
    internal bool Dirty { get; private set; }
    internal bool Busy => _saving || _connectionSaving;
    internal event Action? CloseRequested;

    internal AdminAutoscanSourceEditor(AdminAutoscanSourcesPage page, AdminAutoscanController controller, MonitorAuthorityLease owner, AdminAutoscanSource? source)
    {
        _page = page; _controller = controller; _owner = owner; _sectionsHost.Content = _sections;
        // A detached configuration baseline detects a refreshed row before replacing it.
        _baseline = source == null ? null : new AdminAutoscanSource
        {
            Id = source.Id, PluginId = source.PluginId, CapabilityId = source.CapabilityId,
            ConnectionId = source.ConnectionId, DeliveryMode = source.DeliveryMode, Enabled = source.Enabled,
            PollIntervalSeconds = source.PollIntervalSeconds, Label = source.Label,
            PathRewrites = source.PathRewrites.Select(r => new AdminAutoscanPathRewrite { From = r.From, To = r.To }).ToList(), SourceConfig = new(source.SourceConfig),
        };
        AutomationProperties.SetAutomationId(this, "AutoscanEditor"); Background = AdminMonitorUi.Solid(Microsoft.UI.Colors.Black, .5);
        _panel.Background = U.Brush("AppBackgroundBrush"); _panel.BorderBrush = U.Brush("BorderBrush"); _panel.Child = _layout; Children.Add(_panel);
        _layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); _layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel { Spacing = 8, Padding = new(24, 20, 24, 0) }; heading.Children.Add(_title); heading.Children.Add(_description); _layout.Children.Add(heading);
        AutomationProperties.SetHeadingLevel(_title, AutomationHeadingLevel.Level2);
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive); AutomationProperties.SetAutomationId(_error, "AutoscanEditorError"); _error.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        _content.Children.Add(_trail); _content.Children.Add(_tabs);
        if (source == null) BuildPluginChoices();
        _content.Children.Add(_sectionsHost); _content.Children.Add(_error);
        var scroll = new ScrollViewer { Content = _content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); _layout.Children.Add(scroll);
        if (source != null)
        {
            var delete = U.Action(T("sourceDialog.deleteSource"), "AutoscanEditorDelete"); delete.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
            delete.Click += async (_, _) => { if (Busy) return; var result = await _page.DeleteAsync(source.Id, _owner); if (Current && result is { Succeeded: false }) _error.Text = result.Message ?? ""; }; _footer.Children.Add(delete);
        }
        _footer.Children.Add(_cancel); _footer.Children.Add(_save); Grid.SetRow(_footer, 2); _layout.Children.Add(_footer);
        _cancel.Click += (_, _) => { if (!Busy) CloseRequested?.Invoke(); };
        _save.Click += async (_, _) => await SaveAsync();
        KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Escape && !Busy) { args.Handled = true; CloseRequested?.Invoke(); } };
        SizeChanged += (_, _) => Fit(); Loaded += (_, _) => { Fit(); FocusFirst(); };
        if (source != null)
        {
            _plugin = controller.State.Available.FirstOrDefault(p => p.PluginId == source.PluginId && p.CapabilityId == source.CapabilityId);
            _draft = new(source, P.Descriptor(_plugin)); BuildSections();
        }
        _title.Text = source == null ? T("sourceDialog.addTitle") : T("sourceDialog.editTitle", ("name", P.Title(source, controller.State.Available)));
        _description.Text = source == null ? T("sourceDialog.addDescription") : string.Join(" · ", new[] { P.Subtitle(source, controller.State), controller.State.OptionsReady ? T("sources.editFeeds", ("feeds", P.DescribeTargets(P.Targets(source, P.Descriptor(_plugin), controller.State.Libraries)))) : null }.Where(part => !string.IsNullOrEmpty(part)));
        SyncState();
    }
    private static string T(string key, params (string Name, object? Value)[] values) => AdminAutoscanText.T(key, values);
    private bool Current => !_closed && _controller.IsCurrent(_owner);
    private void Changed() { Dirty = true; _generation++; _testOutcome.Text = ""; }
    private void Fit()
    {
        _panel.Width = Math.Max(1, Math.Min(672, ActualWidth - (ActualWidth < 640 ? 16 : 48)));
        _panel.Height = Math.Max(1, Math.Min(760, ActualHeight - 32));
        _footer.Orientation = _panel.Width < 440 ? Orientation.Vertical : Orientation.Horizontal;
        _tabs.Orientation = _panel.Width < 420 ? Orientation.Vertical : Orientation.Horizontal;
    }
    internal void FocusFirst() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!Current || !IsLoaded) return;
        _opener ??= FocusManager.GetFocusedElement(XamlRoot) as Control;
        // The first enabled, visible control in reading order (no tree-search focus API: it faults on a root that is mid-layout).
        Descendants<Control>(_layout).FirstOrDefault(c => c.IsTabStop && c.IsEnabled && c.Visibility == Visibility.Visible && c.IsLoaded && c is not ScrollViewer)?.Focus(FocusState.Programmatic);
    });
    private void BuildPluginChoices()
    {
        var choices = new StackPanel { Spacing = 8 }; choices.Children.Add(U.Text(T("sourceDialog.pluginLabel"), 14));
        if (_controller.State.Available.Count == 0) choices.Children.Add(U.Text(T("sourceDialog.noPlugins"), 13, true));
        foreach (var plugin in _controller.State.Available)
        {
            var descriptor = P.Descriptor(plugin); var label = new StackPanel { Spacing = 4 };
            label.Children.Add(U.Text(plugin.DisplayName, 14)); label.Children.Add(U.Text(descriptor.Summary.Length > 0 ? descriptor.Summary : plugin.Description, 12, true));
            var button = U.Action(plugin.DisplayName, "AutoscanPlugin_" + plugin.PluginId + "_" + plugin.CapabilityId); button.Content = label; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) =>
            {
                if (!Current || Busy) return; var retainedLabel = _draft?.Label ?? ""; _draft?.Dispose(); ClearSecretControls();
                _plugin = plugin; _draft = new(null, descriptor) { Label = retainedLabel }; _createdId = null; _uncertain = false; Changed();
                if (_draft.DeliveryMode == "webhook") _draft.Mappings = P.SeedMappings(P.Provider(descriptor), _controller.State.Libraries);
                BuildSections(); SyncState();
            };
            choices.Children.Add(button);
        }
        _content.Children.Add(choices);
    }
    private void BuildSections()
    {
        _generation++; ClearSecretControls(preserveDraft: true); _sections.Children.Clear(); _tabs.Children.Clear(); _tabBodies.Clear();
        if (_draft is not { } draft) return;
        if (_baseline == null && draft.Descriptor.DeliveryModes.Count > 1)
        {
            var delivery = new StackPanel { Spacing = 8 }; delivery.Children.Add(U.Text(T("sourceDialog.deliveryLabel"), 14));
            foreach (var mode in draft.Descriptor.DeliveryModes.Where(m => m is "poll" or "webhook"))
            {
                var choice = new RadioButton { Content = T("sourceDialog.delivery." + mode + "Title"), IsChecked = draft.DeliveryMode == mode, GroupName = "AutoscanDelivery" };
                AutomationProperties.SetAutomationId(choice, "AutoscanDelivery_" + mode);
                choice.Checked += (_, _) =>
                {
                    if (!Current || Busy || draft.DeliveryMode == mode) return;
                    draft.DeliveryMode = mode; Changed();
                    if (mode == "webhook" && draft.Mappings.Count == 0) draft.Mappings = P.SeedMappings(P.Provider(draft.Descriptor), _controller.State.Libraries);
                    else if (mode == "poll") draft.Mappings.RemoveAll(row => string.IsNullOrWhiteSpace(row.From));
                    BuildSections(); SyncState();
                };
                delivery.Children.Add(choice); delivery.Children.Add(U.Text(T("sourceDialog.delivery." + mode + "Description"), 12, true));
            }
            _sections.Children.Add(delivery);
        }
        if (draft.DeliveryMode == "webhook" && _baseline != null) AddSection("connect", WebhookSection());
        if (draft.DeliveryMode == "webhook" && _baseline != null) AddSection("paths", PathsSection());
        if (draft.DeliveryMode != "webhook" && draft.Descriptor.Connection != "none") AddSection("connection", ConnectionSection());
        var settings = new StackPanel { Spacing = 16 };
        if (draft.DeliveryMode == "poll")
        {
            var interval = Input(T("sourceDialog.interval.label"), "AutoscanInterval", draft.Interval); interval.PlaceholderText = T("sourceDialog.interval.placeholder");
            interval.TextChanged += (_, _) => { draft.Interval = interval.Text; Changed(); }; settings.Children.Add(Field(T("sourceDialog.interval.label"), interval));
            settings.Children.Add(U.Text(_controller.State.Settings is { } saved ? T("sourceDialog.interval.helpWithDefault", ("interval", P.Interval(saved.DefaultPollIntervalSeconds))) : T("sourceDialog.interval.help"), 12, true));
        }
        if (draft.DeliveryMode == "webhook" && (draft.Form.Fields.Any(f => f.Key == "webhook_provider") || draft.Config.ContainsKey("webhook_provider")))
        {
            var provider = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetAutomationId(provider, "AutoscanWebhookProvider");
            var chosen = ProviderConnectionConfig.Scalar(draft.Config.GetValueOrDefault("webhook_provider"));
            if (chosen.Length == 0) draft.Config["webhook_provider"] = "auto";
            foreach (var key in new[] { "auto", "sonarr", "radarr" }) { var item = new ComboBoxItem { Content = key == "auto" ? T("webhook.provider.auto") : P.ProviderName(key), Tag = key }; provider.Items.Add(item); if (key == chosen || chosen.Length == 0 && key == "auto") provider.SelectedItem = item; }
            provider.SelectionChanged += (_, _) =>
            {
                if (provider.SelectedItem is not ComboBoxItem { Tag: string key }) return;
                draft.Config["webhook_provider"] = key; draft.ConfigDirty = true; Changed();
                if (_baseline == null && !draft.MappingsDirty) { draft.Mappings = P.SeedMappings(key, _controller.State.Libraries); RenderMappings(); }
                RenderWebhookInstructions();
            };
            var providerHost = _baseline != null && _providerHost != null ? _providerHost : settings;
            providerHost.Children.Add(Field(T("webhook.provider.label"), provider)); providerHost.Children.Add(U.Text(T("webhook.provider.autoHelp"), 12, true));
        }
        if (_baseline == null && draft.DeliveryMode == "webhook")
        {
            if (settings.Children.Count > 0) _sections.Children.Add(settings);
            AddSection("paths", PathsSection());
            settings = new StackPanel { Spacing = 16 };
        }
        if (draft.Form.Fields.Any(f => draft.DeliveryMode != "webhook" || f.Key != "webhook_provider"))
        {
            // Provider has its own selector; every remaining field uses the established native schema renderer.
            var fields = draft.Form.Fields.Where(f => draft.DeliveryMode != "webhook" || f.Key != "webhook_provider").ToList();
            var form = new Core.Models.Plugins.PluginAdminForm { Fields = fields, Sections = draft.Form.Sections?.Where(section => section.FieldKeys.Any(key => fields.Any(f => f.Key == key))).ToList() };
            var schema = new ProviderSchemaForm(form, draft.Config, draft.ConfigErrors);
            schema.Changed += () => { draft.ConfigDirty = true; Changed(); }; settings.Children.Add(schema);
            foreach (var field in form.Fields)
            {
                if (P.Fill(field.FillFrom, _controller.State.Libraries) is { Length: > 0 } fill)
                {
                    var button = U.Action(T("sourceForm.fillButton", ("field", field.Label)), "AutoscanFill_" + field.Key);
                    button.Click += (_, _) => { draft.Config[field.Key] = fill; draft.ConfigDirty = true; Changed(); BuildSections(); }; settings.Children.Add(button);
                }
            }
        }
        if (settings.Children.Count > 0) AddSection("settings", settings);
        if (draft.DeliveryMode != "webhook") AddSection("paths", PathsSection());
        var general = new StackPanel { Spacing = 12 };
        var label = Input(T("sourceDialog.label.label"), "AutoscanLabel", draft.Label); label.MaxLength = 1024; label.TextChanged += (_, _) => { draft.Label = label.Text; Changed(); };
        general.Children.Add(Field(T("sourceDialog.label.label"), label)); general.Children.Add(U.Text(T("sourceDialog.label.help", ("name", _plugin?.DisplayName ?? _baseline?.CapabilityId ?? "")), 12, true));
        if (_baseline != null) { general.Children.Add(U.Text(T("sourceDialog.general.type", ("type", P.TypeName(_baseline, _controller.State.Available)), ("mode", draft.DeliveryMode)), 12, true)); general.Children.Add(U.Text(_baseline.PluginId, 12, true)); general.Children.Add(U.Text(T("sourceDialog.general.changeType"), 12, true)); }
        AddSection("general", general);
        if (_baseline != null) SelectTab(_tabBodies[0].Key);
        _trail.Text = _baseline == null ? string.Join(" → ", new[] { T("sourceDialog.step.what"), draft.Descriptor.DeliveryModes.Count > 1 ? T("sourceDialog.step.how") : null, draft.DeliveryMode != "webhook" && draft.Descriptor.Connection != "none" ? T("sourceDialog.step.server") : null, draft.DeliveryMode == "poll" ? T("sourceDialog.step.setup") : null, T("sourceDialog.step.paths"), draft.DeliveryMode == "webhook" ? T("sourceDialog.step.connect") : null }.Where(s => s != null)) : "";
        _error.Text = "";
    }
    private void AddSection(string key, FrameworkElement body)
    {
        var section = new StackPanel { Spacing = 12 }; section.Children.Add(U.Text(T("sourceDialog.tab." + key), 15)); section.Children.Add(body); _sections.Children.Add(section);
        if (_baseline == null) return;
        _tabBodies.Add((key, section)); var button = U.Action(T("sourceDialog.tab." + key), "AutoscanTab_" + key); button.Click += (_, _) => SelectTab(key); _tabs.Children.Add(button);
    }
    private void SelectTab(string key) { foreach (var tab in _tabBodies) tab.Body.Visibility = tab.Key == key ? Visibility.Visible : Visibility.Collapsed; }
    private async Task SaveAsync()
    {
        if (!Current || Busy || _uncertain) return;
        if (_createdId != null) { CloseRequested?.Invoke(); return; }
        if (_draft is not { } draft || _baseline == null && _plugin == null) return;
        if (draft.Validate() is { } problem) { _error.Text = problem; foreach (var tab in _tabBodies) tab.Body.Visibility = Visibility.Visible; return; }
        var live = _baseline == null ? null : _controller.State.Sources.FirstOrDefault(s => s.Id == _baseline.Id);
        if (_baseline != null && (live == null || !AdminAutoscanEditorDraft.SameConfiguration(live, _baseline))) { _error.Text = "This source changed. Close and reopen Edit to review the current configuration."; _uncertain = true; SyncState(); return; }
        _saving = true; _error.Text = ""; SyncState();
        try
        {
            var body = draft.Build(live);
            var result = _baseline == null ? await _controller.CreateSourceAsync(_plugin!.PluginId, _plugin.CapabilityId, body, _owner) : await _controller.UpdateSourceAsync(_baseline.Id, body, _owner, _baseline);
            if (!Current) return;
            switch (result.Outcome)
            {
                // A definite refusal wrote nothing: the draft stays editable so it can be corrected and sent again.
                case AdminAutoscanOutcome.Refused: _error.Text = result.Message ?? ""; return;
                case AdminAutoscanOutcome.Skipped: _error.Text = T("sourceDialog.busy"); return;
                // Only an unknown outcome locks the editor; the controller has re-read the canonical state.
                case AdminAutoscanOutcome.Unknown: _uncertain = true; _error.Text = (result.Message ?? "") + " " + T("sourceDialog.reviewList"); return;
            }
            Dirty = false;
            if (_baseline != null || draft.DeliveryMode != "webhook") { _saving = false; CloseRequested?.Invoke(); return; }
            _createdId = result.Value!.Id;
            // The first-source transition does not dismiss this editor or repeat creation. Generating the endpoint is
            // idempotent for an existing endpoint, so a failure is reported as what it is and retried explicitly.
            var hook = await _controller.ConfigureWebhookAsync(_createdId, false, _owner);
            if (!Current) return;
            _title.Text = T("sourceDialog.connectTitle"); _description.Text = T("sourceDialog.connectDescription");
_createdProvider = P.Provider(draft.Descriptor, result.Value.SourceConfig); draft.Dispose(); _draft = null; Dirty = false;
            _content.Children.Clear(); _content.Children.Add(_sectionsHost); _content.Children.Add(_error); _sections.Children.Clear(); _sections.Children.Add(WebhookSection());
            if (!hook.Succeeded) _error.Text = hook.Message ?? T("sourceDialog.webhookFailedBody");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (Current) _error.Text = T("common.sourceWriteUnconfirmed"); _uncertain = true; }
        finally { _saving = false; if (Current) SyncState(); }
    }
    internal void SyncState()
    {
        if (!Current) return;
        var state = _controller.State;
        _save.Content = _createdId != null ? "Done" : _saving ? T(_baseline == null ? "sourceDialog.adding" : "sourceDialog.saving") : _baseline != null ? "Save" : _draft?.DeliveryMode == "webhook" ? T("sourceDialog.createAndContinue") : T("sourceDialog.addSource");
        _save.IsEnabled = !Busy && (_createdId != null || !_uncertain && !state.Stale && !state.Loading && !state.Busy && (_plugin != null || _baseline != null));
        _cancel.IsEnabled = !Busy; _cancel.Visibility = _createdId != null ? Visibility.Collapsed : Visibility.Visible;
        _sectionsHost.IsEnabled = !Busy && !state.Busy && !state.Loading;
        if (_baseline != null && state.Loaded && !state.Stale && !state.Sources.Any(s => s.Id == _baseline.Id)) { _error.Text = "This source was removed. Close the editor to review the source list."; _uncertain = true; _save.IsEnabled = false; }
        SyncWebhook();
    }
    private static TextBox Input(string label, string id, string value = "")
    { var input = new TextBox { Text = value, HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(input, label); AutomationProperties.SetAutomationId(input, id); return input; }
    private static FrameworkElement Field(string label, FrameworkElement editor)
    { var row = new StackPanel { Spacing = 6 }; row.Children.Add(U.Text(label, 13)); row.Children.Add(editor); AutomationProperties.SetName(editor, label); return row; }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = VisualTreeHelper.GetChild(node, i); if (child is T typed) yield return typed; foreach (var descendant in Descendants<T>(child)) yield return descendant; } }
    private void ClearSecretControls(bool preserveDraft = false)
    {
        var retained = preserveDraft && _draft != null ? new Dictionary<string, object?>(_draft.Config) : null;
        foreach (var secret in _secrets.Concat(Descendants<PasswordBox>(_sections)).Distinct()) secret.Password = "";
        _secrets.Clear();
        if (retained != null && _draft != null) { _draft.Config.Clear(); foreach (var (key, value) in retained) _draft.Config[key] = value; }
    }
    internal void Detach()
    {
        if (_closed) return; _closed = true; _generation++; _reads.Cancel(); _reads.Dispose(); ClearSecretControls();
        _connectionKey.Password = ""; _draft?.Dispose(); _baseline?.SourceConfig.Clear(); _sections.Children.Clear(); _content.Children.Clear();
        _endpoint.Password = ""; _createdId = null; CloseRequested = null;
        if (_opener?.IsLoaded == true) _opener.Focus(FocusState.Programmatic);
    }
}
