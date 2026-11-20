using System.Collections.ObjectModel;
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
/// Official source: 217ff25 web/src/pages/AdminHomeRows.tsx and web/src/components/homeRows.
/// </summary>
public sealed partial class AdminHomeSectionsPage : Page, IAdminGutterScrollPage, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly AdminHomeSectionsApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminServerSettingsApi _settingsApi;
    private readonly Grid _root = new();
    private readonly StackPanel _body = new() { Spacing = 20, Padding = new(0, 0, 0, 24) };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _alerts = new() { Spacing = 8 };
    private readonly TextBlock _status = U.Text("", 13, true);
    private readonly TextBlock _summary = U.Text("", 13, true);
    private readonly TextBlock _libraryNote = U.Text("", 13, true);
    private readonly TextBlock _reorderHint = U.Text("", 13, true);
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, AllowDrop = true,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, CanDragItems = true, CanReorderItems = true };
    private readonly ObservableCollection<RowView> _rows = [];
    private readonly StackPanel _selectionHeader = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly StackPanel _selectionBar = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _defaults = new() { Spacing = 12 };
    private readonly Button _add = Action(T("web.home.rows.page.addRow"), "HomeSectionsAdd");
    private readonly Button _more = Action("⋯", "HomeSectionsMore");
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
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 8 };
        titles.Children.Add(U.Title(T("web.admin.homeRows.page.title")));
        titles.Children.Add(U.Text(T("web.admin.homeRows.page.subtitle"), 14, true)); heading.Children.Add(titles);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(_more); actions.Children.Add(_add); Grid.SetColumn(actions, 1); heading.Children.Add(actions);
        AutomationProperties.SetName(_more, T("web.home.rows.page.actionsRegion"));
        _add.Click += (_, _) => OpenAdd(); _more.Click += MoreClick;
        _body.Children.Add(heading); _body.Children.Add(_pages); AutomationProperties.SetAutomationId(_pages, "HomeSectionsPageSelector");
        AutomationProperties.SetName(_pages, T("web.home.rows.page.pageSwitcher"));
        _pages.Items.Add(new ComboBoxItem { Content = T("web.home.rows.pages.home"), Tag = AdminHomeSectionScope.Home }); _pages.SelectedIndex = 0; _pages.SelectionChanged += PageChanged;
        _body.Children.Add(_summary); _body.Children.Add(_libraryNote); _body.Children.Add(_alerts); _body.Children.Add(_status);
        AutomationProperties.SetAutomationId(_list, "HomeSectionsRows");
        _list.ItemTemplate = (DataTemplate)Resources["HomeSectionRowTemplate"]; _list.ItemsSource = _rows;
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
        _body.Children.Add(_selectionHeader); _body.Children.Add(_list); _body.Children.Add(_reorderHint);
        _body.Children.Add(_selectionBar); _body.Children.Add(_defaults);
        _scroll.Content = _body; _root.Children.Add(_scroll); Content = _root;
        Loaded += (_, _) => Activate(); Unloaded += (_, _) => Close(); SizeChanged += (_, _) => Fit();
        KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Escape && _selectMode && _editor == null && _dialog == null) { ExitSelectMode(); args.Handled = true; } };
    }

    private void Fit()
    {
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
        _alerts.Children.Clear();
        if (current && state.Failure == AdminHomeSectionsFailure.Conflict)
            _alerts.Children.Add(Banner(T("web.home.rows.notes.conflict"), state.Busy));
        else if (current && state.Failure == AdminHomeSectionsFailure.Unknown)
            _alerts.Children.Add(Banner(state.Error ?? new AdminHomeSectionsUnknownException().Message, state.Busy));
        else if (current && (state.Failure == AdminHomeSectionsFailure.Load || state.Snapshot == null && !state.Busy))
            _alerts.Children.Add(Banner(state.Error ?? T("web.home.rows.page.loadFailed"), state.Busy));
        else if (current && _librariesFailed)
            _alerts.Children.Add(Banner("Library pages could not be loaded. Home is available; reload to try again.", state.Busy));
        _status.Text = !current ? "Administrator access is required."
            : state.Busy ? (state.Snapshot == null ? T("web.home.rows.page.loading") : T("web.admin.homeRows.page.loadingRows"))
            : state.Failure == AdminHomeSectionsFailure.Refused ? state.Error ?? ""
            : state.Snapshot != null && rows.Count == 0 ? T("web.home.rows.page.empty", new { page = PageLabel }) : "";
        _summary.Text = state.Snapshot == null || !current ? "" : T("web.home.rows.page.summaryAdmin", new { row_count = (long)rows.Count, shown_count = rows.Count(row => row.Enabled) });
        _libraryNote.Text = current && _scope.Kind == "library" ? T("web.home.rows.notes.libraryPage", new { library = PageLabel }) : "";
        _libraryNote.Visibility = _libraryNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _reorderHint.Text = state.Snapshot != null && rows.Count > 0 && !_selectMode ? T("web.home.rows.notes.reorderHint") : "";
        _reorderHint.Visibility = _reorderHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.CanDragItems = _list.CanReorderItems = canWrite && !_selectMode; _list.IsEnabled = !state.Busy && current;
        AutomationProperties.SetName(_list, T("web.home.rows.page.listLabel", new { page = PageLabel }));
        _pages.IsEnabled = !state.Busy && current && _editor == null;
        // An in-flight drag retains its captured complete snapshot. Any replacement invalidates it.
        if (!ReferenceEquals(_dragSnapshot, state.Snapshot)) _dragSnapshot = null;
        var fresh = new List<RowView>();
        var existing = _rows.ToDictionary(view => view.Row.Id, StringComparer.Ordinal);
        var banner = rows.FirstOrDefault(row => row.Featured && row.Enabled)?.Id;
        foreach (var row in rows)
            {
                var view = existing.TryGetValue(row.Id, out var known) ? known : new RowView();
                view.Update(row, _selectMode, _selection.Contains(row.Id), canWrite, row.Id == banner, DescribeRow(row)); fresh.Add(view);
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

    private FrameworkElement Banner(string message, bool busy)
    {
        var reload = Action(T("web.home.rows.reloadRows"), "HomeSectionsReload"); reload.IsEnabled = !busy;
        reload.Click += async (_, _) => await ReloadAllAsync();
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(U.Alert(message)); panel.Children.Add(reload);
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
        var menu = new MenuFlyout();
        Menu(menu, T("web.admin.homeRows.more.select"), () => { _selectMode = true; Render(); FocusSelectAll(); }, !_selectMode && rows.Count > 0);
        MenuAsync(menu, T("web.admin.homeRows.more.restore"), RestoreAsync);
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
        MenuAsync(menu, T("web.admin.homeRows.rowMenu.edit"), async () => await OpenEditAsync(row.Row.Id), canWrite);
        MenuAsync(menu, T(row.Row.Featured ? "web.admin.homeRows.rowMenu.stopUsingAsHero" : "web.admin.homeRows.rowMenu.useAsHero"),
            async () => await _controller.QuickAsync(row.Row, hero: !row.Row.Featured), canWrite && (row.Row.Featured || !AdminHomeSectionsKinds.HoldsCollections(row.Row.SectionType)));
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuAsync(menu, T("web.home.rows.page.moveToTop"), async () => await MoveAsync(row.Row.Id, 0), canWrite && index > 0);
        MenuAsync(menu, T("web.home.rows.page.moveToBottom"), async () => await MoveAsync(row.Row.Id, _rows.Count - 1), canWrite && index < _rows.Count - 1);
        // Native keyboard path for ordering; the WebUI's handle-and-arrow drag has no native list equivalent.
        MenuAsync(menu, "Move up", async () => await MoveAsync(row.Row.Id, index - 1), canWrite && index > 0);
        MenuAsync(menu, "Move down", async () => await MoveAsync(row.Row.Id, index + 1), canWrite && index < _rows.Count - 1);
        if (_controller.State.Capabilities?.Preview == true) MenuAsync(menu, "Preview", async () => await PeekAsync(row.Row));
        if (_scope.Kind == "library" && AdminHomeSectionDraft.From(row.Row) is { CanCopy: true } copyable && CopyTargets(copyable).Count > 0)
            MenuAsync(menu, T("web.admin.homeRows.rowMenu.addToOtherLibraries"), async () => await CopyAsync(row.Row), canWrite);
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuAsync(menu, T("web.admin.homeRows.rowMenu.delete"), async () => await DeleteAsync([row.Row.Id]), canWrite);
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
        _selectionHeader.Children.Clear(); _selectionBar.Children.Clear();
        _selectionHeader.Visibility = _selectionBar.Visibility = _selectMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_selectMode) return;
        var rows = _controller?.State.Snapshot?.Rows ?? [];
        var all = new CheckBox { Content = AdminHomeSectionsText.T("web.ui.selectMode.selectAll"), MinWidth = 0 };
        AutomationProperties.SetAutomationId(all, "HomeSectionsSelectAll");
        all.IsChecked = rows.Count > 0 && rows.Take(MaxSelected).All(row => _selection.Contains(row.Id)) ? true : _selection.Count > 0 ? null : false;
        all.Click += (_, _) =>
        {
            if (_selection.Count > 0 && rows.Take(MaxSelected).All(row => _selection.Contains(row.Id)) || all.IsChecked != true) _selection.Clear();
            else { _selection.Clear(); foreach (var row in rows.Take(MaxSelected)) _selection.Add(row.Id); }
            Render();
        };
        var done = Action(AdminHomeSectionsText.T("common.done"), "HomeSectionsSelectDone"); done.Click += (_, _) => ExitSelectMode();
        _selectionHeader.Children.Add(all);
        _selectionHeader.Children.Add(U.Text(T("web.common.ui.selectMode.rows.limit", new { limit = (long)MaxSelected }), 12, true)); _selectionHeader.Children.Add(done);
        var tooMany = _selection.Count > MaxSelected; var busy = _controller?.CanWrite != true || _selection.Count == 0 || tooMany;
        var count = U.Text(T("web.ui.selectedCount", new { count = (long)_selection.Count, shown = _selection.Count }), 13);
        AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite); _selectionBar.Children.Add(count);
        if (tooMany) _selectionBar.Children.Add(U.Text(T("web.common.ui.selectMode.rows.tooMany", new { limit = (long)MaxSelected }), 12));
        void Add(string label, string id, Func<Task> run)
        { var button = Action(label, id); button.IsEnabled = !busy; button.Click += async (_, _) => await run(); _selectionBar.Children.Add(button); }
        Add(T("web.home.rows.select.turnOn"), "HomeSectionsSelectTurnOn", () => SetSelectedShownAsync(true));
        Add(T("web.home.rows.select.turnOff"), "HomeSectionsSelectTurnOff", () => SetSelectedShownAsync(false));
        Add(T("web.home.rows.select.delete"), "HomeSectionsSelectDelete", () => DeleteAsync(_selection.ToArray()));
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
            await ShowAsync(RowTitle(row), PreviewPanel(preview), "Close", owner, confirmation: false);
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
    private void InvalidateHome() => DispatcherQueue.TryEnqueue(() =>
    { if (_active && _controller?.State.Owner?.IsCurrent == true) App.Services.GetService<SiloPlayer.ViewModels.HomeViewModel>()?.InvalidateCache(); });

    internal static string T(string key, object? args = null) => AdminHomeSectionsText.T(key, args);
    private static Button Action(string label, string id) => U.Action(label, id);
    private static void Menu(MenuFlyout menu, string label, Action action, bool enabled = true)
    { var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private void MenuAsync(MenuFlyout menu, string label, Func<Task> action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled };
        item.Click += async (_, _) => { try { await action(); } catch (Exception) { if (_active) _status.Text = T("web.admin.homeRows.page.loadRowsFailed"); } }; menu.Items.Add(item);
    }

    /// <summary>One list row: the data its template binds to. One view per row id; Update raises a full refresh so every binding follows the saved row.</summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class RowView : System.ComponentModel.INotifyPropertyChanged
    {
        private FrameworkElement? _root;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public AdminHomeSection Row { get; private set; } = new();
        public bool SelectMode { get; private set; }
        public bool Selected { get; private set; }
        public bool CanWrite { get; private set; }
        public bool Banner { get; private set; }
        public IReadOnlyList<AdminHomeSectionPart> Parts { get; private set; } = [];

        public void Update(AdminHomeSection row, bool selectMode, bool selected, bool canWrite, bool banner, IReadOnlyList<AdminHomeSectionPart> parts)
        {
            Row = row; SelectMode = selectMode; Selected = selected; CanWrite = canWrite; Banner = banner; Parts = parts;
            // An empty name refreshes every bound property, even those whose value did not change (a switch the admin flipped goes back to the saved value).
            PropertyChanged?.Invoke(this, new(string.Empty));
            Sync();
        }
        public void AttachRoot(FrameworkElement root) { _root = root; Sync(); }
        public void DetachRoot(FrameworkElement root) { if (ReferenceEquals(_root, root)) _root = null; }
        /// <summary>Puts the saved values back into the realized controls: a binding does not push an unchanged value over what the admin just flipped.</summary>
        private void Sync()
        {
            if (_root == null) return;
            if (_root.FindName("MetaText") is TextBlock meta) FillMeta(meta);
            if (_root.FindName("ShownSwitch") is ToggleSwitch toggle && toggle.IsOn != Row.Enabled) toggle.IsOn = Row.Enabled;
            if (_root.FindName("SelectBox") is CheckBox box && box.IsChecked != Selected) box.IsChecked = Selected;
        }
        public string Title => RowTitle(Row);
        public bool Shown => Row.Enabled;
        public string ShownAccessibleLabel => AdminHomeSectionsDescription.SwitchLabel(Title, Row.Enabled);
        public string MenuLabel => T("web.home.rows.page.rowMenu", new { row = Title });
        public string SelectLabel => T("web.admin.homeRows.select.rowLabel", new { title = Title });
        public string HeroText => T(Banner && Row.WebOnly ? "web.home.rows.line.heroBannerWebOnly" : "web.home.rows.line.heroBanner");
        public Visibility HeroVisibility => Row.Featured ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SelectionVisibility => SelectMode ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GripVisibility => SelectMode ? Visibility.Collapsed : Visibility.Visible;
        public Visibility TitleVisibility => Row.Enabled ? Visibility.Visible : Visibility.Collapsed;
        private string TitleCountSuffix => !AdminHomeSectionsKinds.HoldsCollections(Row.SectionType) && Row.ItemLimit > 0 ? " · " + AdminHomeSectionsDescription.TitleCount(Row.ItemLimit) : "";
        private void FillMeta(TextBlock meta)
        {
            meta.Inlines.Clear();
            if (!Row.Enabled)
            {
                meta.Inlines.Add(new Run { Text = Title, FontWeight = FontWeights.SemiBold }); meta.Inlines.Add(new Run { Text = AdminHomeSectionsDescription.CollapsedText() });
                return;
            }
            foreach (var part in Parts)
            {
                var run = new Run { Text = part.Text };
                if (part.Style == AdminHomeSectionPartStyle.Strong) run.FontWeight = FontWeights.Medium;
                else if (part.Style == AdminHomeSectionPartStyle.Warning) { run.FontWeight = FontWeights.Medium; run.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)); }
                meta.Inlines.Add(run);
            }
            if (TitleCountSuffix.Length > 0) meta.Inlines.Add(new Run { Text = TitleCountSuffix });
        }
    }
}
