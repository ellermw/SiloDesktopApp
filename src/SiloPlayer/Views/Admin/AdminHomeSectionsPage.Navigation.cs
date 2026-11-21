using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;

namespace SiloPlayer.Views.Admin;

/// <summary>Library-page switching, the Collections link contract (?page=, ?add=, ?edit=, ?return=) and the bounded collection names.</summary>
public sealed partial class AdminHomeSectionsPage
{
    private readonly AdminHomeSectionsPills _pages = new();
    private AdminHomeSectionScope _scope = AdminHomeSectionScope.Home;
    private List<AdminHomeSectionChoice> _libraries = [];
    private readonly Dictionary<string, AdminHomeSectionCollectionLookup> _collectionNames = new(StringComparer.Ordinal);
    private bool _collectionsLoaded, _collectionsPartial, _librariesFailed;
    private AdminRouteRequest _route = new(AdminRoute.HomeSections);
    private bool _syncPages;
    private string? _handledLinks;
    private string? _returnPath;
    private bool _linkedCollection;
    private string PageLabel => _scope.LibraryId is { } id ? _libraries.FirstOrDefault(library => library.Id == id)?.Label ?? AdminHomeSectionsText.T("web.admin.homeRows.restore.libraryFallback", new { id }) : T("web.home.rows.pages.home");
    protected override void OnNavigatedTo(NavigationEventArgs args)
    { _route = args.Parameter as AdminRouteRequest ?? new(AdminRoute.HomeSections); base.OnNavigatedTo(args); }

    private IReadOnlyList<AdminHomeSectionLibraryPage> LibraryPages() => _libraries.Select(library => new AdminHomeSectionLibraryPage(library.Id, library.Label, library.Type)).ToArray();

