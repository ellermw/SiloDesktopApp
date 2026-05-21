using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminCollectionsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

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
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            // Load libraries if not yet loaded
            if (Libraries.Count == 0)
            {
                var libs = await _adminApi.GetAdminLibrariesAsync();
                Libraries.Clear();
                foreach (var l in libs) Libraries.Add(l);
            }

            // Load collections, optionally filtered
            var response = await _adminApi.GetCollectionsAsync(SelectedLibraryId);
            Collections.Clear();
            foreach (var c in response.Collections) Collections.Add(c);

            CollectionGroups.Clear();
            if (SelectedLibraryId.HasValue)
            {
                var groupResponse = await _adminApi.GetCollectionGroupsAsync(SelectedLibraryId.Value);
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
        finally { IsLoading = false; }
    }

    public async Task CreateGroupAsync(string name)
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
            new CreateLibraryCollectionGroupRequest { Name = name.Trim(), DefaultSortMode = "manual" });
        StatusMessage = "Collection group created.";
        await LoadAsync();
    }

    public async Task UpdateGroupAsync(LibraryCollectionGroup group, string name)
    {
        ErrorMessage = null;
        StatusMessage = null;
        await _adminApi.UpdateCollectionGroupAsync(
            group.Id,
            new UpdateLibraryCollectionGroupRequest { Name = name.Trim(), DefaultSortMode = group.DefaultSortMode });
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
