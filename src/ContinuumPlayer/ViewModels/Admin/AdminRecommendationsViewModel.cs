using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminRecommendationsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminRecommendationsViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    // ===== Load (no-op — page is static, but required by convention) =====

    [RelayCommand]
    public Task LoadAsync()
    {
        // No data to load — the page shows 4 trigger cards
        return Task.CompletedTask;
    }

    // ===== Run Embeddings =====

    [ObservableProperty] private bool _isRunningEmbeddings;

    [RelayCommand]
    public async Task RunEmbeddingsAsync()
    {
        IsRunningEmbeddings = true;
        ErrorMessage = null;
        try
        {
            await _adminApi.RunEmbeddingsAsync();
            StatusMessage = "Embeddings job started.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRunningEmbeddings = false;
        }
    }

    // ===== Run Taste Profiles =====

    [ObservableProperty] private bool _isRunningTasteProfiles;

    [RelayCommand]
    public async Task RunTasteProfilesAsync()
    {
        IsRunningTasteProfiles = true;
        ErrorMessage = null;
        try
        {
            await _adminApi.RunTasteProfilesAsync();
            StatusMessage = "Taste Profiles job started.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRunningTasteProfiles = false;
        }
    }

    // ===== Run Co-Watch =====

    [ObservableProperty] private bool _isRunningCowatch;

    [RelayCommand]
    public async Task RunCowatchAsync()
    {
        IsRunningCowatch = true;
        ErrorMessage = null;
        try
        {
            await _adminApi.RunCowatchAsync();
            StatusMessage = "Co-Watch Matrix job started.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRunningCowatch = false;
        }
    }

    // ===== Run Recommendations =====

    [ObservableProperty] private bool _isRunningRecommendations;

    [RelayCommand]
    public async Task RunRecommendationsAsync()
    {
        IsRunningRecommendations = true;
        ErrorMessage = null;
        try
        {
            await _adminApi.RunGenerateRecommendationsAsync();
            StatusMessage = "Recommendations job started.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRunningRecommendations = false;
        }
    }
}
