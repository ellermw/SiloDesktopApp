using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.ViewModels;
using Windows.System;

namespace SiloPlayer.Views;

/// <summary>
/// Watch Party lobby — create a new room or join an existing one via code / invite token.
/// Shadow of continuum-server/web/src/pages/WatchTogetherJoin.tsx.
///
/// Navigation parameters:
///   null         → blank create-or-join form
///   string token → auto-join using the given invite token (from deep-link)
///
/// On successful create/join, navigates to <see cref="WatchTogetherRoomPage"/> passing a
/// <see cref="WatchTogetherRoomNavigationArgs"/> with (roomId, roomAccessToken).
/// </summary>
public sealed partial class WatchTogetherJoinPage : Page
{
    public WatchTogetherJoinViewModel ViewModel { get; }
    private bool _subscribed;

    public WatchTogetherJoinPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchTogetherJoinViewModel>();
        this.InitializeComponent();
        SubscribeToViewModel();
        UpdateSelectionButtons();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SubscribeToViewModel();

        // Deep-link: invite token auto-joins
        if (e.Parameter is string token && !string.IsNullOrWhiteSpace(token))
        {
            HeadlineText.Text = "Joining Watch Party";
            AutoJoinPanel.Visibility = Visibility.Visible;
            ContentStack.Visibility = Visibility.Collapsed;
            await ViewModel.JoinByInviteTokenAsync(token.Trim());
            if (ViewModel.LastResponse == null)
            {
                AutoJoinPanel.Visibility = Visibility.Collapsed;
                ContentStack.Visibility = Visibility.Visible;
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_subscribed)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _subscribed = false;
        }
    }

    private void SubscribeToViewModel()
    {
        if (_subscribed) return;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _subscribed = true;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.LastResponse) && ViewModel.LastResponse != null)
        {
            var resp = ViewModel.LastResponse;
            ViewModel.LastResponse = null; // clear so back/forward doesn't retrigger
            if (string.IsNullOrEmpty(resp.RoomAccessToken)) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                Frame.Navigate(typeof(WatchTogetherRoomPage), new WatchTogetherRoomNavigationArgs
                {
                    RoomId = resp.Room.RoomId,
                    RoomAccessToken = resp.RoomAccessToken,
                });
            });
        }
        else if (e.PropertyName == nameof(ViewModel.SelectionMode))
        {
            DispatcherQueue.TryEnqueue(UpdateSelectionButtons);
        }
    }

    private void HostPickButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectionMode = "host_pick";
    }

    private void VoteButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectionMode = "vote";
    }

    private void SelectionButton_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Right or VirtualKey.Down or VirtualKey.End)
        {
            ViewModel.SelectionMode = "vote";
            VoteButton.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Left or VirtualKey.Up or VirtualKey.Home)
        {
            ViewModel.SelectionMode = "host_pick";
            HostPickButton.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }

    private void UpdateSelectionButtons()
    {
        // Style swaps in WinUI 3 are expensive — the framework invalidates
        // the ControlTemplate and rebuilds the visual tree on every swap,
        // which freezes the UI thread for 1-3 seconds on complex content.
        // Flip the individual properties directly instead.
        bool hostPick = ViewModel.SelectionMode == "host_pick";
        ApplySelectionLook(HostPickButton, hostPick);
        ApplySelectionLook(VoteButton, !hostPick);
    }

    private static void ApplySelectionLook(Button btn, bool active)
    {
        if (active)
        {
            btn.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
            btn.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"];
            btn.BorderThickness = new Thickness(0);
        }
        else
        {
            btn.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            btn.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
            btn.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"];
            btn.BorderThickness = new Thickness(1);
        }
    }

    private async void RoomCodeBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.CanJoin)
        {
            await ViewModel.JoinByCodeCommand.ExecuteAsync(null);
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        var compact = width < 700;
        var narrow = width < 900;
        PageShell.Padding = new Thickness(width < 640 ? 16 : width < 1024 ? 32 : 40, width < 640 ? 24 : 40, width < 640 ? 16 : width < 1024 ? 32 : 40, 40);
        HeadlineText.FontSize = width < 640 ? 30 : 36;

        for (var index = 0; index < InfoGrid.Children.Count; index++)
        {
            if (InfoGrid.Children[index] is not FrameworkElement child) continue;
            Grid.SetColumn(child, compact ? 0 : index);
            Grid.SetRow(child, compact ? index : 0);
        }

        for (var index = 0; index < ActionGrid.Children.Count; index++)
        {
            if (ActionGrid.Children[index] is not FrameworkElement child) continue;
            Grid.SetColumn(child, narrow ? 0 : index);
            Grid.SetRow(child, narrow ? index : 0);
        }
    }
}

/// <summary>
/// Navigation parameter bundle for <see cref="WatchTogetherRoomPage"/>. Defined here
/// because this is the page that produces it.
/// </summary>
public sealed class WatchTogetherRoomNavigationArgs
{
    public string RoomId { get; set; } = "";
    public string RoomAccessToken { get; set; } = "";
}
