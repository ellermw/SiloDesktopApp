using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminUserResourceUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Official MappingsSection, DiscoverDialog and RunsSection: user mappings, durable runs, bulk import and live progress.</summary>
public sealed partial class AdminHistoryImportPage
{
    private static readonly (int Hours, string Key)[] LookbackChoices = [(24, "last24Hours"), (72, "last72Hours"), (168, "last7Days"), (720, "last30Days")];
    private string LookbackLabel(int? hours) => hours == null ? H("lookback.full") : H("lookback." + LookbackChoices.First(x => x.Hours == hours).Key);

    private void RenderMappings()
    {
        ForgetButtons(_mappingPanel); _mappingPanel.Children.Clear();
        if (_source == null || !_source.Value.Flag("has_admin_token") || _source.Value.Flag("needs_reconfiguration")) return;
        var source = _source; var runnable = source.Value.Flag("enabled");
        var head = new Grid { ColumnSpacing = 12 }; head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        head.Children.Add(Text(H("mappings.title"), 16));
        var actions = Inline(8); Grid.SetColumn(actions, 1); head.Children.Add(actions); _mappingPanel.Children.Add(head);
        if (_mappingsFailed) { _mappingPanel.Children.Add(Text(H("page.mappingsFailed"))); _mappingPanel.Children.Add(Button(H("page.retryMappings"), "ImportRetryMappings", () => _ = LoadSourceDataAsync())); UpdateActions(); return; }
        if (!_mappingsReady) { _mappingPanel.Children.Add(Text(H("page.loadingMappings"), 12, true)); UpdateActions(); return; }
        // Only Jellyfin sources accept a lookback; the server refuses it elsewhere.
        var lookbackOffered = _capabilities.Flag("played_lookback") && source.Value.Text("source_type") == "jellyfin";
        if (!lookbackOffered) _lookback = null;
        if (_mappings.Count > 0 && lookbackOffered)
        {
            var choices = new List<(string, string)> { ("full", H("lookback.full")) };
            foreach (var (hours, key) in LookbackChoices) if (hours <= _capabilities.Number("max_lookback_hours")) choices.Add((hours.ToString(CultureInfo.InvariantCulture), H("lookback." + key)));
            if (_lookback != null && choices.All(x => x.Item1 != _lookback.Value.ToString(CultureInfo.InvariantCulture))) _lookback = null;
            var select = Select(H("mappings.lookbackLabel"), choices, _lookback?.ToString(CultureInfo.InvariantCulture) ?? "full", value => { _lookback = int.TryParse(value, out var hours) ? hours : null; RenderMappings(); }); select.MinWidth = 160; Id(select, "ImportLookback");
            actions.Children.Add(select);
        }
        if (_mappings.Count > 0) actions.Children.Add(WriteButton(H("mappings.importAll"), "ImportBulk", () => _ = BulkAsync(source), enabled: () => runnable && _mappings.Count <= Math.Min(200, Math.Max(_capabilities.Number("max_bulk_mappings"), 1))));
        actions.Children.Add(WriteButton(H("mappings.discoverUsers"), "ImportDiscover", () => _ = DiscoverAsync(source), accent: true, enabled: () => runnable && _mappingsReady));
        if (_bulk.ValueKind == JsonValueKind.Object)
        {
            var summary = Stack(2); summary.Children.Add(Text(H("mappings.bulkSummary", ("accepted", _bulk.Number("accepted")), ("active", _bulk.Number("active")), ("failed", _bulk.Number("failed"))), 12));
            foreach (var outcome in _bulk.Rows("outcomes"))
                summary.Children.Add(Text(outcome.Text("error") is { Length: > 0 } error ? H("mappings.bulkOutcomeWithError", ("id", outcome.Text("mapping_id")), ("status", outcome.Text("status")), ("error", SafeDiagnostic(error))) : H("mappings.bulkOutcome", ("id", outcome.Text("mapping_id")), ("status", outcome.Text("status"))), 12));
            _mappingPanel.Children.Add(summary);
        }
        if (_mappings.Count > 200) _mappingPanel.Children.Add(Text(H("mappings.bulkLimit"), 12));
        if (_mappings.Count == 0)
        {
            var empty = Stack(8); empty.Children.Add(Text(H("mappings.empty"), 12, true)); empty.Children.Add(WriteButton(H("mappings.discoverUsers"), "ImportDiscoverEmpty", () => _ = DiscoverAsync(source), enabled: () => runnable && _mappingsReady));
            _mappingPanel.Children.Add(Panel(empty)); UpdateActions(); return;
        }
        var table = Stack(0); var header = new Grid { Margin = new(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var sourceUser = Text(H("mappings.column.sourceUser"), 11, true); var siloUser = Text(H("mappings.column.siloUser"), 11, true); var actionsHead = Text(H("mappings.column.actions"), 11, true);
        Grid.SetColumn(siloUser, 1); Grid.SetColumn(actionsHead, 2); header.Children.Add(sourceUser); header.Children.Add(siloUser); header.Children.Add(actionsHead); table.Children.Add(header);
        var runLabel = _lookback is { } hoursChosen ? H("mappings.runImportWithLookback", ("lookback", LookbackLabel(hoursChosen))) : H("mappings.runImport");
        foreach (var mapping in _mappings.Take(_mappingVisible))
        {
            var id = mapping.Text("id"); var external = mapping.Text("external_user_name", mapping.Text("external_user_id"));
            var row = new Grid { ColumnSpacing = 12, Margin = new(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var from = Text(external, 14); var to = Text(AccountName(mapping.Text("silo_user_id")), 14); Grid.SetColumn(to, 1);
            var buttons = Inline(4); Grid.SetColumn(buttons, 2);
            var run = WriteButton(runLabel, "ImportRun_" + id, () => _ = RunAsync(source, mapping), enabled: () => runnable);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(run, runLabel + ", " + external);
            var remove = WriteButton(H("mappings.removeMapping"), "ImportRemove_" + id, () => _ = DeleteMappingAsync(mapping));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, H("mappings.removeMapping") + ", " + external);
            buttons.Children.Add(run); buttons.Children.Add(remove); row.Children.Add(from); row.Children.Add(to); row.Children.Add(buttons); table.Children.Add(row);
        }
        _mappingPanel.Children.Add(Panel(table));
        if (_mappings.Count > _mappingVisible) _mappingPanel.Children.Add(Button("Show more mappings", "ImportMoreMappings", () => { _mappingVisible += 50; RenderMappings(); }));
        UpdateActions();
    }

    /// <summary>The source's users, then a dialog that stays open so several can be mapped in turn. Every save re-checks the chosen account and profile.</summary>
    private async Task DiscoverAsync(AdminResourceDocument source)
    {
        var pageOwner = Session.Owner; var sourceId = source.Value.Text("id");
        var external = await Session.ReadAsync("discovery", owner => _api.ConfigurationAsync(owner, cursor => _api.DiscoverImportUsersAsync(owner, sourceId, cursor)));
        if (external == null || !Session.IsCurrent(pageOwner)) return; // the failure line says why; Discover users stays available to try again
        var accounts = await Session.ReadAsync("accounts", owner => _api.ConfigurationAsync(owner, cursor => _api.ListAccountsAsync(owner, cursor)));
        if (accounts == null || !Session.IsCurrent(pageOwner) || !ReferenceEquals(_source, source)) return;
        _accounts = accounts;
        var panel = Stack(10); var summary = Text("", 12, true); var search = Input(H("discover.searchPlaceholder"), "ImportExternalSearch", ""); search.PlaceholderText = H("discover.searchPlaceholder");
        var rows = Stack(2); var form = Stack(8); var count = 50; JsonElement selected = default; var account = ""; var profile = "";
        ComboBox profileSelect = null!;
        var accountSelect = Select(H("discover.siloUser"), _accounts.OrderBy(x => x.Text("username"), StringComparer.CurrentCultureIgnoreCase).Select(x => (x.Text("id"), x.Text("username"))), "", value => _ = LoadProfilesAsync(value));
        accountSelect.PlaceholderText = H("discover.selectUser"); Id(accountSelect, "ImportSiloUser");
        profileSelect = Select(H("discover.profile"), [], ""); profileSelect.PlaceholderText = H("discover.selectProfile"); profileSelect.IsEnabled = false; Id(profileSelect, "ImportSiloProfile");
        profileSelect.SelectionChanged += (_, _) => { profile = (profileSelect.SelectedItem as ComboBoxItem)?.Tag as string ?? ""; };
        var prompt = Text("", 14); var saveRow = Inline(8); var mapped = new HashSet<string>(_mappings.Select(x => x.Text("external_user_id")), StringComparer.Ordinal);
        Button? save = null;
        async Task LoadProfilesAsync(string value)
        {
            account = value; profile = ""; profileSelect.Items.Clear(); profileSelect.IsEnabled = false;
            var profiles = await Session.ReadAsync("profiles", owner => _api.ConfigurationAsync(owner, cursor => _api.ListProfilesAsync(owner, value, cursor)));
            if (profiles == null || account != value || !Session.IsCurrent(pageOwner)) return;
            foreach (var row in profiles) profileSelect.Items.Add(new ComboBoxItem { Content = row.Text("name"), Tag = row.Text("id") });
            profileSelect.IsEnabled = true;
        }
        void Render()
        {
            var unmapped = external.Where(x => !mapped.Contains(x.Text("id"))).ToArray(); rows.Children.Clear();
            summary.Text = unmapped.Length == 0 ? H("discover.allMapped") : H("discover.unmappedFound", ("count", unmapped.Length), ("count_text", unmapped.Length));
            search.Visibility = unmapped.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var needle = search.Text.Trim(); var matches = unmapped.Where(x => needle.Length == 0 || x.Text("name").Contains(needle, StringComparison.CurrentCultureIgnoreCase) || x.Text("id").Contains(needle, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            if (unmapped.Length > 0 && matches.Length == 0) rows.Children.Add(Text(H("discover.noMatches", ("search", search.Text)), 12, true));
            foreach (var row in matches.Take(count))
            {
                var user = row; var line = Stack(0); line.Children.Add(Text(user.Text("name"), 14)); line.Children.Add(Truncated(user.Text("id"), 11, true));
                var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.Children.Add(line);
                var chosen = selected.ValueKind == JsonValueKind.Object && selected.Text("id") == user.Text("id"); if (chosen) { var badge = Chip(H("discover.selected")); Grid.SetColumn(badge, 1); content.Children.Add(badge); }
                var button = Id(new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(12, 8, 12, 8), BorderThickness = new(0), Background = chosen ? Brush("SurfaceHoverBrush") : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) }, "ImportExternal_" + user.Text("id"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, user.Text("name") + ", " + user.Text("id"));
                button.Click += (_, _) => { selected = user; account = ""; profile = ""; accountSelect.SelectedItem = null; profileSelect.Items.Clear(); profileSelect.IsEnabled = false; Render(); };
                rows.Children.Add(button);
            }
            if (matches.Length > count) rows.Children.Add(Button("Show more", "ImportExternalMore", () => { count += 50; Render(); }));
            form.Visibility = selected.ValueKind == JsonValueKind.Object ? Visibility.Visible : Visibility.Collapsed;
            prompt.Text = selected.ValueKind == JsonValueKind.Object ? H("discover.mapPrompt", ("name", Safe(selected.Text("name"))), ("highlight", "")) : "";
        }
        search.TextChanged += (_, _) => { count = 50; Render(); };
        save = Button(H("discover.saveMapping"), "ImportSaveMapping", () => _ = SaveMappingAsync(), accent: true);
        async Task SaveMappingAsync()
        {
            if (selected.ValueKind != JsonValueKind.Object || account.Length == 0 || profile.Length == 0 || Session.Busy || Session.NeedsReview) return;
            var target = selected; var targetUser = account; var targetProfile = profile;
            var ok = await Session.WriteAsync(async owner =>
            {
                var freshProfiles = await _api.ConfigurationAsync(owner, cursor => _api.ListProfilesAsync(owner, targetUser, cursor));
                if (account != targetUser || profile != targetProfile || selected.Text("id") != target.Text("id") || !freshProfiles.Any(x => x.Text("id") == targetProfile)) throw new InvalidOperationException("The selected account or profile changed. Review the mapping again.");
                await _api.CreateImportMappingAsync(owner, sourceId, target.Text("id"), target.Text("name"), targetUser, targetProfile);
            });
            if (!Session.IsCurrent(pageOwner) || !ok) return;
            mapped.Add(target.Text("id")); selected = default; account = ""; profile = ""; accountSelect.SelectedItem = null; profileSelect.Items.Clear(); profileSelect.IsEnabled = false; Render();
            await LoadSourceDataAsync();
        }
        saveRow.Children.Add(save); saveRow.Children.Add(Button(T("common.cancel"), "ImportCancelMapping", () => { selected = default; Render(); }));
        form.Children.Add(prompt); form.Children.Add(Columns(accountSelect, profileSelect)); form.Children.Add(saveRow);
        panel.Children.Add(Text(H("discover.intro"), 12, true)); panel.Children.Add(summary); panel.Children.Add(search); panel.Children.Add(rows); panel.Children.Add(form); Render();
        await _host.ToolDialogAsync(H("discover.title", ("name", Safe(source.Value.Text("name")))), panel, T("common.done"), () => ReferenceEquals(_source, source));
    }
    private async Task DeleteMappingAsync(JsonElement mapping)
    {
        var pageOwner = Session.Owner; var sourceId = _source?.Value.Text("id"); var mappings = _mappings;
        var editor = await Session.ReadAsync("mapping", owner => _api.GetImportMappingAsync(owner, mapping.Text("id")));
        if (editor == null || !Session.IsCurrent(pageOwner) || editor.Value.Text("source_id") != sourceId) return;
        if (!await _host.ConfirmAsync(H("mappings.removeDialog.title"), H("mappings.removeDialog.description", ("name", Safe(mapping.Text("external_user_name", mapping.Text("external_user_id"))))), H("mappings.removeDialog.confirm"), destructive: true, stillCurrent: () => ReferenceEquals(_mappings, mappings) && _source?.Value.Text("id") == sourceId) || !Session.IsCurrent(pageOwner)) return;
        if (await Session.WriteAsync(owner => _api.DeleteImportMappingAsync(owner, editor)) && Session.IsCurrent(pageOwner)) await LoadSourceDataAsync();
    }
    private async Task RunAsync(AdminResourceDocument source, JsonElement mapping)
    {
        var owner = Session.Owner;
        if (await Session.WriteAsync(async lease =>
        {
            var current = await _api.GetImportMappingAsync(lease, mapping.Text("id"));
            foreach (var key in new[] { "source_id", "external_user_id", "silo_user_id", "silo_profile_id" })
                if (current.Value.Text(key) != mapping.Text(key)) throw new InvalidOperationException("The import mapping changed. Reload and review its target before starting.");
            var receipt = await _api.StartImportAsync(lease, mapping.Text("id"), source.Value.Text("source_type"), _lookback, _capabilities.Flag("played_lookback"), _capabilities.Number("max_lookback_hours"));
            _nextPoll = DateTimeOffset.UtcNow + receipt.RetryAfter;
        }) && Session.IsCurrent(owner)) await LoadSourceDataAsync();
    }
    private async Task BulkAsync(AdminResourceDocument source)
    {
        var owner = Session.Owner;
        if (await Session.WriteAsync(async lease => _bulk = await _api.BulkImportAsync(lease, source.Value.Text("id"), source.Value.Text("source_type"), _lookback, _capabilities.Flag("played_lookback"), _capabilities.Number("max_lookback_hours"))) && Session.IsCurrent(owner)) await LoadSourceDataAsync();
    }

    // ---------- runs ----------
    private static string StatusText(string status) => status is "queued" or "running" or "canceling" or "completed" or "failed" or "cancelled" ? H("status." + status) : status;
    private void RenderRuns()
    {
        ForgetButtons(_runPanel); _runPanel.Children.Clear();
        if (_runs.Count == 0 && !_runsFailed) return;
        var head = new Grid { ColumnSpacing = 12 }; head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        head.Children.Add(Text(H("runs.title"), 16));
        var filter = Segmented(H("runs.title"), [("all", H("runs.filter.all")), ("admin", H("runs.filter.admin")), ("user", H("runs.filter.user"))], _runFilter, value => { _runFilter = value; RenderRuns(); }, "ImportRunFilter"); Grid.SetColumn(filter, 1); head.Children.Add(filter);
        _runPanel.Children.Add(head);
        var visible = _runs.Where(x => _runFilter == "all" || (_runFilter == "admin") == (x.Text("connection_mode") == "admin_token")).ToArray();
        var list = Stack(0);
        if (visible.Length == 0) list.Children.Add(Text(H("runs.empty", ("filter", _runFilter)), 12, true));
        foreach (var run in visible)
        {
            var id = run.Text("id"); var status = run.Text("status"); var line = Stack(6); line.Margin = new(0, 6, 0, 6);
            var top = new Grid { ColumnSpacing = 10 }; for (var i = 0; i < 4; i++) top.ColumnDefinitions.Add(new() { Width = i == 1 ? new(1, GridUnitType.Star) : GridLength.Auto });
            var expand = Button(_expanded == id ? "▾" : "▸", "ImportExpand_" + id, () => { _expanded = _expanded == id ? null : id; RenderRuns(); }); expand.MinWidth = 36;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(expand, T("web.admin.activity.row.details") + ", " + H("runs.sourceImport", ("source", run.Text("source_type")))); expand.Tag = null;
            var summary = Stack(1); var titleRow = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 }; titleRow.Children.Add(Chip(StatusText(status), status is "failed" ? "ErrorBrush" : status is "completed" ? "SuccessBrush" : status is "running" or "canceling" ? "WarningBrush" : null));
            titleRow.Children.Add(Text(H("runs.sourceImport", ("source", run.Text("source_type"))), 14)); titleRow.Children.Add(Chip(run.Text("connection_mode") == "admin_token" ? H("runs.badgeAdmin") : H("runs.badgeSelf")));
            summary.Children.Add(titleRow);
            var meta = run.Text("played_since") is { Length: > 0 } since ? H("runs.metaWithPlayedSince", ("user", Safe(AccountName(run.Text("user_id")))), ("created", FormatDate(run.Text("created_at"))), ("since", FormatDate(since)))
                : H("runs.meta", ("user", Safe(AccountName(run.Text("user_id")))), ("created", FormatDate(run.Text("created_at"))));
            summary.Children.Add(Text(meta, 12, true));
            var counts = Inline(12);
            if (run.Number("fetched") > 0) counts.Children.Add(Text(H("runs.fetchedCount", ("count", run.Number("fetched")), ("count_text", run.Number("fetched"))), 12, true));
            if (run.Number("matched") > 0) { var matched = Text(H("runs.matchedCount", ("count", run.Number("matched")), ("count_text", run.Number("matched"))), 12); matched.Foreground = Brush("SuccessBrush"); counts.Children.Add(matched); }
            if (run.Number("unmatched") > 0) { var unmatched = Text(H("runs.unmatchedCount", ("count", run.Number("unmatched")), ("count_text", run.Number("unmatched"))), 12); unmatched.Foreground = Brush("WarningBrush"); counts.Children.Add(unmatched); }
            Grid.SetColumn(summary, 1); Grid.SetColumn(counts, 2); top.Children.Add(expand); top.Children.Add(summary); top.Children.Add(counts);
            if (run.Flag("cancelable")) { var cancel = WriteButton(H("runs.cancel"), "ImportCancel_" + id, () => _ = CancelAsync(id)); Grid.SetColumn(cancel, 3); top.Children.Add(cancel); }
            line.Children.Add(top);
            if (_expanded == id) line.Children.Add(RunDetails(run));
            list.Children.Add(line);
        }
        _runPanel.Children.Add(Panel(list));
        if (_runsFailed) { var failed = Text(H("runs.refreshFailed"), 12); failed.Foreground = Brush("ErrorBrush"); _runPanel.Children.Add(failed); _runPanel.Children.Add(Button(T("common.retry"), "ImportRetryRuns", () => _ = LoadSourceDataAsync())); }
        if (_nextRun != null && _runs.Count < 500) _runPanel.Children.Add(Button(H("runs.loadMore"), "ImportMoreRuns", () => _ = MoreRunsAsync()));
        if (_nextRun != null && _runs.Count >= 500) _runPanel.Children.Add(Text("Showing 500 recent runs. Earlier runs remain on the server.", 12, true));
        UpdateActions();
    }
    private static string FormatDate(string value) => DateTimeOffset.TryParse(value, out var at) ? SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(at) : "—";
    private FrameworkElement RunDetails(JsonElement run)
    {
        var details = Stack(8); details.Margin = new(36, 0, 0, 0);
        if (run.Text("error_message") is { Length: > 0 } error) { var box = Text(SafeDiagnostic(error), 13); box.Foreground = Brush("ErrorBrush"); details.Children.Add(box); }
        var stats = new WrapPanel { HorizontalSpacing = 24, VerticalSpacing = 8 };
        foreach (var (key, label) in new[] { ("fetched", "fetched"), ("matched", "matched"), ("unmatched", "unmatched"), ("progress_updated", "updated"), ("history_created", "history"), ("favorites_imported", "favorites"), ("watchlist_added", "watchlist"), ("skipped", "skipped") })
        {
            var cell = Stack(0); cell.Children.Add(Text(H("runs.stat." + label), 11, true)); cell.Children.Add(Text(run.Number(key).ToString("N0", CultureInfo.CurrentCulture), 13)); stats.Children.Add(cell);
        }
        details.Children.Add(stats);
        var warnings = run.Rows("warnings");
        if (warnings.Count > 0)
        {
            details.Children.Add(Text(H("runs.warnings", ("count", warnings.Count), ("count_text", warnings.Count)), 12, true));
            foreach (var warning in warnings.Take(5)) details.Children.Add(Text("• " + SafeDiagnostic(warning.ValueKind == JsonValueKind.String ? warning.GetString() ?? "" : ""), 12, true));
            if (warnings.Count > 5) details.Children.Add(Text(H("runs.moreWarnings", ("count", warnings.Count - 5), ("count_text", warnings.Count - 5)), 12, true));
        }
        var samples = run.Rows("unmatched_samples");
        if (samples.Count > 0)
        {
            details.Children.Add(Text(H("runs.unmatchedSamples"), 12, true));
            foreach (var sample in samples.Take(10))
                details.Children.Add(Text("• " + (sample.Number("year") > 0 ? H("runs.sampleWithYear", ("title", sample.Text("title")), ("year", sample.Number("year")), ("reason", SafeDiagnostic(sample.Text("reason"))))
                    : H("runs.sample", ("title", sample.Text("title")), ("reason", SafeDiagnostic(sample.Text("reason"))))), 12, true));
        }
        if (run.Text("error_message").Length == 0 && warnings.Count == 0 && samples.Count == 0) details.Children.Add(Text(H("runs.noIssues"), 12, true));
        return details;
    }
    private async Task MoreRunsAsync()
    {
        var cursor = _nextRun; var source = _source; if (cursor == null || source == null || _runs.Count >= 500) return; var owner = Session.Owner;
        var page = await Session.ReadAsync("more-runs", lease => _api.ListImportRunsAsync(lease, source.Value.Text("id"), cursor));
        if (page == null || !Session.IsCurrent(owner) || _source != source || _nextRun != cursor) return;
        if (!_runCursors.Add(cursor) || page.Items.Any(x => _runs.Any(old => old.Text("id") == x.Text("id")))) { Session.Report(new InvalidDataException()); return; }
        _runs.AddRange(page.Items); _nextRun = page.NextCursor; RenderRuns();
    }
    /// <summary>At most five visible active runs per tick, honoring the server's retry hint. A failed poll keeps the data and the next tick tries again.</summary>
    private async void PollTick(object? sender, object e)
    {
        var pageOwner = Session.Owner;
        if (_polling || _loadingSourceData || Session.Busy || Session.NeedsReview || Session.Owner is not { IsCurrent: true } || DateTimeOffset.UtcNow < _nextPoll || !_runs.Any(x => !x.Flag("terminal"))) return;
        _polling = true; var revision = _sourceDataVersion;
        try
        {
            var active = _runs.Where(x => !x.Flag("terminal")).ToArray();
            var batch = active.Skip(_pollIndex % active.Length).Concat(active.Take(_pollIndex % active.Length)).Take(5).ToArray();
            _pollIndex = (_pollIndex + batch.Length) % active.Length; var changed = false;
            foreach (var run in batch)
            {
                if (!Session.IsCurrent(pageOwner) || revision != _sourceDataVersion) return;
                var receipt = await Session.ReadAsync("run-" + run.Text("id"), owner => _api.GetImportRunAsync(owner, run.Text("id")));
                if (!Session.IsCurrent(pageOwner) || revision != _sourceDataVersion) return;
                if (receipt == null) continue;
                var index = _runs.FindIndex(x => x.Text("id") == run.Text("id")); if (index >= 0) { _runs[index] = receipt.Value; changed = true; }
                _nextPoll = DateTimeOffset.UtcNow + receipt.RetryAfter;
            }
            if (changed && Session.Owner is { IsCurrent: true } && revision == _sourceDataVersion) RenderRuns();
        }
        finally { _polling = false; }
    }
    private async Task CancelAsync(string id)
    {
        var owner = Session.Owner;
        if (await Session.WriteAsync(async lease =>
        {
            var fresh = await _api.GetImportRunAsync(lease, id);
            if (!fresh.Value.Flag("cancelable")) throw new InvalidOperationException("This import run can no longer be canceled.");
            var receipt = await _api.CancelImportRunAsync(lease, id); _nextPoll = DateTimeOffset.UtcNow + receipt.RetryAfter;
        }) && Session.IsCurrent(owner)) await LoadSourceDataAsync();
    }
}
