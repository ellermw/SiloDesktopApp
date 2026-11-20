using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using Microsoft.Extensions.DependencyInjection;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using Prep = SiloPlayer.Core.Services.Admin.AdminPreparationPresentation;
using Presentation = SiloPlayer.Core.Services.Admin.AdminPreparedFilesPresentation;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminPreparedFilesPage
{
    private const string PrepKey = "web.admin.downloads.preparation";

    /// <summary>The Preparation tab (official <c>PreparationTab.tsx</c>): the live queue with state and worker filters, selection and per-job details.</summary>
    private sealed class PreparationView : TabView
    {
        private readonly StackPanel _root = Stack(20);
        private readonly LogsWrapPanel _filterRow = new() { Spacing = 8 };
        private readonly TextBox _filter;
        private readonly Button _clearAll;
        private readonly StackPanel _top = Stack(20);
        private readonly StackPanel _bottom = Stack(20);
        private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
        private readonly HashSet<string> _details = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FfmpegState> _ffmpeg = new(StringComparer.Ordinal);
        private string _search_text = "";
        private string? _stateFilter;
        private string? _workerFilter;

        private sealed record FfmpegState(bool Loading, IReadOnlyList<AdminActivityLogRow> Rows, bool Failed);

        public PreparationView(AdminPreparedFilesPage page) : base(page)
        {
            _filter = Input(T(PrepKey + ".filter.placeholder"), "PreparedFilesPreparationSearch", 420);
            _filter.Height = 32; _filter.FontSize = 13; _filter.MaxWidth = 420; _filter.HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(_filter, T(PrepKey + ".filter.label"));
            // The starting text is never a change: only a different value updates the filter.
            _filter.TextChanged += (_, _) =>
            {
                if (_filter.Text == _search_text) return;
                _search_text = _filter.Text;
                Refresh();
            };
            _clearAll = Button(T(PrepKey + ".filter.clearAll"), "PreparedFilesPreparationClearFilters", "GhostButtonStyle");
            _clearAll.Click += (_, _) => { _stateFilter = null; _workerFilter = null; Refresh(); };
            _filterRow.Children.Add(_filter);
            _filterRow.Children.Add(_clearAll);
            _root.Children.Add(_top); _root.Children.Add(_filterRow); _root.Children.Add(_bottom);
        }

        public override UIElement Root => _root;
        private AdminPreparedFilesSnapshot? _last;

        public override void Reset()
        {
            _selected.Clear(); _details.Clear(); _ffmpeg.Clear();
            _stateFilter = null; _workerFilter = null; _search_text = "";
            if (_filter.Text.Length > 0) _filter.Text = "";
            _last = null;
        }

        public override void Update(AdminPreparedFilesSnapshot state) { _last = state; Build(state); }
        private void Refresh() { if (_last != null) Build(P.Controller.State); }

        private void Build(AdminPreparedFilesSnapshot state)
        {
            _top.Children.Clear(); _bottom.Children.Clear();
            var items = state.Preparations;
            var busy = state.ActionPending;
            // Collapsing the filter row would drop keyboard focus from the box, so it only collapses where it must.
            if (!state.PreparationsLoaded && state.PreparationsError == null)
            {
                _filterRow.Visibility = Visibility.Collapsed;
                _top.Children.Add(Message(T(PrepKey + ".loading")));
                return;
            }
            if (!state.PreparationsLoaded || (items.Count == 0 && state.PreparationsError != null))
            {
                _filterRow.Visibility = Visibility.Collapsed;
                _top.Children.Add(Message(T(PrepKey + ".unavailable"), "PreparedFilesPreparationReload", () => _ = P.Controller.ReloadPreparationsAsync()));
                return;
            }
            _filterRow.Visibility = Visibility.Visible;
            if (state.PreparationsError != null) _top.Children.Add(StaleBanner("PreparedFilesPreparationReload", () => _ = P.Controller.ReloadPreparationsAsync()));
            var counts = state.PreparationCounts;
            var total = Prep.States.Sum(s => Prep.StateCount(counts, s));
            var workers = items.Where(x => x.State == "running").Select(x => (Job: x, Worker: Prep.Worker(x))).Where(x => x.Worker != null)
                .GroupBy(x => x.Worker!.Key).Select(g => (Worker: g.First().Worker!, Count: g.Count())).OrderByDescending(x => x.Count).ToArray();
            var q = _search_text.Trim().ToLower(System.Globalization.CultureInfo.CurrentCulture);
            var filtered = items.Where(item => (_stateFilter == null || item.State == _stateFilter)
                && (_workerFilter == null || Prep.Worker(item)?.Key == _workerFilter)
                && (q.Length == 0 || Prep.SearchText(item).Contains(q, StringComparison.Ordinal))).ToArray();
            var known = items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            _selected.RemoveWhere(id => !known.Contains(id));
            var selectedItems = filtered.Where(x => _selected.Contains(x.Id)).ToArray();
            var activeFilters = (_stateFilter != null ? 1 : 0) + (_workerFilter != null ? 1 : 0);

            if (total > 0) _top.Children.Add(StateBar(counts, total, workers));
            _clearAll.Visibility = _search_text.Length > 0 || activeFilters > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (selectedItems.Length > 0) _bottom.Children.Add(SelectionBar(selectedItems, busy, state.DestructiveBlocked));
            if (!string.IsNullOrEmpty(q) || activeFilters > 0)
                _bottom.Children.Add(Text(T(PrepKey + ".showing", new { total = items.Count, shown = filtered.Length, total_text = items.Count }), 12, true));
            if (items.Count < total)
                _bottom.Children.Add(Text(T(PrepKey + ".listingFirst", new
                {
                    total, shown = items.Count.ToString("N0", AppStrings.FormatCulture), total_text = total.ToString("N0", AppStrings.FormatCulture),
                }), 12, true));

            if (filtered.Length == 0)
            {
                _bottom.Children.Add(Message(items.Count > 0 ? T(PrepKey + ".noMatches") : T(PrepKey + ".empty")));
                return;
            }
            _bottom.Children.Add(Table(filtered, state, busy));
        }

        private UIElement StateBar(AdminDownloadPreparationCounts counts, int total, IReadOnlyList<(AdminPreparationWorker Worker, int Count)> workers)
        {
            var root = Stack(12);
            root.Children.Add(Text(AppStrings.Uppercase(T(PrepKey + ".stateHeading")), 10, true));
            var bar = new Grid { Height = 6, ColumnSpacing = 0 };
            var index = 0;
            foreach (var state in Prep.States.Where(s => Prep.StateCount(counts, s) > 0))
            {
                bar.ColumnDefinitions.Add(new() { Width = new(Prep.StateCount(counts, state), GridUnitType.Star) });
                var piece = new Border { Background = StateBrush(state) };
                Grid.SetColumn(piece, index++); bar.Children.Add(piece);
            }
            AutomationProperties.SetName(bar, T(PrepKey + ".stateBarLabel"));
            root.Children.Add(new Border { Child = bar, CornerRadius = new CornerRadius(3) });
            var chips = new LogsWrapPanel { Spacing = 16 };
            foreach (var state in Prep.States)
            {
                var toggle = new ToggleButton { Padding = new Thickness(4, 2, 4, 2), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
                var label = Inline(6);
                label.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = StateBrush(state), VerticalAlignment = VerticalAlignment.Center });
                label.Children.Add(Text(Prep.StateLabel(state), 12));
                label.Children.Add(Text(Prep.StateCount(counts, state).ToString("N0", AppStrings.FormatCulture), 12, true));
                toggle.Content = label;
                toggle.IsChecked = _stateFilter == state;
                if (_stateFilter != null && _stateFilter != state) toggle.Opacity = 0.35;
                AutomationProperties.SetAutomationId(toggle, "PreparedFilesPreparationState_" + state);
                AutomationProperties.SetName(toggle, Prep.StateLabel(state));
                var captured = state;
                toggle.Checked += (_, _) => { _stateFilter = captured; Refresh(); };
                toggle.Unchecked += (_, _) => { if (_stateFilter == captured) { _stateFilter = null; Refresh(); } };
                chips.Children.Add(toggle);
            }
            root.Children.Add(chips);
            if (workers.Count > 0)
            {
                var section = new Border { BorderBrush = Tint("BorderBrush", 0.6), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0) };
                var inner = Stack(8);
                inner.Children.Add(Text(AppStrings.Uppercase(T(PrepKey + ".byWorker")), 10, true));
                var row = new LogsWrapPanel { Spacing = 6 };
                foreach (var (worker, count) in workers)
                {
                    var toggle = new ToggleButton { Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(6) };
                    var content = Inline(6);
                    content.Children.Add(Text(worker.Label, 11, true));
                    content.Children.Add(Text(worker.Name, 11));
                    content.Children.Add(Text(count.ToString(AppStrings.FormatCulture), 11, true));
                    toggle.Content = content;
                    toggle.IsChecked = _workerFilter == worker.Key;
                    if (_workerFilter != null && _workerFilter != worker.Key) toggle.Opacity = 0.35;
                    AutomationProperties.SetAutomationId(toggle, "PreparedFilesPreparationWorker_" + worker.Key);
                    AutomationProperties.SetName(toggle, worker.Label + " " + worker.Name);
                    var key = worker.Key;
                    toggle.Checked += (_, _) => { _workerFilter = key; Refresh(); };
                    toggle.Unchecked += (_, _) => { if (_workerFilter == key) { _workerFilter = null; Refresh(); } };
                    row.Children.Add(toggle);
                }
                inner.Children.Add(row);
                section.Child = inner;
                root.Children.Add(section);
            }
            return Panel(root, 16, 16, true);
        }

        private static Microsoft.UI.Xaml.Media.Brush StateBrush(string state) => state switch
        {
            "running" => Brush("AccentBrush"),
            "queued" => Tint("SecondaryTextBrush", 0.5),
            "retrying" => Brush("WarningBrush"),
            "paused" => Tint("SecondaryTextBrush", 0.25),
            _ => Brush("ErrorBrush"),
        };

        private UIElement SelectionBar(IReadOnlyList<AdminDownloadPreparation> selected, bool busy, bool blocked)
        {
            var pausable = selected.Where(Prep.CanPause).ToArray();
            var resumable = selected.Where(Prep.CanResume).ToArray();
            var row = new LogsWrapPanel { Spacing = 8 };
            row.Children.Add(Text(T(PrepKey + ".selection.count", Prep.JobCount(selected.Count)), 12));
            var pause = Button(pausable.Length > 0 ? T(PrepKey + ".selection.pauseCount", Prep.JobCount(pausable.Length)) : T(PrepKey + ".selection.pause"), "PreparedFilesPauseSelected");
            pause.IsEnabled = !busy && pausable.Length > 0;
            pause.Click += (_, _) => _ = P.Controller.RunPreparationActionAsync("pause", pausable);
            var resume = Button(resumable.Length > 0 ? T(PrepKey + ".selection.resumeCount", Prep.JobCount(resumable.Length)) : T(PrepKey + ".selection.resume"), "PreparedFilesResumeSelected");
            resume.IsEnabled = !busy && resumable.Length > 0;
            resume.Click += (_, _) => _ = P.Controller.RunPreparationActionAsync("resume", resumable);
            var cancel = Button(T(PrepKey + ".selection.cancelCount", Prep.JobCount(selected.Count)), "PreparedFilesCancelSelected", "DestructiveButtonStyle");
            cancel.IsEnabled = !busy && !blocked;
            var targets = selected.ToArray();
            cancel.Click += (_, _) => ConfirmCancel(targets);
            var clear = Button(T(PrepKey + ".selection.clear"), "PreparedFilesPreparationClearSelection", "GhostButtonStyle");
            clear.IsEnabled = !busy;
            clear.Click += (_, _) => { _selected.Clear(); Refresh(); };
            row.Children.Add(pause); row.Children.Add(resume); row.Children.Add(cancel); row.Children.Add(clear);
            var panel = Panel(row, 10, 10, true);
            AutomationProperties.SetAutomationId(panel, "PreparedFilesPreparationSelection");
            AutomationProperties.SetName(panel, T(PrepKey + ".selection.toolbarLabel"));
            return panel;
        }

        private static readonly double[] Widths = [32, 280, 300, 150, 280, 220, 150];

        private UIElement Table(IReadOnlyList<AdminDownloadPreparation> rows, AdminPreparedFilesSnapshot state, bool busy)
        {
            var stack = Stack(0);
            var headings = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 10, 12, 10), Background = Tint("MutedBrush", 0.3) };
            foreach (var width in Widths) headings.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
            var allSelected = rows.Count > 0 && rows.All(x => _selected.Contains(x.Id));
            var selectAll = Check("", "PreparedFilesPreparationSelectAll", allSelected, !busy, on =>
            {
                foreach (var row in rows) if (on) _selected.Add(row.Id); else _selected.Remove(row.Id);
                Refresh();
            });
            AutomationProperties.SetName(selectAll, allSelected ? T(PrepKey + ".table.clearSelection") : T(PrepKey + ".table.selectAll"));
            headings.Children.Add(selectAll);
            string[] titles = [PrepKey + ".column.title", PrepKey + ".column.output", PrepKey + ".column.worker", PrepKey + ".column.progress", PrepKey + ".column.requestedBy"];
            for (var i = 0; i < titles.Length; i++)
            {
                var heading = Text(AppStrings.Uppercase(T(titles[i])), 10, true); heading.FontWeight = FontWeights.SemiBold;
                Grid.SetColumn(heading, i + 1); headings.Children.Add(heading);
            }
            stack.Children.Add(headings);
            foreach (var job in rows) stack.Children.Add(Row(job, state, busy));
            var scroller = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var panel = Panel(scroller, 12, 0, true);
            AutomationProperties.SetAutomationId(panel, "PreparedFilesPreparationTable");
            return panel;
        }

        private UIElement Row(AdminDownloadPreparation job, AdminPreparedFilesSnapshot state, bool busy)
        {
            var title = Prep.Title(job);
            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 10, 12, 10) };
            foreach (var width in Widths) grid.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
            var select = Check("", "PreparedFilesPreparationSelect_" + job.Id, _selected.Contains(job.Id), !busy, on =>
            {
                if (on) _selected.Add(job.Id); else _selected.Remove(job.Id);
                Refresh();
            });
            AutomationProperties.SetName(select, T(PrepKey + ".row.select", new { title }));
            grid.Children.Add(select);

            var titleBlock = Stack(2);
            if (!string.IsNullOrEmpty(job.ContentId))
            {
                var link = new HyperlinkButton { Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left };
                var inner = Stack(2);
                var heading = Strong(title, 13); heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.TextWrapping = TextWrapping.NoWrap;
                inner.Children.Add(heading);
                inner.Children.Add(Cell(Prep.Subtitle(job), true));
                inner.Children.Add(Cell(Prep.FormatSource(job), true));
                link.Content = inner;
                AutomationProperties.SetAutomationId(link, "PreparedFilesPreparationItem_" + job.Id);
                AutomationProperties.SetName(link, title);
                var contentId = job.ContentId;
                link.Click += (_, _) => P.OpenItem(contentId);
                titleBlock.Children.Add(link);
            }
            else
            {
                var heading = Strong(title, 13); heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.TextWrapping = TextWrapping.NoWrap;
                titleBlock.Children.Add(heading);
                titleBlock.Children.Add(Cell(Prep.Subtitle(job), true));
                titleBlock.Children.Add(Cell(Prep.FormatSource(job), true));
            }
            Grid.SetColumn(titleBlock, 1); grid.Children.Add(titleBlock);

            var output = Inline(8);
            output.Children.Add(FormatBadge(job.Format));
            var outputText = Text(Prep.FormatOutput(job), 11); outputText.FontWeight = FontWeights.Medium; outputText.MaxWidth = 220;
            output.Children.Add(outputText);
            Grid.SetColumn(output, 2); grid.Children.Add(output);

            var worker = Prep.Worker(job);
            UIElement workerCell = worker == null ? Text(T(PrepKey + ".notAssigned"), 11, true) : WorkerBadge(worker);
            Grid.SetColumn((FrameworkElement)workerCell, 3); grid.Children.Add(workerCell);

            var progress = Progress(job);
            Grid.SetColumn(progress, 4); grid.Children.Add(progress);
            var requesters = Requesters(job);
            Grid.SetColumn(requesters, 5); grid.Children.Add(requesters);

            var pending = state.PendingPreparationIds.Contains(job.Id);
            var actions = Inline(4);
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            if (pending) { var ring = new ProgressRing { IsActive = true, Width = 16, Height = 16 }; AutomationProperties.SetName(ring, T(PrepKey + ".row.working")); actions.Children.Add(ring); }
            else
            {
                if (Prep.CanPause(job))
                    actions.Children.Add(IconAction("", "PreparedFilesPause_" + job.Id, T(PrepKey + ".row.pauseLabel", new { title }),
                        job.State == "running" ? T(PrepKey + ".row.pauseRunningTitle") : T(PrepKey + ".row.pauseWaitingTitle"), !busy,
                        () => _ = P.Controller.RunPreparationActionAsync("pause", [job])));
                if (Prep.CanResume(job))
                    actions.Children.Add(IconAction("", "PreparedFilesResume_" + job.Id, T(PrepKey + ".row.resumeLabel", new { title }), T(PrepKey + ".row.resumeTitle"), !busy,
                        () => _ = P.Controller.RunPreparationActionAsync("resume", [job])));
                var failed = job.State == "failed";
                actions.Children.Add(IconAction("", "PreparedFilesCancel_" + job.Id, failed ? T(PrepKey + ".row.removeLabel", new { title }) : T(PrepKey + ".row.cancelLabel", new { title }),
                    failed ? T(PrepKey + ".row.removeTitle") : T(PrepKey + ".row.cancelTitle"), !busy && !state.DestructiveBlocked, () => ConfirmCancel([job])));
            }
            var open = _details.Contains(job.Id);
            var details = new Button { Style = (Style)Application.Current.Resources["GhostButtonStyle"], Padding = new Thickness(6, 2, 6, 2), MinHeight = 28 };
            var detailContent = Inline(4);
            detailContent.Children.Add(Text(T(PrepKey + ".row.details"), 11, true));
            detailContent.Children.Add(new FontIcon { Glyph = open ? "" : "", FontSize = 10 });
            details.Content = detailContent;
            AutomationProperties.SetAutomationId(details, "PreparedFilesPreparationDetails_" + job.Id);
            AutomationProperties.SetName(details, T(PrepKey + ".row.detailsLabel", new { title }));
            AutomationProperties.SetItemStatus(details, open ? "expanded" : "collapsed");
            details.Click += (_, _) => { if (!_details.Add(job.Id)) { _details.Remove(job.Id); _ffmpeg.Remove(job.Id); } Refresh(); };
            actions.Children.Add(details);
            Grid.SetColumn(actions, 6); grid.Children.Add(actions);

            var container = Stack(0);
            container.Children.Add(grid);
            if (open) container.Children.Add(Details(job));
            var rule = new Border { Child = container, BorderBrush = Tint("BorderBrush", 0.3), BorderThickness = new Thickness(0, 0, 0, 1) };
            if (_selected.Contains(job.Id)) rule.Background = Tint("AccentBrush", 0.05);
            AutomationProperties.SetAutomationId(rule, "PreparedFilesPreparation_" + job.Id);
            return rule;
        }

        private static Button IconAction(string glyph, string id, string name, string tip, bool enabled, Action click)
        {
            var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 13 }, Style = (Style)Application.Current.Resources["GhostButtonStyle"], Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(0), IsEnabled = enabled };
            AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, tip);
            button.Click += (_, _) => click();
            return button;
        }

        private static UIElement FormatBadge(string format)
        {
            var meta = AdminActivityPresentation.MethodMeta(format);
            var tone = meta.Tone switch { ActivityTone.Info => "active", ActivityTone.Destructive => "danger", ActivityTone.Warning => "warning", ActivityTone.Success => "success", _ => "muted" };
            var pill = Pill(meta.Label, tone);
            pill.CornerRadius = new CornerRadius(4);
            return pill;
        }

        private static UIElement WorkerBadge(AdminPreparationWorker worker)
        {
            var content = Inline(4);
            var label = Text(worker.Label, 10, true); label.Opacity = 0.7;
            content.Children.Add(label);
            var name = Text(worker.Name, 10); name.FontWeight = FontWeights.SemiBold; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.MaxWidth = 90;
            content.Children.Add(name);
            return new Border { Child = content, BorderBrush = Tint("AccentBrush", 0.25), BorderThickness = new Thickness(1), Background = Tint("AccentBrush", 0.1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Left };
        }

        private static FrameworkElement Progress(AdminDownloadPreparation job)
        {
            var stack = Stack(2);
            switch (job.State)
            {
                case "running":
                {
                    var percent = Prep.Percent(job);
                    if (percent == null)
                    {
                        var elapsed = Prep.SecondsSince(job.StartedAt);
                        var head = Text(elapsed != null ? T(PrepKey + ".progress.runningElapsed", new { label = Prep.RunningLabel(job), elapsed = Prep.FormatClock(elapsed.Value) }) : Prep.RunningLabel(job), 11);
                        head.FontWeight = FontWeights.SemiBold; head.Foreground = Brush("AccentBrush");
                        stack.Children.Add(head);
                        stack.Children.Add(Text(job.ProgressUnavailable ? T(PrepKey + ".progress.noReports") : T(PrepKey + ".progress.waitingFirstReport"), 10, true));
                    }
                    else
                    {
                        var line = new Grid { ColumnSpacing = 8 };
                        line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                        var pct = Text(T(PrepKey + ".progress.percent", new { percent = (long)Math.Floor(percent.Value) }), 11); pct.FontWeight = FontWeights.SemiBold;
                        line.Children.Add(pct);
                        var detail = Text(string.Join(" · ", new[] { Prep.FormatSpeed(job.Progress?.Speed), Prep.FormatRemaining(Prep.RemainingSeconds(job)) }.Where(x => !string.IsNullOrEmpty(x))), 11, true);
                        detail.TextWrapping = TextWrapping.NoWrap; detail.TextTrimming = TextTrimming.CharacterEllipsis; detail.HorizontalAlignment = HorizontalAlignment.Right;
                        Grid.SetColumn(detail, 1); line.Children.Add(detail);
                        stack.Children.Add(line);
                        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = percent.Value, Height = 6 };
                        AutomationProperties.SetName(bar, T(PrepKey + ".progress.label", new { title = Prep.Title(job) }));
                        stack.Children.Add(bar);
                    }
                    break;
                }
                case "queued":
                    stack.Children.Add(Text(job.QueuePosition == 1 ? T(PrepKey + ".progress.queuedNext")
                        : job.QueuePosition is > 0 ? T(PrepKey + ".progress.queuedPosition", new { position = job.QueuePosition.Value.ToString("N0", AppStrings.FormatCulture) })
                        : T(PrepKey + ".progress.queuedWaiting"), 11, true));
                    break;
                case "retrying":
                {
                    var head = Text(T(PrepKey + ".progress.waitingToRetry"), 11); head.FontWeight = FontWeights.SemiBold; head.Foreground = Brush("WarningBrush");
                    stack.Children.Add(head);
                    stack.Children.Add(Text(string.Join(" · ", new[]
                    {
                        T(PrepKey + ".progress.attempt", new { attempt = job.Attempts + 1, max = job.MaxAttempts, retry_in = Prep.FormatRetryIn(job.NextRetryAt) }), job.Error,
                    }.Where(x => !string.IsNullOrEmpty(x))), 10, true));
                    break;
                }
                case "paused":
                {
                    var head = Text(T(PrepKey + ".progress.paused"), 11, true); head.FontWeight = FontWeights.SemiBold;
                    stack.Children.Add(head);
                    stack.Children.Add(Text(string.Join(" · ", new[] { Prep.FormatAgo(job.PausedAt), T(PrepKey + ".progress.wontStart") }.Where(x => !string.IsNullOrEmpty(x))), 10, true));
                    break;
                }
                default:
                {
                    var head = Text(T(PrepKey + ".progress.failed"), 11); head.FontWeight = FontWeights.SemiBold; head.Foreground = Brush("ErrorBrush");
                    stack.Children.Add(head);
                    var detail = Text(string.Join(" · ", new[] { T(PrepKey + ".progress.afterAttempts", new { count = job.Attempts }), Prep.FormatAgo(job.FailedAt), job.Error }.Where(x => !string.IsNullOrEmpty(x))), 10, true);
                    ToolTipService.SetToolTip(detail, job.Error);
                    stack.Children.Add(detail);
                    break;
                }
            }
            return stack;
        }

        private FrameworkElement Requesters(AdminDownloadPreparation job)
        {
            if (job.Requesters.Count == 0) return Text(T(PrepKey + ".requesters.none"), 11, true);
            var first = job.Requesters[0];
            var stack = Stack(2);
            var line = Inline(6);
            var name = Prep.RequesterName(first);
            var registered = AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.Users);
            var link = new HyperlinkButton { Content = name, Padding = new Thickness(0), FontSize = 12, IsEnabled = registered && !string.IsNullOrEmpty(first.UserId) };
            AutomationProperties.SetAutomationId(link, "PreparedFilesPreparationRequester_" + job.Id);
            if (!link.IsEnabled) ToolTipService.SetToolTip(link, T("configure.native.preparedFiles.linkUnavailable"));
            var userId = first.UserId;
            link.Click += (_, _) => AdminShellPage.Open(AdminRouteRequest.UserDetail(userId));
            line.Children.Add(link);
            if (!string.IsNullOrEmpty(first.ProfileName)) line.Children.Add(Pill(first.ProfileName, "active"));
            stack.Children.Add(line);
            var rest = job.Requesters.Count - 1;
            stack.Children.Add(Text(rest > 0 ? T(PrepKey + ".requesters.deviceMore", new { count = rest, count_text = rest, device = Prep.RequesterDevice(first) }) : Prep.RequesterDevice(first), 10, true));
            return stack;
        }

        // ---- Details panel and FFmpeg console ------------------------------------------------------

        private UIElement Details(AdminDownloadPreparation job)
        {
            var showFfmpeg = _ffmpeg.ContainsKey(job.Id);
            var root = Stack(12);
            var header = new Grid { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var left = Stack(4);
            var chip = Inline(8);
            chip.Children.Add(Pill(AppStrings.Uppercase(T(PrepKey + ".details.chip")), "muted"));
            chip.Children.Add(Text(showFfmpeg ? T(PrepKey + ".details.headingWithFfmpeg") : T(PrepKey + ".details.heading"), 11));
            left.Children.Add(chip);
            left.Children.Add(Text(job.Id, 10, true, true));
            header.Children.Add(left);
            var buttons = Inline(6);
            var ffmpeg = Button(showFfmpeg ? T(PrepKey + ".details.hideFfmpeg") : "FFmpeg", "PreparedFilesPreparationFfmpeg_" + job.Id);
            AutomationProperties.SetItemStatus(ffmpeg, showFfmpeg ? "pressed" : "");
            ffmpeg.Click += (_, _) => ToggleFfmpeg(job);
            buttons.Children.Add(ffmpeg);
            var logs = Button(T(PrepKey + ".details.viewLogs"), "PreparedFilesPreparationLogs_" + job.Id);
            var logsRegistered = AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.ServerLog) && !string.IsNullOrEmpty(job.LogSessionId);
            logs.IsEnabled = logsRegistered;
            if (!logsRegistered) ToolTipService.SetToolTip(logs, T("configure.native.preparedFiles.linkUnavailable"));
            var session = job.LogSessionId;
            logs.Click += (_, _) => AdminShellPage.Open(new AdminRouteRequest(AdminRoute.ServerLog,
                new Dictionary<string, string> { ["playback_session_id"] = session, ["component"] = "ffmpeg" }));
            buttons.Children.Add(logs);
            Grid.SetColumn(buttons, 1); header.Children.Add(buttons);
            root.Children.Add(header);

            var cards = new LogsMetricGrid(4, 1) { ColumnSpacing = 8, RowSpacing = 8 };
            var source = new[]
            {
                Line(T(PrepKey + ".details.container"), string.IsNullOrEmpty(job.Source.Container) ? "—" : job.Source.Container.ToUpperInvariant()),
                Line(T(PrepKey + ".details.video"), Joined(job.Source.Resolution, AdminActivityPresentation.FormatCodec(job.Source.VideoCodec), job.Source.Hdr ? "HDR" : "", Prep.FormatBitrate(job.Source.BitrateKbps))),
                Line(T(PrepKey + ".details.audio"), Prep.FormatSourceAudio(job)),
                Line(T(PrepKey + ".details.size"), Joined(Prep.FormatFileSize(job.Source.FileSize), job.Source.DurationSeconds > 0 ? Prep.FormatClock(job.Source.DurationSeconds) : ""), muted: true),
            };
            cards.Add(Card(T(PrepKey + ".details.source"), null, source));
            var output = new List<UIElement>
            {
                Line(T(PrepKey + ".details.container"), job.Output.Container.ToUpperInvariant()),
                Line(T(PrepKey + ".details.video"), Prep.FormatVideoOutput(job)),
            };
            if (Prep.FormatToneMap(job) is { } toneMap) output.Add(Line(T(PrepKey + ".details.toneMap"), toneMap));
            output.Add(Line(T(PrepKey + ".details.audio"), Prep.FormatAudioOutput(job)));
            cards.Add(Card(T(PrepKey + ".details.output"), FormatBadge(job.Format), output));
            cards.Add(Card(T(PrepKey + ".details.job"), null, JobLines(job)));
            var requested = new List<UIElement>();
            if (job.Requesters.Count == 0) requested.Add(Text(T(PrepKey + ".details.noWaiting"), 11, true));
            foreach (var r in job.Requesters)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var who = Stack(0);
                who.Children.Add(Cell(string.IsNullOrEmpty(r.ProfileName) ? Prep.RequesterName(r) : T(PrepKey + ".details.requesterProfile", new { name = Prep.RequesterName(r), profile = r.ProfileName })));
                who.Children.Add(Cell(Prep.RequesterDevice(r), true));
                row.Children.Add(who);
                var status = Text(r.Status == "preparing" ? T(PrepKey + ".details.requesterWaiting") : r.Status, 10, true); Grid.SetColumn(status, 1); row.Children.Add(status);
                requested.Add(row);
            }
            cards.Add(Card(T(PrepKey + ".details.requestedBy"), null, requested));
            root.Children.Add(cards);
            if (showFfmpeg) root.Children.Add(Console(job));
            var panel = Panel(root, 0, 16, false);
            panel.Background = Tint("MutedBrush", 0.25);
            AutomationProperties.SetAutomationId(panel, "PreparedFilesPreparationDetailsPanel_" + job.Id);
            return panel;
        }

        private static string Joined(params string?[] parts) => parts.Where(x => !string.IsNullOrEmpty(x)).DefaultIfEmpty("—").Aggregate((a, b) => a == "—" ? b : a + " · " + b);

        private static UIElement Line(string label, string value, bool muted = false)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = new(72) });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.Children.Add(Text(label, 10, true));
            var text = Text(value, 11, muted); text.FontWeight = FontWeights.Medium;
            Grid.SetColumn(text, 1); row.Children.Add(text);
            return row;
        }

        private static UIElement Card(string label, UIElement? badge, IEnumerable<UIElement> lines)
        {
            var stack = Stack(6);
            var head = Inline(8);
            head.Children.Add(Text(AppStrings.Uppercase(label), 10, true));
            if (badge != null) head.Children.Add(badge);
            stack.Children.Add(head);
            foreach (var line in lines) stack.Children.Add(line);
            return Panel(stack, 10, 10, true);
        }

        private static IEnumerable<UIElement> JobLines(AdminDownloadPreparation job)
        {
            var lines = new List<UIElement> { Line(T(PrepKey + ".details.status"), Prep.JobStatus(job)) };
            var percent = Prep.Percent(job);
            if (job.State == "running" && job.Progress is { } progress)
            {
                lines.Add(Line(T(PrepKey + ".details.encoded"), progress.DurationSeconds > 0
                    ? percent != null
                        ? T(PrepKey + ".details.encodedOfPercent", new { encoded = Prep.FormatClock(progress.EncodedSeconds), duration = Prep.FormatClock(progress.DurationSeconds), percent = (long)Math.Floor(percent.Value) })
                        : T(PrepKey + ".details.encodedOf", new { encoded = Prep.FormatClock(progress.EncodedSeconds), duration = Prep.FormatClock(progress.DurationSeconds) })
                    : Prep.FormatClock(progress.EncodedSeconds)));
                if (progress.Speed > 0)
                    lines.Add(Line(T(PrepKey + ".details.speed"), string.Join(" · ", new[]
                    {
                        T(PrepKey + ".details.speedRealtime", new { speed = Prep.FormatSpeed(progress.Speed) }), Prep.FormatRemaining(Prep.RemainingSeconds(job)),
                    }.Where(x => !string.IsNullOrEmpty(x)))));
            }
            if (job.StartedAt is { } started)
            {
                var startedAt = Format(started);
                var elapsed = Prep.SecondsSince(started);
                lines.Add(Line(job.State == "running" ? T(PrepKey + ".details.started") : T(PrepKey + ".details.lastTry"),
                    job.State == "running" && elapsed != null ? T(PrepKey + ".details.startedAgo", new { time = startedAt, elapsed = Prep.FormatClock(elapsed.Value) }) : startedAt));
            }
            var worker = Prep.Worker(job);
            lines.Add(Line(T(PrepKey + ".details.worker"), worker != null ? T(PrepKey + ".details.workerValue", new { kind = worker.Label, name = worker.Name }) : T(PrepKey + ".details.notAssignedYet"), muted: true));
            if (!string.IsNullOrEmpty(job.Error)) lines.Add(Line(T(PrepKey + ".details.error"), job.Error, muted: true));
            return lines;
        }

        private async void ToggleFfmpeg(AdminDownloadPreparation job)
        {
            if (_ffmpeg.Remove(job.Id)) { Refresh(); return; }
            _ffmpeg[job.Id] = new(true, [], false);
            Refresh();
            FfmpegState result;
            try { result = new(false, await P.Controller.ReadFfmpegRowsAsync(job.LogSessionId), false); }
            catch (Exception) { result = new(false, [], true); }
            // A late answer for a console that was closed (or a page that was left) is dropped.
            if (P._disposed || !_ffmpeg.ContainsKey(job.Id)) return;
            _ffmpeg[job.Id] = result;
            Refresh();
        }

        private UIElement Console(AdminDownloadPreparation job)
        {
            var state = _ffmpeg.GetValueOrDefault(job.Id) ?? new FfmpegState(true, [], false);
            var body = Stack(0);
            if (state.Loading) body.Children.Add(Text(T(PrepKey + ".ffmpeg.loading"), 11, true, true));
            else if (state.Failed || state.Rows.Count == 0) body.Children.Add(Text(state.Failed ? T("configure.native.preparedFiles.backgroundFailed") : T(PrepKey + ".ffmpeg.empty"), 11, true, true));
            else
                foreach (var row in state.Rows)
                {
                    var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 8, 0, 8) };
                    grid.ColumnDefinitions.Add(new() { Width = new(120) });
                    grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                    var meta = Stack(2);
                    meta.Children.Add(Text(DateTimeOffset.TryParse(row.Timestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? Format(at) : row.Timestamp, 10, true, true));
                    meta.Children.Add(Text(row.Message.Contains("stderr", StringComparison.Ordinal) ? "stderr" : T(PrepKey + ".ffmpeg.event"), 10, true));
                    grid.Children.Add(meta);
                    var text = Stack(2);
                    text.Children.Add(Text(AdminActivityPresentation.FfmpegRowText(row), 11, false, true));
                    if (!string.IsNullOrEmpty(row.NodeId)) text.Children.Add(Text(row.NodeId, 10, true));
                    Grid.SetColumn(text, 1); grid.Children.Add(text);
                    body.Children.Add(grid);
                }
            var panel = Panel(new ScrollViewer { Content = body, MaxHeight = 256, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 12, 12, true);
            AutomationProperties.SetAutomationId(panel, "PreparedFilesPreparationConsole_" + job.Id);
            return panel;
        }

        private async void ConfirmCancel(IReadOnlyList<AdminDownloadPreparation> targets)
        {
            if (targets.Count == 0 || P.Controller.State.ActionPending || P.Controller.State.DestructiveBlocked) return;
            var prompt = Prep.CancelPrompt(targets);
            var content = Stack(12);
            content.Children.Add(Text(prompt.Description));
            var dialog = P.NewDialog(prompt.Title, content, prompt.ConfirmLabel, destructive: true, cancel: T(PrepKey + ".cancelDialog.keep"));
            AutomationProperties.SetAutomationId(dialog, "PreparedFilesCancelDialog");
            if (await P.ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
            // Exactly the jobs the dialog named are sent, whatever the selection became while it was open.
            var ids = targets.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            _selected.ExceptWith(ids);
            await P.Controller.RunPreparationActionAsync("cancel", targets);
        }
    }

    private void OpenItem(string contentId)
    {
        if (_disposed || Controller.State.Owner is not { IsCurrent: true }) return;
        App.Services.GetService<NavigationService>()?.Navigate<ItemDetailPage>(contentId);
    }
}
