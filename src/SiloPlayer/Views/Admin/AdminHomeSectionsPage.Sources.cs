using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminHomeSectionsPage
{
    private void CollectionSelector(StackPanel panel, bool multiple)
    {
        var draft = _draft!; var selected = new StackPanel { Spacing = 8 }; panel.Children.Add(selected);
        JsonArray Refs() => draft.Config["collections"] as JsonArray ?? new();
        void Refresh()
        {
            selected.Children.Clear(); selected.Children.Add(U.Text(multiple ? "Collections in this row" : "Collection", 14));
            var refs = multiple ? Refs().OfType<JsonObject>().Select(entry => Value(entry, "library_collection_id")).ToArray() : [Value(draft.Config, "library_collection_id")];
            foreach (var (id, index) in refs.Select((id, index) => (id, index)))
            {
                if (id == "") continue;
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                line.Children.Add(U.Text(_choiceLabels.GetValueOrDefault(id, "Unavailable collection (" + id + ")"), 13));
                if (multiple)
                {
                    var up = Action("↑", "HomeSectionCollectionUp_" + index); up.IsEnabled = index > 0;
                    up.Click += (_, _) => { var list = Refs().DeepClone().AsArray(); var node = list[index]; list.RemoveAt(index); list.Insert(index - 1, node); SetConfig("collections", list); Refresh(); };
                    var down = Action("↓", "HomeSectionCollectionDown_" + index); down.IsEnabled = index < refs.Length - 1;
                    down.Click += (_, _) => { var list = Refs().DeepClone().AsArray(); var node = list[index]; list.RemoveAt(index); list.Insert(index + 1, node); SetConfig("collections", list); Refresh(); };
                    line.Children.Add(up); line.Children.Add(down);
                }
                var remove = Action("Remove", "HomeSectionCollectionRemove_" + index);
                remove.Click += (_, _) => { if (multiple) { var list = Refs().DeepClone().AsArray(); list.RemoveAt(index); SetConfig("collections", list); } else SetConfig("library_collection_id", null); Refresh(); };
                line.Children.Add(remove); selected.Children.Add(line);
            }
            if (!refs.Any(id => id != "")) selected.Children.Add(U.Text(multiple ? "Add collections in the order you want them to appear." : "Choose a server collection.", 13, true));
        }
        void Pick(AdminHomeSectionChoice choice)
        {
            _choiceLabels[choice.Id] = choice.Label;
            if (multiple)
            {
                var refs = Refs().DeepClone().AsArray();
                if (refs.Any(entry => entry?["library_collection_id"]?.ToString() == choice.Id)) return;
                if (refs.Count >= 30) { _editorNotice.Text = "A row can hold up to 30 collections."; return; }
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
        Refresh(); ChoicePicker(panel, "collections", Pick);
        var chosenIds = multiple ? Refs().OfType<JsonObject>().Select(entry => Value(entry, "library_collection_id")).Take(30).ToArray() : [Value(draft.Config, "library_collection_id")];
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
        // The metadata endpoint is unpaged. A direct, validated identity lookup keeps a large server usable
        // even if its metadata exceeds our bounded picker window.
        var direct = Input("Collection ID", "HomeSectionCollectionId", "", _ => { }, 200);
        var find = Action("Find collection by ID", "HomeSectionFindCollection"); var status = U.Text("", 12, true);
        var revision = _dialogRevision; var pickerRevision = _pickerRevision; var token = _editorReads!.Token;
        find.Click += async (_, _) =>
        {
            if (!EditorCurrent(revision)) return; find.IsEnabled = false;
            try
            {
                var choice = await _api.CollectionByIdAsync(_editorOwner!, direct.Text.Trim(), token);
                if (EditorCurrent(revision) && pickerRevision == _pickerRevision) { Pick(choice); status.Text = choice.Label; }
            }
            catch (Exception) { if (EditorCurrent(revision) && pickerRevision == _pickerRevision) status.Text = "Collection not available. Check its ID and try again."; }
            finally { if (EditorCurrent(revision) && pickerRevision == _pickerRevision) find.IsEnabled = true; }
        };
        panel.Children.Add(direct); panel.Children.Add(find); panel.Children.Add(status);
    }

    /// <summary>Replaces the draft config's members with <paramref name="updated"/> in place, so arrays other editors hold stay the same instances when unchanged.</summary>
    private void ApplyConfig(JsonObject updated)
    {
        var config = _draft!.Config;
        foreach (var key in config.Select(pair => pair.Key).ToArray()) if (!updated.ContainsKey(key)) config.Remove(key);
        foreach (var pair in updated) if (!config.ContainsKey(pair.Key) || !JsonNode.DeepEquals(config[pair.Key], pair.Value)) config[pair.Key] = pair.Value?.DeepClone();
        Dirty();
    }

    /// <summary>"From": the libraries a row takes titles from (official LibraryFilterField), read and written with the backend's precedence.</summary>
    private void LibrarySelector(StackPanel panel)
    {
        var draft = _draft!; var selection = new StackPanel { Spacing = 6 };
        void Refresh()
        {
            selection.Children.Clear(); var chosen = AdminHomeSectionsRules.LibraryFilterIds(draft.Config);
            selection.Children.Add(U.Text(chosen.Count == 0 ? T("web.home.addRow.params.allLibraries") : T("web.home.addRow.libraries"), 13));
            foreach (var id in chosen.ToArray())
            {
                var name = _libraries.FirstOrDefault(library => library.Id == id.ToString())?.Label ?? _choiceLabels.GetValueOrDefault("library:" + id) ?? "Library " + id;
                var remove = Action("Remove " + name, "HomeSectionLibraryRemove_" + id);
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
                line.Children.Add(U.Text(_choiceLabels.GetValueOrDefault(ids[i], "Title (" + ids[i] + ")"), 13));
                var up = Action("↑", "HomeSectionTitleUp_" + i); up.IsEnabled = i > 0;
                up.Click += (_, _) => { var array = Ids().DeepClone().AsArray(); var node = array[at]; array.RemoveAt(at); array.Insert(at - 1, node); SetConfig("item_ids", array); Refresh(); };
                var down = Action("↓", "HomeSectionTitleDown_" + i); down.IsEnabled = i < ids.Length - 1;
                down.Click += (_, _) => { var array = Ids().DeepClone().AsArray(); var node = array[at]; array.RemoveAt(at); array.Insert(at + 1, node); SetConfig("item_ids", array); Refresh(); };
                var remove = Action("Remove", "HomeSectionTitleRemove_" + i);
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
        var search = Input("Search " + kind, "HomeSectionSearch_" + kind, "", _ => { }, 200);
        var run = Action("Search", "HomeSectionSearchGo_" + kind); var next = Action("Next", "HomeSectionPickerNext_" + kind);
        var previous = Action("Previous", "HomeSectionPickerPrevious_" + kind);
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
            status.Text = filtered.Length == 0 ? "No matches." : $"{filtered.Length} choices · page {page + 1}";
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
            if (kind == "titles" && search.Text.Trim().Length == 0) { status.Text = "Enter a title to search."; return; }
            if (metadata != null) { if (!continuation) page = 0; ShowMetadata(); return; }
            var id = ++request; run.IsEnabled = next.IsEnabled = previous.IsEnabled = false; status.Text = "Loading choices…";
            try
            {
                var value = await _api.ChoicesAsync(_editorOwner!, kind, search.Text.Trim(), continuation ? cursor : null, token);
                if (!Current(id)) return;
                results.Children.Clear();
                if (kind == "titles")
                {
                    foreach (var choice in value.Items) Add(choice);
                    cursor = value.Page?.HasMore == true ? value.Page.NextCursor : null;
                    status.Text = value.Items.Count == 0 ? "No titles found." : "Showing up to 50 titles. Refine your search or choose Next.";
                    next.IsEnabled = cursor != null; previous.IsEnabled = false;
                }
                else { metadata = kind == "collections" ? value.Items.Where(choice => choice.Visibility == "visible").ToList() : value.Items; page = 0; ShowMetadata(); }
            }
            catch (Exception) { if (Current(id)) status.Text = kind == "collections" ? "Couldn't load the bounded collection picker. Retry or find a collection by ID below." : "Couldn't load choices. Retry Search."; }
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
