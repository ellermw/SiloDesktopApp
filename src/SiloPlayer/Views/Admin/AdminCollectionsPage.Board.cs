using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminCollectionsPage
{
    private string? _grabbed;
    private readonly Dictionary<string, int> _shelfWindows = [];
    private bool _viewerPreview;
    private void RenderBoard()
    {
        var board = Controller.Board;
        if (Controller.Capabilities?.Groups != true) { _rows.Children.Add(Text("Shelves are not available on this server.")); return; }
        if (board == null) { _rows.Children.Add(Text("Add a library before arranging collections.")); return; }
        _rows.Children.Add(Text($"Shelves on {Controller.Libraries.FirstOrDefault(l => l.Id == board.LibraryId)?.Name} › Collections", 22));
        _rows.Children.Add(Text("Shelves are set separately for each library. Top to bottom, the way viewers see them.", 14, true));
        _rows.Children.Add(Text("Drag shelves and collections, or focus a handle and press Space, then the arrow keys. ⋯ has Move to shelf. Changes save right away.", 13, true));
        var tools = Wrap(); tools.Children.Add(ActionButton("Add shelf", "CollectionsAddShelf", () => ShelfDialogAsync(null)));
        tools.Children.Add(ActionButton(_viewerPreview ? "Back to Arrange" : "What viewers see", "CollectionsViewerPreview", () => { _viewerPreview = !_viewerPreview; RenderRows(); return Task.CompletedTask; }));
        _rows.Children.Add(tools);
        if (_viewerPreview) _rows.Children.Add(Text("The pin marks a collection kept at the start of its shelf. Hidden collections don’t appear.", 13, true));
        var groups = AdminCollectionsApi.Order(board.GroupOrder);
        foreach (var groupId in groups)
        {
            var group = board.List.Groups.FirstOrDefault(g => g.Id == groupId);
            var editable = groupId == "ungrouped" || group?.Kind == "regular";
            var panel = Stack(12); var header = Wrap();
            var title = groupId == "ungrouped" ? "No heading" : group?.Name ?? "";
            header.Children.Add(Text(title, 19));
            if (!_viewerPreview)
            {
                var handle = ActionButton("↕", "CollectionShelfHandle_" + groupId, () => { _grabbed = _grabbed == "shelf:" + groupId ? null : "shelf:" + groupId; return Task.CompletedTask; });
                WireDrag(handle, "shelf:" + groupId, async text =>
                {
                    if (text.StartsWith("shelf:", StringComparison.Ordinal)) await ReorderShelfAsync(text[6..], groupId);
                });
                handle.KeyDown += async (_, e) =>
                {
                    if (e.Key == VirtualKey.Space) { _grabbed = _grabbed == "shelf:" + groupId ? null : "shelf:" + groupId; e.Handled = true; }
                    if (_grabbed == "shelf:" + groupId && e.Key is VirtualKey.Up or VirtualKey.Down)
                    {
                        e.Handled = true; var next = groups.IndexOf(groupId) + (e.Key == VirtualKey.Up ? -1 : 1);
                        if (next >= 0 && next < groups.Count) await ReorderShelfAsync(groupId, groups[next]);
                    }
                    if (e.Key == VirtualKey.Escape) _grabbed = null;
                };
                header.Children.Add(handle);
                if (group?.Kind == "regular")
                {
                    header.Children.Add(ActionButton("Rename shelf", "CollectionShelfRename_" + groupId, () => ShelfDialogAsync(groupId)));
                    header.Children.Add(ActionButton("Delete shelf…", "CollectionShelfDelete_" + groupId, () => DeleteShelfDialogAsync(groupId)));
                }
            }
            panel.Children.Add(header);
            if (groupId == "ungrouped") panel.Children.Add(Text("Collections not on a shelf, shown without a title", 12, true));
            if (!editable) panel.Children.Add(Text("Each viewer’s own collections appear here.", 13, true));
            else
            {
                var ids = AdminCollectionsApi.Order(board.Orders[groupId]);
                IEnumerable<AdminCollectionEntry> items = ids.Select(id => board.List.Items.First(c => c.Id == id));
                if (_viewerPreview)
                {
                    items = items.Where(c => c.Visibility != "hidden");
                    items = group?.DefaultSortMode switch
                    {
                        "name_asc" => items.OrderBy(c => c.Title, StringComparer.Ordinal),
                        "name_desc" => items.OrderByDescending(c => c.Title, StringComparer.Ordinal),
                        "most_items" => items.OrderByDescending(c => c.ItemCount),
                        "recent" => items.OrderByDescending(c => c.UpdatedAt, StringComparer.Ordinal),
                        _ => items
                    };
                    if (group?.DefaultSortMode is null or "manual") items = items.OrderByDescending(c => c.Featured);
                }
                var shown = items.ToList(); var cards = Wrap();
                foreach (var c in shown.Take(_shelfWindows.GetValueOrDefault(groupId, 30)))
                {
                    var tile = Stack(8); tile.Width = 220;
                    tile.Children.Add(Poster(c.PosterUrl, 188, c.Title));
                    tile.Children.Add(ActionButton((_viewerPreview && c.Featured && (group?.DefaultSortMode is null or "manual") ? "📌 " : "") + c.Title, "CollectionBoardEdit_" + c.Id, () => { OpenEditor(c.Id); return Task.CompletedTask; }));
                    tile.Children.Add(Text(Titles(c.ItemCount) + (c.Visibility == "hidden" ? " · Hidden" : ""), 12, true));
                    if (!_viewerPreview)
                    {
                        var move = ActionButton("Move to shelf", "CollectionMove_" + c.Id, () => MoveDialogAsync(c.Id));
                        tile.Children.Add(move);
                        tile.Children.Add(ActionButton(c.Featured ? "Unpin" : "Pin to the start of its shelf", "CollectionPin_" + c.Id, () => PatchAsync(c.Id, new() { ["featured"] = !c.Featured })));
                        tile.Children.Add(ActionButton("Delete…", "CollectionDelete_" + c.Id, () => DeleteDialogAsync(c.Id)));
                        var handle = ActionButton("↕", "CollectionHandle_" + c.Id, () => { _grabbed = _grabbed == "collection:" + c.Id ? null : "collection:" + c.Id; return Task.CompletedTask; });
                        WireDrag(handle, "collection:" + c.Id, async text =>
                        {
                            if (text.StartsWith("collection:", StringComparison.Ordinal)) await MoveCollectionAsync(text[11..], groupId, ids.IndexOf(c.Id));
                        });
                        handle.KeyDown += async (_, e) =>
                        {
                            if (e.Key == VirtualKey.Space) { _grabbed = _grabbed == "collection:" + c.Id ? null : "collection:" + c.Id; e.Handled = true; }
                            if (_grabbed == "collection:" + c.Id && e.Key is VirtualKey.Up or VirtualKey.Down)
                            { e.Handled = true; await MoveCollectionAsync(c.Id, groupId, ids.IndexOf(c.Id) + (e.Key == VirtualKey.Up ? -1 : 1)); }
                            if (e.Key == VirtualKey.Escape) _grabbed = null;
                        };
                        tile.Children.Add(handle);
                    }
                    cards.Children.Add(Card(tile));
                }
                if (!_viewerPreview) WireDrop(panel, async text => { if (text.StartsWith("collection:", StringComparison.Ordinal)) await MoveCollectionAsync(text[11..], groupId, ids.Count); });
                panel.Children.Add(cards);
                if (shown.Count > _shelfWindows.GetValueOrDefault(groupId, 30)) panel.Children.Add(ActionButton("Show more collections", "CollectionShelfMore_" + groupId, () => { _shelfWindows[groupId] = _shelfWindows.GetValueOrDefault(groupId, 30) + 30; RenderRows(); return Task.CompletedTask; }));
                if (shown.Count == 0) panel.Children.Add(Text("No collections", 13, true));
            }
            _rows.Children.Add(Card(panel));
        }
    }
    private void WireDrag(UIElement element, string value, Func<string, Task> drop)
    {
        element.CanDrag = true;
        element.DragStarting += (_, args) => { _grabbed = value; args.Data.SetText(value); args.Data.RequestedOperation = DataPackageOperation.Move; };
        element.DropCompleted += (_, _) => _grabbed = null;
        WireDrop(element, drop);
    }
    private void WireDrop(UIElement element, Func<string, Task> drop)
    {
        element.AllowDrop = true;
        element.DragOver += (_, args) => { args.AcceptedOperation = _grabbed != null && Controller.Owner?.IsCurrent == true ? DataPackageOperation.Move : DataPackageOperation.None; args.Handled = true; };
        element.Drop += async (_, args) =>
        {
            args.Handled = true; var value = _grabbed; _grabbed = null;
            try { if (value != null && _active) await drop(value); }
            catch (Exception error) { if (_active) Notice(SiloPlayer.Core.Services.Admin.AdminCollectionsController.Message(error)); }
        };
    }
    private Task MoveCollectionAsync(string id, string group, int index)
    {
        var board = Controller.Board;
        return board == null ? Task.CompletedTask : RunAsync(async lease => { await _api.MoveAsync(lease, board, id, group, index); });
    }
    private Task ReorderShelfAsync(string id, string target)
    {
        var board = Controller.Board; if (board == null || id == target) return Task.CompletedTask;
        var ids = AdminCollectionsApi.Order(board.GroupOrder); var index = ids.IndexOf(target); if (!ids.Remove(id) || index < 0) return Task.CompletedTask;
        ids.Insert(Math.Min(index, ids.Count), id);
        return RunAsync(async lease => { await _api.ReorderShelvesAsync(lease, board, ids); });
    }
}
