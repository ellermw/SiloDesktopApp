using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

internal sealed partial class AdminAutoscanSourceEditor
{
    private PasswordBox _connectionKey = new();
    private TextBlock _testOutcome = U.Text("", 13);
    private bool _connectionSaving, _testing;
    private FrameworkElement ConnectionSection()
    {
        var draft = _draft!;
        var section = new StackPanel { Spacing = 12 };
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetAutomationId(select, "AutoscanConnection");
        var required = AdminAutoscanSourceDraft.ConnectionRequired(draft.Descriptor, draft.DeliveryMode);
        if (!required) select.Items.Add(new ComboBoxItem { Content = T("connections.picker.none"), Tag = "" });
        foreach (var connection in _controller.State.Connections.Where(c => AdminAutoscanSourceDraft.MatchesConnection(draft.Descriptor, c.Kind)))
        {
            var item = new ComboBoxItem { Content = connection.Name, Tag = connection.Id }; select.Items.Add(item); if (connection.Id == draft.ConnectionId) select.SelectedItem = item;
        }
        if (draft.ConnectionId.Length == 0 && !required) select.SelectedIndex = 0;
        // A removed or no-longer-eligible server is never silently replaced by another one.
        if (select.SelectedItem == null && draft.ConnectionId.Length > 0) { select.Items.Add(new ComboBoxItem { Content = "Saved server unavailable — choose a server", Tag = draft.ConnectionId }); select.SelectedIndex = select.Items.Count - 1; }
        var addChoice = new ComboBoxItem { Content = T("connections.picker.addNew"), Tag = "__add__" }; select.Items.Add(addChoice);
        section.Children.Add(Field(T(required ? "connections.picker.labelRequired" : "connections.picker.label"), select));
        section.Children.Add(U.Text(T(required ? "connections.picker.requiredHelp" : "connections.picker.optionalHelp"), 12, true));
        var add = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed }; section.Children.Add(add);
        var reuse = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetAutomationId(reuse, "AutoscanConnectionReuse");
        var manual = new ComboBoxItem { Content = T("connections.picker.manualOption"), Tag = "" }; reuse.Items.Add(manual); reuse.SelectedItem = manual;
        add.Children.Add(Field(T("connections.picker.server"), reuse));
        var reuseStatus = U.Text("", 12, true); add.Children.Add(reuseStatus);
        var retryServers = U.Action("Reload request servers", "AutoscanRequestServersRetry"); retryServers.Visibility = Visibility.Collapsed; add.Children.Add(retryServers);
        var manualFields = new StackPanel { Spacing = 12 }; add.Children.Add(manualFields);
        var kinds = draft.Descriptor.ConnectionKinds.Count > 0 ? draft.Descriptor.ConnectionKinds : new List<string> { "sonarr", "radarr" };
        var kind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetAutomationId(kind, "AutoscanConnectionKind");
        foreach (var value in kinds) kind.Items.Add(new ComboBoxItem { Content = AdminAutoscanPresentation.ProviderName(value) == "Sonarr/Radarr" ? value : AdminAutoscanPresentation.ProviderName(value), Tag = value }); kind.SelectedIndex = 0;
        if (kinds.Count > 1) manualFields.Children.Add(Field(T("connections.picker.service"), kind));
        var name = Input(T("connections.form.name"), "AutoscanConnectionName"); name.MaxLength = 1024;
        var url = Input(T("connections.form.baseURL"), "AutoscanConnectionUrl"); url.MaxLength = 8192; url.PlaceholderText = "http://localhost:8989";
        _connectionKey = new PasswordBox { MaxLength = 8192, PasswordRevealMode = PasswordRevealMode.Hidden, PlaceholderText = T("connections.form.apiKeyPlaceholder") };
        var key = _connectionKey; _secrets.Add(key); AutomationProperties.SetAutomationId(key, "AutoscanConnectionKey");
        manualFields.Children.Add(Field(T("connections.form.name"), name)); manualFields.Children.Add(Field(T("connections.form.baseURL"), url)); manualFields.Children.Add(Field(T("connections.form.apiKey"), key));
        _testOutcome = U.Text("", 13); var outcome = _testOutcome; AutomationProperties.SetAutomationId(outcome, "AutoscanConnectionTestResult"); add.Children.Add(outcome);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var test = U.Action(T("connections.form.test"), "AutoscanConnectionTest");
        var create = U.Action(T("connections.picker.add"), "AutoscanConnectionCreate"); var cancel = U.Action("Cancel", "AutoscanConnectionCancel");
        tools.Children.Add(test); tools.Children.Add(cancel); tools.Children.Add(create); add.Children.Add(tools);
        string ReuseId() => (reuse.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        string Kind() => (kind.SelectedItem as ComboBoxItem)?.Tag as string ?? kinds[0];
        var serverRows = new Dictionary<string, AdminAutoscanRequestIntegration>();
        // After an unknown creation outcome the draft stays locked: adding again could duplicate the server.
        var createLocked = false;
        void Update()
        {
            var linked = ReuseId().Length > 0; manualFields.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            test.IsEnabled = !_testing && !_connectionSaving && (linked || url.Text.Trim().Length > 0);
            create.IsEnabled = !createLocked && !_testing && !_connectionSaving && (linked || name.Text.Trim().Length > 0 && url.Text.Trim().Length > 0 && key.Password.Trim().Length > 0);
            cancel.IsEnabled = !_connectionSaving; select.IsEnabled = !_connectionSaving;
        }
        void Edit() { Changed(); outcome.Text = ""; Update(); }
        name.TextChanged += (_, _) => Edit(); url.TextChanged += (_, _) => Edit(); key.PasswordChanged += (_, _) => Edit(); kind.SelectionChanged += (_, _) => Edit();
        reuse.SelectionChanged += (_, _) => { if (ReuseId().Length > 0) key.Password = ""; Edit(); };
        async Task LoadServers()
        {
            if (!Current || Busy) return;
            var generation = _generation; reuseStatus.Text = T("connections.dialog.loadingServers"); retryServers.Visibility = Visibility.Collapsed;
            var rows = await _controller.ReusableIntegrationsAsync(_owner, _reads.Token);
            if (!Current || !ReferenceEquals(draft, _draft)) return;
            if (generation != _generation) { reuseStatus.Text = "Request-server draft changed. Reload to select a saved server."; retryServers.Visibility = Visibility.Visible; return; }
            if (rows == null) { reuseStatus.Text = "Request servers could not be loaded. Retry or enter credentials manually."; retryServers.Visibility = Visibility.Visible; return; }
            var linkedIds = _controller.State.Connections.Select(c => c.RequestIntegrationId).ToHashSet();
            foreach (var row in rows)
            {
                if (!row.Enabled || linkedIds.Contains(row.Id) || !row.PluginConfig.TryGetValue("service_kind", out var service) || service.ValueKind != JsonValueKind.String || !AdminAutoscanSourceDraft.MatchesConnection(draft.Descriptor, service.GetString()!)) continue;
                if (!serverRows.TryAdd(row.Id, row)) continue;
                reuse.Items.Add(new ComboBoxItem { Content = T("connections.picker.reuseOption", ("name", row.Name)), Tag = row.Id });
            }
            reuseStatus.Text = serverRows.Count > 0 ? T("connections.picker.reuseHelp") : T("connections.dialog.noRequestServers");
            if (serverRows.Count > 0) reuse.SelectedIndex = 1;
            Update();
        }
        retryServers.Click += async (_, _) => await LoadServers();
        select.SelectionChanged += async (_, _) =>
        {
            if (!Current || select.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            _generation++; outcome.Text = "";
            if (id == "__add__") { add.Visibility = Visibility.Visible; Update(); await LoadServers(); }
            else { add.Visibility = Visibility.Collapsed; key.Password = ""; draft.ConnectionId = id; Changed(); }
        };
        cancel.Click += (_, _) => { if (_connectionSaving) return; key.Password = ""; add.Visibility = Visibility.Collapsed; select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == draft.ConnectionId); };
        test.Click += async (_, _) =>
        {
            if (!Current || _testing || _connectionSaving) return;
            var generation = _generation; _testing = true; Update(); outcome.Text = T("connections.form.testing");
            var request = new AdminAutoscanConnectionTest { RequestIntegrationId = ReuseId().Length > 0 ? ReuseId() : null, BaseUrl = url.Text.Trim(), ApiKeyRef = key.Password.Trim() };
            try
            {
                var result = await _controller.TestConnectionAsync(request, _owner, _reads.Token);
                if (Current && generation == _generation)
                    outcome.Text = result?.Ok == true ? (string.IsNullOrEmpty(result.Version) ? T("connections.form.connected") : T("connections.form.connectedVersion", ("version", result.Version)))
                        : result?.Error is { Length: > 0 } reason ? reason : T("connections.form.connectionFailed");
            }
            finally { request.ApiKeyRef = ""; _testing = false; if (Current) Update(); }
        };
        create.Click += async (_, _) =>
        {
            if (!Current || _connectionSaving || _testing) return;
            var generation = _generation; var linked = serverRows.GetValueOrDefault(ReuseId());
            var request = new AdminAutoscanConnectionCreate { Name = linked?.Name ?? name.Text.Trim(), Kind = linked?.PluginConfig["service_kind"].GetString() ?? Kind(), RequestIntegrationId = linked?.Id, BaseUrl = linked == null ? url.Text.Trim() : "", ApiKeyRef = linked == null ? key.Password.Trim() : "" };
            _connectionSaving = true; Update(); SyncState();
            try
            {
                var result = await _controller.CreateConnectionAsync(request, _owner);
                if (!Current || generation != _generation) return;
                // A definite refusal keeps the typed fields so they can be corrected and sent again.
                if (result.Outcome == AdminAutoscanOutcome.Refused) { outcome.Text = result.Message ?? ""; return; }
                if (!result.Succeeded)
                { createLocked = result.Outcome == AdminAutoscanOutcome.Unknown; outcome.Text = result.Outcome == AdminAutoscanOutcome.Unknown ? (result.Message ?? "") + " " + T("sourceDialog.reviewList") : T("sourceDialog.busy"); return; }
                key.Password = "";
                var created = result.Value!;
                var item = new ComboBoxItem { Content = created.Name, Tag = created.Id }; select.Items.Insert(Math.Max(0, select.Items.Count - 1), item); select.SelectedItem = item; draft.ConnectionId = created.Id; Dirty = true; add.Visibility = Visibility.Collapsed;
            }
            finally { request.ApiKeyRef = ""; _connectionSaving = false; if (Current) { SyncState(); Update(); } }
        };
        Update(); return section;
    }
}
