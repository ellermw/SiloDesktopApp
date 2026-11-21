using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminHomeSectionsPage
{
    private const int MaxRowCollections = 30, MaxShownChoices = 100;
    private List<AdminHomeSectionChoice>? _collectionOptions;
    private bool _collectionOptionsTruncated, _collectionOptionsFailed;

    /// <summary>
    /// The collection pickers of Add row (CollectionPicker, CollectionsRowEditor): what the row holds, then a search over every visible collection with the
    /// Manual / Smart / Synced filter and, when collections come from more than one library, the library filter. The list is one bounded read.
    /// </summary>
    private void CollectionSelector(StackPanel panel, bool multiple)
    {
        var draft = _draft!; var selected = new StackPanel { Spacing = 8 }; panel.Children.Add(selected);
        JsonArray Refs() => draft.Config["collections"] as JsonArray ?? new();
        string NameOf(string id) => _choiceLabels.TryGetValue(id, out var known) ? known : T("web.home.addRow.collections.unavailable");
        void Refresh()
        {
            selected.Children.Clear(); selected.Children.Add(U.Text(multiple ? T("web.home.addRow.collections.chosenLabel") : T("web.home.addRow.collection.label"), 14));
            var refs = multiple ? Refs().OfType<JsonObject>().Select(entry => Value(entry, "library_collection_id")).ToArray() : [Value(draft.Config, "library_collection_id")];
            foreach (var (id, index) in refs.Select((id, index) => (id, index)))
            {
                if (id == "") continue;
                var name = NameOf(id); var line = new Grid { ColumnSpacing = 4 };
                line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                var text = U.Text(name, 14); text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap; line.Children.Add(text);
                var at = 1;
                void AddButton(Button button) { line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(button, at++); line.Children.Add(button); }
                if (multiple)
                {
                    var up = AdminHomeSectionsUi.GhostIcon("chevron-up", T("web.home.addRow.collections.moveUp", new { collection = name }), "HomeSectionCollectionUp_" + index, 32); up.IsEnabled = index > 0;
                    up.Click += (_, _) => { var list = Refs().DeepClone().AsArray(); var node = list[index]; list.RemoveAt(index); list.Insert(index - 1, node); SetConfig("collections", list); Refresh(); };
                    var down = AdminHomeSectionsUi.GhostIcon("chevron-down", T("web.home.addRow.collections.moveDown", new { collection = name }), "HomeSectionCollectionDown_" + index, 32); down.IsEnabled = index < refs.Length - 1;
                    down.Click += (_, _) => { var list = Refs().DeepClone().AsArray(); var node = list[index]; list.RemoveAt(index); list.Insert(index + 1, node); SetConfig("collections", list); Refresh(); };
                    AddButton(up); AddButton(down);
                }
                var remove = AdminHomeSectionsUi.GhostIcon("x", T("web.home.addRow.collections.remove", new { collection = name }), "HomeSectionCollectionRemove_" + index, 32);
                remove.Click += (_, _) => { if (multiple) { var list = Refs().DeepClone().AsArray(); list.RemoveAt(index); SetConfig("collections", list); } else SetConfig("library_collection_id", null); Refresh(); };
                AddButton(remove); selected.Children.Add(AdminMonitorUi.Panel(line, new(12, 4, 6, 4), 12));
            }
            var count = refs.Count(id => id != "");
            if (multiple && count == 0) selected.Children.Add(U.Text(T("web.home.addRow.collections.empty"), 13, true));
            else if (multiple) selected.Children.Add(U.Text(T("web.home.addRow.collections.count", AdminHomeSectionsText.Count(count)), 13, true));
        }
        void Pick(AdminHomeSectionChoice choice)
        {
            _choiceLabels[choice.Id] = choice.Label;
            if (multiple)
            {
                var refs = Refs().DeepClone().AsArray();
                if (refs.Any(entry => entry?["library_collection_id"]?.ToString() == choice.Id)) return;
                if (refs.Count >= MaxRowCollections) { _editorNotice.Text = T("web.home.addRow.collections.limit", AdminHomeSectionsText.Count(MaxRowCollections)); return; }
                refs.Add(new JsonObject { ["library_collection_id"] = choice.Id }); SetConfig("collections", refs);
            }
            else
            {
                var current = _choiceLabels.GetValueOrDefault(AdminHomeSectionsDraftFactory.CollectionIdOf(draft.Config));
                AdminHomeSectionsDraftFactory.WithCollection(draft, choice.Id, choice.Label, current); Dirty();
                // The name follows the pick; rebuild the form so the name box shows it.
                RenderForm(); return;
            }
            Refresh();
        }
        Refresh();
        bool IsPicked(string id) => multiple ? Refs().OfType<JsonObject>().Any(entry => Value(entry, "library_collection_id") == id) : Value(draft.Config, "library_collection_id") == id;
        CollectionPickerView(panel, Pick, IsPicked, multiple);
        var chosenIds = multiple ? Refs().OfType<JsonObject>().Select(entry => Value(entry, "library_collection_id")).Take(MaxRowCollections).ToArray() : [Value(draft.Config, "library_collection_id")];
        var hydrationRevision = _dialogRevision; var hydrationPicker = _pickerRevision; var hydrationToken = _editorReads!.Token;
        _ = HydrateAsync();
        async Task HydrateAsync()
        {
            // At most 30 existing identities, one read in flight. Never walks collection contents.
            foreach (var id in chosenIds.Where(id => id != "" && !_choiceLabels.ContainsKey(id)).Distinct())
            {
                try
                {
                    var value = await _api.CollectionByIdAsync(_editorOwner!, id, hydrationToken);
                    if (!EditorCurrent(hydrationRevision) || hydrationPicker != _pickerRevision) return;
                    _choiceLabels[id] = value.Label; Refresh();
                }
                catch (Exception) { if (!EditorCurrent(hydrationRevision) || hydrationPicker != _pickerRevision) return; }
            }
        }
    }

    /// <summary>The search bar and result list of the collection pickers, with the lookup by ID that stays usable when the bounded list is cut short or cannot be read.</summary>
    private void CollectionPickerView(StackPanel panel, Action<AdminHomeSectionChoice> picked, Func<string, bool> isPicked, bool multiple)
    {
        var host = new StackPanel { Spacing = 10 };
        var editorRevision = _dialogRevision; var viewRevision = _pickerRevision; var token = _editorReads!.Token;
        bool Current() => EditorCurrent(editorRevision) && viewRevision == _pickerRevision;
        var search = Input(T("web.home.addRow.collection.search"), "HomeSectionSearch_collections", "", _ => { }, 200);
        search.Width = double.NaN; search.HorizontalAlignment = HorizontalAlignment.Stretch;
        var kinds = new AdminHomeSectionsPills { PillsEnabled = true }; AutomationProperties.SetName(kinds, T("web.home.addRow.collection.type")); AutomationProperties.SetAutomationId(kinds, "HomeSectionCollectionKinds");
        var libraryBox = new ComboBox { MinWidth = 160, MaxWidth = 192, MinHeight = 32, Height = 32, FontSize = 13, CornerRadius = new(10), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(libraryBox, T("web.home.addRow.collection.library")); AutomationProperties.SetAutomationId(libraryBox, "HomeSectionCollectionLibrary");
        var filters = new Grid { ColumnSpacing = 8 }; filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        filters.Children.Add(kinds); Grid.SetColumn(libraryBox, 1); filters.Children.Add(libraryBox);
        var results = new StackPanel { Spacing = 6 }; var status = U.Text("", 13, true); var scroller = new ScrollViewer { Content = results, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var retry = Action(T("common.retry"), "HomeSectionCollectionsRetry"); retry.Visibility = Visibility.Collapsed; retry.HorizontalAlignment = HorizontalAlignment.Left;
        var libraries = new List<AdminHomeCollectionLibrary>(); string kind = AdminHomeSectionsCollections.AllKinds; string? pickedLibrary = null; var updating = false;
        var loading = true;
        void Show()
        {
            results.Children.Clear(); var options = _collectionOptions ?? [];
            var effective = AdminHomeSectionsCollections.EffectiveLibrary(libraries, pickedLibrary);
            var matches = AdminHomeSectionsCollections.Filter(options, search.Text, kind, effective);
            AutomationProperties.SetName(search, T("web.home.addRow.collection.search"));
            search.PlaceholderText = T("web.home.addRow.collection.searchPlaceholder", AdminHomeSectionsText.Count(options.Count));
            foreach (var choice in matches.Take(MaxShownChoices))
            {
                var chosen = isPicked(choice.Id); var button = Action(choice.Label, "HomeSectionChoice_collections_" + choice.Identity); button.Height = double.NaN; button.MinHeight = 44;
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new(12, 6, 12, 6);
                var line = new Grid { ColumnSpacing = 8 }; line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var texts = new StackPanel { Spacing = 2 };
                var title = U.Text(choice.Label, 14); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap; texts.Children.Add(title);
                var meta = AdminHomeSectionsCollections.Meta(choice, _libraries);
                if (meta.Length > 0) { var line2 = U.Text(meta, 13, true); line2.TextTrimming = TextTrimming.CharacterEllipsis; line2.TextWrapping = TextWrapping.NoWrap; texts.Children.Add(line2); }
                line.Children.Add(texts);
                if (multiple || chosen) { var tag = U.Text(multiple ? T(chosen ? "web.home.addRow.collections.added" : "web.home.addRow.collections.add") : "✓", 13, !chosen); Grid.SetColumn(tag, 1); line.Children.Add(tag); }
                button.Content = line; AutomationProperties.SetName(button, choice.Label + (meta.Length > 0 ? ". " + meta : ""));
                var captured = choice; button.Click += (_, _) => { if (Current()) picked(captured); };
                results.Children.Add(button);
            }
            if (matches.Count > MaxShownChoices) results.Children.Add(U.Text(AdminHomeSectionsText.T("native.homeSections.pickerCapped", new { count = (long)MaxShownChoices }), 12, true));
            status.Text = matches.Count == 0 ? AdminHomeSectionsCollections.EmptyText(loading, _collectionOptionsFailed, options.Count, search.Text, effective != null) : "";
            status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            retry.Visibility = _collectionOptionsFailed && !loading ? Visibility.Visible : Visibility.Collapsed;
        }
        void Build()
        {
            var options = _collectionOptions ?? [];
            libraries = AdminHomeSectionsCollections.LibrariesOf(options, _libraries).ToList();
            updating = true;
            try
            {
                libraryBox.Items.Clear(); libraryBox.Items.Add(new ComboBoxItem { Content = T("web.home.addRow.params.allLibraries"), Tag = "" });
                foreach (var library in libraries) libraryBox.Items.Add(new ComboBoxItem { Content = library.Name, Tag = library.Id });
                var effective = AdminHomeSectionsCollections.EffectiveLibrary(libraries, pickedLibrary); pickedLibrary = effective;
                libraryBox.SelectedItem = libraryBox.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == (effective ?? ""));
                libraryBox.Visibility = libraries.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { updating = false; }
            Show();
        }
        kinds.Set([((object)AdminHomeSectionsCollections.AllKinds, T("web.home.addRow.collection.filterAll"), "HomeSectionCollectionKind_all", false),
            ("manual", T("web.home.addRow.collection.filterManual"), "HomeSectionCollectionKind_manual", false), ("smart", T("web.home.addRow.collection.filterSmart"), "HomeSectionCollectionKind_smart", false),
            ("synced", T("web.home.addRow.collection.filterSynced"), "HomeSectionCollectionKind_synced", false)], AdminHomeSectionsCollections.AllKinds);
        kinds.Chosen += key => { kind = (string)key; kinds.Select(key); Show(); };
        libraryBox.SelectionChanged += (_, _) => { if (updating || libraryBox.SelectedItem is not ComboBoxItem { Tag: string id }) return; pickedLibrary = id == "" ? null : id; Show(); };
        search.TextChanged += (_, _) => Show();
        async Task LoadAsync()
        {
            loading = true; _collectionOptionsFailed = false; retry.IsEnabled = false; Show();
            try
            {
                var value = await _api.ChoicesAsync(_editorOwner!, "collections", ct: token);
                if (!Current()) return;
                _collectionOptions = value.Items.Where(choice => choice.Visibility == "visible").ToList(); _collectionOptionsTruncated = value.Truncated;
                foreach (var option in _collectionOptions) if (!_choiceLabels.ContainsKey(option.Id)) _choiceLabels[option.Id] = option.Label;
            }
            catch (Exception) { if (!Current()) return; _collectionOptionsFailed = true; }
            finally { if (Current()) { loading = false; retry.IsEnabled = true; Build(); } }
        }
        retry.Click += async (_, _) => await LoadAsync();
        host.Children.Add(search); host.Children.Add(filters); host.Children.Add(scroller); host.Children.Add(status); host.Children.Add(retry);
        panel.Children.Add(host);
        if (_collectionOptions != null) { loading = false; Build(); } else { Show(); _ = LoadAsync(); }
        // The list is read whole once (2,000 entries at most). A collection missing from it is found by its ID, which the server checks.
        var direct = Input(AdminHomeSectionsText.T("native.homeSections.collectionId"), "HomeSectionCollectionId", "", _ => { }, 200);
        var find = Action(AdminHomeSectionsText.T("native.homeSections.findCollection"), "HomeSectionFindCollection"); var lookup = U.Text("", 12, true);
        find.Click += async (_, _) =>
        {
            if (!EditorCurrent(editorRevision)) return; find.IsEnabled = false;
            try
            {
                var choice = await _api.CollectionByIdAsync(_editorOwner!, direct.Text.Trim(), token);
                if (Current()) { picked(choice); lookup.Text = choice.Label; }
            }
            catch (Exception) { if (Current()) lookup.Text = AdminHomeSectionsText.T("native.homeSections.collectionNotFound"); }
            finally { if (Current()) find.IsEnabled = true; }
        };
        panel.Children.Add(direct); panel.Children.Add(find); panel.Children.Add(lookup);
    }

    /// <summary>Replaces the draft config's members with <paramref name="updated"/> in place, so arrays other editors hold stay the same instances when unchanged.</summary>
    private void ApplyConfig(JsonObject updated)
    {
        var config = _draft!.Config;
        foreach (var key in config.Select(pair => pair.Key).ToArray()) if (!updated.ContainsKey(key)) config.Remove(key);
        foreach (var pair in updated) if (!config.ContainsKey(pair.Key) || !JsonNode.DeepEquals(config[pair.Key], pair.Value)) config[pair.Key] = pair.Value?.DeepClone();
        Dirty();
    }

    /// <summary>"From": the libraries a row takes titles from (official LibraryFilterField): every library, or a pick of them, read and written with the backend's precedence.</summary>
    private void LibrarySelector(StackPanel panel)
    {
        if (_libraries.Count == 0) { LibrarySelectorSearch(panel); return; }
        var draft = _draft!; var list = new StackPanel { Spacing = 2 }; var updating = false;
        void Build()
        {
            updating = true;
            try
            {
                list.Children.Clear(); var chosen = AdminHomeSectionsRules.LibraryFilterIds(draft.Config);
                var all = new CheckBox { Content = T("web.home.addRow.params.allLibraries"), IsChecked = chosen.Count == 0, MinWidth = 0 }; AutomationProperties.SetAutomationId(all, "HomeSectionLibraryAll");
                // Every library is the absence of a pick: unchecking it does nothing until a library is checked.
                all.Checked += (_, _) => { if (updating) return; ApplyConfig(AdminHomeSectionsRules.WithLibraryFilterIds(draft.Config, [])); Build(); RenderAfterLibraryChange(); };
                all.Unchecked += (_, _) => { if (updating) return; Build(); };
                list.Children.Add(all);
                var entries = _libraries.Select(library => (Id: long.TryParse(library.Id, out var id) ? id : 0L, library.Label)).Where(entry => entry.Id > 0).ToList();
                foreach (var id in chosen) if (entries.All(entry => entry.Id != id)) entries.Add((id, _choiceLabels.GetValueOrDefault("library:" + id) ?? AdminHomeSectionsText.T("web.admin.homeRows.restore.libraryFallback", new { id })));
                foreach (var (id, label) in entries)
                {
                    var box = new CheckBox { Content = label, IsChecked = chosen.Contains(id), MinWidth = 0 }; AutomationProperties.SetAutomationId(box, "HomeSectionLibrary_" + id);
                    var libraryId = id;
                    box.Checked += (_, _) => { if (updating) return; var next = AdminHomeSectionsRules.LibraryFilterIds(draft.Config).ToList(); if (!next.Contains(libraryId) && next.Count < 1000) next.Add(libraryId); ApplyConfig(AdminHomeSectionsRules.WithLibraryFilterIds(draft.Config, next)); Build(); RenderAfterLibraryChange(); };
                    box.Unchecked += (_, _) => { if (updating) return; ApplyConfig(AdminHomeSectionsRules.WithLibraryFilterIds(draft.Config, AdminHomeSectionsRules.LibraryFilterIds(draft.Config).Where(other => other != libraryId))); Build(); RenderAfterLibraryChange(); };
                    list.Children.Add(box);
                }
            }
            finally { updating = false; }
        }
        Build();
        var field = new StackPanel { Spacing = 6 }; field.Children.Add(list); field.Children.Add(U.Text(T("web.home.addRow.params.fromHint"), 12, true));
        Field(panel, T("web.home.addRow.params.from"), field);
    }

    /// <summary>The earlier search over the libraries, used only when the page could not read the library list.</summary>
    private void LibrarySelectorSearch(StackPanel panel)
    {
        var draft = _draft!; var selection = new StackPanel { Spacing = 6 };
        void Refresh()
        {
            selection.Children.Clear(); var chosen = AdminHomeSectionsRules.LibraryFilterIds(draft.Config);
            selection.Children.Add(U.Text(chosen.Count == 0 ? T("web.home.addRow.params.allLibraries") : T("web.home.addRow.libraries"), 13));
            foreach (var id in chosen.ToArray())
            {
                var name = _libraries.FirstOrDefault(library => library.Id == id.ToString())?.Label ?? _choiceLabels.GetValueOrDefault("library:" + id) ?? AdminHomeSectionsText.T("web.admin.homeRows.restore.libraryFallback", new { id });
                var remove = Action(T("native.homeSections.removeName", new { name }), "HomeSectionLibraryRemove_" + id);
                remove.Click += (_, _) => { ApplyConfig(AdminHomeSectionsRules.WithLibraryFilterIds(draft.Config, chosen.Where(other => other != id))); Refresh(); RenderAfterLibraryChange(); };
                selection.Children.Add(remove);
            }
        }
        var field = new StackPanel { Spacing = 6 }; field.Children.Add(selection); Refresh();
        ChoicePicker(field, "libraries", choice =>
        {
            _choiceLabels["library:" + choice.Id] = choice.Label;
            var chosen = AdminHomeSectionsRules.LibraryFilterIds(draft.Config).ToList();
            if (long.TryParse(choice.Id, out var id) && id > 0 && !chosen.Contains(id) && chosen.Count < 1000) chosen.Add(id);
            ApplyConfig(AdminHomeSectionsRules.WithLibraryFilterIds(draft.Config, chosen)); Refresh(); RenderAfterLibraryChange();
        });
        field.Children.Add(U.Text(T("web.home.addRow.params.fromHint"), 12, true));
        Field(panel, T("web.home.addRow.params.from"), field);
    }
    /// <summary>The rule editor keeps its own copies of the groups it shows; rebuild it when the library filter moved the config under it.</summary>
    private Action? _afterLibraryChange;
    private void RenderAfterLibraryChange() => _afterLibraryChange?.Invoke();

    private void CuratedEditor(StackPanel panel)
    {
        var draft = _draft!; var selected = new StackPanel { Spacing = 6 }; panel.Children.Add(selected);
        JsonArray Ids() => draft.Config["item_ids"] as JsonArray ?? new();
        void Refresh()
        {
            selected.Children.Clear(); var ids = Ids().Select(node => node!.ToString()).ToArray();
            for (var i = 0; i < ids.Length; i++)
            {
                var at = i; var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                line.Children.Add(U.Text(_choiceLabels.GetValueOrDefault(ids[i], T("native.homeSections.titleFallback", new { id = ids[i] })), 13));
                var up = Action("↑", "HomeSectionTitleUp_" + i); up.IsEnabled = i > 0;
                up.Click += (_, _) => { var array = Ids().DeepClone().AsArray(); var node = array[at]; array.RemoveAt(at); array.Insert(at - 1, node); SetConfig("item_ids", array); Refresh(); };
                var down = Action("↓", "HomeSectionTitleDown_" + i); down.IsEnabled = i < ids.Length - 1;
                down.Click += (_, _) => { var array = Ids().DeepClone().AsArray(); var node = array[at]; array.RemoveAt(at); array.Insert(at + 1, node); SetConfig("item_ids", array); Refresh(); };
                var remove = Action(T("native.homeSections.remove"), "HomeSectionTitleRemove_" + i);
                remove.Click += (_, _) => { var array = Ids().DeepClone().AsArray(); array.RemoveAt(at); SetConfig("item_ids", array); Refresh(); };
                line.Children.Add(up); line.Children.Add(down); line.Children.Add(remove); selected.Children.Add(line);
            }
        }
        Refresh(); ChoicePicker(panel, "titles", choice =>
        {
            var ids = Ids().DeepClone().AsArray(); if (ids.Count >= 500 || ids.Any(node => node?.ToString() == choice.Identity)) return;
            _choiceLabels[choice.Identity] = choice.Label; ids.Add(choice.Identity); SetConfig("item_ids", ids); Refresh();
        });
    }

    private void ChoicePicker(StackPanel panel, string kind, Action<AdminHomeSectionChoice> picked)
    {
        var host = new StackPanel { Spacing = 8 }; var status = U.Text("", 12, true); var results = new StackPanel { Spacing = 4 };
        var search = Input(T(kind == "titles" ? "web.home.addRow.curated.search" : "web.home.addRow.params.from"), "HomeSectionSearch_" + kind, "", _ => { }, 200);
        var run = Action(T("native.homeSections.search"), "HomeSectionSearchGo_" + kind); var next = Action(T("native.homeSections.next"), "HomeSectionPickerNext_" + kind);
        var previous = Action(T("native.homeSections.previous"), "HomeSectionPickerPrevious_" + kind);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; navigation.Children.Add(previous); navigation.Children.Add(next);
        host.Children.Add(search); host.Children.Add(run); host.Children.Add(status); host.Children.Add(results); host.Children.Add(navigation); panel.Children.Add(host);
        var editorRevision = _dialogRevision; var viewRevision = _pickerRevision; var token = _editorReads!.Token;
        List<AdminHomeSectionChoice>? metadata = null; string? cursor = null; var page = 0; long request = 0;
        bool Current(long id) => EditorCurrent(editorRevision) && viewRevision == _pickerRevision && id == request;
        void ShowMetadata()
        {
            if (metadata == null) return;
            var filtered = metadata.Where(choice => choice.Label.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            results.Children.Clear();
            foreach (var choice in filtered.Skip(page * 50).Take(50)) Add(choice);
            status.Text = filtered.Length == 0 ? T("native.homeSections.noMatches") : $"{filtered.Length} choices · page {page + 1}";
            previous.IsEnabled = page > 0; next.IsEnabled = (page + 1) * 50 < filtered.Length;
        }
        void Add(AdminHomeSectionChoice choice)
        {
            var button = Action(choice.Label, "HomeSectionChoice_" + kind + "_" + choice.Identity); button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Click += (_, _) => { if (EditorCurrent(editorRevision) && viewRevision == _pickerRevision) picked(choice); };
            results.Children.Add(button);
        }
        async Task Load(bool continuation)
        {
            if (kind == "titles" && search.Text.Trim().Length == 0) { status.Text = T("native.homeSections.enterATitleToSearch"); return; }
            if (metadata != null) { if (!continuation) page = 0; ShowMetadata(); return; }
            var id = ++request; run.IsEnabled = next.IsEnabled = previous.IsEnabled = false; status.Text = T("native.homeSections.loadingChoices");
            try
            {
                var value = await _api.ChoicesAsync(_editorOwner!, kind, search.Text.Trim(), continuation ? cursor : null, token);
                if (!Current(id)) return;
                results.Children.Clear();
                if (kind == "titles")
                {
                    foreach (var choice in value.Items) Add(choice);
                    cursor = value.Page?.HasMore == true ? value.Page.NextCursor : null;
                    status.Text = value.Items.Count == 0 ? T("native.homeSections.noTitlesFound") : T("native.homeSections.showingUpTo50TitlesRefine");
                    next.IsEnabled = cursor != null; previous.IsEnabled = false;
                }
                else { metadata = value.Items; page = 0; ShowMetadata(); }
            }
            catch (Exception) { if (Current(id)) status.Text = AdminHomeSectionsText.T("native.homeSections.choicesFailed"); }
            finally { if (Current(id)) run.IsEnabled = true; }
        }
        run.Click += async (_, _) => await Load(false);
        next.Click += async (_, _) => { if (metadata != null) { page++; ShowMetadata(); } else await Load(true); };
        previous.Click += (_, _) => { if (metadata != null) { page = Math.Max(0, page - 1); ShowMetadata(); } };
        search.TextChanged += (_, _) => { request++; cursor = null; run.IsEnabled = true; if (metadata != null) { page = 0; ShowMetadata(); } else { results.Children.Clear(); next.IsEnabled = false; } };
        previous.IsEnabled = next.IsEnabled = false;
        // Fetch only the two small metadata domains on explicit user request. Never prefetch media.
    }
}
