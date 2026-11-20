using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage
{
    private int _memberRead;
    private async Task LoadMembersAsync()
    {
        var owner = Controller.Owner; var serial = ++_memberRead;
        var id = Controller.Editor?.Value["id"]?.GetValue<string>();
        _preview.Children.Clear();
        // On a saved collection these are what a save that stopped part-way has not sent yet. They stay listed, so they can be removed before the next Save sends them.
        void Staged(bool saved)
        {
            if (saved && _pendingTitles.Count > 0) _preview.Children.Add(Text("Not added yet. Save adds these titles.", 13, true));
            foreach (var (item, title) in _pendingTitles.ToArray())
            {
                var row = Wrap(); row.Children.Add(Text(title));
                row.Children.Add(ActionButton("Remove", "CollectionPendingRemove_" + item, () => { _pendingTitles.Remove(item); return LoadMembersAsync(); }));
                _preview.Children.Add(row);
            }
        }
        if (id == null)
        {
            Staged(false);
            _preview.Children.Add(Text(_pendingTitles.Count == 0 ? "Add titles, then create the collection." : $"{_pendingTitles.Count} titles ready to add", 13, true));
            return;
        }
        _preview.Children.Add(Text("Loading titles…"));
        try
        {
            var response = await _api.ItemsAsync(owner!, id, _memberCursor);
            if (!Current(owner) || serial != _memberRead) return;
            var items = (response["items"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
            var hasMore = response["page"]?["has_more"]?.GetValue<bool>() == true;
            var next = response["page"]?["next_cursor"]?.ToString();
            if (items.Length > AdminCollectionsApi.PageSize || hasMore && (string.IsNullOrEmpty(next) || next == _memberCursor || next.Length > 8192)) throw new InvalidDataException("The titles page changed. Restart titles.");
            var order = Controller.Capabilities?.ItemReorder == true && _memberCursor == null ? await _api.ItemOrderAsync(owner!, id) : null;
            if (!Current(owner) || serial != _memberRead) return;
            _preview.Children.Clear();
            foreach (var item in items)
            {
                var itemId = item["media_item_id"]!.ToString(); var row = Wrap();
                row.Children.Add(Text(item["title"]?.ToString() ?? itemId));
                row.Children.Add(ActionButton("Remove", "CollectionMemberRemove_" + itemId, async () =>
                {
                    await RunTitlesAsync(async lease => { await _api.MembershipAsync(lease, id, itemId, false); });
                    _memberCursor = null; _memberHistory.Clear(); await LoadMembersAsync();
                }));
                if (order != null && order.Value["has_more"]?.GetValue<bool>() != true)
                {
                    var ids = AdminCollectionsApi.Order(order); var index = ids.IndexOf(itemId);
                    foreach (var direction in new[] { -1, 1 })
                    {
                        var nextIndex = index + direction;
                        row.Children.Add(ActionButton(direction < 0 ? "Move up" : "Move down", "CollectionMemberMove_" + itemId + "_" + direction, async () =>
                        {
                            var moved = ids.ToList(); moved.RemoveAt(index); moved.Insert(nextIndex, itemId);
                            await RunTitlesAsync(async lease => { await _api.ReorderItemsAsync(lease, id, order, moved); }); await LoadMembersAsync();
                        }, index >= 0 && nextIndex >= 0 && nextIndex < ids.Count));
                    }
                }
                _preview.Children.Add(Card(row));
            }
            if (items.Length == 0) _preview.Children.Add(Text("No titles yet"));
            if (order?.Value["has_more"]?.GetValue<bool>() == true) _preview.Children.Add(Text("More than 200 titles: reorder is unavailable. Add and remove titles individually.", 13, true));
            var paging = Wrap();
            paging.Children.Add(ActionButton("Previous", "CollectionMembersPrevious", async () => { _memberCursor = _memberHistory.Pop(); await LoadMembersAsync(); }, _memberHistory.Count > 0));
            paging.Children.Add(ActionButton("Next", "CollectionMembersNext", async () => { _memberHistory.Push(_memberCursor); _memberCursor = next; await LoadMembersAsync(); }, hasMore && _memberHistory.Count < 100));
            paging.Children.Add(ActionButton("Restart titles", "CollectionMembersRestart", async () => { _memberCursor = null; _memberHistory.Clear(); await LoadMembersAsync(); }));
            _preview.Children.Add(paging);
            Staged(true);
        }
        catch (Exception error)
        {
            if (!Current(owner) || serial != _memberRead) return;
            _preview.Children.Clear(); _preview.Children.Add(Text(AdminCollectionsController.Message(error)));
            _preview.Children.Add(ActionButton("Restart titles", "CollectionMembersRetry", async () => { _memberCursor = null; _memberHistory.Clear(); await LoadMembersAsync(); }));
            Staged(true);
        }
    }
    private async Task TitlePickerAsync()
    {
        var owner = Controller.Owner!; var draft = Controller.Draft; if (draft == null || _editorBusy) return;
        var libraries = (draft["library_ids"] as JsonArray)?.Select(v => v!.ToString()).ToArray() ?? [];
        if (libraries.Length == 0) { Notice("Pick libraries first."); return; }
        var body = Stack(12); var results = Stack(8);
        var library = libraries[0];
        body.Children.Add(Select("CollectionTitleLibrary", Controller.Libraries.Where(l => libraries.Contains(l.Id)).Select(l => (l.Id, l.Name)), library, value => { library = value; results.Children.Clear(); return Task.CompletedTask; }));
        var query = new TextBox { PlaceholderText = "Search titles", MaxLength = 256 }; Identify(query, "CollectionTitleSearch", "Search titles"); body.Children.Add(query);
        var selected = new Dictionary<string, string>();
        var searchSerial = 0;
        async Task Search()
        {
            var serial = ++searchSerial; var scope = library; var text = query.Text;
            results.Children.Clear();
            try
            {
                var response = await _api.SearchAsync(owner, text, scope);
                if (!Current(owner) || serial != searchSerial || scope != library || text != query.Text) return;
                var items = (response["items"] as JsonArray)?.OfType<JsonObject>().Take(30).ToArray() ?? [];
                foreach (var item in items)
                {
                    var id = item["content_id"]?.ToString(); if (string.IsNullOrEmpty(id)) continue;
                    var title = item["title"]?.ToString() ?? id;
                    var pick = new CheckBox { Content = title, IsChecked = selected.ContainsKey(id) };
                    Identify(pick, "CollectionTitlePick_" + id, title);
                    Toggled(pick, on => { if (on && selected.Count < 100) selected[id] = title; else selected.Remove(id); return Task.CompletedTask; });
                    results.Children.Add(pick);
                }
                if (items.Length == 0) results.Children.Add(Text("No titles match."));
                if (response["page"]?["has_more"]?.GetValue<bool>() == true) results.Children.Add(Text("More titles match. Refine your search.", 13, true));
            }
            catch (Exception error) { if (Current(owner)) { results.Children.Clear(); results.Children.Add(Text(AdminCollectionsController.Message(error))); } }
        }
        body.Children.Add(ActionButton("Search", "CollectionTitlesSearchButton", Search));
        body.Children.Add(results);
        var dialog = new ContentDialog { Title = "Add titles", Content = new ScrollViewer { Content = body, MaxHeight = 450 }, PrimaryButtonText = "Add titles", CloseButtonText = "Cancel" };
        Identify(dialog, "CollectionTitlePicker", "Add titles");
        if (await ShowAsync(dialog) != ContentDialogResult.Primary || !Current(owner)) return;
        var collectionId = Controller.Editor?.Value["id"]?.ToString();
        if (collectionId == null)
        {
            foreach (var (id, title) in selected) if (_pendingTitles.Count < 200) _pendingTitles[id] = title;
        }
        else
        {
            await RunTitlesAsync(async lease =>
            {
                // Individual natural-membership changes, never replace the full set. Stop on the first uncertain result.
                var canonical = await _api.GetAsync(lease, collectionId);
                // After the last title, not at the item count: a removed title leaves a gap and the count would tie with the last position.
                var position = AdminCollectionsApi.NextMemberPosition(await _api.ItemsAsync(lease, collectionId), canonical.Value["item_count"]?.GetValue<int>() ?? 0);
                foreach (var (id, _) in selected) await _api.MembershipAsync(lease, collectionId, id, true, position++);
            });
        }
        await LoadMembersAsync();
    }
}
