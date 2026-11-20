using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>Manage › Libraries › Collections and /new, /:id/edit. Viewer pages remain separate.</summary>
public sealed partial class AdminCollectionsPage : Page, IAdminNavigationGuard, IAdminPageSuspension
{
    private readonly AdminCollectionsApi _api;
    public AdminCollectionsController Controller { get; }
    private readonly StackPanel _body = Stack(20);
    private readonly StackPanel _surface = Stack(16);
    private readonly StackPanel _rows = Stack(8);
    private readonly TextBlock _notice = Text("");
    private readonly ScrollViewer _scroll;
    private readonly AdminLeaveGuard _leave;
    private AdminRouteRequest _route = new(AdminRoute.Collections);
    private ContentDialog? _dialog;
    private string? _library;
    private string _kind = "all", _search = "";
    private bool _arrange, _failed, _selecting, _active;
    private int _page, _operation, _initialization;
    private readonly HashSet<string> _selected = [];
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _jobTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public AdminCollectionsPage() : this(App.Services.GetRequiredService<AdminCollectionsApi>(),
        App.Services.GetRequiredService<AdminLibrariesApi>(), App.Services.GetRequiredService<AdminMonitorAuthority>()) { }
    public AdminCollectionsPage(AdminCollectionsApi api, AdminLibrariesApi libraries, AdminMonitorAuthority authority)
    {
        InitializeComponent(); _api = api; Controller = new(api, libraries, authority);
        NavigationCacheMode = NavigationCacheMode.Disabled;
        AutomationProperties.SetAutomationId(this, "AdminCollectionsPage");
        _leave = new(() => Controller.Owner, () => Controller.Dirty || _artwork.Count > 0 || _pendingTitles.Count > 0, () => { Controller.Discard(); ClearStaged(); });
        _body.Margin = new(24, 24, 24, 32);
        _notice.TextWrapping = TextWrapping.Wrap; _notice.Visibility = Visibility.Collapsed;
        _notice.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning);
        _body.Children.Add(_notice); _body.Children.Add(_surface);
        _scroll = new() { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Content = _scroll;
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); _page = 0; _selected.Clear(); ReplaceQuery(); RenderRows(); };
        _jobTimer.Tick += async (_, _) => { _jobTimer.Stop(); await PollJobAsync(); };
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); try { await RefreshPreviewAsync(); } catch (Exception) { } };
        Controller.Retired += Retired;
        Unloaded += (_, _) => { SuspendAdminPage(); Controller.Retired -= Retired; Controller.Dispose(); };
        SizeChanged += (_, args) => _body.Margin = new(args.NewSize.Width < 640 ? 12 : 24, 24, args.NewSize.Width < 640 ? 12 : 24, 32);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e); _route = e.Parameter as AdminRouteRequest ?? new(AdminRoute.Collections);
        _library = _route.Get("libraryId"); _arrange = _route.Get("view") == "arrange" || _route.Get("view") == null && _library != null;
        _kind = _route.Get("kind") ?? "all"; _search = _route.Get("q") ?? ""; _failed = _route.Get("failed") == "1";
        _active = true; Controller.Open(); _ = InitializeAsync();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { SuspendAdminPage(); base.OnNavigatedFrom(e); }
    private bool Current(MonitorAuthorityLease? owner) => _active && owner is { IsCurrent: true } && ReferenceEquals(owner, Controller.Owner);
    public bool RequiresLeaveConfirmation => _leave.RequiresConfirmation;
    public Task<bool> CanLeaveAsync() => _leave.CanLeaveAsync(async owner => await ConfirmAsync("Discard unsaved changes?", "Your changes have not been saved.", "Discard", owner));
    public void SuspendAdminPage()
    {
        _active = false; ++_operation; _searchTimer.Stop(); _jobTimer.Stop(); _previewTimer.Stop(); _previewReads?.Cancel(); ++_previewSerial;
        _dialog?.Hide(); Controller.Suspend(); ClearStaged(); _grabbed = null; _shelfWindows.Clear(); ++_memberRead; _selected.Clear(); _surface.Children.Clear(); _rows.Children.Clear(); _notice.Text = "";
    }
    private void Retired() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_active) return;
        ++_operation; ++_memberRead; _searchTimer.Stop(); _jobTimer.Stop(); _previewTimer.Stop(); _previewReads?.Cancel(); ++_previewSerial; _dialog?.Hide(); ClearStaged(); _grabbed = null; _shelfWindows.Clear(); _selected.Clear(); _surface.Children.Clear(); _rows.Children.Clear();
        // The starter-pack job belonged to the retired authority: never poll or act on it under the next one.
        Controller.EndStarterJob();
        Notice("Administrator access changed. Reload to continue.");
        _surface.Children.Add(ActionButton("Reload", "CollectionsAuthorityReload", async () => { Controller.Open(); await InitializeAsync(); }));
    });
    private async Task InitializeAsync()
    {
        var owner = Controller.Owner; var initialization = ++_initialization;
        _surface.Children.Clear(); _surface.Children.Add(Text("Loading collections…"));
        try
        {
            await Controller.LoadAsync(_library, _arrange && _route.Segments.Count == 0, editorOnly: _route.Segments.Count > 0);
            if (!Current(owner) || initialization != _initialization) return;
            if (_arrange) _library ??= Controller.Board?.LibraryId ?? Controller.Libraries.FirstOrDefault()?.Id;
            if (_route.Segments.Count > 0)
            {
                var isNew = _route.Segment(0) == "new";
                await Controller.EditAsync(isNew ? null : _route.Segment(0), _route.Get("type") ?? "manual", _library, _route.Get("source"));
                if (Current(owner) && initialization == _initialization) RenderEditor();
            }
            else { RenderList(); await ResumeJobAsync(); }
        }
        catch (Exception error)
        {
            if (Current(owner) && initialization == _initialization)
            {
                _surface.Children.Clear(); Notice("Couldn’t load collections. " + AdminCollectionsController.Message(error));
                if (Controller.Libraries.Count > 0) _surface.Children.Add(Select("CollectionsLibraryRecovery", Controller.Libraries.Select(l => (l.Id, l.Name)), _library ?? "", async value => { _library = value; ReplaceQuery(); await InitializeAsync(); }));
                _surface.Children.Add(ActionButton("Retry", "CollectionsRetry", InitializeAsync));
            }
        }
    }
    private void Notice(string message) { _notice.Text = message; _notice.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible; }
    private async Task RunAsync(Func<MonitorAuthorityLease, Task> operation, bool reload = true)
    {
        if (Controller.Busy || !_active) return;
        var owner = Controller.Owner; var serial = ++_operation;
        try
        {
            IsHitTestVisible = false;
            await Controller.CommandAsync(async lease => { await operation(lease); return true; });
            if (!Current(owner) || serial != _operation) return;
            Notice("");
            if (reload) { await Controller.LoadAsync(_library, _arrange); if (Current(owner)) RenderList(); }
        }
        catch (Exception error) { if (Current(owner)) Notice(AdminCollectionsController.Message(error)); }
        finally { IsHitTestVisible = true; }
    }
    private void ReplaceQuery()
    {
        _route = _route.WithQuery("view", _arrange ? "arrange" : "list").WithQuery("libraryId", _library)
            .WithQuery("kind", _kind == "all" ? null : _kind).WithQuery("q", _search).WithQuery("failed", _failed ? "1" : null);
        AdminShellPage.TryReplace(this, _route);
    }
    private void OpenEditor(string? id, string? kind = null)
    {
        var request = (id == null ? AdminRouteRequest.CollectionNew().WithQuery("type", kind ?? "manual") : AdminRouteRequest.CollectionEditor(id))
            .WithQuery("libraryId", _library).WithQuery("view", _arrange ? "arrange" : "list").WithQuery("kind", _kind).WithQuery("q", _search).WithQuery("failed", _failed ? "1" : null);
        AdminShellPage.Open(request);
    }
    private void ReturnToList() => AdminShellPage.Open(_route with { Segments = [] });
    private void RenderList()
    {
        _surface.Children.Clear(); _surface.Children.Add(Text("Collections", 28));
        _surface.Children.Add(Text("Server collections everyone can browse on each library’s Collections tab and use in Sections.", 14, true));
        var toolbar = Wrap();
        toolbar.Children.Add(Select("CollectionsView", [("list", "List"), ("arrange", "Arrange")], _arrange ? "arrange" : "list", async value =>
        { _arrange = value == "arrange"; _selecting = false; _selected.Clear(); _page = 0; ReplaceQuery(); await InitializeAsync(); }));
        var libs = new List<(string, string)> { ("", "All libraries") }; libs.AddRange(Controller.Libraries.Select(l => (l.Id, l.Name)));
        toolbar.Children.Add(Select("CollectionsLibrary", libs, _library ?? "", async value => { _library = value.Length == 0 ? null : value; _selected.Clear(); _page = 0; ReplaceQuery(); await InitializeAsync(); }));
        toolbar.Children.Add(ActionButton("New collection", "CollectionsNew", NewPickerAsync));
        toolbar.Children.Add(ActionButton("Starter packs…", "CollectionsStarterPacks", StarterPacksAsync));
        toolbar.Children.Add(ActionButton("Refresh", "CollectionsRefresh", InitializeAsync));
        if (!_arrange) toolbar.Children.Add(ActionButton(_selecting ? "Done" : "Select collections", "CollectionsSelect", () => { _selecting = !_selecting; _selected.Clear(); RenderList(); return Task.CompletedTask; }));
        if (!_arrange) toolbar.Children.Add(ActionButton("Delete all in this view…", "CollectionsDeleteView", () => BatchAsync("delete", wholeView: true)));
        _surface.Children.Add(toolbar);
        if (!_arrange)
        {
            var filters = Wrap();
            var search = new TextBox { Text = _search, PlaceholderText = "Search collections", MinWidth = 200, MaxLength = 512 };
            Identify(search, "CollectionsSearch", "Search collections");
            search.TextChanged += (_, _) => { _search = search.Text; _searchTimer.Stop(); _searchTimer.Start(); };
            filters.Children.Add(search);
            filters.Children.Add(Select("CollectionsKind", [("all", "All"), ("manual", "Manual"), ("smart", "Smart"), ("synced", "Synced list")], _kind,
                value => { _kind = value; _page = 0; _selected.Clear(); ReplaceQuery(); RenderRows(); return Task.CompletedTask; }));
            var failed = new CheckBox { Content = "Sync failed", IsChecked = _failed }; Identify(failed, "CollectionsFailed", "Sync failed");
            Toggled(failed, on => { _failed = on; _page = 0; _selected.Clear(); ReplaceQuery(); RenderRows(); return Task.CompletedTask; }); filters.Children.Add(failed);
            filters.Children.Add(ActionButton("Clear filters", "CollectionsClear", () => { _search = ""; _kind = "all"; _failed = false; _page = 0; ReplaceQuery(); RenderList(); return Task.CompletedTask; }));
            _surface.Children.Add(filters);
            _surface.Children.Add(Text("Click a collection to edit it. Order and shelves live in Arrange.", 13, true));
        }
        _surface.Children.Add(_rows); RenderRows();
    }
    private List<AdminCollectionEntry> Filtered() => (Controller.List?.Items ?? []).Where(c => c.InLibrary(_library) &&
        (_kind == "all" || c.Kind == _kind) && (!_failed || c.LastSyncStatus is "error" or "failed") &&
        (string.IsNullOrWhiteSpace(_search) || c.Title.Contains(_search, StringComparison.CurrentCultureIgnoreCase))).ToList();
    private void RenderRows()
    {
        _rows.Children.Clear();
        if (Controller.List == null) return;
        if (_arrange) { RenderBoard(); return; }
        var all = Filtered();
        if (_selecting)
        {
            var bar = Wrap();
            bar.Children.Add(Text($"{_selected.Count} selected"));
            bar.Children.Add(ActionButton("Select all", "CollectionsSelectAll", () => { foreach (var c in all.Take(100)) _selected.Add(c.Id); RenderRows(); return Task.CompletedTask; }));
            bar.Children.Add(ActionButton("Show on tabs", "CollectionsBulkShow", () => BatchAsync("show")));
            bar.Children.Add(ActionButton("Hide from tabs", "CollectionsBulkHide", () => BatchAsync("hide")));
            if (Controller.Capabilities?.Imports == true) bar.Children.Add(ActionButton("Sync lists", "CollectionsBulkSync", () => BatchAsync("sync")));
            bar.Children.Add(ActionButton("Delete…", "CollectionsBulkDelete", () => BatchAsync("delete")));
            _rows.Children.Add(bar);
        }
        if (all.Count == 0)
        {
            var inLibrary = Controller.Libraries.FirstOrDefault(l => l.Id == _library)?.Name;
            _rows.Children.Add(Text(Controller.List.Items.Count == 0 ? inLibrary == null ? "No collections yet" : $"No collections in {inLibrary} yet" : "No collections match.", 20));
            if (Controller.List.Items.Count == 0)
            {
                _rows.Children.Add(Text("Make one yourself, or start from ready-made lists.", 14, true));
                _rows.Children.Add(ActionButton("Add a starter pack", "CollectionsEmptyStarterPack", StarterPacksAsync));
            }
            return;
        }
        _page = Math.Clamp(_page, 0, (all.Count - 1) / 50);
        _rows.Children.Add(Text($"{all.Count} collections", 12, true));
        foreach (var collection in all.Skip(_page * 50).Take(50)) _rows.Children.Add(CollectionRow(collection));
        var paging = Wrap();
        paging.Children.Add(ActionButton("Previous", "CollectionsPrevious", () => { _page--; RenderRows(); return Task.CompletedTask; }, _page > 0));
        paging.Children.Add(Text($"{_page * 50 + 1}–{Math.Min(all.Count, (_page + 1) * 50)} of {all.Count}"));
        paging.Children.Add(ActionButton("Next", "CollectionsNext", () => { _page++; RenderRows(); return Task.CompletedTask; }, (_page + 1) * 50 < all.Count));
        _rows.Children.Add(paging);
    }
    private FrameworkElement CollectionRow(AdminCollectionEntry c)
    {
        var row = Wrap();
        row.Children.Add(Poster(c.PosterUrl, 44, c.Title));
        if (_selecting)
        {
            var pick = new CheckBox { IsChecked = _selected.Contains(c.Id) }; Identify(pick, "CollectionSelect_" + c.Id, "Select " + c.Title);
            Toggled(pick, on => { if (on && _selected.Count < 100) _selected.Add(c.Id); else _selected.Remove(c.Id); RenderRows(); return Task.CompletedTask; }); row.Children.Add(pick);
        }
        row.Children.Add(ActionButton(c.Title, "CollectionEdit_" + c.Id, () => { OpenEditor(c.Id); return Task.CompletedTask; }));
        row.Children.Add(Text($"{(c.Kind == "synced" ? "Synced list" : c.Kind == "smart" ? "Smart" : "Manual")} · {LibraryNames(c)} · {Titles(c.ItemCount)}", 13, true));
        var visible = new ToggleSwitch { Header = "On tabs", IsOn = c.Visibility != "hidden" }; Identify(visible, "CollectionVisible_" + c.Id, "Show " + c.Title + " on Collections tabs");
        var changingVisibility = false;
        visible.Toggled += async (_, _) =>
        {
            if (changingVisibility) return; changingVisibility = true; var owner = Controller.Owner;
            try
            {
                var target = visible.IsOn;
                if (!target && c.RowCount > 0 && await ConfirmAsync("Hide from Collections tabs?", "Rows still show this collection. Their See all link will be unavailable.", "Hide", owner!) != true) return;
                if (Current(owner)) await PatchAsync(c.Id, new() { ["visibility"] = target ? "visible" : "hidden" });
            }
            catch (Exception error) { if (Current(owner)) Notice(AdminCollectionsController.Message(error)); }
            finally { visible.IsOn = c.Visibility != "hidden"; changingVisibility = false; }
        }; row.Children.Add(visible);
        if (c.LastSyncStatus is "failed" or "error") row.Children.Add(Text("Sync failed", 13));
        else if (c.LastSyncStatus == "running") row.Children.Add(Text("Syncing now…", 13));
        if (c.HomeRowCount > 0) row.Children.Add(Text("On Home", 12));
        if (c.Featured) row.Children.Add(Text("Pinned", 12));
        if (c.Kind == "synced" && Controller.Capabilities?.Imports == true) row.Children.Add(ActionButton("Sync now", "CollectionSync_" + c.Id, () => RunAsync(async lease => { await _api.SyncAsync(lease, c.Id); })));
        row.Children.Add(ActionButton("Delete…", "CollectionDelete_" + c.Id, () => DeleteDialogAsync(c.Id)));
        return Card(row);
    }
    private static string Titles(int count) => count == 1 ? "1 title" : $"{count} titles";
    private string LibraryNames(AdminCollectionEntry c) => string.Join(", ", Controller.Libraries.Where(l => c.InLibrary(l.Id)).Select(l => l.Name));
    private Task PatchAsync(string id, JsonObject patch) => RunAsync(async lease => { var observed = await _api.GetAsync(lease, id); await _api.SaveAsync(lease, observed, patch); });
}
