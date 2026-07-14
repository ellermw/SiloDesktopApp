using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminPlaybackHistoryViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private CancellationTokenSource? _loadCts;

    public AdminPlaybackHistoryViewModel(AdminApi adminApi) { _adminApi = adminApi; }

    public ObservableCollection<AdminPlaybackHistoryItem> Items { get; } = [];
    public ObservableCollection<AdminUser> Users { get; } = [];
    public ObservableCollection<AdminUserProfile> Profiles { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private int? _selectedUserId;
    [ObservableProperty] private string? _selectedProfileId;
    [ObservableProperty] private string? _mediaItemId;
    [ObservableProperty] private string _activeMediaItemLabel = "";
    [ObservableProperty] private string? _completionFilter; // null/"all", "true", "false"
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _partialCount;

    partial void OnSelectedUserIdChanged(int? value)
    {
        // When user changes, clear profile selection
        SelectedProfileId = null;
        Profiles.Clear();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, owner);
        previous?.Cancel();
        previous?.Dispose();
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            // Load users if not loaded yet
            if (Users.Count == 0)
            {
                var users = await _adminApi.GetUsersAsync(owner.Token);
                owner.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_loadCts, owner)) return;
                Users.Clear();
                foreach (var u in users) Users.Add(u);
            }

            // Profiles are loaded reactively via OnSelectedUserIdChanged;
            // only back-fill here if we have a user but profiles haven't arrived yet.
            if (SelectedUserId.HasValue && Profiles.Count == 0)
                await LoadProfilesAsync(SelectedUserId.Value, owner.Token);

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
                mediaItemId: string.IsNullOrWhiteSpace(MediaItemId) ? null : MediaItemId.Trim(),
                completed: completed,
                limit: 100,
                ct: owner.Token);

            owner.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, owner)) return;

            Items.Clear();
            foreach (var item in items) Items.Add(item);

            ActiveMediaItemLabel = Items.FirstOrDefault()?.MediaTitle ?? MediaItemId ?? "";

            TotalCount = Items.Count;
            CompletedCount = Items.Count(i => i.Completed);
            PartialCount = Items.Count(i => !i.Completed);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadCts, owner)) ErrorMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, owner), owner))
                IsLoading = false;
            owner.Dispose();
        }
    }

    private async Task LoadProfilesAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            var profiles = await _adminApi.GetUserProfilesAsync(userId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (SelectedUserId != userId) return;
            Profiles.Clear();
            foreach (var p in profiles) Profiles.Add(p);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { /* non-fatal */ }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedUserId = null;
        SelectedProfileId = null;
        CompletionFilter = null;
        MediaItemId = null;
        ActiveMediaItemLabel = "";
        Profiles.Clear();
    }

    public bool HasActiveFilters =>
        SelectedUserId.HasValue ||
        !string.IsNullOrEmpty(SelectedProfileId) ||
        !string.IsNullOrWhiteSpace(MediaItemId) ||
        (!string.IsNullOrEmpty(CompletionFilter) && CompletionFilter != "all");

    public void Cancel()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        cts?.Cancel();
        cts?.Dispose();
        IsLoading = false;
    }

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
        if (!DateTimeOffset.TryParse(dateStr, out var value)) return dateStr;
        return $"{DateTimeDisplay.FormatDate(value)}, {DateTimeDisplay.FormatTime(value, seconds: true)}";
    }

    // B29: Delegated to SiloPlayer.Core.Helpers.TimeAgo for consistency
    // across all admin pages.
    public static string FormatRelative(string dateStr)
        => Core.Helpers.TimeAgo.FormatShort(dateStr);
}
