using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminProvidersViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminProvidersViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<MetadataProvider> Providers { get; } = [];

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
            var response = await _adminApi.GetProvidersAsync();
            Providers.Clear();
            foreach (var p in response.Providers) Providers.Add(p);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Create =====

    public async Task<MetadataProvider?> CreateProviderAsync(CreateProviderRequest request)
    {
        try
        {
            var provider = await _adminApi.CreateProviderAsync(request);
            StatusMessage = $"Provider \"{provider.Slug}\" created.";
            await LoadAsync();
            return provider;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    // ===== Update =====

    public async Task UpdateProviderAsync(int id, object request)
    {
        try
        {
            await _adminApi.UpdateProviderAsync(id, request);
            StatusMessage = "Provider updated.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Delete =====

    [RelayCommand]
    public async Task DeleteProviderAsync(int id)
    {
        try
        {
            await _adminApi.DeleteProviderAsync(id);
            StatusMessage = "Provider deleted.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
