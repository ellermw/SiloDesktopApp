using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace ContinuumPlayer.Views;

/// <summary>
/// Watch Party room page — shows room code, participant count, host controls, and the
/// suggestion list in vote mode. Shadow of WatchTogetherRoomPage.tsx.
///
/// Scope for this pass: surface room state, let host toggle policy / close room, let
/// voters vote on suggestions. Content search and synced playback with PlayerService
/// are TODOs.
/// </summary>
public sealed partial class WatchTogetherRoomPage : Page
{
    public WatchTogetherRoomViewModel ViewModel { get; }
    private bool _subscribed;

    public WatchTogetherRoomPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchTogetherRoomViewModel>();
        this.InitializeComponent();
        SuggestionsRepeater.ItemsSource = ViewModel.Suggestions;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not WatchTogetherRoomNavigationArgs args
            || string.IsNullOrEmpty(args.RoomId)
            || string.IsNullOrEmpty(args.RoomAccessToken))
        {
            ViewModel.ErrorMessage = "Room token is required.";
            return;
        }

        if (!_subscribed)
        {
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.Suggestions.CollectionChanged += Suggestions_CollectionChanged;
            _subscribed = true;
        }

        await ViewModel.InitializeAsync(args.RoomId, args.RoomAccessToken);
        UpdateRoomUi();
        UpdateConnectionUi();
        UpdateSuggestionsUi();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_subscribed)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.Suggestions.CollectionChanged -= Suggestions_CollectionChanged;
            _subscribed = false;
        }
        ViewModel.Dispose();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModel.Room):
                    UpdateRoomUi();
                    break;
                case nameof(ViewModel.ConnectionState):
                    UpdateConnectionUi();
                    break;
                case nameof(ViewModel.ClosedReason):
                    UpdateClosedReasonUi();
                    break;
            }
        });
    }

    private void Suggestions_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateSuggestionsUi);
    }

    private void UpdateRoomUi()
    {
        var room = ViewModel.Room;

        CodeText.Text = ViewModel.DisplayCode;
        MemberCountText.Text = ViewModel.MemberCount.ToString();

        bool hasRoom = room != null && ViewModel.ClosedReason == null;
        bool isHost = ViewModel.IsHost;
        bool isVoteMode = ViewModel.IsVoteMode;
        bool isPlaying = ViewModel.IsPlaying;
        bool hasInvite = hasRoom && !string.IsNullOrEmpty(room!.InvitePath);

        CopyInviteButton.Visibility = hasInvite ? Visibility.Visible : Visibility.Collapsed;
        PolicyButton.Visibility = isHost ? Visibility.Visible : Visibility.Collapsed;
        EndButton.Visibility = isHost ? Visibility.Visible : Visibility.Collapsed;

        if (isHost && room != null)
        {
            PolicyButtonText.Text = room.GuestControlPolicy == "guest_play_pause"
                ? "Host Only" : "Allow Pause";
        }

        // Now Playing
        if (isPlaying && !string.IsNullOrEmpty(room?.SelectedContentId))
        {
            NowPlayingPanel.Visibility = Visibility.Visible;
            NowPlayingTitle.Text = "Playing (content details loading on the desktop player is a TODO)";
            NowPlayingSubtitle.Text = $"content_id: {room!.SelectedContentId}";
        }
        else
        {
            NowPlayingPanel.Visibility = Visibility.Collapsed;
        }

        // Lobby: host panel vs guest waiting panel
        if (hasRoom && !isPlaying)
        {
            if (isHost)
            {
                HostPanel.Visibility = Visibility.Visible;
                WaitingPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                HostPanel.Visibility = Visibility.Collapsed;
                WaitingPanel.Visibility = Visibility.Visible;
                WaitingSubtitle.Text = isVoteMode
                    ? "The room is voting on what to watch next."
                    : "The host will choose a movie or episode for the room.";
            }
        }
        else
        {
            HostPanel.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Collapsed;
        }

        // Suggestions visible in vote mode only
        SuggestionsPanel.Visibility = hasRoom && isVoteMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateSuggestionsUi();

        // Auto-start stub — see VM comment. In this pass we just acknowledge so the flag
        // doesn't fire repeatedly; wiring to PlayerService is a follow-up.
        if (ViewModel.ShouldAutoStartPlayback)
        {
            ViewModel.AcknowledgePlaybackStart();
            // TODO: PlayerService.PlayAsync(room.SelectedContentId, room.SelectedFileId, ...) with room sync.
        }
    }

    private void UpdateConnectionUi()
    {
        switch (ViewModel.ConnectionState)
        {
            case "connected":
                StatusDot.Fill = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
                StatusText.Text = "Connected";
                break;
            case "connecting":
                StatusDot.Fill = new SolidColorBrush(Microsoft.UI.Colors.Gold);
                StatusText.Text = "Connecting...";
                break;
            default:
                StatusDot.Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                StatusText.Text = "Disconnected";
                break;
        }
    }

    private void UpdateClosedReasonUi()
    {
        var reason = ViewModel.ClosedReason;
        if (string.IsNullOrEmpty(reason)) return;
        ClosedReasonText.Text = reason switch
        {
            "host_left" or "room_closed" => "The room has ended.",
            "not_found" => "Room not found.",
            _ => "The room is unavailable.",
        };
    }

    private void UpdateSuggestionsUi()
    {
        int n = ViewModel.Suggestions.Count;
        SuggestionsSubtitle.Text = n == 0
            ? "No suggestions yet."
            : $"{n} suggestion{(n == 1 ? "" : "s")} from the room";
    }

    private async void CopyInviteButton_Click(object sender, RoutedEventArgs e)
    {
        var room = ViewModel.Room;
        if (room == null || string.IsNullOrEmpty(room.InvitePath)) return;

        // The invite_path is a relative URL; the web UI resolves it against
        // window.location.origin. On desktop we don't have a canonical origin for the
        // web UI — so copy the room code as a fallback and the path as a hint.
        try
        {
            var pkg = new DataPackage();
            pkg.SetText(room.Code + "  " + room.InvitePath);
            Clipboard.SetContent(pkg);
        }
        catch { }
        await Task.CompletedTask;
    }

    private async void SuggestionVoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string id) return;

        // Find the suggestion to determine toggle direction
        WatchTogetherSuggestion? target = null;
        foreach (var s in ViewModel.Suggestions)
        {
            if (s.Id == id) { target = s; break; }
        }
        if (target == null) return;

        if (target.VotedByMe) await ViewModel.UnvoteAsync(id);
        else await ViewModel.VoteAsync(id);
    }
}
