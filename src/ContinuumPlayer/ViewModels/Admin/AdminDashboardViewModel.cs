using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.ViewModels.Admin;

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

    public int SessionCount => Sessions.Count;

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

    private async Task LoadInternalAsync(bool silent)
    {
        if (IsLoading) return;

        if (!silent) IsLoading = true;
        if (!silent) ErrorMessage = null;

        try
        {
            var statsTask = _adminApi.GetStatsAsync();
            var sessionsTask = _adminApi.GetSessionsAsync();
            var usersTask = _adminApi.GetUsersAsync();
            var librariesTask = _adminApi.GetAdminLibrariesAsync();

            await Task.WhenAll(statsTask, sessionsTask, usersTask, librariesTask);

            Stats = statsTask.Result;

            Sessions.Clear();
            foreach (var s in sessionsTask.Result)
                Sessions.Add(s);

            Users.Clear();
            foreach (var u in usersTask.Result)
                Users.Add(u);

            Libraries.Clear();
            foreach (var l in librariesTask.Result)
                Libraries.Add(l);

            OnPropertyChanged(nameof(SessionCount));
            OnPropertyChanged(nameof(StorageDisplay));
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
