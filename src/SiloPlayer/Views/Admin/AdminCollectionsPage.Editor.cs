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
    private readonly StackPanel _contents = Stack(12);
    private readonly StackPanel _preview = Stack(10);
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
            if (await CanLeaveAsync()) { ClearStaged(); await Controller.EditAsync(id, type, _library); RenderEditor(); Notice(""); }
        }));
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
            pick.Click += async (_, _) =>
            {
                if (pick.IsChecked != true && saved != null && await ConfirmAsync("Remove this library?", "Titles available only in this library will be removed from the collection. Your files are kept.", "Remove library", Controller.Owner!) != true)
                { pick.IsChecked = true; return; }
                var ids = (draft["library_ids"] as JsonArray)?.Select(v => v!.ToString()).Where(v => v != library.Id).ToList() ?? [];
                if (pick.IsChecked == true) ids.Add(library.Id);
                draft["library_ids"] = JsonSerializer.SerializeToNode(ids); _memberCursor = null; _memberHistory.Clear();
            };
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

        _contents.Children.Clear(); _preview.Children.Clear();
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
        catch (Exception error) { if (Current(owner)) Notice(AdminCollectionsController.Message(error)); }
        finally { IsHitTestVisible = true; }
    }
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
        _rules.Load(query);
        _rules.Changed += () => { draft["query_definition"] = JsonSerializer.SerializeToNode(_rules.Query, AdminCollectionsApi.Json); _invalidRules = !_rules.IsValid; };
        Identify(_rules, "CollectionRules", "Collection rules"); _contents.Children.Add(_rules);
        Input(_contents, "Max titles (blank for no limit)", "CollectionSmartLimit", query.Limit is null or 10000000 ? "" : query.Limit.ToString()!, value =>
        {
            if (string.IsNullOrWhiteSpace(value)) { _rules.Query.Limit = null; _invalidFields.Remove("smart-limit"); }
            else if (int.TryParse(value, out var limit) && limit > 0) { _rules.Query.Limit = limit; _invalidFields.Remove("smart-limit"); }
            else { _invalidFields.Add("smart-limit"); return; }
            draft["query_definition"] = JsonSerializer.SerializeToNode(_rules.Query, AdminCollectionsApi.Json);
        });
        var advanced = Stack(10);
        Input(advanced, "Advanced rules", "CollectionRulesJson", (draft["query_definition"] ?? new JsonObject()).ToJsonString(), value =>
        {
            try { var parsed = JsonNode.Parse(value, documentOptions: new() { MaxDepth = 32 })!.AsObject(); _rules.Load(parsed.Deserialize<QueryDefinition>(AdminCollectionsApi.Json)!); draft["query_definition"] = parsed; _invalidRules = false; }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { _invalidRules = true; }
        }, true);
        _contents.Children.Add(new Expander { Header = "Advanced", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        _contents.Children.Add(ActionButton("Preview", "CollectionPreview", async () =>
        {
            if (_invalidRules || _rules?.IsValid == false) throw new ArgumentException("Complete the rules before previewing.");
            var rules = (JsonObject)(draft["query_definition"] as JsonObject ?? new JsonObject()).DeepClone(); rules["library_ids"] = AdminCollectionsApi.RuleLibraryIds(draft["library_ids"]);
            if (rules["library_ids"]!.AsArray().Count == 0) throw new ArgumentException("Pick libraries first.");
            await RunAsync(async lease =>
            {
                var response = await _api.PreviewAsync(lease, rules);
                if (!Current(owner)) return;
                _preview.Children.Clear(); _preview.Children.Add(Text($"{response.Value["total"]} titles match · Preview shows unsaved changes", 13, true));
                foreach (var item in (response.Value["items"] as JsonArray ?? []).OfType<JsonObject>().Take(24)) _preview.Children.Add(Text(item["title"]?.ToString() ?? ""));
            }, false);
        }));
        _contents.Children.Add(_preview);
    }
    private void RenderOrder(JsonObject draft, bool smart)
    {
        if (smart) return; // The grouped rule editor already owns smart sort/limit.
        var order = Stack(12); order.Children.Add(Text("Order", 20));
        var config = draft["sort_config"] as JsonObject ?? new(); draft["sort_config"] = config.Parent == null ? config : config.DeepClone(); config = draft["sort_config"]!.AsObject();
        order.Children.Add(Select("CollectionSort", [("", "List order"), ("title", "Title"), ("added_at", "Date added"), ("release_date", "Release date"), ("year", "Year"), ("rating_imdb", "IMDb rating"), ("runtime", "Duration")],
            config["field"]?.ToString() ?? "", value => { config["field"] = value; return Task.CompletedTask; }));
        order.Children.Add(Select("CollectionDirection", [("asc", "Ascending"), ("desc", "Descending")], config["order"]?.ToString() ?? "asc", value => { config["order"] = value; return Task.CompletedTask; }));
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
