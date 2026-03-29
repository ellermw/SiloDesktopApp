using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminSettingsDetailViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private Dictionary<string, string> _settings = new();
    private readonly Dictionary<string, string> _dirtySettings = new();
    private HashSet<string> _sensitiveConfigured = new();

    // Rate limit config (loaded separately)
    public RateLimitConfig? RateLimitConfig { get; private set; }

    private RateLimitConfig? _dirtyRateLimitConfig;
    public RateLimitConfig? DirtyRateLimitConfig
    {
        get => _dirtyRateLimitConfig;
        set
        {
            _dirtyRateLimitConfig = value;
            // Update dirty state to include rate limit changes
            HasDirtyChanges = _dirtySettings.Count > 0 || _dirtyRateLimitConfig != null;
            DirtyCount = _dirtySettings.Count + (_dirtyRateLimitConfig != null ? 1 : 0);
        }
    }

    public AdminSettingsDetailViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasDirtyChanges;

    [ObservableProperty]
    private int _dirtyCount;

    [ObservableProperty]
    private bool _isSaving;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            _settings = await _adminApi.GetAdminSettingsAsync();
            try { _sensitiveConfigured = await _adminApi.GetSensitiveStatusAsync(); }
            catch { _sensitiveConfigured = new(); }
            try { RateLimitConfig = await _adminApi.GetRateLimitConfigAsync(); }
            catch { RateLimitConfig = null; }
            DirtyRateLimitConfig = null;
            _dirtySettings.Clear();
            HasDirtyChanges = false;
            DirtyCount = 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool IsSensitiveConfigured(string key) => _sensitiveConfigured.Contains(key);

    public string GetSetting(string key)
    {
        if (_dirtySettings.TryGetValue(key, out var dirty))
            return dirty;
        if (_settings.TryGetValue(key, out var val))
            return val;
        return "";
    }

    public void SetSetting(string key, string value)
    {
        // If the value matches the original, remove from dirty
        if (_settings.TryGetValue(key, out var original) && original == value)
        {
            _dirtySettings.Remove(key);
        }
        else
        {
            _dirtySettings[key] = value;
        }

        DirtyCount = _dirtySettings.Count + (_dirtyRateLimitConfig != null ? 1 : 0);
        HasDirtyChanges = _dirtySettings.Count > 0 || _dirtyRateLimitConfig != null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving || !HasDirtyChanges) return;

        IsSaving = true;
        StatusMessage = null;
        ErrorMessage = null;

        try
        {
            foreach (var (key, value) in _dirtySettings)
            {
                await _adminApi.UpdateAdminSettingAsync(key, value);
            }

            if (DirtyRateLimitConfig != null)
            {
                await _adminApi.UpdateRateLimitConfigAsync(DirtyRateLimitConfig);
                RateLimitConfig = DirtyRateLimitConfig;
                DirtyRateLimitConfig = null;
            }

            // Reload to get fresh values
            _settings = await _adminApi.GetAdminSettingsAsync();
            _dirtySettings.Clear();
            HasDirtyChanges = false;
            DirtyCount = 0;
            StatusMessage = "Settings saved successfully.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save settings: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task SaveRateLimitConfigAsync()
    {
        if (DirtyRateLimitConfig == null) return;
        await _adminApi.UpdateRateLimitConfigAsync(DirtyRateLimitConfig);
        RateLimitConfig = DirtyRateLimitConfig;
        DirtyRateLimitConfig = null;
    }

    [RelayCommand]
    private void Discard()
    {
        _dirtySettings.Clear();
        _dirtyRateLimitConfig = null;
        HasDirtyChanges = false;
        DirtyCount = 0;
        StatusMessage = null;
        ErrorMessage = null;
    }
}
