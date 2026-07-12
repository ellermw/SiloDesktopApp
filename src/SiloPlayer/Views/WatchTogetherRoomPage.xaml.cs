using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Views;

/// <summary>
/// Watch Party room page — shows room code, participant count, host controls, and the
/// suggestion list in vote mode. Shadow of WatchTogetherRoomPage.tsx.
///
/// Surfaces room state, host policy controls, content search and voting, and connects
/// synchronized playback to PlayerService through WatchTogetherCoordinator.
/// </summary>
public sealed partial class WatchTogetherRoomPage : Page
{
    public WatchTogetherRoomViewModel ViewModel { get; }
    private bool _subscribed;
    private string? _nowPlayingContentId;

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
                var player = App.Services.GetRequiredService<SiloPlayer.Services.PlayerService>();
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
        HostPickSection.Visibility = (hasRoom && (isHost || isVoteMode))
            ? Visibility.Visible : Visibility.Collapsed;
        HostSearchBox.PlaceholderText = isVoteMode ? "Search to suggest something…" : isPlaying ? "Search to switch what everyone is watching…" : "Search movies and series…";
        if (CandidatePlayBtn.Content is StackPanel candidateContent && candidateContent.Children.LastOrDefault() is TextBlock candidateLabel)
            candidateLabel.Text = isVoteMode ? "Suggest This" : isPlaying ? "Switch Everyone" : "Start for Everyone";

        if (isHost && room != null)
        {
            PolicyButtonText.Text = room.GuestControlPolicy == "guest_play_pause"
                ? "Host Only" : "Allow Pause";
        }

