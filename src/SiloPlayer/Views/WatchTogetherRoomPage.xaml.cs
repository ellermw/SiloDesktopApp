using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
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
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _suppressSearchTextChanged;

    public WatchTogetherRoomPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchTogetherRoomViewModel>();
        this.InitializeComponent();
        SuggestionsRepeater.ItemsSource = ViewModel.Suggestions;
        _searchDebounce.Tick += SearchDebounce_Tick;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not WatchTogetherRoomNavigationArgs args
            || string.IsNullOrEmpty(args.RoomId)
            || string.IsNullOrEmpty(args.RoomAccessToken))
        {
            ShowTerminalState(
                "This invite link is incomplete",
                "The link is missing its access token, so the room can't be opened. Ask the host for a fresh invite, or join with a room code instead.",
                "Join with a code");
            return;
        }

        RoomContent.Visibility = Visibility.Visible;
        TerminalStatePanel.Visibility = Visibility.Collapsed;

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
        _searchDebounce.Stop();
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
        LeaveButton.Visibility = hasRoom && !isHost ? Visibility.Visible : Visibility.Collapsed;
        UpdateMembers(room?.Members);

        // Host content-search section — only for hosts during lobby (not
        // already playing). Hidden for guests and vote-mode rooms.
        HostPickSection.Visibility = (hasRoom && (isHost || isVoteMode))
            ? Visibility.Visible : Visibility.Collapsed;
        HostSearchBox.PlaceholderText = isVoteMode ? "Search to suggest something…" : "Search movies and series…";
        CandidateActionLabel.Text = isVoteMode ? "Suggest This" : isPlaying ? "Switch Everyone" : "Start for Everyone";
        CandidateKindLabel.Text = isVoteMode ? "YOUR SUGGESTION" : "READY TO PLAY";
        SearchEmptyText.Text = isPlaying
            ? "Search to switch what everyone is watching."
            : isVoteMode
                ? "Search for something to suggest to the room."
                : "Find something for everyone to watch.";

        if (isHost && room != null)
        {
            PolicyButtonText.Text = room.GuestControlPolicy == "guest_play_pause"
                ? "Host Only" : "Allow Pause";
        }

        // Now Playing
        if (isPlaying && !string.IsNullOrEmpty(room?.SelectedContentId))
        {
            NowPlayingPanel.Visibility = Visibility.Visible;
            _ = ResolveNowPlayingAsync(room!.SelectedContentId);
        }
        else
        {
            NowPlayingPanel.Visibility = Visibility.Collapsed;
        }

        // Vote-mode guests can search and suggest. Host-pick guests wait for the host.
        WaitingPanel.Visibility = hasRoom && !isHost && !isVoteMode ? Visibility.Visible : Visibility.Collapsed;
        WaitingSubtitle.Text = isPlaying
            ? "The host already started playback. You'll enter together."
            : "The host will choose a movie or episode for the room.";

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

    private void UpdateMembers(IReadOnlyList<WatchTogetherRoomMember>? members)
    {
        MembersWrapPanel.Children.Clear();
        if (members == null || members.Count == 0)
        {
            MembersPanel.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var member in members)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            row.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(member.Connected
                    ? Microsoft.UI.Colors.MediumSpringGreen
                    : Microsoft.UI.Colors.DimGray),
            });
            row.Children.Add(new TextBlock
            {
                Text = member.DisplayLabel,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (member.IsHost)
            {
                row.Children.Add(new TextBlock
                {
                    Text = "HOST",
                    FontSize = 9,
                    CharacterSpacing = 80,
                    Opacity = 0.65,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            MembersWrapPanel.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(11, 6, 11, 6),
                Child = row,
            });
        }

        MembersPanel.Visibility = Visibility.Visible;
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
        var title = reason switch
        {
            "host_left" or "room_closed" => "The room has ended.",
            "not_found" => "Room not found.",
            _ => "The room is unavailable.",
        };
        ShowTerminalState(
            title,
            "Start a new watch party or join another room to keep watching together.",
            "Start a new party");
    }

    private void ShowTerminalState(string title, string description, string action)
    {
        RoomContent.Visibility = Visibility.Collapsed;
        TerminalStateTitle.Text = title;
        TerminalStateDescription.Text = description;
        TerminalStateAction.Content = action;
        TerminalStatePanel.Visibility = Visibility.Visible;
        TerminalStateAction.Focus(FocusState.Programmatic);
    }

    private void UpdateSuggestionsUi()
    {
        int n = ViewModel.Suggestions.Count;
        SuggestionsSubtitle.Text = n == 0
            ? "No suggestions yet — search for something to add."
            : $"{n} suggestion{(n == 1 ? "" : "s")} from the room";
    }

    private async void CopyInviteButton_Click(object sender, RoutedEventArgs e)
    {
        var room = ViewModel.Room;
        if (room == null || string.IsNullOrEmpty(room.InvitePath)) return;

        try
        {
            var apiClient = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
            var baseUri = new Uri(apiClient.BaseUrl.TrimEnd('/') + "/");
            var inviteUri = new Uri(baseUri, room.InvitePath.TrimStart('/'));
            var pkg = new DataPackage();
            pkg.SetText(inviteUri.ToString());
            Clipboard.SetContent(pkg);
        }
        catch { }
        await Task.CompletedTask;
    }

    private async void EndButton_Click(object sender, RoutedEventArgs e)
    {
        var errorText = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "End the watch party for everyone?",
                    TextWrapping = TextWrapping.Wrap,
                },
                errorText,
            },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "End watch party?",
            Content = content,
            PrimaryButtonText = "End Party",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Ending…";
                errorText.Visibility = Visibility.Collapsed;
                await ViewModel.CloseRoomCommand.ExecuteAsync(null);
                if (ViewModel.ClosedReason == "host_left")
                {
                    args.Cancel = false;
                }
                else
                {
                    errorText.Text = ViewModel.ErrorMessage ?? "The watch party could not be ended.";
                    errorText.Visibility = Visibility.Visible;
                    dialog.PrimaryButtonText = "End Party";
                    dialog.IsPrimaryButtonEnabled = true;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private void LeaveButton_Click(object sender, RoutedEventArgs e)
        => Frame?.Navigate(typeof(WatchTogetherJoinPage));

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

    private async void HostSearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) await RunHostSearchAsync();
    }

    private void HostSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchTextChanged) return;
        ClearSearchButton.Visibility = string.IsNullOrWhiteSpace(HostSearchBox.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void SearchDebounce_Tick(object? sender, object e)
    {
        _searchDebounce.Stop();
        await RunHostSearchAsync();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        _suppressSearchTextChanged = true;
        HostSearchBox.Text = "";
        _suppressSearchTextChanged = false;
        ClearSearchButton.Visibility = Visibility.Collapsed;
        HostSearchResults.ItemsSource = null;
        HostSearchResults.Visibility = Visibility.Visible;
        DrillDownPanel.Visibility = Visibility.Collapsed;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        SearchEmptyState.Visibility = Visibility.Visible;
        HostSearchBox.Focus(FocusState.Programmatic);
    }

    private void SuggestButton_Click(object sender, RoutedEventArgs e)
    {
        HostSearchBox.StartBringIntoView();
        HostSearchBox.Focus(FocusState.Programmatic);
    }

    private async Task RunHostSearchAsync()
    {
        var query = HostSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            HostSearchResults.ItemsSource = null;
            SearchEmptyState.Visibility = Visibility.Visible;
            return;
        }
        SearchEmptyState.Visibility = Visibility.Collapsed;
        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var result = await catalogApi.SearchAsync(query, limit: 20);
            var items = result?.Items.Where(item => item.Type is "movie" or "series").ToList() ?? [];
            HostSearchResults.ItemsSource = items;
            SearchEmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count == 0) SearchEmptyText.Text = "No matches found. Try a different search.";
        }
        catch
        {
            HostSearchResults.ItemsSource = null;
            SearchEmptyState.Visibility = Visibility.Visible;
            SearchEmptyText.Text = "Search is temporarily unavailable.";
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
        DrillDownTitle.Text = series.Title;
        DrillDownSubtitle.Text = "Pick a season";
        DrillDownSubtitle.Visibility = Visibility.Visible;
        DrillDownBackText.Text = "Back to results";
        _showingEpisodes = false;

        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var seasonsResp = await catalogApi.GetSeasonsAsync(series.ContentId);
            var seasonItems = seasonsResp.Seasons.Select(s => new SiloPlayer.Core.Models.Home.MediaItem
            {
                ContentId = s.ContentId,
                Title = s.SeasonNumber == 0 ? "Specials" : $"Season {s.SeasonNumber}",
                Type = "season",
                PosterUrl = s.PosterUrl,
                Overview = $"{s.EpisodeCount} episode{(s.EpisodeCount == 1 ? "" : "s")}",
                Year = s.SeasonNumber,
            }).ToList();
            DrillDownItems.ItemsSource = seasonItems;
        }
        catch { DrillDownItems.ItemsSource = null; }
    }

    private async Task ShowEpisodesAsync(SiloPlayer.Core.Models.Home.MediaItem season)
    {
        if (string.IsNullOrWhiteSpace(_drillDownSeriesId)) return;
        DrillDownTitle.Text = season.Title;
        DrillDownSubtitle.Visibility = Visibility.Collapsed;
        DrillDownBackText.Text = "Back to seasons";
        _showingEpisodes = true;
        try
        {
            var seasonNumber = season.Year;
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var response = await catalogApi.GetEpisodesAsync(_drillDownSeriesId, seasonNumber);
            DrillDownItems.ItemsSource = response.Episodes.Select(ep => new SiloPlayer.Core.Models.Home.MediaItem
            {
                ContentId = ep.ContentId, Title = $"{ep.EpisodeNumber}. {ep.Title}", Type = "episode", Overview = ep.Overview ?? "", PosterUrl = ep.StillUrl,
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
        SetImageSource(CandidatePoster, item.PosterUrl);
        SetImageSource(CandidateBackdrop, item.BackdropUrl);
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
            ClearSearchButton_Click(this, new RoutedEventArgs());
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
            if (_nowPlayingContentId == contentId)
            {
                NowPlayingTitle.Text = item.Title;
                NowPlayingSubtitle.Text = item.Type == "episode" ? "Episode" : "Movie";
                SetImageSource(NowPlayingBackdrop, item.BackdropUrl);
            }
        }
        catch { if (_nowPlayingContentId == contentId) NowPlayingTitle.Text = "Now playing"; }
    }

    private static void SetImageSource(Image target, string? url)
    {
        target.Source = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? new BitmapImage(uri) : null;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        var gutter = width < 640 ? 16d : width < 1024 ? 28d : 40d;
        PageShell.Padding = new Thickness(gutter, width < 640 ? 18 : 28, gutter, 42);

        var stackHeader = width < 820;
        Grid.SetRow(RoomHeaderActions, stackHeader ? 1 : 0);
        Grid.SetColumn(RoomHeaderActions, stackHeader ? 0 : 1);
        RoomHeaderActions.HorizontalAlignment = stackHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        RoomHeaderSummary.Spacing = width < 560 ? 10 : 20;

        NowPlayingGrid.Height = width < 640 ? 132 : 150;
        NowPlayingTitle.FontSize = width < 640 ? 22 : 27;

        var compactCandidate = width < 660;
        CandidatePosterBorder.Visibility = compactCandidate ? Visibility.Collapsed : Visibility.Visible;
        CandidateContentGrid.ColumnDefinitions[0].Width = compactCandidate ? new GridLength(0) : new GridLength(116);
        Grid.SetColumn(CandidateTextPanel, compactCandidate ? 0 : 1);
        Grid.SetColumnSpan(CandidateTextPanel, compactCandidate ? 2 : 1);
        CandidateContentGrid.Padding = new Thickness(compactCandidate ? 16 : 22);
        CandidateTitle.FontSize = compactCandidate ? 20 : 24;
    }
}
