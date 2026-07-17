using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.ViewModels;

public partial class NotificationsViewModel : ObservableObject
{
    private readonly NotificationsApi _notificationsApi;

    public NotificationsViewModel(NotificationsApi notificationsApi)
    {
        _notificationsApi = notificationsApi;
    }

    public ObservableCollection<AppNotification> Notifications { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private NotificationPreferences _preferences = new();
    [ObservableProperty] private string _statusFilter = "all";
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isEmpty;
    private string? _nextCursor;
    private DateTime _lastLoadedAt = DateTime.MinValue;
    private string? _lastLoadedFilter;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    [RelayCommand]
    private async Task LoadAsync()
        => await LoadPageAsync(reset: true);

    [RelayCommand]
    private async Task LoadMoreAsync()
        => await LoadPageAsync(reset: false);

    [RelayCommand]
    private async Task SetFilterAsync(string? filter)
    {
        var normalized = filter == "unread" ? "unread" : "all";
        if (StatusFilter == normalized && Notifications.Count > 0) return;
        StatusFilter = normalized;
        await LoadPageAsync(reset: true);
    }

    private async Task LoadPageAsync(bool reset)
    {
        if (IsLoading) return;
        if (reset && Notifications.Count > 0 && _lastLoadedFilter == StatusFilter
            && DateTime.UtcNow - _lastLoadedAt < CacheDuration)
            return;

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Loading notifications...";

        try
        {
            if (reset) _nextCursor = null;
            var inboxTask = _notificationsApi.GetNotificationsAsync(StatusFilter, _nextCursor, limit: 25);
            var unreadTask = reset ? _notificationsApi.GetUnreadCountAsync() : Task.FromResult(UnreadCount);
            var prefsTask = reset ? _notificationsApi.GetPreferencesAsync() : Task.FromResult(Preferences);
            await Task.WhenAll(inboxTask, unreadTask, prefsTask);

            if (reset) Notifications.Clear();
            foreach (var notification in inboxTask.Result.Notifications)
                if (!Notifications.Any(existing => existing.Id == notification.Id)) Notifications.Add(notification);

            UnreadCount = unreadTask.Result;
            Preferences = prefsTask.Result;
            _nextCursor = inboxTask.Result.NextCursor;
            HasMore = !string.IsNullOrWhiteSpace(_nextCursor);
            IsEmpty = Notifications.Count == 0;
            StatusMessage = "";
            if (reset)
            {
                _lastLoadedAt = DateTime.UtcNow;
                _lastLoadedFilter = StatusFilter;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load notifications: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task MarkReadAsync(AppNotification? notification)
    {
        if (notification == null || !notification.IsUnread) return;

        try
        {
            var index = Notifications.IndexOf(notification);
            notification.ReadAt = DateTimeOffset.UtcNow.ToString("O");
            UnreadCount = Math.Max(0, UnreadCount - 1);
            if (StatusFilter == "unread") Notifications.Remove(notification);
            else if (index >= 0)
            {
                Notifications.RemoveAt(index);
                Notifications.Insert(index, notification);
            }
            IsEmpty = Notifications.Count == 0;
            await _notificationsApi.MarkReadAsync(notification.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to mark notification read: {ex.Message}";
            _lastLoadedAt = DateTime.MinValue;
            await LoadPageAsync(reset: true);
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        try
        {
            foreach (var notification in Notifications) notification.ReadAt = DateTimeOffset.UtcNow.ToString("O");
            UnreadCount = 0;
            if (StatusFilter == "unread") Notifications.Clear();
            else
            {
                var snapshot = Notifications.ToList();
                Notifications.Clear();
                foreach (var notification in snapshot) Notifications.Add(notification);
            }
            IsEmpty = Notifications.Count == 0;
            await _notificationsApi.MarkAllReadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to mark notifications read: {ex.Message}";
            _lastLoadedAt = DateTime.MinValue;
            await LoadPageAsync(reset: true);
        }
    }

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        try
        {
            ErrorMessage = null;
            Preferences = await _notificationsApi.UpdatePreferencesAsync(Preferences);
            StatusMessage = "Notification preferences saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save notification preferences: {ex.Message}";
        }
    }
}
