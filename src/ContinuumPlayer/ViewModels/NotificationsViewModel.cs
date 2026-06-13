using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Notifications;

namespace ContinuumPlayer.ViewModels;

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

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Loading notifications...";

        try
        {
            var inboxTask = _notificationsApi.GetNotificationsAsync(limit: 50);
            var unreadTask = _notificationsApi.GetUnreadCountAsync();
            var prefsTask = _notificationsApi.GetPreferencesAsync();
            await Task.WhenAll(inboxTask, unreadTask, prefsTask);

            Notifications.Clear();
            foreach (var notification in inboxTask.Result.Notifications)
                Notifications.Add(notification);

            UnreadCount = unreadTask.Result;
            Preferences = prefsTask.Result;
            StatusMessage = UnreadCount == 1
                ? "1 unread notification."
                : $"{UnreadCount:N0} unread notifications.";
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
            await _notificationsApi.MarkReadAsync(notification.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to mark notification read: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        try
        {
            await _notificationsApi.MarkAllReadAsync();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to mark notifications read: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        try
        {
            Preferences = await _notificationsApi.UpdatePreferencesAsync(Preferences);
            StatusMessage = "Notification preferences saved.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save notification preferences: {ex.Message}";
        }
    }
}
