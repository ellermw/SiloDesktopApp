using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminSubtitleProvidersViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminSubtitleProvidersViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<SubtitleProviderConfig> Providers { get; } = [];

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
            var response = await _adminApi.GetSubtitleProvidersAsync();
            Providers.Clear();
            foreach (var p in response.Providers) Providers.Add(p);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Update =====

    public async Task UpdateProviderAsync(string provider, SubtitleProviderUpdateRequest request)
    {
        try
        {
            await _adminApi.UpdateSubtitleProviderAsync(provider, request);
            StatusMessage = $"Provider \"{provider}\" updated.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Test =====

    public async Task<SubtitleProviderTestResponse?> TestProviderAsync(string provider)
    {
        try
        {
            var result = await _adminApi.TestSubtitleProviderAsync(provider);
            StatusMessage = result.Success ? "Test passed." : $"Test failed: {result.Error}";
            return result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }
}
