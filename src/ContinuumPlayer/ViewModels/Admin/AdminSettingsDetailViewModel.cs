using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminSettingsDetailViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private Dictionary<string, string> _settings = new();
    private readonly Dictionary<string, string> _dirtySettings = new();

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

        DirtyCount = _dirtySettings.Count;
        HasDirtyChanges = _dirtySettings.Count > 0;
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

    [RelayCommand]
    private void Discard()
    {
        _dirtySettings.Clear();
        HasDirtyChanges = false;
        DirtyCount = 0;
        StatusMessage = null;
        ErrorMessage = null;
    }
}
