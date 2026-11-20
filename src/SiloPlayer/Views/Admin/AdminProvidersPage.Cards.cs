using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminProvidersPage
{
    private void RenderGroups(AdminProvidersState state)
    {
        _groups.Children.Clear();
        var watch = NewGroup("Watch providers", "");
        AddPluginTiles(watch, state, AdminPluginProviderKind.Watch, "No watch provider plugins are installed.", state.PluginProvidersFailed);
        watch.Add(Quiet("Simkl, MDBList, and other watch providers install from the plugin catalog. A provider's app credentials live on its plugin page, and viewers link their own accounts from profile settings."));
        watch.Add(AdminLink("Browse plugin catalog", "ProvidersPluginCatalog", new(AdminRoute.Plugins,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["tab"] = "catalog" })));

        var lists = NewGroup("Lists", "");
        lists.Add(MdblistTile(state));

        var subtitles = NewGroup("Subtitle providers", "");
        if (state.SubtitleProvidersFailed) subtitles.Add(Failure("Could not load subtitle providers."));
        else if (state.SubtitleProviders.Count == 0) subtitles.Add(Quiet("No subtitle providers are available."));
        else foreach (var provider in state.SubtitleProviders) subtitles.Add(SubtitleTile(state, provider));

        var markers = NewGroup("Marker providers", "");
        if (state.MarkerProvidersFailed) markers.Add(Failure("Could not load marker providers."));
        else if (state.MarkerProviders.Count == 0)
        {
            markers.Add(Quiet("No marker provider plugins are installed. Install one from the plugin catalog."));
            markers.Add(AdminLink("Browse plugin catalog", "ProvidersMarkerCatalog", new(AdminRoute.Plugins,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["tab"] = "catalog" })));
        }
        else foreach (var provider in state.MarkerProviders) markers.Add(MarkerTile(state, provider));
        var mode = state.Values.GetValueOrDefault("markers.mode", "both");
        markers.Add(Quiet(mode is "online" or "both" ? "Online marker lookup is enabled. Change this under Marker source."
            : "Online marker lookup is disabled. Change this under Marker source."));
        markers.Add(AdminLink("Open Marker source", "ProvidersMarkerSource", new(AdminRoute.PlaybackSettings)));

        var metadata = Quiet("Metadata providers, including TMDB, TheTVDB, and MDBList ratings, connect from Plugins.");
        AutomationProperties.SetAutomationId(metadata, "ProvidersMetadataNote");
        _groups.Children.Add(metadata);
        _groups.Children.Add(AdminLink("Open Plugins", "ProvidersMetadataOpen", new(AdminRoute.Plugins)));
    }

    private AdminSettingsGroup NewGroup(string title, string description)
    {
        var group = new AdminSettingsGroup(title, description); _groups.Children.Add(group); return group;
    }

    private void AddPluginTiles(AdminSettingsGroup group, AdminProvidersState state, AdminPluginProviderKind kind, string empty, bool failed)
    {
        if (failed) { group.Add(Failure("Could not load installed plugins.")); return; }
        var tiles = state.PluginProviders.Where(tile => tile.Kind == kind).ToArray();
        if (tiles.Length == 0) { group.Add(Quiet(empty)); return; }
        foreach (var tile in tiles) group.Add(PluginTile(tile, kind));
    }

    private Border PluginTile(AdminPluginProviderTile tile, AdminPluginProviderKind kind)
    {
        var connected = tile.Enabled && tile.ConfigurationReady;
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(TileHeader(tile.DisplayName, kind == AdminPluginProviderKind.Watch ? "Watch provider plugin" : "Metadata provider plugin",
            !tile.Enabled ? "Disabled" : tile.ConfigurationReady ? "Enabled" : "Needs setup", connected));
        var configure = U.Action("Configure", "ProvidersPlugin_" + SafeId(tile.InstallationId), secondary: true);
        configure.HorizontalAlignment = HorizontalAlignment.Left;
        var pluginRoute = new AdminRouteRequest(AdminRoute.Plugins) { Segments = [tile.PluginId] };
        configure.IsEnabled = AdminShellPage.CanOpen(pluginRoute);
        configure.Click += (_, _) => AdminShellPage.Open(pluginRoute);
        body.Children.Add(configure);
        return Card(body, expanded: false);
    }

    private Border MdblistTile(AdminProvidersState state)
    {
        var expanded = _expandedTile == "mdblist";
        var configured = state.Configured.Contains("mdblist.api_key");
        var managed = state.ManagedByEnv.Contains("mdblist.api_key");
        var draft = _mdblistDraft;
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(TileHeader("MDBList", "Lists", configured ? "Connected" : "Not connected", configured));
        if (!expanded)
        {
            var open = U.Action(configured ? "Manage" : "Connect", "ProvidersMDBListOpen", primary: true);
            open.HorizontalAlignment = HorizontalAlignment.Left; open.Click += (_, _) => { _expandedTile = "mdblist"; Render(state); };
            body.Children.Add(open); return Card(body, false);
        }
        body.Children.Add(Quiet("A key lets collection imports search and browse MDBList lists; pasted list URLs import without one."));
        var keyLink = new HyperlinkButton
        {
            Content = "Get an MDBList API key",
            NavigateUri = new Uri("https://mdblist.com/preferences/#api"),
            Padding = new(0),
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(keyLink, "ProvidersMDBListKeyLink");
        body.Children.Add(keyLink);
        var key = U.Secret("MDBList API key", "ProvidersMDBListApiKey");
        key.Password = draft; key.PlaceholderText = configured ? "••••••••••••" : "Not configured";
        key.IsEnabled = !managed && !state.SensitiveStatusError && !state.SavingSettings;
        body.Children.Add(new AdminSettingsRow("API key", managed ? "Managed by the server environment." : "Write-only credential.", key));
        if (state.RestartKeys.Has("mdblist.api_key")) body.Children.Add(Quiet("A server restart may be required after this setting changes."));
        Border? mdblistResult = null;
        if (state.MdblistTest is { } test)
        {
            mdblistResult = Result(test.Success, test.Message);
            body.Children.Add(mdblistResult);
        }
        void ClearStaleMdblistTest()
        {
            if (mdblistResult != null) mdblistResult.Visibility = Visibility.Collapsed;
            _feedback.Visibility = Visibility.Collapsed;
            _controller?.ClearMdblistTest();
        }
        var actions = Actions();
        var save = U.Action(state.SavingSettings ? "Saving…" : "Save", "ProvidersMDBListSave", primary: true);
        save.IsEnabled = !managed && !state.SavingSettings && draft.Trim().Length > 0 && !state.SettingsConflict && !state.SettingsOutcomeUnknown;
        save.Click += async (_, _) =>
        {
            if (_controller == null) return;
            _controller.SetMdblistApiKey(_mdblistDraft);
            await _controller.SaveMdblistAsync();
            if (!_controller.State.SettingsConflict && !_controller.State.SettingsOutcomeUnknown && !_controller.State.DirtyKeys.Contains("mdblist.api_key")) { _mdblistDraft = ""; _mdblistEdited = false; Render(_controller.State); }
        };
        var testButton = U.Action(state.CheckingMdblist ? "Testing…" : "Test connection", "ProvidersMDBListTest", secondary: true);
        testButton.IsEnabled = !state.CheckingMdblist && (configured || draft.Trim().Length > 0);
        key.PasswordChanged += (_, _) =>
        {
            ClearStaleMdblistTest();
            _mdblistDraft = key.Password; _mdblistEdited = true;
            save.IsEnabled = !managed && !state.SavingSettings && key.Password.Trim().Length > 0 && !state.SettingsConflict && !state.SettingsOutcomeUnknown;
            testButton.IsEnabled = !state.CheckingMdblist && (configured || key.Password.Trim().Length > 0);
        };
        testButton.Click += async (_, _) => { if (_controller != null) { if (_mdblistEdited) _controller.SetMdblistApiKey(_mdblistDraft); await _controller.TestMdblistAsync(); } };
        actions.Children.Add(save); actions.Children.Add(testButton);
        if (configured && !managed)
        {
            var clear = U.Action("Disconnect", "ProvidersMDBListDisconnect");
            clear.Click += async (_, _) =>
            {
                var owner = await ConfirmForCurrentOwnerAsync("Clear MDBList credentials?", "MDBList search and browse stop working. Lists already imported by URL are unaffected.", "Clear");
                if (owner == null || _controller == null) return;
                await _controller.ClearMdblistAsync(owner);
                if (!_controller.State.SettingsConflict && !_controller.State.SettingsOutcomeUnknown) { _mdblistDraft = ""; _mdblistEdited = false; Render(_controller.State); }
            };
            actions.Children.Add(clear);
        }
        actions.Children.Add(CloseButton("ProvidersMDBListClose", () => { _expandedTile = null; Render(state); }));
        body.Children.Add(actions); body.Children.Add(Quiet("Test uses the key typed here, or the saved one."));
        return Card(body, true);
    }

    private Border SubtitleTile(AdminProvidersState state, AdminSubtitleProvider provider)
    {
        var id = provider.ProviderName;
        var expanded = _expandedTile == "subtitle:" + id;
        var configured = id == "opensubtitles" ? provider.HasCredentials : provider.HasApiKey;
        var name = id switch { "opensubtitles" => "OpenSubtitles", "subdl" => "SubDL", "subsource" => "SubSource", _ => id };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(TileHeader(name, id == "opensubtitles" ? "Community subtitles" : "API key provider",
            configured && provider.Enabled ? "Connected" : configured ? "Connected · off" : "Not connected", configured && provider.Enabled));
        if (!expanded)
        {
            var open = U.Action(configured ? "Manage" : "Connect", "ProvidersSubtitleOpen_" + SafeId(id), primary: true);
            open.HorizontalAlignment = HorizontalAlignment.Left; open.Click += async (_, _) => await ExpandSubtitleAsync(state, provider);
            body.Children.Add(open); return Card(body, false);
        }
        if (!_subtitleEditors.TryGetValue(id, out var editor) || !_subtitleDrafts.TryGetValue(id, out var draft))
        {
            body.Children.Add(Quiet(state.BusyProvider == id ? "Loading provider configuration…" : "Provider configuration could not be loaded."));
            var retry = U.Action("Reload", "ProvidersSubtitleReload_" + SafeId(id)); retry.Click += async (_, _) => await ExpandSubtitleAsync(state, provider, reload: true);
            body.Children.Add(retry); return Card(body, true);
        }
        var canEdit = !draft.SaveAttempted && state.BusyProvider == null && !state.SubtitleConflict && !state.SubtitleOutcomeUnknown;
        Border? subtitleResult = null;
        void ClearStaleSubtitleTest()
        {
            if (subtitleResult != null) subtitleResult.Visibility = Visibility.Collapsed;
            _feedback.Visibility = Visibility.Collapsed;
            _controller?.ClearSubtitleTest(id);
        }
        var enable = new ToggleSwitch { Header = "Enabled", OffContent = "", OnContent = "", IsOn = draft.Enabled, MinWidth = 0, IsEnabled = canEdit };
        AutomationProperties.SetAutomationId(enable, "ProvidersSubtitleEnabled_" + SafeId(id));
        enable.Toggled += (_, _) => { if (draft.Enabled != enable.IsOn) { ClearStaleSubtitleTest(); draft.SetEnabled(enable.IsOn); } };
        body.Children.Add(enable);
        if (id == "opensubtitles")
        {
            var username = U.Input("Username", "ProvidersSubtitleUsername_" + SafeId(id)); username.Text = draft.Username; username.IsEnabled = canEdit;
            username.TextChanging += (_, _) => { ClearStaleSubtitleTest(); draft.SetUsername(username.Text); if (enable.IsOn != draft.Enabled) enable.IsOn = draft.Enabled; }; body.Children.Add(new AdminSettingsRow("Username", configured ? "Leave blank to keep the saved username." : "OpenSubtitles account name.", username));
            var password = U.Secret("Password", "ProvidersSubtitlePassword_" + SafeId(id)); password.Password = draft.Password; password.PlaceholderText = configured ? "••••••••••••" : "Not configured"; password.IsEnabled = canEdit;
            password.PasswordChanged += (_, _) => { ClearStaleSubtitleTest(); draft.SetPassword(password.Password); if (enable.IsOn != draft.Enabled) enable.IsOn = draft.Enabled; }; body.Children.Add(new AdminSettingsRow("Password", "Write-only credential.", password));
        }
        else
        {
            var apiKey = U.Secret("API key", "ProvidersSubtitleApiKey_" + SafeId(id)); apiKey.Password = draft.ApiKey; apiKey.PlaceholderText = configured ? "••••••••••••" : "Not configured"; apiKey.IsEnabled = canEdit;
            apiKey.PasswordChanged += (_, _) => { ClearStaleSubtitleTest(); draft.SetApiKey(apiKey.Password); if (enable.IsOn != draft.Enabled) enable.IsOn = draft.Enabled; }; body.Children.Add(new AdminSettingsRow("API key", "Write-only credential.", apiKey));
        }
        if (state.SubtitleTestFor(id) is { } subtitleTest && state.BusyProvider is null)
        {
            subtitleResult = Result(subtitleTest.Success, subtitleTest.Success ? "Connection successful." : subtitleTest.Error ?? "Connection failed.");
            body.Children.Add(subtitleResult);
        }
        var actions = Actions();
        var save = U.Action(state.BusyProvider == id ? "Saving…" : "Save", "ProvidersSubtitleSave_" + SafeId(id), primary: true);
        save.IsEnabled = canEdit;
        save.Click += async (_, _) => await SaveSubtitleAsync(editor, draft, clear: false);
        var test = U.Action("Test connection", "ProvidersSubtitleTest_" + SafeId(id), secondary: true);
        test.IsEnabled = state.BusyProvider == null; test.Click += async (_, _) => { if (_controller != null) await _controller.TestSubtitleAsync(id, SubtitleChange(draft)); };
        actions.Children.Add(save); actions.Children.Add(test);
        if (configured)
        {
            var clear = U.Action("Disconnect", "ProvidersSubtitleDisconnect_" + SafeId(id));
            clear.IsEnabled = canEdit;
            clear.Click += async (_, _) =>
            {
                if (await ConfirmForCurrentOwnerAsync("Clear " + name + " credentials?", "Save " + name + " as disabled and clear its stored credentials. Applying this change on this server is reported separately; other servers may still use older settings.", "Clear and turn off") != null) await SaveSubtitleAsync(editor, draft, clear: true);
            };
            actions.Children.Add(clear);
        }
        actions.Children.Add(CloseButton("ProvidersSubtitleClose_" + SafeId(id), () => { _expandedTile = null; Render(state); }));
        var reload = U.Action("Reload", "ProvidersSubtitleReload_" + SafeId(id)); reload.IsEnabled = state.BusyProvider == null;
        reload.Click += async (_, _) => await ExpandSubtitleAsync(state, provider, reload: true); actions.Children.Add(reload);
        body.Children.Add(actions); body.Children.Add(Quiet("The test uses the staged values and does not save them."));
        return Card(body, true);
    }

    private async Task ExpandSubtitleAsync(AdminProvidersState state, AdminSubtitleProvider provider, bool reload = false)
    {
        var id = provider.ProviderName; _expandedTile = "subtitle:" + id; Render(state);
        if (!reload && _subtitleEditors.ContainsKey(id)) return;
        var loaded = _controller == null ? null : await _controller.GetSubtitleEditorAsync(id);
        if (loaded == null) return;
        _subtitleEditors[id] = loaded;
        if (_subtitleDrafts.TryGetValue(id, out var retained)) retained.RearmAfterReload(loaded);
        else _subtitleDrafts[id] = new(id, loaded.Enabled, loaded.HasApiKey, loaded.HasCredentials);
        if (_controller != null) Render(_controller.State);
    }

    private async Task SaveSubtitleAsync(AdminSubtitleProviderEditor editor, AdminSubtitleProviderDraft draft, bool clear)
    {
        if (_controller == null) return;
        var state = _controller.State;
        // The editor and draft must still be the ones this tile was showing; a replaced authority clears them.
        if (!_subtitleEditors.TryGetValue(editor.Provider, out var liveEditor) || !ReferenceEquals(liveEditor, editor) ||
            !_subtitleDrafts.TryGetValue(editor.Provider, out var liveDraft) || !ReferenceEquals(liveDraft, draft)) return;
        if (draft.SaveAttempted || state.BusyProvider != null || state.SubtitleConflict || state.SubtitleOutcomeUnknown) return;
        var saved = await _controller.SaveSubtitleAsync(editor, draft.CaptureChange(clear));
        if (!saved) return;
        draft.ClearSecrets(); _subtitleDrafts.Remove(editor.Provider); _subtitleEditors.Remove(editor.Provider); _expandedTile = null;
        Render(_controller.State);
    }

    private async Task SaveMarkerAsync(string provider)
    {
        var controller = _controller;
        if (controller == null || !_markerDrafts.TryGetValue(provider, out var sent) || sent.SaveInFlight || !sent.IsValid) return;
        var patch = sent.BeginSave();
        if (patch.Count == 0) { sent.AbandonSave(); return; }
        var saved = await controller.SaveMarkerAsync(provider, patch);
        // A retired page or authority already discarded every draft.
        if (!ReferenceEquals(controller, _controller) || !_markerDrafts.TryGetValue(provider, out var current)) return;
        var updated = saved ? controller.State.MarkerProviders.FirstOrDefault(item => string.Equals(item.Provider, provider, StringComparison.Ordinal)) : null;
        if (updated == null) { current.AbandonSave(); Render(controller.State); return; }
        // Edits typed while the request was pending are kept; only what was actually sent is replaced by the server value.
        var next = current.Rebase(updated);
        if (next.IsValid && !next.Dirty) { _markerDrafts.Remove(provider); _markerAdvanced.Remove(provider); _expandedTile = null; }
        else _markerDrafts[provider] = next;
        Render(controller.State);
    }

    private static AdminSubtitleProviderChange SubtitleChange(AdminSubtitleProviderDraft draft) => draft.Provider == "opensubtitles"
        ? new() { Enabled = draft.Enabled, Username = draft.Username, Password = draft.Password }
        : new() { Enabled = draft.Enabled, ApiKey = draft.ApiKey };

    private Border MarkerTile(AdminProvidersState state, AdminMarkerProvider provider)
    {
        var id = "marker:" + provider.Provider;
        var expanded = _expandedTile == id;
        var name = string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Provider : provider.DisplayName;
        var ready = state.PluginProviders.FirstOrDefault(tile => tile.Kind == AdminPluginProviderKind.Marker && tile.InstallationId == provider.PluginInstallationId)?.ConfigurationReady;
        var connected = ready != false && provider.FetchEnabled;
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(TileHeader(name!, "Skip markers", connected ? "Connected" : ready == false ? "Needs setup" : "Connected · off", connected));
        if (!expanded)
        {
            var open = U.Action(ready == false ? "Set up" : "Manage", "ProvidersMarkerOpen_" + SafeId(provider.Provider), primary: true);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (_, _) => { _expandedTile = id; if (!_markerDrafts.ContainsKey(provider.Provider)) _markerDrafts[provider.Provider] = new(provider); Render(state); };
            body.Children.Add(open); return Card(body, false);
        }
        var draft = _markerDrafts.TryGetValue(provider.Provider, out var existing)
            ? (_markerDrafts[provider.Provider] = existing.Rebase(provider))
            : (_markerDrafts[provider.Provider] = new(provider));
        Button? markerSave = null;
        Border? validation = null;
        void DraftChanged()
        {
            if (markerSave != null) markerSave.IsEnabled = draft.Dirty && draft.IsValid && state.BusyProvider == null && !state.MarkerOutcomeUnknown && !draft.SaveInFlight;
            if (validation != null) validation.Visibility = draft.IsValid ? Visibility.Collapsed : Visibility.Visible;
        }
        if (provider.SourceType == "plugin" && provider.PluginId != null)
        {
            var plugin = U.Action("Configure plugin", "ProvidersMarkerPlugin_" + SafeId(provider.Provider), secondary: true);
            var pluginRoute = new AdminRouteRequest(AdminRoute.Plugins) { Segments = [provider.PluginId] };
            plugin.HorizontalAlignment = HorizontalAlignment.Left; plugin.IsEnabled = AdminShellPage.CanOpen(pluginRoute); plugin.Click += (_, _) => AdminShellPage.Open(pluginRoute); body.Children.Add(plugin);
        }
        body.Children.Add(ToggleRow("Get markers from this provider", "ProvidersMarkerFetch_" + SafeId(provider.Provider), draft.FetchEnabled, value => { draft.FetchEnabled = value; DraftChanged(); }));
        ToggleSwitch? autoToggle = null;
        body.Children.Add(ToggleRow("Allow sharing with this provider", "ProvidersMarkerShare_" + SafeId(provider.Provider), draft.ContributeEnabled, value => { draft.ContributeEnabled = provider.IsSubmitter && value; if (!value) draft.ContributeAutoLocal = false; if (autoToggle != null) { autoToggle.IsEnabled = provider.IsSubmitter && value; if (!value) autoToggle.IsOn = false; } DraftChanged(); }, !provider.IsSubmitter,
            provider.IsSubmitter ? (provider.DefaultContributeEnabled ? "Sharing is enabled by default for this provider." : "") : "This provider does not accept shared markers."));
        body.Children.Add(ToggleRow("Automatically share detected intros and credits", "ProvidersMarkerAuto_" + SafeId(provider.Provider), draft.ContributeAutoLocal, value => { draft.ContributeAutoLocal = value; DraftChanged(); },
            !provider.IsSubmitter || !draft.ContributeEnabled, "Share intros and credits detected on this server that meet the minimum confidence under Advanced.", toggle => autoToggle = toggle));
        var advancedOpen = _markerAdvanced.Contains(provider.Provider) || draft.AdvancedDirty || !draft.IsValid;
        var advancedButton = U.Action("Advanced · 3", "ProvidersMarkerAdvanced_" + SafeId(provider.Provider), secondary: true);
        advancedButton.HorizontalAlignment = HorizontalAlignment.Left;
        advancedButton.Click += (_, _) =>
        {
            if (draft.AdvancedDirty || !draft.IsValid) _markerAdvanced.Add(provider.Provider);
            else if (!_markerAdvanced.Add(provider.Provider)) _markerAdvanced.Remove(provider.Provider);
            Render(state);
        };
        body.Children.Add(advancedButton);
        var advanced = new StackPanel { Spacing = 12, Visibility = advancedOpen ? Visibility.Visible : Visibility.Collapsed };
        advanced.Children.Add(TextRow("Provider priority", "Silo uses the marker with the highest confidence. When two providers are equally confident, it prefers the lower number.", "ProvidersMarkerPriority_" + SafeId(provider.Provider), draft.FetchPriority, value => { draft.FetchPriority = value; DraftChanged(); }, number: true));
        advanced.Children.Add(TextRow("Minimum confidence to use markers", "Skip markers this provider rates lower, so another provider or local detection can fill them. 0% uses every marker.", "ProvidersMarkerFetchConfidence_" + SafeId(provider.Provider), draft.FetchMinimumPercent, value => { draft.FetchMinimumPercent = value; DraftChanged(); }, number: true, unit: "%"));
        advanced.Children.Add(TextRow("Minimum confidence for automatic sharing", "Choose a value from 0% to 100%. Recommended: 95%.", "ProvidersMarkerShareConfidence_" + SafeId(provider.Provider), draft.ContributeMinimumPercent, value => { draft.ContributeMinimumPercent = value; DraftChanged(); }, number: true, unit: "%", disabled: !provider.IsSubmitter));
        validation = Failure("Provider priority must be a whole number, and minimum confidence must be between 0% and 100%.");
        validation.Visibility = draft.IsValid ? Visibility.Collapsed : Visibility.Visible; advanced.Children.Add(validation);
        body.Children.Add(advanced);
        if (state.MarkerTestFor(provider.Provider) is { } markerTest && state.BusyProvider is null)
        {
            body.Children.Add(Result(markerTest.Valid, markerTest.Valid ? "Connection successful." : markerTest.Error ?? "Connection test failed."));
            if (markerTest.Stats is { } stats) body.Children.Add(Quiet($"Total submissions {stats.Total}; accepted {stats.Accepted}; pending {stats.Pending}; rejected {stats.Rejected}; acceptance rate {stats.AcceptanceRate:P0}; best streak {stats.BestStreak}."));
        }
        var actions = Actions();
        var save = U.Action(state.BusyProvider == provider.Provider ? "Saving…" : "Save", "ProvidersMarkerSave_" + SafeId(provider.Provider), primary: true);
        markerSave = save; DraftChanged();
        save.Click += async (_, _) => await SaveMarkerAsync(provider.Provider);
        actions.Children.Add(save);
        if (state.MarkerOutcomeUnknown)
        {
            // The outcome of the last marker write is unknown: only a canonical reload may say what the server holds.
            var reload = U.Action("Reload", "ProvidersMarkerReload_" + SafeId(provider.Provider), secondary: true);
            reload.IsEnabled = state.BusyProvider == null && !state.Loading;
            reload.Click += async (_, _) => { if (_controller != null) await _controller.ReloadAsync(preserveDraft: true); };
            actions.Children.Add(reload);
        }
        if (provider.IsSubmitter && ready == true)
        {
            var test = U.Action("Test connection", "ProvidersMarkerTest_" + SafeId(provider.Provider), secondary: true); test.IsEnabled = state.BusyProvider == null;
            test.Click += async (_, _) => { if (_controller != null) await _controller.TestMarkerAsync(provider.Provider); }; actions.Children.Add(test);
        }
        actions.Children.Add(CloseButton("ProvidersMarkerClose_" + SafeId(provider.Provider), () => { _expandedTile = null; Render(state); }));
        body.Children.Add(actions); return Card(body, true);
    }

    private static FrameworkElement ToggleRow(string label, string id, bool value, Action<bool> change, bool disabled = false, string description = "", Action<ToggleSwitch>? created = null)
    {
        var toggle = new ToggleSwitch { OffContent = "", OnContent = "", IsOn = value, MinWidth = 0, IsEnabled = !disabled };
        created?.Invoke(toggle);
        AutomationProperties.SetAutomationId(toggle, id); toggle.Toggled += (_, _) => change(toggle.IsOn);
        return new AdminSettingsRow(label, description, toggle);
    }

    private static FrameworkElement TextRow(string label, string description, string id, string value, Action<string> change, bool number = false, string? unit = null, bool disabled = false)
    {
        var input = U.Input(label, id, number: number); input.Text = value; input.IsEnabled = !disabled; input.TextChanging += (_, _) => change(input.Text);
        return new AdminSettingsRow(label, description, input, unit);
    }

    /// <summary>
    /// Confirms a destructive action for the authority that is current when the dialog opens. The dialog is modal but a server or
    /// account change can replace the authority behind it, so a confirmation never carries over to the new authority.
    /// </summary>
    private async Task<MonitorAuthorityLease?> ConfirmForCurrentOwnerAsync(string title, string description, string action)
    {
        var controller = _controller; var owner = controller?.State.Owner;
        if (controller == null || owner?.IsCurrent != true) return null;
        if (!await ConfirmAsync(title, description, action)) return null;
        return ReferenceEquals(controller, _controller) && ReferenceEquals(owner, controller.State.Owner) && owner.IsCurrent ? owner : null;
    }

    private async Task<bool> ConfirmAsync(string title, string description, string action)
    {
        if (_dialog != null || XamlRoot == null) return false;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = description, PrimaryButtonText = action, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        _dialog = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { _dialog = null; }
    }

    private static Border Card(UIElement content, bool expanded) => new()
    {
        Child = content, Padding = new(16), Margin = new(0, 0, 0, 12), CornerRadius = new(12),
        BorderThickness = new(1), BorderBrush = U.Brush(expanded ? "AccentFillColorDefaultBrush" : "BorderBrush"), Background = U.Brush("SurfaceBrush"),
    };

    private static Grid TileHeader(string name, string tagline, string status, bool connected)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 3 }; var title = U.Text(name, 15); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; text.Children.Add(title); text.Children.Add(U.Text(tagline, 12, true)); grid.Children.Add(text);
        var pillText = U.Text(status, 11); pillText.Foreground = connected ? U.Accent : U.Brush("SecondaryTextBrush");
        var pill = new Border { Child = pillText, Padding = new(8, 3, 8, 3), CornerRadius = new(12), Background = connected ? AdminMonitorUi.Solid(AdminMonitorUi.Primary, .12) : U.Brush("ControlFillColorDefaultBrush"), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(pill, 1); grid.Children.Add(pill); return grid;
    }

    private static ProviderActionFlow Actions() => new();
    private static Button CloseButton(string id, Action close) { var button = U.Action("Close", id); button.Click += (_, _) => close(); return button; }
    private static HyperlinkButton AdminLink(string label, string id, AdminRouteRequest request)
    {
        var link = new HyperlinkButton { Content = label, Padding = new(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(link, id);
        link.IsEnabled = AdminShellPage.CanOpen(request);
        link.Click += (_, _) => AdminShellPage.Open(request);
        return link;
    }
    private static TextBlock Quiet(string text) { var block = U.Text(text, 13, true); block.Margin = new(0, 8, 0, 8); return block; }
    private static Border Failure(string text) => U.Alert(text);
    private static Border Result(bool success, string message)
    {
        var text = U.Text(message, 13); text.Foreground = success ? AdminMonitorUi.Solid(AdminMonitorUi.Emerald400) : AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite); return new Border { Child = text, Padding = new(0, 6, 0, 6) };
    }
    private static string SafeId(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    /// <summary>Small provider actions wrap at compact widths instead of creating horizontal page overflow.</summary>
    private sealed class ProviderActionFlow : Panel
    {
        private const double Gap = 8;
        private readonly List<(int Start, int End, double Width, double Height)> _lines = [];
        protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
        {
            _lines.Clear();
            var visible = Children.Where(child => child.Visibility == Visibility.Visible).ToArray();
            var limit = double.IsFinite(available.Width) ? available.Width : double.MaxValue;
            int start = 0; double width = 0, height = 0, total = 0, widest = 0;
            for (var index = 0; index < visible.Length; index++)
            {
                visible[index].Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                var size = visible[index].DesiredSize; var next = width == 0 ? size.Width : width + Gap + size.Width;
                if (width > 0 && next > limit)
                {
                    _lines.Add((start, index, width, height)); total += height + Gap; widest = Math.Max(widest, width);
                    start = index; width = size.Width; height = size.Height;
                }
                else { width = next; height = Math.Max(height, size.Height); }
            }
            if (visible.Length > 0) { _lines.Add((start, visible.Length, width, height)); total += height; widest = Math.Max(widest, width); }
            return new(double.IsFinite(available.Width) ? Math.Min(available.Width, widest) : widest, total);
        }
        protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size final)
        {
            var visible = Children.Where(child => child.Visibility == Visibility.Visible).ToArray(); double top = 0;
            foreach (var (start, end, _, height) in _lines)
            {
                double left = 0;
                for (var index = start; index < end; index++)
                {
                    var size = visible[index].DesiredSize; visible[index].Arrange(new(left, top, size.Width, size.Height)); left += size.Width + Gap;
                }
                top += height + Gap;
            }
            return final;
        }
    }
}