    private async Task LoadPagesAsync()
    {
        var owner = _controller?.State.Owner; if (owner?.IsCurrent != true) return;
        try
        {
            var libraries = await _api.ChoicesAsync(owner, "libraries", ct: owner.Token);
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner)) return;
            _libraries = libraries.Items; _librariesFailed = false;
            // An unknown or malformed ?page= opens Home, so a stale or hand-edited link still lands somewhere.
            var requested = _route.Get("page"); var desired = requested != null && _libraries.Any(library => library.Id == requested) ? new AdminHomeSectionScope("library", requested) : AdminHomeSectionScope.Home;
            _pages.Set(PageEntries(), desired);
            if (_scope != desired) { _scope = desired; CloseEditor(); await _controller!.OpenAsync(_scope); }
            await LoadCollectionNamesAsync();
        }
        catch (Exception) { if (_active && owner.IsCurrent) { _librariesFailed = true; Render(); } }
    }

    /// <summary>One bounded read of the collection list (2,000 at most), only when a row on this page names a collection.</summary>
    private async Task LoadCollectionNamesAsync()
    {
        var active = _controller?.State.Owner;
        if (active?.IsCurrent != true || _controller!.State.Snapshot?.Rows.Any(row => row.SectionType is "collection" or "collection_posters") != true) return;
        try
        {
            var collections = await _api.ChoicesAsync(active, "collections", ct: active.Token);
            if (!_active || !active.IsCurrent || !ReferenceEquals(active, _controller?.State.Owner)) return;
            _collectionNames.Clear();
            foreach (var collection in collections.Items) _collectionNames[collection.Id] = AdminHomeSectionCollectionLookup.Found(collection.Label, CollectionKind(collection.CollectionType));
            _collectionsLoaded = true; _collectionsPartial = collections.Truncated; Render();
        }
        catch (Exception) when (active.IsCurrent) { /* Names stay unknown; the rows keep their saved identities. */ }
    }

    private static string? CollectionKind(string type) => string.IsNullOrEmpty(type) ? null
        : AdminHomeSectionsText.T(type switch { "manual" => "web.collections.types.kindManual", "smart" => "web.collections.types.kindSmart", _ => "web.collections.types.kindSynced" });

    /// <summary>Home, then each library page: what the pill strip lists.</summary>
    private IEnumerable<(object Key, string Label, string AutomationId, bool Home)> PageEntries()
    {
        yield return (AdminHomeSectionScope.Home, T("web.home.rows.pages.home"), "HomeSectionsPage_home", true);
        foreach (var library in _libraries) yield return (new AdminHomeSectionScope("library", library.Id), library.Label, "HomeSectionsPage_" + library.Id, false);
    }

    /// <summary>A pill the admin chose. The strip keeps the pressed page until this one is accepted: a busy page or a refused discard leaves it where it was.</summary>
    private async void PageChosen(object key)
    {
        if (key is not AdminHomeSectionScope scope || scope == _scope || _controller == null) return;
        if (_controller.State.Busy || !await CanLeaveAsync()) { _pages.Select(_scope); return; }
        _pages.Select(scope);
        CloseEditor(); _dialogRevision++; _dialog?.Hide(); _scope = scope; _selection.Clear(); _selectMode = false; _collectionsLoaded = false; _collectionNames.Clear();
        var query = AdminHomeSectionsLinks.WithoutLinks(_route.Query) ?? new Dictionary<string, string>();
        query["page"] = scope.LibraryId ?? "home";
        _route = new(AdminRoute.HomeSections, query); AdminShellPage.TryReplace(this, _route);
        var controller = _controller;
        await controller.OpenAsync(scope);
        if (_active && ReferenceEquals(controller, _controller)) { await LoadCollectionNamesAsync(); await LoadDefaultsAsync(); }
    }

    // ── Links ────────────────────────────────────────────────

    /// <summary>
    /// Follows ?add=, ?edit= and ?return= once the page's rows and capabilities are known, then drops them from the address
    /// (217ff25 web/src/components/homeRows/useRowLinks.ts). Nothing opens until the page can change.
    /// </summary>
    private async Task ReadLinksAsync()
    {
        var key = AdminHomeSectionsLinks.Key(_route.Query);
        if (key == "" || key == _handledLinks) return;
        var controller = _controller; if (controller == null || controller.State.Snapshot == null || controller.State.Capabilities == null) return;
        // A link to a library page waits for the library list: followed now it would act on Home. Reload reads the list again.
        if (!AdminHomeSectionsLinks.PageKnown(_route.Query, !_librariesFailed)) return;
        _handledLinks = key;
        var links = AdminHomeSectionsLinks.Read(_route.Query);
        _route = new(AdminRoute.HomeSections, AdminHomeSectionsLinks.WithoutLinks(_route.Query)); AdminShellPage.TryReplace(this, _route);
        if (links == null) return;
        _returnPath = links.ReturnTo;
        if (links.AddInvalid) { Toasts?.Error(T("web.home.rows.links.cantAdd")); return; }
        if (links.Add != null) { await OpenLinkedAddAsync(controller, links); return; }
        if (links.Edit != null)
        {
            if (!controller.CanWrite) { Toasts?.Error(T("web.home.rows.links.pageLocked")); return; }
            if (controller.State.Snapshot.Rows.All(row => row.Id != links.Edit)) { Toasts?.Error(T("web.home.rows.links.rowGone")); return; }
            await OpenEditAsync(links.Edit);
        }
    }

    private async Task OpenLinkedAddAsync(AdminHomeSectionsController controller, AdminHomeSectionsLinks links)
    {
        // An editor or dialog the admin opened while the page loaded stays; the link never replaces it.
        if (_editor != null || _dialog != null) return;
        if (!controller.CanWrite) { Toasts?.Error(T("web.home.rows.links.pageLocked")); return; }
        if (controller.State.Catalog == null) { Toasts?.Error(T(controller.State.CatalogFailed ? "web.home.rows.links.noKinds" : "web.home.rows.links.cantAdd")); return; }
        var recipe = controller.State.Catalog.Categories.SelectMany(group => group.Recipes).FirstOrDefault(entry => entry.Type == "collection");
        var owner = controller.State.Owner!; AdminHomeSectionChoice choice;
        try
        {
            choice = await _api.CollectionByIdAsync(owner, links.Add!.Id, owner.Token);
            if (!_active || !owner.IsCurrent || !ReferenceEquals(owner, _controller?.State.Owner) || !controller.CanWrite || _editor != null || _dialog != null) return;
        }
        catch (ApiException ex) when (ex.StatusCode is 404 or 400) { if (_active) Toasts?.Error(T("web.home.rows.links.cantAdd")); return; }
        catch (InvalidDataException) { if (_active) Toasts?.Error(T("web.home.rows.links.cantAdd")); return; }
        catch (Exception) { if (_active && owner.IsCurrent) Toasts?.Error(T("web.home.rows.links.noCollections")); return; }
        if (recipe == null || recipe.Presets.Count == 0) { Toasts?.Error(T("web.home.rows.links.cantAdd")); return; }
        // Add row opens on step 2 with the collection picked, named after it.
        _session = null; _draft = AdminHomeSectionsDraftFactory.ForPreset(recipe, recipe.Presets[0]);
        BeginEditor(); _linkedCollection = true; _choiceLabels[choice.Id] = choice.Label;
        AdminHomeSectionsDraftFactory.WithCollection(_draft, choice.Id, choice.Label);
        RenderForm();
    }

    private bool CanReturn => _linkedCollection && _returnPath != null && AdminRouteCatalog.TryResolve(_returnPath, out var target) && !target.IsCatchAll && target.Request is { Route: AdminRoute.Collections } request && AdminShellPage.CanOpen(request);
    private async Task ReturnToCollectionAsync()
    { if (CanReturn && await CanLeaveAsync()) { var path = _returnPath!; CloseEditor(); AdminShellPage.TryOpenHref(path); } }

    /// <summary>
    /// After Add row from a link: the row's place, with "Move it" to open it here, and Back to the collection when it can be opened.
    /// <paramref name="back"/> is the return path read while the editor was still open; closing it forgets the link.
    /// </summary>
    private void AfterLinkedAdd(string? id, string? back)
    {
        var rows = _controller?.State.Snapshot?.Rows ?? [];
        var at = id == null ? -1 : rows.ToList().FindIndex(row => row.Id == id);
        if (id == null) { if (back != null) AdminShellPage.TryOpenHref(back); return; }
        var position = at == -1 ? rows.Count : at + 1; var total = rows.Count;
        var page = _scope.LibraryId ?? "home"; var label = PageLabel;
        var message = _scope.Kind == "home" ? T("web.home.rows.links.addedHome", new { position = (long)position, total = (long)total })
            : T("web.home.rows.links.addedLibrary", new { page = label, position = (long)position, total = (long)total });
        if (back != null) AdminShellPage.TryOpenHref(back);
        Toasts?.Success(message, T("web.home.rows.links.moveIt"), () =>
        {
            AdminShellPage.Open(new AdminRouteRequest(AdminRoute.HomeSections, new Dictionary<string, string> { ["page"] = page, ["edit"] = id }));
            return Task.CompletedTask;
        });
    }

    private void ClearPageNames()
    {
        _collectionNames.Clear(); _collectionsLoaded = false; _libraries.Clear(); _syncPages = true;
        try { _pages.Set([(AdminHomeSectionScope.Home, T("web.home.rows.pages.home"), "HomeSectionsPage_home", true)], AdminHomeSectionScope.Home); }
        finally { _syncPages = false; }
        _scope = AdminHomeSectionScope.Home; _handledLinks = null;
    }

    /// <summary>The other library pages a draft can also be added to (never this page), for Add row on a library page.</summary>
    private IReadOnlyList<AdminHomeSectionChoice> CopyTargets(AdminHomeSectionDraft draft)
    {
        if (_scope.LibraryId == null || !draft.CanCopy) return [];
        var fits = AdminHomeSectionsRules.CopyTargetPages(draft.SectionType, draft.Config, LibraryPages(), _scope.LibraryId).Select(page => page.Id).ToHashSet(StringComparer.Ordinal);
        return _libraries.Where(library => library.Id != _scope.LibraryId && fits.Contains(library.Id)).ToArray();
    }
}
