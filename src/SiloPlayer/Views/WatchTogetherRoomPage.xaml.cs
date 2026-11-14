using SiloPlayer.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Converters;
using SiloPlayer.Controls;
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
    private static readonly UrlToImageSourceConverter RemoteImageConverter = new();
    public WatchTogetherRoomViewModel ViewModel { get; private set; }
    private bool _subscribed;
    private long _navigationGeneration;
    private string? _nowPlayingContentId;
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _suppressSearchTextChanged;
    private bool _candidateSpoilerRisk;
    private bool _candidateStateLoading;
    private CancellationTokenSource? _browseCts;
    private long _browseRevision;
    private string? _browseRoomId, _browseRoomToken;
    private SiloPlayer.Core.Api.ApiRequestContext? _browseContext;
    private bool HasCurrentBrowseState() => _browseRevision > 0 && _browseRoomId == ViewModel.RoomId && _browseRoomToken == ViewModel.RoomToken
        && _browseContext is { } context && App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>().IsCurrentContext(context);
    private bool _shelfMode;
    private readonly Dictionary<string, WatchTogetherPickerNextUp> _nextUp = [];
    private readonly Dictionary<string, WatchTogetherItemMemberState> _memberStates = [];
    private readonly Dictionary<string, List<StackPanel>> _memberDots = [];
    private List<WatchTogetherRoomMember> _stateMembers = [];
    private string _memberFingerprint = "";
    private string? _stagedTitleContentId;
    private readonly Dictionary<string, WatchTogetherRoomMember> _previousMembers = [];
    private string? _previousPhase;
    private string? _previousSelection;
    private bool _updatingPausePolicy;
    private readonly ScrollViewer _roomScroll;
    private readonly Grid _browseFilterLayout = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private readonly Border _compactRailStrip = new() { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 8, 16, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock _compactPeopleLabel = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _compactActivityLabel = new() { FontSize = 12, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _modeLabel = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _modeOwner = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _inviteLabel = new() { FontSize = 12, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock _readOnlyModeLabel = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly Border _readOnlyModeChip = new() { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 0, 12, 0), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
    private void UpdateModeChip(double width)
    {
        var visible = width <= 0 || width >= 400;
        var mutable = ViewModel.IsHost && ViewModel.Room?.Phase == "lobby" && ViewModel.Capabilities.SelectionModeSwitch;
        ModeButton.Visibility = visible && mutable ? Visibility.Visible : Visibility.Collapsed;
        var voteMode = ViewModel.Room?.SelectionMode == "vote";
        LocalizeText(_readOnlyModeLabel, () => ModeLabel(voteMode));
        _readOnlyModeChip.Visibility = visible && ViewModel.Room != null && !mutable ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _activitySelected;

    public WatchTogetherRoomPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchTogetherCoordinator>().ActiveRoom ?? App.Services.GetRequiredService<WatchTogetherRoomViewModel>();
        this.InitializeComponent();
        Loaded += (_, _) => ObservePickerSpoilers();
        Unloaded += (_, _) => { if (!IsLoaded && _pickerSpoilers != null) _pickerSpoilers.Changed -= PickerSpoilersChanged; };
        InitializeSeriesPicker();
        var stageCode = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        stageCode.Children.Add(SiloPlayer.Controls.WebUiIcon.Create("copy", 14));
        var stageCodeLabel = new TextBlock { FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        stageCode.Children.Add(stageCodeLabel);
        StageCodeButton.Content = stageCode;
        StageInvitePanel.SizeChanged += (_, _) => UpdateInviteLayout(ActualWidth);
        StageInviteInfo.SizeChanged += (_, _) => UpdateInviteLayout(ActualWidth);
        GuestPausePanel.SizeChanged += (_, _) => UpdateInviteLayout(ActualWidth);
        var modeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        modeContent.Children.Add(_modeLabel); modeContent.Children.Add(_modeOwner);
        modeContent.Children.Add(new FontIcon { Glyph = "\uE70D", FontSize = 12 });
        ModeButton.Content = modeContent;
        // The current room keeps its status strip and people rail fixed while
        // the stage and picker scroll independently inside the remaining viewport.
        RoomContent.Children.Remove(RoomStatusBar);
        RoomWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RoomWorkspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RoomWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RoomWorkspace.Children.Remove(RoomContent);
        _roomScroll = new ScrollViewer { Content = RoomContent, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20, 16, 20, 16) };
        Grid.SetRow(_roomScroll, 1); Grid.SetRow(RoomRail, 1);
        Grid.SetColumnSpan(RoomStatusBar, 2);
        RoomWorkspace.Children.Add(RoomStatusBar); RoomWorkspace.Children.Add(_roomScroll);
        RoomContent.MaxWidth = double.PositiveInfinity;
        RoomContent.HorizontalAlignment = HorizontalAlignment.Stretch;
        RoomWorkspace.MaxWidth = double.PositiveInfinity; RoomWorkspace.ColumnSpacing = 0;
        ((ScrollViewer)Content).VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        PageShell.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var legacyStatusGroup = (StackPanel)RoomHeaderSummary.Children[2];
        var connectionStatus = legacyStatusGroup.Children[1];
        legacyStatusGroup.Children.RemoveAt(1);
        RoomHeaderSummary.Children.Remove(legacyStatusGroup);
        RoomHeaderActions.Children.Insert(0, connectionStatus);
        foreach (var action in RoomHeaderActions.Children.OfType<Button>())
        {
            action.Height = 32; action.MinHeight = 0; action.Padding = new Thickness(10, 0, 10, 0);
        }
        RoomHeaderActions.Children.Remove(CopyInviteButton); RoomHeaderActions.Children.Remove(ModeButton);
        RoomHeaderSummary.Children.Add(CopyInviteButton); RoomHeaderSummary.Children.Add(ModeButton);
        _readOnlyModeChip.Child = _readOnlyModeLabel;
        _readOnlyModeChip.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(26, 255, 255, 255));
        _readOnlyModeLabel.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        RoomHeaderSummary.Children.Add(_readOnlyModeChip);
        CopyInviteButton.Height = ModeButton.Height = 28; CopyInviteButton.CornerRadius = ModeButton.CornerRadius = new CornerRadius(14);
        var invite = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        invite.Children.Add(SiloPlayer.Controls.WebUiIcon.Navigation("link", 12)); invite.Children.Add(_inviteLabel); CopyInviteButton.Content = invite;
        CopyCodeButton.Content = null;
        var code = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; code.Children.Add(CodeText);
        code.Children.Add(SiloPlayer.Controls.WebUiIcon.Create("copy", 12)); CopyCodeButton.Content = code;
        RoomHeaderSummary.Children.Insert(0, new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153)) });
        var leave = (StackPanel)LeaveButton.Content; leave.Children.RemoveAt(0); leave.Children.Insert(0, SiloPlayer.Controls.WebUiIcon.Create("log-out", 14));
        RoomHeaderActions.Children.Remove(LeaveButton); RoomHeaderActions.Children.Insert(RoomHeaderActions.Children.IndexOf(EndButton), LeaveButton);
        RoomHeaderSummary.SizeChanged += (_, args) => RoomHeaderSummary.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height) };
        RoomHeaderActions.SizeChanged += (_, _) => UpdateHeaderWidth();
        for (var index = 0; index < ShelfPicker.Items.Count; index++)
        {
            var chip = new Button { Content = ((ComboBoxItem)ShelfPicker.Items[index]).Content, Tag = index, CornerRadius = new CornerRadius(12), Height = 24, MinHeight = 0, Padding = new Thickness(12, 0, 12, 0), FontSize = 12, BorderThickness = new Thickness(0) };
            chip.Click += (_, _) =>
            {
                ShelfPicker.SelectedIndex = (int)chip.Tag;
                UpdateShelfChips();
            };
            ShelfChipsPanel.Children.Add(chip);
        }
        UpdateShelfChips();
        var filters = BrowseFilterContainer;
        var search = BrowseSearchContainer;
        filters.Children.Remove(search); filters.Children.Remove(ShelfChipsPanel);
        _browseFilterLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); _browseFilterLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _browseFilterLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); _browseFilterLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _browseFilterLayout.Children.Add(search); _browseFilterLayout.Children.Add(ShelfChipsPanel);
        filters.Children.Add(_browseFilterLayout);
        RoomHeaderActions.Children.Remove(RailSheetButton);
        var railSummary = new Grid { ColumnSpacing = 12 };
        railSummary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        railSummary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_compactActivityLabel, 1);
        railSummary.Children.Add(_compactPeopleLabel); railSummary.Children.Add(_compactActivityLabel);
        RailSheetButton.Content = railSummary;
        RailSheetButton.Height = 20; RailSheetButton.MinHeight = 0; RailSheetButton.Padding = new Thickness(0);
        RailSheetButton.BorderThickness = new Thickness(0); RailSheetButton.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        RailSheetButton.HorizontalAlignment = HorizontalAlignment.Stretch; RailSheetButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _compactRailStrip.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        _compactActivityLabel.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        _compactRailStrip.Child = RailSheetButton;
        Grid.SetRow(_compactRailStrip, 2); Grid.SetColumnSpan(_compactRailStrip, 2);
        RoomWorkspace.Children.Add(_compactRailStrip);
        SuggestionsRepeater.ItemsSource = ViewModel.Suggestions;
        _searchDebounce.Tick += SearchDebounce_Tick;
        InitializeLocalization();
        LocalizeText(stageCodeLabel, () => T("web.watchTogether.inviteBar.code", "Code"));
        LocalizeText(InviteEyebrow, () => Upper(T("web.watchTogether.inviteBar.label", "Invite")));
        LocalizeText(NowPlayingEyebrow, () => Upper(T("web.watchTogether.playing.eyebrow", "Now playing")));
        LocalizeText(PickerSeriesEyebrow, () => Upper(T("web.watchTogether.series.eyebrow", "Candidate · series")));
        LocalizeText(PickerPickEyebrow, () => Upper(T("web.watchTogether.series.yourPick", "Your pick")));
        LocalizeText(PickerDotsLegend, () => "● " + T("web.watchTogether.dots.watched", "watched") + "   ◐ " + T("web.watchTogether.dots.inProgress", "in progress") + "   ○ " + T("web.watchTogether.dots.unseen", "unseen"));
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not WatchTogetherRoomNavigationArgs args
            || string.IsNullOrEmpty(args.RoomId)
            || string.IsNullOrEmpty(args.RoomAccessToken))
        {
            ShowTerminalState(
                () => T("web.watchTogether.room.incompleteTitle", "This invite link is incomplete"),
                () => T("web.watchTogether.room.incompleteBody", "The link is missing its access token. Ask the host for a fresh invite, or join with a room code."),
                () => T("watchParty.page.joinWithCode", "Join with a code"));
            return;
        }

        RoomWorkspace.Visibility = Visibility.Visible;
        RoomContent.Visibility = Visibility.Visible;
        TerminalStatePanel.Visibility = Visibility.Collapsed;
        // Leaving during the awaits below must not let this page re-register
        // with the coordinator after OnNavigatedFrom unsubscribed it.
        var navigation = ++_navigationGeneration;
        var coordinator = App.Services.GetRequiredService<WatchTogetherCoordinator>();
        var activeRoom = coordinator.ActiveRoom;
        var join = coordinator.BeginRoomJoin();
        if (ReferenceEquals(ViewModel, activeRoom)
            && (ViewModel.RoomId != args.RoomId || ViewModel.RoomToken != args.RoomAccessToken))
        {
            if (_subscribed)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Suggestions.CollectionChanged -= Suggestions_CollectionChanged;
                _subscribed = false;
            }
            ViewModel = App.Services.GetRequiredService<WatchTogetherRoomViewModel>();
            SuggestionsRepeater.ItemsSource = ViewModel.Suggestions;
            Bindings.Update();
        }

        var joiningRoom = ViewModel;
        bool OwnsJoin()
        {
            if (coordinator.IsCurrentRoomJoin(join)) return true;
            if (!ReferenceEquals(coordinator.ActiveRoom, joiningRoom)) joiningRoom.Dispose();
            return false;
        }

        if (!_subscribed)
        {
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.Suggestions.CollectionChanged += Suggestions_CollectionChanged;
            _subscribed = true;
        }

        await joiningRoom.LoadCapabilitiesAsync();
        if (!OwnsJoin()) return;
        if (joiningRoom.RoomId != args.RoomId || joiningRoom.RoomToken != args.RoomAccessToken || joiningRoom.ConnectionState == "disconnected" || !joiningRoom.CanAdoptRoom)
            await joiningRoom.InitializeCurrentJoinAsync(args.RoomId, args.RoomAccessToken, () => coordinator.IsCurrentRoomJoin(join));
        if (!OwnsJoin()) return;
        // The room socket outlives this page, so the coordinator still adopts
        // the room; only page work and its auto-start handler require the page.
        if (navigation != _navigationGeneration)
        {
            coordinator.TryAdoptRoom(joiningRoom, join);
            return;
        }
        _ = RunHostSearchAsync();
        UpdateRoomUi();
        UpdateConnectionUi();
        UpdateSuggestionsUi();

        // Register with the playback-sync coordinator so inbound transport
        // commands reach mpv and local playback sessions get attached.
        coordinator.PlaybackStartRequested -= OnCoordinatorStartRequested;
        coordinator.PlaybackStartRequested += OnCoordinatorStartRequested;
        if (!coordinator.TryAdoptRoom(joiningRoom, join))
            coordinator.PlaybackStartRequested -= OnCoordinatorStartRequested;
    }

    /// <summary>
    /// Raised by the coordinator when the room transitions to phase=playing on
    /// a new selection_revision. Kicks off local playback so all members share
    /// the experience without the host having to manually navigate each guest.
    /// </summary>
    private void OnCoordinatorStartRequested(string contentId)
    {
        var requestedRoom = ViewModel;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var player = App.Services.GetRequiredService<SiloPlayer.Services.PlayerService>();
                var coordinator = App.Services.GetRequiredService<WatchTogetherCoordinator>();
                if (player.HasRemoteAuthority || !ReferenceEquals(coordinator.ActiveRoom, requestedRoom)) return;
                var room = requestedRoom.Room;
                if (room?.Phase != "playing" || room.SelectedContentId != contentId) return;
                await player.PlayAsync(contentId, fileId: room.SelectedFileId,
                    startPositionOverride: room.AnchorPositionSeconds);
            }
            catch (Exception error) { requestedRoom.SetLocalizedError(SiloPlayer.ViewModels.WatchPartyText.ActionFailure("web.watchTogether.actions.startFailed", "Could not start playback", error)); }
        });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ++_navigationGeneration;
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
            // The room socket continues across navigation; explicit Leave/End revokes it.
        }
        catch { }
        _searchDebounce.Stop();
        _browseCts?.Cancel();
        ++_candidateStateRevision;
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
                case nameof(ViewModel.IsBusy):
                    StartStagedButton.IsEnabled = !ViewModel.IsBusy && ViewModel.Capabilities.StagedSelection;
                    ChangeStagedButton.IsEnabled = !ViewModel.IsBusy;
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
        UpdatePickerConfirmation();

        CodeText.Text = ViewModel.DisplayCode;
        var memberCount = room?.MemberCount ?? 0;
        var stripKey = room?.Phase == "playing" ? ("web.watchTogether.strip.playing", "Playing") : room?.SelectionMode == "vote" ? ("web.watchTogether.strip.voting", "Voting") : ("web.watchTogether.strip.live", "Live");
        LocalizeText(PhaseText, () => T(stripKey.Item1, $"{stripKey.Item2} · {memberCount} here", CountArguments(memberCount)));
        MemberCountText.Text = Count(ViewModel.MemberCount);
        var peopleCount = ViewModel.MemberCount;
        LocalizeText(_compactPeopleLabel, () => T("web.watchTogether.rail.peopleCount", $"People · {peopleCount}", new Dictionary<string, object?> { ["count"] = Count(peopleCount) }));

        bool hasRoom = room != null && ViewModel.ClosedReason == null;
        bool isHost = ViewModel.IsHost;
        bool isVoteMode = ViewModel.IsVoteMode;
        bool isPlaying = ViewModel.IsPlaying;
        bool hasInvite = hasRoom && !string.IsNullOrEmpty(room!.InvitePath);

        CopyInviteButton.Visibility = hasInvite ? Visibility.Visible : Visibility.Collapsed;
        PolicyButton.Visibility = isHost ? Visibility.Visible : Visibility.Collapsed;
        EndButton.Visibility = isHost ? Visibility.Visible : Visibility.Collapsed;
        LeaveButton.Visibility = hasRoom ? Visibility.Visible : Visibility.Collapsed;
        UpdateRoomActivity(room);
        UpdateMembers(room?.Members);
        var fingerprint = string.Join("|", (room?.Members ?? []).Where(m => m.Connected).Select(m => $"{m.UserId}:{m.ProfileId}").Order());
        if (_memberFingerprint != fingerprint)
        {
            _memberFingerprint = fingerprint;
            if (_browseRevision > 0) _ = RefreshPickerMembersAsync();
        }
        LocalizeText(_modeLabel, () => ModeLabel(isVoteMode));
        UpdateModeChip(ActualWidth);
        _modeOwner.Visibility = ActualWidth <= 0 || ActualWidth >= 640 ? Visibility.Visible : Visibility.Collapsed;
        var staged = room?.Phase == "lobby" && !string.IsNullOrEmpty(room.SelectedContentId);
        StagedPanel.Visibility = staged ? Visibility.Visible : Visibility.Collapsed;
        if (staged && _stagedTitleContentId != room!.SelectedContentId)
        {
            _stagedTitleContentId = room.SelectedContentId;
            _ = ResolveStagedTitleAsync(room.SelectedContentId!);
        }
        StartStagedButton.Visibility = staged && isHost ? Visibility.Visible : Visibility.Collapsed;
        StartStagedButton.IsEnabled = !ViewModel.IsBusy && ViewModel.Capabilities.StagedSelection;
        LobbyReadyButton.Visibility = staged && !isHost && ViewModel.Capabilities.LobbyReady ? Visibility.Visible : Visibility.Collapsed;
        var selfReady = room?.Members.FirstOrDefault(m => m.IsSelf)?.LobbyReady == true;
        Localize(LobbyReadyButton, "content", () => LobbyReadyButton.Content = selfReady ? T("web.watchTogether.lobby.readyDone", "Ready ✓") : T("watchParty.button.imReady", "I'm ready"));
        var guests = room?.Members.Where(m => !m.IsHost && m.Connected).ToArray() ?? [];
        var hostName = room?.Members.FirstOrDefault(member => member.IsHost)?.DisplayName;
        var readyCount = guests.Count(m => m.LobbyReady);
        var guestTotal = guests.Length;
        // Member display names are data and pass through as arguments.
        string Host() => hostName ?? T("watchParty.theHost", "The host");
        Dictionary<string, object?> ReadyArguments() => new() { ["host"] = Host(), ["ready"] = Count(readyCount), ["total"] = guestTotal, ["total_text"] = Count(guestTotal) };
        LocalizeText(ReadySummary, () => isHost
            ? guestTotal == 0 ? T("web.watchTogether.lobby.waitingGuest", "Waiting for a guest to join") : T("web.watchTogether.presence.ready", $"{readyCount} of {guestTotal} ready", ReadyArguments())
            : guestTotal == 0 ? T("web.watchTogether.voting.guestCaption", $"Starts when {Host()} presses play", ReadyArguments())
                : T("web.watchTogether.lobby.guestCaption", $"Starts when {Host()} presses play · {readyCount} of {guestTotal} ready", ReadyArguments()));
        LocalizeText(StagedEyebrow, () => Upper(isHost ? T("web.watchTogether.lobby.upNext", "Up next")
            : T("web.watchTogether.lobby.upNextGuest", $"Up next · {Host()} queued this", new Dictionary<string, object?> { ["host"] = Host() })));
        ChangeStagedButton.Visibility = staged && isHost ? Visibility.Visible : Visibility.Collapsed;
        ChangeStagedButton.IsEnabled = !ViewModel.IsBusy;
        ReadyCheckPanel.Visibility = guests.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        LocalizeText(ReadyCheckSummary, () => T("web.watchTogether.presence.ready", $"{readyCount} of {guestTotal} ready", ReadyArguments()));
        ReadyCheckMembers.Children.Clear();
        foreach (var member in guests)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(MemberAvatar(member, 24));
            row.Children.Add(new TextBlock { Text = member.DisplayName, FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = member.LobbyReady ? "✓" : "○", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(member.LobbyReady ? Microsoft.UI.Colors.MediumSeaGreen : Microsoft.UI.Colors.Gray) });
            ReadyCheckMembers.Children.Add(row);
        }
        _updatingPausePolicy = true;
        GuestPauseToggle.IsOn = room?.GuestControlPolicy == "guest_play_pause";
        GuestPausePanel.Visibility = isHost ? Visibility.Visible : Visibility.Collapsed;
        GuestPauseToggle.IsEnabled = !ViewModel.IsBusy;
        _updatingPausePolicy = false;
        PolicyButton.Visibility = Visibility.Collapsed;
        var start = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        start.Children.Add(new FontIcon { Glyph = "\uE768", FontSize = 12 });
        ForgetLocalized("start");
        var startLabel = new TextBlock { FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        LocalizeText(startLabel, () => guestTotal == 0 ? T("web.watchTogether.lobby.startAlone", "Start alone anyway")
            : T("web.watchTogether.lobby.startEveryone", $"Start for everyone · {readyCount}/{guestTotal} ready", ReadyArguments()), "start");
        start.Children.Add(startLabel);
        StartStagedButton.Content = start;


        // Host content-search section — only for hosts during lobby (not
        // already playing). Hidden for guests and vote-mode rooms.
        HostPickSection.Visibility = hasRoom
            ? Visibility.Visible : Visibility.Collapsed;
        LocalizeText(BrowseHeading, () => isHost && !isVoteMode ? T("web.watchTogether.browse.sectionPick", "Browse to pick") : T("web.watchTogether.browse.sectionSuggest", "Browse to suggest"));
        // "Switch everyone", the kind eyebrows and the empty-search prompts have no upstream keys; English fallbacks.
        LocalizeText(CandidateActionLabel, () => !isHost || isVoteMode ? T("web.watchTogether.candidate.suggestThis", "Suggest this") : isPlaying ? "Switch everyone" : T("web.watchTogether.candidate.stageForRoom", "Stage it for the room"));
        CandidateKindLabel.Text = isVoteMode ? "YOUR SUGGESTION" : "READY TO PLAY";
        if (!HasCurrentBrowseState())
            LocalizeText(SearchEmptyText, () => isPlaying
                ? "Search to switch what everyone is watching."
                : isVoteMode
                    ? "Search for something to suggest to the room."
                    : "Find something for everyone to watch.");

        if (isHost && room != null)
        {
            var guestCanPause = room.GuestControlPolicy == "guest_play_pause";
            LocalizeText(PolicyButtonText, () => guestCanPause ? T("web.player.watchTogether.setHostOnly", "Host Only") : T("web.player.watchTogether.allowPause", "Allow Pause"));
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
        WaitingPanel.Visibility = hasRoom && !isHost && !isVoteMode && !staged ? Visibility.Visible : Visibility.Collapsed;
        // Desktop guest waiting copy has no upstream keys; English fallbacks.
        WaitingSubtitle.Text = isPlaying
            ? "The host already started playback. You'll enter together."
            : "The host will choose a movie or episode for the room.";

        // Suggestions visible in vote mode only
        SuggestionsPanel.Visibility = hasRoom ? Visibility.Visible : Visibility.Collapsed;
        UpdateSuggestionsUi();


    }

    private async Task ResolveStagedTitleAsync(string contentId)
    {
        LocalizeText(StagedTitle, () => T("web.watchTogether.hero.loading", "Loading…"));
        try
        {
            var item = await App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>().GetItemDetailAsync(contentId);
            if (ViewModel.Room?.SelectedContentId != contentId) return;
            // Series and episode titles are data; only the S/E pattern is interface text.
            LocalizeText(StagedTitle, () => item.Type == "episode" && !string.IsNullOrWhiteSpace(item.SeriesTitle)
                ? T("web.watchTogether.hero.episodeTitled", $"{item.SeriesTitle} — S{item.SeasonNumber} E{item.EpisodeNumber} \"{item.Title}\"",
                    new Dictionary<string, object?> { ["series"] = item.SeriesTitle, ["season"] = item.SeasonNumber ?? 0, ["episode"] = item.EpisodeNumber ?? 0, ["title"] = item.Title })
                : item.Title);
            SetImageSource(StagedBackdrop, item.BackdropUrl); SetImageSource(StagedPoster, item.PosterUrl);
        }
        catch { if (ViewModel.Room?.SelectedContentId == contentId) LocalizeText(StagedTitle, () => T("web.watchTogether.hero.unavailable", "Unavailable")); }
    }
    private void UpdateRoomActivity(WatchTogetherRoomSnapshot? room)
    {
        if (room == null) return;
        foreach (var member in room.Members)
        {
            var key = member.ProfileId;
            var who = member.DisplayName;
            if (!_previousMembers.TryGetValue(key, out var old)) AddActivity(MemberActivity("web.watchTogether.activity.joined", who + " joined", who));
            else if (old.LobbyReady != member.LobbyReady) AddActivity(member.LobbyReady
                ? MemberActivity("web.watchTogether.activity.ready", who + " is ready", who)
                : MemberActivity("web.watchTogether.activity.unready", who + " is no longer ready", who));
            // Connection changes, phase and selection notes have no upstream activity keys; English fallbacks.
            else if (old.Connected != member.Connected) AddActivity(() => who + (member.Connected ? " reconnected" : " disconnected"));
        }
        foreach (var old in _previousMembers.Values.Where(old => room.Members.All(member => member.ProfileId != old.ProfileId)))
        {
            var who = old.DisplayName;
            AddActivity(MemberActivity("web.watchTogether.activity.left", who + " left", who));
        }
        if (_previousPhase != null && _previousPhase != room.Phase) { var playing = room.Phase == "playing"; AddActivity(() => playing ? "Playback started" : "Returned to the lobby"); }
        if (_previousSelection != room.SelectedContentId && room.SelectedContentId != null) { var lobby = room.Phase == "lobby"; AddActivity(() => lobby ? "A title was staged for the room" : "The room changed titles"); }
        _previousPhase = room.Phase; _previousSelection = room.SelectedContentId;
        _previousMembers.Clear(); foreach (var member in room.Members) _previousMembers[member.ProfileId] = member;
    }
    private void PeopleTab_Click(object sender, RoutedEventArgs e) { _activitySelected = false; UpdateRailTabs(); }
    private void ActivityTab_Click(object sender, RoutedEventArgs e) { _activitySelected = true; UpdateRailTabs(); }
    private void UpdateRailTabs()
    {
        MembersPanel.Visibility = _activitySelected ? Visibility.Collapsed : Visibility.Visible;
        ActivityScroller.Visibility = _activitySelected ? Visibility.Visible : Visibility.Collapsed;
        PeopleTabButton.BorderThickness = new Thickness(0, 0, 0, _activitySelected ? 0 : 2);
        ActivityTabButton.BorderThickness = new Thickness(0, 0, 0, _activitySelected ? 2 : 0);
        ActivityTabButton.BorderBrush = PeopleTabButton.BorderBrush;
        PeopleTabButton.Foreground = (Brush)Application.Current.Resources[_activitySelected ? "SecondaryTextBrush" : "PrimaryTextBrush"];
        ActivityTabButton.Foreground = (Brush)Application.Current.Resources[_activitySelected ? "PrimaryTextBrush" : "SecondaryTextBrush"];
    }
    private async void RailSheetButton_Click(object sender, RoutedEventArgs e)
    {
        var panel = new Grid { Width = Math.Min(384, Math.Max(220, XamlRoot.Size.Width - 64)), Height = Math.Max(200, XamlRoot.Size.Height * .7) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tabs = new Grid(); tabs.ColumnDefinitions.Add(new ColumnDefinition()); tabs.ColumnDefinitions.Add(new ColumnDefinition());
        var peopleButton = new Button { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Background = null, CornerRadius = new CornerRadius(0) };
        var activityButton = new Button { Height = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Background = null, CornerRadius = new CornerRadius(0) };
        Grid.SetColumn(activityButton, 1); tabs.Children.Add(peopleButton); tabs.Children.Add(activityButton); panel.Children.Add(tabs);
        var people = new StackPanel(); var activity = new StackPanel();
        var peopleScroll = new ScrollViewer { Content = people, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var activityScroll = new ScrollViewer { Content = activity, Visibility = Visibility.Collapsed, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(peopleScroll, 1); Grid.SetRow(activityScroll, 1); panel.Children.Add(peopleScroll); panel.Children.Add(activityScroll);
        void Refresh()
        {
            people.Children.Clear(); activity.Children.Clear();
            var members = ViewModel.Room?.Members ?? [];
            peopleButton.Content = PeopleTabContent(members.Count);
            activityButton.Content = T("web.watchTogether.rail.activity", "Activity");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(peopleButton, T("web.watchTogether.rail.people", "People"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(activityButton, T("web.watchTogether.rail.activity", "Activity"));
            foreach (var member in members) people.Children.Add(CreateMemberRow(member));
            if (members.Count == 0) people.Children.Add(new TextBlock { Text = T("web.watchTogether.rail.peopleEmpty", "No one here yet. Share the invite to fill the room."), FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(16, 24, 16, 24) });
            foreach (var entry in ActivityList.Children.OfType<TextBlock>().Take(80)) activity.Children.Add(new TextBlock { Text = entry.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 10, 16, 10) });
            if (activity.Children.Count == 0) activity.Children.Add(new TextBlock { Text = T("web.watchTogether.rail.activityEmpty", "Things people do in the room show up here."), FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(16, 24, 16, 24), Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        }
        void Select(bool showPeople)
        {
            peopleScroll.Visibility = showPeople ? Visibility.Visible : Visibility.Collapsed;
            activityScroll.Visibility = showPeople ? Visibility.Collapsed : Visibility.Visible;
            peopleButton.BorderThickness = new Thickness(0, 0, 0, showPeople ? 2 : 0);
            activityButton.BorderThickness = new Thickness(0, 0, 0, showPeople ? 0 : 2);
            peopleButton.BorderBrush = activityButton.BorderBrush = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            peopleButton.Foreground = (Brush)Application.Current.Resources[showPeople ? "PrimaryTextBrush" : "SecondaryTextBrush"];
            activityButton.Foreground = (Brush)Application.Current.Resources[showPeople ? "SecondaryTextBrush" : "PrimaryTextBrush"];
        }
        peopleButton.Click += (_, _) => Select(true); activityButton.Click += (_, _) => Select(false);
        Refresh(); Select(true);
        void RoomChanged(object? changed, System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == nameof(ViewModel.Room)) DispatcherQueue.TryEnqueue(Refresh); }
        ViewModel.PropertyChanged += RoomChanged;
        var sheet = new ContentDialog { XamlRoot = XamlRoot, Content = panel };
        LocalizeDialog(sheet, () =>
        {
            sheet.Title = T("web.watchTogether.rail.sheetTitle", "People and activity");
            sheet.CloseButtonText = T("web.ui.close", "Close");
            Refresh();
        });
        sheet.Title = T("web.watchTogether.rail.sheetTitle", "People and activity");
        sheet.CloseButtonText = T("web.ui.close", "Close");
        try { await sheet.ShowAsync(); }
        finally { ViewModel.PropertyChanged -= RoomChanged; }
    }

    private Grid CreateMemberRow(WatchTogetherRoomMember member)
    {
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 10, 16, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(MemberAvatar(member));
        var copy = new StackPanel { Spacing = 2 };
        var name = new Grid { ColumnSpacing = 8 }; name.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); name.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); name.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        name.Children.Add(new TextBlock { Text = member.DisplayName, FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxWidth = member.IsHost ? 128 : 190, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
        if (member.IsHost) { var badge = new Border { CornerRadius = new CornerRadius(10), BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1), Padding = new Thickness(6, 2, 6, 2), Child = new TextBlock { Text = Upper(T("watchParty.seat.host", "Host")), FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] } }; Grid.SetColumn(badge, 1); name.Children.Add(badge); }
        if (member.IsSelf) { var self = new TextBlock { Text = T("web.watchTogether.rail.you", "(you)"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] }; Grid.SetColumn(self, 2); name.Children.Add(self); }
        copy.Children.Add(name);
        var playing = ViewModel.Room?.Phase == "playing";
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (!playing && member.LobbyReady) status.Children.Add(WebUiIcon.Create("check", 12, new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153))));
        var playbackState = ViewModel.Room?.PlaybackState;
        status.Children.Add(new TextBlock { Text = playing
                ? playbackState == "playing" ? T("web.watchTogether.rail.stateWatching", "Watching") : playbackState == "waiting" ? T("web.watchTogether.rail.stateBuffering", "Buffering") : T("web.watchTogether.rail.statePaused", "Paused")
                : member.LobbyReady ? T("web.watchTogether.rail.stateReady", "Ready") : T("web.watchTogether.rail.stateConnected", "Connected"),
            FontSize = 12, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        copy.Children.Add(status);
        Grid.SetColumn(copy, 1); row.Children.Add(copy);
        var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(playing && ViewModel.Room?.PlaybackState == "waiting" ? Windows.UI.Color.FromArgb(255, 251, 191, 36) : Windows.UI.Color.FromArgb(255, 52, 211, 153)) };
        Grid.SetColumn(dot, 2); row.Children.Add(dot); return row;
    }

    private static FrameworkElement PeopleTabContent(int count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = T("web.watchTogether.rail.people", "People"), FontSize = 14, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        row.Children.Add(RoundedBadge.Apply(new Border { Name = "PeopleCountBadge", Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"], Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Count(count), FontSize = 10, LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } }, 999));
        return row;
    }

    private void UpdateMembers(IReadOnlyList<WatchTogetherRoomMember>? members)
    {
        MembersWrapPanel.Children.Clear();
        if (members == null || members.Count == 0)
        {
            PeopleTabButton.Content = PeopleTabContent(0);
            MembersWrapPanel.Children.Add(new TextBlock { Text = T("web.watchTogether.rail.peopleEmpty", "No one here yet. Share the invite to fill the room."), FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(16, 24, 16, 24) });
            UpdateRailTabs();
            return;
        }

        foreach (var member in members)
        {
            MembersWrapPanel.Children.Add(CreateMemberRow(member));
        }

        PeopleTabButton.Content = PeopleTabContent(members.Count);
        UpdateRailTabs();
    }

    private void UpdateConnectionUi()
    {
        switch (ViewModel.ConnectionState)
        {
            case "connected":
                StatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 211, 153));
                StatusText.Text = T("web.watchTogether.connection.connected", "Connected");
                break;
            case "connecting":
                StatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 252, 211, 77));
                StatusText.Text = T("web.watchTogether.connection.connecting", "Connecting…");
                break;
            default:
                StatusDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 103));
                StatusText.Text = T("watchParty.connection.disconnected", "Disconnected");
                break;
        }
    }

    private void UpdateClosedReasonUi()
    {
        var reason = ViewModel.ClosedReason;
        if (string.IsNullOrEmpty(reason)) return;
        Func<string> title = reason switch
        {
            "host_left" or "room_closed" => () => T("web.watchTogether.room.closed", "The room has ended."),
            "not_found" => () => T("web.watchTogether.common.roomNotFound", "Room not found."),
            _ => () => T("web.watchTogether.room.unavailable", "Room is unavailable."),
        };
        ShowTerminalState(
            title,
            () => T("web.watchTogether.room.closedBody", "Start a new watch party or join another room to keep watching together."),
            () => T("watchParty.page.start", "Start a party"));
    }

    private void UpdateSuggestionsUi()
    {
        int n = ViewModel.Suggestions.Count;
        SuggestionsPanel.Visibility = ViewModel.Room != null && (ViewModel.IsVoteMode || n > 0) ? Visibility.Visible : Visibility.Collapsed;
        SuggestionsSubtitle.Text = n == 0
            ? T("web.watchTogether.voting.noSuggestions", "No suggestions yet")
            : T("web.watchTogether.lobby.suggestionsCount", $"Suggestions · {n}", new Dictionary<string, object?> { ["count"] = Count(n) });
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

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var code = ViewModel.DisplayCode;
        try { var data = new DataPackage(); data.SetText(code); Clipboard.SetContent(data); ViewModel.SetLocalizedNotice(() => T("web.watchTogether.common.codeCopied", $"Room code {code} copied", new Dictionary<string, object?> { ["code"] = code })); }
        catch { ViewModel.SetLocalizedError(() => T("web.watchTogether.common.codeCopyFailed", "Could not copy the code")); }
    }
    private void ChangeStagedButton_Click(object sender, RoutedEventArgs e) => HostSearchBox.Focus(FocusState.Programmatic);
    private async void GuestPauseToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingPausePolicy && ViewModel.IsHost && GuestPauseToggle.IsOn != (ViewModel.Room?.GuestControlPolicy == "guest_play_pause")) await ViewModel.TogglePolicyCommand.ExecuteAsync(null);
    }

    private Border MemberAvatar(WatchTogetherRoomMember member, double size = 32)
    {
        var initials = string.Concat(member.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => word[0])).ToUpperInvariant();
        var tint = MemberTint(member.UserId, member.ProfileId);
        var pale = Windows.UI.Color.FromArgb(255, (byte)(tint.R + (255 - tint.R) * .65), (byte)(tint.G + (255 - tint.G) * .65), (byte)(tint.B + (255 - tint.B) * .65));
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(64, tint.R, tint.G, tint.B)), Child = new TextBlock { Text = initials, FontSize = size < 32 ? 12 : 14, Foreground = new SolidColorBrush(pale), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
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
                    Text = T("web.watchTogether.endDialog.description", "End the watch party for everyone?"),
                    TextWrapping = TextWrapping.Wrap,
                },
                errorText,
            },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Content = content,
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
        };
        var ending = false;
        void Words()
        {
            dialog.Title = T("web.watchTogether.endDialog.title", "End watch party?");
            ((TextBlock)content.Children[0]).Text = T("web.watchTogether.endDialog.description", "End the watch party for everyone?");
            // "Ending…" has no upstream key; English fallback while the request runs.
            dialog.PrimaryButtonText = ending ? "Ending…" : T("watchParty.end.confirm", "End Party");
            dialog.CloseButtonText = T("common.cancel", "Cancel");
            if (errorText.Visibility == Visibility.Visible) errorText.Text = ViewModel.ErrorMessage ?? T("web.watchTogether.actions.endRoomFailed", "Failed to end room");
        }
        Words();
        LocalizeDialog(dialog, Words);

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                ending = true; Words();
                errorText.Visibility = Visibility.Collapsed;
                await ViewModel.CloseRoomCommand.ExecuteAsync(null);
                if (ViewModel.ClosedReason == "host_left")
                {
                    args.Cancel = false;
                }
                else
                {
                    errorText.Visibility = Visibility.Visible;
                    ending = false; Words();
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
    {
        App.Services.GetRequiredService<WatchTogetherCoordinator>().ClearActiveRoom();
        ViewModel.Dispose();
        Frame?.Navigate(typeof(WatchTogetherJoinPage), null, PageTransitionHelper.ForPageChange());
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
        ClearSeriesPick();
        SearchEmptyState.Visibility = Visibility.Visible;
        HostSearchBox.Focus(FocusState.Programmatic);
        _ = RunHostSearchAsync();
    }

    private void SuggestButton_Click(object sender, RoutedEventArgs e)
    {
        HostSearchBox.StartBringIntoView();
        HostSearchBox.Focus(FocusState.Programmatic);
    }

    private async Task RunHostSearchAsync()
    {
        ClearSeriesPick();
        DrillDownPanel.Visibility = Visibility.Collapsed;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        _browseCts?.Cancel(); _browseCts?.Dispose();
        var ct = (_browseCts = new CancellationTokenSource()).Token;
        var revision = ++_browseRevision;
        _browseRoomId = ViewModel.RoomId; _browseRoomToken = ViewModel.RoomToken;
        _browseContext = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>().CaptureContext();
        var query = HostSearchBox.Text?.Trim();
        var shelf = ShelfPicker?.SelectedIndex ?? 0;
        _shelfMode = string.IsNullOrWhiteSpace(query);
        SearchEmptyIcon.Visibility = _shelfMode ? Visibility.Collapsed : Visibility.Visible;
        BrowseShelfRows.Children.Clear();
        ForgetLocalized("shelves");
        _memberDots.Clear(); _memberStates.Clear(); _nextUp.Clear();
        BrowseShelfRows.Visibility = _shelfMode ? Visibility.Visible : Visibility.Collapsed;
        HostSearchResults.Visibility = _shelfMode ? Visibility.Collapsed : Visibility.Visible;
        SearchEmptyState.Visibility = Visibility.Collapsed;
        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            if (_shelfMode)
            {
                await LoadBrowseShelvesAsync(shelf, revision, ct);
                return;
            }
            var result = await catalogApi.SearchAsync(query!, limit: 30, ct: ct);
            if (revision != _browseRevision || ct.IsCancellationRequested || !HasCurrentBrowseState()) return;
            var items = result?.Items.Where(item => item.Type is "movie" or "series").Where(item => shelf != 1 || item.Type == "movie").Where(item => shelf != 2 || item.Type == "series").ToList() ?? [];
            HostSearchResults.ItemsSource = items;
            await LoadMemberStatesAsync(items.Select(i => i.ContentId), revision, ct);
            SearchEmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count == 0) LocalizeText(SearchEmptyText, () => T("web.watchTogether.browse.noMatches", $"No matches for “{query}”. Try another title, or clear the filter.", new Dictionary<string, object?> { ["query"] = query }));
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (revision != _browseRevision || !HasCurrentBrowseState()) return;
            HostSearchResults.ItemsSource = null;
            SearchEmptyState.Visibility = Visibility.Visible;
            LocalizeText(SearchEmptyText, () => T("web.watchTogether.browse.searchFailed", "Could not search."));
        }
    }

    private async Task LoadBrowseShelvesAsync(int shelf, long revision, CancellationToken ct)
    {
        var rows = new List<(Func<string> Title, List<SiloPlayer.Core.Models.Home.MediaItem> Items, bool Together)>();
        var catalog = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
        var home = App.Services.GetRequiredService<SiloPlayer.Core.Api.HomeApi>();
        var unavailable = false;
        async Task<T?> ReadShelfAsync<T>(Func<Task<T>> read) where T : class
        {
            try { return await read(); }
            catch (OperationCanceledException) { throw; }
            catch { unavailable = true; return null; }
        }
        if (shelf is 0 or 3 or 4 && ViewModel.Capabilities.Picker && ViewModel.RoomId != null && ViewModel.RoomToken != null)
        {
            var picker = await ReadShelfAsync(() => App.Services.GetRequiredService<SiloPlayer.Core.Api.PlaybackApi>().GetWatchTogetherPickerAsync(ViewModel.RoomId, ViewModel.RoomToken, ct));
            if (revision != _browseRevision || ct.IsCancellationRequested) return;
            if (picker != null)
            {
            foreach (var entry in picker.ContinueTogether.Where(entry => entry.NextUp != null)) _nextUp[entry.Item.ContentId] = entry.NextUp!;
            if (shelf is 0 or 3) rows.Add((() => T("web.watchTogether.browse.togetherTitle", "Continue watching together"), picker.ContinueTogether.Select(entry => entry.Item).Take(24).ToList(), true));
            if (shelf is 0 or 4) rows.Add((() => T("web.watchTogether.browse.watchlistsTitle", "On your watchlists"), picker.WatchlistUnion.Select(entry => entry.Item).Take(24).ToList(), false));
            }
        }
        if (shelf is 0 or 1 or 2)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "recently_added", "recently_released", "new_to_library", "continue_watching", "next_up", "next_in_series", "watchlist", "favorites", "profile_activity_feed", "because_you_watched", "recommended_for_you", "similar_users_liked", "taste_match", "forgotten_favorites" };
            var layout = await ReadShelfAsync(() => home.GetLayoutAsync(ct));
            var discovery = layout?.Sections.Where(section => !excluded.Contains(section.SectionType)).Take(4).ToArray() ?? [];
            var sections = await Task.WhenAll(discovery.Select(section => ReadShelfAsync(() => home.GetSectionItemsAsync(section.Id, ct))));
            rows.AddRange(sections.Select(section => section?.Section).OfType<SiloPlayer.Core.Models.Home.HomeSectionWithItems>().Select(section => ((Func<string>)(() => section.DisplayTitle), section.Items.Where(item => item.Type is "movie" or "series").Where(item => shelf != 1 || item.Type == "movie").Where(item => shelf != 2 || item.Type == "series").Take(24).ToList(), false)));
        }
        if (shelf is 0 or 1 or 2 or 5)
        {
            var types = shelf == 1 ? new[] { "movie" } : shelf == 2 ? new[] { "series" } : new[] { "movie", "series" };
            var results = await Task.WhenAll(types.Select(type => ReadShelfAsync(() => catalog.GetCatalogAsync(null, type: type, sort: "added_at", order: "desc", limit: 24, includeTotal: false, ct: ct))));
            for (var index = 0; index < results.Length; index++) if (results[index] is { } result) { var movie = types[index] == "movie"; rows.Add((() => movie ? T("web.watchTogether.browse.recentMovies", "Recently added movies") : T("web.watchTogether.browse.recentSeries", "Recently added series"), result.Items.ToList(), false)); }
        }
        if (revision != _browseRevision || ct.IsCancellationRequested || !HasCurrentBrowseState()) return;
        foreach (var row in rows.Where(row => row.Items.Count > 0)) BuildBrowseShelf(row.Title, row.Items, row.Together);
        SearchEmptyState.Visibility = unavailable || BrowseShelfRows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchEmptyIcon.Visibility = Visibility.Collapsed;
        // Partial-failure retry wording has no upstream key; English fallback.
        LocalizeText(SearchEmptyText, () => unavailable ? "Some shelves are unavailable. Select the shelf again to retry." : shelf switch
        {
            1 => T("web.watchTogether.browse.emptyMovies", "No movies in the library yet. Search for one above."),
            2 => T("web.watchTogether.browse.emptySeries", "No series in the library yet. Search for one above."),
            3 => T("web.watchTogether.browse.emptyTogether", "Nothing in progress for two or more of you yet."),
            4 => T("web.watchTogether.browse.emptyWatchlists", "Nobody in the room has anything on their watchlist."),
            5 => T("web.watchTogether.browse.emptyRecent", "Nothing added recently."),
            _ => T("web.watchTogether.browse.emptyAll", "Nothing to browse yet. Search for something above.")
        });
        await LoadMemberStatesAsync(rows.SelectMany(row => row.Items).Select(item => item.ContentId), revision, ct);
    }

    private void BuildBrowseShelf(Func<string> title, List<SiloPlayer.Core.Models.Home.MediaItem> items, bool together)
    {
        var shelf = new StackPanel { Spacing = 12 };
        var heading = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        LocalizeText(heading, title, "shelves");
        shelf.Children.Add(heading);
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var item in items)
        {
            var body = new StackPanel { Spacing = 6, Width = 140 };
            var card = new SiloPlayer.Controls.PosterCard { MediaItem = item, Width = 140 };
            card.SetCatalogGridLayout(140);
            body.Children.Add(card);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Tag = item.ContentId };
            RegisterMemberDots(dots);
            body.Children.Add(dots);
            var button = new Button { Tag = item, Content = body, Padding = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
            button.Click += async (_, _) => await SelectBrowseItemAsync(item,
                together && _nextUp.TryGetValue(item.ContentId, out var next) ? next.SeasonNumber : null);
            cards.Children.Add(button);
        }
        shelf.Children.Add(new ScrollViewer { Content = cards, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        BrowseShelfRows.Children.Add(shelf);
    }

    private async Task LoadMemberStatesAsync(IEnumerable<string> ids, long revision, CancellationToken ct)
    {
        if (!ViewModel.Capabilities.MemberState || ViewModel.RoomId == null || ViewModel.RoomToken == null) return;
        var roomId = ViewModel.RoomId; var roomToken = ViewModel.RoomToken;
        try
        {
            foreach (var batch in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Order().Chunk(200))
            {
                var result = await App.Services.GetRequiredService<SiloPlayer.Core.Api.PlaybackApi>().GetWatchTogetherMemberStateAsync(roomId, roomToken, batch, ct);
                if (revision != _browseRevision || ct.IsCancellationRequested || !HasCurrentBrowseState()) return;
                _stateMembers = result.Members;
                foreach (var item in result.Items) _memberStates[item.ContentId] = item;
            }
            foreach (var panel in _memberDots.Values.SelectMany(panels => panels)) DrawMemberDots(panel);
        }
        catch (OperationCanceledException) { }
        catch { /* Watch-state decorations must not hide browse results. */ }
    }
    private void MemberDots_Loaded(object sender, RoutedEventArgs e) { if (sender is StackPanel panel) RegisterMemberDots(panel); }
    private void RegisterMemberDots(StackPanel panel)
    {
        if (panel.Tag is not string id) return;
        if (!_memberDots.TryGetValue(id, out var panels)) _memberDots[id] = panels = [];
        if (!panels.Contains(panel)) panels.Add(panel);
        DrawMemberDots(panel);
    }
    private void DrawMemberDots(StackPanel panel)
    {
        panel.Children.Clear();
        if (panel.Tag is not string id || !_memberStates.TryGetValue(id, out var item)) return;
        foreach (var identity in _stateMembers)
        {
            var member = item.Members.FirstOrDefault(member => member.UserId == identity.UserId && member.ProfileId == identity.ProfileId);
            var state = member?.State ?? "unseen";
            var color = new SolidColorBrush(MemberTint(identity.UserId, identity.ProfileId));
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(state == "unseen" ? 1 : 0), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 255, 255, 255)), Background = state == "watched" ? color : new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            if (state == "in_progress") dot.Child = new Border { Width = 4, HorizontalAlignment = HorizontalAlignment.Left, Background = color, CornerRadius = new CornerRadius(4, 0, 0, 4) };
            var stateText = MemberStateText(state);
            var nameArguments = new Dictionary<string, object?> { ["name"] = identity.DisplayName, ["state"] = stateText };
            ToolTipService.SetToolTip(dot, T("web.watchTogether.dots.memberTitle", $"{identity.DisplayName}: {stateText}", nameArguments)
                + (member?.OnWatchlist == true ? " · " + T("web.watchTogether.movie.onWatchlist", "On their watchlist") : ""));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dot, T("web.watchTogether.dots.memberLabel", $"{identity.DisplayName} {stateText}", nameArguments));
            panel.Children.Add(dot);
        }
    }
    private Windows.UI.Color MemberTint(int userId, string profileId)
    {
        var members = ViewModel.Room?.Members ?? _stateMembers;
        var index = members.FindIndex(member => member.UserId == userId && member.ProfileId == profileId);
        Windows.UI.Color[] palette = [Windows.UI.Color.FromArgb(255, 129, 140, 248), Windows.UI.Color.FromArgb(255, 251, 113, 133), Windows.UI.Color.FromArgb(255, 45, 212, 191), Windows.UI.Color.FromArgb(255, 251, 191, 36), Windows.UI.Color.FromArgb(255, 167, 139, 250), Windows.UI.Color.FromArgb(255, 56, 189, 248), Windows.UI.Color.FromArgb(255, 163, 230, 53), Windows.UI.Color.FromArgb(255, 251, 146, 60)];
        return palette[Math.Max(0, index) % palette.Length];
    }
    private void SetBrowseResultsVisible(bool visible)
    {
        HostSearchResults.Visibility = visible && !_shelfMode ? Visibility.Visible : Visibility.Collapsed;
        BrowseShelfRows.Visibility = visible && _shelfMode ? Visibility.Visible : Visibility.Collapsed;
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
    private int? _drillDownInitialSeason;
    private List<SiloPlayer.Core.Models.Catalog.Episode> _drillDownEpisodes = [];

    /// <summary>
    /// Called when a search result is selected. Movies go directly to candidate spotlight.
    /// Series drill down into seasons → episodes.
    /// </summary>
    public async void OnSearchResultSelected(SiloPlayer.Core.Models.Home.MediaItem item)
        => await SelectBrowseItemAsync(item, null);

    private async Task SelectBrowseItemAsync(SiloPlayer.Core.Models.Home.MediaItem item, int? initialSeason)
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
            _drillDownInitialSeason = initialSeason;
            await ShowSeasonsAsync(item);
        }
    }

    private async Task ShowSeasonsAsync(SiloPlayer.Core.Models.Home.MediaItem series)
        => await ShowSeasonsCoreAsync(series, chooseSeason: true);

    private async Task ShowSeasonsCoreAsync(SiloPlayer.Core.Models.Home.MediaItem series, bool chooseSeason)
    {
        SetBrowseResultsVisible(false);
        DrillDownPanel.Visibility = Visibility.Visible;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        ClearSeriesPick();
        _pickerSeasonNumber = null;
        _pickerNextUpId = null;
        PickerSeasons.Children.Clear();
        ForgetLocalized("seasons");
        SetSeriesPickerHeader(series, null);
        LocalizeText(DrillDownTitle, () => series.Title);
        DrillDownSubtitle.Text = "Pick a season";
        DrillDownSubtitle.Visibility = Visibility.Collapsed;
        DrillDownBackText.Text = "Back to results";
        _showingEpisodes = false;
        _drillDownEpisodes.Clear();
        DrillDownItems.ItemsSource = null;
        DrillDownItems.Layout = new UniformGridLayout { MinItemWidth = 220, MinItemHeight = 76, MinColumnSpacing = 10, MinRowSpacing = 10, ItemsStretch = UniformGridLayoutItemsStretch.Fill };
        SetDrillDownPending(true);

        _browseCts?.Cancel(); _browseCts?.Dispose();
        var ct = (_browseCts = new CancellationTokenSource()).Token;
        var revision = ++_browseRevision;
        CapturePickerAuthority();

        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var seasonsTask = catalogApi.GetSeasonsAsync(series.ContentId, ct);
            // The fresh detail carries play_season_number. A prior season-list
            // or continue-together target must not silently choose for search.
            var detailTask = chooseSeason && !_drillDownInitialSeason.HasValue
                ? ReadPickerSeriesDetailAsync(catalogApi, series.ContentId, ct)
                : Task.FromResult<SiloPlayer.Core.Models.Catalog.MediaItemDetail?>(null);
            await Task.WhenAll(seasonsTask, detailTask);
            if (revision != _browseRevision || ct.IsCancellationRequested || !HasCurrentBrowseState()) return;
            var seasonsResp = await seasonsTask;
            var detail = await detailTask;
            SetSeriesPickerHeader(series, detail, seasonsResp.Seasons.Count);
            BuildPickerSeasons(seasonsResp.Seasons);
            if (_drillDownInitialSeason.HasValue) _ = RefreshSeriesPickerHeaderAsync(series, seasonsResp.Seasons.Count);
            // Display-only season rows: a server season title is shown as sent; generated labels follow the language.
            List<SiloPlayer.Core.Models.Home.MediaItem> SeasonItems() => seasonsResp.Seasons.Select(s => new SiloPlayer.Core.Models.Home.MediaItem
            {
                ContentId = s.ContentId,
                Title = !string.IsNullOrWhiteSpace(s.Title) ? s.Title : SeasonLabel(s.SeasonNumber),
                Type = "season",
                PosterUrl = s.PosterUrl,
                Overview = EpisodeCountText(s.EpisodeCount),
                Year = s.SeasonNumber,
            }).ToList();
            var seasonItems = SeasonItems();
            DrillDownItems.ItemsSource = seasonItems;
            Localize(DrillDownItems, "seasons", () => { if (!_showingEpisodes && DrillDownItems.ItemsSource is List<SiloPlayer.Core.Models.Home.MediaItem> { Count: > 0 } shown && shown[0].Type == "season") DrillDownItems.ItemsSource = SeasonItems(); });
            SetDrillDownPending(false);
            if (chooseSeason)
            {
                var number = SiloPlayer.Core.Services.WatchPartyPickerPolicy.ResolveSeason(seasonsResp.Seasons, _drillDownInitialSeason, detail?.PlaySeasonNumber);
                var season = seasonItems.FirstOrDefault(item => item.Year == number);
                if (season == null && number.HasValue)
                    season = new() { Type = "season", Year = number.Value, Title = SeasonLabel(number.Value) };
                if (season != null) await ShowEpisodesAsync(season);
                else DrillDownEmptyText.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (revision != _browseRevision || ct.IsCancellationRequested || !HasCurrentBrowseState()) return;
            DrillDownItems.ItemsSource = null; SetDrillDownPending(false); DrillDownEmptyText.Visibility = Visibility.Visible;
        }
    }

    private async Task ShowEpisodesAsync(SiloPlayer.Core.Models.Home.MediaItem season)
    {
        if (string.IsNullOrWhiteSpace(_drillDownSeriesId)) return;
        _pickerSeasonNumber = season.Year;
        UpdatePickerSeasonButtons();
        if (_seriesPicked != null) { ++_candidateStateRevision; _candidateStateLoading = true; CandidatePlayBtn.IsEnabled = false; UpdatePickerConfirmation(); }
        _pickerNextUpId = null;
        _pickerRows.Clear();
        ForgetLocalized("episodes");
        _pickerNextCaptions.Clear();
        _pickerStills.Clear();
        _pickerActions.Clear();
        LocalizeText(DrillDownTitle, () => season.Title);
        DrillDownSubtitle.Visibility = Visibility.Collapsed;
        DrillDownBackText.Text = "Back to seasons";
        _showingEpisodes = true;
        _drillDownEpisodes.Clear();
        _drillDownMemberStateTask = Task.CompletedTask;
        DrillDownItems.ItemsSource = null;
        DrillDownItems.Layout = new StackLayout { Orientation = Orientation.Vertical, Spacing = 4 };
        SetDrillDownPending(true);
        _browseCts?.Cancel(); _browseCts?.Dispose();
        var ct = (_browseCts = new CancellationTokenSource()).Token;
        var revision = ++_browseRevision;
        CapturePickerAuthority();
        try
        {
            var seasonNumber = season.Year;
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var response = await catalogApi.GetEpisodesAsync(_drillDownSeriesId, seasonNumber, ct);
            if (revision != _browseRevision || ct.IsCancellationRequested || !HasCurrentBrowseState()) return;
            _drillDownEpisodes = SiloPlayer.Core.Services.WatchPartyPickerPolicy.PlayableEpisodes(response.Episodes);
            var items = _drillDownEpisodes.Select(ep => new SiloPlayer.Core.Models.Home.MediaItem
            {
                ContentId = ep.ContentId, Title = ep.Title, Type = "episode", Overview = ep.Overview ?? "", PosterUrl = ep.StillUrl, PosterThumbhash = ep.StillThumbhash,
                PosterIsEpisodeStill = ep.StillIsEpisodeStill, BackdropIsEpisodeStill = ep.StillIsEpisodeStill,
                UserState = new() { Played = ep.UserData?.Played == true, IsInProgress = ep.UserData?.IsInProgress == true }, PositionSeconds = ep.UserData?.PositionSeconds,
                BackdropUrl = ep.StillUrl, BackdropThumbhash = ep.StillThumbhash, SeriesId = _drillDownSeriesId, SeriesTitle = _drillDownSeries?.Title,
                SeasonNumber = seasonNumber, EpisodeNumber = ep.EpisodeNumber, Runtime = ep.Runtime,
                Year = DateTime.TryParse(ep.AirDate, out var airDate) ? airDate.Year : 0
            }).ToList();
            DrillDownItems.ItemsSource = items;
            var episodeCount = items.Count;
            LocalizeText(DrillDownTitle, () => Upper(SeasonLabel(seasonNumber)) + (episodeCount > 0 ? " · " + Upper(EpisodeCountText(episodeCount)) : ""));
            _drillDownMemberStateTask = LoadMemberStatesAsync(items.Select(item => item.ContentId), revision, ct);
            SetDrillDownPending(false);
            DrillDownEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            await _drillDownMemberStateTask;
            if (revision == _browseRevision && !ct.IsCancellationRequested && HasCurrentBrowseState())
            {
                UpdatePickerNextUp();
                if (_seriesPicked is { } picked) await LoadCandidateMemberStateAsync(picked);
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (revision != _browseRevision || ct.IsCancellationRequested || !HasCurrentBrowseState()) return;
            DrillDownItems.ItemsSource = null; SetDrillDownPending(false); DrillDownEmptyText.Visibility = Visibility.Visible;
            if (_seriesPicked is { } picked) await LoadCandidateMemberStateAsync(picked);
        }
    }

    private async void DrillDownResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SiloPlayer.Core.Models.Home.MediaItem item }) return;
        if (item.Type == "season") await ShowEpisodesAsync(item);
        else ShowSeriesEpisodePick(item);
    }

    private void DrillDownBack_Click(object sender, RoutedEventArgs e)
    {
        if (_showingEpisodes && _drillDownSeries != null)
        {
            _ = ShowSeasonsCoreAsync(_drillDownSeries, chooseSeason: false);
            return;
        }
        DrillDownPanel.Visibility = Visibility.Collapsed;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        _browseCts?.Cancel(); ++_browseRevision;
        SetBrowseResultsVisible(true);
    }

    private void ShowCandidateSpotlight(SiloPlayer.Core.Models.Home.MediaItem item)
    {
        ClearSeriesPick();
        SetBrowseResultsVisible(false);
        DrillDownPanel.Visibility = Visibility.Collapsed;
        CandidateSpotlight.Visibility = Visibility.Visible;

        CandidateTitle.Text = item.Title ?? "";
        CandidateMeta.Text = item.Type == "episode" && item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue
            ? $"{item.SeriesTitle} · S{item.SeasonNumber} E{item.EpisodeNumber}" : $"{item.Year}  ·  {item.Type}";
        CandidateOverview.Text = item.Overview ?? "";
        CandidatePlayBtn.Tag = item;
        _ = LoadCandidateMemberStateAsync(item);
        _ = LoadCandidateRatingsAsync(item);
        SetImageSource(CandidatePoster, item.PosterUrl);
        SetImageSource(CandidateBackdrop, item.BackdropUrl);
    }

    private long _candidateDetailRevision;
    private async Task LoadCandidateRatingsAsync(SiloPlayer.Core.Models.Home.MediaItem item)
    {
        var revision = ++_candidateDetailRevision;
        var client = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
        var authority = client.CaptureContext();
        CandidateRatings.Children.Clear(); CandidateRatings.Visibility = Visibility.Collapsed;
        try
        {
            var detail = await App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>().GetItemDetailAsync(item.ContentId);
            if (revision != _candidateDetailRevision || !client.IsCurrentContext(authority) || !ReferenceEquals(CandidatePlayBtn.Tag, item)
                || CandidateSpotlight.Visibility != Visibility.Visible || !IsLoaded) return;
            foreach (var rating in detail.Ratings ?? [])
                CandidateRatings.Children.Add(RoundedBadge.Apply(new Border
                {
                    Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
                    Padding = new Thickness(8, 2, 8, 2),
                    Child = SiloPlayer.Controls.DisplayRatingEntry.Create(rating, small: true, foreground: (Brush)Application.Current.Resources["SecondaryTextBrush"]),
                }, 999));
            CandidateRatings.Visibility = CandidateRatings.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { /* Ratings are optional; preserve the real selection and readiness flow. */ }
    }

    private async Task LoadCandidateMemberStateAsync(SiloPlayer.Core.Models.Home.MediaItem item)
    {
        var revision = ++_candidateStateRevision;
        var client = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
        var authority = client.CaptureContext(); var roomId = ViewModel.RoomId; var roomToken = ViewModel.RoomToken;
        bool Current() => revision == _candidateStateRevision && ReferenceEquals(CandidatePlayBtn.Tag, item)
            && IsCurrentCandidateVisible(item) && ViewModel.RoomId == roomId && ViewModel.RoomToken == roomToken && client.IsCurrentContext(authority);
        LocalizeText(CandidateMemberState, () => "");
        _candidateSpoilerRisk = false;
        _candidateStateLoading = false;
        CandidatePlayBtn.IsEnabled = !ViewModel.IsBusy;
        UpdatePickerConfirmation();
        if (!ViewModel.Capabilities.MemberState || ViewModel.RoomId == null || ViewModel.RoomToken == null) return;
        _candidateStateLoading = true; CandidatePlayBtn.IsEnabled = false;
        UpdatePickerConfirmation();
        try
        {
            await _drillDownMemberStateTask;
            if (!Current()) return;
            var state = await App.Services.GetRequiredService<SiloPlayer.Core.Api.PlaybackApi>().GetWatchTogetherMemberStateAsync(roomId!, roomToken!, [item.ContentId]);
            if (!Current()) return;
            foreach (var value in state.Items) _memberStates[value.ContentId] = value;
            // Member names are data; each state and the risk sentence are keyed.
            var memberStates = state.Items.SelectMany(i => i.Members)
                .Select(m => (Name: state.Members.FirstOrDefault(person => person.UserId == m.UserId && person.ProfileId == m.ProfileId)?.DisplayName, m.State)).ToList();
            var picked = _drillDownEpisodes.FirstOrDefault(episode => episode.ContentId == item.ContentId);
            var risk = picked == null ? null : SiloPlayer.Core.Services.WatchPartyPickerPolicy.FindSpoilerRisk(picked, _drillDownEpisodes, ViewModel.Room?.Members ?? _stateMembers, _memberStates);
            _candidateSpoilerRisk = risk != null;
            LocalizeText(CandidateMemberState, () => string.Join("  ·  ", memberStates.Select(m =>
            {
                var name = m.Name ?? T("web.watchTogether.common.someone", "Someone");
                var stateText = MemberStateText(m.State);
                return T("web.watchTogether.dots.memberTitle", $"{name}: {stateText}", new Dictionary<string, object?> { ["name"] = name, ["state"] = stateText });
            })) + (risk != null ? "\n" + SpoilerRiskText(risk) : ""));
        }
        catch { if (Current()) LocalizeText(CandidateMemberState, () => "Member watch state unavailable. Retry selection to check again."); } // No upstream key; English fallback.
        finally { if (Current()) { _candidateStateLoading = false; CandidatePlayBtn.IsEnabled = !ViewModel.IsBusy; UpdatePickerConfirmation(); } }
    }

    private async void CandidatePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SiloPlayer.Core.Models.Home.MediaItem candidate }) return;
        var contentId = candidate.ContentId;
        if (string.IsNullOrEmpty(ViewModel.RoomId)) return;
        var client = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
        var authority = client.CaptureContext(); var roomId = ViewModel.RoomId; var roomToken = ViewModel.RoomToken;
        var revision = _candidateStateRevision;
        bool Current() => revision == _candidateStateRevision && ReferenceEquals(CandidatePlayBtn.Tag, candidate) && IsCurrentCandidateVisible(candidate)
            && ViewModel.RoomId == roomId && ViewModel.RoomToken == roomToken && client.IsCurrentContext(authority);

        if (ViewModel.IsBusy || _candidateStateLoading) return;
        if (_candidateSpoilerRisk && !IsSeriesPickerCandidate(candidate))
        {
            // The spoiler confirmation wording has no upstream keys; English fallbacks around the keyed member state.
            var confirmation = new ContentDialog { XamlRoot = XamlRoot, Title = "This episode may contain spoilers", PrimaryButtonText = "Queue episode", DefaultButton = ContentDialogButton.Close };
            LocalizeDialog(confirmation, () => { confirmation.Content = CandidateMemberState.Text + " Queue it anyway?"; confirmation.CloseButtonText = T("common.cancel", "Cancel"); });
            confirmation.Content = CandidateMemberState.Text + " Queue it anyway?"; confirmation.CloseButtonText = T("common.cancel", "Cancel");
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary || !Current()) return;
        }
        ViewModel.IsBusy = true; ViewModel.ErrorMessage = null; CandidatePlayBtn.IsEnabled = false;
        UpdatePickerConfirmation();
        var suggesting = (!ViewModel.IsHost || ViewModel.IsVoteMode) && !string.IsNullOrWhiteSpace(ViewModel.RoomToken);
        try
        {
            var playbackApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.PlaybackApi>();
            if (suggesting)
            {
                var contentType = candidate.Type == "episode" ? "episode" : "movie";
                var result = await playbackApi.CreateWatchTogetherSuggestionAsync(roomId, roomToken!, contentId, contentType, CandidateTitle.Text, CandidateMeta.Text, candidate.PosterUrl);
                if (!Current()) return;
                ViewModel.Suggestions.Clear();
                foreach (var suggestion in result.Suggestions) ViewModel.Suggestions.Add(suggestion);
            }
            else
            {
                var resp = ViewModel.IsPlaying
                    ? await playbackApi.SelectWatchTogetherRoomItemAsync(roomId, contentId)
                    : await playbackApi.StageWatchTogetherRoomItemAsync(roomId, contentId);
                if (!Current()) return;
                ViewModel.Room = resp.Room;
            }
            CandidateSpotlight.Visibility = Visibility.Collapsed;
            ClearSearchButton_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            if (Current()) ViewModel.SetLocalizedError(suggesting
                ? SiloPlayer.ViewModels.WatchPartyText.ActionFailure("web.watchTogether.common.suggestFailed", "Could not suggest that", ex)
                : SiloPlayer.ViewModels.WatchPartyText.ActionFailure("web.watchTogether.actions.stageFailed", "Could not stage that", ex));
        }
        finally
        {
            if (ViewModel.RoomId == roomId && ViewModel.RoomToken == roomToken)
            {
                ViewModel.IsBusy = false;
                CandidatePlayBtn.IsEnabled = !_candidateStateLoading;
                UpdatePickerConfirmation();
            }
        }
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor || ViewModel.IsBusy || !ViewModel.IsHost || ViewModel.Room?.Phase != "lobby" || !ViewModel.Capabilities.SelectionModeSwitch) return;
        var presenter = new Style { TargetType = typeof(MenuFlyoutPresenter) };
        presenter.Setters.Add(new Setter(FrameworkElement.WidthProperty, 224d));
        var menu = new MenuFlyout { MenuFlyoutPresenterStyle = presenter };
        // Detached menu items are keyed before the flyout opens.
        foreach (var (mode, label) in new[] { ("host_pick", ModeLabel(false)), ("vote", ModeLabel(true)) })
        {
            var choice = new MenuFlyoutItem { Text = label, IsEnabled = mode != ViewModel.Room.SelectionMode };
            choice.Click += async (_, _) => await ViewModel.ChangeSelectionModeAsync(mode);
            menu.Items.Add(choice);
        }
        menu.ShowAt(anchor);
    }
    private async void StartStagedButton_Click(object sender, RoutedEventArgs e) => await ViewModel.StartStagedAsync();
    private async void LobbyReadyButton_Click(object sender, RoutedEventArgs e) => await ViewModel.SetLobbyReadyAsync(ViewModel.Room?.Members.FirstOrDefault(m => m.IsSelf)?.LobbyReady != true);
    private async void ShelfPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (HostSearchBox != null) await RunHostSearchAsync(); }
    private void UpdateShelfChips()
    {
        foreach (var chip in ShelfChipsPanel.Children.OfType<Button>())
        {
            var selected = (int)chip.Tag == ShelfPicker.SelectedIndex;
            chip.Background = (Brush)Application.Current.Resources[selected ? "PrimaryTextBrush" : "SurfaceRaisedBrush"];
            chip.Foreground = (Brush)Application.Current.Resources[selected ? "AppBackgroundBrush" : "SecondaryTextBrush"];
            // "Selected filter" has no upstream key; English fallback for the selected state.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(chip, selected ? "Selected filter" : T("web.watchTogether.browse.filterLabel", "Filter"));
        }
    }

    private void CandidateBack_Click(object sender, RoutedEventArgs e)
    {
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        SetBrowseResultsVisible(true);
    }

    private async Task ResolveNowPlayingAsync(string contentId)
    {
        if (_nowPlayingContentId == contentId) return;
        _nowPlayingContentId = contentId;
        LocalizeText(NowPlayingTitle, () => T("web.watchTogether.hero.loading", "Loading…"));
        try
        {
            var item = await App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>().GetItemDetailAsync(contentId);
            if (_nowPlayingContentId == contentId)
            {
                LocalizeText(NowPlayingTitle, () => item.Title);
                LocalizeText(NowPlayingSubtitle, () => item.Type == "episode" ? T("web.watchTogether.startSheet.episode", "Episode") : T("web.watchTogether.common.movie", "Movie"));
                SetImageSource(NowPlayingBackdrop, item.BackdropUrl);
            }
        }
        catch { if (_nowPlayingContentId == contentId) LocalizeText(NowPlayingTitle, () => T("web.watchTogether.playing.eyebrow", "Now playing")); }
    }

    private static void SetImageSource(Image target, string? url)
    {
        target.Source = RemoteImageConverter.Convert(url ?? "", typeof(ImageSource), null!, "") as ImageSource;
    }

    private void UpdateInviteLayout(double width)
    {
        var items = StageInviteInfo.Children.OfType<FrameworkElement>().Where(item => item.Visibility == Visibility.Visible).ToArray();
        if (items.Length == 0 || items.Any(item => item.DesiredSize.Width <= 0) || GuestPausePanel.DesiredSize.Width <= 0) return;
        var infoWidth = items.Sum(item => item.DesiredSize.Width + item.Margin.Left + item.Margin.Right) + StageInviteInfo.HorizontalSpacing * (items.Length - 1);
        var interior = StageInvitePanel.ActualWidth > 0
            ? StageInvitePanel.ActualWidth - StageInvitePanel.Padding.Left - StageInvitePanel.Padding.Right - StageInvitePanel.BorderThickness.Left - StageInvitePanel.BorderThickness.Right
            : width - (width < 640 ? 32 : 40) - 34;
        var wraps = infoWidth + GuestPausePanel.DesiredSize.Width + StageInviteGrid.ColumnSpacing > interior + .5;
        StageInviteGrid.RowSpacing = wraps ? 12 : 0;
        Grid.SetColumnSpan(StageInviteInfo, wraps ? 2 : 1);
        Grid.SetRow(GuestPausePanel, wraps ? 1 : 0);
        Grid.SetColumn(GuestPausePanel, wraps ? 0 : 1);
        Grid.SetColumnSpan(GuestPausePanel, wraps ? 2 : 1);
        GuestPausePanel.HorizontalAlignment = HorizontalAlignment.Right;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        RailColumn.Width = new GridLength(width >= 768 ? 320 : 0);
        RoomRail.Visibility = width >= 768 ? Visibility.Visible : Visibility.Collapsed;
        RailSheetButton.Visibility = width < 768 ? Visibility.Visible : Visibility.Collapsed;
        _compactRailStrip.Visibility = RailSheetButton.Visibility;
        UpdateInviteLayout(width);
        PageShell.Padding = new Thickness(0);
        RoomWorkspace.Height = Math.Max(320, e.NewSize.Height);
        UpdateSeriesPickerLayout(DrillDownPanel.ActualWidth);
        _roomScroll.Padding = new Thickness(width < 640 ? 16 : 20, 16, width < 640 ? 16 : 20, 16);
        var showPoster = width >= 640;
        StagedPosterBorder.Visibility = showPoster ? Visibility.Visible : Visibility.Collapsed;
        StagedHeroContent.ColumnDefinitions[0].Width = new GridLength(showPoster ? 112 : 0);
        Grid.SetColumn(StagedHeroInfo, showPoster ? 1 : 0); Grid.SetColumnSpan(StagedHeroInfo, showPoster ? 1 : 2);
        StagedHeroContent.Padding = new Thickness(width < 640 ? 20 : 24);
        StagedTitle.FontSize = width < 640 ? 20 : 24;
        StagedTitle.LineHeight = width < 640 ? 28 : 32;
        StatusText.Visibility = width < 640 ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(ShelfChipsPanel, width >= 1280 ? 1 : 0); Grid.SetRow(ShelfChipsPanel, width >= 1280 ? 0 : 1);
        Grid.SetColumnSpan(ShelfChipsPanel, width >= 1280 ? 1 : 2);

        RoomStatusBar.Padding = new Thickness(width < 640 ? 16 : 20, 10, width < 640 ? 16 : 20, 10);
        Grid.SetRow(RoomHeaderActions, 0); Grid.SetColumn(RoomHeaderActions, 1);
        RoomHeaderActions.HorizontalAlignment = HorizontalAlignment.Right;
        _inviteLabel.Visibility = width >= 640 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var label in ((StackPanel)LeaveButton.Content).Children.OfType<TextBlock>()) label.Visibility = width < 640 ? Visibility.Collapsed : Visibility.Visible;
        var compactEnd = width < 640;
        Localize(EndButton, "content", () => EndButton.Content = compactEnd ? T("web.watchTogether.strip.end", "End") : T("web.watchTogether.strip.endRoom", "End room"));
        UpdateHeaderWidth();
        _modeOwner.Visibility = width >= 640 ? Visibility.Visible : Visibility.Collapsed;
        UpdateModeChip(width);

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
    private void UpdateHeaderWidth() => RoomHeaderSummary.MaxWidth = Math.Max(0, ActualWidth - (ActualWidth < 640 ? 32 : 40) - 8 - RoomHeaderActions.ActualWidth);
}
