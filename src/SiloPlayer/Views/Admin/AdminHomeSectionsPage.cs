using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The Home sections route (/admin/sections), including its library-page switcher. No timers, catalog walks or entrance transitions.
/// Official source: 1a93371 web/src/pages/AdminHomeRows.tsx and web/src/components/homeRows.
/// </summary>
public sealed partial class AdminHomeSectionsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly AdminHomeSectionsApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminServerSettingsApi _settingsApi;
    private readonly Grid _root = new();
    private readonly StackPanel _body = new() { Spacing = 24, Padding = new(0, 0, 0, 24), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _alerts = new() { Spacing = 8 };
    private readonly TextBlock _status = U.Text("", 14, true);
    private readonly TextBlock _summary = U.Text("", 13, true);
    private readonly TextBlock _libraryNote = U.Text("", 13, true);
    private readonly TextBlock _reorderHint = U.Text("", 13, true);
    private readonly TextBlock _empty = U.Text("", 14, true);
    private readonly StackPanel _libraryNoteRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, -12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _reorderRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new(18, 12, 18, 14), Visibility = Visibility.Collapsed };
    private readonly StackPanel _panelBody = new();
    private readonly Border _panel;
    private readonly StackPanel _headerActions = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid _pillsRow = new() { ColumnSpacing = 12, RowSpacing = 4 };
    private readonly Grid _dock = new() { ColumnSpacing = 10, Padding = new(16, 22, 16, 20), VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private readonly Border _selectFloat = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(16, 0, 16, 16), Visibility = Visibility.Collapsed };
    private bool _narrow, _phone;
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, AllowDrop = true,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, CanDragItems = true, CanReorderItems = true };
    private readonly ObservableCollection<RowView> _rows = [];
    private readonly Grid _selectionHeader = new() { ColumnSpacing = 12, Padding = new(8, 10, 14, 10), Margin = new(0, 0, 0, 2), Visibility = Visibility.Collapsed, BorderThickness = new(0, 0, 0, 1) };
    private readonly StackPanel _selectionBar = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly StackPanel _defaults = new() { Spacing = 12 };
    private readonly Button _add = AdminHomeSectionsUi.IconAction(T("web.home.rows.page.addRow"), "HomeSectionsAdd", "plus", primary: true);
    private readonly Button _more = AdminHomeSectionsUi.IconAction(T("web.ui.pageMoreMenu.more"), "HomeSectionsMore", "ellipsis");
    private readonly HashSet<string> _selection = new(StringComparer.Ordinal);
    private AdminHomeSectionsController? _controller;
    private AdminHomeSectionsSnapshot? _dragSnapshot;
    private ContentDialog? _dialog;
    private bool _selectMode, _active, _opening;
    private long _dialogRevision;
    public AdminHomeSectionsController? Controller => _controller;
    public FrameworkElement ScrollContent => _body;
    public bool RequiresLeaveConfirmation => _editor != null && _editorDirty && _controller?.State.Owner?.IsCurrent == true;
    private static ToastService? Toasts => App.Services.GetService<ToastService>();

    public AdminHomeSectionsPage() : this(App.Services.GetRequiredService<AdminHomeSectionsApi>(),
        App.Services.GetRequiredService<AdminMonitorAuthority>(), App.Services.GetRequiredService<AdminServerSettingsApi>()) { }
    public AdminHomeSectionsPage(AdminHomeSectionsApi api, AdminMonitorAuthority authority, AdminServerSettingsApi settingsApi)
    {
        InitializeComponent(); _api = api; _authority = authority; _settingsApi = settingsApi;
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminHomeSectionsPage");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_status, "HomeSectionsStatus");
        _summary.TextWrapping = TextWrapping.NoWrap; _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.TextAlignment = TextAlignment.Center; _empty.Margin = new(18, 32, 18, 32);
        _empty.Visibility = Visibility.Collapsed;
        // PageHeader: the title with its subtitle, and the page's buttons at the right.
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 8 };
        titles.Children.Add(U.Title(T("web.admin.homeRows.page.title")));
        titles.Children.Add(U.Text(T("web.admin.homeRows.page.subtitle"), 14, true)); heading.Children.Add(titles);
        Grid.SetColumn(_headerActions, 1); heading.Children.Add(_headerActions);
        _add.Click += (_, _) => OpenAdd(); _more.Click += MoreClick;
        _body.Children.Add(heading);
        // PillSwitcher: the pages as pills, with the row count at the right (below the pills on a phone).
        _pillsRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _pillsRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _pillsRow.RowDefinitions.Add(new() { Height = GridLength.Auto }); _pillsRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _pillsRow.Children.Add(_pages); _pillsRow.Children.Add(_summary); _summary.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(_pages, "HomeSectionsPageSelector"); AutomationProperties.SetName(_pages, T("web.home.rows.page.pageSwitcher"));
        _pages.Set([(AdminHomeSectionScope.Home, T("web.home.rows.pages.home"), "HomeSectionsPage_home", true)], AdminHomeSectionScope.Home); _pages.Chosen += PageChosen;
        _libraryNoteRow.Children.Add(AdminHomeSectionsUi.Icon("info", 15, U.Brush("SecondaryTextBrush"))); _libraryNoteRow.Children.Add(_libraryNote);
        _body.Children.Add(_pillsRow); _body.Children.Add(_libraryNoteRow); _body.Children.Add(_alerts); _body.Children.Add(_status);
        AutomationProperties.SetAutomationId(_list, "HomeSectionsRows");
        _list.ItemTemplate = (DataTemplate)Resources["HomeSectionRowTemplate"]; _list.ItemContainerStyle = (Style)Resources["HomeSectionRowContainer"]; _list.ItemsSource = _rows;
        _list.ContainerContentChanging += RowContainerChanging;
        _list.ItemClick += async (_, args) => { if (!_selectMode && args.ClickedItem is RowView row) await OpenEditAsync(row.Row.Id); };
        _list.DragItemsStarting += (_, args) =>
        {
            if (_selectMode || _controller?.CanWrite != true || args.Items.Count != 1) { args.Cancel = true; return; }
            _dragSnapshot = _controller.State.Snapshot;
        };
        _list.DragItemsCompleted += async (_, _) =>
        {
            var snapshot = _dragSnapshot; _dragSnapshot = null;
            if (snapshot != null && _controller != null && !snapshot.Rows.Select(row => row.Id).SequenceEqual(_rows.Select(row => row.Row.Id)))
                await _controller.ReorderAsync(snapshot, _rows.Select(row => row.Row.Id).ToArray());
            if (_active) Render();
        };
        _list.SizeChanged += (_, _) => { if (_active && Math.Abs(_list.ActualWidth - _lastListWidth) > 8) { _lastListWidth = _list.ActualWidth; Render(); } };
        // The list's panel: surface-panel, rounded-[26px], p-1.5. Select all sits at its top in select mode; the reorder hint at its foot.
        _selectionHeader.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .75);
        _reorderRow.Children.Add(new ContentControl { Content = AdminHomeSectionsUi.Icon("grip-vertical", 15, U.Brush("SecondaryTextBrush")), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) });
        _reorderRow.Children.Add(_reorderHint);
        _panelBody.Children.Add(_selectionHeader); _panelBody.Children.Add(_empty); _panelBody.Children.Add(_list); _panelBody.Children.Add(_reorderRow);
        _panel = AdminMonitorUi.Panel(_panelBody, new(6), 26); _panel.Visibility = Visibility.Collapsed;
        _body.Children.Add(_panel); _body.Children.Add(_defaults);
        // The select bar floats over the foot of the page; the dock holds More and Add row under 1024px.
        _selectFloat.Child = _selectionBar; _selectFloat.CornerRadius = new(16); _selectFloat.BorderThickness = new(1); _selectFloat.Padding = new(16, 7, 7, 7);
        _selectFloat.Background = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("PopoverColor", Windows.UI.Color.FromArgb(255, 0x12, 0x1A, 0x25)), .95); _selectFloat.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border);
        AutomationProperties.SetName(_selectFloat, T("web.common.ui.selectMode.rows.group")); AutomationProperties.SetAutomationId(_selectFloat, "HomeSectionsSelectBar");
        var dockFill = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
        var background = AdminMonitorUi.ColorOf("AppBackgroundColor", Windows.UI.Color.FromArgb(255, 0x10, 0x17, 0x22));
        dockFill.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(0, background.R, background.G, background.B), Offset = 0 }); dockFill.GradientStops.Add(new() { Color = background, Offset = .3 });
        _dock.Background = dockFill; _dock.ColumnDefinitions.Add(new() { Width = new(48) }); _dock.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        AutomationProperties.SetName(_dock, T("web.home.rows.page.actionsRegion")); AutomationProperties.SetAutomationId(_dock, "HomeSectionsDock");
        _scroll.Content = _body; _root.Children.Add(_scroll); _root.Children.Add(_selectFloat); _root.Children.Add(_dock); Content = _root;
        ApplyLayout();
        Loaded += (_, _) => { ApplyLayout(); Activate(); }; Unloaded += (_, _) => Close(); SizeChanged += (_, _) => Fit();
        KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Escape && _selectMode && _editor == null && _dialog == null) { ExitSelectMode(); args.Handled = true; } };
    }
    private double _lastListWidth;

    /// <summary>The page's two breakpoints, read from the window like the official media queries: under 1024px the buttons move to a docked bar, under 640px rows drop their tags and the row art shrinks.</summary>
    private void ApplyLayout()
    {
        var width = XamlRoot?.Size.Width ?? 1440; var narrow = width < 1024; var phone = width < 640;
        var changed = narrow != _narrow || phone != _phone; _narrow = narrow; _phone = phone;
        var primaryInk = AdminHomeSectionsUi.PrimaryInk;
        _add.Height = narrow ? 48 : 32; _add.CornerRadius = new(narrow ? 14 : 10); _add.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        _add.Content = AdminHomeSectionsUi.ActionContent(T("web.home.rows.page.addRow"), "plus", narrow ? 15 : 14, primaryInk);
        _more.Width = narrow ? 48 : double.NaN; _more.Height = narrow ? 48 : 32; _more.CornerRadius = new(narrow ? 14 : 10); _more.Padding = narrow ? new(0) : new(12, 0, 12, 0);
        _more.Content = narrow ? AdminHomeSectionsUi.Icon("ellipsis", 20, U.Brush("PrimaryTextBrush")) : AdminHomeSectionsUi.ActionContent(T("web.ui.pageMoreMenu.more"), "ellipsis", 14, U.Brush("PrimaryTextBrush"));
        _headerActions.Children.Clear(); _dock.Children.Clear();
        if (narrow) { Grid.SetColumn(_more, 0); Grid.SetColumn(_add, 1); _dock.Children.Add(_more); _dock.Children.Add(_add); }
        else { Grid.SetColumn(_more, 0); Grid.SetColumn(_add, 0); _headerActions.Children.Add(_more); _headerActions.Children.Add(_add); }
        // The row count sits beside the pills, or under them on a phone.
        Grid.SetColumn(_pages, 0); Grid.SetRow(_pages, 0); Grid.SetColumnSpan(_pages, phone ? 2 : 1);
        Grid.SetColumn(_summary, phone ? 0 : 1); Grid.SetRow(_summary, phone ? 1 : 0); Grid.SetColumnSpan(_summary, phone ? 2 : 1);
        _dock.Visibility = narrow && !_selectMode ? Visibility.Visible : Visibility.Collapsed;
        _body.Padding = new(0, 0, 0, narrow || _selectMode ? 96 : 24);
        if (changed && _active) Render();
    }

    private void Fit()
    {
        ApplyLayout();
        _list.MaxHeight = Math.Max(240, ActualHeight - 280);
        var available = _scroll.ActualWidth - _body.Margin.Left - _body.Margin.Right;
        if (available > 0) _body.Width = Math.Min(_body.MaxWidth, available);
        if (_editor != null) { _editor.MaxHeight = Math.Max(200, ActualHeight - 32); _editor.Width = Math.Min(800, Math.Max(280, ActualWidth - 32)); }
    }
    private void Activate()
    {
        if (_active) return; _active = true;
        _controller = new(_api, _authority); _controller.Changed += QueueRender; _controller.Saved += InvalidateHome;
        _authority.AvailabilityChanged += AuthorityChanged; _ = OpenAsync(); Fit();
    }
    private async Task OpenAsync()
    {
        if (!_active || _opening || !_authority.IsAvailable || _controller == null) return;
        _opening = true; var controller = _controller;
        try { await controller.OpenAsync(_scope); if (ReferenceEquals(controller, _controller) && _active) { await LoadPagesAsync(); await LoadDefaultsAsync(); await ReadLinksAsync(); } }
        finally
        {
            _opening = false;
            if (_active && _authority.IsAvailable && _controller?.State.Owner == null) DispatcherQueue.TryEnqueue(() => { if (_active) _ = OpenAsync(); });
        }
    }
    private void AuthorityChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return; CloseEditor(); _dialogRevision++; _dialog?.Hide(); _settings = null; _defaults.Children.Clear();
        if (_authority.IsAvailable) _ = OpenAsync();
    });
    private void QueueRender() => DispatcherQueue.TryEnqueue(() => { if (_active) Render(); });

    private async Task ReloadAllAsync()
    {
        var controller = _controller; if (controller == null || !await CanLeaveAsync()) return;
        CloseEditor(); _selection.Clear(); _librariesFailed = false;
        await controller.ReloadAsync();
        if (_active && ReferenceEquals(controller, _controller)) { await LoadPagesAsync(); await LoadDefaultsAsync(); await ReadLinksAsync(); }
    }

    private void Render()
    {
        if (_controller == null) return; var state = _controller.State;
        var current = state.Owner?.IsCurrent == true; var canWrite = _controller.CanWrite;
        if (!current) { CloseEditor(); _dialog?.Hide(); _selection.Clear(); _selectMode = false; _settings = null; _defaults.Children.Clear(); ClearPageNames(); }
        _add.IsEnabled = canWrite && _editor == null; _more.IsEnabled = canWrite && _editor == null;
        var rows = current ? state.Snapshot?.Rows ?? [] : [];
        var ready = current && state.Snapshot != null;
        _alerts.Children.Clear();
        if (current && state.Failure == AdminHomeSectionsFailure.Conflict)
            _alerts.Children.Add(ConflictBanner(state.Busy));
        else if (current && state.Failure == AdminHomeSectionsFailure.Unknown)
            _alerts.Children.Add(Banner(state.Error ?? new AdminHomeSectionsUnknownException().Message, state.Busy));
        else if (current && (state.Failure == AdminHomeSectionsFailure.Load || state.Snapshot == null && !state.Busy))
            _alerts.Children.Add(ReadFailedPanel(state.Error ?? T("web.home.rows.page.loadFailed"), state.Busy));
        else if (current && _librariesFailed)
            _alerts.Children.Add(Banner(AdminHomeSectionsText.T("native.homeSections.librariesFailed"), state.Busy));
        _alerts.Visibility = _alerts.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = !current ? AdminHomeSectionsText.T("native.homeSections.adminRequired")
            : state.Busy ? (state.Snapshot == null ? T("web.home.rows.page.loading") : T("web.admin.homeRows.page.loadingRows"))
            : state.Failure == AdminHomeSectionsFailure.Refused ? state.Error ?? "" : "";
        _status.Visibility = _status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _panel.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = ready && rows.Count == 0 ? T("web.home.rows.page.empty", new { page = PageLabel }) : "";
        _empty.Visibility = _empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _summary.Text = !ready ? "" : T("web.home.rows.page.summaryAdmin", new { row_count = (long)rows.Count, shown_count = rows.Count(row => row.Enabled) });
        _libraryNote.Text = current && _scope.Kind == "library" ? T("web.home.rows.notes.libraryPage", new { library = PageLabel }) : "";
        _libraryNoteRow.Visibility = _libraryNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _reorderHint.Text = ready && rows.Count > 0 && !_selectMode ? T("web.home.rows.notes.reorderHint") : "";
        _reorderRow.Visibility = _reorderHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _defaults.Visibility = _defaults.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.CanDragItems = _list.CanReorderItems = canWrite && !_selectMode; _list.IsEnabled = !state.Busy && current;
        AutomationProperties.SetName(_list, T("web.home.rows.page.listLabel", new { page = PageLabel }));
        _pages.PillsEnabled = !state.Busy && current && _editor == null;
        _dock.Visibility = _narrow && !_selectMode ? Visibility.Visible : Visibility.Collapsed;
        _selectFloat.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        _body.Padding = new(0, 0, 0, _narrow || _selectMode ? 96 : 24);
        // An in-flight drag retains its captured complete snapshot. Any replacement invalidates it.
        if (!ReferenceEquals(_dragSnapshot, state.Snapshot)) _dragSnapshot = null;
        var fresh = new List<RowView>();
        var existing = _rows.ToDictionary(view => view.Row.Id, StringComparer.Ordinal);
        var banner = rows.FirstOrDefault(row => row.Featured && row.Enabled)?.Id;
        var titleRoom = Math.Max(120, (_lastListWidth > 0 ? _lastListWidth : 700) - (_phone ? 48 : 74) - 28 - 44 - 100);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var view = existing.TryGetValue(row.Id, out var known) ? known : new RowView();
            view.Update(row, _selectMode, _selection.Contains(row.Id), canWrite, row.Id == banner, DescribeRow(row),
                new(index, index > 0 && rows[index - 1].Enabled, _narrow, _phone, row.Featured && !_phone ? titleRoom - 130 : titleRoom)); fresh.Add(view);
        }
        // Rows keep their views, so bindings refresh in place and every switch and name shows the saved value after any outcome.
        foreach (var gone in _rows.Where(view => !fresh.Contains(view)).ToArray()) _rows.Remove(gone);
        for (var i = 0; i < fresh.Count; i++)
        {
            if (i < _rows.Count && ReferenceEquals(_rows[i], fresh[i])) continue;
            if (_rows.Contains(fresh[i])) _rows.Move(_rows.IndexOf(fresh[i]), i); else _rows.Insert(i, fresh[i]);
        }
        _selection.IntersectWith(rows.Select(row => row.Id)); RenderSelection();
        if (_editor != null)
        {
            RefreshSave(); _editorScroll.IsEnabled = !state.Busy;
            _editorBack.IsEnabled = _editorDelete.IsEnabled = canWrite;
            if (state.Error != null && state.Failure != AdminHomeSectionsFailure.None) _editorNotice.Text = state.Error;
        }
    }

    /// <summary>The conflict banner (ConflictBanner): warning border and fill, the notice, and Reload rows.</summary>
    private FrameworkElement ConflictBanner(bool busy)
    {
        var warning = AdminMonitorUi.Warning;
        var reload = AdminHomeSectionsUi.IconAction(T("web.home.rows.reloadRows"), "HomeSectionsReload", null, height: 32, fontSize: 14); reload.IsEnabled = !busy;
        reload.Click += async (_, _) => await ReloadAllAsync();
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var icon = AdminHomeSectionsUi.Icon("circle-alert", 18, AdminHomeSectionsUi.Solid(warning)); icon.VerticalAlignment = VerticalAlignment.Center;
        var text = U.Text(T("web.home.rows.notes.conflict"), 14); AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Assertive);
        row.Children.Add(icon); Grid.SetColumn(text, 1); row.Children.Add(text); Grid.SetColumn(reload, 2); row.Children.Add(reload);
        return new Border { Child = row, CornerRadius = new(16), BorderThickness = new(1), Padding = new(16, 12, 16, 12), BorderBrush = AdminHomeSectionsUi.Solid(warning, .4), Background = AdminHomeSectionsUi.Solid(warning, .1) };
    }

    /// <summary>A failed read: the surface panel with the server's problem text and Reload rows (the official error panel).</summary>
    private FrameworkElement ReadFailedPanel(string message, bool busy)
    {
        var reload = AdminHomeSectionsUi.IconAction(T("web.home.rows.reloadRows"), "HomeSectionsReload", null); reload.IsEnabled = !busy;
        reload.Click += async (_, _) => await ReloadAllAsync();
        var text = U.Text(message, 14); AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Assertive);
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(text); reload.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(reload);
        return AdminMonitorUi.Panel(panel, new(24), 16);
    }

    /// <summary>A native-only alert (unknown outcome, library pages) with Reload under it.</summary>
    private FrameworkElement Banner(string message, bool busy)
    {
        var reload = AdminHomeSectionsUi.IconAction(T("web.home.rows.reloadRows"), "HomeSectionsReload", null); reload.IsEnabled = !busy;
        reload.Click += async (_, _) => await ReloadAllAsync();
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(U.Alert(message)); reload.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(reload);
        return panel;
    }

    private IReadOnlyList<AdminHomeSectionPart> DescribeRow(AdminHomeSection row) => AdminHomeSectionsDescription.Describe(row, _scope.Kind == "home", CollectionLookup);
    private AdminHomeSectionCollectionLookup CollectionLookup(string id) =>
        !_collectionsLoaded ? AdminHomeSectionCollectionLookup.Unknown
        : _collectionNames.TryGetValue(id, out var found) ? found
        // A list the server cut short cannot say a collection is gone.
        : _collectionsPartial ? AdminHomeSectionCollectionLookup.Unknown : AdminHomeSectionCollectionLookup.Gone;

    // ── Row list ─────────────────────────────────────────────

    private void RowContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is not FrameworkElement root) return;
        var row = args.InRecycleQueue ? null : args.Item as RowView;
        // A container shows one row at a time. The row it showed before lets go, so a later Render cannot write that row's values into it.
        if (root.Tag is RowView previous && !ReferenceEquals(previous, row)) previous.DetachRoot(root);
        root.Tag = row;
        if (row == null) return;
        AutomationProperties.SetName(args.ItemContainer, row.Title);
        row.AttachRoot(root);
    }

    private void MoreClick(object sender, RoutedEventArgs args)
    {
        var rows = _controller?.State.Snapshot?.Rows ?? [];
        var canWrite = _controller?.CanWrite == true;
        var menu = new MenuFlyout { Placement = _narrow ? Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft : Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        Menu(menu, T("web.admin.homeRows.more.select"), () => { _selectMode = true; Render(); FocusSelectAll(); }, canWrite && !_selectMode && rows.Count > 0, "\uE73A", T("web.admin.homeRows.more.selectHelp"));
        MenuAsync(menu, T("web.admin.homeRows.more.restore"), RestoreAsync, canWrite && _controller?.State.Busy != true, "\uE777", help: T("web.admin.homeRows.more.restoreHelp"));
        menu.ShowAt(_more);
    }

    private async void ShownToggled(object sender, RoutedEventArgs args)
    {
        // The switch starts from the saved value; only a user change differs from it.
        if (sender is not ToggleSwitch { DataContext: RowView row } toggle || _controller == null || toggle.IsOn == row.Row.Enabled) return;
        await _controller.QuickAsync(row.Row, shown: toggle.IsOn);
    }

    private void RowSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { DataContext: RowView row } box) return;
        if (box.IsChecked == true) { if (!_selection.Contains(row.Row.Id) && _selection.Count < MaxSelected) _selection.Add(row.Row.Id); else if (!_selection.Contains(row.Row.Id)) box.IsChecked = false; }
        else _selection.Remove(row.Row.Id);
        RenderSelection();
    }

    private void RowMenuClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RowView row } button || _controller == null) return;
        var canWrite = _controller.CanWrite; var index = _rows.IndexOf(row);
        var menu = new MenuFlyout();
        MenuAsync(menu, T("web.admin.homeRows.rowMenu.edit"), async () => await OpenEditAsync(row.Row.Id), canWrite, "\uE70F");
        MenuAsync(menu, T(row.Row.Featured ? "web.admin.homeRows.rowMenu.stopUsingAsHero" : "web.admin.homeRows.rowMenu.useAsHero"),
            async () => await _controller.QuickAsync(row.Row, hero: !row.Row.Featured), canWrite && (row.Row.Featured || !AdminHomeSectionsKinds.HoldsCollections(row.Row.SectionType)), row.Row.Featured ? "\uE735" : "\uE734");
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuAsync(menu, T("web.home.rows.page.moveToTop"), async () => await MoveAsync(row.Row.Id, 0), canWrite && index > 0, "\uE74A");
        MenuAsync(menu, T("web.home.rows.page.moveToBottom"), async () => await MoveAsync(row.Row.Id, _rows.Count - 1), canWrite && index < _rows.Count - 1, "\uE74B");
        // Native keyboard path for ordering; the WebUI's handle-and-arrow drag has no native list equivalent.
        MenuAsync(menu, AdminHomeSectionsText.T("native.homeSections.moveUp"), async () => await MoveAsync(row.Row.Id, index - 1), canWrite && index > 0);
        MenuAsync(menu, AdminHomeSectionsText.T("native.homeSections.moveDown"), async () => await MoveAsync(row.Row.Id, index + 1), canWrite && index < _rows.Count - 1);
        if (_controller.State.Capabilities?.Preview == true) MenuAsync(menu, AdminHomeSectionsText.T("native.homeSections.preview"), async () => await PeekAsync(row.Row), true, "\uE7B3");
        if (_scope.Kind == "library" && AdminHomeSectionDraft.From(row.Row) is { CanCopy: true } copyable && CopyTargets(copyable).Count > 0)
            MenuAsync(menu, T("web.admin.homeRows.rowMenu.addToOtherLibraries"), async () => await CopyAsync(row.Row), canWrite, "\uE8C8");
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuAsync(menu, T("web.admin.homeRows.rowMenu.delete"), async () => await DeleteAsync([row.Row.Id]), canWrite, "\uE74D", destructive: true);
        menu.ShowAt(button);
    }
    private async Task MoveAsync(string id, int target)
    {
        if (_controller?.State.Snapshot is not { } snapshot || !_controller.CanWrite || target < 0 || target >= snapshot.Rows.Count) return;
        var ids = snapshot.Rows.Select(row => row.Id).ToList(); ids.Remove(id); ids.Insert(target, id);
        await _controller.ReorderAsync(snapshot, ids);
    }

    // ── Select mode ──────────────────────────────────────────

    private const int MaxSelected = 100;
    private void ExitSelectMode() { _selectMode = false; _selection.Clear(); Render(); FocusMore(); }
    private void FocusSelectAll() => DispatcherQueue.TryEnqueue(() => _selectionHeader.Children.OfType<CheckBox>().FirstOrDefault()?.Focus(FocusState.Programmatic));
    private void FocusMore() => DispatcherQueue.TryEnqueue(() => { if (_more.IsLoaded && _more.IsEnabled) _more.Focus(FocusState.Programmatic); });

    private void RenderSelection()
    {
        _selectionHeader.Children.Clear(); _selectionHeader.ColumnDefinitions.Clear(); _selectionBar.Children.Clear();
        _selectionHeader.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        _selectFloat.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_selectMode) return;
        var rows = _controller?.State.Snapshot?.Rows ?? [];
        // SelectAllHeader: the checkbox, its limit and Done, over a hairline.
        _selectionHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _selectionHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _selectionHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var all = new CheckBox { Content = AdminHomeSectionsText.T("web.ui.selectMode.selectAll"), MinWidth = 0, Padding = new(8, 0, 0, 0), FontSize = 13.5, FontWeight = FontWeights.Medium };
        AutomationProperties.SetAutomationId(all, "HomeSectionsSelectAll");
        all.IsChecked = rows.Count > 0 && rows.Take(MaxSelected).All(row => _selection.Contains(row.Id)) ? true : _selection.Count > 0 ? null : false;
        all.Click += (_, _) =>
        {
            if (_selection.Count > 0 && rows.Take(MaxSelected).All(row => _selection.Contains(row.Id)) || all.IsChecked != true) _selection.Clear();
            else { _selection.Clear(); foreach (var row in rows.Take(MaxSelected)) _selection.Add(row.Id); }
            Render();
        };
        var done = Action(AdminHomeSectionsText.T("common.done"), "HomeSectionsSelectDone"); done.BorderThickness = new(0); done.Click += (_, _) => ExitSelectMode();
        _selectionHeader.Children.Add(all);
        var limit = U.Text(T("web.common.ui.selectMode.rows.limit", new { limit = (long)MaxSelected }), 13, true); Grid.SetColumn(limit, 1); _selectionHeader.Children.Add(limit);
        Grid.SetColumn(done, 2); done.HorizontalAlignment = HorizontalAlignment.Right; _selectionHeader.Children.Add(done);
        // SelectModeBar: the count, then what to do to the picked rows; the labels drop to icons on a phone.
        var tooMany = _selection.Count > MaxSelected; var busy = _controller?.CanWrite != true || _selection.Count == 0 || tooMany;
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = U.Brush("PrimaryTextBrush") };
        count.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = T("web.ui.selectedCount", new { count = (long)_selection.Count, shown = _selection.Count }) });
        if (tooMany) count.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " + T("web.common.ui.selectMode.rows.tooMany", new { limit = (long)MaxSelected }), FontWeight = FontWeights.Normal, Foreground = AdminHomeSectionsUi.Solid(AdminMonitorUi.Warning) });
        AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite); _selectionBar.Children.Add(count);
        void Add(string label, string id, string icon, bool destructive, Func<Task> run)
        {
            var ink = destructive ? AdminHomeSectionsUi.Solid(AdminMonitorUi.Destructive) : U.Brush("PrimaryTextBrush");
            if (destructive) _selectionBar.Children.Add(new Border { Width = 1, Height = 22, Margin = new(4, 0, 4, 0), Background = AdminMonitorUi.Solid(AdminMonitorUi.Border), VerticalAlignment = VerticalAlignment.Center });
            var button = Action(label, id); button.BorderThickness = new(0); button.IsEnabled = !busy;
            button.Content = _phone ? AdminHomeSectionsUi.Icon(icon, 16, ink) : AdminHomeSectionsUi.ActionContent(label, icon, 14, ink);
            AutomationProperties.SetName(button, label); button.Click += async (_, _) => await run(); _selectionBar.Children.Add(button);
        }
        Add(T("web.home.rows.select.turnOn"), "HomeSectionsSelectTurnOn", "eye", false, () => SetSelectedShownAsync(true));
        Add(T("web.home.rows.select.turnOff"), "HomeSectionsSelectTurnOff", "eye-off", false, () => SetSelectedShownAsync(false));
        Add(T("web.home.rows.select.delete"), "HomeSectionsSelectDelete", "trash-2", true, () => DeleteAsync(_selection.ToArray()));
    }

    private async Task SetSelectedShownAsync(bool shown)
    {
        var controller = _controller; if (controller == null || _selection.Count is 0 or > MaxSelected) return;
        AdminHomeSectionsBatchResult? result;
        try { result = await controller.SetShownManyAsync(_selection.ToArray(), shown); }
        catch (Exception) { if (_active) Toasts?.Error(T("web.admin.homeRows.select.changeFailed")); return; }
        if (!_active || result == null) { if (_active && controller.State.Error != null) Toasts?.Error(controller.State.Error); return; }
        ReportShown(result, shown);
    }

    private void ReportShown(AdminHomeSectionsBatchResult result, bool shown)
    {
        var changed = result.ChangedIds.Count; var failures = result.Failures; var attempted = changed + failures.Count;
        string Reason(AdminHomeSectionsRowResult failure) => failure.Outcome == AdminHomeSectionsRowOutcome.Changed
            ? T("web.admin.homeRows.select.rowChanged", new { title = RowTitleOf(failure) })
            : T("web.admin.homeRows.select.rowFailed", new { title = RowTitleOf(failure), message = failure.Message ?? T("web.admin.homeRows.row.saveFailed") });
        var detail = string.Join(" ", failures.Select(Reason));
        // Rows a stopped run never reached are named apart: nothing was sent for them.
        if (result.NotAttempted > 0) detail += " " + AdminHomeSectionsText.NotAttempted(result.NotAttempted);
        if (attempted == 0) Toasts?.Success(T(shown ? "web.admin.homeRows.select.alreadyOn" : "web.admin.homeRows.select.alreadyOff"));
        else if (failures.Count == 0) Toasts?.Success(T(shown ? "web.admin.homeRows.select.turnedOn" : "web.admin.homeRows.select.turnedOff", AdminHomeSectionsText.Count(changed)));
        else if (changed > 0) Toasts?.Warning(T(shown ? "web.admin.homeRows.select.turnedOnPartial" : "web.admin.homeRows.select.turnedOffPartial", AdminHomeSectionsText.Count(attempted, ("done_text", changed))) + " " + detail);
        else Toasts?.Error(T(shown ? "web.admin.homeRows.select.turnOnFailed" : "web.admin.homeRows.select.turnOffFailed", AdminHomeSectionsText.Count(attempted)) + " " + detail);
    }
    private static string RowTitleOf(AdminHomeSectionsRowResult row) => row.Title;

    // ── Dialogs ──────────────────────────────────────────────

    private async Task PeekAsync(AdminHomeSection row)
    {
        var owner = _controller?.State.Owner; var revision = ++_dialogRevision;
        if (owner?.IsCurrent != true || _dialog != null) return;
        try
        {
            var preview = await _api.PreviewAsync(owner, AdminHomeSectionDraft.From(row), owner.Token, _scope);
            if (!_active || !owner.IsCurrent || revision != _dialogRevision) return;
            await ShowAsync(RowTitle(row), PreviewPanel(preview), AdminHomeSectionsText.T("native.homeSections.close"), owner, confirmation: false);
        }
        catch (Exception) { if (_active && owner.IsCurrent) _status.Text = T("web.home.addRow.preview.failed"); }
    }

    private async Task<bool> ShowAsync(string title, object body, string primary, MonitorAuthorityLease owner, bool confirmation = true)
    {
        if (_dialog != null || !_active || !owner.IsCurrent) return false;
        var dialog = new ContentDialog { Title = title, Content = body is string text ? U.Text(text) : body,
            PrimaryButtonText = primary, CloseButtonText = confirmation ? AdminHomeSectionsText.T("common.cancel") : "", DefaultButton = confirmation ? ContentDialogButton.Close : ContentDialogButton.Primary, XamlRoot = XamlRoot };
        _dialog = dialog;
        try { var result = await dialog.ShowAsync(); return _active && owner.IsCurrent && ReferenceEquals(owner, _controller?.State.Owner) && result == ContentDialogResult.Primary; }
        catch (InvalidOperationException) { return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }

    private static string RowTitle(AdminHomeSection row) => SiloPlayer.Core.Localization.AppStrings.ServerText(row.TitleKey, row.TitleArgs, row.Title);

    public async Task<bool> CanLeaveAsync()
    {
        if (!RequiresLeaveConfirmation) return true;
        var result = await U.ConfirmDiscardAsync(this, _controller!.State.Owner!, () => _dialog, dialog => _dialog = dialog);
        if (result == true) { CloseEditor(); return true; }
        return !RequiresLeaveConfirmation;
    }
    public void SuspendAdminPage() => Close();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { Close(); base.OnNavigatedFrom(args); }
    private void Close()
    {
        if (!_active) return; _active = false; _dialogRevision++; _dialog?.Hide(); CloseEditor();
        _authority.AvailabilityChanged -= AuthorityChanged;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= InvalidateHome; _controller.Dispose(); _controller = null; }
        _rows.Clear(); _selection.Clear(); _selectMode = false; _settings = null; _defaults.Children.Clear(); _status.Text = ""; ClearPageNames();
    }
    // Home keeps its page and view model mounted, and a resolved HomeViewModel is a new one. Only the message reaches the mounted Home.
    private void InvalidateHome() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active || _controller?.State.Owner?.IsCurrent != true || App.Services.GetService<HomeApi>() is not { } home) return;
        WeakReferenceMessenger.Default.Send(new SiloPlayer.Messaging.HomePreferenceChanged(home.CaptureContext(), ReloadLayout: true));
    });

    internal static string T(string key, object? args = null) => AdminHomeSectionsText.T(key, args);
    private static Button Action(string label, string id) => U.Action(label, id);
    private static void Menu(MenuFlyout menu, string label, Action action, bool enabled = true, string? glyph = null, string? help = null)
    { var item = Item(label, enabled, glyph, help, false); item.Click += (_, _) => action(); menu.Items.Add(item); }
    private void MenuAsync(MenuFlyout menu, string label, Func<Task> action, bool enabled = true, string? glyph = null, bool destructive = false, string? help = null)
    {
        var item = Item(label, enabled, glyph, help, destructive);
        item.Click += async (_, _) => { try { await action(); } catch (Exception) { if (_active) _status.Text = T("web.admin.homeRows.page.loadRowsFailed"); } }; menu.Items.Add(item);
    }
    /// <summary>One menu entry with its icon; the line of help the official menu shows under More's items is its tooltip and help text.</summary>
    private static MenuFlyoutItem Item(string label, bool enabled, string? glyph, string? help, bool destructive)
    {
        var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled };
        if (glyph != null) item.Icon = new FontIcon { Glyph = glyph, FontSize = 16 };
        if (help != null) { ToolTipService.SetToolTip(item, help); AutomationProperties.SetHelpText(item, help); }
        if (destructive) item.Foreground = AdminHomeSectionsUi.Solid(AdminMonitorUi.Destructive);
        return item;
    }

    /// <summary>The layout facts one row needs from the page: its place in the list and the window's breakpoints.</summary>
    public readonly record struct RowLayout(int Index, bool PreviousOn, bool Narrow, bool Phone, double TitleRoom);

    /// <summary>One list row: the data its template binds to. One view per row id; Update raises a full refresh so every binding follows the saved row.</summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class RowView : System.ComponentModel.INotifyPropertyChanged
    {
        private FrameworkElement? _root;
        private string _artKey = "", _tagKey = "";
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public AdminHomeSection Row { get; private set; } = new();
        public bool SelectMode { get; private set; }
        public bool Selected { get; private set; }
        public bool CanWrite { get; private set; }
        public bool Banner { get; private set; }
        public RowLayout Layout { get; private set; }
        public IReadOnlyList<AdminHomeSectionPart> Parts { get; private set; } = [];

        public void Update(AdminHomeSection row, bool selectMode, bool selected, bool canWrite, bool banner, IReadOnlyList<AdminHomeSectionPart> parts, RowLayout layout)
        {
            Row = row; SelectMode = selectMode; Selected = selected; CanWrite = canWrite; Banner = banner; Parts = parts; Layout = layout;
            // An empty name refreshes every bound property, even those whose value did not change (a switch the admin flipped goes back to the saved value).
            PropertyChanged?.Invoke(this, new(string.Empty));
            Sync();
        }
        public void AttachRoot(FrameworkElement root) { _root = root; _artKey = _tagKey = ""; Sync(); }
        public void DetachRoot(FrameworkElement root) { if (ReferenceEquals(_root, root)) _root = null; }
        /// <summary>Puts the saved values back into the realized controls: a binding does not push an unchanged value over what the admin just flipped.</summary>
        private void Sync()
        {
            if (_root == null) return;
            if (_root.FindName("MetaText") is TextBlock meta) FillMeta(meta);
            if (_root.FindName("CollapsedText") is TextBlock collapsed) FillCollapsed(collapsed);
            if (_root.FindName("ArtHost") is Border art)
            {
                var key = Row.SectionType + "|" + Row.Enabled + "|" + Layout.Phone;
                if (key != _artKey) { _artKey = key; art.Child = AdminHomeSectionsUi.RowArt(Row.SectionType, Layout.Phone, !Row.Enabled); }
            }
            if (_root.FindName("TagHost") is StackPanel tags)
            {
                var show = Row.Featured && Row.Enabled && !Layout.Phone; var key = show ? HeroText : "";
                if (key != _tagKey) { _tagKey = key; tags.Children.Clear(); if (show) tags.Children.Add(AdminHomeSectionsUi.Tag(HeroText, AdminMonitorUi.Warning, "star")); }
            }
            if (_root.FindName("ShownSwitch") is ToggleSwitch toggle && toggle.IsOn != Row.Enabled) toggle.IsOn = Row.Enabled;
            if (_root.FindName("SelectBox") is CheckBox box && box.IsChecked != Selected) box.IsChecked = Selected;
        }
        public string Title => RowTitle(Row);
        public bool Shown => Row.Enabled;
        public string ShownAccessibleLabel => AdminHomeSectionsDescription.SwitchLabel(Title, Row.Enabled);
        public string MenuLabel => T("web.home.rows.page.rowMenu", new { row = Title });
        public string SelectLabel => T("web.admin.homeRows.select.rowLabel", new { title = Title });
        public string HeroText => T(Banner && Row.WebOnly ? "web.home.rows.line.heroBannerWebOnly" : "web.home.rows.line.heroBanner");
        private string HeroShortText => T(Banner && Row.WebOnly ? "web.home.rows.line.webOnlyHero" : "web.home.rows.line.heroBanner");
        public Visibility SelectionVisibility => SelectMode ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GripVisibility => SelectMode ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ExpandedVisibility => Row.Enabled ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CollapsedVisibility => Row.Enabled ? Visibility.Collapsed : Visibility.Visible;
        /// <summary>The hairline above a row: not above the first, a dashed (off) row or the row after one.</summary>
        public Visibility SeparatorVisibility => Layout.Index > 0 && Row.Enabled && Layout.PreviousOn ? Visibility.Visible : Visibility.Collapsed;
        public Thickness RowPadding => Row.Enabled ? new(8, 11, 12, 11) : new(8, 5, 12, 5);
        public Thickness RowMargin => Row.Enabled ? new(0) : new(0, 6, 0, 6);
        public double MenuWidth => Layout.Narrow ? 44 : 36;
        public double TitleMaxWidth => Layout.TitleRoom;
        private string TitleCountSuffix => !AdminHomeSectionsKinds.HoldsCollections(Row.SectionType) && Row.ItemLimit > 0 ? AdminHomeSectionsDescription.TitleCount(Row.ItemLimit) : "";
        /// <summary>The dot between the parts of a meta line: a quiet separator.</summary>
        private static Microsoft.UI.Xaml.Documents.Run Dot() => new() { Text = " · ", Foreground = AdminHomeSectionsUi.Themed("SecondaryTextBrush"), FontWeight = FontWeights.Normal };
        private void FillCollapsed(TextBlock text)
        {
            text.Inlines.Clear(); if (Row.Enabled) return;
            text.Inlines.Add(new Run { Text = Title, FontWeight = FontWeights.SemiBold, Foreground = AdminHomeSectionsUi.Solid(AdminMonitorUi.Foreground, .8) });
            text.Inlines.Add(new Run { Text = AdminHomeSectionsDescription.CollapsedText() });
        }
        private void FillMeta(TextBlock meta)
        {
            meta.Inlines.Clear();
            if (!Row.Enabled) return;
            // Phones have no room for the tag next to the name: the hero label leads the line instead.
            if (Layout.Phone && Row.Featured) { meta.Inlines.Add(new Run { Text = HeroShortText, FontWeight = FontWeights.SemiBold, Foreground = AdminHomeSectionsUi.Solid(AdminMonitorUi.Warning) }); meta.Inlines.Add(Dot()); }
            foreach (var part in Parts)
            {
                var run = new Run { Text = part.Text };
                // The emphasized words read at the full foreground, over the muted rest of the line.
                if (part.Style == AdminHomeSectionPartStyle.Strong) { run.FontWeight = FontWeights.Medium; run.Foreground = AdminHomeSectionsUi.Themed("PrimaryTextBrush"); }
                else if (part.Style == AdminHomeSectionPartStyle.Warning) { run.FontWeight = FontWeights.Medium; run.Foreground = AdminHomeSectionsUi.Solid(AdminMonitorUi.Warning); }
                meta.Inlines.Add(run);
            }
            if (TitleCountSuffix.Length > 0) { meta.Inlines.Add(Dot()); meta.Inlines.Add(new Run { Text = TitleCountSuffix }); }
        }
    }
}
