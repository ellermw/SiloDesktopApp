using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Windows.UI;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SiloPlayer.Views;

public sealed partial class WatchTogetherRoomPage
{
    private long _candidateStateRevision;
    private Task _drillDownMemberStateTask = Task.CompletedTask;
    private MediaItem? _seriesPicked;
    private int? _pickerSeasonNumber;
    private string? _pickerNextUpId, _pickerScrolledNextId;
    private int _pickerNextUpCount;
    private bool _pickerScrollRequest;
    private bool _pickerScrollQueued;
    private readonly Dictionary<string, Border> _pickerRows = [];
    private readonly Dictionary<string, Border> _pickerStills = [];
    private readonly Dictionary<string, TextBlock> _pickerNextCaptions = [];
    private readonly Dictionary<string, Button> _pickerActions = [];

    private void InitializeSeriesPicker()
    {
        PickerConfirmation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        PickerConfirmation.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        DrillDownPanel.SizeChanged += (_, _) => UpdateSeriesPickerLayout(DrillDownPanel.ActualWidth);
        PickerSeriesHeader.SizeChanged += (_, _) => UpdateSeriesPickerLayout(DrillDownPanel.ActualWidth);
        PickerConfirmation.SizeChanged += (_, _) => UpdateSeriesPickerLayout(DrillDownPanel.ActualWidth);
        DrillDownItems.ElementPrepared += (_, args) =>
        {
            if (args.Element is Border { Tag: MediaItem item } row)
            { _pickerRows[item.ContentId] = row; UpdatePickerRows(); }
        };
        DrillDownItems.ElementClearing += (_, args) =>
        {
            if (args.Element is Border { Tag: MediaItem item } row && _pickerRows.GetValueOrDefault(item.ContentId) == row)
            {
                _pickerRows.Remove(item.ContentId); _pickerStills.Remove(item.ContentId);
                _pickerNextCaptions.Remove(item.ContentId); _pickerActions.Remove(item.ContentId);
            }
        };
    }

    private void SetSeriesPickerHeader(MediaItem series, MediaItemDetail? detail, int? count = null)
    {
        PickerSeriesTitle.Text = series.Title;
        var meta = new List<string>();
        var year = detail?.Year ?? series.Year;
        if (year > 0) meta.Add(year.ToString());
        if (!string.IsNullOrWhiteSpace(detail?.ContentRating)) meta.Add(detail.ContentRating);
        if (detail?.Genres.Count > 0) meta.Add(string.Join(", ", detail.Genres.Take(2)));
        if (count.HasValue) meta.Add($"{count} {(count == 1 ? "season" : "seasons")}");
        PickerSeriesMeta.Text = string.Join(" · ", meta);
        PickerSeriesOverview.Text = detail?.Overview ?? "";
        SetPickerPlaceholder(PickerSeriesPosterBorder, string.IsNullOrWhiteSpace(detail?.PosterThumbhash) ? series.PosterThumbhash : detail.PosterThumbhash);
        SetImageSource(PickerSeriesPoster, string.IsNullOrWhiteSpace(detail?.PosterUrl) ? series.PosterUrl : detail.PosterUrl);
        SetImageSource(PickerSeriesBackdrop, detail?.BackdropUrl);
    }

