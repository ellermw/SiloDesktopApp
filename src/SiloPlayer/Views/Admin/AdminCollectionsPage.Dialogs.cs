using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage
{
    private async Task NewPickerAsync()
    {
        var owner = Controller.Owner; var body = Stack(12);
        var dialog = new ContentDialog { Title = "New collection", Content = body, CloseButtonText = "Cancel" };
        foreach (var (kind, label, description) in new[] { ("manual", "Manual", "Pick the titles yourself."), ("smart", "Smart", "Rules keep it up to date."), ("synced", "Synced list", "Follow a list from MDBList or TMDB.") })
        {
            if (kind == "synced" && Controller.Capabilities?.Imports != true) continue;
            body.Children.Add(ActionButton(label, "CollectionsNew_" + kind, () => { dialog.Tag = kind; dialog.Hide(); return Task.CompletedTask; }));
            body.Children.Add(Text(description, 13, true));
        }
        if (Controller.Capabilities?.Imports == true) body.Children.Add(ActionButton("Browse templates", "CollectionsTemplates", () => { dialog.Tag = "templates"; dialog.Hide(); return Task.CompletedTask; }));
        Identify(dialog, "CollectionsNewPicker", "New collection"); await ShowAsync(dialog);
        if (!Current(owner) || dialog.Tag is not string choice) return;
        if (choice == "templates") await TemplatesDialogAsync(); else OpenEditor(null, choice);
    }
    private async Task ShelfDialogAsync(string? id)
    {
        if (_library == null) return; var owner = Controller.Owner!;
        var snapshot = id == null ? null : await _api.ShelfAsync(owner, id);
        if (!Current(owner)) return;
        var body = Stack(12); var name = new TextBox { Text = snapshot?.Value["name"]?.ToString() ?? "", MaxLength = 512 };
        Identify(name, "CollectionShelfName", "Shelf name"); body.Children.Add(Text("Shelf name")); body.Children.Add(name);
        var sort = snapshot?.Value["default_sort_mode"]?.ToString() ?? "manual";
        body.Children.Add(Select("CollectionShelfSort", [("manual", "Your order"), ("name_asc", "Name A–Z"), ("name_desc", "Name Z–A"), ("recent", "Recently updated"), ("most_items", "Most titles")], sort, value => { sort = value; return Task.CompletedTask; }));
        var errorText = Text(""); body.Children.Add(errorText);
        var dialog = new ContentDialog { Title = id == null ? "New shelf" : "Rename shelf", Content = body, PrimaryButtonText = id == null ? "Add shelf" : "Save", CloseButtonText = "Cancel", IsPrimaryButtonEnabled = name.Text.Trim().Length > 0 };
        Identify(dialog, "CollectionShelfDialog", "Shelf"); name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0;
        var saved = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral(); args.Cancel = true;
            body.IsHitTestVisible = false;
            try { await Controller.CommandAsync(lease => _api.SaveShelfAsync(lease, _library, snapshot, new() { ["name"] = name.Text.Trim(), ["default_sort_mode"] = sort })); saved = true; args.Cancel = false; }
            catch (Exception error) { if (Current(owner)) { errorText.Text = AdminCollectionsController.Message(error); dialog.IsPrimaryButtonEnabled = false; } }
            finally { body.IsHitTestVisible = true; deferral.Complete(); }
        };
        await ShowAsync(dialog);
        if (saved && Current(owner)) await InitializeAsync();
    }
    private async Task DeleteShelfDialogAsync(string id)
    {
        var owner = Controller.Owner!; var snapshot = await _api.ShelfAsync(owner, id);
        var members = Controller.Board is { } board && board.Orders.TryGetValue(id, out var order) ? AdminCollectionsApi.Order(order).Count : 0;
        var library = Controller.Libraries.FirstOrDefault(l => l.Id == _library)?.Name;
        if (await ConfirmAsync(AdminCollectionCopy.DeleteShelfTitle(snapshot.Value["name"]?.ToString()), AdminCollectionCopy.DeleteShelfDescription(members, library), AdminCollectionCopy.DeleteShelfLabel, owner, destructive: true) == true)
            await RunAsync(async lease => { await _api.DeleteShelfAsync(lease, snapshot); });
    }
    private async Task MoveDialogAsync(string id)
    {
        var owner = Controller.Owner; var board = Controller.Board; if (board == null) return;
        var options = board.List.Groups.Where(g => g.Kind == "regular").Select(g => (g.Id, g.Name)).Prepend(("ungrouped", "No heading")).ToArray();
        var destination = board.List.Items.First(c => c.Id == id).GroupId ?? "ungrouped";
        var body = Stack(12); body.Children.Add(Select("CollectionMoveShelf", options, destination, value => { destination = value; return Task.CompletedTask; }));
        var dialog = new ContentDialog { Title = "Move to shelf", Content = body, PrimaryButtonText = "Move", CloseButtonText = "Cancel" };
        Identify(dialog, "CollectionMoveDialog", "Move to shelf");
        if (await ShowAsync(dialog) == ContentDialogResult.Primary && Current(owner))
            await RunAsync(async lease => { await _api.MoveAsync(lease, board, id, destination, AdminCollectionsApi.Order(board.Orders[destination]).Count); });
    }
    private async Task DeleteDialogAsync(string id)
    {
        if (Controller.StarterJobId != null) { Notice(AdminCollectionCopy.StarterPackBlocksDelete); return; }
        var owner = Controller.Owner!;
        var plan = await _api.PrepareDeleteAsync(owner, id, Controller.Capabilities?.SectionReferences == true);
        if (!Current(owner)) return;
        var libraryNames = Controller.Libraries.Where(l => (plan.Collection.Value["library_ids"] as JsonArray)?.Any(v => v?.ToString() == l.Id) == true).Select(l => l.Name).ToList();
        var body = Stack(12); var describe = Text(""); var rowsHost = Stack(8); var errorText = StatusText("", true, 14);
        var hint = Text(AdminCollectionCopy.KeepRowsHint, 13, true); var footer = Text(AdminCollectionCopy.DeleteFooter, 13, true);
        body.Children.Add(describe); body.Children.Add(rowsHost); body.Children.Add(hint);
        var removeRows = new CheckBox { Content = "Remove it from the rows that show it, then delete it", IsChecked = false };
        Identify(removeRows, "CollectionDeleteRows", "Remove it from rows"); body.Children.Add(removeRows); body.Children.Add(errorText);
        var dialog = new ContentDialog { Title = AdminCollectionCopy.DeleteTitle(plan.Collection.Value["title"]?.ToString()), Content = new ScrollViewer { Content = body, MaxHeight = 450 }, PrimaryButtonText = AdminCollectionCopy.DeleteLabel, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        Identify(dialog, "CollectionDeleteDialog", "Delete collection"); FocusCancel(dialog);
        // A starter pack being added would race this delete: the confirm stays off with the official reason while one runs, and the press asks again.
        var needsReview = false; string? shownBlock = null;
        string? Blocked() => Controller.StarterJobId != null ? AdminCollectionCopy.StarterPackBlocksDelete : null;
        void UpdateEnabled()
        {
            shownBlock = Blocked();
            if (shownBlock != null) errorText.Text = shownBlock; else if (errorText.Text == AdminCollectionCopy.StarterPackBlocksDelete) errorText.Text = "";
            dialog.IsPrimaryButtonEnabled = shownBlock == null && !needsReview && (plan.Rows.Count == 0 || removeRows.IsChecked == true);
        }
        void RenderPlan()
        {
            rowsHost.Children.Clear();
            var stays = plan.Rows.Any(AdminCollectionCopy.RowStays);
            describe.Text = plan.Rows.Count == 0 ? AdminCollectionCopy.DeleteDescription(libraryNames) : AdminCollectionCopy.DeleteWithRowsDescription(libraryNames, plan.Rows.Count);
            foreach (var row in plan.Rows)
            {
                var line = Wrap(); line.Children.Add(NameBlock(row["title"]?.ToString(), 14, 360, placeholder: AdminCollectionNames.UntitledRow));
                line.Children.Add(Text(row["scope"]?.ToString() == "library" ? "Library" : "Home", 13, true));
                if (AdminCollectionCopy.RowStays(row)) line.Children.Add(Text(AdminCollectionCopy.RowRemovesOnly(row["title"]?.ToString()), 13, true));
                rowsHost.Children.Add(line);
            }
            removeRows.Visibility = plan.Rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            hint.Visibility = footer.Visibility = plan.Rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            dialog.PrimaryButtonText = AdminCollectionCopy.DeleteButton(plan.Rows.Count, stays);
            UpdateEnabled();
        }
        removeRows.Checked += (_, _) => UpdateEnabled();
        removeRows.Unchecked += (_, _) => UpdateEnabled();
        body.Children.Add(ActionButton("Reload and review", "CollectionDeleteReview", async () =>
        {
            plan = await _api.PrepareDeleteAsync(owner, id, Controller.Capabilities?.SectionReferences == true);
            if (Current(owner)) { needsReview = false; removeRows.IsChecked = false; errorText.Text = "Review the current collection and rows, then confirm again."; RenderPlan(); }
        }));
        body.Children.Add(footer);
        var deleted = false; RenderPlan();
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            // Asked again at the press: a starter pack may have started since the confirm was drawn. Nothing is sent.
            if (Blocked() is { } reason) { errorText.Text = reason; dialog.IsPrimaryButtonEnabled = false; deferral.Complete(); return; }
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                await Controller.CommandAsync(async lease => { await _api.DeleteAsync(lease, plan, removeRows.IsChecked == true); return true; });
                deleted = true; args.Cancel = false;
            }
            catch (Exception error) { needsReview = true; if (Current(owner)) errorText.Text = AdminCollectionsController.Message(error) + " Reload and review the remaining rows before confirming again."; }
            finally { deferral.Complete(); }
        };
        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        watch.Tick += (_, _) => { if (Blocked() != shownBlock) UpdateEnabled(); };
        watch.Start();
        var contrast = AdminCollectionsDestructiveStyle.Track(dialog);
        try { await ShowAsync(dialog); } finally { watch.Stop(); contrast.Dispose(); }
        if (deleted && Current(owner))
        {
            if (_route.Segments.Count > 0) { Controller.CloseEditor(); ClearStaged(); ReturnToList(); }
            else { _selected.Remove(id); await InitializeAsync(); Notice("Collection deleted"); }
        }
    }
    private async Task ShowRowsAsync(string id)
    {
        var owner = Controller.Owner!; var rows = await _api.RowsAsync(owner, id); if (!Current(owner)) return;
        var body = Stack(10);
        if (rows.Count == 0) body.Children.Add(Text("No rows show it yet."));
        var dialog = new ContentDialog { Title = "Rows that show it", Content = new ScrollViewer { Content = body, MaxHeight = 420 }, CloseButtonText = "Close" };
        foreach (var row in rows)
        {
            body.Children.Add(Text($"{AdminCollectionNames.PlainOr(row["title"]?.ToString(), AdminCollectionNames.UntitledRow)} · {(row["enabled"]?.GetValue<bool>() == true ? "On" : "Off")}", 15));
            var scope = row["scope"]?.ToString(); var library = row["library_id"]?.ToString();
            body.Children.Add(ActionButton("Open row", "CollectionRowOpen_" + row["id"], () =>
            {
                var href = "/admin/sections?page=" + (scope == "library" ? Uri.EscapeDataString(library ?? "") : "home") + "&edit=" + Uri.EscapeDataString(row["id"]!.ToString());
                dialog.Tag = href; dialog.Hide();
                return Task.CompletedTask;
            }));
        }
        await ShowAsync(dialog);
        if (Current(owner) && dialog.Tag is string href && AdminShellPage.TryOpenHref(href)?.Kind != AdminRouteResolutionKind.Route)
            Notice("Home Sections is not available in this source baseline.");
    }
    private async Task AddAsRowAsync(string id)
    {
        if (Controller.Dirty) { Notice("Save changes before adding it as a row."); return; }
        if (Controller.Draft?["visibility"]?.ToString() == "hidden") { Notice("Show it on Collections tabs first, then add it as a row."); return; }
        var owner = Controller.Owner; var body = Stack();
        var dialog = new ContentDialog { Title = "Add as a row", Content = body, CloseButtonText = "Cancel" };
        foreach (var target in Controller.Libraries.Select(l => (Page: l.Id, Label: AdminCollectionNames.PlainOr(l.Name, AdminCollectionNames.UntitledLibrary) + " page")).Prepend((Page: "home", Label: "Home")))
            body.Children.Add(ActionButton(target.Label, "CollectionAddRow_" + target.Page, () =>
            {
                dialog.Tag = "/admin/sections?page=" + Uri.EscapeDataString(target.Page) + "&add=" + Uri.EscapeDataString("collection:library:" + id) + "&return=" + Uri.EscapeDataString(_route.Href);
                dialog.Hide(); return Task.CompletedTask;
            }));
        await ShowAsync(dialog);
        if (Current(owner) && dialog.Tag is string href && AdminShellPage.TryOpenHref(href)?.Kind != AdminRouteResolutionKind.Route)
            Notice("Home Sections is not available in this source baseline.");
    }
    private async Task ReviewConflictAsync()
    {
        var owner = Controller.Owner!; var id = Controller.Editor?.Value["id"]?.ToString(); if (id == null) return;
        var latest = await _api.GetAsync(owner, id); if (!Current(owner)) return;
        var body = Stack(12); body.Children.Add(Text("This collection changed elsewhere. Choose which of your edits to keep, then Save after reviewing the result."));
        var fields = Controller.ChangedDraftFields(); var keep = new HashSet<string>();
        foreach (var key in fields)
        {
            // Show field names rather than dumping source configs, URLs or complete rule documents.
            var pick = new CheckBox { Content = "Keep my change: " + key.Replace('_', ' '), IsChecked = false };
            Identify(pick, "CollectionConflict_" + key, "Keep my change to " + key.Replace('_', ' '));
            Toggled(pick, on => { if (on) keep.Add(key); else keep.Remove(key); return Task.CompletedTask; }); body.Children.Add(pick);
        }
        var dialog = new ContentDialog { Title = "Review changes", Content = new ScrollViewer { Content = body, MaxHeight = 430 }, PrimaryButtonText = "Review selected changes", CloseButtonText = "Keep editing" };
        Identify(dialog, "CollectionConflictDialog", "Review changes");
        if (await ShowAsync(dialog) == ContentDialogResult.Primary && Current(owner))
        { Controller.Rebase(latest, keep); _invalidFields.Clear(); _invalidRules = false; RenderEditor(); Notice("Review the selected changes, then Save. Nothing was written during conflict review."); }
    }
    private async Task BatchAsync(string action, bool wholeView = false)
    {
        // Bulk actions act on the list only: never over an editor or an error view this page has switched to.
        if (_route.Segments.Count > 0 || Controller.List == null) return;
        if (action == "delete" && Controller.StarterJobId != null) { Notice(AdminCollectionCopy.StarterPackBlocksDelete); return; }
        var owner = Controller.Owner!; var selected = Filtered().Where(c => wholeView || _selected.Contains(c.Id)).Take(101).ToArray();
        if (selected.Length > 100) { Notice("Narrow this view to at most 100 collections before deleting it, or use Select collections in batches."); return; }
        if (selected.Length == 0) { Notice(wholeView ? "No collections match." : "Select collections first."); return; }
        var chosen = selected.Length;
        if (action is "show" or "hide")
        {
            // Official setSelectedVisible: only the collections whose visibility differs are counted, confirmed and sent.
            selected = [.. AdminCollectionBulk.ChangingVisibility(selected, action == "show")];
            if (selected.Length == 0) { Notice(action == "show" ? AdminCollectionCopy.AlreadyShown : AdminCollectionCopy.AlreadyHidden); return; }
        }
        var snapshots = new List<AdminCollectionDeletePlan>();
        if (action == "delete")
        {
            // Match official bulk behavior: keep collections used by rows; only individual delete can remove references.
            foreach (var c in selected) snapshots.Add(await _api.PrepareDeleteAsync(owner, c.Id, Controller.Capabilities?.SectionReferences == true));
            if (!Current(owner)) return;
        }
        bool Used(AdminCollectionEntry c) => snapshots.First(p => p.Collection.Value["id"]!.ToString() == c.Id).Rows.Count > 0 || c.RowCount > 0;
        // What is confirmed is exactly what is deleted: the collections no row uses. The others are named as kept.
        var targets = action == "delete" ? selected.Where(c => !Used(c)).ToArray() : selected;
        var keptTitles = action == "delete" ? selected.Where(Used).Select(c => c.Title).ToArray() : [];
        if (action == "delete" && targets.Length == 0) { Notice(AdminCollectionCopy.CollectionsInUse); return; }
        bool? confirmed;
        if (action == "delete")
        {
            var kinds = targets.Select(c => c.Kind).Distinct().ToArray();
            var elsewhere = new List<(string Title, IReadOnlyList<string> Libraries)>();
            if (_library != null)
                foreach (var c in targets) { var others = Controller.Libraries.Where(l => c.InLibrary(l.Id) && l.Id != _library).Select(l => l.Name).ToList(); if (others.Count > 0) elsewhere.Add((c.Title, others)); }
            var kind = kinds.Length == 1 ? kinds[0] : null;
            var bullets = new List<string>();
            if (elsewhere.Count > 0) bullets.Add(AdminCollectionCopy.AlsoDeletedElsewhere(elsewhere, kind));
            if (keptTitles.Length > 0) bullets.Add(AdminCollectionCopy.KeptForRows(keptTitles));
            var where = wholeView ? Controller.Libraries.FirstOrDefault(l => l.Id == _library)?.Name ?? AdminCollectionCopy.ThisView : null;
            confirmed = await ConfirmAsync(AdminCollectionCopy.BulkDeleteTitle(targets.Length, kind, where), AdminCollectionCopy.BulkDeleteDescription(targets.Length),
                AdminCollectionCopy.DeleteCount(targets.Length), owner, destructive: true, bullets: bullets, footer: AdminCollectionCopy.Irreversible,
                blockedNow: () => Controller.StarterJobId != null ? AdminCollectionCopy.StarterPackBlocksDelete : null);
        }
        else
        {
            var note = AdminCollectionCopy.SelectedNote(targets.Length, chosen, action == "show");
            var rowCount = selected.Sum(c => c.RowCount);
            if (action == "hide" && rowCount > 0)
                confirmed = await ConfirmAsync(AdminCollectionCopy.HideManyTitle(targets.Length), AdminCollectionCopy.HideManyDescription(rowCount), AdminCollectionCopy.HideLabel, owner, footer: targets.Length < chosen ? note : null);
            else if (action == "hide") confirmed = await ConfirmAsync(AdminCollectionCopy.HideManyTitle(targets.Length), note, "Hide from tabs", owner);
            else if (action == "show") confirmed = await ConfirmAsync(AdminCollectionCopy.ShowManyTitle(targets.Length), note, "Show on tabs", owner);
            else confirmed = await ConfirmAsync("Sync lists?", note, "Sync lists", owner);
        }
        if (confirmed != true) return;
        var done = 0; var kept = 0; var failures = new List<string>(); var removed = new List<string>();
        Exception? uncertain = null;
        // A stale notice must not be mistaken for the reason a change did not start.
        Notice("");
        var started = await RunAsync(async lease =>
        {
            foreach (var c in targets)
            {
                try
                {
                    if (action == "delete") await _api.DeleteAsync(lease, snapshots.First(p => p.Collection.Value["id"]!.ToString() == c.Id), false);
                    else if (action == "sync") { if (c.Kind != "synced") continue; await _api.SyncAsync(lease, c.Id); }
                    else await _api.SaveAsync(lease, await _api.GetAsync(lease, c.Id), new() { ["visibility"] = action == "show" ? "visible" : "hidden" });
                    done++; if (action == "delete") removed.Add(c.Id);
                }
                catch (Exception error)
                {
                    lease.ThrowIfRetired();
                    // Do not replay any uncertain write. Each other target is independent.
                    if (uncertain == null && AdminCollectionBulk.IsUncertain(error)) uncertain = error;
                    var outcome = action == "delete" ? AdminCollectionBulk.ClassifyDeleteFailure(error) : AdminBulkDeleteOutcome.Failed;
                    if (outcome == AdminBulkDeleteOutcome.Deleted) { done++; removed.Add(c.Id); } // already gone: the official count
                    else if (outcome == AdminBulkDeleteOutcome.Kept) kept++;                       // a row still shows it (409)
                    else failures.Add(AdminCollectionCopy.FailureLine(c.Title, AdminCollectionsController.Message(error)));
                }
            }
            // A write whose outcome is unknown holds the next change until the list is read again, as every other change on this page does.
            if (uncertain != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(uncertain);
        });
        if (!Current(owner)) return;
        // Nothing ran: the refusal's own reason stays in the notice. A result sentence would read as if something had been tried.
        if (!started) { if (_notice.Text.Length == 0) Notice(AdminCollectionCopy.NotStarted); return; }
        // Only a failed re-read after the run leaves text here; it is kept beside the result.
        var reloadNotice = _notice.Text;
        // The unknown outcome's reason is already on its collection's line.
        if (uncertain != null && reloadNotice == AdminCollectionsController.Message(uncertain)) reloadNotice = "";
        // A deleted collection is no longer selected, so the count and the next action never name it.
        foreach (var id in removed) _selected.Remove(id);
        if (removed.Count > 0) RenderRows();
        var total = action == "sync" ? targets.Count(c => c.Kind == "synced") : targets.Length;
        var result = action == "delete" ? AdminCollectionCopy.BulkDeleteResult(targets.Length, done, kept, failures)
            : AdminCollectionCopy.BatchResult(action, done, total) + (failures.Count > 0 ? " " + string.Join(" ", failures.Take(5)) : "");
        Notice(reloadNotice.Length > 0 && result.Length > 0 ? result + " " + reloadNotice : result.Length > 0 ? result : reloadNotice);
    }
    private async Task TemplatesDialogAsync()
    {
        var owner = Controller.Owner!; var catalog = await _api.TemplatesAsync(owner); if (!Current(owner)) return;
        var body = Stack(12); var templateHost = Stack(12);
        var search = new TextBox { PlaceholderText = "Search templates", MaxLength = 256 }; body.Children.Add(search); body.Children.Add(templateHost);
        var dialog = new ContentDialog { Title = "Collection templates", Content = new ScrollViewer { Content = body, MaxHeight = 450 }, CloseButtonText = "Cancel" };
        void RenderTemplates()
        {
            templateHost.Children.Clear();
            var templates = (catalog["categories"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(c => (c["templates"] as JsonArray ?? []).OfType<JsonObject>())
                .Where(t => Controller.Capabilities?.ImportSources.Contains(t["source"]?.ToString() ?? "") == true && (t["title"]?.ToString() ?? "").Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).Take(201).ToArray();
            foreach (var template in templates.Take(200))
            {
                templateHost.Children.Add(ActionButton(template["title"]?.ToString() ?? "Template", "CollectionTemplate_" + template["id"], () => { dialog.Tag = template; dialog.Hide(); return Task.CompletedTask; }));
                if (template["description"] != null) templateHost.Children.Add(Text(template["description"]!.ToString(), 13, true));
            }
            if (templates.Length > 200) templateHost.Children.Add(Text("More templates match. Refine your search.", 13, true));
            if (templates.Length == 0) templateHost.Children.Add(Text("No templates match."));
        }
        search.TextChanged += (_, _) => RenderTemplates(); RenderTemplates();
        await ShowAsync(dialog);
        if (Current(owner) && dialog.Tag is JsonObject chosen)
        {
            // Open a real new editor and seed its draft after the normal libraries/capabilities read.
            _searchTimer.Stop(); _jobTimer.Stop(); Controller.EndStarterJob();
            await Controller.EditAsync(null, "synced", _library);
            var draft = Controller.Draft!; var source = chosen["source"]!.ToString();
            draft["title"] = chosen["title"]?.DeepClone(); draft["description"] = chosen["description"]?.DeepClone() ?? JsonValue.Create("");
            draft["collection_type"] = source == "mdblist" ? "mdblist" : "tmdb";
            var config = (chosen[source] as JsonObject)?.DeepClone() as JsonObject ?? new(); config["mode"] = source == "mdblist" ? "mdblist_json" : source == "tmdb_list" ? "tmdb_list" : "tmdb_preset";
            draft["source_config"] = config;
            // The template's poster is sent with the import unless a poster is staged.
            if (chosen["poster_path"] is JsonValue poster && poster.TryGetValue<string>(out var posterPath) && !string.IsNullOrWhiteSpace(posterPath)) draft["poster_url"] = posterPath;
            if (chosen["default_limit"] is { } limit) config["limit"] = limit.DeepClone();
            if (chosen["default_sync_schedule"] is { } schedule) draft["sync_schedule"] = schedule.DeepClone();
            _route = _route with { Segments = ["new"] }; AdminShellPage.TryReplace(this, _route); RenderEditor();
        }
    }
    private async Task StarterPacksAsync()
    {
        if (Controller.StarterJobId != null) { Notice("A starter pack is already being added. Try again when it finishes."); return; }
        var owner = Controller.Owner!; var catalog = await _api.BundlesAsync(owner); if (!Current(owner)) return;
        var body = Stack(12); var libraries = new HashSet<string>(); if (_library != null) libraries.Add(_library);
        foreach (var library in Controller.Libraries)
        {
            var libraryName = AdminCollectionNames.PlainOr(library.Name, AdminCollectionNames.UntitledLibrary);
            var check = new CheckBox { Content = libraryName, IsChecked = libraries.Contains(library.Id) };
            Identify(check, "CollectionPackLibrary_" + library.Id, libraryName);
            Toggled(check, on => { if (on) libraries.Add(library.Id); else libraries.Remove(library.Id); return Task.CompletedTask; }); body.Children.Add(check);
        }
        var bundles = (catalog["bundles"] as JsonArray)?.OfType<JsonObject>().Take(200).ToArray() ?? [];
        if (bundles.Length == 0) { Notice("No starter packs are available."); return; }
        var bundle = bundles[0]["id"]!.ToString();
        var homeTemplate = ""; var homeLibrary = _library ?? Controller.Libraries.FirstOrDefault()?.Id ?? "";
        var featuredLibraries = new Dictionary<string, string>();
        var featuredHost = Stack(8);
        JsonObject Featured()
        {
            var value = new JsonObject(); var perLibrary = new JsonObject();
            foreach (var (library, template) in featuredLibraries.Where(p => libraries.Contains(p.Key) && p.Value.Length > 0)) perLibrary[library] = template;
            if (perLibrary.Count > 0) value["libraries"] = perLibrary;
            if (homeTemplate.Length > 0 && libraries.Contains(homeLibrary)) value["home"] = new JsonObject { ["library_id"] = homeLibrary, ["template_id"] = homeTemplate };
            return value;
        }
        void RenderFeatured()
        {
            featuredHost.Children.Clear();
            var templates = (bundles.First(b => b["id"]!.ToString() == bundle)["templates"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
            if (templates.Length == 0) return;
            var choices = templates.Select(t => (t["id"]!.ToString(), t["title"]!.ToString())).Prepend(("", "Keep existing featured row")).ToArray();
            featuredHost.Children.Add(Text("Featured collections", 16));
            featuredHost.Children.Add(Text("Home", 14));
            featuredHost.Children.Add(Select("CollectionPackHomeTemplate", choices, homeTemplate, value => { homeTemplate = value; return Task.CompletedTask; }));
            featuredHost.Children.Add(Select("CollectionPackHomeLibrary", Controller.Libraries.Select(l => (l.Id, l.Name)), homeLibrary, value => { homeLibrary = value; return Task.CompletedTask; }));
            foreach (var library in Controller.Libraries)
            {
                featuredHost.Children.Add(Text(AdminCollectionNames.PlainOr(library.Name, AdminCollectionNames.UntitledLibrary) + " page", 14));
                featuredHost.Children.Add(Select("CollectionPackFeatured_" + library.Id, choices, featuredLibraries.GetValueOrDefault(library.Id, ""), value => { featuredLibraries[library.Id] = value; return Task.CompletedTask; }));
            }
        }
        body.Children.Add(Select("CollectionPack", bundles.Select(b => (b["id"]!.ToString(), b["title"]?.ToString() ?? b["name"]?.ToString() ?? b["id"]!.ToString())), bundle, value => { bundle = value; return Task.CompletedTask; }));
        // A pack switch resets template identities before another preview is accepted.
        var packChoice = body.Children.OfType<ComboBox>().Last();
        packChoice.SelectionChanged += (_, _) => { homeTemplate = ""; featuredLibraries.Clear(); RenderFeatured(); };
        RenderFeatured(); body.Children.Add(featuredHost);
        var replace = new CheckBox { Content = "Delete existing collections", IsChecked = false }; body.Children.Add(replace);
        body.Children.Add(Text("Collections used by rows or shared with unselected libraries are kept.", 13, true));
        var resultHost = Stack(8); var resultSignature = "";
        var dialog = new ContentDialog { Title = "Starter packs", Content = new ScrollViewer { Content = body, MaxHeight = 470 }, PrimaryButtonText = "Add starter pack", CloseButtonText = "Cancel", IsPrimaryButtonEnabled = false };
        Identify(dialog, "CollectionStarterPacksDialog", "Starter packs");
        string Signature() => bundle + ":" + string.Join(",", libraries.Order()) + ":" + replace.IsChecked + ":" + Featured().ToJsonString();
        body.Children.Add(ActionButton("Preview", "CollectionPackPreview", async () =>
        {
            if (libraries.Count == 0) throw new ArgumentException("Pick libraries first.");
            var signature = Signature();
            var result = await _api.ApplyBundleAsync(owner, bundle, libraries.ToArray(), true, replace.IsChecked == true, Featured());
            if (!Current(owner) || Signature() != signature) return;
            resultSignature = signature; RenderTemplateResult(resultHost, result.Value); dialog.IsPrimaryButtonEnabled = true;
        }));
        body.Children.Add(resultHost);
        var accepted = false;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; var deferral = args.GetDeferral();
            try
            {
                if (Signature() != resultSignature || libraries.Count == 0) { resultHost.Children.Add(Text("Preview these choices before adding the pack.")); return; }
                var job = await Controller.CommandAsync(lease => _api.ApplyBundleAsync(lease, bundle, libraries.ToArray(), false, replace.IsChecked == true, Featured()));
                // Remembered only for the authority that queued it; a retirement between the answer and here leaves nothing behind to block deletes.
                accepted = Controller.BeginStarterJob(owner, AdminCollectionsApi.CanonicalId(job.Value["id"]) ?? throw new InvalidDataException("The starter pack job was not identified."));
                args.Cancel = false;
            }
            catch (Exception error) { if (Current(owner)) { resultHost.Children.Add(Text(AdminCollectionsController.Message(error))); dialog.IsPrimaryButtonEnabled = false; } }
            finally { deferral.Complete(); }
        };
        await ShowAsync(dialog);
        if (accepted && Current(owner)) { Notice("Adding a starter pack. This is queued work, not completion."); _jobTimer.Start(); }
    }
    private static void RenderTemplateResult(StackPanel host, JsonObject result)
    {
        host.Children.Clear();
        foreach (var key in new[] { "created", "skipped", "failed", "deleted", "delete_skipped", "delete_failed", "sync_queued", "featured", "featured_failed" })
        {
            if (result[key] is not JsonArray entries || entries.Count == 0) continue;
            host.Children.Add(Text($"{key.Replace('_', ' ')}: {entries.Count}", 14));
            foreach (var item in entries.OfType<JsonObject>().Take(20)) host.Children.Add(Text($"{item["template_title"] ?? item["collection_title"]} · {item["library_name"]} · {item["reason"]}", 12, true));
        }
    }
    private async Task PollJobAsync()
    {
        var owner = Controller.Owner; var id = Controller.StarterJobId; if (id == null || !Current(owner)) return;
        try
        {
            var response = await _api.JobAsync(owner!, id); if (!Current(owner) || Controller.StarterJobId != id) return;
            var terminal = response.Value["terminal"]?.GetValue<bool>() == true;
            Notice("Starter pack: " + response.Value["state"]);
            if (terminal)
            {
                Controller.EndStarterJob(); await Controller.LoadAsync(_library, _arrange);
                if (Current(owner)) { RenderList(); var result = Stack(); RenderTemplateResult(result, response.Value["template_result"] as JsonObject ?? new()); _surface.Children.Add(result); }
            }
            else if (Controller.CountStarterJobRead() < 120) _jobTimer.Start();
            else
            {
                // Observation ends here. Keeping the id would block deletes and starter packs while Refresh could never rediscover the job.
                Controller.EndStarterJob();
                Notice("The starter pack is still running. Refresh to review its collections; it was not queued again.");
            }
        }
        catch (Exception error)
        {
            if (!Current(owner)) return;
            // A failed read stops the observation; Refresh rediscovers the job instead of leaving its id blocking writes with no timer.
            if (Controller.StarterJobId == id) Controller.EndStarterJob();
            Notice(AdminCollectionsController.Message(error) + " The starter pack was not queued again.");
        }
    }
    private async Task ResumeJobAsync()
    {
        var owner = Controller.Owner; if (Controller.StarterJobId != null || !Current(owner)) return;
        try
        {
            var response = await _api.RecentJobsAsync(owner!); if (!Current(owner)) return;
            var active = (response["items"] as JsonArray)?.OfType<JsonObject>().Take(10).FirstOrDefault(j => j["terminal"]?.GetValue<bool>() == false);
            if (active?["id"] is not { } id) return;
            Controller.BeginStarterJob(AdminCollectionsApi.CanonicalId(id) ?? throw new InvalidDataException("Job identity was not recognized.")); Notice("Adding a starter pack"); _jobTimer.Start();
        }
        catch (Exception) { /* Optional recent-job discovery must not hide a successfully loaded collection page. */ }
    }
}
