using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.ApplicationModel.DataTransfer;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

/// <summary>The Home Sections route, including its library-page tabs. No timers, catalog walks or entrance transitions.</summary>
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
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, AllowDrop = true,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, CanDragItems = true, CanReorderItems = true };
    private readonly ObservableCollection<RowView> _rows = [];
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
        _add.Click += (_, _) => OpenAdd(); _more.Click += MoreClick;
        _body.Children.Add(heading); _body.Children.Add(_pages); AutomationProperties.SetAutomationId(_pages, "HomeSectionsPageSelector");
        AutomationProperties.SetName(_pages, T("web.home.rows.page.pageSwitcher"));
        _pages.Items.Add(new ComboBoxItem { Content = "Home", Tag = AdminHomeSectionScope.Home }); _pages.SelectedIndex = 0; _pages.SelectionChanged += PageChanged;
        _body.Children.Add(_summary); _body.Children.Add(_alerts); _body.Children.Add(_status);
        AutomationProperties.SetAutomationId(_list, "HomeSectionsRows"); AutomationProperties.SetName(_list, "Home rows");
        _list.ItemTemplate = (DataTemplate)Resources["HomeSectionRowTemplate"]; _list.ItemsSource = _rows;
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
        _body.Children.Add(_list); _body.Children.Add(U.Text("Drag a row to move it, or use Move up / Move down in its menu. Changes save right away.", 12, true));
        _body.Children.Add(_selectionBar); _body.Children.Add(_defaults);
        _scroll.Content = _body; _root.Children.Add(_scroll); Content = _root;
        Loaded += (_, _) => Activate(); Unloaded += (_, _) => Close(); SizeChanged += (_, _) => Fit();
        KeyDown += (_, args) => { if (args.Key == Windows.System.VirtualKey.Escape && _selectMode && _editor == null) { _selectMode = false; _selection.Clear(); Render(); args.Handled = true; } };
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
    private void Render()
    {
        if (_controller == null) return; var state = _controller.State;
        var current = state.Owner?.IsCurrent == true; var canWrite = _controller.CanWrite;
        if (!current) { CloseEditor(); _dialog?.Hide(); _selection.Clear(); _settings = null; _defaults.Children.Clear(); ClearPageNames(); }
        _add.IsEnabled = canWrite && state.Catalog != null && _editor == null; _more.IsEnabled = canWrite && _editor == null;
        _status.Text = state.Busy ? T("web.admin.homeRows.page.loadingRows") : state.Error ?? (!current ? "Administrator access is required." : "");
        _alerts.Children.Clear();
        if (state.Error != null || state.ReviewRequired || state.Snapshot == null && !state.Busy && current)
        {
            if (state.ReviewRequired) _alerts.Children.Add(U.Text(state.OutcomeUnknown ? new AdminHomeSectionsUnknownException().Message : new AdminHomeSectionsConflictException().Message, 13));
            var reload = Action(T("web.home.rows.reloadRows"), "HomeSectionsReload"); reload.IsEnabled = !state.Busy;
            reload.Click += async (_, _) => { if (!await CanLeaveAsync()) return; CloseEditor(); _selection.Clear(); await _controller.ReloadAsync(); await LoadPagesAsync(); await LoadDefaultsAsync(); };
            _alerts.Children.Add(reload);
        }
        var rows = current ? state.Snapshot?.Rows ?? [] : [];
        _summary.Text = state.Snapshot == null ? "" : T("web.home.rows.page.summaryAdmin", new { row_count = rows.Count, shown_count = rows.Count(row => row.Enabled) });
        _list.CanDragItems = _list.CanReorderItems = canWrite && !_selectMode; _list.IsEnabled = !state.Busy && current;
        _pages.IsEnabled = !state.Busy && current && _editor == null;
        // An in-flight drag retains its captured complete snapshot. Any replacement invalidates it.
        if (!ReferenceEquals(_dragSnapshot, state.Snapshot)) _dragSnapshot = null;
        _rows.Clear();
        var banner = rows.FirstOrDefault(row => row.Featured && row.Enabled)?.Id;
        foreach (var row in rows)
        {
            var source = row.Config.ValueKind == System.Text.Json.JsonValueKind.Object && row.SectionType == "collection" && row.Config.TryGetProperty("library_collection_id", out var id)
                ? " · " + _collectionLabels.GetValueOrDefault(id.ToString(), "Unavailable collection") : "";
            _rows.Add(new(row, _selectMode, _selection.Contains(row.Id), canWrite, row.Id == banner, source));
        }
        if (rows.Count == 0 && state.Snapshot != null && !state.Busy) _status.Text = T("web.home.rows.page.empty", new { page = PageLabel });
        _selection.IntersectWith(rows.Select(row => row.Id)); RenderSelection();
        if (_editor != null)
        {
            RefreshSave(); _editorFields.IsEnabled = !state.Busy;
            _editorBack.IsEnabled = _editorDelete.IsEnabled = canWrite;
            if (state.Error != null) _editorNotice.Text = state.Error;
        }
    }
    private void MoreClick(object sender, RoutedEventArgs args)
    {
        var menu = new MenuFlyout();
        Menu(menu, T("web.admin.homeRows.more.select"), () => { _selectMode = true; Render(); });
        MenuAsync(menu, T("web.admin.homeRows.more.restore"), RestoreAsync);
        menu.ShowAt(_more);
    }
    private void SelectionClick(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { DataContext: RowView row } box) return;
        if (box.IsChecked == true && _selection.Count < 100) _selection.Add(row.Row.Id); else { _selection.Remove(row.Row.Id); box.IsChecked = false; }
        RenderSelection();
    }
    private async void ShownClick(object sender, RoutedEventArgs args)
    { if (sender is Button { DataContext: RowView row } && _controller != null) await _controller.QuickAsync(row.Row, shown: !row.Row.Enabled); }
    private void RowMenuClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RowView row } button || _controller == null) return;
        var menu = new MenuFlyout();
        MenuAsync(menu, T("web.admin.homeRows.rowMenu.edit"), async () => await OpenEditAsync(row.Row.Id));
        MenuAsync(menu, T(row.Row.Featured ? "web.admin.homeRows.rowMenu.stopUsingAsHero" : "web.admin.homeRows.rowMenu.useAsHero"),
            async () => await _controller.QuickAsync(row.Row, hero: !row.Row.Featured), row.Row.SectionType != "collection_posters" || row.Row.Featured);
        var index = _rows.IndexOf(row);
        MenuAsync(menu, "Move up", async () => await MoveAsync(row.Row.Id, index - 1), index > 0);
        MenuAsync(menu, "Move down", async () => await MoveAsync(row.Row.Id, index + 1), index < _rows.Count - 1);
        MenuAsync(menu, T("web.home.rows.page.moveToTop"), async () => await MoveAsync(row.Row.Id, 0), index > 0);
        MenuAsync(menu, T("web.home.rows.page.moveToBottom"), async () => await MoveAsync(row.Row.Id, _rows.Count - 1), index < _rows.Count - 1);
        MenuAsync(menu, "Preview", async () => await PeekAsync(row.Row), _controller.State.Capabilities?.Preview == true);
        if (CopyTargets(AdminHomeSectionDraft.From(row.Row)).Count > 0)
            MenuAsync(menu, "Add to other libraries…", async () => await CopyAsync(row.Row));
        MenuAsync(menu, T("web.admin.homeRows.rowMenu.delete"), async () => await DeleteAsync([row.Row.Id]));
        menu.ShowAt(button);
    }
    private async Task MoveAsync(string id, int target)
    {
        if (_controller?.State.Snapshot is not { } snapshot || !_controller.CanWrite || target < 0 || target >= snapshot.Rows.Count) return;
        var ids = snapshot.Rows.Select(row => row.Id).ToList(); ids.Remove(id); ids.Insert(target, id);
        await _controller.ReorderAsync(snapshot, ids);
    }
    private void RenderSelection()
    {
        _selectionBar.Children.Clear(); if (!_selectMode) return;
        _selectionBar.Children.Add(U.Text($"{_selection.Count} selected", 13));
        void Add(string label, Func<Task> action, bool enabled = true)
        { var button = Action(label, "HomeSectionsSelect" + label.Replace(" ", "")); button.IsEnabled = _controller?.CanWrite == true && enabled; button.Click += async (_, _) => await action(); _selectionBar.Children.Add(button); }
        Add("Select all", () => { _selection.Clear(); foreach (var row in _rows.Take(100)) _selection.Add(row.Row.Id); Render(); return Task.CompletedTask; });
        Add("Turn on", () => SetSelectedShownAsync(true), _selection.Count > 0);
        Add("Turn off", () => SetSelectedShownAsync(false), _selection.Count > 0);
        Add("Delete", () => DeleteAsync(_selection.ToArray()), _selection.Count > 0);
        Add("Done", () => { _selectMode = false; _selection.Clear(); Render(); return Task.CompletedTask; });
    }
    private async Task SetSelectedShownAsync(bool shown)
    {
        if (_controller == null) return;
        try { var controller = _controller; var sessions = await controller.PrepareDeletesAsync(_selection.ToArray());
            if (ReferenceEquals(controller, _controller)) await controller.SetShownManyAsync(sessions, shown); }
        catch (Exception) { if (_active) _status.Text = "Unable to read selected rows. Reload and review."; }
    }
    private async Task DeleteAsync(IReadOnlyList<string> ids)
    {
        var controller = _controller; if (controller?.CanWrite != true || _dialog != null) return;
        var request = _editor == null ? ++_dialogRevision : _dialogRevision;
        try
        {
            var sessions = await controller.PrepareDeletesAsync(ids);
            if (request != _dialogRevision || !_active || !ReferenceEquals(controller, _controller)) return;
            var names = string.Join("\n", sessions.Select(session => session.Row.Title));
            if (await ConfirmAsync(ids.Count == 1 ? "Delete row?" : $"Delete {ids.Count} rows?", names + $"\n\nThis removes the rows from {PageLabel} for every profile. Your media and collections are kept.", "Delete", sessions[0].Owner) && controller.CanWrite)
            { if (await controller.DeleteAsync(sessions)) { _selection.Clear(); CloseEditor(); } }
        }
        catch (Exception) { if (_active && request == _dialogRevision) _status.Text = "Unable to read the rows for deletion. Reload and review."; }
    }
    private async Task RestoreAsync()
    {
        var controller = _controller; if (controller?.CanWrite != true || _dialog != null) return;
        await controller.ReloadAsync();
        if (!_active || !ReferenceEquals(controller, _controller) || controller.State.Snapshot is not { } snapshot || !controller.CanWrite) return;
        var reset = new CheckBox { Content = "Also reset every profile's Home customizations", IsEnabled = controller.State.Capabilities?.ResetProfiles == true };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(U.Text($"This replaces {snapshot.Rows.Count} rows on {PageLabel}, including disabled rows. Collections and media are kept.", 14));
        foreach (var row in snapshot.Rows.Where(row => row.SectionType is "collection" or "collection_posters")) body.Children.Add(U.Text(row.Title, 13, true));
        body.Children.Add(reset);
        var all = new CheckBox { Content = _scope.Kind == "home" ? "Also restore every library page" : "Also restore the other library pages" };
        body.Children.Add(all); body.Children.Add(U.Text("Pages restore one at a time. If a page changes or its result cannot be confirmed, the operation stops; reload and review all affected pages.", 12, true));
        if (await ConfirmAsync($"Restore {PageLabel} to the default rows?", body, "Restore defaults", controller.State.Owner!) && controller.CanWrite)
            await controller.RestoreAsync(snapshot, reset.IsChecked == true, all.IsChecked == true ? _libraries.Select(library => new AdminHomeSectionScope("library", library.Id)).Where(scope => scope != _scope).ToArray() : null);
    }
    private async Task PeekAsync(AdminHomeSection row)
    {
        var owner = _controller?.State.Owner; var revision = ++_dialogRevision;
        if (owner?.IsCurrent != true || _dialog != null) return;
        try
        {
            var preview = await _api.PreviewAsync(owner, AdminHomeSectionDraft.From(row), owner.Token, _scope);
            if (!_active || !owner.IsCurrent || revision != _dialogRevision) return;
            var panel = PreviewPanel(preview); await ConfirmAsync(row.Title, panel, "Close", owner, confirmation: false);
        }
        catch (Exception) { if (_active && owner.IsCurrent) _status.Text = "Couldn't load the preview. Try again."; }
    }
    private async Task<bool> ConfirmAsync(string title, object body, string primary, MonitorAuthorityLease owner, bool confirmation = true)
    {
        if (_dialog != null || !_active || !owner.IsCurrent) return false;
        var dialog = new ContentDialog { Title = title, Content = body is string text ? U.Text(text) : body,
            PrimaryButtonText = primary, CloseButtonText = confirmation ? "Cancel" : "", DefaultButton = confirmation ? ContentDialogButton.Close : ContentDialogButton.Primary, XamlRoot = XamlRoot };
        _dialog = dialog;
        try { var result = await dialog.ShowAsync(); return _active && owner.IsCurrent && ReferenceEquals(owner, _controller?.State.Owner) && result == ContentDialogResult.Primary; }
        catch (InvalidOperationException) { return false; }
        finally { if (ReferenceEquals(_dialog, dialog)) _dialog = null; }
    }
    public async Task<bool> CanLeaveAsync()
    {
        if (!RequiresLeaveConfirmation) return true;
        if (await ConfirmAsync("Discard changes?", "Your unsaved row changes will be lost.", "Discard", _controller!.State.Owner!)) { CloseEditor(); return true; }
        return !RequiresLeaveConfirmation;
    }
    public void SuspendAdminPage() => Close();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { Close(); base.OnNavigatedFrom(args); }
    private void Close()
    {
        if (!_active) return; _active = false; _dialogRevision++; _dialog?.Hide(); CloseEditor();
        _authority.AvailabilityChanged -= AuthorityChanged;
        if (_controller != null) { _controller.Changed -= QueueRender; _controller.Saved -= InvalidateHome; _controller.Dispose(); _controller = null; }
        _rows.Clear(); _selection.Clear(); _settings = null; _defaults.Children.Clear(); _status.Text = ""; ClearPageNames();
    }
    private void InvalidateHome() => DispatcherQueue.TryEnqueue(() =>
    { if (_active && _controller?.State.Owner?.IsCurrent == true) { App.Services.GetService<SiloPlayer.ViewModels.HomeViewModel>()?.InvalidateCache(); if (_controller.State.Error == null) _status.Text = "Rows updated."; } });
    internal static string T(string key, object? args = null)
    {
        var values = args?.GetType().GetProperties().ToDictionary(property => property.Name, property => property.GetValue(args));
        if (AdminLabels.TryGetValue(key, out var text)) return AdminText.Format(text, values ?? new Dictionary<string, object?>());
        return SiloPlayer.Core.Localization.AppStrings.Get(key, values, key);
    }
    private static readonly Dictionary<string, string> AdminLabels = new()
    {
        ["web.admin.homeRows.page.title"] = "Sections",
        ["web.admin.homeRows.page.subtitle"] = "The sections everyone sees on Home and on library pages. Profiles can still hide, rename or reorder them.",
        ["web.admin.homeRows.page.loadingRows"] = "Loading the current rows…",
        ["web.admin.homeRows.more.select"] = "Select rows",
        ["web.admin.homeRows.more.restore"] = "Restore defaults…",
        ["web.admin.homeRows.rowMenu.edit"] = "Edit row…",
        ["web.admin.homeRows.rowMenu.delete"] = "Delete row…",
        ["web.admin.homeRows.rowMenu.useAsHero"] = "Use as hero banner",
        ["web.admin.homeRows.rowMenu.stopUsingAsHero"] = "Stop using as hero banner"
    };
    private static Button Action(string label, string id) => U.Action(label, id);
    private static void Menu(MenuFlyout menu, string label, Action action, bool enabled = true)
    { var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    private void MenuAsync(MenuFlyout menu, string label, Func<Task> action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = label, IsEnabled = enabled };
        item.Click += async (_, _) => { try { await action(); } catch (Exception) { if (_active) _status.Text = "Unable to complete this action. Reload and review the current rows."; } }; menu.Items.Add(item);
    }
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed record RowView(AdminHomeSection Row, bool SelectMode, bool Selected, bool CanWrite, bool Banner, string Source)
    {
        public string Title => SiloPlayer.Core.Localization.AppStrings.ServerText(Row.TitleKey, Row.TitleArgs, Row.Title);
        public string Description => $"{KindLabel(Row.SectionType)} · {Row.ItemLimit} titles" + Source + (Row.Featured ? " · Hero banner" : "") + (Banner && Row.WebOnly ? " · Web only" : "");
        public string ShownLabel => Row.Enabled ? "On" : "Off";
        public string ShownAccessibleLabel => (Row.Enabled ? "Turn off " : "Turn on ") + Row.Title;
        public string MenuLabel => "Actions for " + Row.Title;
        public Visibility SelectionVisibility => SelectMode ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GripVisibility => SelectMode ? Visibility.Collapsed : Visibility.Visible;
    }
}
