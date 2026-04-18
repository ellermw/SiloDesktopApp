using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.ViewModels;
using Windows.System;

namespace ContinuumPlayer.Views;

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

    public WatchTogetherJoinPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchTogetherJoinViewModel>();
        this.InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateSelectionButtons();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Deep-link: invite token auto-joins
        if (e.Parameter is string token && !string.IsNullOrWhiteSpace(token))
        {
            HeadlineText.Text = "Joining Watch Party";
            await ViewModel.JoinByInviteTokenAsync(token.Trim());
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
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
        if (e.Key == VirtualKey.Enter && !ViewModel.IsBusy)
        {
            await ViewModel.JoinByCodeCommand.ExecuteAsync(null);
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
