using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminCollectionsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private int _loadVersion;

    public AdminCollectionsViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<LibraryCollection> Collections { get; } = [];
    public ObservableCollection<LibraryCollectionGroup> CollectionGroups { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int? _selectedLibraryId;
    [ObservableProperty] private int _ungroupedSortOrder = 9999;

    // ===== Load =====

    [RelayCommand]
    private async Task LoadAsync()
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var libraryTask = Libraries.Count == 0 ? _adminApi.GetAdminLibrariesAsync() : null;
            var collectionsTask = _adminApi.GetCollectionsAsync(SelectedLibraryId);
            var groupsTask = SelectedLibraryId.HasValue
                ? _adminApi.GetCollectionGroupsAsync(SelectedLibraryId.Value)
                : null;
            var requests = new List<Task> { collectionsTask };
            if (libraryTask != null) requests.Add(libraryTask);
            if (groupsTask != null) requests.Add(groupsTask);
            await Task.WhenAll(requests);
            if (loadVersion != _loadVersion) return;

            if (libraryTask != null)
            {
                Libraries.Clear();
                foreach (var library in await libraryTask) Libraries.Add(library);
            }

            var response = await collectionsTask;
            Collections.Clear();
            foreach (var c in response.Collections) Collections.Add(c);

            CollectionGroups.Clear();
            if (SelectedLibraryId.HasValue)
            {
                var groupResponse = await groupsTask!;
                UngroupedSortOrder = groupResponse.UngroupedSortOrder;
                foreach (var group in groupResponse.Groups.OrderBy(g => g.SortOrder))
                    CollectionGroups.Add(group);
            }
            else
            {
                UngroupedSortOrder = 9999;
                foreach (var group in response.Groups.OrderBy(g => g.SortOrder))
                    CollectionGroups.Add(group);
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            if (loadVersion == _loadVersion) IsLoading = false;
        }
    }

    public async Task CreateGroupAsync(string name, string defaultSortMode = "manual")
    {
        if (!SelectedLibraryId.HasValue)
        {
            ErrorMessage = "Select a library before creating a collection group.";
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        await _adminApi.CreateCollectionGroupAsync(
            SelectedLibraryId.Value,
            new CreateLibraryCollectionGroupRequest { Name = name.Trim(), DefaultSortMode = defaultSortMode });
        StatusMessage = "Collection group created.";
        await LoadAsync();
    }

    public async Task UpdateGroupAsync(LibraryCollectionGroup group, string name, string defaultSortMode)
    {
        ErrorMessage = null;
        StatusMessage = null;
        await _adminApi.UpdateCollectionGroupAsync(
            group.Id,
            new UpdateLibraryCollectionGroupRequest { Name = name.Trim(), DefaultSortMode = defaultSortMode });
        StatusMessage = "Collection group updated.";
        await LoadAsync();
    }

    public async Task DeleteGroupAsync(LibraryCollectionGroup group)
    {
        ErrorMessage = null;
        StatusMessage = null;
        await _adminApi.DeleteCollectionGroupAsync(group.Id);
        StatusMessage = "Collection group deleted.";
        await LoadAsync();
    }

    public async Task MoveGroupAsync(LibraryCollectionGroup group, int delta)
    {
        if (!SelectedLibraryId.HasValue) return;

        var ordered = CollectionGroups.OrderBy(g => g.SortOrder).ToList();
        var index = ordered.FindIndex(g => g.Id == group.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count)
            return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        await _adminApi.ReorderCollectionGroupsAsync(SelectedLibraryId.Value, ordered.Select(g => g.Id).ToList());
        await LoadAsync();
    }

    public async Task MoveGroupSectionToAsync(string sourceId, string targetId)
    {
        if (!SelectedLibraryId.HasValue || sourceId == targetId) return;

        var sections = CollectionGroups
            .Select(group => (Id: group.Id, Order: group.SortOrder, Name: group.Name))
            .Append((Id: "ungrouped", Order: UngroupedSortOrder, Name: "\uffff"))
            .OrderBy(section => section.Order)
            .ThenBy(section => section.Name, StringComparer.OrdinalIgnoreCase)
            .Select(section => section.Id)
            .ToList();
        var sourceIndex = sections.IndexOf(sourceId);
        var targetIndex = sections.IndexOf(targetId);
        if (sourceIndex < 0 || targetIndex < 0) return;

        sections.RemoveAt(sourceIndex);
        targetIndex = sections.IndexOf(targetId);
        sections.Insert(targetIndex, sourceId);
        await _adminApi.ReorderCollectionGroupsAsync(SelectedLibraryId.Value, sections);
        await LoadAsync();
    }

    public async Task MoveCollectionInGroupAsync(LibraryCollection collection, int delta)
    {
        if (!SelectedLibraryId.HasValue) return;

        var groupId = collection.GroupId;
        var ordered = Collections
            .Where(c => string.Equals(c.GroupId, groupId, StringComparison.Ordinal))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Title)
            .ToList();
        var index = ordered.FindIndex(c => c.Id == collection.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count)
            return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        var orderedIds = ordered.Select(c => c.Id).ToList();
        if (string.IsNullOrEmpty(groupId))
        {
            await _adminApi.ReorderCollectionsInGroupAsync("ungrouped", orderedIds, SelectedLibraryId.Value);
        }
        else
        {
            await _adminApi.ReorderCollectionsInGroupAsync(groupId, orderedIds);
        }

        await LoadAsync();
    }

    public async Task MoveCollectionToAsync(string sourceId, string targetGroupId, string? targetCollectionId = null)
        => await MoveCollectionsToAsync([sourceId], targetGroupId, targetCollectionId);

    public async Task MoveCollectionsToAsync(
        IReadOnlyList<string> sourceIds,
        string targetGroupId,
        string? targetCollectionId = null)
    {
        if (!SelectedLibraryId.HasValue || sourceIds.Count == 0) return;

        var normalizedTarget = targetGroupId == "ungrouped" ? null : targetGroupId;
        var sourceSet = sourceIds.ToHashSet(StringComparer.Ordinal);
        var ordered = Collections
            .Where(collection => string.Equals(collection.GroupId, normalizedTarget, StringComparison.Ordinal))
            .OrderBy(collection => collection.SortOrder)
            .ThenBy(collection => collection.Title)
            .Select(collection => collection.Id)
            .Where(id => !sourceSet.Contains(id))
            .ToList();

        var targetIndex = targetCollectionId == null ? ordered.Count : ordered.IndexOf(targetCollectionId);
        if (targetIndex < 0) targetIndex = ordered.Count;
        ordered.InsertRange(targetIndex, sourceIds);

        await _adminApi.ReorderCollectionsInGroupAsync(
            targetGroupId,
            ordered,
            targetGroupId == "ungrouped" ? SelectedLibraryId.Value : null);
        await LoadAsync();
    }

    // ===== Sync Collection =====

    [RelayCommand]
    private async Task SyncCollectionAsync(string id)
    {
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            var run = await _adminApi.SyncCollectionAsync(id);
            StatusMessage = $"Sync started: {run.Status}";
            // Reload to reflect updated sync status
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Delete Collection =====

    [RelayCommand]
    private async Task DeleteCollectionAsync(string id)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _adminApi.DeleteCollectionAsync(id);
            await LoadAsync();
            StatusMessage = "Collection deleted.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }
}
