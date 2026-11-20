using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminHomeSectionsPage
{
    private readonly ComboBox _pages = new() { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private AdminHomeSectionScope _scope = AdminHomeSectionScope.Home;
    private List<AdminHomeSectionChoice> _libraries = [];
    private readonly Dictionary<string, string> _collectionLabels = new(StringComparer.Ordinal);
    private AdminRouteRequest _route = new(AdminRoute.HomeSections);
    private bool _syncPages, _linksRead;
    private string? _returnPath;
    private bool _linkedCollection;
    private string PageLabel => _scope.LibraryId is { } id ? _libraries.FirstOrDefault(library => library.Id == id)?.Label ?? "Library " + id : "Home";
    protected override void OnNavigatedTo(NavigationEventArgs args)
    { _route = args.Parameter as AdminRouteRequest ?? new(AdminRoute.HomeSections); base.OnNavigatedTo(args); }
    private async Task LoadPagesAsync()
    {
        var owner = _controller?.State.Owner; if (owner?.IsCurrent != true) return;
        try
        {
            var libraries = await _api.ChoicesAsync(owner, "libraries", ct: owner.Token);
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner)) return;
            _libraries = libraries.Items;
            var requested = _route.Get("page"); var desired = _libraries.Any(library => library.Id == requested) ? new AdminHomeSectionScope("library", requested) : AdminHomeSectionScope.Home;
            _syncPages = true;
            try
            {
                _pages.Items.Clear(); _pages.Items.Add(new ComboBoxItem { Content = "Home", Tag = AdminHomeSectionScope.Home });
                foreach (var library in _libraries) _pages.Items.Add(new ComboBoxItem { Content = library.Label, Tag = new AdminHomeSectionScope("library", library.Id) });
                _pages.SelectedItem = _pages.Items.OfType<ComboBoxItem>().First(item => Equals(item.Tag, desired));
            }
            finally { _syncPages = false; }
            if (_scope != desired) { _scope = desired; CloseEditor(); await _controller!.OpenAsync(_scope); }
            var active = _controller!.State.Owner;
            if (active?.IsCurrent == true && _controller.State.Snapshot?.Rows.Any(row => row.SectionType is "collection" or "collection_posters") == true)
            {
                try
                {
                    var collections = await _api.ChoicesAsync(active, "collections", ct: active.Token);
                    if (_active && active.IsCurrent && ReferenceEquals(active, _controller?.State.Owner))
                    { _collectionLabels.Clear(); foreach (var collection in collections.Items) _collectionLabels[collection.Id] = collection.Label; Render(); }
                }
                catch (Exception) when (active.IsCurrent) { /* Names remain unavailable; saved identities and validators are kept. */ }
            }
        }
        catch (Exception) { if (_active && owner.IsCurrent) _status.Text = "Library pages could not be loaded. Reload rows to retry; Home remains available."; }
    }
    private async void PageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncPages || _pages.SelectedItem is not ComboBoxItem { Tag: AdminHomeSectionScope scope } || scope == _scope || _controller == null) return;
        if (_controller.State.Busy || !await CanLeaveAsync())
        { _syncPages = true; _pages.SelectedItem = _pages.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, _scope)); _syncPages = false; return; }
        CloseEditor(); _dialogRevision++; _dialog?.Hide(); _scope = scope; _selection.Clear(); _selectMode = false;
        var query = _route.Query?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new();
        query["page"] = scope.LibraryId ?? "home"; foreach (var key in new[] { "add", "edit", "return" }) query.Remove(key);
        _route = new(AdminRoute.HomeSections, query); AdminShellPage.TryReplace(this, _route);
        await _controller.OpenAsync(scope); await LoadDefaultsAsync();
    }
    private async Task ReadLinksAsync()
    {
        if (_linksRead || _controller?.CanWrite != true) return; _linksRead = true;
        var edit = _route.Get("edit"); var add = _route.Get("add");
        _returnPath = SafeReturnPath(_route.Get("return"));
        var query = _route.Query?.Where(pair => pair.Key is not ("add" or "edit" or "return")).ToDictionary(pair => pair.Key, pair => pair.Value);
        _route = new(AdminRoute.HomeSections, query); AdminShellPage.TryReplace(this, _route);
        if (edit != null) { await OpenEditAsync(edit); return; }
        if (add == null) return;
        if (!add.StartsWith("collection:library:", StringComparison.Ordinal)) { _status.Text = "This collection cannot be added to an administrator row."; return; }
        var owner = _controller.State.Owner!;
        try
        {
            var choice = await _api.CollectionByIdAsync(owner, add["collection:library:".Length..], owner.Token);
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner)) return;
            _session = null; _draft = new() { SectionType = "collection", Title = choice.Label, Config = new JsonObject { ["library_collection_id"] = choice.Id } };
            BeginEditor(); _linkedCollection = true; _choiceLabels[choice.Id] = choice.Label; RenderForm();
        }
        catch (Exception) { if (_active && owner.IsCurrent) _status.Text = "The linked collection is not available."; }
    }

    private static string? SafeReturnPath(string? value)
    {
        if (value == null || !value.StartsWith("/admin/collections/", StringComparison.Ordinal)) return null;
        try
        {
            var decoded = Uri.UnescapeDataString(value);
            bool Unsafe(string path) => path.Contains('\\') || path.Contains("//", StringComparison.Ordinal) || path.Any(char.IsControl) || path.Split('/', '?', '#').Any(part => part is "." or "..");
            return Unsafe(value) || Unsafe(decoded) ? null : value;
        }
        catch (UriFormatException) { return null; }
    }
    private bool CanReturn => _linkedCollection && _returnPath != null && AdminRouteCatalog.TryResolve(_returnPath, out var target) && target.Request != null && AdminShellPage.CanOpen(target.Request);
    private async Task ReturnToCollectionAsync()
    { if (CanReturn && await CanLeaveAsync()) { var path = _returnPath!; CloseEditor(); AdminShellPage.TryOpenHref(path); } }
    private void ClearPageNames()
    {
        _collectionLabels.Clear(); _libraries.Clear(); _syncPages = true;
        try { _pages.Items.Clear(); _pages.Items.Add(new ComboBoxItem { Content = "Home", Tag = AdminHomeSectionScope.Home }); _pages.SelectedIndex = 0; }
        finally { _syncPages = false; }
        _scope = AdminHomeSectionScope.Home;
    }

    private IReadOnlyList<AdminHomeSectionChoice> CopyTargets(AdminHomeSectionDraft draft)
    {
        if (_scope.LibraryId == null || !draft.CanCopy) return [];
        string Kind(string type) => type.ToLowerInvariant() switch { "movies" or "movie" => "movie", "tv" or "series" or "show" or "tvshows" => "tv", "audiobooks" => "audiobook", "ebooks" => "ebook", var other => other };
        var required = Value(draft.Config, "media_scope") != "" ? Kind(_libraries.FirstOrDefault(library => library.Id == _scope.LibraryId)?.Type ?? "") :
            draft.SectionType == "next_in_series" ? "audiobook" : draft.SectionType == "trending_discover" && Value(draft.Config, "filter_type") is "movie" or "series" ? Kind(Value(draft.Config, "filter_type")) :
            draft.SectionType == "continue_watching" ? Value(draft.Config, "continue_type") switch { "listening" => "audiobook", "reading" => "ebook", _ => Value(draft.Config, "filter_type") is "audiobook" or "ebook" ? Value(draft.Config, "filter_type") : "" } : "";
        return _libraries.Where(library => library.Id != _scope.LibraryId && (required == "" || Kind(library.Type) == required)).ToArray();
    }
    private async Task CopyAsync(AdminHomeSection row)
    {
        var controller = _controller; if (controller?.CanWrite != true || _dialog != null) return;
        var session = await controller.OpenEditAsync(row.Id); if (session == null || !_active) return;
        if (AdminHomeSectionsController.Fingerprint(session.Row) != AdminHomeSectionsController.Fingerprint(row)) { _status.Text = "This row changed. Reload and review it before copying."; return; }
        var draft = AdminHomeSectionDraft.From(session.Row); var targets = CopyTargets(draft); var chosen = new HashSet<string>(StringComparer.Ordinal);
        var panel = new StackPanel { Spacing = 8 }; panel.Children.Add(U.Text("Each selected library page gets a new row at its bottom. Copies are never hero banners.", 13));
        foreach (var library in targets)
        {
            var box = new CheckBox { Content = library.Label }; box.Click += (_, _) => { if (box.IsChecked == true && chosen.Count < 100) chosen.Add(library.Id); else { chosen.Remove(library.Id); box.IsChecked = false; } }; panel.Children.Add(box);
        }
        if (await ConfirmAsync("Add to other libraries", new ScrollViewer { Content = panel, MaxHeight = 420 }, "Add row", session.Owner) && chosen.Count > 0)
            await controller.CopyAsync(session, chosen.ToArray());
    }
}
