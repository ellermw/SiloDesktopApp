using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.ViewModels.Admin;

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

    // ===== Active Scans (from event channel) =====
    public List<AdminScanRun> ActiveScans { get; set; } = [];

    // ===== Active Refresh Jobs (library_refresh type) =====
    public List<AdminJob> ActiveRefreshJobs { get; set; } = [];

    // ===== Unmatched Items =====
    public List<UnmatchedLibraryItem> UnmatchedItems { get; set; } = [];
    public int UnmatchedTotal { get; set; }
    [ObservableProperty] private int _unmatchedPage;

    // ===== Stale Media IDs =====
    public List<StaleMediaId> StaleIds { get; set; } = [];

    // ===== Ambiguous Roots =====
    public List<LibraryRoot> AmbiguousRoots { get; set; } = [];

    // ===== Metadata Providers =====
    public List<MetadataProvider> MetadataProviders { get; set; } = [];

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

        // These surfaces are part of the page, not optional background data.
        // Run them concurrently so the first visual rebuild includes every
        // diagnostics card and active refresh row without serial latency.
        await Task.WhenAll(
            LoadSkippedRootsAsync(),
            LoadUnmatchedItemsAsync(),
            LoadStaleIdsAsync(),
            LoadMetadataProvidersAsync(),
            LoadActiveRefreshJobsAsync());
    }

    public async Task RefreshLibrariesOnlyAsync()
    {
        try
        {
            var libs = await _adminApi.GetAdminLibrariesAsync();
            Libraries.Clear();
            foreach (var l in libs) Libraries.Add(l);
        }
        catch
        {
            // Scan events are noisy; keep the current rows if a silent refresh misses.
        }
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

    public async Task LoadUnmatchedItemsAsync(string? search = null)
    {
        try
        {
            var response = await _adminApi.GetUnmatchedItemsAsync(10, UnmatchedPage * 10, search);
            UnmatchedItems = response.Items;
            UnmatchedTotal = response.Total;
            OnPropertyChanged(nameof(UnmatchedItems));
            OnPropertyChanged(nameof(UnmatchedTotal));
        }
        catch { /* Non-critical */ }
    }

    private async Task LoadStaleIdsAsync()
    {
        try
        {
            StaleIds = await _adminApi.GetStaleIdsAsync();
            OnPropertyChanged(nameof(StaleIds));
        }
        catch { /* Non-critical */ }
    }

    public async Task LoadAmbiguousRootsAsync(int libraryId)
    {
        try
        {
            var response = await _adminApi.GetLibraryRootsAsync(libraryId, "ambiguous");
            AmbiguousRoots = response.Items;
            OnPropertyChanged(nameof(AmbiguousRoots));
        }
        catch { AmbiguousRoots = []; OnPropertyChanged(nameof(AmbiguousRoots)); }
    }

    private async Task LoadMetadataProvidersAsync()
    {
        try
        {
            MetadataProviders = await _adminApi.GetProvidersAsync();
            OnPropertyChanged(nameof(MetadataProviders));
        }
        catch { /* Non-critical */ }
    }

    private async Task LoadActiveRefreshJobsAsync()
    {
        try
        {
            var jobs = await _adminApi.GetAdminJobsAsync("library_refresh", limit: 50);
            ActiveRefreshJobs = jobs.Where(j => j.Status is "running" or "queued" or "requested").ToList();
            OnPropertyChanged(nameof(ActiveRefreshJobs));
        }
        catch { ActiveRefreshJobs = []; }
    }

    // ===== Library Providers =====

    public async Task<LibraryProviderChainResponse?> GetLibraryProvidersAsync(int libraryId)
    {
        try { return await _adminApi.GetLibraryProvidersAsync(libraryId); }
        catch { return null; }
    }

    public async Task<bool> SetLibraryProvidersAsync(int libraryId, SetLibraryChainRequest request)
    {
        try
        {
            await _adminApi.UpdateLibraryProvidersAsync(libraryId, request);
            return true;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; return false; }
    }

    public async Task<LibraryProviderChainResponse?> GetLibraryProviderDefaultsAsync(string libraryType)
    {
        try { return await _adminApi.GetLibraryProviderDefaultsAsync(libraryType); }
        catch { return null; }
    }

    public Task<FilesystemBrowseResponse> BrowseFilesystemAsync(string path)
        => _adminApi.BrowseFilesystemAsync(path);

    public async Task<bool> CancelRefreshJobAsync(string jobId)
    {
        try
        {
            await _adminApi.CancelAdminJobAsync(jobId);
            StatusMessage = "Metadata refresh cancellation requested.";
            await LoadActiveRefreshJobsAsync();
            return true;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; return false; }
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

    // ===== Reorder Libraries =====

    [RelayCommand]
    private async Task ReorderLibrariesAsync(object body)
    {
        try
        {
            await _adminApi.ReorderLibrariesAsync(body);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Cancel Library Scans =====

    [RelayCommand]
    private async Task CancelLibraryScansAsync(int libraryId)
    {
        try
        {
            await _adminApi.CancelLibraryScansAsync(libraryId);
            StatusMessage = "Scan cancellation requested.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Root Override =====

    [RelayCommand]
    private async Task UpsertRootOverrideAsync(UpsertLibraryRootOverrideRequest request)
    {
        try
        {
            await _adminApi.UpsertLibraryRootOverrideAsync(request);
            StatusMessage = "Root override saved.";
            // Reload ambiguous roots for the same library
            await LoadAmbiguousRootsAsync(request.LibraryId);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteRootOverrideAsync(DeleteLibraryRootOverrideRequest request)
    {
        try
        {
            await _adminApi.DeleteLibraryRootOverrideAsync(request);
            StatusMessage = "Root override removed.";
            await LoadAmbiguousRootsAsync(request.LibraryId);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
