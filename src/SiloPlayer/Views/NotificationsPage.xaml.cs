using SiloPlayer.Core.Localization;
using SiloPlayer.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using SiloPlayer.Core.Api;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Controls;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class NotificationsPage : Page
{
    public NotificationsViewModel ViewModel { get; }
    private bool _syncingPreferences;
    private readonly EventChannelClient? _events;
    private readonly SiloApiClient? _client;
    private IDisposable? _realtimeSubscription;
    private long _realtimeRevision;
    private bool _showSeries, _seriesAvailable;
    private CancellationTokenSource? _pageLoad;
    private ApiRequestContext? _inboxExpansionContext;
    private ScrollViewer? _scroll;
    private static readonly JsonSerializerOptions NotificationJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public NotificationsPage()
    {
        ViewModel = App.Services.GetRequiredService<NotificationsViewModel>();
        _events = App.Services.GetService<EventChannelClient>();
        _client = App.Services.GetService<SiloApiClient>();
        DataContext = ViewModel;
        InitializeComponent();
        AppLocalization.Register(this, () => { AppLocalization.ApplyDirection(this); ViewModel.RefreshLocalizedText(); UpdateVisuals(); RefreshNotificationRows(); });
        ((StackPanel)HeaderGrid.Children[0]).Children[0] = WebUiIcon.Create("bell", 24);
        ((StackPanel)MarkAllButton.Content).Children[0] = WebUiIcon.Create("check-check", 16);
        ((StackPanel)PreferencesButton.Content).Children[0] = WebUiIcon.Create("settings-2", 16);
        NavigationCacheMode = NavigationCacheMode.Required;
        SeriesView.State.PropertyChanged += (_, _) => { if (IsLoaded) UpdateSelectionBar(); };
        Loaded += NotificationsPage_Loaded;
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            DetachRealtime();
            _pageLoad?.Cancel(); SeriesView.Close(); ViewModel.CancelPendingLoad();
            if (_scroll != null) _scroll.ViewChanged -= Scroll_ViewChanged;
            _scroll = null;
        };
    }

    private async void NotificationsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
        ViewModel.PropertyChanged += OnViewModelChanged;
        AttachRealtime();
        _pageLoad?.Cancel(); _pageLoad?.Dispose(); _pageLoad = new(); var ct = _pageLoad.Token;
        var api = App.Services.GetRequiredService<NotificationsApi>(); var context = api.RequestContext;
        if (_inboxExpansionContext != context) { _expandedInbox.Clear(); _inboxExpansionContext = context; }
        _seriesAvailable = false;
        ViewModel.SetBulkMuteCapability(context, false);
        try
        {
            var capability = await api.GetCapabilityAsync(context, ct);
            _seriesAvailable = capability.Series?.Available == true;
            ViewModel.SetBulkMuteCapability(context, capability.Series?.BulkMute == true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !api.IsCurrentContext(context)) { return; }
        catch { }
        if (ct.IsCancellationRequested || !api.IsCurrentContext(context)) return;
        if (!_seriesAvailable) _showSeries = false;
        var load = _showSeries ? ViewModel.LoadPreferencesForSeriesAsync() : ViewModel.LoadCommand.ExecuteAsync(null);
        if (_showSeries) await SeriesView.OpenAsync();
        UpdateVisuals();
        _scroll = FindScroll(NotificationsList);
        if (_scroll != null) _scroll.ViewChanged += Scroll_ViewChanged;
        await load;
        SyncPreferenceControls();
        UpdatePreferenceError();
        UpdateVisuals();
    }

    private void AttachRealtime()
    {
        if (_events == null || _client == null || _realtimeSubscription != null) return;
        _realtimeRevision++;
        _events.EventReceived += OnRealtimeEvent;
        _events.SnapshotReceived += OnRealtimeSnapshot;
        _realtimeSubscription = _events.Subscribe("notifications");
    }

    private void DetachRealtime()
    {
        _realtimeRevision++;
        if (_events != null)
        {
            _events.EventReceived -= OnRealtimeEvent;
            _events.SnapshotReceived -= OnRealtimeSnapshot;
        }
        _realtimeSubscription?.Dispose();
        _realtimeSubscription = null;
    }

    private void QueueRealtime(Action apply)
    {
        if (_client == null || _realtimeSubscription == null) return;
        var context = _client.CaptureContext();
        var revision = _realtimeRevision;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || revision != _realtimeRevision || !_client.IsCurrentContext(context)) return;
            apply(); UpdateVisuals();
        });
    }

    private bool BelongsToProfile(JsonElement data)
        => data.ValueKind == JsonValueKind.Object &&
            (!data.TryGetProperty("profile_id", out var profile) || profile.ValueKind == JsonValueKind.Null ||
             profile.ValueKind == JsonValueKind.String && profile.GetString() == _client?.CaptureContext().ProfileId);

    private void OnRealtimeEvent(string channel, string name, JsonElement data)
    {
        if (channel != "notifications" || !BelongsToProfile(data)) return;
        var payload = data.Clone();
        QueueRealtime(() =>
        {
            if (!BelongsToProfile(payload)) return;
            if (name == "notification.created")
            {
                var notification = payload.Deserialize<AppNotification>(NotificationJson);
                if (notification != null) ViewModel.ApplyCreated(notification);
            }
            else if (name == "notification.read")
            {
                // Precision-bearing delivery cutoffs and all-read events need
                // an authoritative query; do not guess timestamp tuple order.
                if (payload.TryGetProperty("through_created_at", out _) ||
                    payload.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.True)
                    _ = ViewModel.ReloadAsync();
                else if (payload.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { } value)
                    ViewModel.ApplyRead(value);
            }
        });
    }

    private void OnRealtimeSnapshot(string channel, JsonElement data)
    {
        if (channel != "notifications" || data.ValueKind != JsonValueKind.Array) return;
        QueueRealtime(() => { _ = ViewModel.ReloadAsync(); });
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (e.PropertyName is nameof(NotificationsViewModel.Preferences) or nameof(NotificationsViewModel.HasLoadedPreferences))
            SyncPreferenceControls();
        UpdatePreferenceError();
        UpdateVisuals();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _pageLoad?.Cancel(); SeriesView.Close();
        ViewModel.CancelPendingLoad();
        DetachRealtime();
        base.OnNavigatedFrom(e);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string mode && mode == "series") _showSeries = true;
        base.OnNavigatedTo(e);
    }

    private async void Series_Click(object sender, RoutedEventArgs e)
    {
        if (!_seriesAvailable) return;
        PreferencesButton.Flyout.Hide(); ViewModel.SetInboxSelecting(false); _showSeries = true; UpdateVisuals();
        await SeriesView.OpenAsync(); UpdateVisuals();
    }

    private async void Scroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_showSeries && _scroll != null && _scroll.ScrollableHeight > 0 && _scroll.VerticalOffset >= _scroll.ScrollableHeight - 160)
            await SeriesView.LoadMoreAutomaticallyAsync();
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindScroll(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
        return null;
    }

    private async void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filter }) return;
        _showSeries = false; SeriesView.Close();
        var load = ViewModel.SetFilterCommand.ExecuteAsync(filter);
        UpdateVisuals();
        await load;
        UpdateVisuals();
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadMoreCommand.ExecuteAsync(null);
        UpdateVisuals();
    }

    private async void MarkAll_Click(object sender, RoutedEventArgs e)
    {
        var pending = ViewModel.MarkAllReadCommand.ExecuteAsync(null);
        UpdateVisuals();
        await pending;
        UpdateVisuals();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        var pending = ViewModel.ReloadAsync(); UpdateVisuals();
        await pending; UpdateVisuals();
    }

    private async void Preference_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingPreferences || !IsLoaded) return;
        var preferences = ViewModel.Preferences;
        preferences.Enabled = NotificationsEnabledToggle.IsOn;
        preferences.NotifyNextUp = NextUpToggle.IsOn;
        UpdatePreferenceEnabledState();
        await ViewModel.SavePreferencesCommand.ExecuteAsync(null);
        if (_showSeries) await SeriesView.PreferencesChangedAsync();
        SyncPreferenceControls();
        UpdatePreferenceError();
    }

    private void UpdatePreferenceError()
    {
        PreferenceControlsPanel.Visibility = ViewModel.HasLoadedPreferences
            ? Visibility.Visible
            : Visibility.Collapsed;
        PreferenceLoadingPanel.Visibility = ViewModel.IsLoadingPreferences && !ViewModel.HasLoadedPreferences
            ? Visibility.Visible : Visibility.Collapsed;
        PreferenceLoadFailedPanel.Visibility = ViewModel.HasLoadedPreferences || ViewModel.IsLoadingPreferences
            ? Visibility.Collapsed
            : Visibility.Visible;
        PreferenceErrorText.Text = ViewModel.PreferencesErrorMessage ?? "";
        PreferenceErrorText.Visibility = ViewModel.HasLoadedPreferences &&
            !string.IsNullOrWhiteSpace(ViewModel.PreferencesErrorMessage)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void RetryPreferences_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button retryButton) retryButton.IsEnabled = false;
        try
        {
            await ViewModel.RetryPreferencesCommand.ExecuteAsync(null);
            SyncPreferenceControls();
            UpdatePreferenceError();
        }
        finally
        {
            if (sender is Button completedButton) completedButton.IsEnabled = true;
        }
    }

    private void SyncPreferenceControls()
    {
        _syncingPreferences = true;
        var preferences = ViewModel.Preferences;
        NotificationsEnabledToggle.IsOn = preferences.Enabled;
        NextUpToggle.IsOn = preferences.NotifyNextUp;
        _syncingPreferences = false;
        UpdatePreferenceEnabledState();
    }

    private void UpdatePreferenceEnabledState()
    {
        var enabled = NotificationsEnabledToggle.IsOn;
        NextUpToggle.IsEnabled = enabled;
    }

    private void UpdateVisuals()
    {
        LoadingSkeleton.Visibility = !_showSeries && ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        InboxGroupsHost.Visibility = _showSeries || ViewModel.IsLoading ? Visibility.Collapsed : Visibility.Visible;
        InboxFilters.Visibility = _showSeries ? Visibility.Collapsed : Visibility.Visible;
        InboxButton.Style = (Style)Application.Current.Resources[!_showSeries ? "SecondaryButtonStyle" : "GhostButtonStyle"];
        InboxUnreadBadge.Text = ViewModel.UnreadCount > 0 ? AppStrings.Get("web.notifications.inbox.unreadBadge", new Dictionary<string, object?> { ["count"] = ViewModel.UnreadCount, ["count_text"] = ViewModel.UnreadCount.ToString(AppStrings.FormatCulture) }, ViewModel.UnreadCount.ToString(AppStrings.FormatCulture)) : "";
        InboxUnreadBadge.Visibility = ViewModel.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        InboxSelectButton.Visibility = ViewModel.Notifications.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        InboxSelectButton.Content = ViewModel.IsSelectingInbox ? AppStrings.Get("common.done", fallback: "Done") : AppStrings.Get("web.notifications.selection.select", fallback: "Select");
        SelectionErrorText.Text = ViewModel.SelectionErrorMessage ?? "";
        SelectionErrorText.Visibility = !_showSeries && ViewModel.SelectionErrorMessage != null ? Visibility.Visible : Visibility.Collapsed;
        if (!_showSeries) RenderInbox();
        UpdateSelectionBar();
        EmptyState.Visibility = !_showSeries && !ViewModel.IsLoading && ViewModel.IsEmpty && string.IsNullOrWhiteSpace(ViewModel.ErrorMessage)
            ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitleText.Text = ViewModel.StatusFilter == "unread" ? AppStrings.Get("web.notifications.inbox.emptyUnread", fallback: "No unread notifications") : AppStrings.Get("web.notifications.inbox.emptyAll", fallback: "No notifications yet");
        InboxStateHost.Visibility = LoadingSkeleton.Visibility == Visibility.Visible || EmptyState.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        InboxErrorPanel.Visibility = _showSeries || string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        LoadMoreButton.Visibility = !_showSeries && ViewModel.HasMore ? Visibility.Visible : Visibility.Collapsed;
        LoadMoreButton.IsEnabled = !ViewModel.IsLoading && !ViewModel.IsLoadingMore;
        LoadMoreBusy.IsActive = ViewModel.IsLoadingMore;
        LoadMoreBusy.Visibility = ViewModel.IsLoadingMore ? Visibility.Visible : Visibility.Collapsed;
        MarkAllButton.Visibility = !_showSeries && ViewModel.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadButtonText.Text = ViewModel.UnreadCount > 0 ? AppStrings.Get("web.notifications.inbox.filterUnreadCount", new Dictionary<string, object?> { ["count"] = ViewModel.UnreadCount.ToString(AppStrings.FormatCulture) }, $"Unread ({ViewModel.UnreadCount})") : AppStrings.Get("web.notifications.inbox.unread", fallback: "Unread");
        MarkAllButton.IsEnabled = ViewModel.CanMarkAllRead;
        ErrorText.Text = ViewModel.ErrorMessage ?? "";
        ReloadButton.Visibility = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        ReloadButton.IsEnabled = !ViewModel.IsLoading && !ViewModel.IsMarkingAllRead;
        AllButton.Style = (Style)Application.Current.Resources[!_showSeries && ViewModel.StatusFilter == "all" ? "SecondaryButtonStyle" : "GhostButtonStyle"];
        UnreadButton.Style = (Style)Application.Current.Resources[!_showSeries && ViewModel.StatusFilter == "unread" ? "SecondaryButtonStyle" : "GhostButtonStyle"];
        SeriesButton.Visibility = SeeSeriesButton.Visibility = _seriesAvailable ? Visibility.Visible : Visibility.Collapsed;
        SeriesButton.Style = (Style)Application.Current.Resources[_showSeries ? "SecondaryButtonStyle" : "GhostButtonStyle"];
        SeriesView.Visibility = _showSeries ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        // The WebUI combines the shared shell's 16/32px vertical gutter
        // with the inbox's own 32px padding; its 1024px column uses
        // 16px side gutters, widening to 24px from the 640px breakpoint.
        var viewport = WebUiViewport.Width(this, width);
        var gutter = viewport >= 640 ? 24 : 16;
        PageContent.Padding = new Thickness(gutter, 0, gutter, 0);
        NotificationsList.Padding = new Thickness(0, viewport >= 1024 ? 64 : 48, 0, 100);
        PageContent.Width = Math.Min(width, 1024);
        InboxHeader.Width = Math.Max(0, PageContent.Width - 2 * gutter);
        var narrowToolbar = InboxHeader.Width < 640;
        Grid.SetRow(InboxFilters, narrowToolbar ? 1 : 0);
        Grid.SetColumn(InboxFilters, narrowToolbar ? 0 : 1);
        Grid.SetColumnSpan(InboxFilters, narrowToolbar ? 2 : 1);

        if (IsLoaded && !_showSeries) RenderInbox();
        var compact = width < 480;
        HeaderGrid.RowSpacing = compact ? 12 : 0;
        if (HeaderGrid.Children[0] is FrameworkElement title)
        {
            Grid.SetRow(title, 0);
            Grid.SetColumnSpan(title, compact ? 3 : 1);
        }
        Grid.SetRow(MarkAllButton, compact ? 1 : 0);
        Grid.SetRow(PreferencesButton, compact ? 1 : 0);
        Grid.SetColumn(MarkAllButton, compact ? 1 : 1);
        Grid.SetColumn(PreferencesButton, compact ? 2 : 2);
    }
}
