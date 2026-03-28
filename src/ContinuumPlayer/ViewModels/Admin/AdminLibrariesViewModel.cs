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

    [ObservableProperty] private bool _isLoading;
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
            var libs = await _catalogApi.GetLibrariesAsync();
            Libraries.Clear();
            foreach (var l in libs) Libraries.Add(l);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Scan All =====

    [RelayCommand]
    private async Task ScanAllAsync()
    {
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            await _adminApi.RunScanLibrariesTaskAsync();
            StatusMessage = "Scan all libraries started.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Scan Library =====

    [RelayCommand]
    private async Task ScanLibraryAsync(int id)
    {
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            await _adminApi.ScanLibraryAsync(id);
            StatusMessage = "Library scan started.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Refresh Metadata =====

    [RelayCommand]
    private async Task RefreshMetadataAsync(int id)
    {
        StatusMessage = null;
        ErrorMessage = null;
        try
        {
            await _adminApi.RefreshLibraryMetadataAsync(id);
            StatusMessage = "Metadata refresh started.";
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
