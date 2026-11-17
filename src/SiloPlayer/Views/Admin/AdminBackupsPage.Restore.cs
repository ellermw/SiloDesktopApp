using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Model = SiloPlayer.Core.Services.Admin.AdminBackupRestoreModel;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;
using UiKit = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminBackupsPage
{
    private readonly HashSet<string> _restoreOpen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _filters = new(StringComparer.Ordinal);
    private bool _confirmingStop;

    private void ActivateRestore()
    {
        var controller = App.Services.GetRequiredService<AdminBackupRestoreController>();
        RestoreController = controller;
        _restoreOpen.Clear(); _filters.Clear(); _expandedRows.Clear();
        controller.Changed += QueueRestoreRender; controller.Notified += OnNotified;
        controller.JobChanged += OnJobChanged; controller.Finished += OnSaved;
        if (_root.Children.Contains(_saveBar)) _root.Children.Remove(_saveBar);
        RenderRestore(controller.State);
    }

    private void QueueRestoreRender()
    {
        var controller = RestoreController; if (controller == null) return;
        DispatcherQueue.TryEnqueue(() => { if (_active && ReferenceEquals(controller, RestoreController)) RenderRestore(controller.State); });
    }

    /// <summary>The address follows the step: <c>?job=</c> while a job shows, cleared for a fresh chooser (WebUI setParams).</summary>
    private void OnJobChanged(string? jobId) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active || _location == null) return;
        var next = _location.WithQuery("job", jobId);
        if (AdminShellPage.TryReplace(this, next)) _location = next;
    });

    private void RenderRestoreInvalid()
    {
        _body.Children.Clear();
        _body.Children.Add(BackLink());
        _body.Children.Add(UiKit.Title(T("web.admin.backups.restore.heading.default")));
        _body.Children.Add(Alert(T("web.admin.backups.restore.openFailed", new { reason = T("web.admin.backups.error.notFound") })));
    }

    private HyperlinkButton BackLink()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(AdminMonitorUi.Icon("arrow-left", 16, UiKit.Brush("SecondaryTextBrush")));
        content.Children.Add(Text(T("web.admin.backups.restore.backLink"), 14, true));
        var link = new HyperlinkButton { Content = content, Padding = new(0) };
        AutomationProperties.SetName(link, T("web.admin.backups.restore.backLink"));
        AutomationProperties.SetAutomationId(link, "RestoreBackLink");
        link.Click += (_, _) => AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Backups));
        return link;
    }

    private (string Title, string? Subtitle) RestoreHeading(AdminBackupRestoreState state)
    {
        var fromUpload = state.Source?.Upload == true;
        var backup = fromUpload ? null : state.List?.Items?.FirstOrDefault(item => item.Id == state.Source?.Id);
        var set = state.Preview;
        var source = state.Upload?.Source;
        var sameServer = set?.SameServer ?? (source != null && source.ServerId == state.List?.ServerId);
        var serverName = set?.ServerName ?? source?.ServerName;
        var serverId = set?.ServerId ?? source?.ServerId;
        var known = set != null || source != null;
        var taken = backup?.CreatedAt ?? set?.CreatedAt ?? source?.CreatedAt;
        if (taken is not { } when) return (T("web.admin.backups.restore.heading.default"), null);
        var version = Present.KnownSiloVersion(set?.SiloVersion ?? backup?.SiloVersion ?? source?.SiloVersion);
        var name = known ? Present.SourceServerName(serverName) : null;
        var idDetail = known && !sameServer && name == null && serverId != null ? T("web.admin.backups.restore.heading.serverId", new { id = Present.ShortServerId(serverId) }) : null;
        string Sentence(params string?[] parts) => T("web.admin.backups.restore.heading.subtitle", new { parts = Model.ListPhrase(parts.Where(part => !string.IsNullOrEmpty(part)).Select(part => part!).ToArray()) });
        var time = AdminBackupText.TimeText(when);
        var withVersion = version != null ? T("web.admin.backups.restore.heading.withVersion", new { version }) : null;
        if (fromUpload)
        {
            var origin = sameServer ? T("web.admin.backups.restore.heading.uploadedFromThis") : T("web.admin.backups.restore.heading.uploadedFromAnother");
            var which = name != null ? "other" : sameServer ? "this" : "another";
            return (T("web.admin.backups.restore.heading.upload", new { source = which, date = AdminBackupText.TakenDate(when), name = name ?? "" }),
                Sentence(origin, T("web.admin.backups.restore.heading.takenAt", new { time }), withVersion, idDetail));
        }
        string? where = null;
        if (known) where = sameServer ? T("web.admin.backups.restore.heading.onThisServer")
            : name != null ? T("web.admin.backups.restore.heading.fromServer", new { name }) : T("web.admin.backups.restore.heading.fromAnother");
        return (T("web.admin.backups.restore.heading.backup", new { date = AdminBackupText.TakenDate(when) }),
            Sentence(T("web.admin.backups.restore.heading.takenAtFirst", new { time }), withVersion, where, idDetail));
    }

    private FrameworkElement Steps(int current)
    {
        var steps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        AutomationProperties.SetName(steps, T("web.admin.backups.restore.steps.label"));
        var labels = new[] { "web.admin.backups.restore.steps.choose", "web.admin.backups.restore.steps.review", "web.admin.backups.restore.steps.restore" };
        for (var index = 0; index < labels.Length; index++)
        {
            var step = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var done = index < current;
            var number = new Border
            {
                Width = 24, Height = 24, CornerRadius = new(12), BorderThickness = new(1),
                BorderBrush = UiKit.Brush(index == current || done ? "PrimaryTextBrush" : "BorderBrush"),
                Background = done ? UiKit.Brush("PrimaryTextBrush") : null,
                Child = new TextBlock { Text = done ? "✓" : (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = done ? UiKit.Brush("AppBackgroundBrush") : UiKit.Brush("PrimaryTextBrush") },
            };
            AutomationProperties.SetAccessibilityView(number, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            step.Children.Add(number);
            var label = Text(T(labels[index]), 14, index != current);
            if (index == current) label.FontWeight = FontWeights.Medium;
            AutomationProperties.SetName(step, T(labels[index]) + (index == current ? " (" + T("configure.native.backups.restore.currentStep") + ")" : ""));
            AutomationProperties.SetAutomationId(step, "RestoreStep-" + index);
            step.Children.Add(label);
            steps.Children.Add(step);
        }
        return steps;
    }

    private void RenderRestore(AdminBackupRestoreState state)
    {
        var focused = FocusedId();
        foreach (var box in _filters.Values) if (box.Parent is Panel parent) parent.Children.Remove(box);
        _body.Children.Clear();
        if (state.Owner?.IsCurrent != true) return;
        _body.Children.Add(BackLink());
        var (title, subtitle) = RestoreHeading(state);
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(UiKit.Title(title));
        if (subtitle != null) header.Children.Add(Text(subtitle, 14, true));
        _body.Children.Add(header);
        _body.Children.Add(Steps(state.Step));
        _body.Children.Add(RestoreBody(state));
        RestoreFocus(focused);
    }

    private FrameworkElement RestoreBody(AdminBackupRestoreState state)
    {
        var controller = RestoreController!;
        if (state.Capabilities is { SelectiveRestore: false }) return Alert(T("web.admin.backups.restore.unavailable"));
        if (state.JobId != null)
        {
            if (state.Job == null && state.JobError == null) return UiKit.Skeleton(160);
            if (state.Job == null)
            {
                var retry = Action(T("common.tryAgain"), "RestoreJobRetry");
                retry.Click += async (_, _) => await controller.RetryJobAsync();
                return Alert(T("web.admin.backups.restore.jobLoadFailed", new { reason = Present.ErrorMessage(state.JobError) }), retry);
            }
            return RunView(state, state.Job);
        }
        if (state.PreviewLoading)
        {
            var loading = new StackPanel { Spacing = 16 };
            AutomationProperties.SetName(loading, T("web.admin.backups.restore.reading"));
            loading.Children.Add(UiKit.Skeleton(128)); loading.Children.Add(UiKit.Skeleton(128));
            return loading;
        }
        if (state.Unauthenticated)
        {
            var stack = new StackPanel { Spacing = 12 };
            var heading = Text(T("web.admin.backups.restore.unverifiedTitle")); heading.FontWeight = FontWeights.Medium;
            stack.Children.Add(heading);
            stack.Children.Add(Text(T("web.admin.backups.restore.unverifiedBody"), 14, true));
            var open = Action(T("web.admin.backups.restore.openAnyway"), "RestoreOpenAnyway");
            open.Click += async (_, _) => await controller.OpenUnverifiedAsync();
            stack.Children.Add(open);
            var panel = Surface(stack);
            AutomationProperties.SetLiveSetting(panel, AutomationLiveSetting.Assertive);
            return panel;
        }
        if (state.Preview is not { } set)
        {
            var retry = Action(T("common.tryAgain"), "RestorePreviewRetry");
            retry.Click += async (_, _) => await controller.ReloadPreviewAsync();
            return Alert(T(state.Source?.Upload == true ? "web.admin.backups.restore.openFailedUpload" : "web.admin.backups.restore.openFailed",
                new { reason = Present.ErrorMessage(state.PreviewError) }), retry);
        }
        var selection = controller.Selection;
        return state.Reviewing ? ReviewView(state, set, selection) : ChooseView(state, set, selection);
    }

    // ── Step 1: choose ───────────────────────────────────────────────

    private FrameworkElement ChooseView(AdminBackupRestoreState state, AdminBackupRestoreSet set, IReadOnlySet<string> selection)
    {
        var controller = RestoreController!;
        var names = Model.LibraryNames(set, state.Libraries);
        var layout = new Grid { ColumnSpacing = 32, RowSpacing = 24 };
        var wide = ActualWidth >= 1024;
        layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new() { Width = wide ? new(320) : new(0) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var main = new StackPanel { Spacing = 20 };
        if (state.OutcomeUnknown)
        {
            var reload = Action(T("web.admin.backups.restore.review.reloadPreview"), "RestoreUnknownReload");
            reload.Click += async (_, _) => await controller.ReloadPreviewAsync();
            var check = Action(T("configure.native.backups.restore.checkStatus"), "RestoreUnknownCheck");
            check.Click += async (_, _) => await controller.ReconcileUnknownAsync();
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(Text(T("configure.native.backups.restore.unknownOutcome")));
            stack.Children.Add(Footer(check, reload));
            main.Children.Add(Surface(stack));
        }
        if (state.Jobs.FirstOrDefault() is { } lastJob)
        {
            var fromUpload = state.Source?.Upload == true;
            var key = lastJob.FinishedAt != null ? fromUpload ? "web.admin.backups.restore.lastRun.uploadAt" : "web.admin.backups.restore.lastRun.backupAt"
                : fromUpload ? "web.admin.backups.restore.lastRun.uploadEarlier" : "web.admin.backups.restore.lastRun.backupEarlier";
            var line = Rich(AdminBackupText.Runs(key, new { date = lastJob.FinishedAt is { } finished ? AdminBackupText.DateTimeText(finished) : "" }), 14, true,
                () => { if (_location != null && AdminShellPage.TryReplace(this, _location.WithQuery("job", lastJob.Id))) { _location = _location.WithQuery("job", lastJob.Id); _ = controller.OpenAsync(state.Source!, lastJob.Id); } });
            AutomationProperties.SetAutomationId(line, "RestoreLastRun");
            main.Children.Add(line);
        }
        if (set.KeyMatches != true) main.Children.Add(Status(T("web.admin.backups.restore.choose.otherKey"), "warn"));
        if (set.Omitted is { Length: > 0 } omitted) main.Children.Add(Status(T("web.admin.backups.restore.choose.omitted", AdminBackupText.Counted(omitted.Length)), "warn"));
        if (set.MissingBlobs is { Length: > 0 } missing) main.Children.Add(Status(T("web.admin.backups.restore.choose.missingFiles", AdminBackupText.Counted(missing.Length)), "warn"));
        var chosenAccounts = Model.ChosenItems(set, selection).Where(entry => entry.Section == "users").ToArray();
        foreach (var section in set.Sections!)
        {
            FrameworkElement? top = null, bottom = null;
            if (section.Id == "users" && chosenAccounts.Length > 0) top = DataTypesPanel(state, chosenAccounts, selection);
            if (section.Id == "users" && set.Omitted is { Length: > 0 } accounts) bottom = OmittedAccounts(accounts, restore: true, "RestoreOmitted");
            else if (section.Id == "settings") bottom = NeverRestored(set.NeverRestoredSettings!);
            main.Children.Add(SectionBlock(section, selection, names, top, bottom));
        }
        layout.Children.Add(main);
        var aside = Summary(state, set, selection);
        if (wide) { Grid.SetColumn(aside, 1); } else { Grid.SetRow(aside, 1); }
        layout.Children.Add(aside);
        return layout;
    }

    private FrameworkElement Summary(AdminBackupRestoreState state, AdminBackupRestoreSet set, IReadOnlySet<string> selection)
    {
        var controller = RestoreController!;
        var aside = new StackPanel { Spacing = 12, Padding = new(20), CornerRadius = new(16), BorderThickness = new(1), BorderBrush = UiKit.Brush("BorderBrush"),
            Background = UiKit.Brush("SurfaceBrush"), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAutomationId(aside, "RestoreSummary");
        var index = new StackPanel { Spacing = 2 };
        AutomationProperties.SetName(index, T("web.admin.backups.restore.choose.sectionsLabel"));
        foreach (var section in set.Sections!)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Text(Model.SectionTitle(section.Id!), 13));
            var progress = Text(Model.SectionProgress(section, selection), 12, true); Grid.SetColumn(progress, 1); row.Children.Add(progress);
            var jump = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = null, BorderThickness = new(0), Padding = new(6, 4, 6, 4) };
            AutomationProperties.SetName(jump, Model.SectionTitle(section.Id!) + ", " + Model.SectionProgress(section, selection));
            AutomationProperties.SetAutomationId(jump, "RestoreJump-" + section.Id);
            jump.Click += (_, _) => { if (Find(_body, "RestoreSection-" + section.Id) is FrameworkElement target) target.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 }); };
            index.Children.Add(jump);
        }
        aside.Children.Add(index);
        aside.Children.Add(Heading(T("web.admin.backups.restore.choose.summaryHeading")));
        var plan = Model.Plan(set, selection, state.DataTypes);
        if (plan.Count == 0) aside.Children.Add(Text(T("web.admin.backups.restore.choose.nothingChosen"), 13, true));
        else
        {
            var list = new StackPanel { Spacing = 4 };
            AutomationProperties.SetAutomationId(list, "RestorePlan");
            foreach (var sentence in plan.SelectMany(group => group.Sentences)) list.Children.Add(Text("• " + T("web.admin.backups.restore.plan.sentence", new { sentence }), 13));
            aside.Children.Add(list);
        }
        if (state.Notice != null)
        {
            var notice = Text(state.Notice, 13, true);
            AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite); AutomationProperties.SetAutomationId(notice, "RestoreNotice");
            aside.Children.Add(notice);
        }
        aside.Children.Add(Text(T("web.admin.backups.restore.choose.recoveryPoint"), 12, true));
        var review = Action(T("web.admin.backups.restore.choose.review"), "RestoreReview", primary: true);
        review.IsEnabled = selection.Count > 0 && !state.OutcomeUnknown;
        review.Click += (_, _) => controller.Review();
        aside.Children.Add(review);
        return aside;
    }

    private FrameworkElement DifferenceBar(IReadOnlyDictionary<AdminRestoreItemState, int> counts)
    {
        var total = counts.Values.Sum();
        var stack = new StackPanel { Spacing = 4 };
        if (total == 0) return stack;
        var bar = new Grid { Height = 6, CornerRadius = new(3), MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, Width = 240 };
        AutomationProperties.SetName(bar, Model.StateCountsLabel(counts));
        var column = 0;
        foreach (var state in Model.StateOrder)
        {
            if (counts[state] == 0) continue;
            bar.ColumnDefinitions.Add(new() { Width = new(counts[state], GridUnitType.Star) });
            var color = state switch
            {
                AdminRestoreItemState.Differs => Windows.UI.Color.FromArgb(255, 167, 139, 250),
                AdminRestoreItemState.New => Windows.UI.Color.FromArgb(255, 56, 189, 248),
                AdminRestoreItemState.Blocked => AdminMonitorUi.Amber500,
                _ => Windows.UI.Color.FromArgb(77, 160, 160, 170),
            };
            var segment = new Border { Background = AdminMonitorUi.Solid(color) };
            Grid.SetColumn(segment, column++); bar.Children.Add(segment);
        }
        stack.Children.Add(bar);
        var text = Text(Model.StateCountsText(counts), 12, true);
        AutomationProperties.SetAccessibilityView(text, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        stack.Children.Add(text);
        return stack;
    }

    private FrameworkElement SectionBlock(AdminBackupRestoreSection section, IReadOnlySet<string> selection, AdminRestoreLibraryNames names, FrameworkElement? top, FrameworkElement? bottom)
    {
        var controller = RestoreController!;
        var items = section.Items!;
        var counts = Model.CountStates(items);
        var query = _filters.TryGetValue(section.Id!, out var existing) ? existing.Text : "";
        var visible = query.Trim().Length > 0 ? items.Where(item => Model.MatchesFilter(item, query, names)).ToArray() : items;
        var groups = Model.GroupSection(visible);
        var restorable = counts[AdminRestoreItemState.Differs] + counts[AdminRestoreItemState.New];
        var showAllKey = "section-all:" + section.Id;
        var expanded = restorable > 0 || _restoreOpen.Contains(showAllKey);
        var block = new StackPanel { Spacing = 12, Padding = new(20), CornerRadius = new(16), BorderThickness = new(1), BorderBrush = UiKit.Brush("BorderBrush"), Background = UiKit.Brush("SurfaceBrush") };
        AutomationProperties.SetAutomationId(block, "RestoreSection-" + section.Id);
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 6 };
        titles.Children.Add(Heading(Model.SectionTitle(section.Id!)));
        titles.Children.Add(section.Count == 0 ? Text(T("web.admin.backups.restore.choose.emptySection"), 13, true) : DifferenceBar(counts));
        head.Children.Add(titles);
        if (groups.Restorable.Count > 0)
        {
            var keys = groups.Restorable.SelectMany(item => Model.ToggleKeys(section.Id!, item)).ToArray();
            var chosenShown = groups.Restorable.Count(item => selection.Contains(Model.ItemKey(section.Id!, item.Id!)));
            var all = new CheckBox { Content = T(query.Trim().Length > 0 ? "web.admin.backups.restore.choose.allShown" : "web.admin.backups.restore.choose.all"), IsThreeState = false,
                IsChecked = chosenShown == 0 ? false : chosenShown == groups.Restorable.Count ? true : null };
            AutomationProperties.SetAutomationId(all, "RestoreAll-" + section.Id);
            AutomationProperties.SetName(all, T("web.admin.backups.restore.choose.all") + " " + Model.SectionTitle(section.Id!));
            all.Checked += (_, _) => controller.Toggle(keys, chosenShown != groups.Restorable.Count);
            all.Unchecked += (_, _) => controller.Toggle(keys, chosenShown != groups.Restorable.Count);
            Grid.SetColumn(all, 1); head.Children.Add(all);
        }
        block.Children.Add(head);
        if (items.Length > 0 && restorable == 0)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(Text(Model.NothingToRestore(counts), 13, true));
            line.Children.Add(Disclosure(T(_restoreOpen.Contains(showAllKey) ? "web.admin.backups.restore.choose.hideThem" : "web.admin.backups.restore.choose.showThem"),
                "RestoreShowThem-" + section.Id, _restoreOpen.Contains(showAllKey), () => ToggleOpen(showAllKey)));
            block.Children.Add(line);
        }
        if (expanded)
        {
            if (top != null) block.Children.Add(top);
            if (items.Length > Model.FilterThreshold)
            {
                if (!_filters.TryGetValue(section.Id!, out var filter))
                {
                    filter = new TextBox { PlaceholderText = T(section.Id == "users" ? "web.admin.backups.restore.choose.filterUsers" : "web.admin.backups.restore.choose.filterItems"), MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
                    AutomationProperties.SetName(filter, T("web.admin.backups.restore.choose.filterLabel", new { section = section.Id }));
                    AutomationProperties.SetAutomationId(filter, "RestoreFilter-" + section.Id);
                    filter.TextChanged += (_, _) => QueueRestoreRender();
                    _filters[section.Id!] = filter;
                }
                block.Children.Add(filter);
            }
            if (visible.Length == 0 && query.Trim().Length > 0) block.Children.Add(Text(T("web.admin.backups.restore.choose.noMatches"), 13, true));
            if (Model.GroupsByLibrary(section.Id!))
                foreach (var group in Model.GroupByLibrary(groups.Restorable, names))
                {
                    var key = "library:" + section.Id + ":" + group.Key;
                    var open = !_restoreOpen.Contains(key);
                    block.Children.Add(Disclosure(group.Name + " · " + Model.StateCountsText(Model.CountStates(group.Items)), "RestoreLibrary-" + section.Id + "-" + group.Key, open, () => ToggleOpen(key)));
                    if (open) block.Children.Add(Rows(section.Id!, group.Items, selection, names, false));
                }
            else if (groups.Restorable.Count > 0) block.Children.Add(Rows(section.Id!, groups.Restorable, selection, names, false));
            foreach (var group in groups.Blocked)
            {
                var key = "blocked:" + section.Id + ":" + group.Key;
                var open = _restoreOpen.Contains(key);
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                line.Children.Add(Status(group.Title, "warn"));
                line.Children.Add(Disclosure(T(open ? "web.admin.backups.restore.choose.hideThem" : "web.admin.backups.restore.choose.showThem"), "RestoreBlocked-" + section.Id + "-" + group.Key, open, () => ToggleOpen(key)));
                block.Children.Add(line);
                if (open) block.Children.Add(Rows(section.Id!, group.Items, selection, names, true));
            }
            if (groups.Same.Count > 0)
            {
                var key = "same:" + section.Id;
                var open = _restoreOpen.Contains(key);
                block.Children.Add(Disclosure(T("web.admin.backups.restore.choose.sameLabel", AdminBackupText.Counted(groups.Same.Count)), "RestoreSame-" + section.Id, open, () => ToggleOpen(key)));
                if (open)
                {
                    var same = new StackPanel { Spacing = 2, Margin = new(20, 0, 0, 0) };
                    foreach (var item in groups.Same)
                    {
                        var text = Model.DisplayLabel(item.Kind, item.Label);
                        if (Model.RowSecondary(item, names, true) is { } secondary) text += " · " + secondary;
                        same.Children.Add(Text(text, 13, true));
                    }
                    block.Children.Add(same);
                }
            }
        }
        if (bottom != null) block.Children.Add(bottom);
        return block;
    }

    private void ToggleOpen(string key) { if (!_restoreOpen.Remove(key)) _restoreOpen.Add(key); QueueRestoreRender(); }

    private FrameworkElement Rows(string sectionId, IReadOnlyList<AdminBackupRestoreItem> items, IReadOnlySet<string> selection, AdminRestoreLibraryNames names, bool withLibrary)
    {
        var list = new StackPanel { Spacing = 0 };
        foreach (var item in items)
            list.Children.Add(item.Account != null ? AccountRow(item, selection) : ItemRow(sectionId, item, selection, Model.RowSecondary(item, names, withLibrary)));
        return list;
    }

    private Border StateTag(AdminRestoreItemState state)
    {
        var (key, color) = state switch
        {
            AdminRestoreItemState.Differs => ("web.admin.backups.restore.tag.differs", Windows.UI.Color.FromArgb(255, 167, 139, 250)),
            AdminRestoreItemState.New => ("web.admin.backups.restore.tag.new", Windows.UI.Color.FromArgb(255, 56, 189, 248)),
            AdminRestoreItemState.Blocked => ("web.admin.backups.restore.tag.blocked", AdminMonitorUi.Amber400),
            _ => ("web.admin.backups.restore.tag.same", Windows.UI.Color.FromArgb(255, 160, 160, 170)),
        };
        var label = Text(T(key), 11); label.Foreground = AdminMonitorUi.Solid(color); label.FontWeight = FontWeights.Medium;
        return new Border { Child = label, Padding = new(6, 1, 6, 1), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = AdminMonitorUi.Solid(color, .3), VerticalAlignment = VerticalAlignment.Center };
    }

    private FrameworkElement ItemRow(string sectionId, AdminBackupRestoreItem item, IReadOnlySet<string> selection, string? secondary)
    {
        var controller = RestoreController!;
        var label = Model.DisplayLabel(item.Kind, item.Label);
        var detailsKey = "item:" + sectionId + ":" + item.Id;
        var open = _restoreOpen.Contains(detailsKey);
        var row = new StackPanel { Spacing = 6, Padding = new(0, 8, 0, 8) };
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var check = Check(label, "RestoreItem-" + sectionId + "-" + item.Id, selection.Contains(Model.ItemKey(sectionId, item.Id!)), item.Selectable == true,
            chosen => controller.Toggle(Model.ToggleKeys(sectionId, item), chosen));
        if (secondary != null)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
            var muted = Text(secondary, 12, true); muted.TextTrimming = TextTrimming.CharacterEllipsis; muted.TextWrapping = TextWrapping.NoWrap;
            ToolTipService.SetToolTip(muted, secondary);
            content.Children.Add(muted);
            check.Content = content;
            AutomationProperties.SetHelpText(check, secondary);
        }
        line.Children.Add(check);
        if (item.Kind == "template_collection") { var pack = Text(T("web.admin.backups.restore.rows.starterPack"), 11, true); Grid.SetColumn(pack, 1); line.Children.Add(pack); }
        var tag = StateTag(Model.State(item)); Grid.SetColumn(tag, 2); line.Children.Add(tag);
        if (HasDetails(item))
        {
            var toggle = Disclosure("", "RestoreDetails-" + sectionId + "-" + item.Id, open, () => ToggleOpen(detailsKey));
            AutomationProperties.SetName(toggle, T(open ? "web.admin.backups.restore.rows.hideDetails" : "web.admin.backups.restore.rows.showDetails", new { label }));
            Grid.SetColumn(toggle, 3); line.Children.Add(toggle);
        }
        row.Children.Add(line);
        if (open) row.Children.Add(ItemDetails(item));
        return new Border { Child = row, BorderThickness = new(0, 0, 0, 1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6) };
    }

    private static bool HasDetails(AdminBackupRestoreItem item) => item.Message is { Length: > 0 } || item.Reason is { Length: > 0 } || item.MissingBlobs!.Length > 0 ||
        item.RestartRequired == true || (item.HomeSection?.Changes ?? item.Collection?.Changes ?? []).Length > 0 ||
        (item.Settings ?? []).Any(setting => setting.Status != "on_this_server") || (item.Library?.Paths?.Length ?? 0) > 1 || (item.Library?.MissingPaths?.Length ?? 0) > 0;

    private FrameworkElement ItemDetails(AdminBackupRestoreItem item)
    {
        var details = new StackPanel { Spacing = 6, Margin = new(28, 0, 0, 0) };
        var why = item.Message is { Length: > 0 } message ? message : item.Reason is { Length: > 0 } reason ? Model.ReasonSentence(reason) : null;
        if (why != null) details.Children.Add(Status(why, item.Selectable == true ? "muted" : "warn"));
        if (item.Library?.Paths is { Length: > 1 } paths) foreach (var path in paths) details.Children.Add(Text(path, 12, true));
        if (item.Library?.MissingPaths is { Length: > 0 } missingPaths) details.Children.Add(Text(T("web.admin.backups.restore.rows.cantOpen", new { paths = string.Join(", ", missingPaths) }), 12));
        if (item.MissingBlobs!.Length > 0) details.Children.Add(Status(T("web.admin.backups.restore.rows.filesMissing", AdminBackupText.Counted(item.MissingBlobs.Length)), "warn"));
        if (item.RestartRequired == true) details.Children.Add(Text("↻ " + T("web.admin.backups.restore.rows.restart"), 12, true));
        var changes = item.HomeSection?.Changes ?? item.Collection?.Changes ?? [];
        if (changes.Length > 0)
        {
            details.Children.Add(Text(T("web.admin.backups.restore.rows.changes"), 12, true));
            foreach (var change in changes) details.Children.Add(Text("• " + change, 12, true));
        }
        var settings = (item.Settings ?? []).Where(setting => setting.Status != "on_this_server").ToArray();
        if (settings.Length > 0)
        {
            var table = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
            table.ColumnDefinitions.Add(new() { Width = new(1.2, GridUnitType.Star) }); table.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); table.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            void Cell(UIElement element, int row, int column) { Grid.SetRow((FrameworkElement)element, row); Grid.SetColumn((FrameworkElement)element, column); table.Children.Add(element); }
            Cell(Text(T("web.admin.backups.restore.setting.column"), 12, true), 0, 0);
            Cell(Text(T("web.admin.backups.restore.setting.now"), 12, true), 0, 1);
            Cell(Text(T("web.admin.backups.restore.setting.inBackup"), 12, true), 0, 2);
            var index = 1;
            foreach (var setting in settings)
            {
                table.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var values = Model.SettingValues(setting);
                var key = new StackPanel { Spacing = 2 };
                var keyText = Text(setting.Key! + (setting.RestartRequired == true ? " ↻" : ""), 12); keyText.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
                key.Children.Add(keyText);
                if (setting.Class == "external_service") key.Children.Add(Text(T("web.admin.backups.restore.setting.external"), 11, true));
                Cell(key, index, 0);
                Cell(Rich(AdminBackupText.Runs("web.admin.backups.restore.setting.nowCell", new { value = values.Now }), 12, true), index, 1);
                Cell(Rich(AdminBackupText.Runs("web.admin.backups.restore.setting.inBackupCell", new { value = values.Backup }), 12), index, 2);
                index++;
            }
            details.Children.Add(table);
        }
        return details;
    }

    private FrameworkElement AccountRow(AdminBackupRestoreItem item, IReadOnlySet<string> selection)
    {
        var controller = RestoreController!;
        var account = item.Account!;
        var key = "account:" + item.Id;
        var open = _restoreOpen.Contains(key);
        var primary = Model.RequiredPrimary(item);
        var merges = account.Target != "new";
        var why = item.Message is { Length: > 0 } message ? message : item.Selectable != true && item.Reason is { Length: > 0 } reason ? Model.ReasonSentence(reason) : null;
        var pending = item.Selectable == true && merges ? Model.PendingSummary(account.Profiles!.Where(profile => profile.Selectable == true).SelectMany(profile => profile.DataTypes!)) : null;
        var contact = string.Join(" · ", new[] { pending, account.Username != item.Label ? account.Username : null, account.Email is { Length: > 0 } email ? email : null }.Where(part => !string.IsNullOrEmpty(part)));
        var status = item.Selectable != true ? null : merges ? T("web.admin.backups.restore.users.mergeInto", new { name = account.LocalUsername is { Length: > 0 } local ? local : account.Username }) : T("web.admin.backups.restore.users.newAccount");
        var row = new StackPanel { Spacing = 6, Padding = new(0, 8, 0, 8) };
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var slot = 0; slot < 4; slot++) line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var check = Check(item.Label!, "RestoreAccount-" + item.Id, selection.Contains(Model.ItemKey("users", item.Id!)), item.Selectable == true,
            chosen => controller.Toggle(Model.ToggleKeys("users", item), chosen));
        if (contact.Length > 0) AutomationProperties.SetHelpText(check, contact);
        var labelStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        labelStack.Children.Add(new TextBlock { Text = item.Label, FontWeight = FontWeights.Medium });
        if (contact.Length > 0) { var muted = Text(contact, 12, true); muted.TextTrimming = TextTrimming.CharacterEllipsis; muted.TextWrapping = TextWrapping.NoWrap; labelStack.Children.Add(muted); }
        check.Content = labelStack;
        line.Children.Add(check);
        var column = 1;
        if (account.Admin == true) { var admin = Text(T("web.admin.backups.restore.users.administrator"), 11, true); Grid.SetColumn(admin, column); line.Children.Add(admin); }
        column++;
        if (account.Profiles!.Length > 0) { var count = Text(T("web.admin.backups.restore.users.profileCount", AdminBackupText.Counted(account.Profiles.Length)), 11, true); Grid.SetColumn(count, column); line.Children.Add(count); }
        column++;
        var tag = StateTag(Model.State(item)); Grid.SetColumn(tag, column++); line.Children.Add(tag);
        var toggle = Disclosure("", "RestoreAccountDetails-" + item.Id, open, () => ToggleOpen(key));
        AutomationProperties.SetName(toggle, T(open ? "web.admin.backups.restore.users.hideProfiles" : "web.admin.backups.restore.users.showProfiles", new { name = item.Label }));
        Grid.SetColumn(toggle, column); line.Children.Add(toggle);
        row.Children.Add(line);
        if (open)
        {
            var details = new StackPanel { Spacing = 6, Margin = new(28, 0, 0, 0) };
            if (status != null) details.Children.Add(Text(status, 12, true));
            if (account.Admin == true) details.Children.Add(Text(T("web.admin.backups.restore.users.administrator"), 12, true));
            if (why != null) details.Children.Add(Status(why, item.Selectable == true || item.Reason == "same" ? "muted" : "warn"));
            if (item.Selectable != true) foreach (var difference in account.PolicyDifferences!) details.Children.Add(Status(Model.PolicyDifferenceSentence(account.Group, difference), "warn"));
            if (account.LibrariesLeftOut > 0) details.Children.Add(Status(T("web.admin.backups.restore.users.droppedLibraries", AdminBackupText.Counted(account.LibrariesLeftOut!.Value)), "warn"));
            var newProfiles = account.Profiles.Count(profile => profile.Selectable == true && profile.Target == "new");
            if (item.Selectable == true && newProfiles > account.ProfileRoom)
                details.Children.Add(Status(account.ProfileRoom == 0 ? T("web.admin.backups.restore.users.noRoom") : T("web.admin.backups.restore.users.room", AdminBackupText.Counted(account.ProfileRoom!.Value)), "warn"));
            var chosenProfiles = account.Profiles.Where(profile => selection.Contains(Model.ProfileKey(item.Id!, profile.Id!))).Select(profile => profile.Id!).ToHashSet(StringComparer.Ordinal);
            var profiles = new StackPanel { Spacing = 4 };
            AutomationProperties.SetName(profiles, T("web.admin.backups.restore.users.profilesOf", new { name = item.Label }));
            foreach (var profile in account.Profiles)
            {
                var locked = primary != null && profile.Id == primary.Id && chosenProfiles.Any(id => id != primary.Id);
                var selectable = profile.Selectable == true && item.Selectable == true && !locked;
                var same = profile.Status == "on_this_server" || profile.Reason == "same";
                var profileWhy = profile.Selectable == true || same ? null : profile.Message is { Length: > 0 } text ? text : profile.Reason is { Length: > 0 } profileReason ? Model.ReasonSentence(profileReason) : null;
                var parts = new[]
                {
                    profile.IsPrimary == true ? T("web.admin.backups.restore.users.primary") : null, profile.HasPin == true ? T("web.admin.backups.restore.users.hasPin") : null,
                    profile.Restricted == true ? T("web.admin.backups.restore.users.restricted") : null,
                    profile.Target == "new" ? T("web.admin.backups.restore.users.createdHere") : T("web.admin.backups.restore.users.mergesHere"),
                };
                var detail = same ? T("web.admin.backups.restore.users.matches")
                    : string.Join(" · ", new[] { string.Join(" · ", parts.Where(part => part != null)), Model.PendingSummary(profile.DataTypes!) }.Where(part => !string.IsNullOrEmpty(part)));
                var profileBox = new StackPanel { Spacing = 2 };
                var profileCheck = Check(profile.Name!, "RestoreProfile-" + item.Id + "-" + profile.Id, chosenProfiles.Contains(profile.Id!) || locked, selectable, chosen =>
                {
                    var keys = new List<string> { Model.ProfileKey(item.Id!, profile.Id!) };
                    if (chosen && primary != null && primary.Id != profile.Id) keys.Add(Model.ProfileKey(item.Id!, primary.Id!));
                    controller.Toggle(keys, chosen);
                });
                AutomationProperties.SetHelpText(profileCheck, detail);
                profileBox.Children.Add(profileCheck);
                profileBox.Children.Add(Text(detail, 12, true));
                if (profileWhy != null) profileBox.Children.Add(Status(profileWhy, profile.Selectable == true ? "muted" : "warn"));
                if (locked) profileBox.Children.Add(Status(T("web.admin.backups.restore.users.lockedPrimary"), "muted"));
                if (profile.NoLibraryAccess == true) profileBox.Children.Add(Status(T("web.admin.backups.restore.users.noLibraryAccess"), "warn"));
                else if (profile.LibrariesLeftOut > 0) profileBox.Children.Add(Status(T("web.admin.backups.restore.users.droppedLibraries", AdminBackupText.Counted(profile.LibrariesLeftOut!.Value)), "warn"));
                profiles.Children.Add(profileBox);
            }
            details.Children.Add(profiles);
            row.Children.Add(details);
        }
        return new Border { Child = row, BorderThickness = new(0, 0, 0, 1), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6) };
    }

    private FrameworkElement DataTypesPanel(AdminBackupRestoreState state, IReadOnlyList<AdminRestoreEntry> accounts, IReadOnlySet<string> selection)
    {
        var controller = RestoreController!;
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in accounts)
            foreach (var profile in Model.ChosenProfiles(entry.Item, selection))
                foreach (var data in profile.DataTypes!) counts[data.Id!] = counts.GetValueOrDefault(data.Id!) + Model.PendingCount(data);
        var panel = new StackPanel { Spacing = 8, Padding = new(16), CornerRadius = new(12), Background = UiKit.Brush("AppBackgroundBrush") };
        AutomationProperties.SetAutomationId(panel, "RestoreDataTypes");
        panel.Children.Add(Text(T("web.admin.backups.restore.users.dataLegend"), 13));
        var boxes = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 220 };
        foreach (var id in Model.DataTypeIds)
        {
            var count = counts.GetValueOrDefault(id);
            var label = count > 0 ? AdminBackupText.Accessible("web.admin.backups.restore.users.dataTypeCount", new { label = Model.DataTypeLabel(id), count }) : Model.DataTypeLabel(id);
            boxes.Children.Add(Check(label, "RestoreDataType-" + id, state.DataTypes.Contains(id), true, on => controller.SetDataType(id, on)));
        }
        panel.Children.Add(boxes);
        panel.Children.Add(Text(T("web.admin.backups.restore.users.mergeNote"), 12, true));
        return panel;
    }

    private FrameworkElement NeverRestored(IReadOnlyList<string> keys)
    {
        var stack = new StackPanel { Spacing = 6 };
        if (keys.Count == 0) return stack;
        var key = "never-restored";
        var open = _restoreOpen.Contains(key);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(Text("🔒 " + T("web.admin.backups.restore.choose.neverRestored"), 13, true));
        line.Children.Add(Disclosure(T(open ? "web.admin.backups.restore.choose.hideThem" : "web.admin.backups.restore.choose.showThem"), "RestoreNeverRestored", open, () => ToggleOpen(key)));
        stack.Children.Add(line);
        if (open)
        {
            var list = Text(string.Join(", ", Model.NeverRestoredSettings(keys)), 12, true);
            list.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"); list.IsTextSelectionEnabled = true;
            stack.Children.Add(list);
        }
        return stack;
    }

    // ── Step 2: review ───────────────────────────────────────────────

    private FrameworkElement ReviewView(AdminBackupRestoreState state, AdminBackupRestoreSet set, IReadOnlySet<string> selection)
    {
        var controller = RestoreController!;
        var view = new StackPanel { Spacing = 20, MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var group in Model.Plan(set, selection, state.DataTypes))
        {
            var box = new AdminSettingsGroup(group.Title);
            var list = new StackPanel { Spacing = 4, Padding = new(16) };
            foreach (var sentence in group.Sentences) list.Children.Add(Text("• " + T("web.admin.backups.restore.plan.sentence", new { sentence }), 14));
            box.Add(list);
            view.Children.Add(box);
        }
        var caveats = Model.Caveats(set, selection);
        if (caveats.Count > 0)
        {
            var box = new AdminSettingsGroup(T("web.admin.backups.restore.review.leftOut"), T("web.admin.backups.restore.review.leftOutDescription"));
            var list = new StackPanel { Spacing = 4, Padding = new(16) };
            AutomationProperties.SetAutomationId(list, "RestoreCaveats");
            foreach (var caveat in caveats) list.Children.Add(Rich(AdminBackupText.Runs("web.admin.backups.restore.review.caveat", new { label = caveat.Label, text = caveat.Text }), 13));
            box.Add(list);
            view.Children.Add(box);
        }
        if (state.Refusal is { } refusal)
        {
            var stack = new StackPanel { Spacing = 8 };
            var message = Text(T("web.admin.backups.restore.review.refused", new { reason = refusal.Message }));
            AutomationProperties.SetAutomationId(message, "RestoreRefusal");
            stack.Children.Add(message);
            if (refusal.RunningJobId is { } running)
            {
                var openRunning = Action(T("web.admin.backups.restore.review.openRunning"), "RestoreOpenRunning");
                openRunning.Click += async (_, _) =>
                {
                    if (_location != null && AdminShellPage.TryReplace(this, _location.WithQuery("job", running))) _location = _location.WithQuery("job", running);
                    await controller.OpenAsync(state.Source!, running);
                };
                stack.Children.Add(openRunning);
            }
            else if (refusal.PreviewChanged)
            {
                var reload = Action(T("web.admin.backups.restore.review.reloadPreview"), "RestoreReloadPreview");
                reload.Click += async (_, _) => await controller.ReloadPreviewAsync();
                stack.Children.Add(reload);
            }
            var panel = Surface(stack);
            AutomationProperties.SetLiveSetting(panel, AutomationLiveSetting.Assertive);
            view.Children.Add(panel);
        }
        if (set.Authenticated != true)
        {
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(Text(T("web.admin.backups.restore.review.unauthenticated"), 13));
            stack.Children.Add(Check(T("web.admin.backups.restore.review.trust"), "RestoreTrust", state.AcceptUnauthenticated, !state.Starting, controller.SetAcceptUnauthenticated));
            view.Children.Add(Surface(stack));
        }
        view.Children.Add(Text(T("web.admin.backups.restore.review.duration"), 13, true));
        var back = Action(T("common.back"), "RestoreBack");
        back.IsEnabled = !state.Starting;
        back.Click += (_, _) => controller.LeaveReview();
        var restore = Action(T("web.admin.backups.restore.review.restore") + (state.Starting ? "…" : ""), "RestoreStart", primary: true);
        restore.IsEnabled = AdminBackupRestoreController.CanRestore(state, selection);
        restore.Click += async (_, _) => await controller.StartAsync();
        view.Children.Add(Footer(back, restore));
        return view;
    }

    // ── Step 3: run and report ───────────────────────────────────────

    private FrameworkElement RunView(AdminBackupRestoreState state, AdminBackupRestoreJob job)
    {
        var controller = RestoreController!;
        var report = job.BackupRestoreResult;
        var items = report?.Items ?? [];
        var view = new StackPanel { Spacing = 20, MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };
        if (job.Terminal != true)
        {
            var progress = job.Progress;
            var percent = progress is { Total: > 0 } ? (int)Math.Round(progress.Current!.Value * 100d / progress.Total!.Value) : 0;
            var stopping = job.State == "canceling";
            var note = progress is { Total: > 0 }
                ? T("web.admin.backups.restore.run.steps", new { total = progress.Total, current_text = AdminText.FormatNumber(progress.Current ?? 0), total_text = AdminText.FormatNumber(progress.Total!.Value) })
                : T("web.admin.backups.restore.run.backsUpFirst");
            var box = new StackPanel { Spacing = 8 };
            AutomationProperties.SetLiveSetting(box, AutomationLiveSetting.Polite);
            var heading = Text(T(stopping ? "web.admin.backups.restore.run.stopping" : job.State == "queued" ? "web.admin.backups.restore.run.waiting" : "web.admin.backups.restore.run.restoring"));
            heading.FontWeight = FontWeights.Medium; AutomationProperties.SetAutomationId(heading, "RestoreRunState");
            box.Children.Add(heading);
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = percent };
            AutomationProperties.SetName(bar, T("web.admin.backups.restore.run.progressLabel")); AutomationProperties.SetAutomationId(bar, "RestoreRunProgress");
            box.Children.Add(bar);
            var active = state.List?.Active;
            box.Children.Add(Text(active?.Operation == "restore" && active.Message is { Length: > 0 } message
                ? T("web.admin.backups.restore.run.withServerMessage", new { text = note, message = AdminBackupValidation.SafeServerText(message) }) : note, 13, true));
            var stop = Action(T("web.admin.backups.restore.run.stop"), "RestoreStop");
            stop.IsEnabled = job.Cancelable == true && !stopping && !state.Cancelling && !_confirmingStop;
            var displayed = job.Id!;
            stop.Click += async (_, _) =>
            {
                _confirmingStop = true;
                var confirmed = await ShowDialogAsync(T("web.admin.backups.restore.run.stopTitle"), Text(T("web.admin.backups.restore.run.stopDescription")),
                    T("web.admin.backups.restore.run.stop"), T("web.admin.backups.restore.run.keepRestoring"), true, state.Owner, "RestoreStopDialog");
                _confirmingStop = false;
                if (confirmed && ReferenceEquals(controller, RestoreController)) await controller.CancelAsync(displayed);
                else QueueRestoreRender();
            };
            box.Children.Add(stop);
            view.Children.Add(Surface(box));
            view.Children.Add(ReportList(T("web.admin.backups.restore.run.doneSoFar"), items));
            return view;
        }
        var recovery = report?.BeforeRestoreBackupId is { } recoveryId ? state.List?.Items?.FirstOrDefault(backup => backup.Id == recoveryId) : null;
        var code = report?.ErrorCode ?? (report?.Interrupted == true ? "interrupted" : null);
        string? ending = job.State == "canceled" ? T("web.admin.backups.restore.run.canceled")
            : code != null ? Model.ReportErrorSentence(code) : job.State == "failed" ? Model.ReportErrorSentence("failed") : null;
        var detail = ending != null && code != "interrupted" ? report?.Error ?? (report == null ? job.Failure?.Detail : null) : null;
        var summary = new StackPanel { Spacing = 6 };
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        var headline = Text(report?.Counts is { } counts ? Model.ReportHeadline(counts) : T("web.admin.backups.restore.run.noReport"), 16);
        headline.FontWeight = FontWeights.Medium; AutomationProperties.SetAutomationId(headline, "RestoreReportHeadline");
        summary.Children.Add(headline);
        if (job.FinishedAt is { } finished) summary.Children.Add(Text(T("web.admin.backups.restore.run.finished", new { date = AdminBackupText.DateTimeText(finished) }), 13, true));
        if (ending != null) summary.Children.Add(Status(ending, "warn", "RestoreReportEnding"));
        if (detail is { Length: > 0 }) summary.Children.Add(Text(AdminBackupValidation.SafeServerText(detail), 13, true));
        view.Children.Add(summary);
        view.Children.Add(ReportList(T("web.admin.backups.restore.run.report"), items));
        if (report?.BeforeRestoreBackupId is { } before)
        {
            var copy = recovery?.CreatedAt is { } created ? AdminBackupText.TimeText(created) : before;
            var line = Rich(AdminBackupText.Runs("web.admin.backups.restore.run.recovery", new { copy }), 13, true,
                () => AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Backups, new Dictionary<string, string> { ["backup"] = before })));
            AutomationProperties.SetAutomationId(line, "RestoreRecoveryPoint");
            view.Children.Add(line);
        }
        var buttons = new List<Button>();
        if (report != null)
        {
            var download = Action(T("web.admin.backups.restore.run.downloadReport"), "RestoreDownloadReport");
            download.Click += async (_, _) => await SaveReportAsync(job);
            buttons.Add(download);
        }
        var again = Action(T("web.admin.backups.restore.run.chooseAgain"), "RestoreChooseAgain");
        again.Click += async (_, _) => await controller.ChooseAgainAsync();
        buttons.Add(again);
        var footer = Footer([.. buttons]); footer.HorizontalAlignment = HorizontalAlignment.Left;
        view.Children.Add(footer);
        return view;
    }

    private FrameworkElement ReportList(string label, IReadOnlyList<AdminBackupRestoreReportItem> items)
    {
        var group = new StackPanel { Spacing = 8 };
        if (items.Count == 0) return group;
        group.Children.Add(Heading(label, 14, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3));
        var list = new StackPanel { Spacing = 10 };
        AutomationProperties.SetAutomationId(list, "RestoreReportItems");
        foreach (var item in items)
        {
            var outcome = T("web.admin.backups.restore.outcome." + Model.Camel(item.Outcome!));
            var entry = new StackPanel { Spacing = 2 };
            entry.Children.Add(Rich(AdminBackupText.Runs("web.admin.backups.restore.run.itemLine", new { label = Model.DisplayLabel(item.Kind, item.Label), section = Model.SectionTitle(item.Section!), outcome }), 14));
            var details = new List<string?>
            {
                item.Outcome == "left_out" && item.Reason is { Length: > 0 } reason ? Model.ReasonSentence(reason) : null,
                item.Error is { } error ? AdminBackupValidation.SafeServerText(error) : null, item.Message,
            };
            details.AddRange(item.Notes!);
            if (item.MissingBlobs!.Length > 0) details.Add(T("web.admin.backups.restore.run.filesMissing", AdminBackupText.Counted(item.MissingBlobs.Length)));
            details.Add(item.RestartKeys!.Length > 0 ? T("web.admin.backups.restore.run.restartKeys", new { keys = string.Join(", ", item.RestartKeys) })
                : item.RestartRequired == true ? T("web.admin.backups.restore.run.restart") : null);
            foreach (var line in details.Where(line => !string.IsNullOrEmpty(line))) entry.Children.Add(Text(line!, 12, true));
            if (item.Account is { } account)
            {
                var accountBox = new StackPanel { Spacing = 2, Margin = new(16, 0, 0, 0) };
                if (string.IsNullOrEmpty(item.Message)) accountBox.Children.Add(Text(T(account.Created == true ? "web.admin.backups.restore.run.accountCreated" : "web.admin.backups.restore.run.accountMerged"), 12, true));
                foreach (var profile in account.Profiles!)
                {
                    var profileOutcome = profile.Created == true ? T("web.admin.backups.restore.outcome.created") : T("web.admin.backups.restore.outcome." + Model.Camel(profile.Outcome!));
                    accountBox.Children.Add(Text(profile.Label + " " + T("web.admin.backups.restore.run.profileOutcome", new { outcome = profileOutcome }), 12));
                    var lines = new List<string?> { profile.Outcome == "left_out" && profile.Reason is { Length: > 0 } profileReason ? Model.ReasonSentence(profileReason) : null, profile.Message };
                    lines.AddRange(profile.Notes!);
                    foreach (var line in lines.Where(line => !string.IsNullOrEmpty(line))) accountBox.Children.Add(Text(line!, 12, true));
                    foreach (var data in profile.Data!)
                    {
                        accountBox.Children.Add(Rich(AdminBackupText.Runs("web.admin.backups.restore.run.dataLine", new { label = Model.DataTypeLabel(data.Type!), counts = Model.DataCountsPhrase(data) }), 12));
                        foreach (var note in data.Notes!) accountBox.Children.Add(Text(note, 11, true));
                    }
                }
                if (account.Unmatched!.Length > 0) accountBox.Children.Add(Text(T("web.admin.backups.restore.run.unmatched", new { titles = Model.TitleList(account.Unmatched) }), 12, true));
                if (account.Hidden!.Length > 0) accountBox.Children.Add(Text(T("web.admin.backups.restore.run.hidden", new { titles = Model.TitleList(account.Hidden) }), 12, true));
                entry.Children.Add(accountBox);
            }
            list.Children.Add(entry);
        }
        group.Children.Add(list);
        return group;
    }

    /// <summary>Official downloadReport: the job's report as indented JSON, to a path the admin picks.</summary>
    private async Task SaveReportAsync(AdminBackupRestoreJob job)
    {
        var controller = RestoreController; if (controller?.State.Owner is not { IsCurrent: true } owner) return;
        var result = job.BackupRestoreResult;
        var stem = result?.BackupId ?? result?.UploadId ?? job.Id ?? "report";
        var safe = new string(stem.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.').Take(96).ToArray());
        var path = await PickSavePathAsync("silo-restore-report-" + safe + ".json");
        if (path == null || !owner.IsCurrent) return;
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        });
        try
        {
            await DiagnosticsFileTransfer.SaveAsync(owner, path, async (stream, token) => { await stream.WriteAsync(json, token); return true; });
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Toast(T("configure.native.backups.download.failed"), true); }
    }
}
