using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Services;
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

        // Register with the playback-sync coordinator so inbound transport
        // commands reach mpv and local playback sessions get attached.
        var coordinator = App.Services.GetRequiredService<WatchTogetherCoordinator>();
        coordinator.PlaybackStartRequested -= OnCoordinatorStartRequested;
        coordinator.PlaybackStartRequested += OnCoordinatorStartRequested;
        coordinator.SetActiveRoom(ViewModel);
    }

    /// <summary>
    /// Raised by the coordinator when the room transitions to phase=playing on
    /// a new selection_revision. Kicks off local playback so all members share
    /// the experience without the host having to manually navigate each guest.
    /// </summary>
    private void OnCoordinatorStartRequested(string contentId)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var player = App.Services.GetRequiredService<ContinuumPlayer.Services.PlayerService>();
                await player.PlayAsync(contentId);
            }
            catch { }
        });
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
        try
        {
            var coordinator = App.Services.GetRequiredService<WatchTogetherCoordinator>();
            coordinator.PlaybackStartRequested -= OnCoordinatorStartRequested;
            coordinator.ClearActiveRoom();
        }
        catch { }
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

        // Host content-search section — only for hosts during lobby (not
        // already playing). Hidden for guests and vote-mode rooms.
        HostPickSection.Visibility = (isHost && hasRoom && !isPlaying && !isVoteMode)
            ? Visibility.Visible : Visibility.Collapsed;

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

        // Auto-start: when the room has selected content and we haven't started yet,
        // launch playback via PlayerService.
        if (ViewModel.ShouldAutoStartPlayback)
        {
            ViewModel.AcknowledgePlaybackStart();
            var selectedContentId = ViewModel.Room?.SelectedContentId;
            if (!string.IsNullOrEmpty(selectedContentId))
            {
                try
                {
                    var player = App.Services.GetRequiredService<Services.PlayerService>();
                    _ = player.PlayAsync(selectedContentId);
                }
                catch { }
            }
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

    // ── Host content search ──────────────────────────────────────────────

    private async void HostSearchBtn_Click(object sender, RoutedEventArgs e) => await RunHostSearchAsync();

    private async void HostSearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) await RunHostSearchAsync();
    }

    private async Task RunHostSearchAsync()
    {
        var query = HostSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            HostSearchResults.ItemsSource = null;
            return;
        }
        try
        {
            var catalogApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.CatalogApi>();
            var result = await catalogApi.SearchAsync(query, limit: 20);
            HostSearchResults.ItemsSource = result?.Items;
        }
        catch
        {
            // Keep the UI usable on transient errors — empty list is fine.
            HostSearchResults.ItemsSource = null;
        }
    }

    private async void HostSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string contentId) return;
        if (string.IsNullOrEmpty(ViewModel.RoomId)) return;

        try
        {
            var playbackApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.PlaybackApi>();
            var resp = await playbackApi.SelectWatchTogetherRoomItemAsync(
                ViewModel.RoomId!, contentId);
            ViewModel.Room = resp.Room;
            HostSearchResults.ItemsSource = null;
            HostSearchBox.Text = "";
        }
        catch { }
    }

    // ===== Series Drill-Down =====

    private string? _drillDownSeriesId;

    /// <summary>
    /// Called when a search result is selected. Movies go directly to candidate spotlight.
    /// Series drill down into seasons → episodes.
    /// </summary>
    public async void OnSearchResultSelected(ContinuumPlayer.Core.Models.Home.MediaItem item)
    {
        if (item.Type == "movie")
        {
            ShowCandidateSpotlight(item);
            return;
        }

        if (item.Type == "series")
        {
            _drillDownSeriesId = item.ContentId;
            await ShowSeasonsAsync(item);
        }
    }

    private async Task ShowSeasonsAsync(ContinuumPlayer.Core.Models.Home.MediaItem series)
    {
        HostSearchResults.Visibility = Visibility.Collapsed;
        DrillDownPanel.Visibility = Visibility.Visible;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        DrillDownTitle.Text = $"{series.Title} — Seasons";
        DrillDownBackText.Text = "Back to results";

        try
        {
            var catalogApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.CatalogApi>();
            var seasonsResp = await catalogApi.GetSeasonsAsync(series.ContentId);
            var seasonItems = seasonsResp.Seasons.Select(s => new ContinuumPlayer.Core.Models.Home.MediaItem
            {
                ContentId = s.ContentId,
                Title = $"Season {s.SeasonNumber}",
                Type = "season",
                PosterUrl = s.PosterUrl,
            }).ToList();
            DrillDownItems.ItemsSource = seasonItems;
        }
        catch { DrillDownItems.ItemsSource = null; }
    }

    private void DrillDownBack_Click(object sender, RoutedEventArgs e)
    {
        DrillDownPanel.Visibility = Visibility.Collapsed;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        HostSearchResults.Visibility = Visibility.Visible;
    }

    private void ShowCandidateSpotlight(ContinuumPlayer.Core.Models.Home.MediaItem item)
    {
        HostSearchResults.Visibility = Visibility.Collapsed;
        DrillDownPanel.Visibility = Visibility.Collapsed;
        CandidateSpotlight.Visibility = Visibility.Visible;

        CandidateTitle.Text = item.Title ?? "";
        CandidateMeta.Text = $"{item.Year}  ·  {item.Type}";
        CandidateOverview.Text = item.Overview ?? "";
        CandidatePlayBtn.Tag = item.ContentId;
    }

    private async void CandidatePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string contentId) return;
        if (string.IsNullOrEmpty(ViewModel.RoomId)) return;

        try
        {
            var playbackApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.PlaybackApi>();
            var resp = await playbackApi.SelectWatchTogetherRoomItemAsync(ViewModel.RoomId!, contentId);
            ViewModel.Room = resp.Room;
            CandidateSpotlight.Visibility = Visibility.Collapsed;
            HostSearchBox.Text = "";
        }
        catch { }
    }

    private void CandidateBack_Click(object sender, RoutedEventArgs e)
    {
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        HostSearchResults.Visibility = Visibility.Visible;
    }
}
