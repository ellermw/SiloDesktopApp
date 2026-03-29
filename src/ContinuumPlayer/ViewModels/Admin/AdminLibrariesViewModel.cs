using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminLibrariesViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private readonly CatalogApi _catalogApi;

    public AdminLibrariesViewModel(AdminApi adminApi, CatalogApi catalogApi)
    {
        _adminApi = adminApi;
        _catalogApi = catalogApi;
    }

    public ObservableCollection<Library> Libraries { get; } = [];
    public ObservableCollection<LibrarySkippedRoot> SkippedRoots { get; } = [];

    /// <summary>Per-library mount check results, keyed by library ID.</summary>
    public Dictionary<int, LibraryMountCheckResponse> MountCheckResults { get; } = new();

    /// <summary>Set of library IDs currently performing an operation (scan, refresh, mount check).</summary>
    public HashSet<int> ScanningIds { get; } = [];
    public HashSet<int> RefreshingIds { get; } = [];
    public HashSet<int> MountCheckingIds { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isScanningAll;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    // ===== Load =====

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var libs = await _adminApi.GetAdminLibrariesAsync();
            Libraries.Clear();
            foreach (var l in libs) Libraries.Add(l);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }

        // Load skipped roots in background (non-blocking)
        _ = LoadSkippedRootsAsync();
    }

    private async Task LoadSkippedRootsAsync()
    {
        try
        {
            var skipped = await _adminApi.GetSkippedRootsAsync();
            SkippedRoots.Clear();
            foreach (var s in skipped) SkippedRoots.Add(s);
        }
        catch { /* Non-critical */ }
    }

    // ===== Scan All =====

    [RelayCommand]
    private async Task ScanAllAsync()
    {
        StatusMessage = null;
        ErrorMessage = null;
        IsScanningAll = true;
        try
        {
            await _adminApi.RunScanLibrariesTaskAsync();
            StatusMessage = "Scan all libraries started.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsScanningAll = false; }
    }

    // ===== Scan Library =====

    [RelayCommand]
    private async Task ScanLibraryAsync(int id)
    {
        StatusMessage = null;
        ErrorMessage = null;
        ScanningIds.Add(id);
        OnPropertyChanged(nameof(ScanningIds));
        try
        {
            await _adminApi.ScanLibraryAsync(id);
            StatusMessage = "Library scan started.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            ScanningIds.Remove(id);
            OnPropertyChanged(nameof(ScanningIds));
        }
    }

    // ===== Refresh Metadata =====

    [RelayCommand]
    private async Task RefreshMetadataAsync(int id)
    {
        StatusMessage = null;
        ErrorMessage = null;
        RefreshingIds.Add(id);
        OnPropertyChanged(nameof(RefreshingIds));
        try
        {
            await _adminApi.RefreshLibraryMetadataAsync(id);
            StatusMessage = "Metadata refresh started.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            RefreshingIds.Remove(id);
            OnPropertyChanged(nameof(RefreshingIds));
        }
    }

    // ===== Check Mount =====

    [RelayCommand]
    private async Task CheckMountAsync(int id)
    {
        StatusMessage = null;
        ErrorMessage = null;
        MountCheckingIds.Add(id);
        OnPropertyChanged(nameof(MountCheckingIds));
        try
        {
            var result = await _adminApi.CheckLibraryMountAsync(id);
            MountCheckResults[id] = result;
            OnPropertyChanged(nameof(MountCheckResults));
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            MountCheckingIds.Remove(id);
            OnPropertyChanged(nameof(MountCheckingIds));
        }
    }

    // ===== Confirm Empty Root Cleanup =====

    [RelayCommand]
    private async Task ConfirmEmptyRootCleanupAsync(int id)
    {
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            await _adminApi.ConfirmEmptyRootCleanupAsync(id);
            StatusMessage = "Empty root cleanup confirmed.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Create Library =====

    [RelayCommand]
    private async Task CreateLibraryAsync(object body)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _adminApi.CreateLibraryAsync(body);
            await LoadAsync();
            StatusMessage = "Library created.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Update Library =====

    [RelayCommand]
    private async Task UpdateLibraryAsync((int Id, object Body) args)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _adminApi.UpdateLibraryAsync(args.Id, args.Body);
            await LoadAsync();
            StatusMessage = "Library updated.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Delete Library =====

    [RelayCommand]
    private async Task DeleteLibraryAsync(int id)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _adminApi.DeleteLibraryAsync(id);
            await LoadAsync();
            StatusMessage = "Library deleted.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }
}
