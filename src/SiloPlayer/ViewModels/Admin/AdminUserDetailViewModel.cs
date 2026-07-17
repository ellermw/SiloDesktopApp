using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminUserDetailViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminUserDetailViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private AdminUser? _user;

    public ObservableCollection<AdminUserProfile> Profiles { get; } = [];
    public ObservableCollection<AdminPlaybackHistoryItem> History { get; } = [];
    public ObservableCollection<UserIPEntry> IPs { get; } = [];
    public ObservableCollection<AdminUserSetting> UserSettings { get; } = [];
    public ObservableCollection<AdminDeviceSetting> DeviceSettings { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];
    public ObservableCollection<AccessGroup> AccessGroups { get; } = [];
    private int _loadedUserId;
    private bool _profilesLoaded;
    private bool _historyLoaded;
    private bool _ipsLoaded;
    private bool _userSettingsLoaded;
    private bool _deviceSettingsLoaded;

    [RelayCommand]
    private async Task LoadAsync(int userId)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            if (_loadedUserId != userId)
            {
                _loadedUserId = userId;
                _profilesLoaded = _historyLoaded = _ipsLoaded = false;
                _userSettingsLoaded = _deviceSettingsLoaded = false;
                Profiles.Clear();
                History.Clear();
                IPs.Clear();
                UserSettings.Clear();
                DeviceSettings.Clear();
            }
            var userTask     = _adminApi.GetUserAsync(userId);
            var librariesTask = _adminApi.GetAdminLibrariesAsync();
            var accessGroupsTask = _adminApi.GetAccessGroupsAsync();

            await Task.WhenAll(userTask, librariesTask, accessGroupsTask);

            User = userTask.Result;

            Libraries.Clear();
            foreach (var l in librariesTask.Result) Libraries.Add(l);

            AccessGroups.Clear();
            foreach (var group in accessGroupsTask.Result.OrderByDescending(group => group.IsDefault)
                         .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase))
                AccessGroups.Add(group);
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

    public async Task LoadProfilesAsync(int userId, bool force = false)
    {
        if (_profilesLoaded && !force) return;
        var items = await _adminApi.GetUserProfilesAsync(userId);
        Profiles.Clear();
        foreach (var item in items) Profiles.Add(item);
        _profilesLoaded = true;
    }

    public async Task LoadHistoryAsync(int userId, bool force = false)
    {
        if (_historyLoaded && !force) return;
        var items = await _adminApi.GetPlaybackHistoryAsync(userId: userId, limit: 50);
        History.Clear();
        foreach (var item in items) History.Add(item);
        _historyLoaded = true;
    }

    public async Task LoadIPsAsync(int userId, bool force = false)
    {
        if (_ipsLoaded && !force) return;
        var items = await _adminApi.GetUserIPsAsync(userId, days: 30);
        IPs.Clear();
        foreach (var item in items) IPs.Add(item);
        _ipsLoaded = true;
    }

    public async Task LoadUserSettingsAsync(int userId, bool force = false)
    {
        if (_userSettingsLoaded && !force) return;
        var items = await _adminApi.GetUserSettingsAsync(userId);
        UserSettings.Clear();
        foreach (var item in items) UserSettings.Add(item);
        _userSettingsLoaded = true;
    }

    public async Task LoadDeviceSettingsAsync(int userId, bool force = false)
    {
        if (_deviceSettingsLoaded && !force) return;
        var items = await _adminApi.GetUserDeviceSettingsAsync(userId);
        DeviceSettings.Clear();
        foreach (var item in items) DeviceSettings.Add(item);
        _deviceSettingsLoaded = true;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (User == null) return;
        await _adminApi.DeleteUserAsync(User.Id);
    }

    public async Task UpdateUserAsync(int id, UpdateUserRequest request)
    {
        var updated = await _adminApi.UpdateUserAsync(id, request);
        User = updated;
    }

    public async Task SaveUserSettingAsync(int userId, AdminUserSetting setting, string value)
    {
        await _adminApi.UpdateUserSettingAsync(userId, setting.Key, value);
        setting.Value = value;
    }

    public async Task ResetUserSettingAsync(int userId, AdminUserSetting setting)
    {
        await _adminApi.DeleteUserSettingAsync(userId, setting.Key);
        UserSettings.Remove(setting);
    }

    public async Task SaveDeviceSettingAsync(int userId, AdminDeviceSetting setting, string value)
    {
        await _adminApi.UpdateDeviceSettingAsync(userId, setting.ProfileId, setting.DeviceId, setting.Key, value);
        setting.Value = value;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task ResetDeviceSettingAsync(int userId, AdminDeviceSetting setting)
    {
        await _adminApi.DeleteDeviceSettingAsync(userId, setting.ProfileId, setting.DeviceId, setting.Key);
        DeviceSettings.Remove(setting);
    }

    public async Task ResetDeviceProfileAsync(int userId, string profileId, string deviceId)
    {
        await _adminApi.DeleteAllDeviceSettingsAsync(userId, profileId, deviceId);
        foreach (var setting in DeviceSettings.Where(setting => setting.ProfileId == profileId && setting.DeviceId == deviceId).ToList())
            DeviceSettings.Remove(setting);
    }

    // ===== Formatting helpers =====

    public static string FormatWatchTime(double seconds)
    {
        int total = Math.Max(0, (int)Math.Floor(seconds));
        int hours   = total / 3600;
        int minutes = (total % 3600) / 60;
        int secs    = total % 60;

        if (hours > 0)   return $"{hours}h {minutes}m";
        if (minutes > 0) return $"{minutes}m {secs}s";
        return $"{secs}s";
    }

    public static string FormatDateTime(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (!DateTimeOffset.TryParse(iso, out var dt)) return iso;
        return SiloPlayer.Helpers.DateTimeDisplay.FormatDateTime(dt);
    }

    public static string FormatDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (!DateTimeOffset.TryParse(iso, out var dt)) return iso;
        return SiloPlayer.Helpers.DateTimeDisplay.FormatDate(dt, medium: true);
    }

    // B29: Delegated to SiloPlayer.Core.Helpers.TimeAgo for consistency.
    public static string FormatRelative(string? iso)
        => Core.Helpers.TimeAgo.FormatShort(iso);
}
