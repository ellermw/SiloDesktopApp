using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminDashboardViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminDashboardViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private AdminStats? _stats;

    public ObservableCollection<AdminSession> Sessions { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];
    public ObservableCollection<AdminUser> Users { get; } = [];
    public List<AdminScanRun> ActiveScans { get; set; } = [];

    public int SessionCount => Sessions.Count;
    public bool HasCachedData => Stats is not null || Sessions.Count > 0 || Libraries.Count > 0 || Users.Count > 0;

    public string StorageDisplay
    {
        get
        {
            if (Stats is null) return "0 GB";
            var bytes = Stats.TotalStorageBytes;
            var tb = bytes / (1024.0 * 1024.0 * 1024.0 * 1024.0);
            if (tb >= 1.0)
                return $"{tb:F1} TB";
            var gb = bytes / (1024.0 * 1024.0 * 1024.0);
            return $"{(int)Math.Round(gb)} GB";
        }
    }

    partial void OnStatsChanged(AdminStats? value)
    {
        OnPropertyChanged(nameof(StorageDisplay));
    }

    [RelayCommand]
    private Task LoadAsync() => LoadInternalAsync(silent: false);

    /// <summary>
    /// Silent refresh used by the realtime event-channel subscription.
    /// Identical to LoadAsync except it does NOT flip <see cref="IsLoading"/>
    /// so the ProgressRing never flashes on incremental updates.
    /// </summary>
    public Task RefreshSilentAsync() => LoadInternalAsync(silent: true);

    public async Task RefreshSessionsOnlyAsync()
    {
        try
        {
            var sessions = await _adminApi.GetSessionsAsync();
            Sessions.Clear();
            foreach (var s in sessions)
                Sessions.Add(s);
            OnPropertyChanged(nameof(SessionCount));
        }
        catch
        {
            // Realtime updates are best-effort; keep current dashboard state.
        }
    }

    public async Task LoadStatsSectionAsync()
    {
        Stats = await _adminApi.GetStatsAsync();
        OnPropertyChanged(nameof(StorageDisplay));
    }

    public async Task LoadSessionsSectionAsync()
    {
        var sessions = await _adminApi.GetSessionsAsync();
        Sessions.Clear();
        foreach (var session in sessions) Sessions.Add(session);
        OnPropertyChanged(nameof(SessionCount));
    }

    public async Task LoadLibrariesSectionAsync()
    {
        var libraries = await _adminApi.GetAdminLibrariesAsync();
        Libraries.Clear();
        foreach (var library in libraries) Libraries.Add(library);
    }

    public async Task LoadUsersSectionAsync()
    {
        var users = await _adminApi.GetUsersAsync();
        Users.Clear();
        foreach (var user in users) Users.Add(user);
    }

    private async Task LoadInternalAsync(bool silent)
    {
        if (IsLoading) return;

        if (!silent) IsLoading = true;
        if (!silent) ErrorMessage = null;

        try
        {
            await Task.WhenAll(
                LoadStatsSectionAsync(),
                LoadSessionsSectionAsync(),
                LoadUsersSectionAsync(),
                LoadLibrariesSectionAsync());
        }
        catch (Exception ex)
        {
            if (!silent) ErrorMessage = $"Failed to load dashboard: {ex.Message}";
        }
        finally
        {
            if (!silent) IsLoading = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadInternalAsync(silent: false);

    [RelayCommand]
    private async Task ScanAllAsync()
    {
        try
        {
            await _adminApi.RunScanLibrariesTaskAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Scan failed: {ex.Message}";
        }
    }

    public async Task ScanLibraryAsync(int libraryId)
    {
        try
        {
            await _adminApi.ScanLibraryAsync(libraryId);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Scan failed: {ex.Message}";
        }
    }

    public async Task CancelLibraryScansAsync(int libraryId)
    {
        try { await _adminApi.CancelLibraryScansAsync(libraryId); }
        catch (Exception ex) { ErrorMessage = $"Cancel scan failed: {ex.Message}"; }
    }

    public static string GetTimeAgo(string dateStr)
    {
        if (!DateTime.TryParse(dateStr, out var dt)) return "";
        var diff = DateTime.UtcNow - dt.ToUniversalTime();
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }
}
