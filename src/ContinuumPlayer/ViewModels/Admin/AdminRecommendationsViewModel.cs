using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminRecommendationsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    // Server settings (for the configuration sections)
    private Dictionary<string, string> _serverSettings = new();
    private HashSet<string> _sensitiveConfigured = new();

    public AdminRecommendationsViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private RecommendationsStatus? _status;

    // ===== Load =====

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            // Load job status and server settings in parallel
            var statusTask = _adminApi.GetRecommendationsStatusAsync();
            var settingsTask = _adminApi.GetAdminSettingsAsync();
            var sensitiveTask = _adminApi.GetSensitiveStatusAsync();

            await Task.WhenAll(statusTask, settingsTask, sensitiveTask);

            Status = statusTask.Result;
            _serverSettings = settingsTask.Result;
            _sensitiveConfigured = sensitiveTask.Result;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ===== Settings helpers =====

    /// <summary>Returns the current server value for a setting key, or "" if not set.</summary>
    public string GetSetting(string key)
    {
        _serverSettings.TryGetValue(key, out var val);
        return val ?? "";
    }

    /// <summary>Returns whether a sensitive (password) setting has been configured on the server.</summary>
    public bool IsSensitiveConfigured(string key) => _sensitiveConfigured.Contains(key);

    /// <summary>
    /// Commits a setting change to the server (fire-and-forget from UI perspective).
    /// Updates local cache optimistically.
    /// </summary>
    public async Task UpdateSettingAsync(string key, string value)
    {
        try
        {
            await _adminApi.UpdateAdminSettingAsync(key, value);
            _serverSettings[key] = value;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
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
            await RefreshStatusAsync();
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
            await RefreshStatusAsync();
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
            await RefreshStatusAsync();
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
            await RefreshStatusAsync();
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

    // ===== Helpers =====

    public async Task RefreshStatusAsync()
    {
        try
        {
            Status = await _adminApi.GetRecommendationsStatusAsync();
        }
        catch
        {
            // Ignore refresh errors — don't overwrite an existing error message
        }
    }

    public bool AnyJobRunning =>
        Status is { } s && (s.Embeddings.Running || s.TasteProfiles.Running || s.Cowatch.Running || s.Recommendations.Running);
}
