using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;
using P = SiloPlayer.Core.Services.Admin.AdminLibraryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The diagnostics sections under the list (WebUI <c>UnmatchedItemsSection</c>,
/// <c>MetadataMatcherQueuesSection</c>, <c>AmbiguousRootsSection</c>, <c>SkippedRootsSection</c>,
/// <c>StaleIDsSection</c>): each keeps its own loading, empty, filtered-empty, error,
/// partial and expanded states; search boxes stay mounted while results reload.
/// </summary>
public sealed partial class AdminLibrariesPage
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    private AdminUnmatchedPanel? _unmatched;
    private AdminMatcherQueuesPanel? _matcher;
    private AdminAppendList<AdminLibraryRoot>? _ambiguous;
    private AdminAppendList<AdminSkippedRoot>? _skipped;
    private AdminAppendList<AdminStaleMediaId>? _stale;
    private UnmatchedView? _unmatchedView;
    private MatcherView? _matcherView;
    private AmbiguousView? _ambiguousView;
    private SkippedView? _skippedView;
    private StaleView? _staleView;

    private void OpenDiagnostics(AdminLibrariesController controller)
    {
        CloseDiagnostics();
        try
        {
            _unmatched = new AdminUnmatchedPanel(controller);
            _matcher = new AdminMatcherQueuesPanel(controller);
            CreateLazyHealthPanels(controller);
        }
        catch (OperationCanceledException) { CloseDiagnostics(); return; }
        _unmatchedView = new UnmatchedView(this, _unmatched);
        _matcherView = new MatcherView(this, _matcher);
        CreateLazyHealthViews();
        // Each section sits in the Catalog Health card (`surface-panel rounded-2xl border-0 p-4 sm:p-5`) that its tile selects.
        _healthCards.Clear();
        _healthCards[AdminHealthSection.Unmatched] = HealthCard(_unmatchedView.Section);
        _healthCards[AdminHealthSection.Matcher] = HealthCard(_matcherView.Section);
        AddLazyHealthCards();
        // The tab badge and the tiles follow every panel, wherever the admin is.
        _unmatched.Changed += OnHealthChanged; _matcher.Changed += OnHealthChanged;
        SubscribeLazyHealthPanels();
        // The unmatched and matcher totals feed the Catalog Health badge from the first moment (official: the page reads both on open).
        _ = _unmatched.LoadTotalsAsync();
        _ = _matcher.LoadSummaryAsync();
    }

    // The Ambiguous, Skipped and Stale sections read lazily when their tile is chosen and live inside the Catalog Health panel, which unmounts when its
    // tab is left (the unmatched and matcher totals are the page's own and stay).
    private void CreateLazyHealthPanels(AdminLibrariesController controller)
    {
        _ambiguous = new AdminAppendList<AdminLibraryRoot>(controller, (lease, scope, search, cursor, ct) =>
            scope == null ? Task.FromResult(new AdminTotalCursorPage<AdminLibraryRoot>([], null, 0)) : controller.Api.ListRootsAsync(lease, scope, "ambiguous", search, cursor, ct), root => root.LibraryId + "\n" + root.RootPath);
        _skipped = new AdminAppendList<AdminSkippedRoot>(controller, (lease, _, search, cursor, ct) => controller.Api.ListSkippedRootsAsync(lease, search, cursor, ct), root => root.LibraryId + "\n" + root.RootPath);
        _stale = new AdminAppendList<AdminStaleMediaId>(controller, (lease, _, search, cursor, ct) => controller.Api.ListStaleIdsAsync(lease, search, cursor, ct), item => item.ContentId + "\n" + item.Provider);
    }

    private void CreateLazyHealthViews()
    {
        _ambiguousView = new AmbiguousView(this, _ambiguous!);
        _skippedView = new SkippedView(this, _skipped!);
        _staleView = new StaleView(this, _stale!);
    }

    private void AddLazyHealthCards()
    {
        _healthCards[AdminHealthSection.Ambiguous] = HealthCard(_ambiguousView!.Section);
        _healthCards[AdminHealthSection.Skipped] = HealthCard(_skippedView!.Section);
        _healthCards[AdminHealthSection.Stale] = HealthCard(_staleView!.Section);
    }

    private void SubscribeLazyHealthPanels() { _ambiguous!.Changed += OnHealthChanged; _skipped!.Changed += OnHealthChanged; _stale!.Changed += OnHealthChanged; }

    /// <summary>
    /// Leaving Catalog Health (official: the panel unmounts): it comes back on Unmatched, and the tiles that read lazily say "Inspect" again until
    /// they are chosen. The Unmatched and Metadata Matcher totals belong to the page and stay (so the tab's badge does not blink).
    /// </summary>
    private void ResetHealthPanel()
    {
        var controller = _controller;
        _health = AdminHealthSection.Unmatched;
        _tileStatus.Clear();
        if (controller == null || _ambiguous == null || _skipped == null || _stale == null) return;
        _ambiguousView?.Detach(); _skippedView?.Detach(); _staleView?.Detach();
        _ambiguous.Dispose(); _skipped.Dispose(); _stale.Dispose();
        _ambiguous = null; _skipped = null; _stale = null;
        _ambiguousView = null; _skippedView = null; _staleView = null;
        try { CreateLazyHealthPanels(controller); }
        catch (OperationCanceledException) { _ambiguous = null; _skipped = null; _stale = null; _healthCards.Remove(AdminHealthSection.Ambiguous); _healthCards.Remove(AdminHealthSection.Skipped); _healthCards.Remove(AdminHealthSection.Stale); return; }
        CreateLazyHealthViews();
        AddLazyHealthCards();
        SubscribeLazyHealthPanels();
    }

    private Border HealthCard(DiagnosticsSection section) => Surface(section, Radius2Xl, _phone ? 16 : 20);

    private void CloseDiagnostics()
    {
        _unmatchedView?.Detach(); _matcherView?.Detach(); _ambiguousView?.Detach(); _skippedView?.Detach(); _staleView?.Detach();
        _unmatched?.Dispose(); _matcher?.Dispose(); _ambiguous?.Dispose(); _skipped?.Dispose(); _stale?.Dispose();
        _unmatched = null; _matcher = null; _ambiguous = null; _skipped = null; _stale = null;
        _unmatchedView = null; _matcherView = null; _ambiguousView = null; _skippedView = null; _staleView = null;
        _healthCards.Clear();
        _healthBody.Content = null;
    }

    private void RenderDiagnosticsLayout()
    {
        foreach (var card in _healthCards.Values) card.Padding = new(_phone ? 16 : 20);
        foreach (var section in new[] { _unmatchedView?.Section, _matcherView?.Section, _ambiguousView?.Section, _skippedView?.Section, _staleView?.Section }) section?.SetWide(_wide);
        _unmatchedView?.Render(); _matcherView?.Render(); _ambiguousView?.Render(); _skippedView?.Render(); _staleView?.Render();
    }

    private void OnDiagnosticsStale()
    {
        var matcher = _matcher;
        EnqueueUi(() => { if (matcher != null && ReferenceEquals(matcher, _matcher)) matcher.InvalidateSummary(); });
    }

    private Task RefreshMatchDiagnosticsAsync(LibraryUiOwner owner)
    {
        if (!owner.IsCurrent) return Task.CompletedTask;
        return Task.WhenAll(_unmatched?.RefreshAfterMatchAsync() ?? Task.CompletedTask, _stale?.ReloadAsync() ?? Task.CompletedTask);
    }

    /// <summary>The library list for the sections that name or pick libraries.</summary>
    private IReadOnlyList<AdminLibrary> Libraries => _controller?.State.Libraries ?? [];

    // ── Shared section pieces (DiagnosticsListParts.tsx) ─────────────────

    private abstract class SectionView
    {
        protected readonly AdminLibrariesPage Page;
        protected readonly LibraryUiOwner UiOwner;
        internal DiagnosticsSection Section { get; }
        private readonly Action _changed;
        private readonly AdminLibraryPanel _panel;
        protected bool Detached;

        protected SectionView(AdminLibrariesPage page, AdminLibraryPanel panel, string key, string title, string description)
        {
            Page = page; _panel = panel; UiOwner = page.RenderOwner(page._controller!.State);
            Section = new DiagnosticsSection(key, title, description);
            // Catalog Health (`embedded`): the tile above picks the section, which has no collapsible header of its own. The page draws the
            // `surface-panel rounded-2xl border-0 p-4 sm:p-5` card around it.
            Section.SetEmbedded();
            Section.SetWide(page._wide);
            _changed = () => page.EnqueueUi(() => { if (!Detached) Render(); });
            panel.Changed += _changed;
        }

        internal void Detach() { Detached = true; _panel.Changed -= _changed; }

        internal abstract void Render();

        protected bool Wide => Page._wide;

        /// <summary>Ambiguous Roots, Troubleshooting and Stale External IDs show their table from 1280 px (WebUI <c>useWideTableLayout</c>); cards below.</summary>
        protected bool WideTable => Page._wideTable;

        /// <summary>Card controls are 44 px tall below 1024 px (touch) and the desktop height from there up.</summary>
        protected double CardHeight => Wide ? 32 : 44;

        protected static TextBox SearchBox(string placeholder, string automationId, Action<string> changed)
        {
            var box = new TextBox { PlaceholderText = placeholder, Height = 32, FontSize = 12, Padding = new(32, 6, 12, 6), CornerRadius = new(RadiusMd) };
            AutomationProperties.SetName(box, placeholder);
            AutomationProperties.SetAutomationId(box, automationId);
            var timer = new DispatcherTimer { Interval = SearchDebounce };
            timer.Tick += (_, _) => { timer.Stop(); changed(box.Text); };
            box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
            box.KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Enter) { args.Handled = true; timer.Stop(); changed(box.Text); } };
            return box;
        }

        protected static Grid WithSearchIcon(TextBox box)
        {
            var grid = new Grid();
            grid.Children.Add(box);
            var icon = Icon("search", 14, Brush("SecondaryTextBrush"));
            icon.HorizontalAlignment = HorizontalAlignment.Left; icon.VerticalAlignment = VerticalAlignment.Center; icon.Margin = new(10, 0, 0, 0);
            grid.Children.Add(icon);
            return grid;
        }

        protected void Touch(Control control) { if (!Wide) { control.Height = 44; control.MinHeight = 44; control.FontSize = 14; } else { control.Height = 32; control.MinHeight = 32; } }

        protected static Border TableFrame(UIElement child) => new()
        {
            Child = new ScrollViewer { Content = child, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled },
            CornerRadius = new(RadiusXl), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.4), Background = Tint("AppBackgroundBrush", 0.4),
        };

        /// <summary>A grid table: header row then the rows; a message spans every column when there are none.</summary>
        protected static Grid GridTable(IReadOnlyList<(string Header, GridLength Width, bool End)> columns, IReadOnlyList<IReadOnlyList<UIElement>> rows, string? message,
            bool error = false, Func<int, (UIElement Content, bool Shown)?>? expansion = null, Action<int>? rowClick = null)
        {
            var grid = new Grid();
            foreach (var column in columns) grid.ColumnDefinitions.Add(new() { Width = column.Width });
            var rowIndex = 0;
            void Band(int row, bool clickable, Action? click)
            {
                var band = new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                Grid.SetRow(band, row); Grid.SetColumnSpan(band, columns.Count);
                if (clickable && click != null) band.Tapped += (_, _) => click();
                grid.Children.Add(band);
            }
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Band(0, false, null);
            for (var column = 0; column < columns.Count; column++)
            {
                var head = Text(columns[column].Header, 14); head.FontWeight = FontWeights.Medium; head.TextWrapping = TextWrapping.NoWrap;
                var cell = new Border { Child = head, Padding = new(8, 10, 8, 10), HorizontalAlignment = columns[column].End ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                Grid.SetColumn(cell, column); grid.Children.Add(cell);
            }
            rowIndex++;
            if (message != null)
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Band(rowIndex, false, null);
                var text = Text(message, 14, !error); text.HorizontalAlignment = HorizontalAlignment.Center; text.TextAlignment = TextAlignment.Center;
                if (error) text.Foreground = Solid(Destructive);
                var cell = new Border { Child = text, Padding = new(8, 12, 8, 12) };
                Grid.SetRow(cell, rowIndex); Grid.SetColumnSpan(cell, columns.Count); grid.Children.Add(cell);
                return grid;
            }
            for (var index = 0; index < rows.Count; index++)
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var captured = index;
                Band(rowIndex, rowClick != null, rowClick == null ? null : () => rowClick(captured));
                for (var column = 0; column < rows[index].Count; column++)
                {
                    var cell = new Border { Child = rows[index][column], Padding = new(8), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = columns[column].End ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                    Grid.SetRow(cell, rowIndex); Grid.SetColumn(cell, column); grid.Children.Add(cell);
                }
                rowIndex++;
                if (expansion?.Invoke(index) is { Shown: true } expanded)
                {
                    grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    var holder = new Border { Child = expanded.Content, Padding = new(16, 12, 16, 12), Background = Tint("MutedBrush", 0.3), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
                    Grid.SetRow(holder, rowIndex); Grid.SetColumnSpan(holder, columns.Count); grid.Children.Add(holder);
                    rowIndex++;
                }
            }
            return grid;
        }

        protected static Border CardMessage(string message, bool error = false)
        {
            var text = Text(message, 14, !error); text.TextAlignment = TextAlignment.Center; text.HorizontalAlignment = HorizontalAlignment.Center;
            if (error) text.Foreground = Solid(Destructive);
            return new Border { Child = text, Padding = new(16, 24, 16, 24), CornerRadius = new(RadiusXl), BorderThickness = new(1), BorderBrush = Tint("BorderBrush", 0.4), Background = Tint("AppBackgroundBrush", 0.4) };
        }

        protected static Border Card(UIElement content) => Surface(content, Radius2Xl, 16);

        /// <summary>WebUI <c>CardFields</c>: label/value lines.</summary>
        protected static Grid Fields(params (string Label, UIElement Value)[] fields)
        {
            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var index = 0; index < fields.Length; index++)
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var label = Text(fields[index].Label, 12, true); label.FontWeight = FontWeights.Medium;
                Grid.SetRow(label, index); grid.Children.Add(label);
                var value = fields[index].Value as FrameworkElement;
                if (value != null) { Grid.SetRow(value, index); Grid.SetColumn(value, 1); grid.Children.Add(value); }
            }
            return grid;
        }

        /// <summary>WebUI <c>PaginationBar</c>: range text and First/Previous/Next/Last icon buttons; hidden at one page.</summary>
        protected Grid? Pager(string key, long total, long start, long end, bool canPrevious, bool canNext, Action first, Action previous, Action next, Action last, int pageSize = 10)
        {
            if (total <= pageSize) return null;
            var grid = new Grid { ColumnSpacing = 8, Padding = new(4, 12, 4, 0), BorderBrush = Tint("BorderBrush", 0.4), BorderThickness = new(0, 1, 0, 0) };
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var range = Text(T("web.admin.libraries.pagination.range", ("start", start), ("end", end), ("total", total)), 12, true);
            AutomationProperties.SetAutomationId(range, key + "Range");
            grid.Children.Add(range);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var size = Wide ? 28 : 44;
            void Add(string icon, string label, string id, bool enabled, Action click)
            {
                var button = IconButton(icon, T(label), key + id, size, 14);
                button.IsEnabled = enabled; button.Click += (_, _) => click();
                buttons.Children.Add(button);
            }
            Add("chevrons-left", "web.admin.libraries.pagination.first", "First", canPrevious, first);
            Add("chevron-left", "web.admin.libraries.pagination.previous", "Previous", canPrevious, previous);
            Add("chevron-right", "web.admin.libraries.pagination.next", "Next", canNext, next);
            Add("chevrons-right", "web.admin.libraries.pagination.last", "Last", canNext, last);
            Grid.SetColumn(buttons, 1); grid.Children.Add(buttons);
            return grid;
        }

        protected Button LoadMore(string automationId, bool loading, Action click)
        {
            var button = Action(T(loading ? "web.admin.libraries.loadingMore" : "web.admin.libraries.loadMore"), automationId);
            Touch(button);
            button.IsEnabled = !loading;
            button.Click += (_, _) => click();
            return button;
        }

        protected Button RetryButton(string automationId, Action click)
        {
            var button = Action(Core.Localization.AppStrings.Get("common.retry", fallback: "Retry"), automationId);
            Touch(button);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => click();
            return button;
        }

        protected static void KeepFocus(FrameworkElement host, Action rebuild)
        {
            var focused = Focused(host.XamlRoot);
            var id = focused != null && IsDescendant(focused, host) ? AutomationProperties.GetAutomationId(focused) : "";
            rebuild();
            if (id.Length > 0) host.DispatcherQueue.TryEnqueue(() => { try { FindControl(host, id)?.Focus(FocusState.Programmatic); } catch (Exception) { /* focus restore is best effort */ } });
        }
    }

    // ── Unmatched items ──────────────────────────────────────────────────

    private sealed class UnmatchedView : SectionView
    {
        private readonly AdminUnmatchedPanel _panel;
        private readonly StackPanel _totals = new() { Spacing = 8 };
        private readonly StackPanel _body = new() { Spacing = 8 };
        private readonly ComboBox _view;
        private readonly TextBox _search;
        private readonly ContentControl _rows = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly StackPanel _footer = new() { Spacing = 8 };
        private readonly Grid _controls = new() { ColumnSpacing = 8, RowSpacing = 8 };
        private readonly Grid _searchHost;
        private bool _syncing;

        internal UnmatchedView(AdminLibrariesPage page, AdminUnmatchedPanel panel)
            : base(page, panel, "Unmatched", T("web.admin.libraries.unmatched.title"), T("web.admin.libraries.unmatched.description"))
        {
            _panel = panel;
            Section.SetIcon("search", Amber);
            _view = new ComboBox { Width = 180, Height = 32, FontSize = 12, CornerRadius = new(RadiusMd) };
            _view.Items.Add(new ComboBoxItem { Content = T("web.admin.libraries.unmatched.viewAwaiting"), Tag = "unmatched" });
            _view.Items.Add(new ComboBoxItem { Content = T("web.admin.libraries.unmatched.viewIgnored"), Tag = "ignored" });
            _view.SelectedIndex = 0;
            AutomationProperties.SetName(_view, T("web.admin.libraries.unmatched.viewLabel"));
            AutomationProperties.SetAutomationId(_view, "UnmatchedView");
            _view.SelectionChanged += (_, _) => { if (!_syncing) _ = _panel.SetViewAsync(_view.SelectedIndex == 1); };
            _search = SearchBox(T("web.admin.libraries.unmatched.searchAwaiting"), "UnmatchedSearch", text => _ = _panel.SetSearchAsync(text));
            _searchHost = WithSearchIcon(_search);
            _body.Children.Add(_totals);
            _body.Children.Add(_controls);
            _body.Children.Add(_rows);
            _body.Children.Add(_footer);
            Section.Body.Child = _body;
            Render();
        }

        internal override void Render()
        {
            var state = _panel.State;
            Section.SetOpen(state.Open);
            Section.SetCount(state.AwaitingTotal, error: state.TotalsError != null);
            _totals.Children.Clear();
            if (state.TotalsError is { } totalsError)
            {
                _totals.Children.Add(Text(totalsError, 12));
                var retry = RetryButton("UnmatchedTotalsRetry", () => _ = _panel.LoadTotalsAsync());
                retry.IsEnabled = !state.TotalsLoading;
                _totals.Children.Add(retry);
            }
            if (!state.Open) return;
            _syncing = true;
            try { if (_view.SelectedIndex != (state.Ignored ? 1 : 0)) _view.SelectedIndex = state.Ignored ? 1 : 0; }
            finally { _syncing = false; }
            _search.PlaceholderText = T(state.Ignored ? "web.admin.libraries.unmatched.searchIgnored" : "web.admin.libraries.unmatched.searchAwaiting");
            AutomationProperties.SetName(_search, _search.PlaceholderText);
            _controls.Children.Clear(); _controls.ColumnDefinitions.Clear(); _controls.RowDefinitions.Clear();
            Touch(_view); Touch(_search);
            var searchHost = _searchHost;
            if (Page._phone)
            {
                _view.Width = double.NaN; _view.HorizontalAlignment = HorizontalAlignment.Stretch;
                _controls.RowDefinitions.Add(new() { Height = GridLength.Auto }); _controls.RowDefinitions.Add(new() { Height = GridLength.Auto });
                _controls.Children.Add(_view); Grid.SetRow(searchHost, 1); _controls.Children.Add(searchHost);
            }
            else
            {
                _view.Width = 180;
                _controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _controls.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                _controls.Children.Add(_view); Grid.SetColumn(searchHost, 1); _controls.Children.Add(searchHost);
            }
            var empty = T(state.Ignored
                ? state.Search.Trim().Length > 0 ? "web.admin.libraries.unmatched.emptyIgnoredSearch" : "web.admin.libraries.unmatched.emptyIgnored"
                : state.Search.Trim().Length > 0 ? "web.admin.libraries.unmatched.emptyAwaitingSearch" : "web.admin.libraries.unmatched.emptyAwaiting");
            KeepFocus(_body, () =>
            {
                if (state.Error != null && state.Items.Count == 0)
                {
                    var failure = new StackPanel { Spacing = 8 };
                    failure.Children.Add(Wide ? TableFrame(GridTable(Columns(), [], state.Error, error: true)) : CardMessage(state.Error, true));
                    failure.Children.Add(RetryButton("UnmatchedRetry", () => _ = _panel.ReloadAsync()));
                    _rows.Content = failure;
                }
                else if (Wide)
                {
                    var rows = state.Items.Select(item => (IReadOnlyList<UIElement>)new UIElement[]
                    {
                        TitleWithYear(item), Text(item.LibraryName, 14), Badge(item.ContentType, BadgeTone.Outline), Badge(item.Status), Actions(item, state, false),
                    }).ToArray();
                    _rows.Content = TableFrame(GridTable(Columns(), rows, state.Items.Count == 0 ? (state.Loading ? T("common.loading") : empty) : null));
                }
                else if (state.Items.Count == 0) _rows.Content = CardMessage(state.Loading ? T("common.loading") : empty);
                else
                {
                    var list = new StackPanel { Spacing = 12 };
                    foreach (var item in state.Items)
                    {
                        var card = new StackPanel { Spacing = 12 };
                        card.Children.Add(TitleWithYear(item));
                        card.Children.Add(Fields((T("web.admin.libraries.column.library"), Text(item.LibraryName, 12)),
                            (T("web.admin.libraries.column.type"), Badge(item.ContentType, BadgeTone.Outline)), (T("web.admin.libraries.column.status"), Badge(item.Status))));
                        card.Children.Add(Actions(item, state, true));
                        list.Children.Add(Card(card));
                    }
                    _rows.Content = list;
                }
                _footer.Children.Clear();
                if (state.Error != null && state.Items.Count > 0) _footer.Children.Add(Text(state.Error, 12));
                if (Pager("Unmatched", state.Total, state.RangeStart, state.RangeEnd, state.PageIndex > 0 && !state.Loading, state.HasNext && !state.Loading,
                        () => _ = _panel.FirstAsync(), () => _ = _panel.PreviousAsync(), () => _ = _panel.NextAsync(), () => _ = _panel.LastAsync()) is { } pager)
                    _footer.Children.Add(pager);
                if (state.WalkCapped) _footer.Children.Add(Text(T("admin.native.libraries.pageCap", AdminLibrariesText.Count(state.PageIndex + 1)), 12, true));
                if (state.LastChange is { Undone: false } change) _footer.Children.Add(UndoBar(change));
            });
        }

        private static IReadOnlyList<(string, GridLength, bool)> Columns() =>
        [
            (T("web.admin.libraries.column.title"), new(1, GridUnitType.Star), false), (T("web.admin.libraries.column.library"), GridLength.Auto, false),
            (T("web.admin.libraries.column.type"), GridLength.Auto, false), (T("web.admin.libraries.column.status"), GridLength.Auto, false),
            (T("web.admin.libraries.column.actions"), new(190), false),
        ];

        private UIElement TitleWithYear(AdminUnmatchedItem item)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var link = new HyperlinkButton { Content = Strong(item.Title), Padding = new(0), MinHeight = 0 };
            AutomationProperties.SetName(link, item.Title);
            AutomationProperties.SetAutomationId(link, "UnmatchedItem_" + item.ContentId);
            link.Click += (_, _) => Page.OpenItem(item.ContentId, UiOwner);
            row.Children.Add(link);
            if (item.Year > 0) row.Children.Add(Text(T("web.admin.libraries.unmatched.year", ("year", item.Year)), 12, true));
            return row;
        }

        private FrameworkElement Actions(AdminUnmatchedItem item, AdminUnmatchedState state, bool card)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = card ? 8 : 4 };
            var match = Action(T("web.admin.libraries.match"), "UnmatchedMatch_" + item.ContentId, "search", iconSize: 12, gap: 4, fontSize: card ? 14 : 12, height: card ? 44 : 28);
            match.Click += async (_, _) =>
            {
                await Page.ShowMatchAsync(item.ContentId, item.Title, item.Year, item.ContentType, item.LibraryId, UiOwner);
            };
            actions.Children.Add(match);
            if (state.Ignored || P.CanIgnore(item))
            {
                var ignore = Action(T(state.Ignored ? "web.admin.libraries.unmatched.restore" : "web.admin.libraries.unmatched.ignore"), "UnmatchedIgnore_" + item.ContentId,
                    state.Ignored ? "undo-2" : "eye-off", "GhostButtonStyle", iconSize: 12, gap: 4, fontSize: card ? 14 : 12, height: card ? 44 : 28);
                AutomationProperties.SetName(ignore, T(state.Ignored ? "web.admin.libraries.unmatched.restoreLabel" : "web.admin.libraries.unmatched.ignoreLabel", ("title", item.Title)));
                ToolTipService.SetToolTip(ignore, T(state.Ignored ? "web.admin.libraries.unmatched.restoreTitle" : "web.admin.libraries.unmatched.ignoreTitle"));
                ignore.IsEnabled = !state.Pending.Contains(item.ContentId);
                ignore.Click += (_, _) => _ = _panel.SetIgnoredAsync(item, !state.Ignored);
                actions.Children.Add(ignore);
            }
            return card ? CardActionRow(actions) : actions;
        }

        /// <summary>The ignore/restore toast's description and Undo action, kept inline under the list.</summary>
        private Border UndoBar(AdminUnmatchedChange change)
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(Strong(T(change.Ignored ? "web.admin.libraries.toast.ignored" : "web.admin.libraries.toast.restored", ("title", change.Title)), 13));
            text.Children.Add(Text(T(change.Ignored ? "web.admin.libraries.toast.ignoredDescription" : "web.admin.libraries.toast.restoredDescription"), 12, true));
            grid.Children.Add(text);
            var undo = Action(T("web.admin.libraries.toast.undo"), "UnmatchedUndo");
            undo.Click += (_, _) => _ = _panel.UndoAsync();
            Grid.SetColumn(undo, 1); grid.Children.Add(undo);
            var dismiss = IconButton("x", Core.Localization.AppStrings.Get("web.ui.close", fallback: "Close"), "UnmatchedUndoDismiss", 28, 14);
            dismiss.Click += (_, _) => _panel.DismissChange();
            Grid.SetColumn(dismiss, 2); grid.Children.Add(dismiss);
            var bar = new Border { Child = grid, Padding = new(12), CornerRadius = new(RadiusLg), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Brush("SurfaceRaisedBrush") };
            AutomationProperties.SetLiveSetting(bar, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            return bar;
        }
    }

    // ── Metadata matcher queues ──────────────────────────────────────────

    private sealed class MatcherView : SectionView
    {
        private readonly AdminMatcherQueuesPanel _panel;
        private readonly ContentControl _rows = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private object?[]? _rendered;

        internal MatcherView(AdminLibrariesPage page, AdminMatcherQueuesPanel panel)
            : base(page, panel, "Matcher", T("web.admin.common.libraryEditor.matcher.title"), T("web.admin.common.libraryEditor.matcher.description"))
        {
            _panel = panel;
            Section.SetIcon("wrench", Amber);
            Section.Body.Child = _rows;
            Render();
        }

        private string LibraryName(string id) => Page.Libraries.FirstOrDefault(library => library.Id == id)?.Name ?? T("web.admin.common.libraryEditor.matcher.libraryFallback", ("id", id));

        internal override void Render()
        {
            var state = _panel.State;
            Section.SetOpen(state.Open);
            if (!state.Open) { _rendered = null; return; }
            object?[] key = [string.Join(",", state.Queues.Select(q => q.LibraryId + ":" + q.PendingCount + ":" + q.ParkedCount + ":" + q.TotalCount)), state.SelectedLibraryId,
                state.Detail, state.DetailFetching, state.DetailHasNext, state.DetailPageIndex, state.DetailError, state.Retrying, state.Error, state.Loaded, state.HasData, Wide, Page.Libraries];
            if (_rendered != null && key.Zip(_rendered).All(pair => Equals(pair.First, pair.Second))) return;
            _rendered = key;
            KeepFocus(_rows, () =>
            {
                // Official order: a failed first read with Retry, then Loading, then the empty message, then the queues.
                if (state.Error != null && !state.HasData)
                {
                    var failure = new StackPanel { Spacing = 8 };
                    failure.Children.Add(CardMessage(state.Error, true));
                    failure.Children.Add(RetryButton("MatcherRetryLoad", () => _ = _panel.LoadSummaryAsync()));
                    _rows.Content = failure;
                    return;
                }
                if (!state.HasData) { _rows.Content = CardMessage(T("common.loading")); return; }
                if (state.Queues.Count == 0) { _rows.Content = CardMessage(T("web.admin.libraries.health.matcherEmpty")); return; }
                if (Wide)
                {
                    var grid = new Grid();
                    foreach (var width in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, new GridLength(112) }) grid.ColumnDefinitions.Add(new() { Width = width });
                    var row = 0;
                    void Line(UIElement[] cells, bool header, string? libraryId)
                    {
                        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                        var band = new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                        Grid.SetRow(band, row); Grid.SetColumnSpan(band, 4);
                        if (libraryId != null) band.Tapped += (_, _) => _ = _panel.ToggleLibraryAsync(libraryId);
                        grid.Children.Add(band);
                        for (var column = 0; column < cells.Length; column++)
                        {
                            var cell = new Border { Child = cells[column], Padding = new(8, header ? 10 : 8, 8, header ? 10 : 8), HorizontalAlignment = column is 1 or 2 or 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                            Grid.SetRow(cell, row); Grid.SetColumn(cell, column);
                            grid.Children.Add(cell);
                        }
                        row++;
                    }
                    TextBlock Head(string key) { var text = Text(T(key), 14); text.FontWeight = FontWeights.Medium; return text; }
                    Line([Head("web.admin.common.libraryEditor.matcher.library"), Head("web.admin.common.libraryEditor.matcher.pending"), Head("web.admin.common.libraryEditor.matcher.parked"), new Border()], true, null);
                    foreach (var queue in state.Queues)
                    {
                        var name = new HyperlinkButton { Content = Strong(LibraryName(queue.LibraryId)), Padding = new(0), MinHeight = 0 };
                        AutomationProperties.SetAutomationId(name, "MatcherLibrary_" + queue.LibraryId);
                        AutomationProperties.SetItemStatus(name, state.SelectedLibraryId == queue.LibraryId ? "expanded" : "collapsed");
                        var id = queue.LibraryId;
                        name.Click += (_, _) => _ = _panel.ToggleLibraryAsync(id);
                        Line([name, Text(queue.PendingCount.ToString("N0", Core.Localization.AppStrings.FormatCulture)), ParkedCount(queue.ParkedCount), RetryNow(queue.LibraryId, state, false)], false, id);
                        if (state.SelectedLibraryId == queue.LibraryId)
                        {
                            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                            var holder = new Border { Child = Detail(state, false), Padding = new(12), Background = Tint("MutedBrush", 0.3), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
                            Grid.SetRow(holder, row); Grid.SetColumnSpan(holder, 4); grid.Children.Add(holder);
                            row++;
                        }
                    }
                    _rows.Content = new Border { Child = grid, CornerRadius = new(RadiusXl), BorderThickness = new(1), BorderBrush = Brush("BorderBrush") };
                }
                else
                {
                    var list = new StackPanel { Spacing = 12 };
                    foreach (var queue in state.Queues)
                    {
                        var selected = state.SelectedLibraryId == queue.LibraryId;
                        var card = new StackPanel { Spacing = 12 };
                        var head = new Grid { ColumnSpacing = 12 };
                        head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                        var text = new StackPanel { Spacing = 4 };
                        text.Children.Add(Strong(LibraryName(queue.LibraryId)));
                        var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                        var pending = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        pending.Children.Add(Text(T("web.admin.common.libraryEditor.matcher.pending"), 12, true)); pending.Children.Add(Text(queue.PendingCount.ToString("N0", Core.Localization.AppStrings.FormatCulture), 12));
                        var parked = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                        parked.Children.Add(Text(T("web.admin.common.libraryEditor.matcher.parked"), 12, true)); parked.Children.Add(ParkedCount(queue.ParkedCount));
                        counts.Children.Add(pending); counts.Children.Add(parked);
                        text.Children.Add(counts);
                        head.Children.Add(text);
                        var chevron = Icon(selected ? "chevron-down" : "chevron-right", 16, Brush("SecondaryTextBrush")); Grid.SetColumn(chevron, 1); head.Children.Add(chevron);
                        var toggle = new Button { Content = head, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 44, Padding = new(8), CornerRadius = new(RadiusXl), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
                        AutomationProperties.SetName(toggle, LibraryName(queue.LibraryId));
                        AutomationProperties.SetAutomationId(toggle, "MatcherLibrary_" + queue.LibraryId);
                        AutomationProperties.SetItemStatus(toggle, selected ? "expanded" : "collapsed");
                        var id = queue.LibraryId;
                        toggle.Click += (_, _) => _ = _panel.ToggleLibraryAsync(id);
                        card.Children.Add(toggle);
                        if (selected) card.Children.Add(new Border { Child = Detail(state, true), Padding = new(12), CornerRadius = new(RadiusXl), Background = Tint("MutedBrush", 0.3) });
                        var retry = RetryNow(queue.LibraryId, state, true); retry.HorizontalAlignment = HorizontalAlignment.Stretch;
                        card.Children.Add(retry);
                        list.Children.Add(Card(card));
                    }
                    _rows.Content = list;
                }
            });
        }

        private static UIElement ParkedCount(long count) =>
            count > 0 ? Badge(count.ToString("N0", Core.Localization.AppStrings.FormatCulture), BadgeTone.Warning) : Text("0");

        private Button RetryNow(string libraryId, AdminMatcherState state, bool touch)
        {
            var button = Action(T("web.admin.common.libraryEditor.matcher.retryNow"), "MatcherRetry_" + libraryId, height: touch ? 44 : 32);
            button.IsEnabled = !state.Retrying.Contains(libraryId);
            button.Click += (_, _) => _ = _panel.RetryAsync(libraryId);
            return button;
        }

        private StackPanel Detail(AdminMatcherState state, bool touch)
        {
            var stack = new StackPanel { Spacing = 8 };
            AutomationProperties.SetAutomationId(stack, "MatcherDetail");
            if (state.DetailError != null) stack.Children.Add(Text(state.DetailError, 12));
            var entries = state.Detail == null ? [] : P.MatcherEntries(state.Detail);
            foreach (var entry in entries)
            {
                var box = new StackPanel { Spacing = 4 };
                var head = new Grid { ColumnSpacing = 8 };
                head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var path = Mono(entry.Path, 12, false);
                // A long unbroken path segment wraps in the width it has (WebUI CardPath) instead of being cut or widening the table.
                path.TextWrapping = TextWrapping.Wrap; path.VerticalAlignment = VerticalAlignment.Center;
                ToolTipService.SetToolTip(path, entry.Path);
                var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
                if (entry.FailureKind is { Length: > 0 } kind) badges.Children.Add(Badge(kind.Replace('_', ' '), BadgeTone.Outline));
                badges.Children.Add(Badge(P.MatcherState(entry.State), entry.State == "parked" ? BadgeTone.Outline : BadgeTone.Secondary));
                if (touch) { box.Children.Add(path); box.Children.Add(badges); }
                else { head.Children.Add(path); Grid.SetColumn(badges, 1); head.Children.Add(badges); box.Children.Add(head); }
                if ((entry.Rejection ?? entry.Message) is { Length: > 0 } message) box.Children.Add(Text(message, 12, true));
                if (entry.Candidates.Count > 0)
                {
                    var candidates = new StackPanel { Spacing = 4, Padding = new(0, 8, 0, 0), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 1, 0, 0) };
                    foreach (var candidate in entry.Candidates)
                    {
                        var line = new LogsWrapPanel { Spacing = 8 };
                        line.Children.Add(Strong(candidate.Title, 12));
                        line.Children.Add(Text(candidate.Score, 12, true));
                        if (candidate.MatchedTitle != null) line.Children.Add(Text(candidate.MatchedTitle, 12, true));
                        if (candidate.Reasons != null) line.Children.Add(Text(candidate.Reasons, 12, true));
                        if (candidate.Sources != null) line.Children.Add(Text(candidate.Sources, 12, true));
                        candidates.Children.Add(line);
                    }
                    box.Children.Add(candidates);
                }
                stack.Children.Add(new Border { Child = box, Padding = new(8), CornerRadius = new(RadiusLg), BorderThickness = new(1), BorderBrush = Brush("BorderBrush"), Background = Tint("AppBackgroundBrush", 0.7) });
            }
            if (state.Detail != null && entries.Count == 0)
            {
                var none = Text(T("web.admin.common.libraryEditor.matcher.noDetails"), 12, true); none.HorizontalAlignment = HorizontalAlignment.Center;
                stack.Children.Add(none);
            }
            if (state.DetailPageIndex > 0 || state.DetailHasNext)
            {
                var pager = new Grid { ColumnSpacing = 8, RowSpacing = 8, Padding = new(0, 4, 0, 0) };
                pager.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); pager.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                pager.Children.Add(Text(T("web.admin.common.libraryEditor.matcher.page", ("page", state.DetailPageIndex + 1)), 12, true));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var previous = Action(T("web.admin.common.libraryEditor.matcher.previous"), "MatcherPrevious", height: touch ? 44 : 32);
                previous.IsEnabled = state.DetailPageIndex > 0 && !state.DetailFetching;
                previous.Click += (_, _) => _ = _panel.PreviousDetailAsync();
                var next = Action(T("web.admin.common.libraryEditor.matcher.next"), "MatcherNext", height: touch ? 44 : 32);
                next.IsEnabled = state.DetailHasNext && !state.DetailFetching;
                next.Click += (_, _) => _ = _panel.NextDetailAsync();
                buttons.Children.Add(previous); buttons.Children.Add(next);
                Grid.SetColumn(buttons, 1); pager.Children.Add(buttons);
                stack.Children.Add(pager);
            }
            return stack;
        }
    }
}
