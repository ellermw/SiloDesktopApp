using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
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

    /// <summary>
    /// The Preparation tab (official <c>PreparationTab.tsx</c>): the live queue with state and worker filters, selection and per-job
    /// details. Every control (the state chips, the selection strip, each row's check box and buttons, the details buttons) is built
    /// once and updated in place, so progress readings arriving every second never replace the control being clicked or focused.
    /// </summary>
    private sealed class PreparationView : TabView
    {
        private static readonly double[] Widths = [32, 260, 260, 130, 240, 190, 150];

        private readonly StackPanel _root = Stack(20);
        private readonly LogsWrapPanel _filterRow = new() { Spacing = 8 };
        private readonly TextBox _filter;
        private readonly Button _clearAll;
        private readonly StatusHost _topStatus = new(), _banner = new(), _bottomStatus = new();
        private readonly StateStrip _states;
        private readonly SelectionStrip _selection;
        private readonly TextBlock _showing = Text("", 12, true), _listing = Text("", 12, true);
        private readonly Border _tablePanel;
        private readonly StableTable _table;
        private readonly Tick _selectAll;
        private readonly Dictionary<string, PrepRow> _rows = new(StringComparer.Ordinal);
        private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
        private readonly HashSet<string> _details = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FfmpegState> _ffmpeg = new(StringComparer.Ordinal);
        // The registry never changes while a page is open: it is read once, not once per row.
        private readonly bool _usersRegistered, _logsRegistered;
        private string _searchText = "";
        private string? _stateFilter;
        private string? _workerFilter;
        private IReadOnlyList<AdminDownloadPreparation> _visible = [];

        private sealed record FfmpegState(bool Loading, IReadOnlyList<AdminActivityLogRow> Rows, bool Failed);

        public PreparationView(AdminPreparedFilesPage page) : base(page)
        {
            var registry = AdminShellPage.CreateRegistry();
            _usersRegistered = registry.IsRegistered(AdminRoute.Users);
            _logsRegistered = registry.IsRegistered(AdminRoute.ServerLog);
            _filter = Input(T(PrepKey + ".filter.placeholder"), "PreparedFilesPreparationSearch", 420);
            _filter.Height = 32; _filter.FontSize = 13; _filter.MaxWidth = 420; _filter.HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(_filter, T(PrepKey + ".filter.label"));
            // The starting text is never a change: only a different value updates the filter.
            _filter.TextChanged += (_, _) =>
            {
                if (_filter.Text == _searchText) return;
                _searchText = _filter.Text;
                Refresh();
            };
            _clearAll = Button(T(PrepKey + ".filter.clearAll"), "PreparedFilesPreparationClearFilters", "GhostButtonStyle");
            _clearAll.Click += (_, _) => { _stateFilter = null; _workerFilter = null; Refresh(); };
            _filterRow.Children.Add(_filter);
            _filterRow.Children.Add(_clearAll);

            _states = new StateStrip(this);
            _selection = new SelectionStrip(this);
            _selectAll = new Tick("", "PreparedFilesPreparationSelectAll", on =>
            {
                foreach (var row in _visible) if (on) _selected.Add(row.Id); else _selected.Remove(row.Id);
                Refresh();
            });
            var headings = new List<UIElement?> { _selectAll.Box };
            foreach (var key in new[] { ".column.title", ".column.output", ".column.worker", ".column.progress", ".column.requestedBy" })
            {
                var heading = Text(AppStrings.Uppercase(T(PrepKey + key)), 10, true);
                heading.FontWeight = FontWeights.SemiBold;
                headings.Add(heading);
            }
            _table = new StableTable(headings, Widths);
            _tablePanel = Panel(_table, 12, 0, true);
            AutomationProperties.SetAutomationId(_tablePanel, "PreparedFilesPreparationTable");
            _tablePanel.Visibility = Visibility.Collapsed;
            foreach (UIElement child in new UIElement[] { _topStatus, _banner, _states.Root, _filterRow, _selection.Root, _showing, _listing, _bottomStatus, _tablePanel })
                _root.Children.Add(child);
            _states.Root.Visibility = Visibility.Collapsed; _filterRow.Visibility = Visibility.Collapsed; _selection.Root.Visibility = Visibility.Collapsed;
            _showing.Visibility = Visibility.Collapsed; _listing.Visibility = Visibility.Collapsed;
        }

        public override UIElement Root => _root;

        public override void Reset()
        {
            base.Reset();
            _selected.Clear(); _details.Clear(); _ffmpeg.Clear(); _rows.Clear();
            _stateFilter = null; _workerFilter = null; _searchText = "";
            if (_filter.Text.Length > 0) _filter.Text = "";
            _table.Body.Children.Clear();
        }

        protected override object Inputs(AdminPreparedFilesSnapshot state) =>
            (state.PreparationsLoaded, state.PreparationsError, state.Preparations, state.PreparationCounts, state.ActionPending, state.DestructiveBlocked, state.PendingPreparationIds);

        public override void Update(AdminPreparedFilesSnapshot state) => Build(state);

        /// <summary>A change made on this tab itself (filter, selection, details) draws from the current state.</summary>
        private void Refresh() => Build(P.Controller.State);

        private void Build(AdminPreparedFilesSnapshot state)
        {
            var items = state.Preparations;
            var busy = state.ActionPending;
            if (!state.PreparationsLoaded && state.PreparationsError == null)
            {
                ShowOnly(_topStatus, "loading", () => Message(T(PrepKey + ".loading")));
                return;
            }
            if (!state.PreparationsLoaded || (items.Count == 0 && state.PreparationsError != null))
            {
                ShowOnly(_topStatus, "unavailable", () => Message(T(PrepKey + ".unavailable"), "PreparedFilesPreparationReload", () => _ = P.Controller.ReloadPreparationsAsync()));
                return;
            }
            _topStatus.Hide();
            _filterRow.Visibility = Visibility.Visible;
            if (state.PreparationsError != null) _banner.Show("stale", () => StaleBanner("PreparedFilesPreparationReload", () => _ = P.Controller.ReloadPreparationsAsync()));
            else _banner.Hide();

            var counts = state.PreparationCounts;
            var total = Prep.States.Sum(s => Prep.StateCount(counts, s));
            var workers = items.Where(x => x.State == "running").Select(x => (Job: x, Worker: Prep.Worker(x))).Where(x => x.Worker != null)
                .GroupBy(x => x.Worker!.Key).Select(g => (Worker: g.First().Worker!, Count: g.Count())).OrderByDescending(x => x.Count).ToArray();
            var q = _searchText.Trim().ToLower(System.Globalization.CultureInfo.CurrentCulture);
            var filtered = items.Where(item => (_stateFilter == null || item.State == _stateFilter)
                && (_workerFilter == null || Prep.Worker(item)?.Key == _workerFilter)
                && (q.Length == 0 || Prep.SearchText(item).Contains(q, StringComparison.Ordinal))).ToArray();
            _visible = filtered;
            var known = items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            _selected.RemoveWhere(id => !known.Contains(id));
            foreach (var id in _rows.Keys.Where(id => !known.Contains(id)).ToArray()) { _rows.Remove(id); _details.Remove(id); _ffmpeg.Remove(id); }
            var selectedItems = filtered.Where(x => _selected.Contains(x.Id)).ToArray();
            var activeFilters = (_stateFilter != null ? 1 : 0) + (_workerFilter != null ? 1 : 0);

            _states.Sync(counts, total, workers);
            _clearAll.Visibility = _searchText.Length > 0 || activeFilters > 0 ? Visibility.Visible : Visibility.Collapsed;
            _selection.Sync(selectedItems, busy, state.DestructiveBlocked);

            var showing = !string.IsNullOrEmpty(q) || activeFilters > 0;
            _showing.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
            if (showing) _showing.Text = T(PrepKey + ".showing", new { total = items.Count, shown = filtered.Length, total_text = items.Count });
            var truncated = items.Count < total;
            _listing.Visibility = truncated ? Visibility.Visible : Visibility.Collapsed;
            if (truncated)
                _listing.Text = T(PrepKey + ".listingFirst", new
                {
                    total, shown = items.Count.ToString("N0", AppStrings.FormatCulture), total_text = total.ToString("N0", AppStrings.FormatCulture),
                });

            if (filtered.Length == 0)
            {
                var kind = items.Count > 0 ? "noMatches" : "empty";
                _bottomStatus.Show(kind, () => Message(items.Count > 0 ? T(PrepKey + ".noMatches") : T(PrepKey + ".empty")));
                SyncChildren(_table.Body, []); // no row stays in the tree while the message shows
                _tablePanel.Visibility = Visibility.Collapsed;
                return;
            }
            _bottomStatus.Hide();
            _tablePanel.Visibility = Visibility.Visible;
            _selectAll.Sync(filtered.All(x => _selected.Contains(x.Id)), !busy,
                filtered.All(x => _selected.Contains(x.Id)) ? T(PrepKey + ".table.clearSelection") : T(PrepKey + ".table.selectAll"));

            var wanted = new List<UIElement>(filtered.Length);
            foreach (var job in filtered)
            {
                if (!_rows.TryGetValue(job.Id, out var row)) _rows[job.Id] = row = new PrepRow(this, job.Id);
                row.Apply(job, state, busy, _selected.Contains(job.Id), _details.Contains(job.Id));
                wanted.Add(row.Root);
            }
            SyncChildren(_table.Body, wanted);
        }

        /// <summary>Shows one message and hides everything the loaded queue shows.</summary>
        private void ShowOnly(StatusHost host, string kind, Func<UIElement> build)
        {
            host.Show(kind, build);
            _banner.Hide(); _bottomStatus.Hide();
            _filterRow.Visibility = Visibility.Collapsed; _states.Root.Visibility = Visibility.Collapsed; _selection.Root.Visibility = Visibility.Collapsed;
            _showing.Visibility = Visibility.Collapsed; _listing.Visibility = Visibility.Collapsed; _tablePanel.Visibility = Visibility.Collapsed;
            SyncChildren(_table.Body, []);
        }

        // ---- State and worker filters -----------------------------------------------------------------

        /// <summary>The queue state bar, its state chips and the worker chips: built once, updated in place.</summary>
        private sealed class StateStrip
        {
            private readonly PreparationView _view;
            private readonly Grid _bar = new() { Height = 6, ColumnSpacing = 0 };
            private readonly Dictionary<string, (Chip Chip, TextBlock Count)> _chips = new(StringComparer.Ordinal);
            private readonly Border _workerSection;
            private readonly LogsWrapPanel _workerRow = new() { Spacing = 6 };
            private readonly Dictionary<string, (Chip Chip, TextBlock Name, TextBlock Count)> _workers = new(StringComparer.Ordinal);
            private string _barSignature = "";

            public StateStrip(PreparationView view)
            {
                _view = view;
                var root = Stack(12);
                root.Children.Add(Text(AppStrings.Uppercase(T(PrepKey + ".stateHeading")), 10, true));
                AutomationProperties.SetName(_bar, T(PrepKey + ".stateBarLabel"));
                root.Children.Add(new Border { Child = _bar, CornerRadius = new CornerRadius(3) });
                var chips = new LogsWrapPanel { Spacing = 16 };
                foreach (var state in Prep.States)
                {
                    var toggle = new ToggleButton { Padding = new Thickness(4, 2, 4, 2), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
                    var label = Inline(6);
                    label.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = StateBrush(state), VerticalAlignment = VerticalAlignment.Center });
                    label.Children.Add(Text(Prep.StateLabel(state), 12));
                    var count = Text("", 12, true);
                    label.Children.Add(count);
                    toggle.Content = label;
                    AutomationProperties.SetAutomationId(toggle, "PreparedFilesPreparationState_" + state);
                    AutomationProperties.SetName(toggle, Prep.StateLabel(state));
                    var captured = state;
                    _chips[state] = (new Chip(toggle, on =>
                    {
                        if (on) _view._stateFilter = captured; else if (_view._stateFilter == captured) _view._stateFilter = null;
                        _view.Refresh();
                    }), count);
                    chips.Children.Add(toggle);
                }
                root.Children.Add(chips);
                var inner = Stack(8);
                inner.Children.Add(Text(AppStrings.Uppercase(T(PrepKey + ".byWorker")), 10, true));
                inner.Children.Add(_workerRow);
                _workerSection = new Border { BorderBrush = Tint("BorderBrush", 0.6), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Child = inner, Visibility = Visibility.Collapsed };
                root.Children.Add(_workerSection);
                Root = Panel(root, 16, 16, true);
            }

            public Border Root { get; }

            public void Sync(AdminDownloadPreparationCounts counts, int total, IReadOnlyList<(AdminPreparationWorker Worker, int Count)> workers)
            {
                Root.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
                var signature = string.Join(",", Prep.States.Select(s => Prep.StateCount(counts, s)));
                if (signature != _barSignature)
                {
                    _barSignature = signature;
                    _bar.Children.Clear(); _bar.ColumnDefinitions.Clear();
                    var index = 0;
                    foreach (var state in Prep.States.Where(s => Prep.StateCount(counts, s) > 0))
                    {
                        _bar.ColumnDefinitions.Add(new() { Width = new(Prep.StateCount(counts, state), GridUnitType.Star) });
                        var piece = new Border { Background = StateBrush(state) };
                        Grid.SetColumn(piece, index++); _bar.Children.Add(piece);
                    }
                }
                foreach (var state in Prep.States)
                {
                    var (chip, count) = _chips[state];
                    var text = Prep.StateCount(counts, state).ToString("N0", AppStrings.FormatCulture);
                    if (count.Text != text) count.Text = text;
                    chip.Sync(_view._stateFilter == state, _view._stateFilter != null && _view._stateFilter != state ? 0.35 : 1);
                }
                _workerSection.Visibility = workers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                var wanted = new List<UIElement>(workers.Count);
                foreach (var (worker, count) in workers)
                {
                    if (!_workers.TryGetValue(worker.Key, out var entry))
                    {
                        var toggle = new ToggleButton { Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(6) };
                        var content = Inline(6);
                        content.Children.Add(Text(worker.Label, 11, true));
                        var name = Text(worker.Name, 11);
                        content.Children.Add(name);
                        var amount = Text("", 11, true);
                        content.Children.Add(amount);
                        toggle.Content = content;
                        AutomationProperties.SetAutomationId(toggle, "PreparedFilesPreparationWorker_" + worker.Key);
                        AutomationProperties.SetName(toggle, worker.Label + " " + worker.Name);
                        var key = worker.Key;
                        entry = (new Chip(toggle, on =>
                        {
                            if (on) _view._workerFilter = key; else if (_view._workerFilter == key) _view._workerFilter = null;
                            _view.Refresh();
                        }), name, amount);
                        _workers[worker.Key] = entry;
                    }
                    if (entry.Name.Text != worker.Name) entry.Name.Text = worker.Name;
                    var amountText = count.ToString(AppStrings.FormatCulture);
                    if (entry.Count.Text != amountText) entry.Count.Text = amountText;
                    entry.Chip.Sync(_view._workerFilter == worker.Key, _view._workerFilter != null && _view._workerFilter != worker.Key ? 0.35 : 1);
                    wanted.Add(entry.Chip.Toggle);
                }
                SyncChildren(_workerRow, wanted);
            }
        }

        private static Microsoft.UI.Xaml.Media.Brush StateBrush(string state) => state switch
        {
            "running" => Brush("AccentBrush"),
            "queued" => Tint("SecondaryTextBrush", 0.5),
            "retrying" => Brush("WarningBrush"),
            "paused" => Tint("SecondaryTextBrush", 0.25),
            _ => Brush("ErrorBrush"),
        };

        // ---- Selection strip --------------------------------------------------------------------------

        /// <summary>The bulk-action strip. Its buttons are built once; what they act on is read when they are clicked.</summary>
        private sealed class SelectionStrip
        {
            private readonly TextBlock _count = Text("", 12);
            private readonly Button _pause, _resume, _cancel, _clear;
            private IReadOnlyList<AdminDownloadPreparation> _selected = [], _pausable = [], _resumable = [];

            public SelectionStrip(PreparationView view)
            {
                var row = new LogsWrapPanel { Spacing = 8 };
                row.Children.Add(_count);
                _pause = Button("", "PreparedFilesPauseSelected");
                _pause.Click += (_, _) => _ = view.P.Controller.RunPreparationActionAsync("pause", _pausable);
                _resume = Button("", "PreparedFilesResumeSelected");
                _resume.Click += (_, _) => _ = view.P.Controller.RunPreparationActionAsync("resume", _resumable);
                _cancel = Button("", "PreparedFilesCancelSelected", "DestructiveButtonStyle");
                _cancel.Click += (_, _) => view.ConfirmCancel(_selected.ToArray());
                _clear = Button(T(PrepKey + ".selection.clear"), "PreparedFilesPreparationClearSelection", "GhostButtonStyle");
                _clear.Click += (_, _) => { view._selected.Clear(); view.Refresh(); };
                row.Children.Add(_pause); row.Children.Add(_resume); row.Children.Add(_cancel); row.Children.Add(_clear);
                Root = Panel(row, 10, 10, true);
                AutomationProperties.SetAutomationId(Root, "PreparedFilesPreparationSelection");
                AutomationProperties.SetName(Root, T(PrepKey + ".selection.toolbarLabel"));
            }

            public Border Root { get; }

            public void Sync(IReadOnlyList<AdminDownloadPreparation> selected, bool busy, bool blocked)
            {
                Root.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (selected.Count == 0) { _selected = _pausable = _resumable = []; return; }
                _selected = selected;
                _pausable = selected.Where(Prep.CanPause).ToArray();
                _resumable = selected.Where(Prep.CanResume).ToArray();
                SetText(_count, T(PrepKey + ".selection.count", Prep.JobCount(selected.Count)));
                SetLabel(_pause, _pausable.Count > 0 ? T(PrepKey + ".selection.pauseCount", Prep.JobCount(_pausable.Count)) : T(PrepKey + ".selection.pause"));
                SetLabel(_resume, _resumable.Count > 0 ? T(PrepKey + ".selection.resumeCount", Prep.JobCount(_resumable.Count)) : T(PrepKey + ".selection.resume"));
                SetLabel(_cancel, T(PrepKey + ".selection.cancelCount", Prep.JobCount(selected.Count)));
                _pause.IsEnabled = !busy && _pausable.Count > 0;
                _resume.IsEnabled = !busy && _resumable.Count > 0;
                _cancel.IsEnabled = !busy && !blocked;
                _clear.IsEnabled = !busy;
            }

            private static void SetText(TextBlock block, string text) { if (block.Text != text) block.Text = text; }

            private static void SetLabel(Button button, string label)
            {
                if (button.Content as string == label) return;
                button.Content = label;
                AutomationProperties.SetName(button, label);
            }
        }

        // ---- Rows -------------------------------------------------------------------------------------

        private sealed record TitleCell(string ContentId, string Title, string Subtitle, string Source);
        private sealed record OutputCell(string Format, string Text);
        private sealed record RequesterCell(string UserId, string Name, string Profile, string Device, int Rest);
        private sealed record ProgressCell(string Mode, string Head, string Sub, string Right, double? Percent, string BarName, string Tooltip);

        /// <summary>One queue row. Its check box and action buttons are built once; its cells are rebuilt only when what they show changed.</summary>
        private sealed class PrepRow
        {
            private readonly PreparationView _view;
            private readonly string _id;
            private readonly CellRow _cells = new(Widths);
            private readonly Tick _select;
            private readonly Button _pause, _resume, _cancel, _detailsButton;
            private readonly ProgressRing _ring = new() { IsActive = true, Width = 16, Height = 16, Visibility = Visibility.Collapsed };
            private readonly FontIcon _chevron = new() { FontSize = 10 };
            private string _namesKey = "";
            private bool? _open;
            private DetailsPanel? _panel;

            public PrepRow(PreparationView view, string id)
            {
                _view = view; _id = id;
                AutomationProperties.SetAutomationId(Root, "PreparedFilesPreparation_" + id);
                _select = new Tick("", "PreparedFilesPreparationSelect_" + id, on =>
                {
                    if (on) view._selected.Add(id); else view._selected.Remove(id);
                    view.Refresh();
                });
                _cells.Hold(0, _select.Box);
                _pause = IconAction("", "PreparedFilesPause_" + id, () => _ = view.P.Controller.RunPreparationActionAsync("pause", [Job]));
                _resume = IconAction("", "PreparedFilesResume_" + id, () => _ = view.P.Controller.RunPreparationActionAsync("resume", [Job]));
                _cancel = IconAction("", "PreparedFilesCancel_" + id, () => view.ConfirmCancel([Job]));
                _detailsButton = new Button { Style = (Style)Application.Current.Resources["GhostButtonStyle"], Padding = new Thickness(6, 2, 6, 2), MinHeight = 28 };
                var detailContent = Inline(4);
                detailContent.Children.Add(Text(T(PrepKey + ".row.details"), 11, true));
                detailContent.Children.Add(_chevron);
                _detailsButton.Content = detailContent;
                AutomationProperties.SetAutomationId(_detailsButton, "PreparedFilesPreparationDetails_" + id);
                _detailsButton.Click += (_, _) =>
                {
                    if (!view._details.Add(id)) { view._details.Remove(id); view._ffmpeg.Remove(id); }
                    view.Refresh();
                };
                var actions = Inline(4);
                actions.HorizontalAlignment = HorizontalAlignment.Right;
                foreach (UIElement child in new UIElement[] { _ring, _pause, _resume, _cancel, _detailsButton }) actions.Children.Add(child);
                _cells.Hold(6, actions);
            }

            public AdminDownloadPreparation Job { get; private set; } = null!;
            public Border Root => _cells.Rule;

            public void Apply(AdminDownloadPreparation job, AdminPreparedFilesSnapshot state, bool busy, bool selected, bool open)
            {
                Job = job;
                var title = Prep.Title(job);
                _select.Sync(selected, !busy, T(PrepKey + ".row.select", new { title }));
                _cells.Set(1, new TitleCell(job.ContentId, title, Prep.Subtitle(job), Prep.FormatSource(job)), () => BuildTitle(job, title));
                _cells.Set(2, new OutputCell(job.Format, Prep.FormatOutput(job)), () => BuildOutput(job));
                _cells.Set(3, Prep.Worker(job), () => BuildWorker(Prep.Worker(job)));
                var progress = DescribeProgress(job);
                _cells.Set(4, progress, () => BuildProgress(progress));
                var first = job.Requesters.Count > 0 ? job.Requesters[0] : null;
                _cells.Set(5, first == null ? null : new RequesterCell(first.UserId, Prep.RequesterName(first), first.ProfileName, Prep.RequesterDevice(first), job.Requesters.Count - 1), () => BuildRequesters(job));

                var pending = state.PendingPreparationIds.Contains(job.Id);
                var key = title + "\n" + job.State;
                if (key != _namesKey)
                {
                    _namesKey = key;
                    Describe(_pause, T(PrepKey + ".row.pauseLabel", new { title }), job.State == "running" ? T(PrepKey + ".row.pauseRunningTitle") : T(PrepKey + ".row.pauseWaitingTitle"));
                    Describe(_resume, T(PrepKey + ".row.resumeLabel", new { title }), T(PrepKey + ".row.resumeTitle"));
                    var failed = job.State == "failed";
                    Describe(_cancel, failed ? T(PrepKey + ".row.removeLabel", new { title }) : T(PrepKey + ".row.cancelLabel", new { title }), failed ? T(PrepKey + ".row.removeTitle") : T(PrepKey + ".row.cancelTitle"));
                    AutomationProperties.SetName(_ring, T(PrepKey + ".row.working"));
                    AutomationProperties.SetName(_detailsButton, T(PrepKey + ".row.detailsLabel", new { title }));
                }
                _ring.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
                _pause.Visibility = !pending && Prep.CanPause(job) ? Visibility.Visible : Visibility.Collapsed;
                _resume.Visibility = !pending && Prep.CanResume(job) ? Visibility.Visible : Visibility.Collapsed;
                _cancel.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
                _pause.IsEnabled = !busy; _resume.IsEnabled = !busy;
                _cancel.IsEnabled = !busy && !state.DestructiveBlocked;
                if (_open != open)
                {
                    _open = open;
                    _chevron.Glyph = open ? "" : "";
                    AutomationProperties.SetItemStatus(_detailsButton, open ? "expanded" : "collapsed");
                }
                _cells.Grid.Background = selected ? Tint("AccentBrush", 0.05) : null;

                if (open)
                {
                    _panel ??= new DetailsPanel(_view, id: _id);
                    _panel.Sync(job, _view._ffmpeg.GetValueOrDefault(job.Id));
                    _cells.Below = _panel.Root;
                }
                else if (_panel != null) { _panel = null; _cells.Below = null; }
            }

            private static void Describe(Button button, string name, string tip)
            {
                AutomationProperties.SetName(button, name);
                ToolTipService.SetToolTip(button, tip);
            }

            private UIElement BuildTitle(AdminDownloadPreparation job, string title)
            {
                var block = Stack(2);
                if (!string.IsNullOrEmpty(job.ContentId))
                {
                    var link = new HyperlinkButton { Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left };
                    var inner = Stack(2);
                    var heading = Strong(title, 13); heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.TextWrapping = TextWrapping.NoWrap;
                    inner.Children.Add(heading);
                    inner.Children.Add(Small(Prep.Subtitle(job)));
                    inner.Children.Add(Small(Prep.FormatSource(job)));
                    link.Content = inner;
                    AutomationProperties.SetAutomationId(link, "PreparedFilesPreparationItem_" + job.Id);
                    AutomationProperties.SetName(link, title);
                    var contentId = job.ContentId;
                    link.Click += (_, _) => _view.P.OpenItem(contentId);
                    block.Children.Add(link);
                }
                else
                {
                    var heading = Strong(title, 13); heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.TextWrapping = TextWrapping.NoWrap;
                    block.Children.Add(heading);
                    block.Children.Add(Small(Prep.Subtitle(job)));
                    block.Children.Add(Small(Prep.FormatSource(job)));
                }
                return block;
            }

            private static UIElement BuildOutput(AdminDownloadPreparation job)
            {
                var output = Inline(8);
                output.Children.Add(FormatBadge(job.Format));
                var outputText = Text(Prep.FormatOutput(job), 11); outputText.FontWeight = FontWeights.Medium; outputText.MaxWidth = 220;
                output.Children.Add(outputText);
                return output;
            }

            private static UIElement BuildWorker(AdminPreparationWorker? worker)
            {
                UIElement cell = worker == null ? Text(T(PrepKey + ".notAssigned"), 11, true) : WorkerBadge(worker);
                ((FrameworkElement)cell).VerticalAlignment = VerticalAlignment.Center;
                return cell;
            }

            private UIElement BuildRequesters(AdminDownloadPreparation job)
            {
                if (job.Requesters.Count == 0) return Text(T(PrepKey + ".requesters.none"), 11, true);
                var first = job.Requesters[0];
                var stack = Stack(2);
                var line = Inline(6);
                var link = new HyperlinkButton { Content = Prep.RequesterName(first), Padding = new Thickness(0), FontSize = 12, IsEnabled = _view._usersRegistered && !string.IsNullOrEmpty(first.UserId) };
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
        }

        private static TextBlock Small(string text)
        {
            var block = Cell(text, true);
            block.FontSize = 10; block.LineHeight = 14;
            return block;
        }

        private static Button IconAction(string glyph, string id, Action click)
        {
            var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 13 }, Style = (Style)Application.Current.Resources["GhostButtonStyle"], Width = 30, Height = 30, MinHeight = 0, Padding = new Thickness(0) };
            AutomationProperties.SetAutomationId(button, id);
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

        // ---- Progress cell ----------------------------------------------------------------------------

        /// <summary>What the progress cell shows for a job, as values: the cell is redrawn only when these change.</summary>
        private static ProgressCell DescribeProgress(AdminDownloadPreparation job)
        {
            switch (job.State)
            {
                case "running":
                {
                    var percent = Prep.Percent(job);
                    if (percent == null)
                    {
                        var elapsed = Prep.SecondsSince(job.StartedAt);
                        return new("running-wait", elapsed != null ? T(PrepKey + ".progress.runningElapsed", new { label = Prep.RunningLabel(job), elapsed = Prep.FormatClock(elapsed.Value) }) : Prep.RunningLabel(job),
                            job.ProgressUnavailable ? T(PrepKey + ".progress.noReports") : T(PrepKey + ".progress.waitingFirstReport"), "", null, "", "");
                    }
                    return new("running", T(PrepKey + ".progress.percent", new { percent = (long)Math.Floor(percent.Value) }), "",
                        string.Join(" · ", new[] { Prep.FormatSpeed(job.Progress?.Speed), Prep.FormatRemaining(Prep.RemainingSeconds(job)) }.Where(x => !string.IsNullOrEmpty(x))),
                        percent.Value, T(PrepKey + ".progress.label", new { title = Prep.Title(job) }), "");
                }
                case "queued":
                    return new("queued", "", job.QueuePosition == 1 ? T(PrepKey + ".progress.queuedNext")
                        : job.QueuePosition is > 0 ? T(PrepKey + ".progress.queuedPosition", new { position = job.QueuePosition.Value.ToString("N0", AppStrings.FormatCulture) })
                        : T(PrepKey + ".progress.queuedWaiting"), "", null, "", "");
                case "retrying":
                    return new("retrying", T(PrepKey + ".progress.waitingToRetry"), string.Join(" · ", new[]
                    {
                        T(PrepKey + ".progress.attempt", new { attempt = job.Attempts + 1, max = job.MaxAttempts, retry_in = Prep.FormatRetryIn(job.NextRetryAt) }), job.Error,
                    }.Where(x => !string.IsNullOrEmpty(x))), "", null, "", "");
                case "paused":
                    return new("paused", T(PrepKey + ".progress.paused"), string.Join(" · ", new[] { Prep.FormatAgo(job.PausedAt), T(PrepKey + ".progress.wontStart") }.Where(x => !string.IsNullOrEmpty(x))), "", null, "", "");
                default:
                    return new("failed", T(PrepKey + ".progress.failed"),
                        string.Join(" · ", new[] { T(PrepKey + ".progress.afterAttempts", new { count = job.Attempts }), Prep.FormatAgo(job.FailedAt), job.Error }.Where(x => !string.IsNullOrEmpty(x))), "", null, "", job.Error ?? "");
            }
        }

        private static UIElement BuildProgress(ProgressCell model)
        {
            var stack = Stack(2);
            switch (model.Mode)
            {
                case "running-wait":
                {
                    var head = Text(model.Head, 11); head.FontWeight = FontWeights.SemiBold; head.Foreground = Brush("AccentBrush");
                    stack.Children.Add(head);
                    stack.Children.Add(Text(model.Sub, 10, true));
                    break;
                }
                case "running":
                {
                    var line = new Grid { ColumnSpacing = 8 };
                    line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                    var pct = Text(model.Head, 11); pct.FontWeight = FontWeights.SemiBold;
                    line.Children.Add(pct);
                    var detail = Text(model.Right, 11, true);
                    detail.TextWrapping = TextWrapping.NoWrap; detail.TextTrimming = TextTrimming.CharacterEllipsis; detail.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetColumn(detail, 1); line.Children.Add(detail);
                    stack.Children.Add(line);
                    var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = model.Percent ?? 0, Height = 6 };
                    AutomationProperties.SetName(bar, model.BarName);
                    stack.Children.Add(bar);
                    break;
                }
                case "queued":
                    stack.Children.Add(Text(model.Sub, 11, true));
                    break;
                case "retrying":
                {
                    var head = Text(model.Head, 11); head.FontWeight = FontWeights.SemiBold; head.Foreground = Brush("WarningBrush");
                    stack.Children.Add(head);
                    stack.Children.Add(Text(model.Sub, 10, true));
                    break;
                }
                case "paused":
                {
                    var head = Text(model.Head, 11, true); head.FontWeight = FontWeights.SemiBold;
                    stack.Children.Add(head);
                    stack.Children.Add(Text(model.Sub, 10, true));
                    break;
                }
                default:
                {
                    var head = Text(model.Head, 11); head.FontWeight = FontWeights.SemiBold; head.Foreground = Brush("ErrorBrush");
                    stack.Children.Add(head);
                    var detail = Text(model.Sub, 10, true);
                    ToolTipService.SetToolTip(detail, model.Tooltip);
                    stack.Children.Add(detail);
                    break;
                }
            }
            return stack;
        }

        // ---- Details panel and FFmpeg console ---------------------------------------------------------

        /// <summary>
        /// One open details panel. The two header buttons are built once; the cards and the console are redrawn when the job or the
        /// console changed, and hold nothing focusable.
        /// </summary>
        private sealed class DetailsPanel
        {
            private readonly PreparationView _view;
            private readonly Border _panel;
            private readonly TextBlock _heading = Text("", 11);
            private readonly Button _ffmpeg, _logs;
            private readonly ContentControl _cardsHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            private readonly ContentControl _consoleHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            private AdminDownloadPreparation? _job;
            private FfmpegState? _console;
            private bool? _consoleShown;
            private string _session = "";

            public DetailsPanel(PreparationView view, string id)
            {
                _view = view;
                var root = Stack(12);
                var header = new Grid { ColumnSpacing = 12 };
                header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var left = Stack(4);
                var chip = Inline(8);
                chip.Children.Add(Pill(AppStrings.Uppercase(T(PrepKey + ".details.chip")), "muted"));
                chip.Children.Add(_heading);
                left.Children.Add(chip);
                left.Children.Add(Text(id, 10, true, true));
                header.Children.Add(left);
                var buttons = Inline(6);
                _ffmpeg = Button("FFmpeg", "PreparedFilesPreparationFfmpeg_" + id);
                _ffmpeg.Click += (_, _) => { if (_job != null) view.ToggleFfmpeg(_job); };
                buttons.Children.Add(_ffmpeg);
                _logs = Button(T(PrepKey + ".details.viewLogs"), "PreparedFilesPreparationLogs_" + id);
                _logs.Click += (_, _) => AdminShellPage.Open(new AdminRouteRequest(AdminRoute.ServerLog,
                    new Dictionary<string, string> { ["playback_session_id"] = _session, ["component"] = "ffmpeg" }));
                buttons.Children.Add(_logs);
                Grid.SetColumn(buttons, 1); header.Children.Add(buttons);
                root.Children.Add(header);
                root.Children.Add(_cardsHost);
                root.Children.Add(_consoleHost);
                _panel = Panel(root, 0, 16, false);
                _panel.Background = Tint("MutedBrush", 0.25);
                AutomationProperties.SetAutomationId(_panel, "PreparedFilesPreparationDetailsPanel_" + id);
            }

            public UIElement Root => _panel;

            public void Sync(AdminDownloadPreparation job, FfmpegState? console)
            {
                var showFfmpeg = console != null;
                if (_consoleShown != showFfmpeg)
                {
                    _consoleShown = showFfmpeg;
                    _heading.Text = showFfmpeg ? T(PrepKey + ".details.headingWithFfmpeg") : T(PrepKey + ".details.heading");
                    _ffmpeg.Content = showFfmpeg ? T(PrepKey + ".details.hideFfmpeg") : "FFmpeg";
                    AutomationProperties.SetItemStatus(_ffmpeg, showFfmpeg ? "pressed" : "");
                }
                if (_session != job.LogSessionId)
                {
                    _session = job.LogSessionId;
                    var registered = _view._logsRegistered && !string.IsNullOrEmpty(_session);
                    _logs.IsEnabled = registered;
                    ToolTipService.SetToolTip(_logs, registered ? null : T("configure.native.preparedFiles.linkUnavailable"));
                }
                if (!ReferenceEquals(_job, job)) { _job = job; _cardsHost.Content = Cards(job); }
                if (!ReferenceEquals(_console, console))
                {
                    _console = console;
                    _consoleHost.Content = console == null ? null : Console(job, console);
                }
            }
        }

        private static UIElement Cards(AdminDownloadPreparation job)
        {
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
            return cards;
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

        private static UIElement Console(AdminDownloadPreparation job, FfmpegState state)
        {
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

        /// <summary>The confirmation names exactly <paramref name="targets"/>; what it sends is those jobs, under the authority it opened under.</summary>
        private async void ConfirmCancel(IReadOnlyList<AdminDownloadPreparation> targets)
        {
            if (targets.Count == 0 || P.Controller.State.ActionPending || P.Controller.State.DestructiveBlocked) return;
            MonitorAuthorityLease lease;
            try { lease = P.Controller.CaptureAction(); }
            catch (OperationCanceledException) { return; }
            using (lease)
            {
                var prompt = Prep.CancelPrompt(targets);
                var content = Stack(12);
                content.Children.Add(Text(prompt.Description));
                var dialog = P.NewDialog(prompt.Title, content, prompt.ConfirmLabel, destructive: true, cancel: T(PrepKey + ".cancelDialog.keep"));
                AutomationProperties.SetAutomationId(dialog, "PreparedFilesCancelDialog");
                if (await P.ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
                // Exactly the jobs the dialog named are sent, whatever the selection became while it was open, and only while the
                // authority they were listed under is still current: otherwise nothing is sent and the dialog is already closed.
                if (!P.Controller.IsActionCurrent(lease)) return;
                var ids = targets.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                _selected.ExceptWith(ids);
                await P.Controller.RunPreparationActionAsync("cancel", targets, lease);
            }
        }
    }

    private void OpenItem(string contentId)
    {
        if (_disposed || Controller.State.Owner is not { IsCurrent: true }) return;
        App.Services.GetService<NavigationService>()?.Navigate<ItemDetailPage>(contentId);
    }
}
