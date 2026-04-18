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
    private HashSet<string> _managedByEnv = new();

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
            try
            {
                var (configured, managed) = await _adminApi.GetSensitiveStatusAsync();
                _sensitiveConfigured = configured;
                _managedByEnv = managed;
            }
            catch { _sensitiveConfigured = new(); _managedByEnv = new(); }
            try { RateLimitConfig = await _adminApi.GetRateLimitConfigAsync(); }
            catch
            {
                // Route may be disabled on servers without rate-limit middleware.
                // Fall back to the webui DEFAULT_CONFIG so the tab still renders
                // and the admin can at least edit + try to save.
                RateLimitConfig = BuildDefaultRateLimitConfig();
            }
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
    public bool IsManagedByEnv(string key) => _managedByEnv.Contains(key);

    /// <summary>
    /// Merged view of persisted settings overlaid with any unsaved edits.
    /// Used by the connection-check endpoint so admins can test against
    /// whatever they are about to save, not just what is on disk.
    /// </summary>
    public Dictionary<string, string> GetEffectiveSettings()
    {
        var merged = new Dictionary<string, string>(_settings);
        foreach (var (k, v) in _dirtySettings)
            merged[k] = v;
        return merged;
    }

    /// <summary>Keys the user has edited since the last load/save.</summary>
    public List<string> GetDirtyKeys() => [.. _dirtySettings.Keys];

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

    /// <summary>
    /// Webui DEFAULT_CONFIG equivalent — used when the server rate-limit
    /// endpoint is unavailable (server route not wired, or error), so the
    /// Rate Limiting tab still renders with sensible starting values.
    /// </summary>
    private static RateLimitConfig BuildDefaultRateLimitConfig() => new()
    {
        Enabled = false,
        Backend = "memory",
        GlobalRequestsPerSecond = 1000,
        IpRequestsPerSecond = 120,
        IpRequestsPerMinute = 6000,
        IpBurst = 120,
        Tiers = new Dictionary<string, RateLimitTierConfig>
        {
            ["standard"] = new() { RequestsPerSecond = 20,  RequestsPerMinute = 1200, Burst = 20 },
            ["elevated"] = new() { RequestsPerSecond = 100, RequestsPerMinute = 6000, Burst = 100 },
        },
        AuthEndpoints = new Dictionary<string, RateLimitAuthEndpointConfig>
        {
            ["login"]  = new() { RequestsPerMinute = 20, Burst = 10 },
            ["signup"] = new() { RequestsPerMinute = 10, Burst = 6  },
            ["setup"]  = new() { RequestsPerMinute = 10, Burst = 6  },
        },
    };

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
