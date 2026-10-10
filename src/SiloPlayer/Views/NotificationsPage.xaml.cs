using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using SiloPlayer.Core.Api;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
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
    private static readonly JsonSerializerOptions NotificationJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public NotificationsPage()
    {
        ViewModel = App.Services.GetRequiredService<NotificationsViewModel>();
        _events = App.Services.GetService<EventChannelClient>();
        _client = App.Services.GetService<SiloApiClient>();
        DataContext = ViewModel;
        InitializeComponent();
        ((StackPanel)HeaderGrid.Children[0]).Children[0] = WebUiIcon.Create("bell", 24);
        ((StackPanel)MarkAllButton.Content).Children[0] = WebUiIcon.Create("check-check", 16);
        ((StackPanel)PreferencesButton.Content).Children[0] = WebUiIcon.Create("settings-2", 16);
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += NotificationsPage_Loaded;
        Unloaded += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            DetachRealtime();
        };
    }

    private async void NotificationsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelChanged;
        ViewModel.PropertyChanged += OnViewModelChanged;
        AttachRealtime();
        var load = ViewModel.LoadCommand.ExecuteAsync(null);
        UpdateVisuals();
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
        ViewModel.CancelPendingLoad();
        DetachRealtime();
        base.OnNavigatedFrom(e);
    }

    private async void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string filter }) return;
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

    private async void MarkRead_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AppNotification notification }) return;
        await ViewModel.MarkReadCommand.ExecuteAsync(notification);
        UpdateVisuals();
    }

    private void NotificationsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AppNotification notification) return;
        if (notification.IsUnread) _ = ViewModel.MarkReadCommand.ExecuteAsync(notification);
        var destination = NotificationNavigation.Resolve(notification);
        var navigation = App.Services.GetRequiredService<NavigationService>();
        if (destination?.ContentId is string contentId)
            navigation.Navigate<ItemDetailPage>(contentId);
        else if (destination is { MediaType: string mediaType, TmdbId: int tmdbId })
            navigation.Navigate<RequestDetailPage>(new RequestDetailNavigation(mediaType, tmdbId));
    }

    private async void Preference_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingPreferences || !IsLoaded) return;
        var preferences = ViewModel.Preferences;
        preferences.Enabled = NotificationsEnabledToggle.IsOn;
        preferences.NotifyFavorites = FavoritesToggle.IsOn;
        preferences.NotifyWatchlist = WatchlistToggle.IsOn;
        preferences.NotifyContinueWatching = ContinueWatchingToggle.IsOn;
        preferences.NotifyNextUp = NextUpToggle.IsOn;
        UpdatePreferenceEnabledState();
        await ViewModel.SavePreferencesCommand.ExecuteAsync(null);
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
        FavoritesToggle.IsOn = preferences.NotifyFavorites;
        WatchlistToggle.IsOn = preferences.NotifyWatchlist;
        ContinueWatchingToggle.IsOn = preferences.NotifyContinueWatching;
        NextUpToggle.IsOn = preferences.NotifyNextUp;
        _syncingPreferences = false;
        UpdatePreferenceEnabledState();
    }

    private void UpdatePreferenceEnabledState()
    {
        var enabled = NotificationsEnabledToggle.IsOn;
        FavoritesToggle.IsEnabled = enabled;
        WatchlistToggle.IsEnabled = enabled;
        ContinueWatchingToggle.IsEnabled = enabled;
        NextUpToggle.IsEnabled = enabled;
    }

    private void UpdateVisuals()
    {
        LoadingSkeleton.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        // Header/footer share the list's scroll surface; only rows are hidden
        // while loading, so the page header and states keep their natural flow.
        var rowSource = ViewModel.IsLoading ? null : ViewModel.Notifications;
        if (!ReferenceEquals(NotificationsList.ItemsSource, rowSource))
            NotificationsList.ItemsSource = rowSource;
        EmptyState.Visibility = !ViewModel.IsLoading && ViewModel.IsEmpty && string.IsNullOrWhiteSpace(ViewModel.ErrorMessage)
            ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitleText.Text = ViewModel.StatusFilter == "unread" ? "No unread notifications" : "No notifications yet";
        InboxStateHost.Visibility = LoadingSkeleton.Visibility == Visibility.Visible || EmptyState.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        InboxErrorPanel.Visibility = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        LoadMoreButton.Visibility = ViewModel.HasMore ? Visibility.Visible : Visibility.Collapsed;
        LoadMoreButton.IsEnabled = !ViewModel.IsLoading && !ViewModel.IsLoadingMore;
        LoadMoreBusy.IsActive = ViewModel.IsLoadingMore;
        LoadMoreBusy.Visibility = ViewModel.IsLoadingMore ? Visibility.Visible : Visibility.Collapsed;
        MarkAllButton.Visibility = ViewModel.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadButtonText.Text = ViewModel.UnreadCount > 0 ? $"Unread ({ViewModel.UnreadCount})" : "Unread";
        MarkAllButton.IsEnabled = ViewModel.CanMarkAllRead;
        ErrorText.Text = ViewModel.ErrorMessage ?? "";
        ReloadButton.Visibility = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        ReloadButton.IsEnabled = !ViewModel.IsLoading && !ViewModel.IsMarkingAllRead;
        AllButton.Style = (Style)Application.Current.Resources[ViewModel.StatusFilter == "all" ? "SecondaryButtonStyle" : "GhostButtonStyle"];
        UnreadButton.Style = (Style)Application.Current.Resources[ViewModel.StatusFilter == "unread" ? "SecondaryButtonStyle" : "GhostButtonStyle"];
    }

    private static Brush GetTabBrush(bool active)
        => active
            ? (Brush)Application.Current.Resources["SurfaceBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private void NotificationRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid row) UpdateNotificationRow(row);
    }

    private void NotificationRow_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is Grid row) UpdateNotificationRow(row);
    }

    private static void UpdateNotificationRow(Grid row)
    {
        if (row.DataContext is not AppNotification notification) return;
        AutomationProperties.SetName(row,
            string.Join(", ", new[] { notification.DisplayTitle, notification.Subtitle, notification.RelativeTime }
                .Where(value => !string.IsNullOrWhiteSpace(value))));
        if (row.FindName("NotificationTitleText") is TextBlock title)
            title.FontWeight = notification.IsUnread ? FontWeights.SemiBold : FontWeights.Medium;
        var hovered = row.Tag is true && NotificationNavigation.Resolve(notification) != null;
        if ((notification.IsUnread || hovered) && Application.Current.Resources["SurfaceBrush"] is SolidColorBrush surface)
            row.Background = new SolidColorBrush(surface.Color) { Opacity = hovered ? 0.6 : 0.3 };
        else
            row.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private void NotificationRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid hovered) { hovered.Tag = true; UpdateNotificationRow(hovered); }
        if (sender is FrameworkElement row && row.FindName("InlineMarkReadButton") is Button button)
            button.Opacity = 1;
    }

    private void NotificationRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid hovered) { hovered.Tag = false; UpdateNotificationRow(hovered); }
        if (sender is FrameworkElement row && row.FindName("InlineMarkReadButton") is Button button)
            button.Opacity = 0;
    }

    private void MarkRead_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void InlineMarkReadButton_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.Opacity = 1;
    }

    private void InlineMarkReadButton_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.Opacity = 0;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        // The WebUI combines the shared shell's 16/32px vertical gutter
        // with the inbox's own 32px padding.
        PageContent.Padding = new Thickness(16, 0, 16, 0);
        NotificationsList.Padding = new Thickness(0, WebUiViewport.Width(this, width) >= 1024 ? 64 : 48, 0, 32);
        PageContent.Width = Math.Min(width, 768);
        InboxHeader.Width = Math.Max(0, PageContent.Width - 32);

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
