using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class NotificationsPage : Page
{
    public NotificationsViewModel ViewModel { get; }
    private bool _syncingPreferences;

    public NotificationsPage()
    {
        ViewModel = App.Services.GetRequiredService<NotificationsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += NotificationsPage_Loaded;
    }

    private async void NotificationsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var load = ViewModel.LoadCommand.ExecuteAsync(null);
        UpdateVisuals();
        await load;
        SyncPreferenceControls();
        UpdateVisuals();
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
        await ViewModel.MarkAllReadCommand.ExecuteAsync(null);
        UpdateVisuals();
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
        var contentId = notification.EpisodeId ?? notification.SeriesId;
        if (!string.IsNullOrWhiteSpace(contentId))
            App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(contentId);
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
        PreferenceErrorText.Text = ViewModel.ErrorMessage ?? "";
        PreferenceErrorText.Visibility = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage)
            ? Visibility.Collapsed
            : Visibility.Visible;
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
        NotificationsList.Visibility = ViewModel.IsLoading ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = !ViewModel.IsLoading && ViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitleText.Text = ViewModel.StatusFilter == "unread" ? "No unread notifications" : "No notifications yet";
        LoadMoreButton.Visibility = ViewModel.HasMore ? Visibility.Visible : Visibility.Collapsed;
        MarkAllButton.Visibility = ViewModel.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadButtonText.Text = ViewModel.UnreadCount > 0 ? $"Unread ({ViewModel.UnreadCount})" : "Unread";
        ErrorText.Text = ViewModel.ErrorMessage ?? "";
        AllButton.Background = GetTabBrush(ViewModel.StatusFilter == "all");
        UnreadButton.Background = GetTabBrush(ViewModel.StatusFilter == "unread");
    }

    private static Brush GetTabBrush(bool active)
        => active
            ? (Brush)Application.Current.Resources["SurfaceBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
}