        // Now Playing
        if (isPlaying && !string.IsNullOrEmpty(room?.SelectedContentId))
        {
            NowPlayingPanel.Visibility = Visibility.Visible;
            NowPlayingSubtitle.Text = "Synchronized playback";
            _ = ResolveNowPlayingAsync(room!.SelectedContentId);
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

    private void SuggestionPromote_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) element.Visibility = ViewModel.IsHost ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SuggestionDelete_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WatchTogetherSuggestion suggestion } element) return;
        var profileId = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>().ProfileId;
        element.Visibility = ViewModel.IsHost || suggestion.SuggesterProfileId == profileId ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SuggestionPromote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WatchTogetherSuggestion suggestion }) return;
        await ViewModel.PromoteSuggestionAsync(suggestion.Id);
    }

    private async void SuggestionDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WatchTogetherSuggestion suggestion }) return;
        await ViewModel.DeleteSuggestionAsync(suggestion.Id);
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
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var result = await catalogApi.SearchAsync(query, limit: 20);
            HostSearchResults.ItemsSource = result?.Items.Where(item => item.Type is "movie" or "series").ToList();
        }
        catch
        {
            // Keep the UI usable on transient errors — empty list is fine.
            HostSearchResults.ItemsSource = null;
        }
    }

    private async void HostSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SiloPlayer.Core.Models.Home.MediaItem item })
            OnSearchResultSelected(item);
        await Task.CompletedTask;
    }

    // ===== Series Drill-Down =====

    private string? _drillDownSeriesId;
    private SiloPlayer.Core.Models.Home.MediaItem? _drillDownSeries;
    private bool _showingEpisodes;

    /// <summary>
    /// Called when a search result is selected. Movies go directly to candidate spotlight.
    /// Series drill down into seasons → episodes.
    /// </summary>
    public async void OnSearchResultSelected(SiloPlayer.Core.Models.Home.MediaItem item)
    {
        if (item.Type == "movie")
        {
            ShowCandidateSpotlight(item);
            return;
        }

        if (item.Type == "series")
        {
            _drillDownSeriesId = item.ContentId;
            _drillDownSeries = item;
            await ShowSeasonsAsync(item);
        }
    }

    private async Task ShowSeasonsAsync(SiloPlayer.Core.Models.Home.MediaItem series)
    {
        HostSearchResults.Visibility = Visibility.Collapsed;
        DrillDownPanel.Visibility = Visibility.Visible;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        DrillDownTitle.Text = $"{series.Title} — Seasons";
        DrillDownBackText.Text = "Back to results";
        _showingEpisodes = false;

        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var seasonsResp = await catalogApi.GetSeasonsAsync(series.ContentId);
            var seasonItems = seasonsResp.Seasons.Select(s => new SiloPlayer.Core.Models.Home.MediaItem
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

    private async Task ShowEpisodesAsync(SiloPlayer.Core.Models.Home.MediaItem season)
    {
        if (string.IsNullOrWhiteSpace(_drillDownSeriesId)) return;
        DrillDownTitle.Text = season.Title;
        DrillDownBackText.Text = "Back to seasons";
        _showingEpisodes = true;
        try
        {
            var numberText = new string(season.Title.Where(char.IsDigit).ToArray());
            if (!int.TryParse(numberText, out var seasonNumber)) return;
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var response = await catalogApi.GetEpisodesAsync(_drillDownSeriesId, seasonNumber);
            DrillDownItems.ItemsSource = response.Episodes.Select(ep => new SiloPlayer.Core.Models.Home.MediaItem
            {
                ContentId = ep.ContentId, Title = ep.Title, Type = "episode", Overview = ep.Overview ?? "", PosterUrl = ep.StillUrl,
                Year = DateTime.TryParse(ep.AirDate, out var airDate) ? airDate.Year : 0
            }).ToList();
        }
        catch { DrillDownItems.ItemsSource = null; }
    }

    private async void DrillDownResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SiloPlayer.Core.Models.Home.MediaItem item }) return;
        if (item.Type == "season") await ShowEpisodesAsync(item);
        else ShowCandidateSpotlight(item);
    }

    private void DrillDownBack_Click(object sender, RoutedEventArgs e)
    {
        if (_showingEpisodes && _drillDownSeries != null)
        {
            _ = ShowSeasonsAsync(_drillDownSeries);
            return;
        }
        DrillDownPanel.Visibility = Visibility.Collapsed;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        HostSearchResults.Visibility = Visibility.Visible;
    }

    private void ShowCandidateSpotlight(SiloPlayer.Core.Models.Home.MediaItem item)
    {
        HostSearchResults.Visibility = Visibility.Collapsed;
        DrillDownPanel.Visibility = Visibility.Collapsed;
        CandidateSpotlight.Visibility = Visibility.Visible;

        CandidateTitle.Text = item.Title ?? "";
        CandidateMeta.Text = $"{item.Year}  ·  {item.Type}";
        CandidateOverview.Text = item.Overview ?? "";
        CandidatePlayBtn.Tag = item;
    }

    private async void CandidatePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SiloPlayer.Core.Models.Home.MediaItem candidate }) return;
        var contentId = candidate.ContentId;
        if (string.IsNullOrEmpty(ViewModel.RoomId)) return;

        try
        {
            var playbackApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.PlaybackApi>();
            if (ViewModel.IsVoteMode && !string.IsNullOrWhiteSpace(ViewModel.RoomToken))
            {
                var contentType = candidate.Type == "episode" ? "episode" : "movie";
                var result = await playbackApi.CreateWatchTogetherSuggestionAsync(ViewModel.RoomId!, ViewModel.RoomToken!, contentId, contentType, CandidateTitle.Text, CandidateMeta.Text, candidate.PosterUrl);
                ViewModel.Suggestions.Clear();
                foreach (var suggestion in result.Suggestions) ViewModel.Suggestions.Add(suggestion);
            }
            else
            {
                var resp = await playbackApi.SelectWatchTogetherRoomItemAsync(ViewModel.RoomId!, contentId);
                ViewModel.Room = resp.Room;
            }
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

    private async Task ResolveNowPlayingAsync(string contentId)
    {
        if (_nowPlayingContentId == contentId) return;
        _nowPlayingContentId = contentId;
        NowPlayingTitle.Text = "Loading title…";
        try
        {
            var item = await App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>().GetItemDetailAsync(contentId);
            if (_nowPlayingContentId == contentId) NowPlayingTitle.Text = item.Title;
        }
        catch { if (_nowPlayingContentId == contentId) NowPlayingTitle.Text = "Now playing"; }
    }
}
