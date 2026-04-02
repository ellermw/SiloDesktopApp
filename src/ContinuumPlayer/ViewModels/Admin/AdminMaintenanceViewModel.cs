using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminMaintenanceViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminMaintenanceViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<StaleMediaId> StaleIds { get; } = [];
    public ObservableCollection<LibrarySkippedRoot> SkippedRoots { get; } = [];
    public ObservableCollection<UnmatchedLibraryItem> UnmatchedItems { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    // ===== Load =====

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var staleTask = _adminApi.GetStaleIdsAsync();
            var skippedTask = _adminApi.GetSkippedRootsAsync();
            var unmatchedTask = _adminApi.GetUnmatchedItemsAsync();
            await Task.WhenAll(staleTask, skippedTask, unmatchedTask);

            StaleIds.Clear();
            foreach (var s in staleTask.Result.Items) StaleIds.Add(s);

            SkippedRoots.Clear();
            foreach (var r in skippedTask.Result) SkippedRoots.Add(r);

            UnmatchedItems.Clear();
            foreach (var u in unmatchedTask.Result) UnmatchedItems.Add(u);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Rematch Stale ID =====

    [RelayCommand]
    public async Task RematchStaleIdAsync(string contentId)
    {
        try
        {
            await _adminApi.RematchStaleIdAsync(contentId);
            StatusMessage = "Rematch initiated.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
