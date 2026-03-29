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
    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int? _selectedLibraryId;

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
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
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
