using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Official source/token dialogs, user discovery, mapping and durable import runs at 217ff25.</summary>
public sealed partial class AdminHistoryImportPage : Page, IAdminPageSuspension
{
    private readonly AdminUserResourcesApi _api;
    private readonly AdminUserResourceHost _host;
    private AdminRouteRequest _route = new(AdminRoute.HistoryImport);
    private IReadOnlyList<JsonElement> _sources = [], _mappings = [], _accounts = [];
    private readonly List<JsonElement> _runs = [];
    private AdminResourceDocument? _source;
    private JsonElement _capabilities;
    private readonly StackPanel _mappingPanel = Stack(12), _runPanel = Stack(8);
    private readonly HashSet<string> _runCursors = [];
    private string? _nextRun;
    private string _runFilter = "all";
    private string? _expanded;
    private int? _lookback;
    private JsonElement _bulk;
    private DispatcherTimer? _poll;
    private DateTimeOffset _nextPoll;
    private bool _polling;
    private int _pollIndex;
    private readonly List<PasswordBox> _secrets = [];
    private readonly List<Button> _writes = [];
    private bool _loadingSourceData;
    private bool _mappingsReady;
    private long _sourceDataVersion;
    private int _mappingVisible = 50;
    public AdminUserResourceController Session => _host.Session;
    public AdminHistoryImportPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled; Id(this, "AdminHistoryImportPage");
        _api = App.Services.GetRequiredService<AdminUserResourcesApi>();
        _host = new(this, new(App.Services.GetRequiredService<AdminMonitorAuthority>()), LoadAsync, Clear);
        _host.StateChanged += UpdateActions;
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); if (e.Parameter is AdminRouteRequest route) _route = route; }
    public void SuspendAdminPage() => _host.Suspend();
    private void Clear()
    {
        _sourceDataVersion++; _loadingSourceData = false; _expanded = null; _pollIndex = 0;
        _poll?.Stop(); if (_poll != null) _poll.Tick -= PollTick; _poll = null;
        foreach (var secret in _secrets) secret.Password = ""; _secrets.Clear();
        _sources = _mappings = _accounts = []; _runs.Clear(); _runCursors.Clear(); _source = null; _capabilities = _bulk = default; _nextRun = null;
        _mappingPanel.Children.Clear(); _runPanel.Children.Clear(); _writes.Clear(); _mappingsReady = false;
    }
    private static string H(string key, params (string Name, object? Value)[] args) => T("web.admin.historyImport." + key, args);
    private async Task LoadAsync()
    {
        var pageOwner = Session.Owner;
        _host.Content.Children.Clear(); _host.Content.Children.Add(Text(H("page.loading")));
        var capabilities = await Session.ReadAsync("capabilities", lease => _api.ImportCapabilitiesAsync(lease));
        if (!Session.IsCurrent(pageOwner)) return; _capabilities = capabilities;
        if (!_capabilities.Flag("available") || !_capabilities.Flag("guarded_configuration") || !_capabilities.Flag("durable_runs"))
        { _host.Content.Children.Clear(); _host.Content.Children.Add(Text(H("page.unavailable"))); return; }
        var sources = await Session.ReadAsync("sources", owner => _api.ConfigurationAsync(owner, cursor => _api.ListImportSourcesAsync(owner, cursor)));
        if (sources == null || !Session.IsCurrent(pageOwner)) return;
        _sources = sources;
        var selected = _route.Get("source");
        if (!_sources.Any(x => x.Text("id") == selected)) selected = _sources.FirstOrDefault().Text("id");
        if (!string.IsNullOrEmpty(selected))
        {
            var source = await Session.ReadAsync("source", lease => _api.GetImportSourceAsync(lease, selected));
            if (source == null || !Session.IsCurrent(pageOwner)) return; _source = source;
            _route = _route.WithQuery("source", selected); AdminShellPage.TryReplace(this, _route);
        }
        RenderPage();
        if (_source != null) await LoadSourceDataAsync();
        if (Session.IsCurrent(pageOwner))
        {
            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; _poll.Tick += PollTick; _poll.Start();
        }
    }
    private void RenderPage()
    {
        _host.Content.Children.Clear(); _writes.Clear();
        _host.Content.Children.Add(LogsNativeUi.Header(H("page.title"), H("page.subtitle"), WriteButton(H("sourceBar.addServer"), "ImportAddSource", () => _ = SourceDialogAsync(null))));
        if (_sources.Count == 0) { _host.Content.Children.Add(Panel(Section(H("sourceBar.emptyTitle"), Text(H("sourceBar.emptyBody"))))); return; }
        var select = Select(H("sourceBar.selectServer"), _sources.Select(x => (x.Text("id"), x.Text("name") + " · " + x.Text("source_type"))), _source?.Value.Text("id") ?? "", id =>
        { if (_source?.Value.Text("id") != id) AdminShellPage.Open(new AdminRouteRequest(AdminRoute.HistoryImport).WithQuery("source", id)); });
        _host.Content.Children.Add(select);
        if (_source == null) return;
        var source = _source; var value = source.Value;
        _host.Content.Children.Add(Text(SafeSourceAddress(value.Text("base_url")), 12, true));
        var actions = Stack(8);
        actions.Children.Add(WriteButton(H("sourceBar.editServer"), "ImportEditSource", () => _ = SourceDialogAsync(source), source.CanWrite));
        actions.Children.Add(WriteButton(H("sourceBar.setApiKey"), "ImportToken", () => _ = TokenDialogAsync(source), source.CanWrite));
        actions.Children.Add(WriteButton(H("sourceBar.deleteServer"), "ImportDeleteSource", () => _ = DeleteSourceAsync(source), source.CanWrite));
        _host.Content.Children.Add(actions);
        _host.Content.Children.Add(Text(H(value.Flag("has_admin_token") ? "sourceBar.apiKeyConfigured" : "sourceBar.noApiKey"), 12, true));
        if (value.Flag("needs_reconfiguration")) _host.Content.Children.Add(Text(H("page.needsReconfiguration")));
        if (value.Text("source_type") == "plex")
            _host.Content.Children.Add(Panel(Section(H("plexLimits.title"), Text(H("plexLimits.intro"), 12), Text(H("plexLimits.resumePoints"), 12), Text(H("plexLimits.markedWatched"), 12), Text(H("plexLimits.watchlists"), 12), Text(H("plexLimits.outro"), 12))));
        if (_mappingPanel.Parent is Panel mp) mp.Children.Remove(_mappingPanel); if (_runPanel.Parent is Panel rp) rp.Children.Remove(_runPanel);
        _host.Content.Children.Add(_mappingPanel); _host.Content.Children.Add(_runPanel); RenderMappings(); RenderRuns(); UpdateActions();
    }
    private async Task LoadSourceDataAsync()
    {
        if (_source == null || _loadingSourceData) return;
        _loadingSourceData = true;
        var source = _source; var pageOwner = Session.Owner; var revision = ++_sourceDataVersion;
        try
        {
        if (source.Value.Flag("has_admin_token") && !source.Value.Flag("needs_reconfiguration"))
        {
            var mappings = await Session.ReadAsync("mappings", owner => _api.ConfigurationAsync(owner, cursor => _api.ListImportMappingsAsync(owner, source.Value.Text("id"), cursor)));
            if (!Session.IsCurrent(pageOwner) || revision != _sourceDataVersion) return;
            _mappingsReady = mappings != null; if (mappings != null) _mappings = mappings;
        }
        var page = await Session.ReadAsync("runs", owner => _api.ListImportRunsAsync(owner, source.Value.Text("id")));
        if (!Session.IsCurrent(pageOwner) || revision != _sourceDataVersion) return;
        if (page != null) { _runs.Clear(); _runs.AddRange(page.Items); _nextRun = page.NextCursor; _runCursors.Clear(); }
        if (_source != source || Session.Owner is not { IsCurrent: true }) return;
        RenderMappings(); RenderRuns();
        }
        finally { if (revision == _sourceDataVersion) _loadingSourceData = false; }
    }
    private PasswordBox Secret(string label)
    {
        var secret = new PasswordBox { Header = label, PasswordRevealMode = PasswordRevealMode.Hidden, MaxLength = 65536 };
        _secrets.Add(secret); return secret;
    }
    private async Task SourceDialogAsync(AdminResourceDocument? editor)
    {
        var name = Input(H("sourceDialog.name"), "ImportSourceName", editor?.Value.Text("name") ?? "");
        var url = Input(H("sourceDialog.serverUrl"), "ImportSourceUrl", editor?.Value.Text("base_url") ?? "");
        var type = editor?.Value.Text("source_type") ?? "jellyfin"; var enabled = editor?.Value.Flag("enabled") ?? true;
        var kind = Select(H("sourceDialog.type"), [("jellyfin", "Jellyfin"), ("emby", "Emby"), ("plex", "Plex")], type, value => type = value); kind.IsEnabled = editor == null;
        var credential = Secret(editor == null ? H("tokenDialog.tokenPlaceholder") : H("sourceDialog.replacementCredential"));
        var clear = false; var username = Input(H("sourceDialog.plexUser"), "ImportPlexUser", ""); var password = Secret(H("sourceDialog.plexPassword"));
        var login = false;
        var fields = Stack(12); fields.Children.Add(name); fields.Children.Add(kind); fields.Children.Add(url);
        fields.Children.Add(Toggle(H("sourceDialog.enabled"), enabled, value => enabled = value));
        fields.Children.Add(credential);
        if (editor != null) { fields.Children.Add(Toggle(H("sourceDialog.clearCredential"), false, value => { clear = value; credential.IsEnabled = !value; })); fields.Children.Add(Text(H("sourceDialog.addressChangeHint"), 12, true)); }
        else
        {
            fields.Children.Add(Toggle(H("credential.signInWithPlex"), false, value => { login = value; username.IsEnabled = password.IsEnabled = value; credential.IsEnabled = !value; }));
            username.IsEnabled = password.IsEnabled = false; fields.Children.Add(username); fields.Children.Add(password);
        }
        try
        {
            var saved = await _host.SaveDialogAsync(H(editor == null ? "sourceDialog.titleAdd" : "sourceDialog.titleEdit"), fields, T("common.save"), () => !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(url.Text), async () =>
            {
                return await Session.WriteAsync(async owner =>
                {
                    var token = credential.Password.Trim();
                    try
                    {
                        if (login)
                        {
                            if (type != "plex") throw new ArgumentException("Plex sign-in requires a Plex source.");
                            token = await _api.ImportPlexLoginAsync(owner, username.Text.Trim(), password.Password); password.Password = "";
                        }
                        var body = new JsonObject { ["name"] = name.Text.Trim(), ["base_url"] = url.Text.Trim(), ["enabled"] = enabled };
                        if (editor == null) { body["source_type"] = type; body["sort_order"] = 0; }
                        if (clear) body["admin_token"] = ""; else if (token.Length > 0) body["admin_token"] = token;
                        if (editor == null) await _api.CreateImportSourceAsync(owner, JsonSerializer.SerializeToElement(body));
                        else await _api.SaveImportSourceAsync(owner, editor, JsonSerializer.SerializeToElement(body));
                        body.Remove("admin_token");
                    }
                    finally { token = ""; credential.Password = ""; password.Password = ""; }
                });
            });
            if (saved) await _host.ReloadAsync();
        }
        finally { credential.Password = password.Password = ""; _secrets.Remove(credential); _secrets.Remove(password); username.Text = ""; }
    }
    private async Task TokenDialogAsync(AdminResourceDocument source)
    {
        var token = Secret(H("tokenDialog.tokenPlaceholder")); var username = Input(H("sourceDialog.plexUser"), "ImportTokenPlexUser", ""); var password = Secret(H("sourceDialog.plexPassword"));
        var useLogin = false; var remove = false; var fields = Stack(12); fields.Children.Add(token);
        if (source.Value.Text("source_type") == "plex") { fields.Children.Add(Toggle(H("credential.signInWithPlex"), false, value => useLogin = value)); fields.Children.Add(username); fields.Children.Add(password); }
        if (source.Value.Flag("has_admin_token")) fields.Children.Add(Toggle(H("tokenDialog.remove"), false, value => remove = value));
        try
        {
            var saved = await _host.SaveDialogAsync(H("sourceBar.setApiKey"), fields, T("common.save"), () => remove || useLogin && username.Text.Trim().Length > 0 && password.Password.Length > 0 || token.Password.Trim().Length > 0, async () =>
            {
                return await Session.WriteAsync(async owner =>
                {
                    var credential = token.Password;
                    try
                    {
                        if (remove) await _api.ClearImportTokenAsync(owner, source);
                        else
                        {
                            if (useLogin) credential = await _api.ImportPlexLoginAsync(owner, username.Text.Trim(), password.Password);
                            await _api.SetImportTokenAsync(owner, source, credential);
                        }
                    }
                    finally { credential = ""; token.Password = password.Password = ""; }
                });
            });
            if (saved) await _host.ReloadAsync();
        }
        finally { token.Password = password.Password = ""; _secrets.Remove(token); _secrets.Remove(password); username.Text = ""; }
    }
    private async Task DeleteSourceAsync(AdminResourceDocument source)
    {
        if (await _host.ConfirmAsync(H("page.deleteDialog.title"), H("page.deleteDialog.description", ("name", source.Value.Text("name"))), H("page.deleteDialog.confirm")) &&
            await Session.WriteAsync(owner => _api.DeleteImportSourceAsync(owner, source))) await _host.ReloadAsync();
    }
    private Button WriteButton(string label, string id, Action command, bool enabled = true)
    {
        var owner = Session.Owner;
        var button = Button(label, id, () => { if (Session.IsCurrent(owner) && !Session.Busy && !Session.NeedsReview) command(); });
        button.Tag = enabled; _writes.Add(button); return button;
    }
    private void UpdateActions()
    {
        foreach (var button in _writes) button.IsEnabled = button.Tag is true && Session.Owner is { IsCurrent: true } && !Session.Busy && !Session.NeedsReview;
    }
    private void RenderMappings()
    {
        ForgetButtons(_mappingPanel);
        _mappingPanel.Children.Clear(); if (_source == null || !_source.Value.Flag("has_admin_token") || _source.Value.Flag("needs_reconfiguration")) return;
        var source = _source; var runnable = source.Value.Flag("enabled");
        _mappingPanel.Children.Add(Text(H("mappings.title"), 16));
        if (_capabilities.Flag("played_lookback") && source.Value.Text("source_type") == "jellyfin")
        {
            var choices = new List<(string, string)> { ("full", H("lookback.full")) };
            foreach (var (hours, key) in new[] { (24, "last24Hours"), (72, "last72Hours"), (168, "last7Days"), (720, "last30Days") }) if (hours <= _capabilities.Number("max_lookback_hours")) choices.Add((hours.ToString(), H("lookback." + key)));
            _mappingPanel.Children.Add(Select(H("mappings.lookbackLabel"), choices, _lookback?.ToString() ?? "full", value => _lookback = int.TryParse(value, out var hours) ? hours : null));
        }
        _mappingPanel.Children.Add(WriteButton(H("mappings.discoverUsers"), "ImportDiscover", () => _ = DiscoverAsync(source), runnable && _mappingsReady));
        if (!_mappingsReady) { _mappingPanel.Children.Add(Text(H("page.mappingsFailed"))); _mappingPanel.Children.Add(Button(H("page.retryMappings"), "ImportRetryMappings", () => _ = LoadSourceDataAsync())); UpdateActions(); return; }
        if (_mappings.Count == 0) _mappingPanel.Children.Add(Text(H("mappings.empty"), 12, true));
        else
        {
            var bulk = WriteButton(H("mappings.importAll"), "ImportBulk", () => _ = BulkAsync(source), runnable && _mappings.Count <= Math.Min(200, _capabilities.Number("max_bulk_mappings")));
            _mappingPanel.Children.Add(bulk);
            if (_mappings.Count > 200) _mappingPanel.Children.Add(Text(H("mappings.bulkLimit"), 12));
            foreach (var mapping in _mappings.Take(_mappingVisible))
            {
                var row = Section(mapping.Text("external_user_name", mapping.Text("external_user_id")), Text(H("userFallback", ("id", mapping.Text("silo_user_id"))) + " · " + mapping.Text("silo_profile_id"), 12, true));
                row.Children.Add(WriteButton(H("mappings.runImport"), "ImportRun_" + mapping.Text("id"), () => _ = RunAsync(source, mapping), runnable));
                row.Children.Add(WriteButton(H("mappings.removeMapping"), "ImportRemove_" + mapping.Text("id"), () => _ = DeleteMappingAsync(mapping))); _mappingPanel.Children.Add(Panel(row));
            }
            if (_mappings.Count > _mappingVisible) _mappingPanel.Children.Add(Button("Show more mappings", "ImportMoreMappings", () => { _mappingVisible += 50; RenderMappings(); }));
        }
        if (_bulk.ValueKind == JsonValueKind.Object)
        {
            _mappingPanel.Children.Add(Text(H("mappings.bulkSummary", ("accepted", _bulk.Number("accepted")), ("active", _bulk.Number("active")), ("failed", _bulk.Number("failed"))), 12));
            foreach (var outcome in _bulk.Rows("outcomes")) _mappingPanel.Children.Add(Text(H("mappings.bulkOutcome", ("id", outcome.Text("mapping_id")), ("status", outcome.Text("status"))), 12));
        }
        UpdateActions();
    }
    private async Task DiscoverAsync(AdminResourceDocument source)
    {
        var pageOwner = Session.Owner;
        var external = await Session.ReadAsync("discovery", owner => _api.ConfigurationAsync(owner, cursor => _api.DiscoverImportUsersAsync(owner, source.Value.Text("id"), cursor)));
        if (external == null || !Session.IsCurrent(pageOwner)) return;
        var accounts = await Session.ReadAsync("accounts", owner => _api.ConfigurationAsync(owner, cursor => _api.ListAccountsAsync(owner, cursor)));
        if (accounts == null || !Session.IsCurrent(pageOwner) || !ReferenceEquals(_source, source)) return; _accounts = accounts;
        var unmapped = external.Where(x => _mappings.All(m => m.Text("external_user_id") != x.Text("id"))).ToArray();
        var fields = Stack(12); var rows = Stack(4); var search = Input(H("discover.searchPlaceholder"), "ImportExternalSearch", ""); fields.Children.Add(search); fields.Children.Add(rows);
        JsonElement selected = default; var account = ""; var profile = ""; var profileOptions = Select(H("discover.profile"), [], "");
        var selectedText = Text(""); fields.Children.Add(selectedText);
        var accountOptions = Select(H("discover.siloUser"), _accounts.Select(x => (x.Text("id"), x.Text("username"))), "", value => _ = ProfilesAsync(value));
        async Task ProfilesAsync(string value)
        {
            account = value; profile = ""; profileOptions.Items.Clear(); profileOptions.IsEnabled = false;
            var profiles = await Session.ReadAsync("profiles", owner => _api.ConfigurationAsync(owner, cursor => _api.ListProfilesAsync(owner, value, cursor)));
            if (profiles == null || account != value || !Session.IsCurrent(pageOwner)) return;
            foreach (var row in profiles) profileOptions.Items.Add(new ComboBoxItem { Content = row.Text("name"), Tag = row.Text("id") });
            profileOptions.IsEnabled = true;
        }
        profileOptions.SelectionChanged += (_, _) => profile = (profileOptions.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        fields.Children.Add(accountOptions); fields.Children.Add(profileOptions);
        var count = 50;
        void Render()
        {
            rows.Children.Clear(); var matches = unmapped.Where(x => (x.Text("name") + " " + x.Text("id")).Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            foreach (var row in matches.Take(count)) rows.Children.Add(Button(row.Text("name") + " · " + row.Text("id"), "ImportExternal_" + row.Text("id"), () => { selected = row; selectedText.Text = row.Text("name"); }));
            if (matches.Length > count) rows.Children.Add(Button("Show more", "ImportExternalMore", () => { count += 50; Render(); }));
            if (matches.Length == 0) rows.Children.Add(Text(H(unmapped.Length == 0 ? "discover.allMapped" : "discover.noMatches", ("search", search.Text))));
        }
        search.TextChanged += (_, _) => Render(); Render();
        if (await _host.SaveDialogAsync(H("discover.title", ("name", source.Value.Text("name"))), fields, H("discover.saveMapping"), () => selected.ValueKind == JsonValueKind.Object && account.Length > 0 && profile.Length > 0, () =>
            Session.WriteAsync(async owner =>
            {
                var targetUser = account; var targetProfile = profile; var targetExternal = selected;
                var freshProfiles = await _api.ConfigurationAsync(owner, cursor => _api.ListProfilesAsync(owner, targetUser, cursor));
                if (account != targetUser || profile != targetProfile || selected.Text("id") != targetExternal.Text("id") || !freshProfiles.Any(x => x.Text("id") == targetProfile)) throw new InvalidOperationException("The selected account or profile changed. Review the mapping again.");
                await _api.CreateImportMappingAsync(owner, source.Value.Text("id"), targetExternal.Text("id"), targetExternal.Text("name"), targetUser, targetProfile);
            }))) await LoadSourceDataAsync();
    }
    private async Task DeleteMappingAsync(JsonElement mapping)
    {
        var pageOwner = Session.Owner;
        var editor = await Session.ReadAsync("mapping", owner => _api.GetImportMappingAsync(owner, mapping.Text("id")));
        if (editor == null || !Session.IsCurrent(pageOwner) || editor.Value.Text("source_id") != _source?.Value.Text("id")) return;
        if (await _host.ConfirmAsync(H("mappings.removeDialog.title"), H("mappings.removeDialog.description", ("name", mapping.Text("external_user_name", mapping.Text("external_user_id")))), H("mappings.removeDialog.confirm")) &&
            await Session.WriteAsync(owner => _api.DeleteImportMappingAsync(owner, editor))) await LoadSourceDataAsync();
    }
    private async Task RunAsync(AdminResourceDocument source, JsonElement mapping)
    {
        if (await Session.WriteAsync(async owner =>
        {
            var current = await _api.GetImportMappingAsync(owner, mapping.Text("id"));
            foreach (var key in new[] { "source_id", "external_user_id", "silo_user_id", "silo_profile_id" })
                if (current.Value.Text(key) != mapping.Text(key)) throw new InvalidOperationException("The import mapping changed. Reload and review its target before starting.");
            var receipt = await _api.StartImportAsync(owner, mapping.Text("id"), source.Value.Text("source_type"), _lookback, _capabilities.Flag("played_lookback"), _capabilities.Number("max_lookback_hours"));
            _nextPoll = DateTimeOffset.UtcNow + receipt.RetryAfter;
        })) await LoadSourceDataAsync();
    }
    private async Task BulkAsync(AdminResourceDocument source)
    {
        if (await Session.WriteAsync(async owner => _bulk = await _api.BulkImportAsync(owner, source.Value.Text("id"), source.Value.Text("source_type"), _lookback, _capabilities.Flag("played_lookback"), _capabilities.Number("max_lookback_hours")))) await LoadSourceDataAsync();
    }
    private void RenderRuns()
    {
        ForgetButtons(_runPanel);
        _runPanel.Children.Clear(); if (_runs.Count == 0) return;
        _runPanel.Children.Add(Text(H("runs.title"), 16));
        _runPanel.Children.Add(Select("Import runs", [("all", H("runs.filter.all")), ("admin", H("runs.filter.admin")), ("user", H("runs.filter.user"))], _runFilter, value => { _runFilter = value; RenderRuns(); }));
        foreach (var run in _runs.Where(x => _runFilter == "all" || (_runFilter == "admin") == (x.Text("connection_mode") == "admin_token")))
        {
            var id = run.Text("id"); var status = run.Text("status");
            var body = Stack(8); body.Children.Add(Button((status is "queued" or "running" or "canceling" or "completed" or "failed" or "cancelled" ? H("status." + status) : status) + " · " + H("runs.sourceImport", ("source", run.Text("source_type"))), "ImportExpand_" + id, () => { _expanded = _expanded == id ? null : id; RenderRuns(); }));
            body.Children.Add(Text((run.Text("connection_mode") == "admin_token" ? H("runs.badgeAdmin") : H("runs.badgeSelf")) + " · " + H("userFallback", ("id", run.Text("user_id"))) + " · " + run.Text("created_at"), 12, true));
            if (run.Text("played_since") is { Length: > 0 } since) body.Children.Add(Text("Played since " + since, 12, true));
            if (run.Flag("cancelable")) body.Children.Add(WriteButton(H("runs.cancel"), "ImportCancel_" + id, () => _ = CancelAsync(id)));
            if (_expanded == id)
            {
                foreach (var (key, text) in new[] { ("fetched", "fetched"), ("matched", "matched"), ("unmatched", "unmatched"), ("progress_updated", "updated"), ("history_created", "history"), ("favorites_imported", "favorites"), ("watchlist_added", "watchlist"), ("skipped", "skipped") }) body.Children.Add(Text(H("runs.stat." + text) + ": " + run.Number(key), 12));
                if (run.Text("error_message").Length > 0) body.Children.Add(Text("The import failed. Review the source, credential and target profile before trying again.", 12));
                foreach (var warning in run.Rows("warnings").Take(5)) body.Children.Add(Text(SafeDiagnostic(warning.ValueKind == JsonValueKind.String ? warning.GetString() ?? "" : ""), 12));
                foreach (var sample in run.Rows("unmatched_samples").Take(10)) body.Children.Add(Text(H("runs.sample", ("title", sample.Text("title")), ("reason", SafeDiagnostic(sample.Text("reason")))), 12));
                if (run.Text("error_message").Length == 0 && run.Rows("warnings").Count == 0 && run.Rows("unmatched_samples").Count == 0) body.Children.Add(Text(H("runs.noIssues"), 12, true));
            }
            _runPanel.Children.Add(Panel(body));
        }
        if (_nextRun != null && _runs.Count < 500) _runPanel.Children.Add(Button(H("runs.loadMore"), "ImportMoreRuns", () => _ = MoreRunsAsync()));
        if (_nextRun != null && _runs.Count >= 500) _runPanel.Children.Add(Text("Showing 500 recent runs. Earlier runs remain on the server.", 12, true));
        UpdateActions();
    }
    private async Task MoreRunsAsync()
    {
        var cursor = _nextRun; var source = _source; if (cursor == null || source == null || _runs.Count >= 500) return;
        var page = await Session.ReadAsync("more-runs", owner => _api.ListImportRunsAsync(owner, source.Value.Text("id"), cursor));
        if (page == null || _source != source || _nextRun != cursor) return;
        if (!_runCursors.Add(cursor) || page.Items.Any(x => _runs.Any(old => old.Text("id") == x.Text("id")))) { Session.Report(new InvalidDataException()); return; }
        _runs.AddRange(page.Items); _nextRun = page.NextCursor; RenderRuns();
    }
    private async void PollTick(object? sender, object e)
    {
        var pageOwner = Session.Owner;
        if (_polling || _loadingSourceData || Session.Busy || Session.NeedsReview || Session.Owner is not { IsCurrent: true } || DateTimeOffset.UtcNow < _nextPoll || !_runs.Any(x => !x.Flag("terminal"))) return;
        _polling = true;
        try
        {
            // At most five visible active runs per tick; no unbounded task catalog or background catch-up.
            var active = _runs.Where(x => !x.Flag("terminal")).ToArray();
            var batch = active.Skip(_pollIndex % active.Length).Concat(active.Take(_pollIndex % active.Length)).Take(5).ToArray();
            _pollIndex = (_pollIndex + batch.Length) % active.Length;
            foreach (var run in batch)
            {
                if (!Session.IsCurrent(pageOwner)) return;
                var receipt = await Session.ReadAsync("run-" + run.Text("id"), owner => _api.GetImportRunAsync(owner, run.Text("id")));
                if (!Session.IsCurrent(pageOwner)) return;
                if (receipt == null) continue;
                var index = _runs.FindIndex(x => x.Text("id") == run.Text("id")); if (index >= 0) _runs[index] = receipt.Value;
                _nextPoll = DateTimeOffset.UtcNow + receipt.RetryAfter;
            }
            if (Session.Owner is { IsCurrent: true }) RenderRuns();
        }
        finally { _polling = false; }
    }
    private async Task CancelAsync(string id)
    {
        if (await Session.WriteAsync(async owner =>
        {
            var fresh = await _api.GetImportRunAsync(owner, id);
            if (!fresh.Value.Flag("cancelable")) throw new InvalidOperationException("This import run can no longer be canceled.");
            var receipt = await _api.CancelImportRunAsync(owner, id); _nextPoll = DateTimeOffset.UtcNow + receipt.RetryAfter;
        })) await LoadSourceDataAsync();
    }
    private static string SafeSourceAddress(string value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0 ? value : "Source address requires review";
    private void ForgetButtons(UIElement element)
    {
        if (element is Button button) _writes.Remove(button);
        if (element is Panel panel) foreach (var child in panel.Children) ForgetButtons(child);
        if (element is Border { Child: { } content }) ForgetButtons(content);
    }
    private static string SafeDiagnostic(string text) => text.Contains("://", StringComparison.Ordinal) || text.Contains("token", StringComparison.OrdinalIgnoreCase) || text.Contains("password", StringComparison.OrdinalIgnoreCase) || text.Contains("secret", StringComparison.OrdinalIgnoreCase)
        ? "The source reported a diagnostic. Review its configuration and credentials." : text.Length > 512 ? text[..512] + "…" : text;
}
