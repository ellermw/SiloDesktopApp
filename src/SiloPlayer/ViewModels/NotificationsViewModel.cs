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
    [ObservableProperty] private string? _preferencesErrorMessage;
    [ObservableProperty] private bool _hasLoadedPreferences;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private NotificationPreferences _preferences = new();
    [ObservableProperty] private string _statusFilter = "all";
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isMarkingAllRead;
    public bool CanMarkAllRead => !IsLoading && !IsMarkingAllRead && string.IsNullOrWhiteSpace(ErrorMessage) && _notificationsApi.CanMarkAllRead;
    public Task ReloadAsync() { _lastLoadedAt = DateTime.MinValue; return LoadPageAsync(reset: true); }
    private string? _nextCursor;
    private DateTime _lastLoadedAt = DateTime.MinValue;
    private string? _lastLoadedFilter;
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private bool _loadInProgress;
    private long _preferencesSaveVersion;
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
        if (_loadInProgress && !reset) return;
        if (reset && Notifications.Count > 0 && _lastLoadedFilter == StatusFilter
            && DateTime.UtcNow - _lastLoadedAt < CacheDuration)
            return;

        CancellationTokenSource owner;
        if (reset)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            owner = new CancellationTokenSource();
            _loadCts = owner;
        }
        else
        {
            owner = _loadCts ??= new CancellationTokenSource();
        }
        var generation = reset ? Interlocked.Increment(ref _loadGeneration) : Volatile.Read(ref _loadGeneration);
        var ct = owner.Token;
        _loadInProgress = true;
        // Keep a populated same-filter inbox mounted during a stale refresh.
        // First visits and explicit filter swaps still get the WebUI skeleton.
        IsLoading = Notifications.Count == 0 || _lastLoadedFilter != StatusFilter;
        ErrorMessage = null;
        StatusMessage = "Loading notifications...";

        try
        {
            if (reset) _nextCursor = null;
            // These requests are independent in the WebUI. The inbox is the
            // only page-critical request; an older server or a temporary
            // preferences/count failure must not replace a valid notification
            // list with the full-page error state.
            var inboxTask = _notificationsApi.GetNotificationsAsync(StatusFilter, _nextCursor, limit: 25, ct);
            var unreadTask = reset ? TryLoadUnreadCountAsync(ct) : Task.FromResult<int?>(UnreadCount);
            var prefsTask = reset
                ? TryLoadPreferencesAsync(ct)
                : Task.FromResult<(NotificationPreferences? Value, string? Error)>((Preferences, null));
            var inbox = await inboxTask;
            ct.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _loadGeneration)) return;

            if (reset) Notifications.Clear();
            foreach (var notification in inbox.Notifications)
                if (!Notifications.Any(existing => existing.Id == notification.Id)) Notifications.Add(notification);

            if (reset)
            {
                var unreadCount = await unreadTask;
                ct.ThrowIfCancellationRequested();
                if (unreadCount.HasValue)
                    UnreadCount = unreadCount.Value;

                var preferencesResult = await prefsTask;
                ct.ThrowIfCancellationRequested();
                if (preferencesResult.Value != null)
                {
                    Preferences = preferencesResult.Value;
                    PreferencesErrorMessage = null;
                    HasLoadedPreferences = true;
                }
                else
                {
                    PreferencesErrorMessage = preferencesResult.Error;
                }
            }

            _nextCursor = inbox.NextCursor;
            HasMore = !string.IsNullOrWhiteSpace(_nextCursor);
            IsEmpty = Notifications.Count == 0;
            StatusMessage = "";
            if (reset)
            {
                _lastLoadedAt = DateTime.UtcNow;
                _lastLoadedFilter = StatusFilter;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load notifications: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                IsLoading = false;
                _loadInProgress = false;
            }
        }
    }

    public void CancelPendingLoad()
    {
        Interlocked.Increment(ref _loadGeneration);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _loadInProgress = false;
        IsLoading = false;
    }

    private async Task<int?> TryLoadUnreadCountAsync(CancellationToken ct = default)
    {
        try { return await _notificationsApi.GetUnreadCountAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private async Task<(NotificationPreferences? Value, string? Error)> TryLoadPreferencesAsync(CancellationToken ct = default)
    {
        try { return (await _notificationsApi.GetPreferencesAsync(ct), null); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return (null, $"Couldn't load preferences: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task RetryPreferencesAsync()
    {
        var result = await TryLoadPreferencesAsync();
        if (result.Value != null)
        {
            Preferences = result.Value;
            PreferencesErrorMessage = null;
            HasLoadedPreferences = true;
        }
        else
        {
            PreferencesErrorMessage = result.Error;
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
            // Reconcile optimistic read/count state but retain the mutation failure.
            _lastLoadedAt = DateTime.MinValue;
            await LoadPageAsync(reset: true);
            ErrorMessage = $"Failed to mark notification read: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        if (!CanMarkAllRead) return;
        IsMarkingAllRead = true;
        try
        {
            await _notificationsApi.MarkAllReadAsync();
            // V2 marks only through the observed cutoff. New arrivals remain unread.
            _lastLoadedAt = DateTime.MinValue;
            await LoadPageAsync(reset: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to mark notifications read: {ex.Message}";
            _lastLoadedAt = DateTime.MinValue;
        }
        finally { IsMarkingAllRead = false; }
    }

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        var version = Interlocked.Increment(ref _preferencesSaveVersion);
        var requested = ClonePreferences(Preferences);
        try
        {
            PreferencesErrorMessage = null;
            var saved = await _notificationsApi.UpdatePreferencesAsync(requested);
            if (version == Volatile.Read(ref _preferencesSaveVersion))
            {
                Preferences = saved;
                StatusMessage = "Notification preferences saved.";
            }
        }
        catch (Exception ex)
        {
            if (version != Volatile.Read(ref _preferencesSaveVersion)) return;

            PreferencesErrorMessage = $"Failed to save notification preferences: {ex.Message}";
            try
            {
                // A previous overlapping mutation may already have reached the
                // server. Re-read the authoritative value rather than guessing
                // which optimistic switch state should be rolled back.
                Preferences = await _notificationsApi.GetPreferencesAsync();
            }
            catch
            {
                // Keep the current controls visible and retain the actionable
                // failure message. A later page refresh will retry the query.
            }
        }
    }

    private static NotificationPreferences ClonePreferences(NotificationPreferences source) => new()
    {
        ProfileId = source.ProfileId,
        Enabled = source.Enabled,
        NotifyFavorites = source.NotifyFavorites,
        NotifyWatchlist = source.NotifyWatchlist,
        NotifyContinueWatching = source.NotifyContinueWatching,
        NotifyNextUp = source.NotifyNextUp,
    };
}
