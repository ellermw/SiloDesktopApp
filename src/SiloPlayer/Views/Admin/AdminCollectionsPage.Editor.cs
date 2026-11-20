using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage
{
    private readonly Dictionary<string, (string? Source, byte[]? Bytes, string MediaType)> _artwork = [];
    private readonly Dictionary<string, string> _pendingTitles = [];
    // Rebuilt on every render: a panel cannot move to a new card while the old card still owns it.
    private StackPanel _contents = Stack(12);
    private StackPanel _preview = Stack(10);
    private readonly Stack<string?> _memberHistory = [];
    private string? _memberCursor;
    private QueryRulesEditor? _rules;
    private bool _invalidRules;
    private readonly HashSet<string> _invalidFields = [];
    private void RenderEditor()
    {
        _surface.Children.Clear();
        var draft = Controller.Draft; if (draft == null) return;
        var saved = Controller.Editor; var id = saved?.Value["id"]?.GetValue<string>();
        var type = draft["collection_type"]?.GetValue<string>() ?? "manual";
        var manual = type == "manual"; var smart = type == "smart";
        var top = Wrap();
        top.Children.Add(ActionButton("Back to Collections", "CollectionEditorBack", async () => { if (await CanLeaveAsync()) ReturnToList(); }));
        top.Children.Add(ActionButton(id == null ? "Create collection" : "Save", "CollectionEditorSave", SaveEditorAsync, !Controller.OutcomeUnknown));
        top.Children.Add(ActionButton("Discard changes", "CollectionEditorDiscard", () => { Controller.Discard(); ClearStaged(); RenderEditor(); return Task.CompletedTask; }));
        top.Children.Add(ActionButton("Reload and review", "CollectionEditorReload", async () =>
        {
            // A confirmed create whose canonical read failed reloads by its identity; it is never started again as a blank draft.
            if (await CanLeaveAsync()) { ClearStaged(); await Controller.ReloadEditorAsync(type, _library);
                // A reloaded confirmed create is a saved collection: /new gives way to its identity.
                if (Controller.Editor?.Value["id"]?.ToString() is { } reloaded && _route.Segment(0) == "new") { _route = _route with { Segments = [reloaded, "edit"] }; AdminShellPage.TryReplace(this, _route); }
                RenderEditor(); Notice(""); }
        }));
        if (Controller.OutcomeUnknown) top.Children.Add(ActionButton("Check the collections list", "CollectionEditorCheckList", CheckListAfterUnknownAsync));
        if (id != null) top.Children.Add(ActionButton("Delete…", "CollectionEditorDelete", () => DeleteDialogAsync(id)));
        _surface.Children.Add(top);
        _surface.Children.Add(Text(id == null ? "New collection" : draft["title"]?.GetValue<string>() ?? "Collection", 28));
        _surface.Children.Add(Text(manual ? "Manual" : smart ? "Smart" : "Synced list", 14, true));
        var details = Stack(12); details.Children.Add(Text("Details", 20));
        Input(details, "Name", "CollectionName", draft["title"]?.GetValue<string>() ?? "", value => draft["title"] = value);
        Input(details, "Description", "CollectionDescription", draft["description"]?.GetValue<string>() ?? "", value => draft["description"] = value, true);
        _surface.Children.Add(Card(details));

        var where = Stack(12); where.Children.Add(Text("Where it shows", 20)); where.Children.Add(Text("Libraries", 14));
        var selectedLibraries = draft["library_ids"] as JsonArray ?? new JsonArray();
        var libraryRow = Wrap();
        foreach (var library in Controller.Libraries)
        {
            var pick = new CheckBox { Content = library.Name, IsChecked = selectedLibraries.Any(v => v?.ToString() == library.Id) };
            Identify(pick, "CollectionLibrary_" + library.Id, library.Name);
            Toggled(pick, async on =>
            {
                if (!on && saved != null && await ConfirmAsync("Remove this library?", "Titles available only in this library will be removed from the collection. Your files are kept.", "Remove library", Controller.Owner!) != true)
                { SetQuietly(pick, true); return; }
                var ids = (draft["library_ids"] as JsonArray)?.Select(v => v!.ToString()).Where(v => v != library.Id).ToList() ?? [];
                if (on) ids.Add(library.Id);
                draft["library_ids"] = JsonSerializer.SerializeToNode(ids); _memberCursor = null; _memberHistory.Clear();
            });
            libraryRow.Children.Add(pick);
        }
        where.Children.Add(libraryRow);
        var visibility = new ToggleSwitch { Header = "Show on Collections tabs", IsOn = draft["visibility"]?.GetValue<string>() != "hidden" };
        Identify(visibility, "CollectionEditorVisibility", "Show on Collections tabs");
        var changingVisibility = false;
        visibility.Toggled += async (_, _) =>
        {
            if (changingVisibility) return; changingVisibility = true; var owner = Controller.Owner;
            try
            {
                var target = visibility.IsOn;
                var used = !target && id != null && Controller.Capabilities?.SectionReferences == true && (await _api.RowsAsync(owner!, id)).Count > 0;
                if (!target && used && await ConfirmAsync("Hide from Collections tabs?", "Rows still show it. See all will be unavailable.", "Hide", owner!) != true) return;
                if (Current(owner) && ReferenceEquals(draft, Controller.Draft)) draft["visibility"] = target ? "visible" : "hidden";
            }
            catch (Exception error) { if (Current(owner)) Notice(AdminCollectionsController.Message(error)); }
            finally { visibility.IsOn = draft["visibility"]?.ToString() != "hidden"; changingVisibility = false; }
        }; where.Children.Add(visibility);
        if (id != null && Controller.Capabilities?.SectionReferences == true) where.Children.Add(ActionButton("Rows that show it", "CollectionRows", () => ShowRowsAsync(id)));
        if (id != null) where.Children.Add(ActionButton("Add as a row", "CollectionAddAsRow", () => AddAsRowAsync(id)));
        _surface.Children.Add(Card(where));

        _contents = Stack(12); _preview = Stack(10); _storedSort = Stack(8);
        _contents.Children.Add(Text(manual ? "Titles" : smart ? "Rules" : "List", 20));
        if (manual)
        {
            _contents.Children.Add(ActionButton("Add titles", "CollectionAddTitles", TitlePickerAsync));
            _contents.Children.Add(_preview);
            _ = LoadMembersAsync();
        }
        else if (smart) RenderSmart(draft);
        else RenderSynced(draft, id);
        _surface.Children.Add(Card(_contents));
        RenderOrder(draft, smart);
        if (Controller.Capabilities?.Artwork == true) RenderArtwork(id);
    }
    /// <summary>A save whose outcome is unknown is answered by the canonical list, never by a replay.</summary>
    private async Task CheckListAfterUnknownAsync()
    {
        var owner = Controller.Owner; var title = Controller.Draft?["title"]?.ToString()?.Trim() ?? "";
        await Controller.LoadAsync(_library, false);
        if (!Current(owner)) return;
        var match = (Controller.List?.Items ?? []).FirstOrDefault(c => string.Equals(c.Title, title, StringComparison.CurrentCultureIgnoreCase));
        RenderEditor();
        Notice(match == null ? "No collection with that name exists yet. You can create it now."
            : $"A collection named {match.Title} already exists in the list. Open it from Collections instead of creating it again.");
    }
    private void ClearStaged() { _artwork.Clear(); _pendingTitles.Clear(); _memberCursor = null; _memberHistory.Clear(); _rules = null; _invalidRules = false; _invalidFields.Clear(); }
    private async Task SaveEditorAsync()
    {
        if (Controller.Busy) return;
        var owner = Controller.Owner;
        if (_invalidRules || _rules?.IsValid == false || _invalidFields.Count > 0) { Notice("Complete the rules and title limits before saving."); return; }
        try
        {
            IsHitTestVisible = false;
            var id = await Controller.SaveAsync();
            if (!Current(owner)) return;
            // Replace /new immediately, even if a later membership or artwork operation fails.
            _route = _route with { Segments = [id, "edit"] }; AdminShellPage.TryReplace(this, _route);
            var position = 0;
            foreach (var (item, _) in _pendingTitles.ToArray())
            {
                await Controller.CommandAsync(async lease => await _api.MembershipAsync(lease, id, item, true, position++));
                _pendingTitles.Remove(item);
            }
            foreach (var (slot, art) in _artwork.ToArray())
            {
                await Controller.CommandAsync(async lease => await _api.ArtworkAsync(lease, id, slot, art.Source, art.Bytes, art.MediaType));
                _artwork.Remove(slot);
            }
            await Controller.EditAsync(id);
            if (Current(owner)) { RenderEditor(); Notice("Collection saved"); }
        }
        catch (ApiException error) when (error.StatusCode == 412)
        { if (Current(owner)) { Notice(AdminCollectionsController.Message(error)); await ReviewConflictAsync(); } }
        catch (Exception error)
        {
            if (!Current(owner)) return;
            Notice(Controller.CreatedId != null && Controller.Editor == null
                ? "The collection was created, but its saved copy could not be read. Press Save to load it; it will not be created again."
                : AdminCollectionsController.Message(error));
            if (Controller.OutcomeUnknown) RenderEditor();
        }
        finally { IsHitTestVisible = true; }
    }
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private StackPanel _storedSort = Stack(8);
    private CancellationTokenSource? _previewReads;
    private int _previewSerial;
    private void RenderSmart(JsonObject draft)
    {
        var owner = Controller.Owner!;
        var query = (draft["query_definition"] as JsonObject)?.Deserialize<QueryDefinition>(AdminCollectionsApi.Json) ?? new();
        _rules = new QueryRulesEditor { AllowPersonalizedFilters = false, AllowPersonalizedSorts = false, CollectionPresentation = false, ExtendedRules = false, ShownRatingSources = new HashSet<string>(),
            CapabilitiesLoader = ct => _api.QueryCapabilitiesAsync(owner, ct),
            PeopleSearch = (_, text, ct) => _api.PeopleAsync(owner, text, ct),
            FacetSearch = (field, text, ct) => _api.FacetsAsync(owner, field, text, _library, ct),
            LanguageLoader = (field, ct) => _api.LanguagesAsync(owner, field, _library, ct)
        };
        _contents.Children.Add(Text("Titles that match are in the collection. New matches join on their own.", 13, true));
        // The rules name their own libraries (none: every library). Where it shows is picked separately and never rewrites them.
        var libraries = Stack(8); libraries.Children.Add(Text("Titles from", 15));
        var libraryRow = Wrap();
        foreach (var library in Controller.Libraries)
        {
            var pick = new CheckBox { Content = library.Name, IsChecked = query.LibraryIds.Any(v => v.ToString() == library.Id) };
            Identify(pick, "CollectionRuleLibrary_" + library.Id, library.Name);
            Toggled(pick, on =>
            {
                var current = (draft["query_definition"]?["library_ids"] as JsonArray)?.Select(v => v?.ToString() ?? "").ToList() ?? [];
                current.Remove(library.Id); if (on) current.Add(library.Id);
                Controller.SetRuleLibraries(current);
                _rules.Query.LibraryIds = current.Select(int.Parse).ToList();
                SchedulePreview(); return Task.CompletedTask;
            });
            libraryRow.Children.Add(pick);
        }
        libraries.Children.Add(libraryRow);
        libraries.Children.Add(Text("None picked means every library.", 12, true));
        _contents.Children.Add(libraries);
        _rules.Load(query);
        _rules.Changed += () => { draft["query_definition"] = JsonSerializer.SerializeToNode(_rules.Query, AdminCollectionsApi.Json); _invalidRules = !_rules.IsValid; SchedulePreview(); };
        // A saved default sort wins over the rules' Order, so changing Order clears it.
        _rules.SortChanged += () => { Controller.ClearStoredDefaultSort(); RenderStoredSort(); };
        Identify(_rules, "CollectionRules", "Collection rules"); _contents.Children.Add(_rules);
        RenderStoredSort(); _contents.Children.Add(_storedSort);
        Input(_contents, "Max titles (blank for no limit)", "CollectionSmartLimit", query.Limit is null or 10000000 ? "" : query.Limit.ToString()!, value =>
        {
            if (string.IsNullOrWhiteSpace(value)) { _rules.Query.Limit = null; _invalidFields.Remove("smart-limit"); }
            else if (int.TryParse(value, out var limit) && limit > 0) { _rules.Query.Limit = limit; _invalidFields.Remove("smart-limit"); }
            else { _invalidFields.Add("smart-limit"); return; }
            draft["query_definition"] = JsonSerializer.SerializeToNode(_rules.Query, AdminCollectionsApi.Json); SchedulePreview();
        });
        _contents.Children.Add(Text("A profile that picks its own sort while browsing keeps that choice.", 13, true));
        var advanced = Stack(10);
        Input(advanced, "Advanced rules", "CollectionRulesJson", (draft["query_definition"] ?? new JsonObject()).ToJsonString(), value =>
        {
            try { var parsed = JsonNode.Parse(value, documentOptions: new() { MaxDepth = 32 })!.AsObject(); _rules.Load(parsed.Deserialize<QueryDefinition>(AdminCollectionsApi.Json)!); draft["query_definition"] = parsed; _invalidRules = false; SchedulePreview(); }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { _invalidRules = true; }
        }, true);
        _contents.Children.Add(new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var live = Wrap(); live.Children.Add(Text("● Live preview", 14, true));
        live.Children.Add(ActionButton("Refresh preview", "CollectionPreview", () => RefreshPreviewAsync()));
        _contents.Children.Add(live); _contents.Children.Add(_preview);
        SchedulePreview();
    }
    private void RenderStoredSort()
    {
        _storedSort.Children.Clear();
        if (Controller.StoredDefaultSort is not { } stored) return;
        var field = stored["field"]!.ToString(); var order = stored["order"]?.ToString() == "asc" ? "ascending" : "descending";
        var label = field.Replace('_', ' ') + ", " + order;
        var row = Wrap(); row.Children.Add(Text($"A saved default sort, {label}, wins over this Order.", 13, true));
        row.Children.Add(ActionButton("Clear", "CollectionClearStoredSort", () => { Controller.ClearStoredDefaultSort(); RenderStoredSort(); return Task.CompletedTask; }));
        _storedSort.Children.Add(row);
    }
    private void SchedulePreview() { _previewTimer.Stop(); _previewTimer.Start(); }
    /// <summary>The live preview of the unsaved rules. A read-only question: it never gates saving and never marks an outcome unknown.</summary>
    private async Task RefreshPreviewAsync()
    {
        _previewTimer.Stop();
        var draft = Controller.Draft; var owner = Controller.Owner; var rules = _rules;
        if (draft == null || rules == null || !Current(owner) || draft["collection_type"]?.ToString() != "smart") return;
        _previewReads?.Cancel(); _previewReads = CancellationTokenSource.CreateLinkedTokenSource(owner!.Token); var reads = _previewReads; var serial = ++_previewSerial;
        _preview.Children.Clear();
        if (!AdminCollectionsController.HasCompleteRule(rules.Query))
        {
            _preview.Children.Add(Text("Add a rule to see what it matches", 15));
            _preview.Children.Add(Text("A smart collection needs at least one rule. Without one it would hold every title.", 13, true)); return;
        }
        if (_invalidRules || !rules.IsValid) { _preview.Children.Add(Text("Complete the rules to see what they match.", 13, true)); return; }
        _preview.Children.Add(Text("Loading preview…", 13, true));
        try
        {
            var query = (JsonObject)(draft["query_definition"] as JsonObject ?? new JsonObject()).DeepClone();
            query["library_ids"] = AdminCollectionsApi.RuleLibraryIds(query["library_ids"]);
            var result = await _api.PreviewAsync(owner, query, reads.Token);
            if (!Current(owner) || serial != _previewSerial) return;
            _preview.Children.Clear();
            _preview.Children.Add(Text((result.Total == 1 ? "1 title matches" : $"{result.Total} titles match") + (result.Items.Count > 0 && result.Total > result.Items.Count ? $" · showing {result.Items.Count}" : "") + " · Preview shows unsaved changes", 13, true));
            if (result.Items.Count == 0)
            {
                _preview.Children.Add(Text("No titles match yet", 15));
                _preview.Children.Add(Text("You can still save. Titles that match later join on their own.", 13, true));
                return;
            }
            var grid = Wrap();
            foreach (var item in result.Items) { var tile = Stack(4); tile.Width = 110; tile.Children.Add(Text(item.Title, 12)); grid.Children.Add(tile); }
            _preview.Children.Add(grid);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (Current(owner) && serial == _previewSerial) { _preview.Children.Clear(); _preview.Children.Add(Text("The preview didn’t load. You can still save.", 13, true)); }
        }
    }
    private void RenderOrder(JsonObject draft, bool smart)
    {
        if (smart) return; // The grouped rule editor owns smart Order; synced lists render their own below.
        var order = Stack(12); order.Children.Add(Text("Order", 20));
        order.Children.Add(Text("Your order. Drag titles to change it.", 14, true));
        _surface.Children.Add(Card(order));
    }
    private void RenderSynced(JsonObject draft, string? id)
    {
        var config = (JsonObject)(draft["source_config"] as JsonObject ?? new()).DeepClone(); draft["source_config"] = config;
        var mdblist = draft["collection_type"]?.ToString() == "mdblist";
        var mode = config["mode"]?.ToString() ?? "";
        var source = mdblist ? "mdblist" : mode == "tmdb_list" ? "tmdb_list" : mode == "tmdb_collection" ? "tmdb_collection" : mode == "tmdb_discover" ? "tmdb_discover" : "tmdb";
        if (id == null)
        {
            var options = new List<(string, string)>();
            if (Controller.Capabilities?.ImportSources.Contains("mdblist") == true) options.Add(("mdblist", "MDBList"));
            if (Controller.Capabilities?.ImportSources.Contains("tmdb_list") == true) options.Add(("tmdb_list", "TMDB list"));
            if (Controller.Capabilities?.ImportSources.Contains("tmdb") == true) options.Add(("tmdb", "TMDB chart"));
            _contents.Children.Add(Select("CollectionListSource", options, source, value =>
            {
                draft["collection_type"] = value == "mdblist" ? "mdblist" : "tmdb";
                draft["source_config"] = new JsonObject { ["mode"] = value == "tmdb_list" ? "tmdb_list" : value == "mdblist" ? "mdblist_json" : "tmdb_preset" };
                RenderEditor(); return Task.CompletedTask;
            }));
        }
        else _contents.Children.Add(Text(source switch { "mdblist" => "MDBList", "tmdb_list" => "TMDB list", "tmdb_collection" => "TMDB franchise", "tmdb_discover" => "TMDB Discover", _ => "TMDB chart" }, 15));
        if (source is "mdblist" or "tmdb_list")
        {
            if (source == "mdblist" && id == null) RenderMdbListSearch(draft, config);
            var url = config["url"]?.ToString() ?? draft["source_url"]?.ToString() ?? "";
            Input(_contents, source == "mdblist" ? "MDBList URL" : "TMDB list URL or ID", "CollectionListUrl", url, value => { config["url"] = value; draft["source_url"] = value; config["mode"] = source == "mdblist" ? "mdblist_json" : "tmdb_list"; });
        }
        else if (source == "tmdb")
        {
            if (id == null) AdminCollectionsController.NormalizeChart(config);
            void ChangedChart()
            {
                AdminCollectionsController.NormalizeChart(config);
                draft["source_url"] = "tmdb://" + config["preset"] + "/" + config["media_type"] + (config["time_window"] == null ? "" : "/" + config["time_window"]);
                RenderEditor();
            }
            var preset = config["preset"]?.ToString() ?? "trending";
            _contents.Children.Add(Select("CollectionChart", [("trending", "Trending"), ("popular", "Popular"), ("top_rated", "Top rated"), ("now_playing", "In theaters"), ("upcoming", "Upcoming"), ("airing_today", "Airing today"), ("on_the_air", "On the air")], preset, value => { config["preset"] = value; ChangedChart(); return Task.CompletedTask; }));
            (string, string)[] mediaOptions = preset switch
            {
                "trending" => new[] { ("movie", "Movies"), ("tv", "TV shows"), ("all", "Both") },
                "now_playing" or "upcoming" => [("movie", "Movies")],
                "airing_today" or "on_the_air" => [("tv", "TV shows")],
                _ => [("movie", "Movies"), ("tv", "TV shows")]
            };
            _contents.Children.Add(Select("CollectionChartMedia", mediaOptions, config["media_type"]?.ToString() ?? "all", value => { config["media_type"] = value; ChangedChart(); return Task.CompletedTask; }));
            if (preset == "trending") _contents.Children.Add(Select("CollectionChartWindow", [("day", "Today"), ("week", "This week")], config["time_window"]?.ToString() ?? "day", value => { config["time_window"] = value; ChangedChart(); return Task.CompletedTask; }));
        }
        else if (source == "tmdb_collection") Input(_contents, "TMDB collection ID", "CollectionFranchise", config["collection_id"]?.ToString() ?? "", value =>
        {
            if (int.TryParse(value, out var number) && number > 0) { config["collection_id"] = number; draft["source_url"] = "tmdb://collection/" + number; _invalidFields.Remove("franchise"); }
            else _invalidFields.Add("franchise");
        });
        else _contents.Children.Add(Text("This list keeps its saved source. Its source settings are shown below.", 13, true));
        Input(_contents, "Max titles (blank for no limit)", "CollectionListLimit", config["limit"]?.ToString() ?? "", value =>
        {
            if (string.IsNullOrWhiteSpace(value)) { config.Remove("limit"); _invalidFields.Remove("list-limit"); }
            else if (int.TryParse(value, out var number) && number > 0) { config["limit"] = number; _invalidFields.Remove("list-limit"); }
            else _invalidFields.Add("list-limit");
        });
        RenderSyncedSort(draft);
        _contents.Children.Add(Select("CollectionSchedulePreset", [("", "Manual only"), ("0 * * * *", "Every hour"), ("0 */6 * * *", "Every 6 hours"), ("0 3 * * *", "Every day at 3:00 AM"), ("0 3 * * 1", "Every Monday at 3:00 AM"), ("0 3 * * 0", "Every Sunday at 3:00 AM"), ("0 3 1 * *", "Monthly on the 1st at 3:00 AM")], draft["sync_schedule"]?.ToString() ?? "", value => { draft["sync_schedule"] = value; RenderEditor(); return Task.CompletedTask; }));
        Input(_contents, "Custom schedule (server time)", "CollectionSchedule", draft["sync_schedule"]?.ToString() ?? "", value => draft["sync_schedule"] = value);
        if (id != null)
        {
            _contents.Children.Add(Text($"{Controller.Editor?.Value["last_sync_status"]} · {Controller.Editor?.Value["last_sync_at"]} · Next: {Controller.Editor?.Value["next_sync_at"]}", 13, true));
            if (Controller.Capabilities?.Imports == true) _contents.Children.Add(ActionButton("Sync now", "CollectionEditorSync", async () =>
            {
                if (Controller.Dirty) { Notice("Save changes before syncing. Sync now uses the saved list."); return; }
                await RunAsync(async lease => { await _api.SyncAsync(lease, id); }, false); await Controller.EditAsync(id); RenderEditor();
            }));
            _contents.Children.Add(ActionButton("Preview titles", "CollectionSavedPreview", async () =>
            {
                var library = (draft["library_ids"] as JsonArray)?.FirstOrDefault()?.ToString();
                if (library == null) throw new ArgumentException("Pick libraries first.");
                var owner = Controller.Owner!; var response = await _api.SavedPreviewAsync(owner, id, library);
                if (!Current(owner)) return;
                _preview.Children.Clear();
                foreach (var item in (response["items"] as JsonArray ?? []).OfType<JsonObject>().Take(24)) _preview.Children.Add(Text(item["title"]?.ToString() ?? ""));
                _preview.Children.Add(Text("Preview of the saved list · Up to 24 titles", 13, true));
            }));
            _contents.Children.Add(_preview);
        }
        var advanced = Stack();
        Input(advanced, "Saved source", "CollectionSourceJson", config.ToJsonString(), value =>
        {
            try { config = JsonNode.Parse(value, documentOptions: new() { MaxDepth = 32 })!.AsObject(); draft["source_config"] = config; _invalidRules = false; }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { _invalidRules = true; }
        }, true);
        _contents.Children.Add(new Expander { Header = "Source settings", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
    }
    private static readonly (string Value, string Label)[] SyncedSorts =
    [
        ("", "List order"), ("title", "Title"), ("added_at", "Date Added"), ("release_date", "Release Date"), ("year", "Year"), ("content_rating", "Content Rating"),
        ("runtime", "Duration"), ("rating_imdb", "IMDb Rating"), ("rating_tmdb", "TMDB Rating"), ("rating_rt_critic", "RT Critic Rating"), ("rating_rt_audience", "RT Audience Rating"),
        ("resolution", "Resolution"), ("bitrate", "Bitrate")
    ];
    /// <summary>The default sort viewers land on for a synced list. List order keeps the source order and stores {}.</summary>
    private void RenderSyncedSort(JsonObject draft)
    {
        if (draft["sort_config"] is not JsonObject sort) draft["sort_config"] = sort = new JsonObject();
        var field = sort["field"]?.ToString() ?? "";
        var options = SyncedSorts.AsEnumerable(); if (field.Length > 0 && !SyncedSorts.Any(o => o.Value == field)) options = options.Append((field, field.Replace('_', ' ')));
        var row = Wrap(); row.Children.Add(Text("Default sort", 14));
        row.Children.Add(Select("CollectionDefaultSort", options, field, value =>
        {
            var next = new JsonObject(); if (value.Length > 0) { next["field"] = value; next["order"] = value is "title" or "content_rating" ? "asc" : "desc"; }
            draft["sort_config"] = next; RenderEditor(); return Task.CompletedTask;
        }));
        var alphabetical = field is "title" or "content_rating"; var date = field is "added_at" or "release_date" or "year";
        row.Children.Add(Select("CollectionDefaultDirection", field.Length == 0 ? [("", "As listed")] : [("desc", alphabetical ? "Z–A" : date ? "Newest first" : "Highest first"), ("asc", alphabetical ? "A–Z" : date ? "Oldest first" : "Lowest first")],
            field.Length == 0 ? "" : sort["order"]?.ToString() ?? "desc", value => { if (field.Length > 0) sort["order"] = value; return Task.CompletedTask; }));
        _contents.Children.Add(row);
        _contents.Children.Add(Text("Blank takes the whole list: up to 10,000 titles from MDBList, or the first 20 of a TMDB chart. A profile that picks its own sort while browsing keeps that choice.", 13, true));
    }
    private CancellationTokenSource? _mdbListReads;
    private int _mdbListSerial;
    /// <summary>Official MDBList tab: a search box over MDBList's public lists, shown only when the server reports mdblist_search; a pasted link always works.</summary>
    private void RenderMdbListSearch(JsonObject draft, JsonObject config)
    {
        if (Controller.Capabilities?.MdblistSearch != true)
        {
            _contents.Children.Add(Text("Searching MDBList needs an API key in Settings › Providers. Popular picks and pasted links still work.", 13, true));
            return;
        }
        var owner = Controller.Owner!; var results = Stack(8); var box = new TextBox { PlaceholderText = "Search MDBList and popular picks", MaxLength = 256, HorizontalAlignment = HorizontalAlignment.Stretch };
        Identify(box, "CollectionMdbListSearch", "Search lists");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        async Task SearchAsync()
        {
            timer.Stop(); var text = box.Text.Trim(); var serial = ++_mdbListSerial; _mdbListReads?.Cancel();
            results.Children.Clear();
            if (text.Length == 0) return;
            _mdbListReads = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); var reads = _mdbListReads;
            results.Children.Add(Text("Searching…", 13, true));
            try
            {
                var found = await _api.MdbListSearchAsync(owner, text, reads.Token);
                if (!Current(owner) || serial != _mdbListSerial) return;
                results.Children.Clear();
                results.Children.Add(Text("From MDBList · Public lists by other people", 13, true));
                foreach (var list in found.Lists)
                {
                    var feed = AdminCollectionsApi.MdbListFeed(list);
                    var detail = $"by {list.UserName}" + (list.MediaType is "movie" or "show" ? $" · {list.MediaType}" : "") + $" · {list.Items:N0} titles" + (list.Likes > 0 ? $" · ♥ {list.Likes:N0}" : "");
                    var choose = ActionButton(list.Name, "CollectionMdbListPick_" + list.Id, () =>
                    {
                        config["url"] = feed; config["mode"] = "mdblist_json"; draft["source_url"] = feed;
                        // A pick fills the name and description only where nothing was typed.
                        if (string.IsNullOrWhiteSpace(draft["title"]?.ToString())) draft["title"] = list.Name;
                        if (string.IsNullOrWhiteSpace(draft["description"]?.ToString())) draft["description"] = list.Description;
                        RenderEditor(); return Task.CompletedTask;
                    }, feed.Length > 0);
                    var line = Stack(2); line.Children.Add(choose); line.Children.Add(Text(feed.Length > 0 ? detail : detail + " · MDBList did not report a list page", 12, true)); results.Children.Add(line);
                }
                if (found.Lists.Count == 0) results.Children.Add(Text($"No public lists match “{text}”.", 13, true));
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (Current(owner) && serial == _mdbListSerial) { results.Children.Clear(); results.Children.Add(Text("Couldn't search MDBList. " + AdminCollectionsController.Message(error), 13)); }
            }
        }
        timer.Tick += async (_, _) => await SearchAsync();
        box.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        box.Unloaded += (_, _) => { timer.Stop(); _mdbListReads?.Cancel(); };
        _contents.Children.Add(box); _contents.Children.Add(ActionButton("Search", "CollectionMdbListSearchButton", SearchAsync)); _contents.Children.Add(results);
    }
    private void RenderArtwork(string? id)
    {
        var panel = Stack(12); panel.Children.Add(Text("Look", 20));
        foreach (var slot in new[] { "poster", "backdrop" })
        {
            panel.Children.Add(Text(slot == "poster" ? "Poster" : "Backdrop", 15));
            Input(panel, "Image URL", "CollectionArtworkUrl_" + slot, _artwork.GetValueOrDefault(slot).Source ?? "", value => { if (value.Length == 0) _artwork.Remove(slot); else _artwork[slot] = (value.Trim(), null, "image/png"); });
            var actions = Wrap();
            actions.Children.Add(ActionButton("Upload image", "CollectionArtworkUpload_" + slot, () => PickArtworkAsync(slot)));
            actions.Children.Add(ActionButton("Remove image", "CollectionArtworkRemove_" + slot, () => { _artwork[slot] = (null, null, "image/png"); Notice("Image removal is staged. Save to apply it."); return Task.CompletedTask; }));
            panel.Children.Add(actions);
        }
        _surface.Children.Add(Card(panel));
    }
    private async Task PickArtworkAsync(string slot)
    {
        var owner = Controller.Owner!;
        var picker = new Windows.Storage.Pickers.FileOpenPicker(); foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp" }) picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
        var file = await picker.PickSingleFileAsync(); if (file == null || !Current(owner)) return;
        var properties = await file.GetBasicPropertiesAsync(); if (properties.Size is 0 or > 10485760) throw new ArgumentException("Use an image up to 10 MiB.");
        using var stream = await file.OpenStreamForReadAsync(); using var memory = new MemoryStream();
        var buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, owner.Token)) > 0)
        { if (memory.Length + read > 10485760) throw new ArgumentException("Use an image up to 10 MiB."); await memory.WriteAsync(buffer.AsMemory(0, read), owner.Token); }
        if (!Current(owner)) return;
        _artwork[slot] = (null, memory.ToArray(), file.FileType.ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" });
        Notice("Image is staged. Save to apply it.");
    }
}
