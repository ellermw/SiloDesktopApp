using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

public partial class AdminPlaybackHistoryViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminPlaybackHistoryViewModel(AdminApi adminApi) { _adminApi = adminApi; }

    public ObservableCollection<AdminPlaybackHistoryItem> Items { get; } = [];
    public ObservableCollection<AdminUser> Users { get; } = [];
    public ObservableCollection<AdminUserProfile> Profiles { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private int? _selectedUserId;
    [ObservableProperty] private string? _selectedProfileId;
    [ObservableProperty] private string? _completionFilter; // null/"all", "true", "false"
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _partialCount;

    partial void OnSelectedUserIdChanged(int? value)
    {
        // When user changes, clear profile selection
        SelectedProfileId = null;
        Profiles.Clear();
        // Async load profiles for the new user
        if (value.HasValue)
            _ = LoadProfilesAsync(value.Value);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            // Load users if not loaded yet
            if (Users.Count == 0)
            {
                var users = await _adminApi.GetUsersAsync();
                Users.Clear();
                foreach (var u in users) Users.Add(u);
            }

            // If a user is selected, load their profiles
            if (SelectedUserId.HasValue && Profiles.Count == 0)
                await LoadProfilesAsync(SelectedUserId.Value);

            // Load history with current filters
            bool? completed = CompletionFilter switch
            {
                "true" => true,
                "false" => false,
                _ => null
            };

            var items = await _adminApi.GetPlaybackHistoryAsync(
                userId: SelectedUserId,
                profileId: SelectedProfileId,
                completed: completed,
                limit: 100);

            Items.Clear();
            foreach (var item in items) Items.Add(item);

            TotalCount = Items.Count;
            CompletedCount = Items.Count(i => i.Completed);
            PartialCount = Items.Count(i => !i.Completed);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    private async Task LoadProfilesAsync(int userId)
    {
        try
        {
            var profiles = await _adminApi.GetUserProfilesAsync(userId);
            Profiles.Clear();
            foreach (var p in profiles) Profiles.Add(p);
        }
        catch { /* non-fatal */ }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedUserId = null;
        SelectedProfileId = null;
        CompletionFilter = null;
        Profiles.Clear();
    }

    public bool HasActiveFilters =>
        SelectedUserId.HasValue ||
        !string.IsNullOrEmpty(SelectedProfileId) ||
        (!string.IsNullOrEmpty(CompletionFilter) && CompletionFilter != "all");

    // ===== Static helpers =====

    public static string FormatWatchTime(double watchedSeconds, double? durationSeconds)
    {
        var watched = TimeSpan.FromSeconds(watchedSeconds);
        var watchedStr = watched.TotalHours >= 1
            ? $"{(int)watched.TotalHours}h {watched.Minutes}m"
            : $"{(int)watched.TotalMinutes}m";
        if (durationSeconds.HasValue && durationSeconds.Value > 0)
        {
            var total = TimeSpan.FromSeconds(durationSeconds.Value);
            var totalStr = total.TotalHours >= 1
                ? $"{(int)total.TotalHours}h {total.Minutes}m"
                : $"{(int)total.TotalMinutes}m";
            return $"{watchedStr} / {totalStr}";
        }
        return watchedStr;
    }

    public static string FormatDateTime(string dateStr)
    {
        if (!DateTime.TryParse(dateStr, out var dt)) return dateStr;
        return dt.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
    }

    public static string FormatRelative(string dateStr)
    {
        if (!DateTime.TryParse(dateStr, out var dt)) return dateStr;
        var diff = DateTime.UtcNow - dt.ToUniversalTime();
        if (diff.TotalSeconds < 60) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }
}