    private async Task RefreshSeriesPickerHeaderAsync(MediaItem series, int count)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var authority = client.CaptureContext(); var room = ViewModel.RoomId; var proof = ViewModel.RoomToken;
        var detail = await ReadPickerSeriesDetailAsync(App.Services.GetRequiredService<CatalogApi>(), series.ContentId, CancellationToken.None);
        if (ReferenceEquals(_drillDownSeries, series) && DrillDownPanel.Visibility == Visibility.Visible
            && ViewModel.RoomId == room && ViewModel.RoomToken == proof && client.IsCurrentContext(authority)) SetSeriesPickerHeader(series, detail, count);
    }

    private void BuildPickerSeasons(IEnumerable<Season> seasons)
    {
        PickerSeasons.Children.Clear();
        foreach (var season in seasons)
        {
            var label = season.SeasonNumber == 0 ? "Specials" : $"Season {season.SeasonNumber}";
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (season.UserData is { } data && data.WatchedCount + data.UnplayedCount is var total && total > 0)
            {
                var progress = data.WatchedCount == 0 && data.InProgressCount == 0 ? "unwatched"
                    : data.WatchedCount == total ? "all watched" : $"{data.WatchedCount} of {total} watched";
                text.Children.Add(new TextBlock { Text = "You · " + progress, FontSize = 10, Opacity = .7 });
            }
            var button = new Button { Tag = season.SeasonNumber, Content = text, Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(8), Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(button, label);
            button.Click += async (_, _) => await ShowEpisodesAsync(new MediaItem { Type = "season", Year = season.SeasonNumber, Title = label });
            PickerSeasons.Children.Add(button);
        }
        UpdatePickerSeasonButtons();
        UpdateSeriesPickerLayout(DrillDownPanel.ActualWidth);
    }

    private void UpdatePickerSeasonButtons()
    {
        foreach (var button in PickerSeasons.Children.OfType<Button>())
        {
            var active = button.Tag is int number && number == _pickerSeasonNumber;
            button.Background = active ? (Brush)Application.Current.Resources["PrimaryTextBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Foreground = (Brush)Application.Current.Resources[active ? "AppBackgroundBrush" : "SecondaryTextBrush"];
            AutomationProperties.SetItemStatus(button, active ? "Selected" : "");
        }
    }

    private void UpdateSeriesPickerLayout(double width)
    {
        var wide = width >= 672; // Source container @2xl, independent of the room width.
        PickerSeasons.Orientation = wide ? Orientation.Vertical : Orientation.Horizontal;
        PickerSeasonScroll.Width = wide ? 176 : double.NaN;
        PickerSeasonScroll.HorizontalScrollBarVisibility = wide ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PickerSeasonScroll.VerticalScrollBarVisibility = wide ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        PickerSeasonScroll.Padding = wide ? new Thickness(8, 12, 8, 12) : new Thickness(12, 8, 12, 8);
        Grid.SetRowSpan(PickerSeasonScroll, wide ? 2 : 1); Grid.SetColumnSpan(PickerSeasonScroll, wide ? 1 : 2);
        Grid.SetRow(PickerEpisodeArea, wide ? 0 : 1); Grid.SetColumn(PickerEpisodeArea, wide ? 1 : 0);
        Grid.SetRowSpan(PickerEpisodeArea, wide ? 2 : 1); Grid.SetColumnSpan(PickerEpisodeArea, wide ? 1 : 2);
        foreach (var button in PickerSeasons.Children.OfType<Button>()) button.HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        PickerSeriesPosterBorder.Width = width >= 576 ? 80 : 64;
        PickerSeriesPosterBorder.Height = PickerSeriesPosterBorder.Width * 1.5;
        PickerDotsLegend.Visibility = width >= 576 && (ViewModel.Room?.Members.Count ?? 0) > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var still in _pickerStills.Values)
        {
            still.Visibility = width >= 576 ? Visibility.Visible : Visibility.Collapsed;
            still.Width = width >= 768 ? 144 : 112; still.Height = still.Width * 9 / 16;
        }
        // The outer stage scroll must not expand the inner episode list without bound.
        var maximum = Math.Min(896, Math.Max(320, ActualHeight) * .78);
        DrillDownPanel.MaxHeight = maximum;
        PickerEpisodeScroll.MaxHeight = Math.Max(80, maximum - PickerSeriesHeader.ActualHeight
            - (PickerConfirmation.Visibility == Visibility.Visible ? PickerConfirmation.ActualHeight : 0) - (wide ? 0 : PickerSeasonScroll.ActualHeight) - 50);
        PickerSeasonScroll.MaxHeight = wide ? PickerEpisodeScroll.MaxHeight + 34 : double.PositiveInfinity;
        PickerConfirmation.RowDefinitions[1].Height = width < 500 ? GridLength.Auto : new GridLength(0);
        var pickText = (FrameworkElement)PickerConfirmation.Children[0];
        Grid.SetColumnSpan(pickText, width < 500 ? 3 : 1);
        foreach (var action in PickerConfirmation.Children.OfType<Button>()) Grid.SetRow(action, width < 500 ? 1 : 0);
        PickerConfirmation.RowSpacing = width < 500 ? 8 : 0;
        TryScrollPickerNextUp();
    }

    private bool IsSeriesPickerCandidate(MediaItem item) => ReferenceEquals(_seriesPicked, item) && DrillDownPanel.Visibility == Visibility.Visible && PickerConfirmation.Visibility == Visibility.Visible;
    private bool IsCurrentCandidateVisible(MediaItem item) => CandidateSpotlight.Visibility == Visibility.Visible || IsSeriesPickerCandidate(item);
    private void ClearSeriesPick()
    {
        _seriesPicked = null; ++_candidateStateRevision;
        PickerConfirmation.Visibility = Visibility.Collapsed;
        UpdatePickerRows();
    }

    private void ShowSeriesEpisodePick(MediaItem item)
    {
        if (!_showingEpisodes || item.Type != "episode") { ShowCandidateSpotlight(item); return; }
        _seriesPicked = item;
        CandidateSpotlight.Visibility = Visibility.Collapsed;
        CandidatePlayBtn.Tag = item; CandidateTitle.Text = item.Title;
        CandidateMeta.Text = $"{item.SeriesTitle} · S{item.SeasonNumber} E{item.EpisodeNumber}";
        PickerPickTitle.Text = $"S{item.SeasonNumber} E{item.EpisodeNumber} “{item.Title}”";
        PickerConfirmation.Visibility = Visibility.Visible;
        _ = LoadCandidateMemberStateAsync(item);
        UpdatePickerRows(); UpdateSeriesPickerLayout(DrillDownPanel.ActualWidth);
    }

    private void UpdatePickerConfirmation()
    {
        if (_seriesPicked is not { } item) return;
        PickerConfirmButton.IsEnabled = !ViewModel.IsBusy && !_candidateStateLoading;
        PickerConfirmButton.Content = ViewModel.IsBusy ? "Working…" : !ViewModel.IsHost || ViewModel.IsVoteMode ? "Suggest" : ViewModel.IsPlaying ? "Play now" : "Stage it";
        var picked = _drillDownEpisodes.FirstOrDefault(episode => episode.ContentId == item.ContentId);
        var risk = !_candidateStateLoading && picked != null ? WatchPartyPickerPolicy.FindSpoilerRisk(picked, _drillDownEpisodes, ViewModel.Room?.Members ?? _stateMembers, _memberStates) : null;
        PickerPickRisk.Text = _candidateStateLoading ? "Checking member watch state…" : risk != null
            ? $"{string.Join(" and ", risk.Names)} {(risk.Names.Count == 1 ? "hasn't" : "haven't")} seen E{risk.EpisodeNumber}. Heads up."
            : ViewModel.Room?.SelectedContentId is { } staged && staged != item.ContentId ? $"Replaces {StagedTitle.Text}" : "";
        PickerPickRisk.Foreground = (Brush)Application.Current.Resources[risk != null ? "WarningBrush" : "SecondaryTextBrush"];
    }

    private void CloseSeriesPicker_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        _browseCts?.Cancel(); ++_browseRevision; ClearSeriesPick();
        DrillDownPanel.Visibility = Visibility.Collapsed; CandidateSpotlight.Visibility = Visibility.Collapsed;
        SetBrowseResultsVisible(true);
    }
    private void SeriesConfirm_Click(object sender, RoutedEventArgs e) => CandidatePlay_Click(CandidatePlayBtn, e);

    private void EpisodeRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border { Tag: MediaItem item } row) return;
        _pickerRows[item.ContentId] = row;
        row.PointerEntered += (_, _) => { if (row.Tag is MediaItem current && _pickerActions.TryGetValue(current.ContentId, out var button)) button.Opacity = 1; };
        row.PointerExited += (_, _) => UpdatePickerRows();
        UpdatePickerRows();
    }
    private void EpisodeRow_SizeChanged(object sender, SizeChangedEventArgs e) => TryScrollPickerNextUp();
    private void EpisodeStill_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border { Tag: MediaItem item } still)
        { _pickerStills[item.ContentId] = still; SetPickerPlaceholder(still, item.PosterThumbhash); UpdateSeriesPickerLayout(DrillDownPanel.ActualWidth); }
    }
    private static void SetPickerPlaceholder(Border border, string? hash)
    {
        border.Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
        if (string.IsNullOrWhiteSpace(hash)) return;
        try
        {
            var decoded = ThumbhashDecoder.Decode(hash);
            var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);
            var bgra = new byte[decoded.Rgba.Length];
            for (var index = 0; index < bgra.Length; index += 4)
            { bgra[index] = decoded.Rgba[index + 2]; bgra[index + 1] = decoded.Rgba[index + 1]; bgra[index + 2] = decoded.Rgba[index]; bgra[index + 3] = decoded.Rgba[index + 3]; }
            System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.CopyTo(bgra, bitmap.PixelBuffer);
            bitmap.Invalidate(); border.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
        }
        catch { /* Optional malformed artwork must not interrupt selection. */ }
    }
    private void PickerImage_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image) return;
        var backdrop = ReferenceEquals(image, PickerSeriesBackdrop);
        var animation = new DoubleAnimation { From = 0, To = backdrop ? .3 : 1, Duration = new Duration(TimeSpan.FromMilliseconds(backdrop ? 700 : 300)) };
        Storyboard.SetTarget(animation, image); Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard(); storyboard.Children.Add(animation); storyboard.Begin();
    }
    private void EpisodeStill_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: MediaItem item }) { ShowSeriesEpisodePick(item); e.Handled = true; }
    }
    private void EpisodeNextUp_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock { Tag: MediaItem item } text) { _pickerNextCaptions[item.ContentId] = text; UpdatePickerRows(); }
    }
    private void EpisodeAction_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaItem item } button)
        {
            _pickerActions[item.ContentId] = button;
            button.GotFocus += (_, _) => button.Opacity = 1;
            button.LostFocus += (_, _) => UpdatePickerRows();
            UpdatePickerRows();
        }
    }
    private void UpdatePickerRows()
    {
        foreach (var (id, row) in _pickerRows)
        {
            var picked = _seriesPicked?.ContentId == id; var next = _pickerNextUpId == id;
            row.Background = new SolidColorBrush(Color.FromArgb(picked ? (byte)15 : next ? (byte)13 : (byte)0, 255, 255, 255));
            row.BorderBrush = picked ? (Brush)Application.Current.Resources["AccentBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        foreach (var (id, text) in _pickerNextCaptions)
        {
            text.Text = $"NEXT UP FOR {_pickerNextUpCount} OF YOU";
            text.Visibility = _pickerNextUpId == id ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var (id, button) in _pickerActions)
        {
            var next = _pickerNextUpId == id;
            button.Style = (Style)Application.Current.Resources[next ? "AccentButtonStyle" : "OutlineButtonStyle"];
            var label = !ViewModel.IsHost || ViewModel.IsVoteMode ? "Suggest" : ViewModel.IsPlaying ? "Play now" : "Stage it";
            if (next)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                content.Children.Add(SiloPlayer.Controls.WebUiIcon.Create("play", 12));
                content.Children.Add(new TextBlock { Text = label, FontSize = 12 }); button.Content = content;
            }
            else button.Content = label;
            // Reserve action width while revealing pointer-only secondary actions.
            button.Opacity = next || _seriesPicked?.ContentId == id || button.FocusState != FocusState.Unfocused ? 1 : 0;
            AutomationProperties.SetName(button, $"{label} S{_pickerSeasonNumber} E{(button.Tag as MediaItem)?.EpisodeNumber}");
        }
        TryScrollPickerNextUp();
    }

    private void TryScrollPickerNextUp()
    {
        if (_pickerScrollQueued || _pickerScrollRequest || _pickerNextUpId == null || _pickerNextUpId == _pickerScrolledNextId || !HasCurrentBrowseState() || !_showingEpisodes) return;
        // ElementPrepared/SizeChanged can run inside repeater layout. Realizing
        // another row there reenters layout and throws; scroll on the next turn.
        _pickerScrollQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _pickerScrollQueued = false;
            ScrollPickerNextUp();
        });
    }

    private void ScrollPickerNextUp()
    {
        if (_pickerScrollRequest || _pickerNextUpId is not { } id || id == _pickerScrolledNextId || !HasCurrentBrowseState() || !_showingEpisodes || PickerEpisodeScroll.ViewportHeight <= 0) return;
        if (!_pickerRows.TryGetValue(id, out var row) || !row.IsLoaded)
        {
            var index = _drillDownEpisodes.FindIndex(episode => episode.ContentId == id);
            if (index >= 0)
            {
                _pickerScrollRequest = true;
                try { DrillDownItems.GetOrCreateElement(index).StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = .5, AnimationDesired = false }); }
                finally { _pickerScrollRequest = false; }
            }
            return;
        }
        if (row.ActualHeight <= 0) return;
        var point = row.TransformToVisual(PickerEpisodeScroll).TransformPoint(new Windows.Foundation.Point());
        var offset = PickerEpisodeScroll.VerticalOffset + point.Y - (PickerEpisodeScroll.ViewportHeight - row.ActualHeight) / 2;
        if (PickerEpisodeScroll.ChangeView(null, Math.Max(0, offset), null, true)) _pickerScrolledNextId = id;
    }

    private void CapturePickerAuthority()
    {
        _browseRoomId = ViewModel.RoomId; _browseRoomToken = ViewModel.RoomToken;
        _browseContext = App.Services.GetRequiredService<SiloApiClient>().CaptureContext();
    }

    private void SetDrillDownPending(bool pending)
    {
        DrillDownLoading.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        DrillDownItems.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
        DrillDownEmptyText.Visibility = Visibility.Collapsed;
    }

    private static async Task<MediaItemDetail?> ReadPickerSeriesDetailAsync(CatalogApi api, string contentId, CancellationToken ct)
    {
        try { return await api.GetItemDetailAsync(contentId, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return null; } // A failed optional detail read uses normal first-season fallback.
    }

    private void UpdatePickerNextUp()
    {
        if (!_showingEpisodes) return;
        var next = _memberStates.Count == 0 ? null : WatchPartyPickerPolicy.NextUpForRoom(_drillDownEpisodes, ViewModel.Room?.Members ?? _stateMembers, _memberStates);
        _pickerNextUpId = next?.Episode.ContentId; _pickerNextUpCount = next?.Count ?? 0;
        DrillDownSubtitle.Text = next == null ? "" : $"Next up for {next.Count} of you · E{next.Episode.EpisodeNumber} {next.Episode.Title}";
        DrillDownSubtitle.Visibility = Visibility.Collapsed;
        UpdatePickerRows();
    }

    private async Task RefreshPickerMembersAsync()
    {
        // Membership refresh must not clear the selected title/season. Source
        // refreshes member-state for the current stage rather than reselecting.
        if (!HasCurrentBrowseState()) return;
        var revision = _browseRevision;
        bool Current() => revision == _browseRevision && HasCurrentBrowseState();
        if (CandidatePlayBtn.Tag is MediaItem item && IsCurrentCandidateVisible(item))
        {
            ++_candidateStateRevision;
            _candidateStateLoading = true;
            CandidatePlayBtn.IsEnabled = false;
            UpdatePickerConfirmation();
            _drillDownMemberStateTask = LoadMemberStatesAsync(_drillDownEpisodes.Select(episode => episode.ContentId), revision, _browseCts?.Token ?? CancellationToken.None);
            await _drillDownMemberStateTask;
            if (Current() && ReferenceEquals(CandidatePlayBtn.Tag, item)) { UpdatePickerNextUp(); await LoadCandidateMemberStateAsync(item); }
        }
        else if (DrillDownPanel.Visibility == Visibility.Visible)
        {
            _drillDownMemberStateTask = LoadMemberStatesAsync(_drillDownEpisodes.Select(episode => episode.ContentId), revision, _browseCts?.Token ?? CancellationToken.None);
            await _drillDownMemberStateTask;
            if (Current()) UpdatePickerNextUp();
        }
        else await RunHostSearchAsync();
    }

    private void EpisodeNumber_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock { Tag: MediaItem item } text) return;
        text.Text = item.Type == "episode" ? $"E{item.EpisodeNumber}" : "";
        text.Visibility = item.Type == "episode" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EpisodeRuntime_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock { Tag: MediaItem item } text) return;
        text.Text = item.Type == "episode" && item.Runtime > 0 ? $"{item.Runtime}m" : "";
        text.Visibility = text.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
