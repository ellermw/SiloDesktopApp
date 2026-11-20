using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPolicyPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly AdminAccessPageUi _ui;
    private readonly Dictionary<string, Button> _tabs = [], _commands = [];
    private Microsoft.UI.Xaml.Media.Brush? _tabBackground;
    private readonly StackPanel _view = new() { Spacing = 16 }, _versions = new() { Spacing = 8 }, _review = new() { Spacing = 8 };
    private readonly TextBlock _validation = U.Text("", 12), _simulation = U.Text("", 12), _health = U.Text("", 12);
    private TextBox? _source, _comment, _input;
    private TextBlock? _name, _overrideCan, _rail;
    private string? _viewKey;
    private IReadOnlyList<AdminPolicyVersion>? _shownVersions;
    private bool _sync;
    private AdminRouteRequest _location = new(AdminRoute.Policy);
    public AdminPolicyController Controller { get; }
    public FrameworkElement ScrollContent => _ui.ScrollContent;
    public bool RequiresLeaveConfirmation => Controller.Dirty;
    public AdminPolicyPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Disabled;
        Controller = App.Services.GetRequiredService<AdminPolicyController>();
        _ui = new(this, Controller, T("title"), T("subtitle"), Render)
        { Reopen = () => Controller.OpenAsync(Controller.Tab, Controller.Snapshot?.Value.Id), RequiresDiscard = () => RequiresLeaveConfirmation };
        foreach (var (id, label) in new[] { ("documents", "overrides"), ("vendor", "baseline"), ("decisions", "decisions") })
        {
            var button = U.Action(T("tab." + label), "PolicyTab_" + id); _tabs[id] = button; _tabBackground = button.Background; _ui.Actions.Children.Add(button);
            button.Click += async (_, _) => { if (Controller.Busy) return; await Controller.SelectTabAsync(id); };
        }
        Loaded += async (_, _) => await Controller.OpenAsync(_location.Get("tab") ?? "documents", _location.Get("document")); Unloaded += (_, _) => Close();
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { _location = e.Parameter as AdminRouteRequest ?? new(AdminRoute.Policy); base.OnNavigatedTo(e); }
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
        if (!Controller.Current) { _viewKey = null; return; }
        foreach (var (id, button) in _tabs)
        {
            var selected = id == Controller.Tab; button.IsEnabled = Controller.Available && !Controller.Busy; AutomationProperties.SetHelpText(button, selected ? "Selected" : "");
            button.Background = selected ? U.Brush("MutedBrush") : _tabBackground; button.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
        if (!Controller.Available)
        {
            _ui.Fields.Children.Clear();
            if (Controller.Capability != null) _ui.Fields.Children.Add(AdminAccessPageUi.Stack(U.Text(T("unavailable.title"), 18), U.Text(T("unavailable.body"), 13, true)));
            return;
        }
        var key = Controller.Tab + ":" + Controller.Snapshot?.Value.Id + ":" + Controller.EditorRevision;
        if (_viewKey != key) Build(key);
        if (Controller.Tab == "documents" && Controller.Snapshot == null) RenderDocuments();
        else if (Controller.Tab == "documents") RenderEditor();
        else if (Controller.Tab == "vendor") RenderVendor();
        else RenderDecisions();
        _health.Text = Controller.Capability is { Degraded: true } caps ? "Policy service degraded: " + LocalLog.RedactDiagnosticText(caps.DegradedReason ?? "") + $" · generation {caps.Generation} · evaluation timeouts {caps.EvalTimeouts}" : "";
        var location = _location.WithQuery("tab", Controller.Tab == "documents" ? null : Controller.Tab).WithQuery("document", Controller.Tab == "documents" ? Controller.Snapshot?.Value.Id : null);
        if (_location.Href != location.Href && AdminShellPage.TryReplace(this, location)) _location = location;
    }
    private void Build(string key)
    {
        if (_viewKey != key) _ui.Scroller.ChangeView(null, 0, null, true);
        _viewKey = key; _commands.Clear(); _source = _comment = _input = null; _shownVersions = null;
        _ui.Fields.Children.Clear(); _view.Children.Clear(); _review.Children.Clear(); _versions.Children.Clear();
        _ui.Fields.Children.Add(_health);
        var pipeline = new StackPanel { Spacing = 8 };
        foreach (var step in new[] { "baseline", "overrides", "log" }) pipeline.Children.Add(AdminAccessPageUi.Stack(U.Text(T("pipeline." + step + ".title"), 14), U.Text(T("pipeline." + step + ".detail"), 12, true)));
        _ui.Fields.Children.Add(new Border { Child = pipeline, Padding = new(16), CornerRadius = new(16), Background = U.Brush("SecondaryBrush") }); _ui.Fields.Children.Add(_view);
        if (Controller.Tab == "documents" && Controller.Snapshot != null) BuildEditor();
    }
    private void RenderDocuments()
    {
        _view.Children.Clear();
        foreach (var domain in Controller.Capability!.DecisionTypes)
        {
            var card = new StackPanel { Spacing = 12 }; card.Children.Add(U.Text(Domain(domain), 18)); card.Children.Add(U.Text(T("domain." + domain + ".governs"), 13, true));
            var create = U.Action(T("overrides.new"), "PolicyNew_" + domain); create.IsEnabled = !Controller.Busy && !Controller.ReviewRequired; create.Click += async (_, _) => await CreateAsync(domain); card.Children.Add(create);
            var rows = Controller.Documents.Where(d => d.Domain == domain).ToArray();
            if (rows.Length == 0) card.Children.Add(U.Text(T("overrides.emptyExample", new { example = T("domain." + domain + ".example") }), 13, true));
            foreach (var document in rows)
            {
                var open = U.Action(document.Name, "PolicyDocument_" + document.Id); open.IsEnabled = !Controller.Busy;
                open.Click += async (_, _) => { if (!RequiresLeaveConfirmation || await CanLeaveAsync()) await Controller.SelectAsync(document.Id); };
                var rowToggle = U.Action(T(document.Enabled ? "enabled.disable" : "enabled.enable"), "PolicyRowEnabled_" + document.Id);
                rowToggle.IsEnabled = !Controller.Busy && !Controller.ReviewRequired; rowToggle.Click += async (_, _) => await ToggleRowAsync(document.Id);
                card.Children.Add(AdminAccessPageUi.Stack(open, U.Text(Status(document) + " · " + T("overrides.updated", new { date = FormatDate(document.UpdatedAt) }), 12, true), rowToggle));
            }
            _view.Children.Add(new Border { Child = card, Padding = new(20), CornerRadius = new(16), Background = U.Brush("SecondaryBrush") });
        }
        if (Controller.DocumentCursor != null) _view.Children.Add(Command(T("overrides.loadMore"), "PolicyMoreDocuments", Controller.MoreDocumentsAsync, !Controller.Busy && Controller.Documents.Count < 1000));
    }
    private async Task ToggleRowAsync(string id)
    {
        var owner = Controller.Owner; if (owner is not { IsCurrent: true }) return;
        await Controller.ReviewToggleAsync(id);
        var reviewed = Controller.Toggle; if (reviewed == null || reviewed.Value.Id != id || !ReferenceEquals(owner, Controller.Owner)) return;
        var target = !reviewed.Value.Enabled; var targetState = target ? "enabled" : "disabled";
        var confirmed = await _ui.ConfirmAsync(T(target ? "enabled.enable" : "enabled.disable"),
            T("enabled.confirmText", new { name = reviewed.Value.Name, state = AdminPolicyPresentation.EnabledState(reviewed), target = targetState }), T("enabled.confirm", new { target = targetState }));
        if (confirmed && ReferenceEquals(owner, Controller.Owner)) await Controller.ApplyToggleAsync(reviewed, target, true); else Controller.CancelToggle();
    }
    private static string FormatDate(string value) => DateTimeOffset.TryParse(value, out var date) ? SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(date) : value;
    private async Task CreateAsync(string domain)
    {
        var name = U.Input(T("overrides.nameLabel", new { domain = Domain(domain) }), "PolicyName", T("overrides.namePlaceholderExample", new { example = T("domain." + domain + ".example") })); name.MaxLength = 500;
        var error = U.Text("", 13);
        var dialog = new ContentDialog { Title = T("overrides.new"), Content = AdminAccessPageUi.Stack(U.Text(T("domain." + domain + ".governs")), name, error), PrimaryButtonText = T("overrides.create"), CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        var owner = Controller.Owner;
        dialog.Closing += (_, args) => { if (Controller.Busy && owner?.IsCurrent == true) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral(); dialog.IsPrimaryButtonEnabled = false;
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = T("overrides.nameRequired"); dialog.IsPrimaryButtonEnabled = true; return; }
                await Controller.CreateAsync(domain, name.Text, true);
                if (Controller.Snapshot != null) dialog.Hide(); else error.Text = Controller.Message ?? T("overrides.createFailed");
            }
            finally { deferral.Complete(); }
        };
        await _ui.ShowAsync(dialog);
    }
    private void BuildEditor()
    {
        _view.Children.Add(Command(T("allOverrides"), "PolicyAllOverrides", async () => { if (await CanLeaveAsync()) await Controller.SelectAsync(null); }));
        _name = U.Text(AdminPolicyPresentation.Title(Controller.Snapshot!), 22); _overrideCan = U.Text(AdminPolicyPresentation.OverrideCan(Controller.Snapshot!), 13, true); _view.Children.Add(_name); _view.Children.Add(_overrideCan);
        _rail = U.Text("", 12, true); AutomationProperties.SetName(_rail, T("editor.lifecycleLabel")); _view.Children.Add(_rail);
        _source = AdminAccessPageUi.Multiline("Rego source", "PolicySource", Controller.Draft, 360); _source.MaxLength = 262144; _source.FontFamily = new("Consolas");
        _source.TextChanging += (_, _) => { if (!_sync) Controller.SetDraft(AdminAccessPageUi.Lines(_source.Text)); }; _view.Children.Add(_source);
        _comment = U.Input(T("editor.commentLabel"), "PolicyComment", T("editor.commentPlaceholder")); _comment.Width = double.NaN; _comment.HorizontalAlignment = HorizontalAlignment.Stretch; _comment.MaxLength = 10000;
        _comment.TextChanging += (_, _) => { if (!_sync) Controller.SetComment(AdminAccessPageUi.Lines(_comment.Text)); }; _view.Children.Add(_comment);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Command(T("editor.validate"), "PolicyValidate", Controller.ValidateAsync));
        actions.Children.Add(Command(T("editor.save"), "PolicySave", async () =>
        {
            var owner = Controller.Owner;
            if (await _ui.ConfirmAsync(T("editor.save"), "Save this draft as a new immutable version? It will not be activated.", T("editor.save")) && ReferenceEquals(owner, Controller.Owner)) await Controller.SaveAsync(true);
        }));
        actions.Children.Add(Command("Discard", "PolicyDiscard", () => { Controller.Discard(); return Task.CompletedTask; })); _view.Children.Add(actions); _view.Children.Add(_validation);
        _view.Children.Add(_review);
        _view.Children.Add(Command(T("review.button"), "PolicyReview", Controller.ReviewAsync));
        _view.Children.Add(Command(T("editor.loadSaved"), "PolicyLoadSaved", async () => { if (await _ui.ConfirmAsync(T("editor.loadSaved"), T("editor.newerRevision"), T("editor.loadSaved"))) await Controller.LoadSavedSourceAsync(true); }));
        _view.Children.Add(Command(AdminPolicyPresentation.ToggleLabel(Controller.Snapshot!), "PolicyEnabled", async () =>
        {
            var reviewed = Controller.Snapshot; var owner = Controller.Owner; if (reviewed == null) return;
            var enabled = !reviewed.Value.Enabled;
            if (await _ui.ConfirmAsync(AdminPolicyPresentation.ToggleLabel(reviewed), T("enabled.confirmText", new { name = reviewed.Value.Name, state = reviewed.Value.Enabled ? "enabled" : "disabled", target = enabled ? "enabled" : "disabled" }), "Confirm") && ReferenceEquals(owner, Controller.Owner)) await Controller.SetEnabledAsync(reviewed, enabled, true);
        }));
        _view.Children.Add(Command("Delete override", "PolicyDelete", async () =>
        {
            var reviewed = Controller.Snapshot; var owner = Controller.Owner; if (reviewed == null) return;
            if (await _ui.ConfirmAsync("Delete override?", $"Delete “{reviewed.Value.Name}” and its saved history? This cannot be undone.", "Delete") && ReferenceEquals(owner, Controller.Owner)) await Controller.DeleteAsync(reviewed, true);
        }));
        _view.Children.Add(U.Text(T("simulate.title"), 18)); _view.Children.Add(U.Text(T("simulate.description"), 13, true));
        _input = AdminAccessPageUi.Multiline(T("simulate.input"), "PolicyInput", "{\n  \"schema_version\": 1,\n  \"request_time\": \"2026-07-02T16:00:00Z\"\n}", 180); _input.MaxLength = 262144;
        _view.Children.Add(_input); _view.Children.Add(Command(T("simulate.run"), "PolicySimulate", () => Controller.SimulateAsync(_input.Text))); _view.Children.Add(_simulation);
        _view.Children.Add(U.Text(T("history.selectedSource"), 18)); _view.Children.Add(_versions);
    }
    private void RenderEditor()
    {
        var editable = !Controller.Busy && Controller.Available;
        _sync = true;
        try { if (_source != null) { _source.IsEnabled = editable; if (AdminAccessPageUi.Lines(_source.Text) != Controller.Draft) _source.Text = Controller.Draft; } if (_comment != null) { _comment.IsEnabled = editable; if (AdminAccessPageUi.Lines(_comment.Text) != Controller.Comment) _comment.Text = Controller.Comment; } }
        finally { _sync = false; }
        foreach (var command in _commands.Values) command.IsEnabled = editable;
        if (_commands.TryGetValue("PolicySave", out var save)) save.IsEnabled = editable && Controller.Validated && !Controller.ReviewRequired;
        // Labels and titles are re-derived from the current snapshot on every render so an adopted revision never leaves the previous one on screen.
        if (Controller.Snapshot is { } current)
        {
            if (_name != null) _name.Text = AdminPolicyPresentation.Title(current);
            if (_overrideCan != null) _overrideCan.Text = AdminPolicyPresentation.OverrideCan(current);
            if (_rail != null) { var reached = AdminPolicyPresentation.LifecycleProgress(current, Controller.SavedVersion, Controller.Draft, Controller.Validated); _rail.Text = AdminPolicyPresentation.LifecycleRail(reached, reached >= 4 ? Controller.SavedVersion?.VersionNumber : null); }
            if (_commands.TryGetValue("PolicyEnabled", out var label)) { var text = AdminPolicyPresentation.ToggleLabel(current); label.Content = text; AutomationProperties.SetName(label, text); }
        }
        if (_commands.TryGetValue("PolicyEnabled", out var enable)) enable.IsEnabled = editable && !Controller.ReviewRequired;
        if (_commands.TryGetValue("PolicyDelete", out var delete)) delete.IsEnabled = editable && !Controller.ReviewRequired && Controller.Snapshot?.Value is { ActiveVersionId: null };
        _validation.Text = Controller.Validation == null ? "" : (Controller.Validated ? T("editor.validationPassed") : T("editor.validationStale")) + "\n" + string.Join("\n", Controller.Validation.Errors.Select(e => $"{e.Row}:{e.Col} " + LocalLog.RedactDiagnosticText(e.Message)));
        _simulation.Text = Controller.Simulation is { } simulation ? Pretty(simulation.Decision) + $"\nGeneration {simulation.Generation} · {simulation.EvalTimeNs / 1000.0:0.0} µs" : "";
        _review.Children.Clear();
        if (Controller.ReviewRequired) _review.Children.Add(U.Text(T("review.keepNote"), 12, true));
        if (Controller.Review is { } review)
        {
            _review.Children.Add(U.Text(T("review.summary", new { name = review.Value.Name, state = review.Value.Enabled ? "enabled" : "disabled", has_version = review.Value.ActiveVersionId != null ? "yes" : "no", version = review.Value.ActiveVersion?.VersionNumber }), 13));
            _review.Children.Add(Command(T("review.adopt"), "PolicyAdopt", () => { Controller.AdoptReview(); return Task.CompletedTask; }, editable));
        }
        if (Controller.SavedVersion is { CompiledOk: true } saved && saved.Source == Controller.Draft)
            _review.Children.Add(Command(T("editor.activate", new { version = saved.VersionNumber }), "PolicyActivate", () => ActivateAsync(saved), editable && !Controller.ReviewRequired));
        if (!ReferenceEquals(_shownVersions, Controller.Versions)) RenderVersions();
        foreach (var button in _versions.Children.OfType<Button>()) button.IsEnabled = editable;
    }
    private void RenderVersions()
    {
        _shownVersions = Controller.Versions; _versions.Children.Clear();
        if (Controller.Versions.Count == 0) _versions.Children.Add(U.Text(T("history.empty"), 13, true));
        foreach (var version in Controller.Versions)
        {
            var button = U.Action($"v{version.VersionNumber} · " + T(version.CompiledOk ? "history.compiled" : "history.failed") + " · " + version.CreatedAt + " · " + (version.Comment ?? ""), "PolicyVersion_" + version.Id);
            button.Click += async (_, _) =>
            {
                await Controller.ViewVersionAsync(version.Id); var selected = Controller.SelectedVersion; if (selected?.Id != version.Id) return;
                var code = AdminAccessPageUi.Multiline(T("history.selectedSource"), "PolicyVersionSource", selected.Source ?? "", 300); code.IsReadOnly = true;
                var dialog = new ContentDialog { Title = $"v{selected.VersionNumber}", Content = AdminAccessPageUi.Stack(U.Text(selected.SourceSha256, 12, true), code, U.Text(LocalLog.RedactDiagnosticText(selected.CompileError ?? ""), 12)), CloseButtonText = "Close", PrimaryButtonText = selected.CompiledOk && !Controller.ReviewRequired ? T("history.activate") : "", DefaultButton = ContentDialogButton.Close };
                if (await _ui.ShowAsync(dialog) == ContentDialogResult.Primary) await ActivateAsync(selected);
            };
            _versions.Children.Add(button);
        }
        if (Controller.VersionCursor != null) _versions.Children.Add(Command(T("history.loadOlder"), "PolicyMoreVersions", Controller.MoreVersionsAsync, Controller.Versions.Count < 1000));
    }
    private async Task ActivateAsync(AdminPolicyVersion version)
    {
        var reviewed = Controller.Snapshot; var owner = Controller.Owner; if (reviewed == null) return;
        if (await _ui.ConfirmAsync(T("editor.confirmActivation"), T("editor.activateBody", new { state = reviewed.Value.Enabled ? "enabled" : "disabled", has_version = reviewed.Value.ActiveVersionId != null ? "yes" : "no", version = reviewed.Value.ActiveVersion?.VersionNumber }), T("editor.confirmActivation")) && ReferenceEquals(owner, Controller.Owner)) await Controller.ActivateAsync(reviewed, version, true);
    }
    private void RenderVendor()
    {
        _view.Children.Clear(); _view.Children.Add(U.Text(T("baseline.intro"), 13, true));
        foreach (var domain in new[] { "scope", "permission", "action" })
        {
            var summary = new StackPanel { Spacing = 8 }; summary.Children.Add(U.Text(Domain(domain), 18));
            for (var i = 1; i <= (domain == "scope" ? 5 : domain == "permission" ? 4 : 3); i++) summary.Children.Add(U.Text("• " + T($"domain.{domain}.baseline{i}"), 13, true));
            _view.Children.Add(summary);
        }
        if (Controller.Vendor.Count == 0) _view.Children.Add(U.Text(T("baseline.empty")));
        foreach (var module in Controller.Vendor)
        {
            if (module.Path.EndsWith("ratings.rego", StringComparison.Ordinal) || module.Path.EndsWith("quality.rego", StringComparison.Ordinal))
            {
                var rating = module.Path.EndsWith("ratings.rego", StringComparison.Ordinal);
                _view.Children.Add(U.Text(T(rating ? "baseline.ratingTiers" : "baseline.qualityTiers"), 16));
                _view.Children.Add(U.Text(RankLadder(module.Source), 13));
                _view.Children.Add(U.Text(T(rating ? "baseline.ratingCaption" : "baseline.qualityCaption"), 12, true));
            }
            var code = AdminAccessPageUi.Multiline(T("baseline.viewSource"), "PolicyVendor_" + module.Path, module.Source, 300); code.IsReadOnly = true;
            _view.Children.Add(U.Text(module.Path)); _view.Children.Add(new Expander { Header = T("baseline.viewSource"), Content = code, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
    }
    private bool _filtersBuilt;
    private readonly StackPanel _decisionRows = new() { Spacing = 8 };
    private ComboBox? _decisionName, _allowed;
    private TextBox? _user, _from, _to;
    private void RenderDecisions()
    {
        if (!_view.Children.Contains(_decisionRows))
        {
            _view.Children.Clear(); _filtersBuilt = false;
        }
        if (!_filtersBuilt)
        {
            _filtersBuilt = true;
            _decisionName = U.Select(T("decisions.filter.decision"), "PolicyDecisionName", new[] { ("", T("decisions.filter.allDecisions")) }.Concat(Controller.Capability!.DecisionTypes.Select(d => ("silo." + d + ".decision", Domain(d))))); _decisionName.SelectedIndex = 0;
            _allowed = U.Select(T("decisions.filter.allowed"), "PolicyAllowed", [("", T("decisions.filter.all")), ("true", T("outcome.allowed")), ("false", T("outcome.denied"))]); _allowed.SelectedIndex = 0;
            _user = U.Input(T("decisions.filter.userId"), "PolicyDecisionUser"); _from = U.Input(T("decisions.filter.from"), "PolicyDecisionFrom", "2026-11-17T00:00:00Z"); _to = U.Input(T("decisions.filter.to"), "PolicyDecisionTo", "2026-11-17T23:59:59Z");
            AdminAccessPageUi.Select(_decisionName, Controller.Filters.DecisionName ?? ""); AdminAccessPageUi.Select(_allowed, Controller.Filters.Allowed ?? "");
            _user.Text = Controller.Filters.UserId ?? ""; _from.Text = Controller.Filters.From ?? ""; _to.Text = Controller.Filters.To ?? "";
            foreach (var control in new Control[] { _decisionName, _allowed, _user, _from, _to }) _view.Children.Add(control);
            _view.Children.Add(Command(T("decisions.filter.apply"), "PolicyApplyFilters", async () =>
            {
                string? Date(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : DateTimeOffset.TryParse(box.Text, out var date) ? date.ToUniversalTime().ToString("O") : throw new ArgumentException();
                try { await Controller.ApplyFiltersAsync(new(AdminAccessPageUi.Selected(_decisionName).NullIfEmpty(), _user.Text.Trim().NullIfEmpty(), AdminAccessPageUi.Selected(_allowed).NullIfEmpty(), Date(_from), Date(_to))); }
                catch (ArgumentException) { _ui.Feedback.Text = "Enter valid From and To dates."; }
            }));
            _view.Children.Add(Command(T("decisions.filter.reset"), "PolicyResetFilters", async () => { _decisionName.SelectedIndex = _allowed.SelectedIndex = 0; _user.Text = _from.Text = _to.Text = ""; await Controller.ApplyFiltersAsync(new()); }));
            _view.Children.Add(_decisionRows);
        }
        _decisionRows.Children.Clear();
        if (Controller.Decisions.Count == 0) _decisionRows.Children.Add(U.Text(T("decisions.empty"), 13, true));
        var table = new StackPanel { MinWidth = 1040 };
        table.Children.Add(DecisionRow(new[] { "time", "decision", "user", "allowed", "eval", "digest" }.Select(k => T("decisions.column." + k)).ToArray(), null));
        foreach (var decision in Controller.Decisions)
        {
            var button = U.Action("Details", "PolicyDecision_" + decision.Id);
            button.IsEnabled = !Controller.Busy; button.Click += async (_, _) => await Controller.SelectDecisionAsync(decision.Id);
            table.Children.Add(DecisionRow([decision.Timestamp, decision.DecisionName, decision.UserId ?? "—", decision.Allowed == null ? "—" : T(decision.Allowed == true ? "outcome.allowed" : "outcome.denied"), $"{decision.EvalTimeNs / 1000.0:0.0} µs", decision.InputDigest], button));
            if (Controller.Decision is { } detail && detail.Id == decision.Id)
            {
                table.Children.Add(U.Text(T("decisions.detail.input") + "\n" + Pretty(detail.InputSample), 12));
                table.Children.Add(U.Text(T("decisions.detail.result") + "\n" + Pretty(detail.ResultSample), 12));
                if (detail.Error?.Length > 0) table.Children.Add(U.Text(LocalLog.RedactDiagnosticText(detail.Error), 12));
            }
        }
        _decisionRows.Children.Add(new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _decisionRows.Children.Add(Command(T("decisions.previous"), "PolicyPrevious", () => Controller.PageDecisionsAsync(false), !Controller.Busy && Controller.HasPrevious));
        _decisionRows.Children.Add(Command(T("decisions.next"), "PolicyNext", () => Controller.PageDecisionsAsync(true), !Controller.Busy && Controller.NextDecisionCursor != null));
    }
    private Button Command(string label, string id, Func<Task> action, bool enabled = true)
    {
        var button = U.Action(label, id); button.IsEnabled = enabled; _commands[id] = button; button.Click += async (_, _) => await action(); return button;
    }
    private static Grid DecisionRow(string[] values, Button? action)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new(12), BorderThickness = new(0, 0, 0, 1), BorderBrush = U.Brush("BorderBrush") };
        foreach (var width in new[] { 1.4, 1.5, .6, .7, .7, 1.5, .7 }) row.ColumnDefinitions.Add(new() { Width = new(width, GridUnitType.Star) });
        for (var index = 0; index < values.Length; index++) { var text = U.Text(values[index], 12); Grid.SetColumn(text, index); row.Children.Add(text); }
        if (action != null) { Grid.SetColumn(action, 6); row.Children.Add(action); } return row;
    }
    private static string RankLadder(string source)
    {
        try
        {
            var table = Regex.Match(source, @"rank\s*:?=\s*\{([\s\S]*?)\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var entries = Regex.Matches(table.Groups[1].Value, "\"([^\"]*)\"\\s*:\\s*(\\d+)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Cast<Match>()
                .Select(m => (Label: m.Groups[1].Value, Rank: int.TryParse(m.Groups[2].Value, out var rank) ? rank : -1)).Where(e => e.Rank >= 0).ToArray();
            return entries.Length == 0 ? T("baseline.tiersUnparsed") : string.Join(" → ", entries.GroupBy(e => e.Rank).OrderBy(g => g.Key).Select(g => string.Join(" / ", g.Select(e => e.Label.Length == 0 ? T("baseline.anyTier") : e.Label))));
        }
        catch (RegexMatchTimeoutException) { return T("baseline.tiersUnparsed"); }
    }
    private static string Pretty(JsonElement value) => AdminAccessRedaction.Json(value);
    private static string Domain(string domain) => T("domain." + domain + ".title");
    private static string Status(AdminPolicyDocument document) => AdminPolicyPresentation.Status(document);
    private static string T(string suffix, object? values = null) => AdminText.T("web.admin.access.policy." + suffix, values);
}

internal static class AdminPolicyTextExtensions
{
    internal static string? NullIfEmpty(this string text) => text.Length == 0 ? null : text;
}
