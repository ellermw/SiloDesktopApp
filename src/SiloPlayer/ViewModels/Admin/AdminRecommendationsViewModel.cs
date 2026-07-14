using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminRecommendationsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private CancellationTokenSource? _loadCts;

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
        var ownerCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, ownerCts);
        previous?.Cancel();
        previous?.Dispose();

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            // Load job status and server settings in parallel
            var statusTask = _adminApi.GetRecommendationsStatusAsync(ownerCts.Token);
            var settingsTask = _adminApi.GetAdminSettingsAsync(ownerCts.Token);
            var sensitiveTask = _adminApi.GetSensitiveStatusAsync(ownerCts.Token);

            await Task.WhenAll(statusTask, settingsTask, sensitiveTask);
            ownerCts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, ownerCts)) return;

            Status = statusTask.Result;
            _serverSettings = settingsTask.Result;
            _sensitiveConfigured = sensitiveTask.Result.Configured;
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadCts, ownerCts))
                ErrorMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, ownerCts), ownerCts))
                IsLoading = false;
            ownerCts.Dispose();
        }
    }

    public void CancelLoad()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
        IsLoading = false;
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
    public async Task<bool> UpdateSettingAsync(string key, string value)
    {
        try
        {
            await _adminApi.UpdateAdminSettingAsync(key, value);
            _serverSettings[key] = value;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
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
