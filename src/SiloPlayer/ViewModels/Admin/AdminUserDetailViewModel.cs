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
    public ObservableCollection<Library> Libraries { get; } = [];
    public ObservableCollection<AccessGroup> AccessGroups { get; } = [];

    [RelayCommand]
    private async Task LoadAsync(int userId)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var userTask     = _adminApi.GetUserAsync(userId);
            var profilesTask = _adminApi.GetUserProfilesAsync(userId);
            var historyTask  = _adminApi.GetPlaybackHistoryAsync(userId: userId, limit: 50);
            var ipsTask      = _adminApi.GetUserIPsAsync(userId, days: 30);
            var librariesTask = _adminApi.GetAdminLibrariesAsync();
            var accessGroupsTask = _adminApi.GetAccessGroupsAsync();

            await Task.WhenAll(userTask, profilesTask, historyTask, ipsTask, librariesTask, accessGroupsTask);

            User = userTask.Result;

            Profiles.Clear();
            foreach (var p in profilesTask.Result) Profiles.Add(p);

            History.Clear();
            foreach (var h in historyTask.Result) History.Add(h);

            IPs.Clear();
            foreach (var ip in ipsTask.Result) IPs.Add(ip);

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
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        return dt.ToLocalTime().ToString("g");
    }

    public static string FormatDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        return dt.ToLocalTime().ToString("MMM d, yyyy");
    }

    // B29: Delegated to SiloPlayer.Core.Helpers.TimeAgo for consistency.
    public static string FormatRelative(string? iso)
        => Core.Helpers.TimeAgo.FormatShort(iso);
}
