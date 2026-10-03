using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage : Page
{
    private static readonly UrlToImageSourceConverter RemoteImageConverter = new();
    private readonly UICustomizationService _uiCustomizationService;
    public ItemDetailViewModel ViewModel { get; }
    private CancellationTokenSource? _imageCts;
    private CancellationTokenSource? _navigationCts;
    private CancellationTokenSource? _translationCts;
    private bool _isTranslatingOverview;
    private bool _translateButtonMode;
    private WatchDetailResponse? _watchDetail;
    private FileVersion? _selectedVersion;
    private List<SubtitleEntry> _downloadedSubtitles = [];
    private bool _loadingDownloadedSubtitles;
    private string? _readerTargetContentId;
    private string? _requestSeasonsContentId;
    private int? _readerTargetFileId;
    private FrameworkElement? _mangaResumeRow;
    private readonly List<(Expander Control, string Label)> _mangaVolumes = [];
    private Expander? _stickyVolume;
    private Grid? _tvViewport, _tvCopyHost;
    private ScrollViewer? _tvNavigation, _tvCopyScroll, _tvActionsScroll;
    private StackPanel? _tvNavigationItems, _tvPinnedActions;
    private bool _seriesSeasonsResolved;
    private readonly Dictionary<FrameworkElement, int> _tvSectionIndexes = [];

    private uint? _siblingEpisodesDragPointerId;
    private double _siblingEpisodesDragStartX;
    private double _siblingEpisodesDragStartOffset;
    private bool _siblingEpisodesDragging;
    private uint? _detailCarouselDragPointerId;
    private ScrollViewer? _detailCarouselDragScroller;
    private double _detailCarouselDragStartX;
    private double _detailCarouselDragStartOffset;
    private bool _detailCarouselDragging;
    private int _pendingSiblingEpisodeIndex = -1;
    private readonly List<AudiobookChapterRow> _audiobookChapterRows = [];
    private bool _audiobookChaptersExpanded;
    private bool _audiobookChaptersLongestFirst;
    private const double SiblingEpisodesDragThreshold = 7d;
    private const double DetailCarouselDragThreshold = 7d;
    /// <summary>
    /// Pre-play audio track selection (Phase 2a). Null = auto (server picks based
    /// on effective_audio_track_index or default flag). Otherwise an explicit track
    /// index into <see cref="FileVersion.AudioTracks"/> that will be passed to
    /// <c>PlaybackManager.StartSessionAsync</c> so the initial stream uses it.
    /// </summary>
    private int? _selectedAudioTrackIndex;

    /// <summary>
    /// Pre-play subtitle selection (Phase 2b). Sentinels:
    ///   null = auto (let mpv pick default or server-resolved language)
    ///   -1   = off (no subtitles)
    ///   0+   = explicit embedded subtitle track index (0-based, into FileVersion.SubtitleTracks).
    /// Applied client-side via mpv's "sid" property before loadfile, rather than
    /// the server, since subtitles aren't transmuxed — they're picked live by mpv.
    /// </summary>
    private int? _selectedSubtitleIndex;
    private SubtitleTrackSignature? _selectedSubtitleSignature;
    private Services.PlayerService? _playerService;
    private FrameworkElement? _rootElement;

    public ItemDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<ItemDetailViewModel>();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        this.InitializeComponent();
        ComposeHeroLayout();
        UpdateThemeGradientColors();

        this.Loaded += OnPageLoaded;
        Unloaded += (_, _) => { SettingsViewModel.TitleArtPreferenceChanged -= OnTitleArtPreferenceChanged; ++_titleArtRevision; };
        ContentScroll.ViewChanged += (_, _) => UpdateMangaStickyHeader();
        MangaStickyHeader.SizeChanged += (_, _) => UpdateMangaStickyHeader();
    }

    private async Task LoadAdvisoryDisplayAsync(SiloPlayer.Core.Models.Catalog.MediaItemDetail item, CancellationToken ct)
    {
        if (item.AdvisoryAge is not > 0) return;
        try
        {
            var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.SettingsApi>();
            var setting = await api.GetEffectiveSettingsAsync(["catalog.show_advisory_age"], ct);
            if (ct.IsCancellationRequested || ViewModel.Item != item) return;
            if (setting.Settings.FirstOrDefault()?.EffectiveValue == "true")
            {
                AdvisoryAgeText.Text = $"{item.AdvisoryAge}+";
                AdvisoryAgeBadge.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch { /* Optional advisory display does not block details or alter access. */ }
    }

    private void EnsureTvViewport()
    {
        if (_tvViewport != null || ViewModel.Item?.Type is not ("series" or "season" or "episode")) return;
        _tvViewport = new Grid();
        _tvViewport.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _tvViewport.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _tvViewport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _tvViewport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        DetailPageFlow.Children.Remove(BackdropContainer);
        _tvViewport.Children.Add(BackdropContainer);
        _tvNavigationItems = new StackPanel { Spacing = 12 };
        _tvNavigationItems.SizeChanged += (_, _) =>
        {
            if (ActualWidth >= 1024 && ActualHeight >= 651 && ViewModel.Item?.Type == "series" && SeriesLayoutSeasonCount != 1)
                SizeTvViewport();
        };
        foreach (var section in new FrameworkElement[] { SeasonsSection, EpisodesSection, SiblingEpisodesSection })
            _tvSectionIndexes[section] = DetailContentPanel.Children.IndexOf(section);
        foreach (var section in new FrameworkElement[] { SeasonsSection, EpisodesSection, SiblingEpisodesSection })
        { DetailContentPanel.Children.Remove(section); _tvNavigationItems.Children.Add(section); }
        _tvNavigation = new ScrollViewer { Content = _tvNavigationItems, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(24, 12, 24, 12) };
        Grid.SetRow(_tvNavigation, 1); _tvViewport.Children.Add(_tvNavigation);
        DetailPageFlow.Children.Insert(0, _tvViewport);

        HeroContentGrid.Children.Remove(HeroInfoPanel);
        _tvCopyHost = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
        _tvCopyHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _tvCopyHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _tvCopyScroll = new ScrollViewer { Content = HeroInfoPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        HeroInfoPanel.VerticalAlignment = VerticalAlignment.Bottom;
        _tvCopyHost.Children.Add(_tvCopyScroll);
        _tvPinnedActions = new StackPanel { Spacing = 8 };
        HeroInfoPanel.Children.Remove(HeroActionsRow); HeroInfoPanel.Children.Remove(PlaybackOptionsRow);
        _tvPinnedActions.Children.Add(HeroActionsRow); _tvPinnedActions.Children.Add(PlaybackOptionsRow);
        _tvActionsScroll = new ScrollViewer { Content = _tvPinnedActions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 180 };
        Grid.SetRow(_tvActionsScroll, 1); _tvCopyHost.Children.Add(_tvActionsScroll); HeroContentGrid.Children.Add(_tvCopyHost);
    }
    // An empty collection before the companion request finishes means unknown,
    // not multi-season. Use the detail count for the first paint, then the
    // authoritative season result (including an empty result) once available.
    private int? SeriesLayoutSeasonCount => _seriesSeasonsResolved || ViewModel.Seasons.Count > 0
        ? ViewModel.Seasons.Count : ViewModel.Item?.SeasonCount;

    private void SizeTvViewport()
    {
        if (_tvViewport == null || _tvNavigation == null || _tvCopyHost == null || _tvCopyScroll == null || _tvActionsScroll == null) return;
        // A completed card prefetch can paint from OnNavigatedTo before Frame
        // attaches this page to a window. Keep that metadata paint synchronous;
        // Loaded and root SizeChanged will apply the window-dependent layout.
        if (XamlRoot is not { } root) return;
        var height = Math.Max(260, ActualHeight > 0 ? ActualHeight : root.Size.Height - 80);
        var narrow = ActualWidth < 1024;
        var landscape = !narrow && height <= 650 && ActualWidth > height;
        var naturalRail = !narrow && height >= 651 && ViewModel.Item?.Type == "series" && SeriesLayoutSeasonCount != 1;
        var naturalCopy = narrow || naturalRail;
        // The final mobile WebUI rules override the landscape viewport: copy
        // and actions determine the hero's height, then navigation follows it.
        _tvViewport.Height = naturalCopy ? double.NaN : height;
        _tvViewport.MinHeight = naturalRail ? height : 0;
        _tvViewport.RowDefinitions[0].Height = naturalCopy ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _tvViewport.ColumnDefinitions[1].Width = landscape ? new GridLength(35, GridUnitType.Star) : new GridLength(0);
        _tvViewport.ColumnDefinitions[0].Width = new GridLength(landscape ? 65 : 1, GridUnitType.Star);
        _tvViewport.RowDefinitions[1].Height = naturalCopy ? GridLength.Auto : landscape ? new GridLength(0) : new GridLength(Math.Min(height * .4, ViewModel.Item?.Type == "series" ? 352 : 288));
        Grid.SetRow(_tvNavigation, landscape ? 0 : 1); Grid.SetColumn(_tvNavigation, landscape ? 1 : 0);
        _tvNavigation.Height = landscape ? Math.Min(256, height) : double.NaN;
        _tvNavigation.VerticalAlignment = landscape ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        _tvNavigation.VerticalScrollBarVisibility = naturalCopy ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _tvNavigation.VerticalScrollMode = naturalCopy ? ScrollMode.Disabled : ScrollMode.Enabled;
        _tvNavigation.Padding = narrow ? new Thickness(ActualWidth < 640 ? 16 : 24, 12, ActualWidth < 640 ? 16 : 24, 0) : new Thickness(landscape ? 12 : 24, 12, landscape ? 12 : 24, 12);
        _tvCopyHost.RowDefinitions[0].Height = naturalCopy ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        _tvCopyHost.Padding = new Thickness(4);
        // The original information scroller reserves its scrollbar gutter
        // on desktop, including when the current copy does not overflow.
        _tvCopyScroll.Padding = new Thickness(0, 0, narrow ? 0 : 16, 0);
        _tvCopyScroll.VerticalScrollBarVisibility = naturalCopy ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _tvCopyScroll.VerticalScrollMode = naturalCopy ? ScrollMode.Disabled : ScrollMode.Enabled;
        _tvActionsScroll.VerticalScrollBarVisibility = naturalCopy ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _tvActionsScroll.VerticalScrollMode = naturalCopy ? ScrollMode.Disabled : ScrollMode.Enabled;
        _tvActionsScroll.MaxHeight = naturalCopy ? double.PositiveInfinity : 180;
        BackdropContainer.Height = double.NaN; BackdropContainer.MinHeight = naturalRail ? Math.Max(0, height - _tvNavigationItems!.ActualHeight - _tvNavigation.Padding.Top - _tvNavigation.Padding.Bottom) : 0;
        BackdropContainer.MaxHeight = landscape ? Math.Max(0, height - 208) : double.PositiveInfinity;
        BackdropContainer.VerticalAlignment = landscape ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        HeroContentGrid.VerticalAlignment = naturalCopy ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        HeroContentGrid.RowDefinitions[0].Height = naturalCopy ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        HeroContentGrid.RowDefinitions[1].Height = new GridLength(0);
        HeroContentGrid.ColumnSpacing = narrow || ViewModel.Item?.Type == "episode" ? 0 : 16;
        HeroContentGrid.Margin = narrow ? new Thickness(16, 52, 16, 8) : new Thickness(landscape ? 16 : 40, height <= 800 ? 52 : 64, landscape ? 16 : 40, height <= 800 ? 8 : 16);
        Grid.SetColumn(_tvCopyHost, narrow || ViewModel.Item?.Type == "episode" ? 0 : 1);
        Grid.SetColumnSpan(_tvCopyHost, narrow || ViewModel.Item?.Type == "episode" ? 2 : 1);
        Grid.SetRow(_tvCopyHost, 0);
        HeroPosterContainer.Visibility = !narrow && ViewModel.Item?.Type != "episode" ? Visibility.Visible : Visibility.Collapsed;
        var posterHeight = 330d;
        HeroPosterContainer.Height = posterHeight; HeroPosterContainer.Width = posterHeight * 2 / 3;
        HeroPosterContainer.VerticalAlignment = VerticalAlignment.Bottom;
        FavoriteButton.Visibility = narrow || ViewModel.Item?.Type == "season" ? Visibility.Collapsed : Visibility.Visible;
        StarRatingContainer.Visibility = narrow || ViewModel.Item?.Type is "season" or "episode" ? Visibility.Collapsed : Visibility.Visible;
        HeroActionsRow.HorizontalSpacing = narrow ? 8 : 12;
        UpdateWatchedButton();
        BuildMoreFlyout();
        if (ViewModel.Episodes.Count > 0) LayoutEpisodeGrid();
        TitleText.MaxLines = narrow ? 3 : 2;
        TitleText.CharacterSpacing = -25;
        TitleText.FontSize = narrow ? Math.Clamp(ActualWidth * .06, 20, 30) : height > 800 ? 72 : 48;
        TitleText.LineHeight = TitleText.FontSize * 1.15;
        if (narrow)
        {
            DetailContentPanel.Padding = new Thickness(ActualWidth < 640 ? 16 : 24, 28, ActualWidth < 640 ? 16 : 24, 48);
            DetailContentPanel.Spacing = 32;
        }
    }
    private void UpdateMangaStickyHeader()
    {
        _stickyVolume = null;
        MangaStickyHeader.Visibility = Visibility.Collapsed;
        if (ViewModel.Item?.Type != "manga") return;
        foreach (var volume in _mangaVolumes.Where(v => v.Control.IsExpanded))
        {
            var top = volume.Control.TransformToVisual(ContentScroll).TransformPoint(new Windows.Foundation.Point()).Y;
            var bottom = top + volume.Control.ActualHeight;
            if (top >= 0 || bottom <= 0) continue;
            _stickyVolume = volume.Control;
            MangaStickyHeaderButton.Content = volume.Label;
            MangaStickyHeader.Visibility = Visibility.Visible;
            var headerHeight = MangaStickyHeader.ActualHeight > 0
                ? MangaStickyHeader.ActualHeight
                : MangaStickyHeader.DesiredSize.Height;
            MangaStickyHeader.RenderTransform = new TranslateTransform { Y = Math.Min(0, bottom - headerHeight) };
            break;
        }
    }
    private void MangaStickyHeader_Click(object sender, RoutedEventArgs e)
    {
        if (_stickyVolume != null) _stickyVolume.IsExpanded = false;
        UpdateMangaStickyHeader();
    }

    private void UpdateBackdropHeight()
    {
        if (ViewModel.Item?.Type is not ("series" or "season" or "episode")) ResetTvViewport();
        EnsureTvViewport();
        if (_tvViewport != null) { SizeTvViewport(); return; }
        // Current WebUI uses a compact 35vh hero for season pages and the
        // standard 60dvh hero for full item detail pages. MinHeight (rather
        // than a fixed Height) lets wrapped actions and long metadata expand
        // naturally on smaller windows.
        if (XamlRoot?.Content is FrameworkElement root && root.ActualHeight > 0)
        {
            var wide = root.ActualWidth >= 1024;
            var factor = ViewModel.Item?.Type == "season"
                ? wide ? 0.42 : 0.35
                : wide ? 0.72 : 0.60;
            BackdropContainer.Height = double.NaN;
            BackdropContainer.MinHeight = Math.Max(300, root.ActualHeight * factor);
            DetailSkeletonHero.MinHeight = Math.Max(300, root.ActualHeight * factor);
        }
    }

    private void ResetTvViewport()
    {
        if (_tvViewport == null || _tvNavigationItems == null || _tvPinnedActions == null || _tvCopyScroll == null || _tvCopyHost == null) return;
        DetailPageFlow.Children.Remove(_tvViewport);
        _tvViewport.Children.Remove(BackdropContainer);
        DetailPageFlow.Children.Insert(0, BackdropContainer);
        foreach (var (section, index) in _tvSectionIndexes.OrderBy(pair => pair.Value))
        {
            _tvNavigationItems.Children.Remove(section);
            DetailContentPanel.Children.Insert(Math.Clamp(index, 0, DetailContentPanel.Children.Count), section);
        }
        _tvSectionIndexes.Clear();
        _tvCopyScroll.Content = null;
        _tvPinnedActions.Children.Clear();
        HeroInfoPanel.Children.Add(HeroActionsRow); HeroInfoPanel.Children.Add(PlaybackOptionsRow);
        HeroContentGrid.Children.Remove(_tvCopyHost);
        Grid.SetRow(HeroInfoPanel, 0); Grid.SetColumn(HeroInfoPanel, 1); Grid.SetColumnSpan(HeroInfoPanel, 1);
        HeroContentGrid.Children.Add(HeroInfoPanel);
        TitleText.MaxLines = 0;
        BackdropContainer.MaxHeight = double.PositiveInfinity;
        BackdropContainer.VerticalAlignment = VerticalAlignment.Stretch;
        HeroContentGrid.VerticalAlignment = VerticalAlignment.Bottom;
        HeroContentGrid.RowDefinitions[0].Height = HeroContentGrid.RowDefinitions[1].Height = GridLength.Auto;
        _tvViewport = _tvCopyHost = null;
        _tvNavigation = _tvCopyScroll = _tvActionsScroll = null;
        _tvNavigationItems = _tvPinnedActions = null;
    }

    private void ItemDetailPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
        if (TrailerOverlay.Visibility == Visibility.Visible)
            SizeTrailerModal(e.NewSize.Width, e.NewSize.Height);
    }

    private void HorizontalCarousel_KeyDown(
        object sender,
        Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroller) return;
        var delta = e.Key switch
        {
            Windows.System.VirtualKey.Left => -Math.Max(240, scroller.ViewportWidth * 0.82),
            Windows.System.VirtualKey.Right => Math.Max(240, scroller.ViewportWidth * 0.82),
            _ => 0,
        };
        if (delta == 0) return;
        scroller.ChangeView(Math.Max(0, scroller.HorizontalOffset + delta), null, null);
        e.Handled = true;
    }

    private void TrailersScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        => UpdateDetailCarouselButtons(TrailersScrollViewer, TrailersPrevButton, TrailersNextButton);

    private void TrailersScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateDetailCarouselButtons(TrailersScrollViewer, TrailersPrevButton, TrailersNextButton);

    private void CastScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        => UpdateDetailCarouselButtons(CastScrollViewer, CastPrevButton, CastNextButton);

    private void CastScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateDetailCarouselButtons(CastScrollViewer, CastPrevButton, CastNextButton);

    private void TrailersPrev_Click(object sender, RoutedEventArgs e)
        => ScrollDetailCarousel(TrailersScrollViewer, TrailersPrevButton, TrailersNextButton, -1, 292);

    private void TrailersNext_Click(object sender, RoutedEventArgs e)
        => ScrollDetailCarousel(TrailersScrollViewer, TrailersPrevButton, TrailersNextButton, 1, 292);

    private void CastPrev_Click(object sender, RoutedEventArgs e)
        => ScrollDetailCarousel(CastScrollViewer, CastPrevButton, CastNextButton, -1, 122);

    private void CastNext_Click(object sender, RoutedEventArgs e)
        => ScrollDetailCarousel(CastScrollViewer, CastPrevButton, CastNextButton, 1, 122);

    private static void ScrollDetailCarousel(
        ScrollViewer scroller,
        Button previousButton,
        Button nextButton,
        int direction,
        double minimumStep)
    {
        var delta = Math.Max(minimumStep, scroller.ViewportWidth * 0.82d) * direction;
        var target = Math.Clamp(scroller.HorizontalOffset + delta, 0, Math.Max(0, scroller.ScrollableWidth));
        scroller.ChangeView(target, null, null);
        UpdateDetailCarouselButtons(scroller, previousButton, nextButton);
    }

    private static void UpdateDetailCarouselButtons(
        ScrollViewer scroller,
        Button previousButton,
        Button nextButton)
    {
        var canScroll = scroller.ExtentWidth > scroller.ViewportWidth + 1;
        var canScrollPrevious = canScroll && scroller.HorizontalOffset > 1;
        var canScrollNext = canScroll && scroller.HorizontalOffset < scroller.ScrollableWidth - 1;
        previousButton.Visibility = canScrollPrevious ? Visibility.Visible : Visibility.Collapsed;
        nextButton.Visibility = canScrollNext ? Visibility.Visible : Visibility.Collapsed;
        previousButton.IsEnabled = canScrollPrevious;
        nextButton.IsEnabled = canScrollNext;
    }

    private void DetailCarousel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroller)
            return;

        var point = e.GetCurrentPoint(scroller);
        if (!point.Properties.IsLeftButtonPressed && !point.IsInContact)
            return;

        _detailCarouselDragPointerId = e.Pointer.PointerId;
        _detailCarouselDragScroller = scroller;
        _detailCarouselDragStartX = point.Position.X;
        _detailCarouselDragStartOffset = scroller.HorizontalOffset;
        _detailCarouselDragging = false;
    }

    private void DetailCarousel_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroller ||
            _detailCarouselDragScroller != scroller ||
            _detailCarouselDragPointerId != e.Pointer.PointerId)
            return;

        var point = e.GetCurrentPoint(scroller);
        var delta = point.Position.X - _detailCarouselDragStartX;
        if (!_detailCarouselDragging)
        {
            if (Math.Abs(delta) < DetailCarouselDragThreshold)
                return;

            _detailCarouselDragging = scroller.CapturePointer(e.Pointer);
            if (!_detailCarouselDragging)
            {
                ResetDetailCarouselDragState();
                return;
            }
        }

        var target = Math.Clamp(
            _detailCarouselDragStartOffset - delta,
            0,
            Math.Max(0, scroller.ScrollableWidth));
        scroller.ChangeView(target, null, null, disableAnimation: true);
        UpdateButtonsForDetailCarousel(scroller);
        e.Handled = true;
    }

    private void DetailCarousel_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroller ||
            _detailCarouselDragScroller != scroller ||
            _detailCarouselDragPointerId != e.Pointer.PointerId)
            return;

        var handled = _detailCarouselDragging;
        if (_detailCarouselDragging)
            scroller.ReleasePointerCapture(e.Pointer);
        ResetDetailCarouselDragState();
        e.Handled = handled;
    }

    private void DetailCarousel_PointerCanceled(object sender, PointerRoutedEventArgs e)
        => ResetDetailCarouselDragState();

    private void DetailCarousel_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        => ResetDetailCarouselDragState();

    private void ResetDetailCarouselDragState()
    {
        _detailCarouselDragPointerId = null;
        _detailCarouselDragScroller = null;
        _detailCarouselDragging = false;
    }

    private void UpdateButtonsForDetailCarousel(ScrollViewer scroller)
    {
        if (scroller == TrailersScrollViewer)
            UpdateDetailCarouselButtons(scroller, TrailersPrevButton, TrailersNextButton);
        else if (scroller == CastScrollViewer)
            UpdateDetailCarouselButtons(scroller, CastPrevButton, CastNextButton);
    }

    private void SiblingEpisodesScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        => UpdateSiblingEpisodeScrollButtons();

    private void SiblingEpisodesScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TryAlignSiblingEpisodeToStart();
        UpdateSiblingEpisodeScrollButtons();
    }

    private void SeasonsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        => UpdateSeasonScrollButtons();

    private void SeasonsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateSeasonScrollButtons();

    private void SeasonsPrev_Click(object sender, RoutedEventArgs e)
        => ScrollSeasons(-1);

    private void SeasonsNext_Click(object sender, RoutedEventArgs e)
        => ScrollSeasons(1);

    private void ScrollSeasons(int direction)
    {
        var delta = Math.Max(186d, SeasonsScrollViewer.ViewportWidth * 0.82d) * direction;
        var target = Math.Clamp(
            SeasonsScrollViewer.HorizontalOffset + delta,
            0,
            Math.Max(0, SeasonsScrollViewer.ScrollableWidth));
        SeasonsScrollViewer.ChangeView(target, null, null);
        UpdateSeasonScrollButtons();
    }

    private void UpdateSeasonScrollButtons()
    {
        if (SeasonsPrevButton == null || SeasonsNextButton == null)
            return;

        var canScroll = SeasonsScrollViewer.ExtentWidth > SeasonsScrollViewer.ViewportWidth + 1;
        SeasonsPrevButton.Visibility = canScroll && SeasonsScrollViewer.HorizontalOffset > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        SeasonsNextButton.Visibility = canScroll &&
            SeasonsScrollViewer.HorizontalOffset < SeasonsScrollViewer.ScrollableWidth - 1
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void SiblingEpisodesPrev_Click(object sender, RoutedEventArgs e)
        => ScrollSiblingEpisodes(-1);

    private void SiblingEpisodesNext_Click(object sender, RoutedEventArgs e)
        => ScrollSiblingEpisodes(1);

    private void ScrollSiblingEpisodes(int direction)
    {
        var delta = Math.Max(252d, SiblingEpisodesScrollViewer.ViewportWidth * 0.82d) * direction;
        var target = Math.Clamp(
            SiblingEpisodesScrollViewer.HorizontalOffset + delta,
            0,
            Math.Max(0, SiblingEpisodesScrollViewer.ScrollableWidth));
        SiblingEpisodesScrollViewer.ChangeView(target, null, null);
        UpdateSiblingEpisodeScrollButtons();
    }

    private void UpdateSiblingEpisodeScrollButtons()
    {
        if (SiblingEpisodesArrowPanel == null || SiblingEpisodesPrevButton == null || SiblingEpisodesNextButton == null)
            return;

        var canScroll = SiblingEpisodesScrollViewer.ExtentWidth > SiblingEpisodesScrollViewer.ViewportWidth + 1;
        SiblingEpisodesArrowPanel.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
        var canScrollPrev = canScroll && SiblingEpisodesScrollViewer.HorizontalOffset > 1;
        var canScrollNext = canScroll &&
            SiblingEpisodesScrollViewer.HorizontalOffset < SiblingEpisodesScrollViewer.ScrollableWidth - 1;
        SiblingEpisodesPrevButton.Visibility = canScrollPrev ? Visibility.Visible : Visibility.Collapsed;
        SiblingEpisodesNextButton.Visibility = canScrollNext ? Visibility.Visible : Visibility.Collapsed;
        SiblingEpisodesPrevButton.IsEnabled = canScrollPrev;
        SiblingEpisodesNextButton.IsEnabled = canScrollNext;
    }

    private void SiblingEpisodesScrollViewer_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(SiblingEpisodesScrollViewer);
        if (!point.Properties.IsLeftButtonPressed && !point.IsInContact)
            return;

        _siblingEpisodesDragPointerId = e.Pointer.PointerId;
        _siblingEpisodesDragStartX = point.Position.X;
        _siblingEpisodesDragStartOffset = SiblingEpisodesScrollViewer.HorizontalOffset;
        _siblingEpisodesDragging = false;
    }

    private void SiblingEpisodesScrollViewer_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_siblingEpisodesDragPointerId != e.Pointer.PointerId)
            return;

        var point = e.GetCurrentPoint(SiblingEpisodesScrollViewer);
        var delta = point.Position.X - _siblingEpisodesDragStartX;
        if (!_siblingEpisodesDragging)
        {
            if (Math.Abs(delta) < SiblingEpisodesDragThreshold)
                return;

            _siblingEpisodesDragging = SiblingEpisodesScrollViewer.CapturePointer(e.Pointer);
            if (!_siblingEpisodesDragging)
            {
                ResetSiblingEpisodesDragState();
                return;
            }
        }

        var target = Math.Clamp(
            _siblingEpisodesDragStartOffset - delta,
            0,
            Math.Max(0, SiblingEpisodesScrollViewer.ScrollableWidth));
        SiblingEpisodesScrollViewer.ChangeView(target, null, null, disableAnimation: true);
        UpdateSiblingEpisodeScrollButtons();
        e.Handled = true;
    }

    private void SiblingEpisodesScrollViewer_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_siblingEpisodesDragPointerId != e.Pointer.PointerId)
            return;

        var handled = _siblingEpisodesDragging;
        if (_siblingEpisodesDragging)
            SiblingEpisodesScrollViewer.ReleasePointerCapture(e.Pointer);
        ResetSiblingEpisodesDragState();
        e.Handled = handled;
    }

    private void SiblingEpisodesScrollViewer_PointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => ResetSiblingEpisodesDragState();

    private void SiblingEpisodesScrollViewer_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => ResetSiblingEpisodesDragState();

    private void ResetSiblingEpisodesDragState()
    {
        _siblingEpisodesDragPointerId = null;
        _siblingEpisodesDragging = false;
    }

    private double SeasonWidth(double width) => width < 1024 ? 112 : ActualHeight > 0 && ActualHeight <= 650 && width > ActualHeight ? 80 : 140;

    private void UpdateResponsiveLayout(double width)
    {
        if (width <= 0) return;

        var heroGutter = width >= 1280 ? 48d : width >= 1024 ? 40d : width >= 640 ? 24d : 16d;
        var contentGutter = width >= 1024 ? 40d : width >= 640 ? 24d : 16d;
        HeroContentGrid.Margin = new Thickness(heroGutter, 0, heroGutter, 32);
        // CSS page-shell-wide uses border-box sizing: its 1520px maximum
        // includes the responsive inline padding. XAML margins are external,
        // so subtract them to keep the same outer shell width and centering.
        HeroContentGrid.MaxWidth = Math.Max(0, 1520 - (heroGutter * 2));
        DetailSkeletonHeroContent.Margin = new Thickness(heroGutter, 112, heroGutter, 32);
        DetailSkeletonHeroContent.MaxWidth = Math.Max(0, 1520 - (heroGutter * 2));
        DetailContentPanel.Padding = new Thickness(contentGutter, 40, contentGutter, 48);
        DetailSkeletonSections.Padding = new Thickness(contentGutter, 40, contentGutter, 48);
        DetailErrorContent.Margin = new Thickness(contentGutter, 32, contentGutter, 0);
        DetailErrorContent.MaxWidth = Math.Max(0, 1400 - (contentGutter * 2));
        SeasonEpisodesFatalErrorContent.Margin = new Thickness(
            contentGutter,
            width >= 640 ? 40 : 24,
            contentGutter,
            0);
        DetailContentPanel.Spacing = width >= 640 ? 56 : 48;
        // PageBack is positioned independently of the page-shell gutters in
        // the WebUI (left-2; top-4 on compact and top-6 from sm upward).
        BackButton.Margin = new Thickness(8, width >= 640 ? 24 : 16, 0, 0);

        var stackedHero = width < 1024 && _tvViewport == null;
        var hidesPoster = ViewModel.Item?.Type == "episode";
        Grid.SetRow(HeroPosterContainer, 0);
        Grid.SetColumn(HeroPosterContainer, 0);
        Grid.SetRow(HeroInfoPanel, stackedHero && !hidesPoster ? 1 : 0);
        Grid.SetColumn(HeroInfoPanel, stackedHero || hidesPoster ? 0 : 1);
        Grid.SetColumnSpan(HeroInfoPanel, stackedHero || hidesPoster ? 2 : 1);
        HeroContentGrid.ColumnSpacing = hidesPoster ? 0 : 24;
        HeroContentGrid.RowSpacing = stackedHero && !hidesPoster ? 24 : 0;

        Grid.SetRow(DetailSkeletonPoster, 0);
        Grid.SetColumn(DetailSkeletonPoster, 0);
        if (DetailSkeletonPoster.Parent is Grid skeletonGrid &&
            skeletonGrid.Children.Count > 1 &&
            skeletonGrid.Children[1] is FrameworkElement skeletonInfo)
        {
            Grid.SetRow(skeletonInfo, stackedHero ? 1 : 0);
            Grid.SetColumn(skeletonInfo, stackedHero ? 0 : 1);
            Grid.SetColumnSpan(skeletonInfo, stackedHero ? 2 : 1);
            skeletonGrid.RowSpacing = stackedHero ? 24 : 0;
        }

        var isSeason = ViewModel.Item?.Type == "season";
        var isSquare = ViewModel.Item?.Type == "audiobook";
        var posterWidth = isSeason
            ? width >= 640 ? 160d : 140d
            : isSquare
                ? width >= 640 ? 260d : 200d
                : width >= 640 ? 220d : 170d;
        HeroPosterContainer.Width = posterWidth;
        HeroPosterContainer.Height = isSquare ? posterWidth : posterWidth * 1.5;
        DetailSkeletonPoster.Width = width >= 640 ? 220 : 170;
        DetailSkeletonPoster.Height = DetailSkeletonPoster.Width * 1.5;

        TitleText.FontSize = isSeason
            ? width >= 640 ? 36 : 30
            : width >= 1024 ? 72 : width >= 640 ? 48 : 36;
        TitleText.CharacterSpacing = isSeason ? -25 : -50;
        TitleText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        TitleText.LineHeight = TitleText.FontSize * (isSeason ? 1.1d : 0.98d);
        HeroLogoImage.MaxWidth = TitleArtPending.MaxWidth = width >= 1024 ? 480 : 420;
        HeroLogoImage.Height = TitleArtPending.Height = width >= 1024 ? 112 : 80;
        HeroLogoImage.MaxHeight = HeroLogoImage.Height;

        var seasonCardWidth = SeasonWidth(width);
        foreach (var child in SeasonsLoadingSkeleton.Children.OfType<SkeletonPoster>())
            child.SetResponsiveWidth(seasonCardWidth);
        if (SeasonsPanel.Children.Count > 0 && Math.Abs(_seasonCardWidth - seasonCardWidth) > 0.5)
            BuildSeasonCards();

        var similarSkeletonWidth = width >= 1280 ? 178d : width >= 640 ? 150d : 130d;
        foreach (var child in SimilarLoadingSkeleton.Children.OfType<SkeletonPoster>())
            child.SetResponsiveWidth(similarSkeletonWidth);

        if (_tvViewport != null) SizeTvViewport();
        UpdateSimilarGridLayout(width, contentGutter);
        foreach (var panel in ExtrasGroupsPanel.Children.OfType<StackPanel>())
            if (panel.Children.LastOrDefault() is Grid extrasGrid) ReflowExtras(extrasGrid, width);

    }

    private void UpdateSimilarGridLayout(double width, double gutter)
    {
        var innerWidth = Math.Max(280, Math.Min(1400, width) - (gutter * 2));
        var columns = _uiCustomizationService.GetPosterColumnCount(innerWidth);
        var cardWidth = Math.Max(110, (innerWidth - ((columns - 1) * 12)) / columns);
        foreach (var child in SimilarPanel.Children)
        {
            if (child is PosterCard card)
                card.SetCatalogGridLayout(cardWidth);
        }
    }

    private void ComposeHeroLayout()
    {
        // DetailHero keeps metadata, scores, overview, credits, genres, and
        // actions inside the artwork hero. Earlier desktop builds rendered
        // everything except the title below the backdrop, which made the page
        // visibly unlike the WebUI even though all controls were functional.
        if (ScoresPanel.Children.Count > 0)
            ScoresPanel.Children[0].Visibility = Visibility.Collapsed;
        HeroMetadataRow.Children.Remove(ScoresPanel);

        // ActionBar.tsx renders two distinct rows. Detach the stream selectors
        // from the old split Play button / primary row before moving both rows
        // into the hero information column.
        SplitPlayButton.Children.Remove(VersionDropdownButton);
        SplitPlayButton.Children.Remove(VersionSeparator);
        VersionSeparator.Visibility = Visibility.Collapsed;
        HeroActionsRow.Children.Remove(EditionButton);
        HeroActionsRow.Children.Remove(AudioTracksButton);
        HeroActionsRow.Children.Remove(SubtitlesPopoverButton);
        PlaybackOptionsRow.Children.Add(VersionDropdownButton);
        PlaybackOptionsRow.Children.Add(EditionButton);
        PlaybackOptionsRow.Children.Add(AudioTracksButton);
        PlaybackOptionsRow.Children.Add(SubtitlesPopoverButton);

        MoveIntoHero(HeroMetadataRow);
        MoveIntoHero(ScoresPanel);
        MoveIntoHero(OverviewText);
        MoveIntoHero(TranslateOverviewButton);
        MoveIntoHero(HeroCrewLine);
        MoveIntoHero(GenresBadgesPanel);
        MoveIntoHero(BookProgressSummaryText);
        MoveIntoHero(HeroActionsRow);
        MoveIntoHero(PlaybackOptionsRow);

        HeroMetadataRow.Margin = new Thickness(0, 6, 0, 0);
        ScoresPanel.Margin = new Thickness(0, 2, 0, 0);
        OverviewText.Margin = new Thickness(0, 4, 0, 0);
        TranslateOverviewButton.Margin = new Thickness(0, 2, 0, 0);
        HeroCrewLine.Margin = new Thickness(0, 2, 0, 0);
        GenresBadgesPanel.Margin = new Thickness(0, 4, 0, 0);
        BookProgressSummaryText.Margin = new Thickness(0, 4, 0, 0);
        HeroActionsRow.Margin = new Thickness(0, 8, 0, 0);
        PlaybackOptionsRow.Margin = new Thickness(0, 0, 0, 0);
    }

    private void UpdateThemeGradientColors()
    {
        if (Application.Current.Resources["AppBackgroundColor"] is not Windows.UI.Color background)
            return;

        Windows.UI.Color WithAlpha(byte alpha) => Microsoft.UI.ColorHelper.FromArgb(
            alpha, background.R, background.G, background.B);

        DetailBottomTransparent.Color = WithAlpha(0);
        DetailBottomSoft.Color = WithAlpha(0x33);
        DetailBottomMid.Color = WithAlpha(0x8C);
        DetailBottomStrong.Color = WithAlpha(0xEB);
        DetailBottomSolid.Color = WithAlpha(0xFF);
        DetailLeftSolid.Color = WithAlpha(0xFF);
        DetailLeftStrong.Color = WithAlpha(0xCC);
        DetailLeftSoft.Color = WithAlpha(0x66);
        DetailLeftTransparent.Color = WithAlpha(0);
        DetailVignetteStrong.Color = WithAlpha(0x66);
        DetailVignetteSoft.Color = WithAlpha(0x33);
        DetailVignetteTransparent.Color = WithAlpha(0);

        SkeletonLeftSolid.Color = WithAlpha(0xFF);
        SkeletonLeftMid.Color = WithAlpha(0xB3);
        SkeletonLeftSoft.Color = WithAlpha(0x33);
        SkeletonBottomTransparent.Color = WithAlpha(0);
        SkeletonBottomSolid.Color = WithAlpha(0xFF);
    }

    private void MoveIntoHero(UIElement element)
    {
        var index = DetailContentPanel.Children.IndexOf(element);
        if (index >= 0)
            DetailContentPanel.Children.RemoveAt(index);
        HeroInfoPanel.Children.Add(element);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.UserRating))
            DispatcherQueue.TryEnqueue(UpdateStarRating);
        else if (args.PropertyName == nameof(ViewModel.IsWatched))
            DispatcherQueue.TryEnqueue(UpdateWatchedButton);
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        SettingsViewModel.TitleArtPreferenceChanged -= OnTitleArtPreferenceChanged;
        SettingsViewModel.TitleArtPreferenceChanged += OnTitleArtPreferenceChanged;
        UpdateBackdropHeight();
        UpdateResponsiveLayout(ActualWidth);
        if (XamlRoot?.Content is FrameworkElement root)
        {
            _rootElement = root;
            _rootElement.SizeChanged += OnRootSizeChanged;
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBackdropHeight();
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private bool IsActiveAudiobook() => ViewModel.Item?.Type == "audiobook" && _playerService?.IsAudiobook == true
        && _playerService.State != Services.PlayerState.Idle && _playerService.ContentId == ViewModel.Item.ContentId;
    private void OnDetailPauseChanged(bool paused) => DispatcherQueue.TryEnqueue(UpdateActiveAudiobook);
    private void OnDetailPositionChanged(double position) => DispatcherQueue.TryEnqueue(UpdateActiveAudiobook);
    private void UpdateActiveAudiobook()
    {
        if (!IsActiveAudiobook()) return;
        PlayButtonText.Text = _playerService!.IsPaused ? "Resume" : "Pause";
        BookProgressSummaryText.Text = $"{FormatExtraDuration(_playerService.Position)} / {FormatExtraDuration(_playerService.Duration)}";
        BookProgressSummaryText.Visibility = Visibility.Visible;
    }

    private void OnPlayerStateChanged(Services.PlayerState state)
    {
        if (state == Services.PlayerState.Idle)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (_navigationCts is not { IsCancellationRequested: false }) return;
                if (_playableContentId != null)
                    _ = LoadWatchDetailAsync(_playableContentId);
                _ = ViewModel.RefreshWatchedStateAsync(
                    _playerService?.PendingProgressSave ?? Task.CompletedTask,
                    _navigationCts.Token);
            });
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        var music = App.Services.GetService<SiloPlayer.Services.ThemeMusicService>();
        if (e.SourcePageType == typeof(ItemDetailPage)) music?.Suspend(); else music?.Stop();
        if (TrailerOverlay.Visibility == Visibility.Visible)
            CloseTrailerModal();
        base.OnNavigatedFrom(e);

        SettingsViewModel.TitleArtPreferenceChanged -= OnTitleArtPreferenceChanged;
        ++_titleArtRevision;
        ViewModel.CancelPendingLoads();
        _uiCustomizationService.Changed -= UICustomization_Changed;
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_rootElement != null)
        {
            _rootElement.SizeChanged -= OnRootSizeChanged;
            _rootElement = null;
        }
        if (_playerService != null)
        {
            _playerService.StateChanged -= OnPlayerStateChanged;
            _playerService.PauseChanged -= OnDetailPauseChanged;
            _playerService.PositionChanged -= OnDetailPositionChanged;
            _subscribedToStateChanged = false;
        }
        _translationCts?.Cancel();
        _translationCts?.Dispose();
        _translationCts = null;
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = null;
    }

    private string? _currentContentId;
    private string? _playableContentId; // The actual episode/movie ID used for watch detail
    private bool _subscribedToStateChanged;
    private double _seasonCardWidth;
    private string _seasonEpisodesErrorMessage = "Season not found";

    private bool IsCurrentDetail(string contentId) =>
        _navigationCts is { IsCancellationRequested: false }
        && string.Equals(_currentContentId, contentId, StringComparison.Ordinal)
        && string.Equals(ViewModel.Item?.ContentId, contentId, StringComparison.Ordinal);

    private bool IsActiveDetail(string contentId, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && IsCurrentDetail(contentId);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Navigation away removes this subscription; cached revisits need it too.
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _uiCustomizationService.Changed += UICustomization_Changed;
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = new CancellationTokenSource();
        var navigationToken = _navigationCts.Token;

        // This page instance can be reused while navigating directly between
        // detail routes. Never let the previous item's watch response or
        // pre-play selections influence the next item's actions while its
        // companion watch request is still loading.
        _watchDetail = null;
        _selectedVersion = null;
        _playableContentId = null;
        _selectedAudioTrackIndex = null;
        _selectedSubtitleIndex = null;
        _selectedSubtitleSignature = null;
        _downloadedSubtitles = [];
        _seriesSeasonsResolved = false;
        _seasonEpisodesErrorMessage = "Season not found";
        SeasonEpisodesFatalError.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Visible;

        // Subscribe to player state changes to refresh play button after playback ends
        if (!_subscribedToStateChanged)
        {
            _subscribedToStateChanged = true;
            _playerService = App.Services.GetRequiredService<Services.PlayerService>();
            _playerService.StateChanged += OnPlayerStateChanged;
            _playerService.PauseChanged += OnDetailPauseChanged;
            _playerService.PositionChanged += OnDetailPositionChanged;
        }

        if (e.Parameter is string contentId && !string.IsNullOrEmpty(contentId))
        {
            _currentContentId = contentId;
            await ViewModel.LoadCommand.ExecuteAsync(contentId);
            if (_currentContentId != contentId || navigationToken.IsCancellationRequested)
                return;
            if (ViewModel.Item == null)
            {
                App.Services.GetRequiredService<Services.ToastService>().Error(
                    ViewModel.ErrorMessage ?? "Failed to load item");
                return;
            }
            if (ViewModel.Item.ContentId != contentId)
                return;

            // Paint the item response immediately. Series enrichment and season
            // episode queries are companion requests in the WebUI and must not
            // hold the entire page behind another network round trip.
            UpdateUI();
            AdvisoryAgeBadge.Visibility = Visibility.Collapsed;
            _ = LoadAdvisoryDisplayAsync(ViewModel.Item, navigationToken);
            var music = App.Services.GetService<SiloPlayer.Services.ThemeMusicService>();
            if (music != null) _ = music.SelectAsync(ViewModel.Item, navigationToken);
            _requestSeasonsContentId = null;
            if (ViewModel.Item.Type == "series") _ = LoadSeriesRequestActionAsync(ViewModel.Item, navigationToken);

            if (ViewModel.Item.Type == "episode")
            {
                _playableContentId = contentId;
                _ = LoadWatchDetailAsync(contentId);
            }

            // If this is an episode, enrich with series metadata
            if (ViewModel.Item?.Type == "episode" && !string.IsNullOrEmpty(ViewModel.Item.SeriesId))
            {
                _ = EnrichEpisodeAndRefreshAsync(
                    contentId,
                    ViewModel.Item.SeriesId,
                    navigationToken);
            }

            // If this is a season, load its episodes for display
            if (ViewModel.Item?.Type == "season")
            {
                EpisodesSection.Visibility = Visibility.Visible;
                EpisodesLoadingSkeleton.Visibility = Visibility.Visible;
                EpisodesLoadError.Visibility = Visibility.Collapsed;
                EpisodesPanel.Visibility = Visibility.Collapsed;
                var episodesLoaded = await LoadSeasonEpisodesAsync(
                    ViewModel.Item.ContentId,
                    ViewModel.Item.SeasonNumber ?? 0,
                    navigationToken);
                if (_currentContentId != contentId || ViewModel.Item?.ContentId != contentId)
                    return;
                if (navigationToken.IsCancellationRequested)
                    return;
                EpisodesLoadingSkeleton.Visibility = Visibility.Collapsed;
                if (!episodesLoaded)
                {
                    ShowSeasonEpisodesFatalError(ViewModel.Item);
                    return;
                }
                EpisodesLoadError.Visibility = Visibility.Collapsed;
                EpisodesPanel.Visibility = Visibility.Visible;
            }

            _ = ConfigureOnViewTranslationAsync(ViewModel.Item);
            if (ViewModel.Item?.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase) == true)
                _ = LoadEbookProgressAsync(ViewModel.Item);

            // Rating is inlined on the item detail response via `user_rating`
            // (server commit 4172a16). Only fire the dedicated /ratings/{id}
            // endpoint if the server didn't populate it — keeps older servers
            // working while avoiding an extra round trip on current ones.
            if (ViewModel.Item?.UserRating == null && ViewModel.UserRating == null)
            {
                _ = ViewModel.LoadRatingCommand.ExecuteAsync(null);
            }

            // Current WebUI shows "More Like This" only on movie and series pages.
            if (ViewModel.Item?.Type is "movie" or "series")
            {
                SimilarSection.Visibility = Visibility.Visible;
                SimilarLoadingSkeleton.Visibility = Visibility.Visible;
                SimilarLoadError.Visibility = Visibility.Collapsed;
                SimilarPanel.Visibility = Visibility.Collapsed;
                _ = LoadSimilarItemsAsync();
            }

            // Load sibling episodes if this is an episode (non-blocking)
            if (ViewModel.Item?.Type == "episode")
            {
                SiblingEpisodesSection.Visibility = Visibility.Visible;
                SiblingEpisodesLoadingSkeleton.Visibility = Visibility.Visible;
                SiblingEpisodesLoadError.Visibility = Visibility.Collapsed;
                SiblingEpisodesScrollViewer.Visibility = Visibility.Collapsed;
                _ = LoadSiblingEpisodesAsync();
            }

            if (ViewModel.IsSeries)
            {
                SeasonsSection.Visibility = Visibility.Visible;
                SeasonsLoadingSkeleton.Visibility = Visibility.Visible;
                SeasonsLoadError.Visibility = Visibility.Collapsed;
                SeasonsScrollViewer.Visibility = Visibility.Collapsed;

                await ViewModel.LoadSeasonsCommand.ExecuteAsync(null);

                if (_currentContentId != contentId || ViewModel.Item?.ContentId != contentId)
                    return;
                if (navigationToken.IsCancellationRequested)
                    return;

                SeasonsLoadError.Visibility = Visibility.Collapsed;
                _seriesSeasonsResolved = !ViewModel.SeasonsLoadFailed;
                UpdateSeriesCountsFromLoadedSeasons();
                SizeTvViewport();

                // Paint the season result as soon as that request completes.
                // Continue-watching lookup is independent and can take several
                // seconds; awaiting it first left a visibly blank section.
                if (ViewModel.SeasonsLoadFailed)
                {
                    SeasonsLoadingSkeleton.Visibility = Visibility.Collapsed;
                    SeasonsScrollViewer.Visibility = Visibility.Collapsed;
                    SeasonsSection.Visibility = Visibility.Collapsed;
                }
                else if (ViewModel.Seasons.Count == 1)
                {
                    await ShowSingleSeasonEpisodesAsync(
                        ViewModel.Seasons[0],
                        contentId,
                        navigationToken);
                    if (_currentContentId != contentId || ViewModel.Item?.ContentId != contentId)
                        return;
                    if (navigationToken.IsCancellationRequested)
                        return;
                }
                else if (ViewModel.Seasons.Count == 0)
                {
                    SeasonsLoadingSkeleton.Visibility = Visibility.Collapsed;
                    SeasonsScrollViewer.Visibility = Visibility.Collapsed;
                    SeasonsSection.Visibility = Visibility.Collapsed;
                    EpisodesSection.Visibility = Visibility.Collapsed;
                }
                else
                {
                    BuildSeasonCards();
                    SeasonsLoadingSkeleton.Visibility = Visibility.Collapsed;
                    SeasonsScrollViewer.Visibility = Visibility.Visible;
                    EpisodesSection.Visibility = Visibility.Collapsed;
                }

                ApplyAuthoritativeSeriesAction();
                VersionDropdownButton.Visibility = Visibility.Collapsed;
                VersionSeparator.Visibility = Visibility.Collapsed;
                AudioTracksButton.Visibility = Visibility.Collapsed;
                SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
            }
            else if (ViewModel.Item?.Type == "season")
            {
                // A season is a collection, not a playable catalog leaf. The
                // current WebUI targets its first episode and labels the action
                // "Play First Episode". Never request /watch/{season-id}.
                BuildEpisodeRows();
                var firstEpisode = ViewModel.Episodes.FirstOrDefault();
                _playableContentId = ViewModel.Item.HasAuthoritativePlayTarget
                    ? ViewModel.Item.PlayContentId : firstEpisode?.ContentId;
                PlayButtonText.Text = "Play First Episode";
                SplitPlayButton.Visibility = string.IsNullOrWhiteSpace(_playableContentId)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                VersionDropdownButton.Visibility = Visibility.Collapsed;
                VersionSeparator.Visibility = Visibility.Collapsed;
                AudioTracksButton.Visibility = Visibility.Collapsed;
                SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
            }
            else if (!IsReaderItem(ViewModel.Item))
            {
                // For movies/episodes, load watch detail directly
                if (!string.Equals(_playableContentId, contentId, StringComparison.Ordinal))
                {
                    _playableContentId = contentId;
                    _ = LoadWatchDetailAsync(contentId);
                }
            }
        }
    }

    /// <summary>
    /// Builds the Series › Season › Episode breadcrumb for an episode page.
    /// Mirrors webui DetailBreadcrumb in ItemDetail/EpisodeContent.tsx.
    /// Series link is available immediately (from item.series_id); the season link
    /// is resolved async by fetching the series' seasons and matching season_number.
    /// </summary>
    private void BuildEpisodeBreadcrumb(MediaItemDetail item)
    {
        if (string.IsNullOrEmpty(item.SeriesId) || string.IsNullOrEmpty(item.SeriesTitle))
            return;

        var seriesTitle = item.SeriesTitle;
        var seriesId = item.SeriesId;
        var seasonNum = item.SeasonNumber;
        var episodeNum = item.EpisodeNumber;

        // 1. Series link (always clickable)
        BreadcrumbPanel.Children.Add(MakeBreadcrumbLink(seriesTitle, seriesId!));

        // 2. Season separator + link (season content_id resolved async)
        if (seasonNum.HasValue)
        {
            BreadcrumbPanel.Children.Add(MakeBreadcrumbSeparator());

            var seasonLabel = seasonNum.Value == 0 ? "Specials" : $"Season {seasonNum.Value}";
            var seasonLink = MakeBreadcrumbLink(seasonLabel, contentId: null);
            seasonLink.IsEnabled = false; // enabled once we resolve content_id
            BreadcrumbPanel.Children.Add(seasonLink);

            _ = ResolveSeasonLinkAsync(seasonLink, seriesId!, seasonNum.Value);
        }

        // 3. Episode (terminal, not clickable)
        if (episodeNum.HasValue)
        {
            BreadcrumbPanel.Children.Add(MakeBreadcrumbSeparator());
            BreadcrumbPanel.Children.Add(new TextBlock
            {
                Text = $"Episode {episodeNum.Value}",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            });
        }

        BreadcrumbPanel.Visibility = Visibility.Visible;
    }

    private HyperlinkButton MakeBreadcrumbLink(string label, string? contentId)
    {
        var btn = new HyperlinkButton
        {
            Content = new TextBlock
            {
                Text = label,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            },
            Padding = new Thickness(0, 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(contentId))
        {
            btn.Click += (_, _) =>
            {
                var nav = App.Services.GetRequiredService<NavigationService>();
                nav.Navigate<ItemDetailPage>(contentId);
            };
        }
        return btn;
    }

    private TextBlock MakeBreadcrumbSeparator()
    {
        return new TextBlock
        {
            Text = " \u203A ",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)Application.Current.Resources["TertiaryTextBrush"],
        };
    }

    /// <summary>
    /// Fetches the season list for the series and flips the season breadcrumb
    /// link to navigate to the matching season's content_id.
    /// </summary>
    private async Task ResolveSeasonLinkAsync(HyperlinkButton seasonLink, string seriesId, int seasonNumber)
    {
        try
        {
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var resp = await catalogApi.GetSeasonsAsync(seriesId);
            var season = resp?.Seasons?.FirstOrDefault(s => s.SeasonNumber == seasonNumber);
            if (season == null || string.IsNullOrEmpty(season.ContentId)) return;

            var seasonContentId = season.ContentId;
            seasonLink.Click += (_, _) =>
            {
                var nav = App.Services.GetRequiredService<NavigationService>();
                nav.Navigate<ItemDetailPage>(seasonContentId);
            };
            seasonLink.IsEnabled = true;
        }
        catch
        {
            // Season link will just stay disabled — non-critical
        }
    }

    private void BuildSeasonBreadcrumb(MediaItemDetail item, string seasonLabel)
    {
        BreadcrumbPanel.Children.Clear();
        if (!string.IsNullOrWhiteSpace(item.SeriesId) && !string.IsNullOrWhiteSpace(item.SeriesTitle))
        {
            BreadcrumbPanel.Children.Add(MakeBreadcrumbLink(item.SeriesTitle!, item.SeriesId));
            BreadcrumbPanel.Children.Add(MakeBreadcrumbSeparator());
        }
        BreadcrumbPanel.Children.Add(new TextBlock
        {
            Text = seasonLabel,
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        BreadcrumbPanel.Visibility = Visibility.Visible;
    }

    private void UICustomization_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => UpdateResponsiveLayout(ActualWidth));

    private async Task<bool> EnrichEpisodeWithSeriesDataAsync(string seriesId, CancellationToken ct)
    {
        try
        {
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var series = await catalogApi.GetItemDetailAsync(seriesId, ct);
            if (series == null) return false;

            var episode = ViewModel.Item!;
            var changed = false;

            // Fill in missing episode data from series
            if (string.IsNullOrEmpty(episode.BackdropUrl) && !string.IsNullOrEmpty(series.BackdropUrl))
            {
                episode.BackdropUrl = series.BackdropUrl;
                changed = true;
            }
            if (string.IsNullOrEmpty(episode.PosterUrl) && !string.IsNullOrEmpty(series.PosterUrl))
            {
                episode.PosterUrl = series.PosterUrl;
                changed = true;
            }
            if ((episode.Cast == null || episode.Cast.Count == 0) && series.Cast?.Count > 0)
            {
                episode.Cast = series.Cast;
                changed = true;
            }
            if ((episode.Crew == null || episode.Crew.Count == 0) && series.Crew?.Count > 0)
            {
                episode.Crew = series.Crew;
                changed = true;
            }
            if ((episode.Studios?.Count ?? 0) == 0 && (series.Studios?.Count ?? 0) > 0)
            {
                episode.Studios = series.Studios!;
                changed = true;
            }
            if ((episode.Networks?.Count ?? 0) == 0 && (series.Networks?.Count ?? 0) > 0)
            {
                episode.Networks = series.Networks!;
                changed = true;
            }
            if ((episode.Countries?.Count ?? 0) == 0 && (series.Countries?.Count ?? 0) > 0)
            {
                episode.Countries = series.Countries!;
                changed = true;
            }
            if ((episode.Genres?.Count ?? 0) == 0 && (series.Genres?.Count ?? 0) > 0)
            {
                episode.Genres = series.Genres!;
                changed = true;
            }
            if (string.IsNullOrEmpty(episode.ContentRating) && !string.IsNullOrEmpty(series.ContentRating))
            {
                episode.ContentRating = series.ContentRating;
                changed = true;
            }
            return changed;
        }
        catch
        {
            // Non-critical — episode still shows with its own data
            return false;
        }
    }

    private async Task EnrichEpisodeAndRefreshAsync(
        string contentId,
        string seriesId,
        CancellationToken ct)
    {
        var enriched = await EnrichEpisodeWithSeriesDataAsync(seriesId, ct);
        if (!enriched || ct.IsCancellationRequested ||
            !string.Equals(_currentContentId, contentId, StringComparison.Ordinal) ||
            !string.Equals(ViewModel.Item?.ContentId, contentId, StringComparison.Ordinal))
            return;

        // Older servers can omit inherited artwork/cast/crew from the episode
        // response. Current servers normally need no visible second paint.
        UpdateUI();
    }

    private void UpdateUI()
    {
        var item = ViewModel.Item;
        if (item == null) return;

        UpdateMetadataBadgeTheme();

        UpdateBackdropHeight();

        PrimaryPlayButton.IsEnabled = true;
        SplitPlayButton.Opacity = 1;
        WatchedButton.Visibility = Visibility.Visible;
        FavoriteButton.Visibility = Visibility.Visible;
        StarRatingContainer.Visibility = Visibility.Visible;
        MoreButton.Visibility = Visibility.Visible;
        AddCollectionButton.Visibility = Visibility.Collapsed;
        ListenFromStartButton.Visibility = Visibility.Collapsed;
        BookDownloadButton.Visibility = Visibility.Collapsed;
        BookAuthorLine.Visibility = Visibility.Collapsed;
        BookNarratorLine.Visibility = Visibility.Collapsed;
        BookProgressSummaryText.Visibility = Visibility.Collapsed;
        AudiobookNarratorSection.Visibility = Visibility.Collapsed;
        VersionDropdownButton.Visibility = Visibility.Collapsed;
        EditionButton.Visibility = Visibility.Collapsed;
        AudioTracksButton.Visibility = Visibility.Collapsed;
        SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
        _playProgressFraction = 0;
        PlayProgressBar.Width = 0;
        HeroPosterContainer.Width = 170;
        HeroPosterContainer.Height = 255;
        MediaLocationsTitle.Text = item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase) ? "Files" : "Media locations";

        // Reset breadcrumb visibility by default; episode case repopulates it
        BreadcrumbPanel.Children.Clear();
        BreadcrumbPanel.Visibility = Visibility.Collapsed;
        EpisodeContextText.Visibility = Visibility.Collapsed;
        HeroContextText.Visibility = Visibility.Collapsed;
        SeasonCountBadge.Visibility = Visibility.Collapsed;
        EpisodeCountBadge.Visibility = Visibility.Collapsed;

        // Star rating widget is hidden on season pages (webui parity — the
        // test suite explicitly asserts this; users rate individual movies
        // and series, not seasons).
        StarRatingContainer.Visibility = item.Type is "season" or "episode"
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (item.Type is "season" or "episode")
        {
            // SeasonContent exposes watched + curator actions, but not the
            // series/movie favorite action in the current WebUI.
            FavoriteButton.Visibility = Visibility.Collapsed;
        }

        if (item.Type is "movie" or "series" or "audiobook" or "ebook" or "manga")
        {
            HeroContextText.Text = item.Type switch
            {
                "movie" => "Movie",
                "series" => "Series",
                "audiobook" => "Audiobook",
                "ebook" => "Ebook",
                "manga" => "Manga",
                _ => "",
            };
            HeroContextText.Visibility = Visibility.Visible;
        }

        // ─── Hero poster / logo / kicker (webui DetailHero parity) ───

        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();
        var imageToken = _imageCts.Token;

        // Portrait poster column. Only show for non-episode items — episodes
        // inherit the series backdrop and don't have a dedicated portrait.
        HeroPosterImage.Source = null;
        HeroPosterFallback.Text = item.Title;
        HeroPosterFallback.Visibility = item.Type is "series" or "season" ? Visibility.Visible : Visibility.Collapsed;
        if (item.Type != "episode" && !string.IsNullOrEmpty(item.PosterUrl))
        {
            _ = LoadHeroPosterAsync(item.PosterUrl, imageToken);
            HeroPosterContainer.Visibility = Visibility.Visible;
        }
        else
        {
            HeroPosterContainer.Visibility = Visibility.Collapsed;
            HeroPosterImage.Source = null;
        }

        HeroLogoImage.Source = null;
        HeroLogoImage.Visibility = Visibility.Collapsed;
        TitleArtPending.Visibility = string.IsNullOrWhiteSpace(item.LogoUrl) ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Visibility = string.IsNullOrWhiteSpace(item.LogoUrl) ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TitleArtPending, item.Title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HeroLogoImage, item.Title);
        _ = LoadTitleArtPreferenceAsync(item, imageToken);

        // Studio/network kicker — uppercase line above the title. Networks
        // win for series, studios for movies. First one only.
        // MovieContent passes its first studio to DetailHero while
        // SeriesContent passes its first network. Do not cross-fallback here:
        // doing so can expose a different label from the current WebUI when a
        // server response happens to contain both collections.
        string? kicker = item.Type switch
        {
            "movie" when item.Studios is { Count: > 0 } => item.Studios[0],
            "series" when item.Networks is { Count: > 0 } => item.Networks[0],
            "ebook" when !string.IsNullOrWhiteSpace(item.Ebook?.Publisher) => item.Ebook.Publisher,
            "ebook" when item.Studios is { Count: > 0 } => item.Studios[0],
            _ => null,
        };
        if (!string.IsNullOrEmpty(kicker))
        {
            StudioKickerText.Text = kicker.ToUpperInvariant();
            StudioKickerText.Visibility = Visibility.Visible;
        }
        else
        {
            StudioKickerText.Visibility = Visibility.Collapsed;
        }

        HeroContextText.Text = HeroContextText.Text.ToUpperInvariant();
        var hasType = HeroContextText.Visibility == Visibility.Visible;
        var hasStudio = StudioKickerText.Visibility == Visibility.Visible;
        HeroEyebrow.Visibility = hasType || hasStudio ? Visibility.Visible : Visibility.Collapsed;
        HeroEyebrowDot.Visibility = hasType && hasStudio ? Visibility.Visible : Visibility.Collapsed;

        // Episode context: show series title and S##E## above/below episode title
        if (item.Type == "episode")
        {
            // Clickable breadcrumb: Series › Season N › Episode M
            // The season link navigates by season content_id (resolved async below).
            BuildEpisodeBreadcrumb(item);

            // Hide the plain tagline — breadcrumb replaces it
            TaglineText.Visibility = Visibility.Collapsed;

            if (item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
            {
                EpisodeContextText.Text = $"S{item.SeasonNumber} \u00B7 E{item.EpisodeNumber}";
                EpisodeContextText.Visibility = Visibility.Visible;
            }
            YearText.Text = FormatDetailDate(item.AirDate);

            TitleText.Text = item.Title;
        }
        else if (item.Type == "season")
        {
            var seasonLabel = item.IsSpecials == true || item.SeasonNumber == 0
                ? "Specials"
                : string.IsNullOrWhiteSpace(item.Title)
                    ? $"Season {item.SeasonNumber ?? 0}"
                    : item.Title;
            TitleText.Text = string.IsNullOrWhiteSpace(item.SeriesTitle)
                ? seasonLabel
                : $"{item.SeriesTitle}: {seasonLabel}";
            TaglineText.Visibility = Visibility.Collapsed;
            YearText.Text = !string.IsNullOrWhiteSpace(item.AirDate) && item.AirDate.Length >= 4
                ? item.AirDate[..4]
                : item.Year > 0 ? item.Year.ToString() : "";
            BuildSeasonBreadcrumb(item, seasonLabel);
        }
        else if (item.Type == "series")
        {
            TitleText.Text = item.Title;
            TaglineText.Text = item.Tagline ?? "";
            TaglineText.Visibility = string.IsNullOrEmpty(item.Tagline)
                ? Visibility.Collapsed : Visibility.Visible;

            var firstYear = AirDateYear(item.FirstAirDate);
            var lastYear = AirDateYear(item.LastAirDate);
            YearText.Text = firstYear != null
                ? lastYear != null && !string.Equals(firstYear, lastYear, StringComparison.Ordinal)
                    ? $"{firstYear}–{lastYear}"
                    : firstYear
                : item.Year > 0 ? item.Year.ToString() : "";
        }
        else
        {
            TitleText.Text = item.Title;
            TaglineText.Text = item.Tagline ?? "";
            TaglineText.Visibility = string.IsNullOrEmpty(item.Tagline)
                ? Visibility.Collapsed : Visibility.Visible;

            YearText.Text = item.Year > 0 ? item.Year.ToString() : "";
        }
        // The shared WebUI metadata-badge class applies text-transform:
        // uppercase to every media type, including an episode's formatted
        // air date.
        YearText.Text = YearText.Text.ToUpperInvariant();
        YearBadge.Visibility = string.IsNullOrWhiteSpace(YearText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // F7: set dynamic window title to the item's name
        if (App.MainWindowInstance is MainWindow mw)
            mw.SetDynamicTitle(item.Title);

        // Content rating in pill badge
        if (!string.IsNullOrEmpty(item.ContentRating) && item.Type is not ("episode" or "season"))
        {
            ContentRatingText.Text = item.ContentRating.ToUpperInvariant();
            ContentRatingBadge.Visibility = Visibility.Visible;
            MetaDot1.Visibility = Visibility.Collapsed;
        }
        else
        {
            ContentRatingBadge.Visibility = Visibility.Collapsed;
            MetaDot1.Visibility = Visibility.Collapsed;
        }

        RuntimeText.Text = ViewModel.RuntimeDisplay.ToUpperInvariant();
        if (item.Type == "series")
        {
            // SeriesContent renders season and episode counts as two separate
            // MetadataBadges; it does not add a third combined runtime pill.
            RuntimeText.Text = "";
        }
        if (item.Type == "season")
            RuntimeText.Text = $"{item.EpisodeCount ?? ViewModel.Episodes.Count} EPISODES";
        MetaDot2.Visibility = Visibility.Collapsed;

        RuntimeBadge.Visibility = string.IsNullOrWhiteSpace(RuntimeText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (item.Type == "series")
        {
            if (item.SeasonCount is > 0)
            {
                SeasonCountText.Text = $"{item.SeasonCount} {(item.SeasonCount == 1 ? "SEASON" : "SEASONS")}";
                SeasonCountBadge.Visibility = Visibility.Visible;
            }
            if (item.EpisodeCount is > 0)
            {
                EpisodeCountText.Text = $"{item.EpisodeCount} {(item.EpisodeCount == 1 ? "EPISODE" : "EPISODES")}";
                EpisodeCountBadge.Visibility = Visibility.Visible;
            }
            RuntimeBadge.Visibility = Visibility.Collapsed;
        }
        if (item.Type == "season")
            RuntimeBadge.Visibility = Visibility.Visible;
        ArrangeMetadataBadges(item.Type);
        MetaDot1.Visibility = Visibility.Collapsed;
        MetaDot2.Visibility = Visibility.Collapsed;

        OverviewText.Text = item.Overview ?? "";
        if (string.IsNullOrWhiteSpace(item.PendingTranslationLanguage))
            TranslateOverviewButton.Visibility = Visibility.Collapsed;

        // Current WebUI renders movie/series genres inline in HeroCrewLine,
        // not as a second row of pills beneath it.
        GenresBadgesPanel.Children.Clear();
        GenresBadgesPanel.Visibility = Visibility.Collapsed;

        UpdateScoresRow(item);
        UpdateWatchedButton();
        UpdateFavoriteButton();
        UpdateStarRating();
        BuildMoreFlyout();

        // Set initial play button text from catalog item user data (before watch detail loads)
        UpdatePlayButtonFromItemData(item);

        // Set initial quality badges from catalog item user data (before watch detail loads)
        UpdateQualityBadgesFromItemData(item);

        // Show Match + Refresh metadata buttons for admin users
        var authService = App.Services.GetRequiredService<AuthService>();
        var canCurateMetadata = AuthorizationPolicy.CanCurateMetadata(authService);
        // Current WebUI keeps metadata tools inside the More menu.
        BuildMediaLocationsSection(canCurateMetadata, item.Versions);

        // Load backdrop
        _ = LoadBackdropAsync(item, imageToken);

        BuildTrailers(item.Videos ?? []);
        BuildExtras(item.Extras ?? []);

        // Build cast
        BuildCast(item.Cast ?? new());

        // Build crew (directors + writers)
        BuildCrew(item.Crew ?? new());

        // Hero crew line: "Directed by X · Written by Y"
        BuildHeroCrewLine(item);

        // Studios
        if (item.Studios?.Count > 0)
        {
            StudiosText.Text = "Studios:  " + string.Join(", ", item.Studios);
            StudiosText.Visibility = Visibility.Visible;
        }
        else
        {
            StudiosText.Visibility = Visibility.Collapsed;
        }

        // Networks (series only)
        if (item.Networks?.Count > 0)
        {
            NetworksText.Text = "Networks:  " + string.Join(", ", item.Networks);
            NetworksText.Visibility = Visibility.Visible;
        }
        else
        {
            NetworksText.Visibility = Visibility.Collapsed;
        }

        // Countries
        if (item.Countries?.Count > 0)
        {
            CountriesText.Text = "Countries:  " + string.Join(", ", item.Countries);
            CountriesText.Visibility = Visibility.Visible;
        }
        else
        {
            CountriesText.Visibility = Visibility.Collapsed;
        }

        ArrangeCurrentWebUiContentOrder(item.Type);

        // Episode series enrichment can complete after /watch and repaint this
        // page. Reapply the watch-derived controls and complete version data so
        // that repaint cannot hide Audio, Subtitles, Version, or media paths.
        if (_watchDetail != null &&
            string.Equals(_watchDetail.ContentId, _playableContentId, StringComparison.Ordinal))
        {
            UpdatePlayButton();
            UpdateQualityBadges();
            BuildMediaLocationsSection(canCurateMetadata, _watchDetail.Versions);
        }
        // Book surfaces deliberately diverge from the generic movie/episode
        // watch controls. Apply them last so a companion /watch response cannot
        // reintroduce video-quality badges or hide the listening actions.
        ConfigureBookDetail(item);
        ApplyAuthoritativeSeriesAction();
        UpdateResponsiveLayout(ActualWidth);
    }

    private void ArrangeMetadataBadges(string itemType)
    {
        // EpisodeContent renders duration before air date. Other detail types
        // use the WebUI MetadataBadges order already represented by the XAML.
        MetaPanel.Children.Remove(YearBadge);
        MetaPanel.Children.Remove(RuntimeBadge);
        if (itemType.Equals("episode", StringComparison.OrdinalIgnoreCase))
        {
            MetaPanel.Children.Insert(0, RuntimeBadge);
            MetaPanel.Children.Insert(1, YearBadge);
        }
        else
        {
            MetaPanel.Children.Insert(0, YearBadge);
            MetaPanel.Children.Insert(4, RuntimeBadge);
        }
    }

    private void ArrangeCurrentWebUiContentOrder(string itemType)
    {
        // Data can finish before Loaded, when FrameworkElement.Parent is still
        // null. The named content stack is stable and lets us establish the
        // WebUI section order deterministically on both cold and cached loads.
        var parent = DetailContentPanel;

        FrameworkElement[] movable =
        [
            MediaLocationsSection,
            SeasonsSection,
            EpisodesSection,
            TrailersSection,
            ExtrasSection,
            SiblingEpisodesSection,
            CastSection,
            CrewSection,
            SimilarSection,
            SubtitlesSection,
            StudiosText,
            NetworksText,
            CountriesText,
            AudiobookNarratorSection,
            BookRelatedSection,
            AudiobookChaptersSection,
            MangaChaptersSection,
        ];
        foreach (var element in movable)
        {
            parent.Children.Remove(element);
            element.Margin = new Thickness(0);
        }

        // These legacy detail rows are not standalone sections in the current
        // WebUI. Studio/network context and subtitle actions live in the hero.
        StudiosText.Visibility = Visibility.Collapsed;
        NetworksText.Visibility = Visibility.Collapsed;
        CountriesText.Visibility = Visibility.Collapsed;
        SubtitlesSection.Visibility = Visibility.Collapsed;

        IEnumerable<FrameworkElement> ordered = itemType switch
        {
            "movie" =>
            [
                MediaLocationsSection, TrailersSection, ExtrasSection,
                CastSection, CrewSection, SimilarSection,
            ],
            "series" =>
            [
                SeasonsSection, EpisodesSection, TrailersSection, ExtrasSection,
                CastSection, CrewSection, SimilarSection,
            ],
            "episode" =>
            [
                MediaLocationsSection, SiblingEpisodesSection,
                CastSection, CrewSection,
            ],
            "audiobook" =>
            [
                AudiobookNarratorSection, BookRelatedSection, AudiobookChaptersSection,
            ],
            "ebook" =>
            [
                BookRelatedSection, MediaLocationsSection,
            ],
            "manga" =>
            [
                MangaChaptersSection,
            ],
            _ =>
            [
                EpisodesSection, CastSection, CrewSection,
            ],
        };

        if (itemType is not ("movie" or "episode" or "ebook"))
            MediaLocationsSection.Visibility = Visibility.Collapsed;
        if (itemType is not ("movie" or "series"))
        {
            TrailersSection.Visibility = Visibility.Collapsed;
            ExtrasSection.Visibility = Visibility.Collapsed;
            SimilarSection.Visibility = Visibility.Collapsed;
        }

        var orderedElements = ordered.ToList();
        foreach (var element in orderedElements)
        {
            // DetailContentPanel.Spacing already mirrors the WebUI's
            // space-y-12 / sm:space-y-14 rhythm. Do not add a second margin.
            if (_tvNavigationItems?.Children.Contains(element) != true)
                parent.Children.Add(element);
        }
        foreach (var element in movable.Except(orderedElements))
            if (_tvNavigationItems?.Children.Contains(element) != true)
                parent.Children.Add(element);
    }

    private void ConfigureBookDetail(MediaItemDetail item)
    {
        _readerTargetContentId = null;
        _readerTargetFileId = null;
        BookRelatedSection.Visibility = Visibility.Collapsed;
        AudiobookChaptersSection.Visibility = Visibility.Collapsed;
        AudiobookNarratorSection.Visibility = Visibility.Collapsed;
        MangaChaptersSection.Visibility = Visibility.Collapsed;

        if (item.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase))
        {
            App.Services.GetRequiredService<Services.PlayerService>().SetAudiobookPresentation(item);
            var position = Math.Max(0, item.UserData?.PositionSeconds ?? 0);
            var duration = Math.Max(0, item.Audiobook?.TotalDurationSeconds
                ?? item.UserData?.DurationSeconds
                ?? item.Versions.Sum(version => Math.Max(0, version.Duration)));
            HeroPosterContainer.Height = 170;
            WatchedButton.Visibility = Visibility.Collapsed;
            FavoriteButton.Visibility = Visibility.Collapsed;
            StarRatingContainer.Visibility = Visibility.Collapsed;
            MoreButton.Visibility = Visibility.Collapsed;
            AddCollectionButton.Visibility = Visibility.Visible;
            ListenFromStartButton.Visibility = position > 0 && item.UserData?.Played != true
                ? Visibility.Visible : Visibility.Collapsed;
            VersionDropdownButton.Visibility = Visibility.Collapsed;
            VersionSeparator.Visibility = Visibility.Collapsed;
            EditionButton.Visibility = Visibility.Collapsed;
            AudioTracksButton.Visibility = Visibility.Collapsed;
            SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
            QualityBadgesPanel.Visibility = Visibility.Collapsed;
            PlaybackOptionsRow.Visibility = Visibility.Collapsed;
            MediaLocationsSection.Visibility = Visibility.Collapsed;
            SplitPlayButton.Visibility = item.Versions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PlayButtonIcon.Glyph = "\uE768";
            var hasProgress = position > 0 && item.UserData?.Played != true && duration > 0;
            if (duration > 0)
            {
                RuntimeText.Text = FormatBookDuration(duration).ToUpperInvariant();
                RuntimeBadge.Visibility = Visibility.Visible;
                _playProgressFraction = Math.Clamp(position / duration, 0, 1);
                UpdatePlayProgressWidth();
            }
            var authors = PeopleNames(item.Audiobook?.Authors, item.Crew, "Author");
            var narrators = PeopleNames(item.Audiobook?.Narrators, item.Crew, "Narrator");
            TaglineText.Visibility = Visibility.Collapsed;
            HeroCrewLine.Visibility = Visibility.Collapsed;
            BookAuthorLine.Text = authors.Count > 0 ? $"By {string.Join(", ", authors)}" : "";
            BookAuthorLine.Visibility = authors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BookNarratorLine.Text = narrators.Count > 0 ? $"Narrated by {string.Join(", ", narrators)}" : "";
            BookNarratorLine.Visibility = narrators.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildBookGenreLinks(item);

            BuildAudiobookChapters(item);
            var currentChapter = FindAudiobookChapter(position);
            PlayButtonText.Text = hasProgress
                ? currentChapter == null ? "Resume" : $"Resume \u00B7 {currentChapter.Label}"
                : "Listen";
            BookProgressSummaryText.Text = hasProgress
                ? $"{FormatBookDuration(position)} listened \u00B7 {Math.Round(Math.Clamp(position / duration, 0, 1) * 100):0}%"
                : "";
            BookProgressSummaryText.Visibility = hasProgress ? Visibility.Visible : Visibility.Collapsed;

            BuildNarrationPicker(item);
            UpdateActiveAudiobook();
            BuildAudiobookNarrator(narrators.FirstOrDefault());
            BuildBookRelatedGroups(item.Audiobook?.Series, item.Audiobook?.Related, authors.FirstOrDefault());
            return;
        }

        if (item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase))
        {
            FavoriteButton.Visibility = Visibility.Collapsed;
            StarRatingContainer.Visibility = Visibility.Collapsed;
            MoreButton.Visibility = Visibility.Collapsed;
            _readerTargetContentId = item.ContentId;
            _selectedVersion = ChooseReadableBookVersion(item.Versions, item.UserData?.LastFileId);
            _readerTargetFileId = _selectedVersion?.FileId;
            SplitPlayButton.Visibility = _selectedVersion != null ? Visibility.Visible : Visibility.Collapsed;
            BookDownloadButton.Visibility = item.Versions.Count > 0 && CanCurrentUserDownload()
                ? Visibility.Visible : Visibility.Collapsed;
            PlayButtonIcon.Glyph = "\uE736";
            PlayButtonText.Text = "Read";
            var authors = PeopleNames(item.Ebook?.Authors, item.Crew, "Author");
            TaglineText.Visibility = Visibility.Collapsed;
            BookAuthorLine.Visibility = Visibility.Collapsed;
            HeroCrewLine.Text = authors.Count > 0 ? $"By {string.Join(", ", authors)}" : "";
            HeroCrewLine.Visibility = authors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildBookGenreLinks(item);
            BuildBookRelatedGroups(item.Ebook?.Series, item.Ebook?.Related, authors.FirstOrDefault());
            return;
        }

        if (item.Type.Equals("manga", StringComparison.OrdinalIgnoreCase))
        {
            WatchedButton.Visibility = Visibility.Collapsed;
            FavoriteButton.Visibility = Visibility.Collapsed;
            StarRatingContainer.Visibility = Visibility.Collapsed;
            MoreButton.Visibility = Visibility.Visible;
            VersionDropdownButton.Visibility = Visibility.Collapsed;
            VersionSeparator.Visibility = Visibility.Collapsed;
            EditionButton.Visibility = Visibility.Collapsed;
            AudioTracksButton.Visibility = Visibility.Collapsed;
            SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
            BuildMangaChapters(item);
            BuildMoreFlyout();
        }
    }

    private async Task LoadEbookProgressAsync(MediaItemDetail item)
    {
        try
        {
            var progress = await App.Services.GetRequiredService<EbooksApi>().GetProgressAsync(item.ContentId);
            if (ViewModel.Item?.ContentId != item.ContentId) return;
            if (progress.Progress > 0)
            {
                _readerTargetFileId = progress.FileId;
                _selectedVersion = item.Versions.FirstOrDefault(v => v.FileId == progress.FileId) ?? _selectedVersion;
                PlayButtonText.Text = $"Continue  {Math.Round(Math.Clamp(progress.Progress, 0, 1) * 100):0}%";
                _playProgressFraction = Math.Clamp(progress.Progress, 0, 1);
                UpdatePlayProgressWidth();
            }
        }
        catch { }
    }

    private static FileVersion? ChooseReadableBookVersion(IEnumerable<FileVersion> versions, int? preferredFileId)
    {
        var list = versions.ToList();
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "epub", "pdf", "mobi", "azw", "azw3", "cbz", "cbr", "fb2", "fbz" };
        static string Format(FileVersion version)
        {
            var extension = Path.GetExtension(version.FileName ?? version.FilePath ?? "").TrimStart('.');
            return string.IsNullOrWhiteSpace(extension) ? version.Container.TrimStart('.') : extension;
        }
        if (preferredFileId.HasValue)
        {
            var preferred = list.FirstOrDefault(v => v.FileId == preferredFileId.Value && supported.Contains(Format(v)));
            if (preferred != null) return preferred;
        }
        return list.FirstOrDefault(v => Format(v).Equals("epub", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(v => supported.Contains(Format(v)));
    }

    private static List<string> PeopleNames(IEnumerable<AudiobookPerson>? people, IEnumerable<CrewMember> crew, string job)
    {
        var names = people?.Select(person => person.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList() ?? [];
        if (names.Count == 0)
            names = crew.Where(member => member.Job.Equals(job, StringComparison.OrdinalIgnoreCase))
                .Select(member => member.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToList();
        return names;
    }

    private void BuildNarrationPicker(MediaItemDetail item)
    {
        NarrationPickerButton.Visibility = Visibility.Collapsed;
        if (item.Audiobook?.OtherNarrations.Count > 0)
        {
            NarrationPickerButton.Content = BookNarratorLine.Text + " ▾";
            NarrationPickerButton.Visibility = Visibility.Visible;
            BookNarratorLine.Visibility = Visibility.Collapsed;
        }
    }
    private void NarrationPicker_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.Item;
        if (item?.Audiobook == null) return;
        var menu = new MenuFlyout();
        menu.Items.Add(new ToggleMenuFlyoutItem { Text = BookNarratorLine.Text, IsChecked = true, IsEnabled = false });
        foreach (var narration in item.Audiobook.OtherNarrations)
        {
            var choice = new MenuFlyoutItem { Text = (narration.Narrators.Count > 0 ? string.Join(", ", narration.Narrators) : "Unknown narrator") + (narration.Year > 0 ? $" · {narration.Year}" : "") };
            choice.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(narration.ContentId);
            menu.Items.Add(choice);
        }
        menu.ShowAt(NarrationPickerButton);
    }

    private void BuildAudiobookNarrator(string? narrator)
    {
        if (string.IsNullOrWhiteSpace(narrator))
        {
            AudiobookNarratorSection.Visibility = Visibility.Collapsed;
            return;
        }

        AudiobookNarratorName.Text = narrator;
        AudiobookNarratorInitials.Text = string.Concat(narrator
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0])));
        if (string.IsNullOrWhiteSpace(AudiobookNarratorInitials.Text))
            AudiobookNarratorInitials.Text = "?";
        AudiobookNarratorSection.Visibility = Visibility.Visible;
    }

    private void BuildBookGenreLinks(MediaItemDetail item)
    {
        GenresBadgesPanel.Children.Clear();
        foreach (var genre in item.Genres ?? [])
        {
            if (string.IsNullOrWhiteSpace(genre)) continue;
            var button = new Button
            {
                Content = genre,
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(999),
                FontSize = 12,
                Tag = genre,
            };
            button.Click += (_, _) =>
            {
                var navigation = App.Services.GetRequiredService<NavigationService>();
                var library = (App.MainWindowInstance as MainWindow)?.FindLibraryByType(item.Type);
                if (library != null)
                {
                    navigation.Navigate<LibraryPage>(new LibraryPage.NavigationArgs(
                        library,
                        InitialTab: "Library",
                        InitialGenre: genre));
                }
                else
                {
                    navigation.Navigate<CatalogPage>(new CatalogNavigation(
                        Title: genre,
                        Subtitle: $"Browse {genre} {item.Type}s",
                        Scope: item.Type,
                        Genre: genre));
                }
            };
            GenresBadgesPanel.Children.Add(button);
        }
        GenresBadgesPanel.Visibility = GenresBadgesPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BuildBookRelatedGroups(AudiobookSeriesGroup? series, AudiobookRelatedItems? related, string? author)
    {
        BookRelatedGroupsPanel.Children.Clear();
        if (series?.Entries.Count > 0)
            AddBookRelatedGroup(!string.IsNullOrWhiteSpace(series.Name) ? $"In {series.Name}" : "In this series", series.Entries);
        if (related?.AlsoByAuthor.Count > 0)
            AddBookRelatedGroup($"Also by {author ?? "this author"}", related.AlsoByAuthor);
        if (related?.Similar.Count > 0)
            AddBookRelatedGroup("You might also like", related.Similar);
        BookRelatedSection.Visibility = BookRelatedGroupsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddBookRelatedGroup(string heading, IEnumerable<AudiobookRelatedItem> entries)
    {
        var group = new StackPanel { Spacing = 12 };
        group.Children.Add(new TextBlock
        {
            Text = heading,
            Style = (Style)Application.Current.Resources["TitleTextStyle"],
            FontSize = 20,
        });
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var entry in entries)
        {
            cards.Children.Add(new PosterCard
            {
                MediaItem = new MediaItem
                {
                    ContentId = entry.ContentId,
                    Type = ViewModel.Item?.Type ?? "ebook",
                    Title = entry.Title,
                    Year = entry.Year ?? 0,
                    PosterUrl = entry.PosterUrl,
                }
            });
        }
        group.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = cards,
        });
        BookRelatedGroupsPanel.Children.Add(group);
    }

    private void BuildAudiobookChapters(MediaItemDetail item)
    {
        _audiobookChapterRows.Clear();
        AudiobookChaptersPanel.Children.Clear();
        AudiobookChaptersBorder.Visibility = Visibility.Collapsed;
        AudiobookChapterSortButton.Visibility = Visibility.Collapsed;
        AudiobookChaptersChevron.Glyph = "\uE76C";
        _audiobookChaptersExpanded = false;

        var offset = 0d;
        var positionIndex = 1;
        foreach (var version in item.Versions)
        {
            foreach (var chapter in (version.Chapters ?? []).OrderBy(chapter => chapter.StartSeconds))
            {
                var absoluteStart = offset + Math.Max(0, chapter.StartSeconds);
                _audiobookChapterRows.Add(new AudiobookChapterRow(
                    positionIndex++,
                    string.IsNullOrWhiteSpace(chapter.Title) ? $"Chapter {positionIndex - 1}" : chapter.Title,
                    absoluteStart,
                    Math.Max(0, chapter.EndSeconds - chapter.StartSeconds),
                    version.FileId));
            }
            offset += Math.Max(0, version.Duration);
        }

        if (_audiobookChapterRows.Count == 0) return;
        AudiobookChapterCountText.Text = $"Chapters  ({_audiobookChapterRows.Count})";
        AudiobookChaptersSection.Visibility = Visibility.Visible;
    }

    private AudiobookChapterRow? FindAudiobookChapter(double absolutePosition)
    {
        for (var index = _audiobookChapterRows.Count - 1; index >= 0; index--)
        {
            if (absolutePosition >= _audiobookChapterRows[index].AbsoluteStart)
                return _audiobookChapterRows[index];
        }
        return null;
    }

    private void AudiobookChaptersToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _audiobookChaptersExpanded = !_audiobookChaptersExpanded;
        AudiobookChaptersChevron.Glyph = _audiobookChaptersExpanded ? "\uE70D" : "\uE76C";
        AudiobookChapterSortButton.Visibility = _audiobookChaptersExpanded ? Visibility.Visible : Visibility.Collapsed;
        AudiobookChaptersBorder.Visibility = _audiobookChaptersExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (_audiobookChaptersExpanded)
            RenderAudiobookChapters();
        else
            AudiobookChaptersPanel.Children.Clear();
    }

    private void AudiobookChapterSortButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var option in new[] { (Label: "By position", Longest: false), (Label: "Longest first", Longest: true) })
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = option.Label,
                IsChecked = _audiobookChaptersLongestFirst == option.Longest,
            };
            item.Click += (_, _) =>
            {
                _audiobookChaptersLongestFirst = option.Longest;
                AudiobookChapterSortText.Text = option.Label;
                RenderAudiobookChapters();
            };
            flyout.Items.Add(item);
        }
        flyout.ShowAt(AudiobookChapterSortButton);
    }

    private void RenderAudiobookChapters()
    {
        AudiobookChaptersPanel.Children.Clear();
        var position = Math.Max(0, ViewModel.Item?.UserData?.PositionSeconds ?? 0);
        var current = FindAudiobookChapter(position);
        var rows = _audiobookChaptersLongestFirst
            ? _audiobookChapterRows.OrderByDescending(row => row.DurationSeconds)
            : _audiobookChapterRows.AsEnumerable();
        foreach (var chapter in rows)
        {
            var isCurrent = ReferenceEquals(chapter, current);
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Tag = chapter,
                Background = isCurrent ? (Brush)Application.Current.Resources["SurfaceRaisedBrush"] : null,
            };
            AutomationProperties.SetName(button, $"Play {chapter.Label}");

            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 11, 16, 11) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var indicator = new Border
            {
                Background = isCurrent ? (Brush)Application.Current.Resources["AccentBrush"] : null,
                CornerRadius = new CornerRadius(1),
            };
            grid.Children.Add(indicator);
            var number = new TextBlock
            {
                Text = chapter.PositionIndex.ToString(),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            };
            Grid.SetColumn(number, 1);
            grid.Children.Add(number);
            var title = new TextBlock
            {
                Text = chapter.Label,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(title, 2);
            grid.Children.Add(title);
            var time = new TextBlock
            {
                Text = FormatChapterStart(chapter.AbsoluteStart),
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(time, 3);
            grid.Children.Add(time);
            var duration = new TextBlock
            {
                Text = FormatChapterDuration(chapter.DurationSeconds),
                FontFamily = new FontFamily("Consolas"),
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(duration, 4);
            grid.Children.Add(duration);
            if (isCurrent)
            {
                var listening = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                listening.Children.Add(new FontIcon { Glyph = "\uE768", FontSize = 11, Foreground = (Brush)Application.Current.Resources["AccentBrush"] });
                listening.Children.Add(new TextBlock { Text = "listening", FontSize = 12, Foreground = (Brush)Application.Current.Resources["AccentBrush"] });
                Grid.SetColumn(listening, 5);
                grid.Children.Add(listening);
            }
            button.Content = grid;
            button.Click += AudiobookChapter_Click;
            AudiobookChaptersPanel.Children.Add(button);
        }
    }

    private async void AudiobookChapter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AudiobookChapterRow chapter } || ViewModel.Item == null) return;
        await App.Services.GetRequiredService<Services.PlayerService>().PlayAsync(
            ViewModel.Item.ContentId,
            fileId: chapter.FileId,
            startPositionOverride: chapter.AbsoluteStart);
    }

    private sealed record AudiobookChapterRow(
        int PositionIndex,
        string Label,
        double AbsoluteStart,
        double DurationSeconds,
        int FileId);

    private void BuildMangaChapters(MediaItemDetail item)
    {
        MangaChaptersPanel.Children.Clear();
        _mangaVolumes.Clear();
        _mangaResumeRow = null;
        var chapters = item.Manga?.Chapters
            .OrderBy(chapter => MangaVolumeSort(chapter.Volume))
            .ThenBy(chapter => chapter.ChapterIndex ?? double.MaxValue)
            .ThenBy(chapter => chapter.Title, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var target = chapters.FirstOrDefault(chapter => chapter.Read != true) ?? chapters.FirstOrDefault();
        _readerTargetContentId = target?.ContentId;
        SplitPlayButton.Visibility = target != null ? Visibility.Visible : Visibility.Collapsed;
        PlayButtonIcon.Glyph = "\uE736";
        PlayButtonText.Text = target == null ? "No chapters" : target.Progress > 0
            ? $"Resume Reading  {MangaChapterLabel(target)}"
            : chapters.Any(chapter => chapter.Read == true) && target.Read != true
                ? $"Continue  {MangaChapterLabel(target)}"
                : chapters.All(chapter => chapter.Read == true)
                    ? $"Read Again  {MangaChapterLabel(target)}"
                    : $"Start Reading  {MangaChapterLabel(target)}";
        MangaProgressText.Text = chapters.Count == 0 ? "" : $"{chapters.Count(chapter => chapter.Read == true)} of {chapters.Count} read";
        MangaJumpButton.Visibility = chapters.Count > 10 && target != null
            ? Visibility.Visible
            : Visibility.Collapsed;
        MangaJumpButtonText.Text = target == null ? "" : $"Jump to {MangaChapterLabel(target)}";

        foreach (var group in chapters.GroupBy(
                     chapter => string.IsNullOrWhiteSpace(chapter.Volume) ? "" : chapter.Volume.Trim(),
                     StringComparer.OrdinalIgnoreCase))
        {
            var groupedChapters = group.ToList();
            if (group.Key.Length == 0 || groupedChapters.Count == 1)
            {
                foreach (var chapter in groupedChapters)
                {
                    var row = CreateMangaChapterRow(chapter);
                    if (chapter.ContentId == target?.ContentId) _mangaResumeRow = row;
                    MangaChaptersPanel.Children.Add(row);
                }
                continue;
            }

            var allRead = groupedChapters.All(chapter => chapter.Read == true);
            var header = new Grid { Margin = new Thickness(14, 8, 10, 8), ColumnSpacing = 10 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = $"Volume {group.Key}",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 35,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (allRead)
                summary.Children.Add(new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 13,
                    Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                });
            summary.Children.Add(new TextBlock
            {
                Text = $"{groupedChapters.Count} chapters",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            Grid.SetColumn(summary, 1);
            header.Children.Add(summary);

            var rows = new StackPanel { Spacing = 0, Margin = new Thickness(16, 0, 0, 0) };
            foreach (var chapter in groupedChapters)
            {
                var row = CreateMangaChapterRow(chapter);
                if (chapter.ContentId == target?.ContentId) _mangaResumeRow = row;
                rows.Children.Add(row);
            }

            var expander = new Expander
            {
                Header = header,
                Content = rows,
                IsExpanded = !allRead,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            _mangaVolumes.Add((expander, $"Volume {group.Key} · {groupedChapters.Count} chapters"));
            expander.Collapsed += (_, _) => UpdateMangaStickyHeader();
            MangaChaptersPanel.Children.Add(expander);
        }
        if (chapters.Count == 0)
            MangaChaptersPanel.Children.Add(new TextBlock { Text = "No chapters found. Chapters appear here once the library scan completes.", Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        MangaChaptersSection.Visibility = Visibility.Visible;
    }

    private async void MangaJumpButton_Click(object sender, RoutedEventArgs e)
    {
        var target = _mangaResumeRow;
        if (target == null || !target.IsLoaded) return;
        var cancellation = _navigationCts?.Token ?? CancellationToken.None;
        try
        {
            // Expander reopening animates its content transform after layout
            // already reports the final height. Bringing that transient row
            // into view centers the wrong position once the animation settles.
            double Position() => target.TransformToVisual(MangaChaptersPanel).TransformPoint(new Windows.Foundation.Point()).Y;
            var position = Position(); var stableFrames = 0;
            for (var frame = 0; frame < 40 && stableFrames < 3; frame++)
            {
                await Task.Delay(25, cancellation);
                if (!target.IsLoaded || !ReferenceEquals(target, _mangaResumeRow)) return;
                var next = Position();
                stableFrames = Math.Abs(next - position) < .5 ? stableFrames + 1 : 0;
                position = next;
            }
        }
        catch (OperationCanceledException) { return; }
        target.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.5,
        });
    }

    private Border CreateMangaChapterRow(MangaChapter chapter)
    {
        var row = new Grid { ColumnSpacing = 10, Margin = new Thickness(10, 7, 6, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var imageHost = new Grid { Width = 32, Height = 48 };
        var placeholder = new FontIcon
        {
            Glyph = "\uE736",
            FontSize = 17,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var image = new Image { Stretch = Stretch.UniformToFill };
        imageHost.Children.Add(placeholder);
        imageHost.Children.Add(image);
        row.Children.Add(new Border
        {
            Width = 32,
            Height = 48,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            Child = imageHost,
        });
        if (!string.IsNullOrWhiteSpace(chapter.PosterUrl))
            _ = LoadMangaChapterPosterAsync(image, placeholder, chapter);

        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = MangaChapterLabel(chapter),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources[chapter.Read == true ? "SecondaryTextBrush" : "PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (chapter.Read != true && chapter.Progress is > 0 and < 1)
        {
            var progressRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            var track = new Grid { Width = 64, Height = 4, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["BorderBrush"],
                CornerRadius = new CornerRadius(2),
            });
            track.Children.Add(new Border
            {
                Width = 64 * Math.Clamp(chapter.Progress.Value, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = (Brush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(2),
            });
            progressRow.Children.Add(track);
            progressRow.Children.Add(new TextBlock
            {
                Text = $"{Math.Round(chapter.Progress.Value * 100):0}%",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            labels.Children.Add(progressRow);
        }

        var readButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Tag = chapter.ContentId,
            Content = labels,
        };
        readButton.Click += MangaRead_Click;
        Grid.SetColumn(readButton, 1);
        row.Children.Add(readButton);
        var state = new FontIcon
        {
            Glyph = chapter.Read == true ? "\uE73E" : "",
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["AccentBrush"],
        };
        Grid.SetColumn(state, 2);
        row.Children.Add(state);
        var toggle = new Button
        {
            Content = new FontIcon { Glyph = "\uE73E", FontSize = 14 },
            Tag = chapter,
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
        };
        ToolTipService.SetToolTip(toggle, chapter.Read == true ? "Mark chapter unread" : "Mark chapter read");
        toggle.Click += MangaWatched_Click;
        Grid.SetColumn(toggle, 3);
        row.Children.Add(toggle);

        if (CanCurrentUserDownload())
        {
            var download = new Button
            {
                Content = new FontIcon { Glyph = "\uE896", FontSize = 14 },
                Tag = chapter,
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
            };
            ToolTipService.SetToolTip(download, "Download chapter");
            download.Click += MangaDownload_Click;
            Grid.SetColumn(download, 4);
            row.Children.Add(download);
        }

        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row,
        };
    }

    private async Task LoadMangaChapterPosterAsync(Image image, FrameworkElement placeholder, MangaChapter chapter)
    {
        try
        {
            var bytes = await App.Services.GetRequiredService<ImageService>().GetImageAsync(
                chapter.ContentId,
                "poster",
                chapter.PosterUrl!,
                App.Services.GetRequiredService<HttpClient>(),
                CancellationToken.None);
            if (bytes == null) return;
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 64,
                DecodePixelType = DecodePixelType.Logical,
            };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            image.Source = bitmap;
            placeholder.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void MangaRead_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string contentId })
            App.Services.GetRequiredService<NavigationService>().Navigate<EbookReaderPage>(new EbookReaderNavigation(contentId));
    }

    private async void MangaWatched_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MangaChapter chapter } button || ViewModel.Item == null) return;
        var parentContentId = ViewModel.Item.ContentId;
        var navigationToken = _navigationCts?.Token;
        if (navigationToken == null || !IsActiveDetail(parentContentId, navigationToken.Value)) return;
        button.IsEnabled = false;
        try
        {
            var api = App.Services.GetRequiredService<CatalogApi>();
            if (chapter.Read == true) await api.MarkUnwatchedAsync(chapter.ContentId, navigationToken.Value);
            else await api.MarkWatchedAsync(chapter.ContentId, navigationToken.Value);
            if (!IsActiveDetail(parentContentId, navigationToken.Value)) return;
            await ViewModel.ReloadAsync(parentContentId);
            if (!IsActiveDetail(parentContentId, navigationToken.Value)) return;
            UpdateUI();
        }
        catch (OperationCanceledException) when (navigationToken.Value.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<Services.ToastService>().Error(ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void MangaDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MangaChapter chapter } button || !CanCurrentUserDownload()) return;
        button.IsEnabled = false;
        try
        {
            var versions = await App.Services.GetRequiredService<CatalogApi>()
                .GetItemVersionsAsync(chapter.ContentId);
            if (versions.Count == 0)
            {
                App.Services.GetRequiredService<Services.ToastService>()
                    .Error("No downloadable files for this chapter.");
                return;
            }
            await ShowDownloadDialogAsync(MangaChapterLabel(chapter), versions);
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<Services.ToastService>().Error(ex.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static string MangaChapterLabel(MangaChapter chapter)
        => !string.IsNullOrWhiteSpace(chapter.Title) ? chapter.Title
            : chapter.ChapterIndex.HasValue ? $"Chapter {chapter.ChapterIndex:0.##}" : "Chapter";

    private static double MangaVolumeSort(string? volume)
        => double.TryParse(volume?.TrimStart('v', 'V'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : double.MaxValue;

    private static string FormatBookDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
            : span.TotalMinutes >= 1
                ? $"{span.Minutes}m {span.Seconds:00}s"
                : $"{span.Seconds}s";
    }

    private static string FormatClock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
    }

    private static string FormatChapterStart(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
    }

    private static string FormatChapterDuration(double seconds)
    {
        if (seconds <= 0) return "";
        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalMinutes}m {span.Seconds:00}s";
    }

    // ===== Scores Row =====

    private async Task ConfigureOnViewTranslationAsync(MediaItemDetail? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.PendingTranslationLanguage))
        {
            TranslateOverviewButton.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var status = await App.Services.GetRequiredService<CatalogApi>().GetMetadataAiStatusAsync();
            if (!status.Enabled)
            {
                TranslateOverviewButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (status.OnView.Equals("button", StringComparison.OrdinalIgnoreCase))
            {
                _translateButtonMode = true;
                TranslateOverviewButton.Visibility = Visibility.Visible;
                TranslateOverviewButton.IsEnabled = true;
                TranslateOverviewButton.Content = "Translate description";
            }
            else if (status.OnView.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                _translateButtonMode = false;
                TranslateOverviewButton.Visibility = Visibility.Collapsed;
                await TranslateOverviewAsync(item);
            }
            else
            {
                _translateButtonMode = false;
                TranslateOverviewButton.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            TranslateOverviewButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void TranslateOverviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item != null)
            await TranslateOverviewAsync(ViewModel.Item);
    }

    private async Task TranslateOverviewAsync(MediaItemDetail item)
    {
        if (_isTranslatingOverview
            || string.IsNullOrWhiteSpace(item.PendingTranslationLanguage)
            || !IsCurrentDetail(item.ContentId)) return;
        _isTranslatingOverview = true;
        _translationCts?.Cancel();
        _translationCts?.Dispose();
        _translationCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = _translationCts.Token;
        TranslateOverviewButton.Visibility = Visibility.Visible;
        TranslateOverviewButton.IsEnabled = false;
        TranslateOverviewButton.Content = "Translating…";
        OverviewText.Opacity = 0.55;

        try
        {
            var api = App.Services.GetRequiredService<CatalogApi>();
            await api.TranslateItemDescriptionAsync(item.ContentId, item.PendingTranslationLanguage!, ct);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                if (!IsActiveDetail(item.ContentId, ct)) return;
                await ViewModel.ReloadAsync(item.ContentId);
                if (!IsActiveDetail(item.ContentId, ct)) return;
                var refreshed = ViewModel.Item;
                if (refreshed == null) continue;
                OverviewText.Text = refreshed.Overview ?? "";
                if (string.IsNullOrWhiteSpace(refreshed.PendingTranslationLanguage))
                {
                    UpdateUI();
                    TranslateOverviewButton.Visibility = Visibility.Collapsed;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The page was left or the WebUI-equivalent 45 second poll window elapsed.
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<Services.ToastService>().Error(ex.Message);
        }
        finally
        {
            _isTranslatingOverview = false;
            if (IsCurrentDetail(item.ContentId))
            {
                OverviewText.Opacity = 1;
                if (_translateButtonMode && !string.IsNullOrWhiteSpace(ViewModel.Item?.PendingTranslationLanguage))
                {
                    TranslateOverviewButton.Visibility = Visibility.Visible;
                    TranslateOverviewButton.IsEnabled = true;
                    TranslateOverviewButton.Content = "Translate description";
                }
            }
        }
    }

    private void UpdateScoresRow(MediaItemDetail item)
    {
        // Server order and display values are authoritative, including unknown
        // providers. An empty list deliberately hides legacy scalar scores.
        ImdbScorePanel.Visibility = RtCriticPanel.Visibility = RtAudiencePanel.Visibility = Visibility.Collapsed;
        ScoresPanel.Children.Clear();
        var entries = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 20, VerticalSpacing = 8 };
        foreach (var rating in item.Ratings ?? [])
            entries.Children.Add(SiloPlayer.Controls.DisplayRatingEntry.Create(rating));
        ScoresPanel.Children.Add(entries);
        ScoresPanel.Visibility = entries.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMetadataBadgeTheme()
    {
        // app.css .metadata-badge is intentionally theme-derived rather than
        // a solid generic surface: foreground/8 fill, border/55 outline, and
        // a foreground-to-muted text mix. Recreate those colors from the
        // active server theme whenever a detail item is bound.
        var foreground = (Application.Current.Resources["PrimaryTextBrush"] as SolidColorBrush)?.Color
            ?? Microsoft.UI.Colors.White;
        var muted = (Application.Current.Resources["SecondaryTextBrush"] as SolidColorBrush)?.Color
            ?? Microsoft.UI.Colors.Gray;
        var borderColor = (Application.Current.Resources["BorderBrush"] as SolidColorBrush)?.Color
            ?? Microsoft.UI.Colors.Gray;

        static byte Mix(byte first, byte second, double firstWeight) =>
            (byte)Math.Clamp(Math.Round((first * firstWeight) + (second * (1 - firstWeight))), 0, 255);

        var fillBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            0x14, foreground.R, foreground.G, foreground.B));
        var borderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            0x8C, borderColor.R, borderColor.G, borderColor.B));
        var textBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            0xFF,
            Mix(foreground.R, muted.R, 0.72),
            Mix(foreground.G, muted.G, 0.72),
            Mix(foreground.B, muted.B, 0.72)));

        foreach (var badge in new[]
                 {
                     YearBadge, ContentRatingBadge, RuntimeBadge,
                     SeasonCountBadge, EpisodeCountBadge,
                 })
        {
            badge.Background = fillBrush;
            badge.BorderBrush = borderBrush;
        }

        foreach (var label in new[]
                 {
                     YearText, ContentRatingText, RuntimeText,
                     SeasonCountText, EpisodeCountText,
                 })
        {
            label.Foreground = textBrush;
        }
    }

    // ===== Quality Badges =====

    private void UpdateQualityBadges()
    {
        QualityBadgesPanel.Children.Clear();

        if (_watchDetail == null || _watchDetail.Versions.Count == 0)
        {
            QualityBadgesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var manager = App.Services.GetRequiredService<PlaybackManager>();
        var best = _selectedVersion ?? manager.SelectBestVersion(_watchDetail.Versions, userData: _watchDetail.UserData);
        if (best == null)
        {
            QualityBadgesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        // Resolution badge (e.g., "2160p", "1080p")
        if (!string.IsNullOrEmpty(best.Resolution))
        {
            QualityBadgesPanel.Children.Add(CreateQualityBadge(
                best.Resolution,
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeResolutionBrush"]));
        }

        var videoRange = MediaVideoRange.Label(best);
        if (!string.IsNullOrEmpty(videoRange))
        {
            QualityBadgesPanel.Children.Add(CreateQualityBadge(
                videoRange,
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeHdrBrush"]));
        }

        // QualityBadges.tsx receives audioLabel from pickBestAttributes().
        // Keep that exact ranking contract here: codecs not ranked by the
        // current WebUI (for example plain AC3) do not produce a hero badge,
        // while DTS/EAC3/TrueHD/Atmos still do.
        var audioLabel = VersionRanking.PickBestAttributes([best], qualityPreference: null)?.AudioLabel;
        if (!string.IsNullOrEmpty(audioLabel))
        {
            QualityBadgesPanel.Children.Add(CreateQualityBadge(
                audioLabel!,
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeBackgroundBrush"],
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeTextBrush"]));
        }

        QualityBadgesPanel.Visibility = QualityBadgesPanel.Children.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Border CreateQualityBadge(
        string text,
        Microsoft.UI.Xaml.Media.Brush background,
        Microsoft.UI.Xaml.Media.Brush? foreground = null)
    {
        // QualityBadges.tsx uses the active server primary color for every
        // badge (10% fill, 20% border, full-color text). Derive those brushes
        // at creation time so a server theme change cannot leave the desktop
        // badges in the old hard-coded green/orange palette.
        var accent = Application.Current.Resources["AccentBrush"] as Microsoft.UI.Xaml.Media.SolidColorBrush
            ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        var accentColor = accent.Color;
        var fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            0x1A, accentColor.R, accentColor.G, accentColor.B));
        var border = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            0x33, accentColor.R, accentColor.G, accentColor.B));
        return new Border
        {
            Background = fill,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 40,
                Foreground = accent
            }
        };
    }

    // ===== Watched Toggle =====

    private void UpdateWatchedButton()
    {
        var type = ViewModel.Item?.Type;
        var compactTv = _tvViewport != null && ActualWidth < 1024;
        if (ViewModel.IsWatched)
        {
            WatchedIcon.Glyph = "\uE73E"; // Checkmark
            WatchedText.Text = compactTv ? (ViewModel.IsWatched ? "Mark Unwatched" : "Mark Watched") : type switch
            {
                "series" => "Mark Series Unwatched",
                "season" => "Mark Season Unwatched",
                "ebook" => "Mark Unread",
                _ => "Mark Unwatched",
            };
            WatchedIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            WatchedIcon.Glyph = "\uE73E"; // Checkmark outline
            WatchedText.Text = compactTv ? (ViewModel.IsWatched ? "Mark Unwatched" : "Mark Watched") : type switch
            {
                "series" => "Mark Series Watched",
                "season" => "Mark Season Watched",
                "ebook" => "Mark Read",
                _ => "Mark Watched",
            };
            WatchedIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
        }
    }

    private async void WatchedButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleWatchedCommand.ExecuteAsync(null);
        UpdateWatchedButton();
    }

    // ===== Star Rating =====

    /// <summary>
    /// The currently hovered star index (1-5), or null when the pointer
    /// isn't over the rating widget. Drives the hover-preview fill so users
    /// see what their click will commit to. Mirrors the webui <c>hoverValue</c>
    /// state in <c>StarRating.tsx</c>.
    /// </summary>
    private int? _starHoverValue;

    private void UpdateStarRating()
    {
        // Use the hover value when active; otherwise the committed rating.
        int? effective = _starHoverValue ?? ViewModel.UserRating;
        FontIcon[] stars = [Star1Icon, Star2Icon, Star3Icon, Star4Icon, Star5Icon];
        Button[] starButtons = [Star1, Star2, Star3, Star4, Star5];
        var tabbableStar = Math.Clamp(ViewModel.UserRating ?? 1, 1, 5);

        // The WebUI deliberately keeps rating stars yellow-400 regardless of
        // the active server accent/theme. Using AccentBrush here made a white
        // themed server render selected stars white instead of matching the
        // WebUI's fixed rating color.
        var highlightBrush = new SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFA, 0xCC, 0x15));

        for (int i = 0; i < 5; i++)
        {
            bool filled = effective != null && (i + 1) <= effective;
            stars[i].Glyph = filled ? "\uE735" : "\uE734"; // E735=filled, E734=outline
            stars[i].Foreground = filled
                ? highlightBrush
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"];
            starButtons[i].IsTabStop = i + 1 == tabbableStar;
            AutomationProperties.SetItemStatus(starButtons[i],
                ViewModel.UserRating == i + 1 ? "Selected" : "Not selected");
        }
    }

    private async void Star_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag
            || !int.TryParse(tag, out var current))
            return;

        var isNavigationKey = e.Key is Windows.System.VirtualKey.Right
            or Windows.System.VirtualKey.Up
            or Windows.System.VirtualKey.Left
            or Windows.System.VirtualKey.Down
            or Windows.System.VirtualKey.Home
            or Windows.System.VirtualKey.End;
        if (!isNavigationKey)
            return;

        var next = e.Key switch
        {
            Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up => Math.Min(5, current + 1),
            Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Down => Math.Max(1, current - 1),
            Windows.System.VirtualKey.Home => 1,
            Windows.System.VirtualKey.End => 5,
            _ => current,
        };

        e.Handled = true;
        if (next != current)
        {
            await ViewModel.SetRatingCommand.ExecuteAsync(next);
            _starHoverValue = null;
            UpdateStarRating();
        }
        Button[] starButtons = [Star1, Star2, Star3, Star4, Star5];
        starButtons[next - 1].Focus(FocusState.Keyboard);
    }

    private async void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tagStr || !int.TryParse(tagStr, out int rating))
            return;

        // Toggle-off: clicking the currently-selected star clears the rating
        // (matches webui behavior + passes the "clicking active star clears"
        // test case). The ViewModel's SetRatingAsync already detects this by
        // comparing current vs new — calling it with the same value clears.
        await ViewModel.SetRatingCommand.ExecuteAsync(rating);
        _starHoverValue = null;
        UpdateStarRating();
    }

    /// <summary>
    /// Hover enter on a star → set preview index and repaint. Fires on each
    /// star's PointerEntered so moving across the row updates smoothly.
    /// </summary>
    private void Star_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int rating))
        {
            _starHoverValue = rating;
            UpdateStarRating();
        }
    }

    /// <summary>
    /// Pointer exited the whole container (not just a single star) — clear
    /// hover state and snap back to the committed rating.
    /// </summary>
    private void StarPanel_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _starHoverValue = null;
        UpdateStarRating();
    }

    // ===== Favorite & Watchlist =====

    private void UpdateFavoriteButton()
    {
        // Segoe Fluent: \uEB52 = heart filled, \uEB51 = heart outline. WebUI uses a Heart
        // icon with text-red-400 fill-current when favorited — matches the red tint below.
        FavoriteIcon.Glyph = ViewModel.IsFavorite ? "\uEB52" : "\uEB51";
        FavoriteIcon.Foreground = ViewModel.IsFavorite
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF8, 0x71, 0x71)) // text-red-400
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
        ToolTipService.SetToolTip(FavoriteButton, ViewModel.IsFavorite ? "Unfavorite" : "Favorite");
        AutomationProperties.SetName(FavoriteButton, ViewModel.IsFavorite ? "Unfavorite" : "Favorite");
    }

    private void UpdateWatchlistButton()
    {
        // Legacy — kept for any remaining references but no longer visible.
    }

    /// <summary>
    /// Populate the "More" kebab flyout with secondary actions: Watchlist,
    /// Mark Watched, admin-only Refresh Metadata. Rebuilds each time the
    /// item loads so labels/icons reflect current state.
    /// </summary>
    private void RetryDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContentId == null) return;
        App.Services.GetRequiredService<ItemDetailPrefetchCache>().Invalidate(_currentContentId);
        Frame?.Navigate(typeof(ItemDetailPage), _currentContentId);
    }

    private async Task SendDetailToRoomAsync(SiloPlayer.ViewModels.WatchTogetherRoomViewModel room)
    {
        if (ViewModel.Item == null || room.RoomId == null || room.RoomToken == null) return;
        var item = ViewModel.Item;
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var authority = client.CaptureContext();
        var contentId = await ResolvePlayableContentIdForPlaybackAsync();
        if (contentId == null || !client.IsCurrentContext(authority) || ViewModel.Item != item) return;
        try
        {
            var api = App.Services.GetRequiredService<PlaybackApi>();
            if (room.IsHost && !room.IsVoteMode && room.Room?.Phase == "lobby")
                room.Room = (await api.StageWatchTogetherRoomItemAsync(room.RoomId, contentId, _selectedVersion?.FileId)).Room;
            else await api.CreateWatchTogetherSuggestionAsync(room.RoomId, room.RoomToken, contentId, ViewModel.Item.Type == "movie" ? "movie" : "episode", ViewModel.Item.Title, posterUrl: ViewModel.Item.PosterUrl);
            App.Services.GetRequiredService<NavigationService>().Navigate<WatchTogetherRoomPage>(new WatchTogetherRoomNavigationArgs { RoomId = room.RoomId, RoomAccessToken = room.RoomToken });
        }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error($"Could not queue this title: {ex.Message}"); }
    }
    private async Task ShowStartPartyAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var api = App.Services.GetRequiredService<PlaybackApi>();
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var authority = client.CaptureContext();
        try
        {
            var capabilities = await api.GetWatchTogetherCapabilitiesAsync();
            if (!client.IsCurrentContext(authority) || !IsCurrentDetail(item.ContentId)) return;
            if (!capabilities.Allowed || capabilities.State != "available" || !capabilities.StagedSelection) { App.Services.GetRequiredService<ToastService>().Error("Watch Together is unavailable on this server."); return; }
            var target = await ResolvePlayableContentIdForPlaybackAsync();
            var panel = new StackPanel { Spacing = 14, Width = Math.Min(400, Math.Max(240, XamlRoot.Size.Width - 100)) };
            panel.Children.Add(new TextBlock { Text = item.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var targetChoice = new ComboBox { Header = "Episode", HorizontalAlignment = HorizontalAlignment.Stretch };
            if (item.Type is "series" or "season")
            {
                if (item.Type == "series")
                    foreach (var season in ViewModel.Seasons)
                        foreach (var ep in (await App.Services.GetRequiredService<CatalogApi>().GetEpisodesAsync(item.ContentId, season.SeasonNumber)).Episodes)
                            targetChoice.Items.Add(new ComboBoxItem { Content = $"S{season.SeasonNumber} E{ep.EpisodeNumber} · {ep.Title}", Tag = ep.ContentId });
                else
                    foreach (var ep in ViewModel.Episodes) targetChoice.Items.Add(new ComboBoxItem { Content = $"E{ep.EpisodeNumber} · {ep.Title}", Tag = ep.ContentId });
                targetChoice.SelectedItem = targetChoice.Items.Cast<ComboBoxItem>().FirstOrDefault(e => Equals(e.Tag, target)) ?? targetChoice.Items.Cast<ComboBoxItem>().FirstOrDefault();
                panel.Children.Add(targetChoice);
            }
            var mode = new ComboBox { Header = "How to pick", SelectedIndex = 0, Items = { "Host picks · Stage this for the room", "Everyone votes · Add this as a suggestion" }, HorizontalAlignment = HorizontalAlignment.Stretch };
            var pause = new ToggleSwitch { Header = "Guests can pause", IsOn = false };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["ErrorBrush"], Visibility = Visibility.Collapsed };
            panel.Children.Add(mode); panel.Children.Add(pause); panel.Children.Add(error);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Start a party with this", Content = panel, PrimaryButtonText = "Create room", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true; var deferral = args.GetDeferral(); dialog.IsPrimaryButtonEnabled = false;
                try
                {
                    if (!client.IsCurrentContext(authority)) return;
                    target = targetChoice.SelectedItem is ComboBoxItem { Tag: string chosen } ? chosen : target;
                    var response = await api.CreateWatchTogetherRoomAsync(mode.SelectedIndex == 1 ? "vote" : "host_pick");
                    if (!client.IsCurrentContext(authority) || response.RoomAccessToken == null) return;
                    try { if (pause.IsOn) await api.UpdateWatchTogetherRoomPolicyAsync(response.Room.RoomId, "guest_play_pause"); }
                    catch { App.Services.GetRequiredService<ToastService>().Error("Room created. Guest pause could not be enabled; change it from the room."); }
                    try
                    {
                        if (target != null)
                            if (mode.SelectedIndex == 0) await api.StageWatchTogetherRoomItemAsync(response.Room.RoomId, target, item.Type is "movie" or "episode" ? _selectedVersion?.FileId : null);
                            else await api.CreateWatchTogetherSuggestionAsync(response.Room.RoomId, response.RoomAccessToken, target, item.Type == "movie" ? "movie" : "episode", item.Title, posterUrl: item.PosterUrl);
                    }
                    catch { App.Services.GetRequiredService<ToastService>().Error("Room created. This title could not be queued; choose it from the room."); }
                    if (!client.IsCurrentContext(authority)) return;
                    await RecentPartyStore.RememberAsync(client, App.Services.GetRequiredService<AuthService>(), response);
                    args.Cancel = false;
                    DispatcherQueue.TryEnqueue(() => App.Services.GetRequiredService<NavigationService>().Navigate<WatchTogetherRoomPage>(new WatchTogetherRoomNavigationArgs { RoomId = response.Room.RoomId, RoomAccessToken = response.RoomAccessToken }));
                }
                catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
                finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
    }

    private void BuildMoreFlyout()
    {
        MoreFlyout.Items.Clear();
        var item = ViewModel.Item;
        if (item == null) return;
        var hasOverflowActions = false;
        if (_tvViewport != null && ActualWidth < 1024)
        {
            if (item.Type != "season")
            {
                var favorite = new MenuFlyoutItem { Text = ViewModel.IsFavorite ? "Remove from favorites" : "Add to favorites" };
                favorite.Click += async (_, _) =>
                {
                    await ViewModel.ToggleFavoriteCommand.ExecuteAsync(null);
                    UpdateFavoriteButton(); BuildMoreFlyout();
                };
                MoreFlyout.Items.Add(favorite);
            }
            if (item.Type == "series")
            {
                var rate = new MenuFlyoutSubItem { Text = "Rate" };
                for (var value = 1; value <= 5; value++)
                {
                    var rating = value;
                    var star = new MenuFlyoutItem { Text = $"{rating} {(rating == 1 ? "star" : "stars")}" };
                    star.Click += async (_, _) =>
                    {
                        await ViewModel.SetRatingCommand.ExecuteAsync(rating);
                        UpdateStarRating(); BuildMoreFlyout();
                    };
                    rate.Items.Add(star);
                }
                MoreFlyout.Items.Add(rate);
            }
            hasOverflowActions = true;
        }
        if (item.Type is "movie" or "series" or "season" or "episode" &&
            AuthorizationPolicy.CanCurateMetadata(App.Services.GetRequiredService<AuthService>()))
        {
            var edit = new MenuFlyoutItem { Text = "Edit Metadata", Icon = new FontIcon { Glyph = "\uE70F" } };
            edit.Click += async (_, _) =>
            {
                var dialog = new SiloPlayer.Views.Dialogs.EditMetadataDialog(item) { XamlRoot = XamlRoot };
                await dialog.ShowAsync();
                if (dialog.HasSaved) { await ViewModel.LoadCommand.ExecuteAsync(item.ContentId); UpdateUI(); }
            };
            MoreFlyout.Items.Add(edit); hasOverflowActions = true;
        }
        if (item.Type is "movie" or "series" or "season" or "episode")
        {
            var party = new MenuFlyoutItem { Text = "Start a party with this", Icon = new FontIcon { Glyph = "\uE716" } };
            party.Click += async (_, _) => await ShowStartPartyAsync();
            MoreFlyout.Items.Add(party);
            hasOverflowActions = true;
            var active = App.Services.GetRequiredService<WatchTogetherCoordinator>().ActiveRoom;
            if (active?.Room is { Phase: not "ended" } && active.RoomId != null && active.RoomToken != null)
            {
                var go = new MenuFlyoutItem { Text = "Go to live room" };
                go.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<WatchTogetherRoomPage>(new WatchTogetherRoomNavigationArgs { RoomId = active.RoomId, RoomAccessToken = active.RoomToken });
                MoreFlyout.Items.Add(go);
                var suggest = new MenuFlyoutItem { Text = active.IsHost ? "Stage this in live room" : "Suggest this to live room" };
                suggest.Click += async (_, _) => await SendDetailToRoomAsync(active);
                MoreFlyout.Items.Add(suggest);
            }
        }


        if (item.Type.Equals("manga", StringComparison.OrdinalIgnoreCase))
        {
            var details = new MenuFlyoutItem
            {
                Text = "View Details",
                Icon = new FontIcon { Glyph = "\uE946" },
            };
            details.Click += async (_, _) => await ShowMangaDetailsDialogAsync();
            MoreFlyout.Items.Add(details);
            var mangaMaintenance = ItemMaintenanceActionPolicy.Resolve(
                AuthorizationPolicy.CanCurateMetadata(
                    App.Services.GetRequiredService<Core.Services.AuthService>()),
                item.Type);
            if (mangaMaintenance.CanRefreshMetadata)
            {
                MoreFlyout.Items.Add(new MenuFlyoutSeparator());
                var refresh = new MenuFlyoutItem { Text = "Refresh Metadata", Icon = new FontIcon { Glyph = "\uE72C" } };
                refresh.Click += async (_, _) => await ShowRefreshMetadataDialogAsync();
                MoreFlyout.Items.Add(refresh);
            }
            return;
        }

        if (item.UserData is { PositionSeconds: > 0, Played: false })
        {
            var restart = new MenuFlyoutItem
            {
                Text = "Play from Beginning",
                Icon = new FontIcon { Glyph = "\uE777" },
            };
            restart.Click += (_, _) => PlayFromStart_Click(restart, new RoutedEventArgs());
            MoreFlyout.Items.Add(restart);
            hasOverflowActions = true;
        }

        if (item.Type is "movie" or "series")
        {
            var wlItem = new MenuFlyoutItem
            {
                Text = ViewModel.InWatchlist ? "Remove from Watchlist" : "Add to Watchlist",
                Icon = new FontIcon { Glyph = ViewModel.InWatchlist ? "\uE73E" : "\uE710" },
            };
            wlItem.Click += async (_, _) =>
            {
                await ViewModel.ToggleWatchlistCommand.ExecuteAsync(null);
                BuildMoreFlyout();
            };
            MoreFlyout.Items.Add(wlItem);
            hasOverflowActions = true;
        }

        if (item.Type == "series" && _requestSeasonsContentId == item.ContentId && int.TryParse(item.TmdbId, out var tmdbId))
        {
            var request = new MenuFlyoutItem { Text = "Request seasons", Icon = new FontIcon { Glyph = "\uE710" } };
            request.Click += async (_, _) => await RequestSeriesSeasonsAsync(item, tmdbId);
            MoreFlyout.Items.Add(request);
            hasOverflowActions = true;
        }

        var addToCollection = new MenuFlyoutItem
        {
            Text = "Add to Collection",
            Icon = new FontIcon { Glyph = "\uE8B7" },
        };
        addToCollection.Click += async (_, _) => await ShowAddToCollectionDialogAsync();
        MoreFlyout.Items.Add(addToCollection);

        if (_selectedVersion != null)
        {
            if (CanCurrentUserDownload())
            {
                var download = new MenuFlyoutItem
                {
                    Text = "Download",
                    Icon = new FontIcon { Glyph = "\uE896" },
                };
                download.Click += DownloadButton_Click;
                MoreFlyout.Items.Add(download);
                hasOverflowActions = true;
            }

            var searchSubtitles = new MenuFlyoutItem
            {
                Text = "Search Subtitles",
                Icon = new FontIcon { Glyph = "\uED1E" },
            };
            searchSubtitles.Click += async (_, _) => await OpenSubtitleSearchDialogAsync();
            MoreFlyout.Items.Add(searchSubtitles);
            hasOverflowActions = true;
        }

        if (_selectedVersion != null)
        {
            if (hasOverflowActions)
                MoreFlyout.Items.Add(new MenuFlyoutSeparator());

            var mediaInfo = new MenuFlyoutItem
            {
                Text = "Media Info",
                Icon = new FontIcon { Glyph = "\uE946" },
            };
            mediaInfo.Click += async (_, _) => await ShowMediaInfoDialogAsync();
            MoreFlyout.Items.Add(mediaInfo);
            hasOverflowActions = true;
        }

        var maintenance = ItemMaintenanceActionPolicy.Resolve(
            AuthorizationPolicy.CanCurateMetadata(
                App.Services.GetRequiredService<Core.Services.AuthService>()),
            item.Type);
        if (maintenance.CanMatch || maintenance.CanRefreshMetadata)
        {
            if (hasOverflowActions)
                MoreFlyout.Items.Add(new MenuFlyoutSeparator());

            if (maintenance.CanRefreshMetadata)
            {
                var refreshItem = new MenuFlyoutItem
                {
                    Text = "Refresh Metadata",
                    Icon = new FontIcon { Glyph = "\uE72C" },
                };
                refreshItem.Click += async (_, _) => await ShowRefreshMetadataDialogAsync();
                MoreFlyout.Items.Add(refreshItem);
            }

            if (maintenance.CanMatch)
            {
                var match = new MenuFlyoutItem
                {
                    Text = "Match Item",
                    Icon = new FontIcon { Glyph = "\uE721" },
                };
                match.Click += MatchButton_Click;
                MoreFlyout.Items.Add(match);
            }
        }
    }

    private async Task LoadSeriesRequestActionAsync(MediaItemDetail item, CancellationToken ct)
    {
        if (!int.TryParse(item.TmdbId, out var tmdbId)) return;
        try
        {
            var api = App.Services.GetRequiredService<RequestsApi>();
            var status = await api.GetStatusAsync(ct);
            if (!status.RequestsEnabled || status.Allowed == false || !status.SeasonRequestsSupported || !status.MissingSeasonsRequestable) return;
            var detail = await api.GetDetailAsync("series", tmdbId, ct);
            if (!IsCurrentDetail(item.ContentId) || ct.IsCancellationRequested || !detail.Request.Requestable || !(detail.Seasons?.Any(RequestViewerPolicy.SeasonRequestable) ?? false)) return;
            _requestSeasonsContentId = item.ContentId;
            BuildMoreFlyout();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Season request capability unavailable: {ex.GetType().Name}"); }
    }

    private async Task RequestSeriesSeasonsAsync(MediaItemDetail item, int tmdbId)
    {
        var token = _navigationCts?.Token ?? CancellationToken.None;
        try
        {
            var api = App.Services.GetRequiredService<RequestsApi>();
            var detail = await api.GetDetailAsync("series", tmdbId, token);
            if (!IsCurrentDetail(item.ContentId) || token.IsCancellationRequested) return;
            var selected = await Dialogs.RequestSeasonsDialog.PickAsync(XamlRoot, detail, async seasons =>
            {
                if (!IsCurrentDetail(item.ContentId) || token.IsCancellationRequested) throw new OperationCanceledException(token);
                await api.CreateAsync(new Core.Models.Requests.CreateMediaRequestInput { MediaType = "series", TmdbId = tmdbId, Title = detail.Title, Seasons = seasons }, token);
            });
            if (selected == null || !IsCurrentDetail(item.ContentId) || token.IsCancellationRequested) return;
            App.Services.GetRequiredService<ToastService>().Success("Season request submitted.");
            _requestSeasonsContentId = null;
            BuildMoreFlyout();
            await LoadSeriesRequestActionAsync(item, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error($"Season request failed: {ex.Message}"); }
    }

    private async Task ShowAddToCollectionDialogAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var toast = App.Services.GetRequiredService<Services.ToastService>();
        var dialog = new Controls.AddToCollectionDialog(item.ContentId, item.Title)
        {
            XamlRoot = XamlRoot,
        };
        dialog.ItemAdded += () => toast.Success("Added to collection");
        await dialog.ShowAsync();
    }

    private async void AddCollectionButton_Click(object sender, RoutedEventArgs e)
        => await ShowAddToCollectionDialogAsync();

    private async Task ShowMangaDetailsDialogAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        await new Controls.MangaFilesDialog(item.ContentId, item.Title)
        {
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    private static string FormatFileBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private async Task ShowMediaInfoDialogAsync(int? preferredFileId = null)
    {
        var versions = (_watchDetail?.Versions ?? ViewModel.Item?.Versions ?? [])
            .OrderByDescending(version => ResolutionRank(version.Resolution))
            .ToList();
        var initialFileId = preferredFileId ?? _selectedVersion?.FileId ?? versions.FirstOrDefault()?.FileId;

        // The WebUI caps this sheet at 2xl but lets it contract with the
        // viewport. A fixed 560px native minimum clipped the labels and close
        // affordance in narrow snapped windows.
        var availableWidth = ActualWidth > 0 ? ActualWidth - 144 : 640;
        var dialogWidth = Math.Clamp(availableWidth, 300, 640);
        var availableHeight = ActualHeight > 0 ? ActualHeight * 0.65 : 680;
        var dialogHeight = Math.Clamp(availableHeight, 280, 680);
        var body = new StackPanel { Spacing = 14, Width = dialogWidth };
        body.Children.Add(new TextBlock
        {
            Text = ViewModel.Item?.Title ?? _watchDetail?.Title ?? "",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (versions.Count == 0)
        {
            body.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 24, 16, 24),
                Child = new TextBlock
                {
                    Text = "No media files for this item.",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            });
        }
        else if (versions.Count == 1)
        {
            body.Children.Add(BuildMediaInfoSpecSheet(versions[0]));
        }
        else
        {
            foreach (var version in versions)
            {
                var summary = BuildQualitySummary(version);
                if (string.IsNullOrWhiteSpace(summary))
                    summary = version.FileName ?? $"Version {versions.IndexOf(version) + 1}";
                var details = new List<string>();
                if (version.FileSize > 0) details.Add(FormatFileSize(version.FileSize));
                var sourceHint = ExtractReleaseHint(version.FileName);
                if (!string.IsNullOrWhiteSpace(sourceHint)) details.Add(sourceHint);
                var header = new StackPanel { Spacing = 2 };
                header.Children.Add(new TextBlock { Text = summary, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                if (details.Count > 0)
                    header.Children.Add(new TextBlock { Text = string.Join(" · ", details), FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
                body.Children.Add(new Expander
                {
                    Header = header,
                    Content = BuildMediaInfoSpecSheet(version),
                    IsExpanded = version.FileId == initialFileId,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                });
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Media Info",
            Content = new ScrollViewer { Content = body, MaxHeight = dialogHeight, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            CloseButtonText = "Close",
        };
        await dialog.ShowAsync();
    }

    private StackPanel BuildMediaInfoSpecSheet(FileVersion version)
    {
        var sheet = new StackPanel { Spacing = 16, Padding = new Thickness(0, 8, 0, 8) };
        AddMediaInfoSection(sheet, "General",
        [
            ("Container", version.Container?.ToUpperInvariant()),
            ("File Size", version.FileSize > 0 ? FormatFileSize(version.FileSize) : null),
            ("Duration", version.Duration > 0 ? FormatDetailedDuration(version.Duration) : null),
            ("Overall Bitrate", FormatMediaBitrate(version.Bitrate)),
            ("Edition", version.EditionRaw),
            ("Added", FormatMediaAdded(version.AddedAt)),
            ("File Path", version.FilePath),
        ]);

        var videos = version.VideoTracks ?? [];
        for (var index = 0; index < videos.Count; index++)
        {
            var track = videos[index];
            AddMediaInfoSection(sheet, videos.Count > 1 ? $"Video {index + 1}" : "Video",
            [
                ("Codec", track.Codec?.ToUpperInvariant()),
                ("Profile", track.Profile),
                ("Level", FormatVideoLevel(track.Codec, track.Level)),
                ("Resolution", track.Width > 0 && track.Height > 0 ? $"{track.Width}x{track.Height}" : null),
                ("Aspect Ratio", track.AspectRatio),
                ("Frame Rate", string.IsNullOrWhiteSpace(track.FrameRate) ? null : $"{track.FrameRate} fps"),
                ("Bitrate", FormatMediaBitrate(track.Bitrate)),
                ("Bit Depth", track.BitDepth > 0 ? $"{track.BitDepth}-bit" : null),
                ("Pixel Format", track.PixelFormat),
                ("Chroma Subsampling", FormatChromaSubsampling(track.PixelFormat)),
                ("Dynamic Range", FormatTrackDynamicRange(track)),
                ("Color Range", FormatColorRange(track.ColorRange)),
                ("Color Primaries", track.ColorPrimaries),
                ("Color Transfer", track.ColorTransfer),
                ("Color Space", track.ColorSpace),
                ("Reference Frames", track.ReferenceFrames > 0 ? track.ReferenceFrames.ToString() : null),
                ("Scan", track.Interlaced == true ? "Interlaced" : "Progressive"),
            ]);
        }

        var audioTracks = version.AudioTracks ?? [];
        for (var index = 0; index < audioTracks.Count; index++)
        {
            var track = audioTracks[index];
            AddMediaInfoSection(sheet, audioTracks.Count > 1 ? $"Audio {index + 1}" : "Audio",
            [
                ("Title", !string.IsNullOrWhiteSpace(track.Title) ? track.Title : track.EmbeddedTitle),
                ("Language", string.IsNullOrWhiteSpace(track.Language) ? null : MediaLanguageCatalog.Label(track.Language)),
                ("Codec", NormalizeAudioCodec(track.Codec, null)),
                ("Profile", track.Profile),
                ("Layout", track.Layout),
                ("Channels", FormatMediaChannels(track.Channels)),
                ("Bitrate", FormatMediaBitrate(track.Bitrate)),
                ("Sample Rate", track.SampleRate > 0 ? $"{track.SampleRate:N0} Hz" : null),
                ("Bit Depth", track.BitDepth > 0 ? $"{track.BitDepth}-bit" : null),
                ("Default", track.Default ? "Yes" : null),
            ]);
        }

        var subtitleTracks = version.SubtitleTracks ?? [];
        for (var index = 0; index < subtitleTracks.Count; index++)
        {
            var track = subtitleTracks[index];
            AddMediaInfoSection(sheet, subtitleTracks.Count > 1 ? $"Subtitle {index + 1}" : "Subtitle",
            [
                ("Title", !string.IsNullOrWhiteSpace(track.Title) ? track.Title : track.EmbeddedTitle),
                ("Language", string.IsNullOrWhiteSpace(track.Language) ? null : MediaLanguageCatalog.Label(track.Language)),
                ("Format", track.Codec?.ToUpperInvariant()),
                ("Source", track.External == true ? "External" : "Embedded"),
                ("File", track.External == true ? track.FileName : null),
                ("Resolution", track.Resolution),
                ("Forced", track.Forced == true ? "Yes" : null),
                ("Default", track.Default == true ? "Yes" : null),
                ("Hearing Impaired", track.HearingImpaired == true ? "Yes" : null),
            ]);
        }
        return sheet;
    }

    private static void AddMediaInfoSection(StackPanel sheet, string title, IEnumerable<(string Label, string? Value)> candidates)
    {
        var rows = candidates.Where(row => !string.IsNullOrWhiteSpace(row.Value)).ToList();
        if (rows.Count == 0) return;
        var section = new StackPanel { Spacing = 6 };
        section.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 80, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
        var rowStack = new StackPanel();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = new Grid { ColumnSpacing = 20, Padding = new Thickness(12, 7, 12, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = rows[index].Label, FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            var value = new TextBlock { Text = rows[index].Value, FontSize = 12, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            if (index < rows.Count - 1)
            {
                row.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
                row.BorderThickness = new Thickness(0, 0, 0, 1);
            }
            rowStack.Children.Add(row);
        }
        section.Children.Add(new Border { BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = (Brush)Application.Current.Resources["SurfaceBrush"], Child = rowStack });
        sheet.Children.Add(section);
    }

    private static string? FormatMediaAdded(string? value) => DateTimeOffset.TryParse(value, out var added) ? DateTimeDisplay.FormatDateTime(added) : null;
    private static string? FormatMediaBitrate(int? bitrate) => bitrate > 0 ? bitrate >= 1_000_000 ? $"{bitrate.Value / 1_000_000d:0.##} Mbps" : $"{bitrate.Value / 1_000d:0} kbps" : null;

    private static string FormatDetailedDuration(double seconds)
    {
        var total = Math.Max(0, (int)Math.Floor(seconds));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var remainder = total % 60;
        return hours > 0 ? $"{hours}h {minutes}m {remainder}s" : minutes > 0 ? $"{minutes}m {remainder}s" : $"{remainder}s";
    }

    private static string? FormatMediaChannels(int? channels) => channels switch
    {
        8 => "7.1",
        6 => "5.1",
        2 => "stereo",
        > 0 => $"{channels} ch",
        _ => null
    };

    private static string? FormatChromaSubsampling(string? pixelFormat)
    {
        var value = pixelFormat?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains("444")) return "4:4:4";
        if (value.Contains("440")) return "4:4:0";
        if (value.Contains("422")) return "4:2:2";
        if (value.Contains("420") || value.StartsWith("nv12") || value.StartsWith("nv21") || value.StartsWith("p010") || value.StartsWith("p016")) return "4:2:0";
        if (value.Contains("411")) return "4:1:1";
        if (value.Contains("410")) return "4:1:0";
        return null;
    }

    private static string? FormatColorRange(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "tv" => "Limited (tv)",
            "pc" => "Full (pc)",
            "unknown" => "Unknown",
            _ => null,
        };
    }

    private static string? FormatVideoLevel(string? codec, int? level)
    {
        if (level is null or <= 0) return null;
        var normalized = codec?.ToLowerInvariant() ?? "";
        if (normalized.Contains("av1"))
            return $"{2 + (level.Value >> 2)}.{level.Value & 3}";
        var value = normalized.Contains("hevc") || normalized.Contains("h265") || normalized.Contains("265")
            ? level.Value / 30d
            : normalized.Contains("avc") || normalized.Contains("h264") || normalized.Contains("264")
                ? level.Value / 10d
                : level.Value;
        return Math.Abs(value - Math.Round(value)) < 0.001 ? Math.Round(value).ToString("0") : value.ToString("0.0");
    }

    private static readonly Dictionary<int, string> DolbyVisionCompatibilityLabels = new()
    {
        [1] = "HDR10",
        [2] = "SDR",
        [4] = "HLG",
        [6] = "HDR10",
    };

    private static readonly Dictionary<string, string> DolbyVisionRangeTypeDetails = new(StringComparer.Ordinal)
    {
        ["DOVIWithEL"] = "EL",
        ["DOVIWithELHDR10Plus"] = "EL",
        ["DOVIWithHDR10"] = "HDR10 compatible",
        ["DOVIWithSDR"] = "SDR compatible",
        ["DOVIWithHLG"] = "HLG compatible",
    };

    private static readonly Dictionary<string, string> VideoRangeTypeLabels = new(StringComparer.Ordinal)
    {
        ["SDR"] = "SDR",
        ["HDR10"] = "HDR10",
        ["HDR10Plus"] = "HDR10+",
        ["HLG"] = "HLG",
        ["DOVI"] = "Dolby Vision",
        ["DOVIWithEL"] = "Dolby Vision (with EL)",
        ["DOVIWithELHDR10Plus"] = "Dolby Vision (with EL) · HDR10+",
        ["DOVIWithHDR10"] = "Dolby Vision (HDR10 compatible)",
        ["DOVIWithHDR10Plus"] = "Dolby Vision · HDR10+",
        ["DOVIWithHLG"] = "Dolby Vision (HLG compatible)",
        ["DOVIWithSDR"] = "Dolby Vision (SDR compatible)",
    };

    private static string? FormatTrackDynamicRange(VersionVideoTrack track)
    {
        var dolbyVision = FormatDolbyVisionLabel(track);
        if (!string.IsNullOrWhiteSpace(dolbyVision))
            return AppendHdr10Plus(dolbyVision, track.Hdr10Plus);

        var rangeType = track.VideoRangeType?.Trim();
        if (!string.IsNullOrWhiteSpace(rangeType))
        {
            var label = VideoRangeTypeLabels.TryGetValue(rangeType, out var mapped)
                ? mapped
                : rangeType;
            return AppendHdr10Plus(label, track.Hdr10Plus);
        }

        if (track.Hdr10Plus == true) return "HDR10+";
        return track.VideoRange;
    }

    private static string? FormatDolbyVisionLabel(VersionVideoTrack track)
    {
        var raw = track.DolbyVision?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw) && track.DvProfile is null)
            return null;

        var baseLabel = !string.IsNullOrWhiteSpace(raw)
            ? (raw.StartsWith("Dolby Vision", StringComparison.OrdinalIgnoreCase) ? raw : $"Dolby Vision {raw}")
            : $"Dolby Vision Profile {track.DvProfile}";

        var details = new List<string>();
        if (track.DvBlCompatId is int compatId
            && DolbyVisionCompatibilityLabels.TryGetValue(compatId, out var compatibility))
        {
            details.Add($"{compatibility} compatible");
        }
        if (track.DvElPresent == true)
            details.Add("EL");

        if (details.Count == 0
            && !string.IsNullOrWhiteSpace(track.VideoRangeType)
            && DolbyVisionRangeTypeDetails.TryGetValue(track.VideoRangeType.Trim(), out var rangeDetail))
        {
            details.Add(rangeDetail);
        }

        return details.Count > 0 ? $"{baseLabel} ({string.Join(", ", details)})" : baseLabel;
    }

    private static string AppendHdr10Plus(string label, bool? hdr10Plus)
    {
        return hdr10Plus == true && !label.Contains("HDR10+", StringComparison.Ordinal)
            ? $"{label} · HDR10+"
            : label;
    }

    private async Task ShowRefreshMetadataDialogAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var toast = App.Services.GetRequiredService<Services.ToastService>();
        var dialog = new Controls.RefreshMetadataDialog(async mode =>
        {
            await App.Services.GetRequiredService<MediaMaintenanceApi>()
                .RefreshMetadataAsync(item.ContentId, mode);
            toast.Success(mode == "complete" ? "Complete refresh queued" : "Metadata refresh queued");
        })
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleFavoriteCommand.ExecuteAsync(null);
        UpdateFavoriteButton();
    }

    private async void WatchlistButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleWatchlistCommand.ExecuteAsync(null);
        UpdateWatchlistButton();
    }

    // ===== Download =====

    private static bool CanCurrentUserDownload()
        => App.Services.GetRequiredService<AuthService>().CurrentUser?.DownloadAllowed == true;

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.Item;
        if (item == null || item.Versions.Count == 0 || !CanCurrentUserDownload()) return;

        await ShowDownloadDialogAsync(item.Title, item.Versions);
    }

    private async Task ShowDownloadDialogAsync(string title, IReadOnlyCollection<FileVersion> versions)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Download: {title}",
            CloseButtonText = "Cancel",
        };
        var content = new StackPanel { Width = 410, Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "Choose a file to download. Make sure you have enough disk space.",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
        var versionButtons = new List<Button>();
        var downloadInProgress = false;
        foreach (var version in versions
                     .OrderByDescending(version => ResolutionRank(version.Resolution))
                     .ThenByDescending(version => version.Bitrate))
        {
            var quality = BuildQualitySummary(version);
            var versionButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = (Brush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 11, 14, 11),
            };
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var iconHost = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = (Brush)Application.Current.Resources["SidebarAccentBrush"],
                Child = new FontIcon { Glyph = "\uE896", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            row.Children.Add(iconHost);
            var labels = new StackPanel { Spacing = 2 };
            labels.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(quality) ? version.FileName ?? "Media file" : quality, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (version.FileSize > 0)
                labels.Children.Add(new TextBlock { Text = FormatFileSize(version.FileSize), FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);
            versionButton.Content = row;
            versionButtons.Add(versionButton);
            var selected = version;
            versionButton.Click += async (_, _) =>
            {
                if (downloadInProgress) return;
                downloadInProgress = true;
                foreach (var button in versionButtons) button.IsEnabled = false;
                iconHost.Child = new ProgressRing
                {
                    IsActive = true,
                    Width = 16,
                    Height = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var saved = await SaveDirectDownloadAsync(selected, title);
                if (saved)
                {
                    dialog.Hide();
                    return;
                }

                iconHost.Child = new FontIcon
                {
                    Glyph = "\uE896",
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                foreach (var button in versionButtons) button.IsEnabled = true;
                downloadInProgress = false;
            };
            content.Children.Add(versionButton);
        }
        if (versions.Count > 1)
            content.Children.Add(new TextBlock { Text = "Larger files require more storage space.", FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        dialog.Content = content;
        await dialog.ShowAsync();
    }

    private async Task<bool> SaveDirectDownloadAsync(FileVersion version, string title)
    {
        var toast = App.Services.GetRequiredService<Services.ToastService>();
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var extension = Path.GetExtension(version.FileName);
            if (string.IsNullOrWhiteSpace(extension))
                extension = string.IsNullOrWhiteSpace(version.Container) ? ".mkv" : $".{version.Container.TrimStart('.')}";
            picker.SuggestedFileName = string.IsNullOrWhiteSpace(version.FileName)
                ? $"{title}{extension}"
                : version.FileName;
            picker.FileTypeChoices.Add("Media file", [extension]);
            var file = await picker.PickSaveFileAsync();
            if (file == null) return false;

            var apiClient = App.Services.GetRequiredService<SiloApiClient>();
            var path = DownloadsApi.GetDirectDownloadPath(version.FileId);
            var http = App.Services.GetRequiredService<HttpClient>();
            using var request = apiClient.CreateAuthenticatedRequest(HttpMethod.Get, path);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                throw new InvalidOperationException("You are not allowed to download this file.");
            if ((int)response.StatusCode == 429)
                throw new InvalidOperationException("Download limit reached. Try again later.");
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = await file.OpenStreamForWriteAsync();
            destination.SetLength(0);
            await source.CopyToAsync(destination);
            toast.Success("Download saved");
            return true;
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
            return false;
        }
    }

    // ===== Match (admin) =====

    private async void MatchButton_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.Item;
        if (item == null) return;

        // Pass the loaded watch-detail versions so the dialog can show on-disk
        // paths (webui parity — admin/media-locations). Watch detail is the
        // source of file_path/file_name; if it hasn't loaded yet, fall back to
        // the item-detail versions which may lack file_path for non-admins.
        var versions = _watchDetail?.Versions ?? item.Versions;
        var dialog = new MatchItemDialog(
            item.ContentId,
            item.Title,
            item.Year > 0 ? item.Year : null,
            item.Type,
            libraryId: null,
            versions: versions,
            folderPaths: item.FolderPaths)
        {
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
        if (dialog.HasAppliedMatch)
        {
            // The dialog performs the mutation and stays open when the server
            // rejects it. Refresh only after its explicit success signal.
            await ViewModel.ReloadAsync(item.ContentId);
            UpdateUI();
        }
    }

    // ===== Media Locations (permission-gated read-only detail) =====
    //
    // Mirrors web/src/components/MediaLocations.tsx. Shows folder/filename
    // for each version with a quality summary header and a copy-full-path
    // button. The server permission contract determines whether this detail is
    // available; it is not a desktop administration surface.

    private void BuildMediaLocationsSection(bool canCurateMetadata, List<SiloPlayer.Core.Models.Playback.FileVersion>? versions)
    {
        MediaLocationsPanel.Children.Clear();
        var isEbook = ViewModel.Item?.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase) == true;
        if ((!canCurateMetadata && !isEbook) || versions == null || versions.Count == 0)
        {
            MediaLocationsSection.Visibility = Visibility.Collapsed;
            return;
        }

        // Order: resolution desc → HDR first → fileId asc (matches web sort).
        var ordered = versions
            .Where(v => !string.IsNullOrWhiteSpace(v.FilePath))
            .OrderByDescending(v => ResolutionScore(v.Resolution))
            .ThenByDescending(v => v.Hdr)
            .ThenBy(v => v.FileId)
            .ToList();

        if (ordered.Count == 0)
        {
            if (ViewModel.Item?.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase) == true)
            {
                MediaLocationsPanel.Children.Add(new TextBlock
                {
                    Text = "No ebook files found.",
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                    FontSize = 13,
                });
                MediaLocationsSection.Visibility = Visibility.Visible;
            }
            else
            {
                MediaLocationsSection.Visibility = Visibility.Collapsed;
            }
            return;
        }

        MediaLocationsSection.Visibility = Visibility.Visible;
        int index = 0;
        foreach (var version in ordered)
        {
            index++;
            MediaLocationsPanel.Children.Add(BuildMediaLocationRow(version, index));
        }
    }

    private Border BuildMediaLocationRow(SiloPlayer.Core.Models.Playback.FileVersion version, int index)
    {
        var (folderName, folderPath, fileName) = SplitMediaPath(version.FilePath!, version.FileName);

        var card = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 10, 10),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { Spacing = 4 };
        // Version label (quality summary).
        var isEbook = ViewModel.Item?.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase) == true;
        var versionLabel = isEbook
            ? BuildEbookVersionSummary(version)
            : BuildVersionQualitySummary(version) ?? $"Version {index}";
        textStack.Children.Add(new TextBlock
        {
            Text = versionLabel,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
        });

        // Folder/ filename in mono.
        var pathRow = new TextBlock
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        };
        if (!string.IsNullOrEmpty(folderName))
        {
            var folderRun = new Microsoft.UI.Xaml.Documents.Run
            {
                Text = folderName + "/",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            };
            pathRow.Inlines.Add(folderRun);
            ToolTipService.SetToolTip(pathRow, folderPath);
        }
        pathRow.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = fileName,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
        });
        textStack.Children.Add(pathRow);
        Grid.SetColumn(textStack, 0);
        grid.Children.Add(textStack);

        var locationActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Top,
        };

        if (!isEbook)
        {
            var infoBtn = new Button
            {
                Width = 28, Height = 28, Padding = new Thickness(0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Content = new FontIcon { Glyph = "\uE946", FontSize = 13 },
            };
            ToolTipService.SetToolTip(infoBtn, "View media info");
            AutomationProperties.SetName(infoBtn, $"View media info for {fileName}");
            infoBtn.Click += async (_, _) => await ShowMediaInfoDialogAsync(version.FileId);
            locationActions.Children.Add(infoBtn);
        }

        // Copy folder path button.
        if (!string.IsNullOrEmpty(folderPath))
        {
            var copyBtn = new Button
            {
                Width = 28, Height = 28, Padding = new Thickness(0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Top,
                Content = new FontIcon { Glyph = "\uE8C8", FontSize = 13 }, // Copy
            };
            ToolTipService.SetToolTip(copyBtn, "Copy full folder path");
            AutomationProperties.SetName(copyBtn, $"Copy full folder path for {fileName}");
            copyBtn.Click += (_, _) =>
            {
                var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                pkg.SetText(folderPath);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            };
            locationActions.Children.Add(copyBtn);
        }

        Grid.SetColumn(locationActions, 1);
        grid.Children.Add(locationActions);

        card.Child = grid;
        return card;
    }

    private static (string folderName, string folderPath, string fileName) SplitMediaPath(
        string filePath, string? fallbackName)
    {
        var slash = Math.Max(filePath.LastIndexOf('/'), filePath.LastIndexOf('\\'));
        if (slash < 0)
        {
            var name = !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName!.Trim() : filePath.Trim();
            return ("", "", name);
        }
        var folder = filePath[..slash];
        var file = slash + 1 < filePath.Length ? filePath[(slash + 1)..] : (fallbackName?.Trim() ?? "Unknown file");
        var segments = folder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var folderName = segments.Length > 0 ? segments[^1] : (string.IsNullOrEmpty(folder) ? "/" : folder);
        return (folderName, folder, file);
    }

    private static int ResolutionScore(string? resolution)
    {
        return (resolution?.ToLowerInvariant()) switch
        {
            "2160p" or "4k" => 4000,
            "1080p" => 1080,
            "720p" => 720,
            "480p" => 480,
            _ => 0,
        };
    }

    private static string? BuildVersionQualitySummary(SiloPlayer.Core.Models.Playback.FileVersion v)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(v.Resolution)) parts.Add(v.Resolution);
        if (!string.IsNullOrWhiteSpace(v.CodecVideo)) parts.Add(v.CodecVideo.ToUpperInvariant());
        var rangeLabel = MediaVideoRange.Label(v);
        if (!string.IsNullOrWhiteSpace(rangeLabel)) parts.Add(rangeLabel);
        if (!string.IsNullOrWhiteSpace(v.CodecAudio)) parts.Add(VersionRanking.MapAudioLabel(v.CodecAudio));
        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(v.Container))
            parts.Add(v.Container.ToUpperInvariant());
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string BuildEbookVersionSummary(SiloPlayer.Core.Models.Playback.FileVersion version)
    {
        var extension = Path.GetExtension(version.FileName ?? version.FilePath ?? "").TrimStart('.');
        var parts = new List<string>
        {
            (string.IsNullOrWhiteSpace(extension) ? version.Container : extension).ToUpperInvariant(),
        };
        if (version.FileSize > 0)
            parts.Add(FormatFileSize(version.FileSize));
        if (version.Duration > 0)
        {
            var pages = Math.Round(version.Duration);
            parts.Add($"{pages:N0} {(pages == 1 ? "page" : "pages")}");
        }
        return string.Join(" \u00B7 ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    // ===== Subtitles Section =====

    private async Task LoadSubtitlesSectionAsync(int mediaFileId)
    {
        try
        {
            var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
            var response = await playbackApi.GetSubtitlesAsync(mediaFileId);

            if (response.Subtitles.Count == 0)
            {
                SubtitlesSection.Visibility = Visibility.Collapsed;
                return;
            }

            SubtitlesSection.Visibility = Visibility.Visible;
            SubtitlesList.Children.Clear();

            foreach (var sub in response.Subtitles)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

                // Subtitle info
                var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

                // Language
                var langName = Services.PlayerService.LanguageCodeToName(sub.Language);
                infoPanel.Children.Add(new TextBlock
                {
                    Text = langName,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                });

                // Codec badge
                if (!string.IsNullOrEmpty(sub.Codec))
                {
                    infoPanel.Children.Add(new Border
                    {
                        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Child = new TextBlock
                        {
                            Text = sub.Codec.ToUpperInvariant(),
                            FontSize = 11,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
                        }
                    });
                }

                // Source badge
                if (!string.IsNullOrEmpty(sub.Source))
                {
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = sub.Source,
                        FontSize = 12,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                // Title
                if (!string.IsNullOrEmpty(sub.Title))
                {
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = sub.Title,
                        FontSize = 12,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 300
                    });
                }

                if (sub.Forced)
                {
                    infoPanel.Children.Add(new TextBlock
                    {
                        Text = "[Forced]",
                        FontSize = 12,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                Grid.SetColumn(infoPanel, 0);
                row.Children.Add(infoPanel);

                // Delete button
                var deleteBtn = new Button
                {
                    Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                    Padding = new Thickness(6, 4, 6, 4),
                    CornerRadius = new CornerRadius(4),
                    Tag = sub.Id,
                    Content = new FontIcon
                    {
                        Glyph = "\uE74D",
                        FontSize = 12,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"]
                    },
                    VerticalAlignment = VerticalAlignment.Center
                };
                deleteBtn.Click += SubtitleDeleteButton_Click;
                Grid.SetColumn(deleteBtn, 1);
                row.Children.Add(deleteBtn);

                SubtitlesList.Children.Add(row);
            }
        }
        catch
        {
            SubtitlesSection.Visibility = Visibility.Collapsed;
        }
    }

    private async void SubtitleDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int subtitleId) return;

        try
        {
            var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
            await playbackApi.DeleteSubtitleAsync(subtitleId);

            // Remove the row from UI
            var parent = btn.Parent as Grid;
            if (parent != null)
                SubtitlesList.Children.Remove(parent);

            if (SubtitlesList.Children.Count == 0)
                SubtitlesSection.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Non-fatal
        }
    }

    // ===== Navigation =====

    private static string ExtraKindLabel(string kind) => kind switch
    {
        "trailer" => "Trailer",
        "teaser" => "Teaser",
        "featurette" => "Featurette",
        "clip" => "Clip",
        "behind_the_scenes" => "Behind the Scenes",
        "bloopers" => "Bloopers",
        "deleted_scene" => "Deleted Scene",
        _ => "Extra",
    };

    private static string ExtraKindGroupLabel(string kind) => kind switch
    {
        "trailer" => "Trailers",
        "teaser" => "Teasers",
        "featurette" => "Featurettes",
        "clip" => "Clips",
        "behind_the_scenes" => "Behind the Scenes",
        "bloopers" => "Bloopers",
        "deleted_scene" => "Deleted Scenes",
        _ => "Other",
    };

    private void BuildTrailers(IReadOnlyList<ItemVideo> videos)
    {
        TrailersPanel.Children.Clear();
        var playable = videos
            .Where(video => video.Site.Equals("youtube", StringComparison.OrdinalIgnoreCase)
                            && IsSafeYouTubeKey(video.SiteKey))
            .ToList();

        TrailersSection.Visibility = playable.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var video in playable)
            TrailersPanel.Children.Add(CreateTrailerCard(video));

        TrailersScrollViewer.ChangeView(0, null, null, disableAnimation: true);
        DispatcherQueue.TryEnqueue(() =>
            UpdateDetailCarouselButtons(TrailersScrollViewer, TrailersPrevButton, TrailersNextButton));
    }

    private static bool IsSafeYouTubeKey(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');

    private Button CreateTrailerCard(ItemVideo video)
    {
        var label = string.IsNullOrWhiteSpace(video.Name)
            ? ExtraKindLabel(video.Kind)
            : video.Name!;

        var thumbnail = new Image
        {
            Stretch = Stretch.UniformToFill,
            Source = (ImageSource)RemoteImageConverter.Convert(
                $"https://i.ytimg.com/vi/{video.SiteKey}/hqdefault.jpg",
                typeof(ImageSource),
                null!,
                string.Empty),
        };

        var imageHost = new Grid { Width = 280, Height = 158 };
        imageHost.Children.Add(thumbnail);
        var playOverlay = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xA8, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Child = new FontIcon
            {
                Glyph = "\uE768",
                FontSize = 18,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            },
        };
        imageHost.Children.Add(playOverlay);

        var imageClip = new Border
        {
            Width = 280,
            Height = 158,
            CornerRadius = new CornerRadius(8),
            Child = imageHost,
        };

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        meta.Children.Add(new TextBlock
        {
            Text = ExtraKindLabel(video.Kind),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        if (video.IsOfficial)
        {
            meta.Children.Add(new Border
            {
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = "Official",
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                },
            });
        }

        var content = new StackPanel { Width = 280, Spacing = 5 };
        content.Children.Add(imageClip);
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        content.Children.Add(meta);

        var card = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = content,
            Tag = video,
        };
        card.PointerEntered += (_, _) => playOverlay.Opacity = 1;
        card.PointerExited += (_, _) => playOverlay.Opacity = card.FocusState == FocusState.Unfocused ? 0 : 1;
        card.GotFocus += (_, _) => playOverlay.Opacity = 1;
        card.LostFocus += (_, _) => playOverlay.Opacity = 0;
        card.Click += TrailerCard_Click;
        return card;
    }

    private void TrailerCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ItemVideo video }
            || !IsSafeYouTubeKey(video.SiteKey))
            return;

        _trailerInvoker = sender as Control;
        App.Services.GetService<SiloPlayer.Services.ThemeMusicService>()?.Interrupt();
        SizeTrailerModal(ActualWidth, ActualHeight);
        TrailerWebView.Source = new Uri(
            $"https://www.youtube-nocookie.com/embed/{video.SiteKey}?autoplay=1");
        TrailerOverlay.Visibility = Visibility.Visible;
        TrailerCloseButton.Focus(FocusState.Programmatic);
    }

    private Control? _trailerInvoker;

    private void SizeTrailerModal(double width, double height)
    {
        var modalWidth = Math.Max(320, Math.Min(896, width - 64));
        var modalHeight = modalWidth * 9 / 16;
        if (modalHeight > height - 64)
        {
            modalHeight = Math.Max(180, height - 64);
            modalWidth = modalHeight * 16 / 9;
        }
        TrailerWebView.Width = modalWidth;
        TrailerWebView.Height = modalHeight;
    }

    private void TrailerBackdrop_Click(object sender, RoutedEventArgs e) => CloseTrailerModal();
    private void TrailerClose_Click(object sender, RoutedEventArgs e) => CloseTrailerModal();

    private void TrailerEscape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (TrailerOverlay.Visibility != Visibility.Visible) return;
        CloseTrailerModal();
        args.Handled = true;
    }

    private void CloseTrailerModal()
    {
        if (TrailerOverlay.Visibility != Visibility.Visible) return;
        TrailerOverlay.Visibility = Visibility.Collapsed;
        TrailerWebView.Source = new Uri("about:blank");
        _trailerInvoker?.Focus(FocusState.Programmatic);
        _trailerInvoker = null;
    }

    private static void ReflowExtras(Grid grid, double width)
    {
        var columns = width >= 1024 ? 3 : width >= 640 ? 2 : 1;
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < Math.Ceiling(grid.Children.Count / (double)columns); i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < grid.Children.Count; i++) { if (grid.Children[i] is FrameworkElement element) { Grid.SetRow(element, i / columns); Grid.SetColumn(element, i % columns); } }
    }

    private void BuildExtras(IReadOnlyList<ItemExtra> extras)
    {
        ExtrasGroupsPanel.Children.Clear();
        var playable = extras.Where(extra => !string.IsNullOrWhiteSpace(extra.ContentId)).ToList();
        ExtrasSection.Visibility = playable.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (playable.Count == 0) return;

        foreach (var group in playable.GroupBy(extra => extra.Kind))
        {
            var groupPanel = new StackPanel { Spacing = 10 };
            groupPanel.Children.Add(new TextBlock
            {
                Text = ExtraKindGroupLabel(group.Key).ToUpperInvariant(),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 100,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            for (var column = 0; column < 3; column++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var groupedExtras = group.ToList();
            for (var row = 0; row < (int)Math.Ceiling(groupedExtras.Count / 3.0); row++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var index = 0; index < groupedExtras.Count; index++)
            {
                var card = CreateExtraCard(groupedExtras[index]);
                Grid.SetRow(card, index / 3);
                Grid.SetColumn(card, index % 3);
                grid.Children.Add(card);
            }

            ReflowExtras(grid, ActualWidth);
            groupPanel.Children.Add(grid);
            ExtrasGroupsPanel.Children.Add(groupPanel);
        }
    }

    private Button CreateExtraCard(ItemExtra extra)
    {
        var title = string.IsNullOrWhiteSpace(extra.Title)
            ? ExtraKindGroupLabel(extra.Kind)
            : extra.Title!;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        });
        if (extra.DurationSeconds is > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = FormatExtraDuration(extra.DurationSeconds.Value),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var play = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M5 5a2 2 0 0 1 3-1.732l13 7.5a2 2 0 0 1 0 3.464l-13 7.5A2 2 0 0 1 5 20z' StrokeThickness='2' StrokeLineJoin='Round'/>");
        play.Fill = play.Stroke = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        var canvas = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(play);
        var playBox = new Viewbox { Width = 16, Height = 16, Margin = new Thickness(2, 0, 0, 0), Child = canvas };
        var playDisc = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = (Brush)Application.Current.Resources["MutedBrush"], Child = playBox };
        row.Children.Add(playDisc);
        row.Children.Add(text);

        var button = new Button
        {
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1),
            Content = row,
            Tag = extra.ContentId,
        };
        button.PointerEntered += (_, _) => { playDisc.Background = (Brush)Application.Current.Resources["AccentBrush"]; play.Fill = play.Stroke = (Brush)Application.Current.Resources["AccentForegroundBrush"]; button.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"]; };
        button.PointerExited += (_, _) => { playDisc.Background = (Brush)Application.Current.Resources["MutedBrush"]; play.Fill = play.Stroke = (Brush)Application.Current.Resources["PrimaryTextBrush"]; button.Background = (Brush)Application.Current.Resources["SurfaceBrush"]; };
        button.Click += ExtraCard_Click;
        return button;
    }

    private static string FormatExtraDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    private void ExtraCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string contentId })
            _ = PlayExtraAsync(contentId);
    }

    private async Task PlayExtraAsync(string contentId)
    {
        var playerService = App.Services.GetRequiredService<Services.PlayerService>();
        await playerService.PlayAsync(contentId);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
        else
            nav.Navigate<HomePage>();
    }

    private async void RetryDetailButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentContentId)) return;
        await ViewModel.LoadCommand.ExecuteAsync(_currentContentId);
        if (ViewModel.Item != null)
            UpdateUI();
    }

    private async void RetrySeasonsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item?.Type != "series") return;
        SeasonsLoadError.Visibility = Visibility.Collapsed;
        SeasonsLoadingSkeleton.Visibility = Visibility.Visible;
        SeasonsScrollViewer.Visibility = Visibility.Collapsed;

        await ViewModel.LoadSeasonsCommand.ExecuteAsync(null);
        if (ViewModel.Item?.Type != "series") return;

        SeasonsLoadingSkeleton.Visibility = Visibility.Collapsed;
        SeasonsLoadError.Visibility = ViewModel.SeasonsLoadFailed
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (ViewModel.SeasonsLoadFailed) return;
        UpdateSeriesCountsFromLoadedSeasons();

        if (ViewModel.Seasons.Count == 1)
        {
            await ShowSingleSeasonEpisodesAsync(
                ViewModel.Seasons[0],
                ViewModel.Item.ContentId,
                _navigationCts?.Token ?? CancellationToken.None);
        }
        else if (ViewModel.Seasons.Count == 0)
        {
            SeasonsSection.Visibility = Visibility.Collapsed;
            SeasonsScrollViewer.Visibility = Visibility.Collapsed;
            EpisodesSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            SeasonsSection.Visibility = Visibility.Visible;
            SeasonsScrollViewer.Visibility = Visibility.Visible;
            BuildSeasonCards();
        }

        if (!ApplyAuthoritativeSeriesAction() && string.IsNullOrWhiteSpace(_playableContentId))
        {
            var primaryAction = SeriesPrimaryActionResolver.Resolve(ViewModel.Seasons);
            if (!string.IsNullOrWhiteSpace(primaryAction.TargetSeasonId))
                await LoadSeriesPrimaryEpisodesAsync(primaryAction.TargetSeasonId);
            var index = Math.Clamp(
                (primaryAction.TargetEpisodeNumber ?? 1) - 1,
                0,
                Math.Max(ViewModel.Episodes.Count - 1, 0));
            var episode = ViewModel.Episodes.Count > 0 ? ViewModel.Episodes[index] : null;
            _playableContentId = episode?.ContentId;
            PlayButtonText.Text = primaryAction.Label;
            SplitPlayButton.Visibility = episode == null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async void RetryEpisodesButton_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.Item;
        if (item?.Type != "season") return;

        EpisodesLoadError.Visibility = Visibility.Collapsed;
        EpisodesLoadingSkeleton.Visibility = Visibility.Visible;
        EpisodesPanel.Visibility = Visibility.Collapsed;
        var loaded = await LoadSeasonEpisodesAsync(
            item.ContentId,
            item.SeasonNumber ?? 0,
            _navigationCts?.Token ?? CancellationToken.None);
        if (!ReferenceEquals(ViewModel.Item, item)) return;

        EpisodesLoadingSkeleton.Visibility = Visibility.Collapsed;
        EpisodesLoadError.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
        EpisodesPanel.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        if (loaded) BuildEpisodeRows();
    }

    private void RetrySimilarButton_Click(object sender, RoutedEventArgs e)
    {
        SimilarLoadError.Visibility = Visibility.Collapsed;
        SimilarLoadingSkeleton.Visibility = Visibility.Visible;
        SimilarPanel.Visibility = Visibility.Collapsed;
        _ = LoadSimilarItemsAsync();
    }

    private void RetrySiblingEpisodesButton_Click(object sender, RoutedEventArgs e)
    {
        SiblingEpisodesLoadError.Visibility = Visibility.Collapsed;
        SiblingEpisodesLoadingSkeleton.Visibility = Visibility.Visible;
        SiblingEpisodesScrollViewer.Visibility = Visibility.Collapsed;
        _ = LoadSiblingEpisodesAsync();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item == null) return;
        if (IsActiveAudiobook()) { _playerService!.ToggleAudiobookPlayback(); UpdateActiveAudiobook(); return; }

        if (IsReaderItem(ViewModel.Item))
        {
            var target = _readerTargetContentId ?? ViewModel.Item.ContentId;
            App.Services.GetRequiredService<NavigationService>()
                .Navigate<EbookReaderPage>(new EbookReaderNavigation(target, _readerTargetFileId));
            return;
        }

        if (ShouldOfferResumeChoice(out var resumePosition, out var resumeDuration))
        {
            await ShowResumeChoiceDialogAsync(resumePosition, resumeDuration);
            return;
        }

        var contentId = await ResolvePlayableContentIdForPlaybackAsync();
        if (!string.IsNullOrEmpty(contentId))
        {
            NavigateToPlayer(contentId, fileId: _selectedVersion?.FileId);
        }
        else
        {
            ShowNoPlayableEpisodeToast(ViewModel.Item.Type);
        }
    }

    private void EpisodeRow_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string contentId)
        {
            // Navigate to episode detail page, not directly to player
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<ItemDetailPage>(contentId);
        }
    }

    private async void PlayFromStart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item == null) return;

        if (IsReaderItem(ViewModel.Item))
        {
            var target = _readerTargetContentId ?? ViewModel.Item.ContentId;
            App.Services.GetRequiredService<NavigationService>()
                .Navigate<EbookReaderPage>(new EbookReaderNavigation(target, _readerTargetFileId));
            return;
        }

        var contentId = await ResolvePlayableContentIdForPlaybackAsync();
        if (!string.IsNullOrEmpty(contentId))
        {
            NavigateToPlayer(contentId, fromStart: true, fileId: _selectedVersion?.FileId);
        }
        else
        {
            ShowNoPlayableEpisodeToast(ViewModel.Item.Type);
        }
    }

    private bool ApplyAuthoritativeSeriesAction()
    {
        var item = ViewModel.Item;
        if (item?.Type != "series") return false;
        var action = SeriesPrimaryActionResolver.ResolveDetail(item);
        _playableContentId = action.ContentId;
        PlayButtonText.Text = action.Label;
        SplitPlayButton.Visibility = Visibility.Visible;
        PrimaryPlayButton.IsEnabled = action.ContentId != null;
        SplitPlayButton.Opacity = action.ContentId == null ? .5 : 1;
        // Retain the accent pill beneath the inert native button. The default
        // disabled template otherwise paints an opaque settings surface.
        PrimaryPlayButton.Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        PrimaryPlayButton.Resources["ButtonBorderBrushDisabled"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        PrimaryPlayButton.Resources["ButtonForegroundDisabled"] = Application.Current.Resources["AccentForegroundBrush"];
        return true;
    }

    private async Task<string?> ResolvePlayableContentIdForPlaybackAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return null;
        if (item.Type == "series" || item.Type == "season" && item.HasAuthoritativePlayTarget)
            return string.IsNullOrWhiteSpace(item.PlayContentId) ? null : item.PlayContentId;

        if (!string.IsNullOrWhiteSpace(_playableContentId))
            return _playableContentId;

        if (IsReaderItem(item))
            return _readerTargetContentId ?? item.ContentId;

        if (string.Equals(item.Type, "season", StringComparison.OrdinalIgnoreCase))
        {
            if (ViewModel.Episodes.Count == 0)
            {
                EpisodesSection.Visibility = Visibility.Visible;
                EpisodesLoadingSkeleton.Visibility = Visibility.Visible;
                EpisodesLoadError.Visibility = Visibility.Collapsed;
                EpisodesPanel.Visibility = Visibility.Collapsed;
                var loaded = await LoadSeasonEpisodesAsync(
                    item.ContentId,
                    item.SeasonNumber ?? 0,
                    _navigationCts?.Token ?? CancellationToken.None);
                EpisodesLoadingSkeleton.Visibility = Visibility.Collapsed;
                EpisodesLoadError.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
                EpisodesPanel.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
                if (loaded) BuildEpisodeRows();
            }

            var firstEpisode = ViewModel.Episodes.FirstOrDefault();
            _playableContentId = firstEpisode?.ContentId;
            SplitPlayButton.Visibility = firstEpisode == null ? Visibility.Collapsed : Visibility.Visible;
            return _playableContentId;
        }

        return item.ContentId;
    }

    private static void ShowNoPlayableEpisodeToast(string? itemType)
    {
        var message = string.Equals(itemType, "season", StringComparison.OrdinalIgnoreCase)
            ? "No playable episodes found for this season."
            : "No playable episode could be resolved for this series.";
        App.Services.GetRequiredService<Services.ToastService>().Error(message);
    }

    private void NavigateToPlayer(string contentId, bool fromStart = false, int? fileId = null)
    {
        var playerService = App.Services.GetRequiredService<Services.PlayerService>();
        playerService.BeginViewerPlaybackIntent();

        // Phase 3b: pre-load the "next episode" hint so the Playing Next
        // cinematic overlay can show at the end of this episode. Applies
        // when this is a series episode with a known successor.
        SetNextEpisodeHintIfApplicable(playerService, contentId);

        // Phase 2a + 2b: pass pre-play audio + subtitle selections through.
        _ = playerService.PlayAsync(
            contentId,
            fromStart: fromStart,
            fileId: fileId,
            audioTrackIndex: _selectedAudioTrackIndex,
            subtitleSelection: _selectedSubtitleIndex,
            prefetchedWatchDetail: string.Equals(_watchDetail?.ContentId, contentId, StringComparison.Ordinal)
                ? _watchDetail
                : null,
            subtitleTrackSignature: _selectedSubtitleSignature);
    }

    /// <summary>
    /// Phase 3b — if the content being played is a series episode in the
    /// currently-loaded Episodes list, find the next one after it and record
    /// metadata on the PlayerService so the "Up next" overlay fires at the
    /// end. Clears any stale hint from a previous playback first.
    /// </summary>
    private void SetNextEpisodeHintIfApplicable(Services.PlayerService playerService, string currentContentId)
    {
        if (!ViewModel.IsSeries || ViewModel.Episodes.Count == 0) return;

        int currentIdx = -1;
        for (int i = 0; i < ViewModel.Episodes.Count; i++)
        {
            if (ViewModel.Episodes[i].ContentId == currentContentId)
            {
                currentIdx = i;
                break;
            }
        }
        if (currentIdx < 0 || currentIdx >= ViewModel.Episodes.Count - 1) return;

        var next = ViewModel.Episodes[currentIdx + 1];
        // Match the current WebUI post-roll episode label.
        var label = $"S{next.SeasonNumber}:E{next.EpisodeNumber}";
        playerService.SetNextEpisodeHint(
            currentContentId,
            next.ContentId,
            string.IsNullOrEmpty(next.Title) ? label : $"{label} \u2014 {next.Title}",
            ViewModel.Item?.Title,
            next.StillUrl,
            next.Overview,
            next.AirDate,
            next.Runtime,
            next.SeasonNumber,
            next.EpisodeNumber);
    }

    // ===== Initial Play Button & Quality Badges from Catalog Item Data =====

    private static WatchUserData? ToWatchUserData(ItemDetailUserData? source) => source == null
        ? null
        : new WatchUserData
        {
            PositionSeconds = source.PositionSeconds,
            DurationSeconds = source.DurationSeconds,
            Played = source.Played,
            LastFileId = source.LastFileId,
            LastResolution = source.LastResolution,
            LastHdr = source.LastHdr,
            LastCodecVideo = source.LastCodecVideo,
            LastEditionKey = source.LastEditionKey,
        };

    /// <summary>
    /// Sets the play button text using catalog item data (before watch detail loads).
    /// Shows "Resume from X:XX" if in-progress, and quality like "· 2160p HDR".
    /// </summary>
    private void UpdatePlayButtonFromItemData(MediaItemDetail item)
    {
        if (IsReaderItem(item))
        {
            PlayButtonText.Text = "Read";
            PlayButtonIcon.Glyph = "\uE736";
            VersionDropdownButton.Visibility = Visibility.Collapsed;
            VersionSeparator.Visibility = Visibility.Collapsed;
            AudioTracksButton.Visibility = Visibility.Collapsed;
            SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
            EditionButton.Visibility = Visibility.Collapsed;
            DownloadButton.Visibility = Visibility.Collapsed;
            return;
        }

        // The current WebUI distinguishes an untouched episode action from a
        // movie action: episodes say "Play Episode", while movies say "Play".
        // Resume-choice items still intentionally render "Play" below, just
        // like ActionBar's displayedPlayLabel when it opens that dialog.
        if (item.Type is "movie" or "episode")
            PlayButtonText.Text = DefaultLeafPlayLabel(item.Type);

        // Show quality on play button from OverlaySummary or Versions
        var qualityParts = new List<string>();
        if (item.OverlaySummary != null && !string.IsNullOrEmpty(item.OverlaySummary.Resolution))
            qualityParts.Add(item.OverlaySummary.Resolution);
        if (item.Versions?.Count > 0)
        {
            var best = VersionRanking.SelectDefaultPlaybackVariantVersion(
                item.Versions,
                item.PlaybackVariants,
                ToWatchUserData(item.UserData),
                qualityPreference: null,
                preferredEditionKey: item.EffectiveVersionEditionKey)
                ?? item.Versions[0];
            _selectedVersion = best;
            UpdateSelectedVersionHeroSummary(best);
            VersionSummaryText.Text = BuildQualitySummary(best);
            if (qualityParts.Count == 0 && !string.IsNullOrEmpty(best.Resolution))
                qualityParts.Add(best.Resolution);
            var rangeLabel = MediaVideoRange.Label(best);
            if (!string.IsNullOrEmpty(rangeLabel)) qualityParts.Add(rangeLabel);
        }
        // Quality/HDR summary shows beside the version dropdown chevron
        // (webui commit a42f46b — no longer duplicated on the Play button).
        if (qualityParts.Count > 0)
        {
            VersionSummaryText.Text = _selectedVersion != null
                ? BuildQualitySummary(_selectedVersion)
                : string.Join(" ", qualityParts);
        }

        // Show version dropdown if multiple versions, OR if we have a quality
        // summary to display (so the single-version case still shows "4K HDR").
        if (item.Versions?.Count > 1)
        {
            VersionDropdownButton.Visibility = Visibility.Visible;
            VersionSeparator.Visibility = Visibility.Collapsed;
        }

        // Resume state from user data
        var userData = item.UserData;
        // Match resolveLeafPrimaryAction in the current WebUI: a watched item
        // can be in the middle of a rewatch, so any nonzero position remains a
        // live resume point even while the historical played flag stays true.
        if (userData != null && userData.PositionSeconds > 0)
        {
            PlayButtonText.Text = ShouldOfferResumeChoice(out _, out _)
                ? "Play"
                : "Resume";

            // Show progress bar
            if (userData.DurationSeconds > 0)
            {
                var fraction = userData.PositionSeconds / userData.DurationSeconds;
                _playProgressFraction = Math.Min(fraction, 1.0);
                SplitPlayButton.SizeChanged -= OnSplitPlayButtonSizeChanged;
                SplitPlayButton.SizeChanged += OnSplitPlayButtonSizeChanged;
                UpdatePlayProgressWidth();
            }

            // Restart now lives in the More menu, matching ActionBar.tsx.
            VersionDropdownButton.Visibility = item.Versions?.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
            VersionSeparator.Visibility = Visibility.Collapsed;
            ConfigureVersionSelectors(item.Versions ?? [], item.PlaybackVariants, _selectedVersion, isResuming: true);
        }
        else if (item.Versions?.Count > 1)
        {
            // Multiple versions available — show version picker
            ConfigureVersionSelectors(item.Versions, item.PlaybackVariants, _selectedVersion, isResuming: false);
        }
        else if (item.Versions?.Count > 0)
        {
            ConfigureVersionSelectors(item.Versions, item.PlaybackVariants, _selectedVersion, isResuming: false);
        }
    }

    /// <summary>
    /// Shows quality badges from the catalog item's OverlaySummary, Versions, or UserData
    /// before the watch detail response is available.
    /// </summary>
    private void UpdateQualityBadgesFromItemData(MediaItemDetail item)
    {
        QualityBadgesPanel.Children.Clear();

        var selectedVersion = _selectedVersion ?? (item.Versions?.Count > 0
            ? VersionRanking.SelectDefaultPlaybackVariantVersion(
                item.Versions,
                item.PlaybackVariants,
                ToWatchUserData(item.UserData),
                qualityPreference: null,
                preferredEditionKey: item.EffectiveVersionEditionKey)
            : null);

        // The current WebUI summarizes the selected version. It no longer
        // combines the best attributes found across unrelated versions.
        if (selectedVersion != null)
        {
            if (!string.IsNullOrEmpty(selectedVersion.Resolution))
            {
                QualityBadgesPanel.Children.Add(CreateQualityBadge(
                    selectedVersion.Resolution,
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeResolutionBrush"]));
            }

            var videoRange = MediaVideoRange.Label(selectedVersion);
            if (!string.IsNullOrEmpty(videoRange))
            {
                QualityBadgesPanel.Children.Add(CreateQualityBadge(
                    videoRange,
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeHdrBrush"]));
            }
            // The current WebUI resolves the hero audio badge from every audio
            // track on the selected file, then displays the highest-ranked codec
            // family without a channel-count suffix.
            var audioLabel = VersionRanking.PickBestAttributes([selectedVersion], qualityPreference: null)?.AudioLabel;
            if (!string.IsNullOrEmpty(audioLabel))
            {
                QualityBadgesPanel.Children.Add(CreateQualityBadge(
                    audioLabel,
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeBackgroundBrush"],
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeTextBrush"]));
            }
        }

        // Final fallback: use UserData from last playback
        if (QualityBadgesPanel.Children.Count == 0)
        {
            var userData = item.UserData;
            if (userData != null && !string.IsNullOrEmpty(userData.LastResolution))
            {
                QualityBadgesPanel.Children.Add(CreateQualityBadge(
                    userData.LastResolution,
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeResolutionBrush"]));

                if (userData.LastHdr == true)
                {
                    QualityBadgesPanel.Children.Add(CreateQualityBadge(
                        "HDR",
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeHdrBrush"]));
                }

                if (!string.IsNullOrEmpty(userData.LastCodecVideo))
                {
                    QualityBadgesPanel.Children.Add(CreateQualityBadge(
                        userData.LastCodecVideo.ToUpperInvariant(),
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeBackgroundBrush"],
                        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeTextBrush"]));
                }
            }
        }

        QualityBadgesPanel.Visibility = QualityBadgesPanel.Children.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Watch Detail & Play Button =====

    private async Task LoadWatchDetailAsync(string contentId)
    {
        try
        {
            var playerService = App.Services.GetRequiredService<Services.PlayerService>();
            var watchDetail = await playerService.GetOrFetchWatchDetailAsync(contentId);
            if (!string.Equals(_playableContentId, contentId, StringComparison.Ordinal)) return;
            _watchDetail = watchDetail;
            _downloadedSubtitles = [];
            UpdatePlayButton();
            UpdateQualityBadges();

            // The item-detail response can omit file paths while /watch has
            // the complete versions. Match the WebUI permission gate: media
            // locations are visible only to metadata curators (including an
            // admin account while its primary profile is active).
            var authService = App.Services.GetRequiredService<AuthService>();
            BuildMediaLocationsSection(
                AuthorizationPolicy.CanCurateMetadata(authService),
                _watchDetail.Versions);

            // Show download button if we have a selected version
            DownloadButton.Visibility = Visibility.Collapsed;
            BuildMoreFlyout();

            // Subtitle selection/search lives in the ActionBar in the current
            // WebUI. Avoid a duplicate detail-page list and its extra request.
            if (_selectedVersion != null)
                await LoadDownloadedSubtitlesAsync(contentId, _selectedVersion.FileId);
        }
        catch (Exception ex)
        {
            // Log the error so we can debug
            LocalLog.AppendLine("watch_detail_error.txt", $"LoadWatchDetailAsync failed for contentId={contentId}: {ex}");
        }
    }

    private bool ShouldOfferResumeChoice(out double position, out double duration)
    {
        position = _watchDetail?.UserData?.PositionSeconds
            ?? ViewModel.Item?.UserData?.PositionSeconds
            ?? 0;
        duration = _watchDetail?.UserData?.DurationSeconds
            ?? ViewModel.Item?.UserData?.DurationSeconds
            ?? 0;
        var type = ViewModel.Item?.Type;
        var versionCount = _watchDetail?.Versions.Count
            ?? ViewModel.Item?.Versions?.Count
            ?? 0;
        var variantCount = _watchDetail?.PlaybackVariants?.Count
            ?? ViewModel.Item?.PlaybackVariants?.Count
            ?? 0;

        return type is "movie" or "episode"
            && position > 0
            && versionCount <= 1
            && variantCount <= 1;
    }

    private async Task ShowResumeChoiceDialogAsync(double position, double duration)
    {
        var resumeTime = FormatClock(position);
        var percent = duration > 0
            ? (int)Math.Round(Math.Clamp(position / duration, 0, 1) * 100)
            : 0;
        var description = percent > 0
            ? $"You're {resumeTime} in, about {percent}% through."
            : $"You're {resumeTime} in. Resume where you left off or start over.";

        var resumeButton = new Button
        {
            Content = $"Resume at {resumeTime}",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8),
        };
        var restartButton = new Button
        {
            Content = "Play from Beginning",
            Style = (Style)Application.Current.Resources["OutlineButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8),
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2),
        });
        content.Children.Add(resumeButton);
        content.Children.Add(restartButton);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Resume Playback?",
            Content = content,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None,
        };
        resumeButton.Click += (_, _) =>
        {
            dialog.Hide();
            StartResolvedPlayback(fromStart: false);
        };
        restartButton.Click += (_, _) =>
        {
            dialog.Hide();
            StartResolvedPlayback(fromStart: true);
        };
        await dialog.ShowAsync();
    }

    private async void StartResolvedPlayback(bool fromStart)
    {
        var contentId = await ResolvePlayableContentIdForPlaybackAsync();
        if (!string.IsNullOrEmpty(contentId))
            NavigateToPlayer(contentId, fromStart, _selectedVersion?.FileId);
        else
            ShowNoPlayableEpisodeToast(ViewModel.Item?.Type);
    }

    private static bool IsReaderItem(MediaItemDetail? item) =>
        item != null && (item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase) ||
            item.Type.Equals("manga", StringComparison.OrdinalIgnoreCase) ||
            item.Type.Equals("comic", StringComparison.OrdinalIgnoreCase));

    private void UpdatePlayButton()
    {
        if (_watchDetail == null) return;

        // Show resume button text if there's saved progress
        var userData = _watchDetail.UserData;
        // Watched is historical state; a nonzero position can belong to a
        // rewatch in progress and must still present Resume like the WebUI.
        bool isResuming = userData?.PositionSeconds > 0;

        if (isResuming)
        {
            PlayButtonText.Text = ShouldOfferResumeChoice(out _, out _)
                ? "Play"
                : "Resume";
        }
        else
        {
            PlayButtonText.Text = DefaultLeafPlayLabel(ViewModel.Item?.Type);
        }

        // Show version info for the best version
        var versions = _watchDetail.Versions;
        if (versions.Count > 0)
        {
            var manager = App.Services.GetRequiredService<PlaybackManager>();
            var best = manager.SelectBestVariantVersion(
                versions,
                _watchDetail.PlaybackVariants,
                userData: userData,
                preferredEditionKey: _watchDetail.EffectiveVersionEditionKey);
            _selectedVersion = best;
            BuildAudioTracksFlyout(best);
            BuildSubtitlesPopoverFlyout(best);
            if (best != null)
                UpdateSelectedVersionHeroSummary(best);

            if (best != null)
            {
                VersionSummaryText.Text = BuildQualitySummary(best);
                var qualityParts = new List<string>();
                if (!string.IsNullOrEmpty(best.Resolution))
                    qualityParts.Add(best.Resolution);
                var rangeLabel = MediaVideoRange.Label(best);
                if (!string.IsNullOrEmpty(rangeLabel))
                    qualityParts.Add(rangeLabel);

                if (qualityParts.Count > 0)
                {
                    // Quality summary now lives beside the version dropdown chevron
                    // (webui commit a42f46b — removed from the Play button).
                    VersionSummaryText.Text = BuildQualitySummary(best);
                }
            }

            // Show version dropdown if multiple versions OR if resuming (for "Play from Start")
            // OR if we have a quality summary to show beside it.
            if (versions.Count > 1)
            {
                VersionDropdownButton.Visibility = Visibility.Visible;
                VersionSeparator.Visibility = Visibility.Collapsed;
                ConfigureVersionSelectors(
                    versions,
                    _watchDetail.PlaybackVariants,
                    best,
                    isResuming);
            }
            else
            {
                ConfigureVersionSelectors(
                    versions,
                    _watchDetail.PlaybackVariants,
                    best,
                    isResuming);
            }

            // Show progress bar overlay
            if (isResuming && userData?.DurationSeconds > 0)
            {
                var fraction = userData.PositionSeconds!.Value / userData.DurationSeconds.Value;
                SplitPlayButton.SizeChanged -= OnSplitPlayButtonSizeChanged;
                SplitPlayButton.SizeChanged += OnSplitPlayButtonSizeChanged;
                _playProgressFraction = Math.Min(fraction, 1.0);
                UpdatePlayProgressWidth();
            }
        }
    }

    private double _playProgressFraction;

    private void OnSplitPlayButtonSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_playProgressFraction > 0 && e.NewSize.Width > 0)
        {
            PlayProgressBar.Width = e.NewSize.Width * _playProgressFraction;
        }
    }

    // ===== Pre-play audio track popover (Phase 2a) =====

    /// <summary>
    /// Rebuild the Audio track popover for the currently-selected file version.
    /// Hides the button if the version has fewer than 2 audio tracks. Mirrors
    /// the WebUI AudioTracksPopover behavior: "Auto: &lt;summary&gt;" option first,
    /// then every track individually. User selection is stored in
    /// <see cref="_selectedAudioTrackIndex"/> and flows into StartSessionAsync
    /// when playback starts.
    /// </summary>
    private void BuildAudioTracksFlyout(FileVersion? version)
    {
        AudioTracksFlyoutContent.Children.Clear();
        var tracks = version?.AudioTracks;
        if (tracks == null || tracks.Count == 0)
        {
            AudioTracksButton.Visibility = Visibility.Collapsed;
            _selectedAudioTrackIndex = null;
            return;
        }

        // Show only when there's a real choice — one track = no point in showing.
        AudioTracksButton.Visibility = Visibility.Visible;

        // Reset selection when we're on a new version so stale explicit indices
        // from a previous version don't leak in.
        _selectedAudioTrackIndex = null;

        // Auto index: prefer server's effective_audio_track_index, else first default, else 0.
        var autoIndex = version!.EffectiveAudioTrackIndex ?? -1;
        if (autoIndex < 0 || autoIndex >= tracks.Count)
            autoIndex = tracks.FindIndex(t => t.Default);
        if (autoIndex < 0) autoIndex = 0;
        RenderAudioTracksFlyout(version, tracks, autoIndex);
        UpdateAudioTracksSummary(tracks, autoIndex);
    }

    private void RenderAudioTracksFlyout(FileVersion version, List<AudioTrackInfo> tracks, int autoIndex)
    {
        AudioTracksFlyoutContent.Children.Clear();
        var autoSummary = FormatAudioTrackSummary(tracks[autoIndex]);
        var autoItem = CreateTrackSelectionRow("Auto", autoSummary, [], _selectedAudioTrackIndex is null);
        autoItem.Click += (_, _) =>
        {
            _selectedAudioTrackIndex = null;
            UpdateAudioTracksSummary(tracks, autoIndex);
            AudioTracksFlyout.Hide();
            RenderAudioTracksFlyout(version, tracks, autoIndex);
        };
        AudioTracksFlyoutContent.Children.Add(autoItem);

        for (var i = 0; i < tracks.Count; i++)
        {
            var idx = i;
            var track = tracks[i];
            var title = !string.IsNullOrWhiteSpace(track.Language)
                ? MediaLanguageCatalog.Label(track.Language)
                : !string.IsNullOrWhiteSpace(track.Title)
                    ? track.Title!
                    : !string.IsNullOrWhiteSpace(track.EmbeddedTitle) ? track.EmbeddedTitle! : "Unknown";
            var embeddedTitle = track.Title?.Trim() ?? track.EmbeddedTitle?.Trim() ?? "";
            var descriptionParts = new List<string>();
            if (embeddedTitle.Length > 0 && !embeddedTitle.Equals(title, StringComparison.OrdinalIgnoreCase))
                descriptionParts.Add(embeddedTitle);
            if (!string.IsNullOrWhiteSpace(track.Layout)) descriptionParts.Add(track.Layout!);
            if (track.Bitrate is > 0) descriptionParts.Add(FormatTrackBitrate(track.Bitrate.Value));
            if (track.SampleRate is > 0) descriptionParts.Add(FormatSampleRate(track.SampleRate.Value));
            if (track.BitDepth is > 0) descriptionParts.Add($"{track.BitDepth}-bit");
            var badges = new List<string>();
            if (!string.IsNullOrWhiteSpace(track.Codec)) badges.Add(VersionRanking.MapAudioLabel(track.Codec));
            if (track.Channels.HasValue) badges.Add(FormatAudioChannels(track.Channels.Value));
            if (track.Default) badges.Add("DEFAULT");
            var item = CreateTrackSelectionRow(
                title,
                string.Join(" · ", descriptionParts),
                badges,
                _selectedAudioTrackIndex == idx);
            item.Click += (_, _) =>
            {
                _selectedAudioTrackIndex = idx;
                UpdateAudioTracksSummary(tracks, autoIndex);
                AudioTracksFlyout.Hide();
                RenderAudioTracksFlyout(version, tracks, autoIndex);
            };
            AudioTracksFlyoutContent.Children.Add(item);
        }
    }

    private void UpdateAudioTracksSummary(List<AudioTrackInfo> tracks, int autoIndex)
    {
        var shownIndex = _selectedAudioTrackIndex ?? autoIndex;
        if (shownIndex < 0 || shownIndex >= tracks.Count) shownIndex = 0;
        var track = tracks[shownIndex];
        AudioTracksSummary.Text = (_selectedAudioTrackIndex == null ? "Auto: " : "") +
                                  FormatAudioTrackSummary(track);
    }

    // ===== Pre-play subtitle popover (Phase 2b) =====

    /// <summary>
    /// Rebuild the Subtitles popover for the currently-selected file version.
    /// Shows "Off" + "Auto" + the selected version's embedded and external
    /// subtitle inventory. PlayerService maps the choice to the playback
    /// session's source-specific track order and uses either the native stream
    /// (direct play) or Silo's sidecar URL (remux/HLS).
    /// </summary>
    private void BuildSubtitlesPopoverFlyout(FileVersion? version, bool resetSelection = true)
    {
        SubtitlesPopoverContent.Children.Clear();
        var subs = version?.SubtitleTracks ?? [];
        if (version == null)
        {
            SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
            _selectedSubtitleIndex = null;
            return;
        }

        SubtitlesPopoverButton.Visibility = Visibility.Visible;
        if (resetSelection)
        {
            _selectedSubtitleIndex = null; // Reset only on an actual version change.
            _selectedSubtitleSignature = null;
        }

        var downloaded = _downloadedSubtitles ?? [];
        var hasSubtitleInventory = subs.Count > 0 || downloaded.Count > 0;

        // WebUI SubtitlesPopover always exposes the pre-play mode controls
        // for a selected version, even before any tracks are available:
        // Auto, Off, optional candidate sections, and the empty-state copy.

        RenderSubtitlesPopoverFlyout(version, subs, downloaded);

        if (hasSubtitleInventory)
            UpdateSubtitlesPopoverSummary(subs);
        else
            SubtitlesSummary.Text = "Auto: Off";
    }

    private void RenderSubtitlesPopoverFlyout(
        FileVersion version,
        List<VersionSubtitleTrack> subs,
        List<SubtitleEntry> downloaded)
    {
        SubtitlesPopoverContent.Children.Clear();
        var autoCandidate = ResolveAutoSubtitle(subs);
        var preferredSignature = _selectedSubtitleIndex is null && _selectedSubtitleSignature is null
            ? _watchDetail?.EffectiveSubtitleTrackSignature
            : null;
        var autoItem = CreateTrackSelectionRow(
            "Auto",
            preferredSignature is null
                ? autoCandidate is null ? "Off" : FormatSubtitleTrackSummary(autoCandidate)
                : "Reset to profile defaults",
            [],
            _selectedSubtitleIndex is null && _selectedSubtitleSignature is null && preferredSignature is null);
        autoItem.Click += async (_, _) =>
        {
            _selectedSubtitleIndex = null;
            _selectedSubtitleSignature = null;
            UpdateSubtitlesPopoverSummary(subs);
            SubtitlesPopoverFlyout.Hide();
            await ResetPrePlaySubtitlePreferenceAsync();
            RenderSubtitlesPopoverFlyout(version, subs, downloaded);
        };
        SubtitlesPopoverContent.Children.Add(autoItem);

        var offItem = CreateTrackSelectionRow("Off", null, [], _selectedSubtitleIndex == -1);
        offItem.Click += async (_, _) =>
        {
            _selectedSubtitleIndex = -1;
            _selectedSubtitleSignature = null;
            UpdateSubtitlesPopoverSummary(subs);
            SubtitlesPopoverFlyout.Hide();
            await PersistPrePlaySubtitlePreferenceAsync(null, -1, "off");
            RenderSubtitlesPopoverFlyout(version, subs, downloaded);
        };
        SubtitlesPopoverContent.Children.Add(offItem);

        var hasSubtitleInventory = subs.Count > 0 || downloaded.Count > 0;

        if (hasSubtitleInventory)
        {
            void AddTrackGroup(string heading, IEnumerable<(VersionSubtitleTrack Track, int Index)> tracks)
            {
                var rows = tracks.ToList();
                if (rows.Count == 0) return;

                SubtitlesPopoverContent.Children.Add(new TextBlock
                {
                    Text = heading,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                    Margin = new Thickness(12, 5, 12, 0),
                });
                foreach (var (sub, idx) in rows)
                {
                    var signature = BuildSubtitleSignature(sub);
                    var badges = new List<string>();
                    var format = SubtitleFormatLabel(sub.Codec);
                    if (format.Length > 0) badges.Add(format);
                    if (sub.Forced == true) badges.Add("FORCED");
                    if (sub.HearingImpaired == true) badges.Add("HI");
                    if (sub.Default == true) badges.Add("DEFAULT");
                    var item = CreateTrackSelectionRow(
                        FormatSubtitleTrackSummary(sub),
                        SubtitleTrackDescription(sub),
                        badges,
                        (_selectedSubtitleIndex == idx && SubtitleSignaturesEqual(_selectedSubtitleSignature, signature)) ||
                        (_selectedSubtitleIndex is null && _selectedSubtitleSignature is null &&
                         SubtitleSignaturesEqual(preferredSignature, signature)));
                    item.Click += async (_, _) =>
                    {
                        _selectedSubtitleIndex = idx;
                        _selectedSubtitleSignature = signature;
                        UpdateSubtitlesPopoverSummary(subs);
                        SubtitlesPopoverFlyout.Hide();
                        await PersistPrePlaySubtitlePreferenceAsync(signature, sub.Index ?? idx, "always");
                        RenderSubtitlesPopoverFlyout(version, subs, downloaded);
                    };
                    SubtitlesPopoverContent.Children.Add(item);
                }
            }

            var indexedTracks = subs
                .Select((track, index) => (Track: track, Index: index))
                .OrderBy(row => MediaLanguageCatalog.Label(row.Track.Language),
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(row => row.Track.Forced == true)
                .ThenByDescending(row => row.Track.Default == true)
                .ThenBy(
                    row => row.Track.Title ?? row.Track.EmbeddedTitle ?? row.Track.Codec ?? "",
                    StringComparer.CurrentCultureIgnoreCase);
            AddTrackGroup("Embedded", indexedTracks.Where(row => row.Track.External != true));
            AddTrackGroup("External", indexedTracks.Where(row => row.Track.External == true));

            if (_loadingDownloadedSubtitles)
                AddDownloadedSubtitlesLoadingState();

            if (downloaded.Count > 0)
            {
                SubtitlesPopoverContent.Children.Add(new TextBlock
                {
                    Text = "Downloaded",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                    Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                    Margin = new Thickness(12, 5, 12, 0),
                });
                foreach (var subtitle in downloaded
                    .OrderByDescending(entry => entry.Score)
                    .ThenBy(entry => entry.Provider, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(entry => entry.ReleaseName, StringComparer.CurrentCultureIgnoreCase))
                {
                    var downloadedSubtitle = subtitle;
                    var signature = new SubtitleTrackSignature
                    {
                        Source = "downloaded",
                        Language = downloadedSubtitle.Language,
                        Codec = downloadedSubtitle.Format,
                        Label = DownloadedSubtitleLabel(downloadedSubtitle),
                        Forced = false,
                        HearingImpaired = downloadedSubtitle.HearingImpaired,
                    };
                    var badges = new List<string>();
                    var format = SubtitleFormatLabel(downloadedSubtitle.Format);
                    if (format.Length > 0) badges.Add(format);
                    if (downloadedSubtitle.HearingImpaired) badges.Add("HI");
                    var item = CreateTrackSelectionRow(
                        FormatDownloadedSubtitleSummary(downloadedSubtitle),
                        DownloadedSubtitleLabel(downloadedSubtitle),
                        badges,
                        SubtitleSignaturesEqual(_selectedSubtitleSignature, signature) ||
                        (_selectedSubtitleIndex is null && _selectedSubtitleSignature is null &&
                         SubtitleSignaturesEqual(preferredSignature, signature)));
                    item.Click += async (_, _) =>
                    {
                        _selectedSubtitleIndex = null;
                        _selectedSubtitleSignature = signature;
                        SubtitlesSummary.Text = FormatDownloadedSubtitleSummary(downloadedSubtitle);
                        SubtitlesPopoverFlyout.Hide();
                        await PersistPrePlaySubtitlePreferenceAsync(signature, -1, "always");
                        RenderSubtitlesPopoverFlyout(version, subs, downloaded);
                    };
                    SubtitlesPopoverContent.Children.Add(item);
                }
            }
        }
        else if (_loadingDownloadedSubtitles)
        {
            AddDownloadedSubtitlesLoadingState();
        }
        else
        {
            SubtitlesPopoverContent.Children.Add(new TextBlock
            {
                Text = "No subtitles available.",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                Padding = new Thickness(12, 7, 12, 7),
            });
        }

        void AddDownloadedSubtitlesLoadingState()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Padding = new Thickness(12, 7, 12, 7),
            };
            row.Children.Add(new ProgressRing
            {
                Width = 12,
                Height = 12,
                IsActive = true,
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = "Loading downloaded...",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            SubtitlesPopoverContent.Children.Add(row);
        }
    }

    private static string DefaultLeafPlayLabel(string? itemType)
        => string.Equals(itemType, "episode", StringComparison.OrdinalIgnoreCase)
            ? "Play Episode"
            : "Play";

    private void UpdatePlayProgressWidth()
    {
        if (SplitPlayButton.ActualWidth > 0)
            PlayProgressBar.Width = SplitPlayButton.ActualWidth * Math.Clamp(_playProgressFraction, 0, 1);
    }

    private async Task OpenSubtitleSearchDialogAsync()
    {
        if (_selectedVersion == null) return;
        // Default language: effective pref from watch detail, fallback to
        // profile pref, else English.
        string? defaultLang = _watchDetail?.EffectiveSubtitleLanguage;
        if (string.IsNullOrWhiteSpace(defaultLang))
        {
            try
            {
                var s = App.Services.GetService<SettingsViewModel>();
                defaultLang = s?.SubtitleLanguage;
            }
            catch { }
        }
        var dialog = new Controls.SubtitleSearchDialog(
            _selectedVersion.FileId,
            defaultLang,
            playerMode: false,
            title: ViewModel.Item?.Title,
            versionLabel: BuildVersionQualitySummary(_selectedVersion))
        {
            XamlRoot = this.XamlRoot,
        };
        dialog.SubtitleDownloaded += async _ =>
        {
            // Refresh the watch-detail so the new subtitle appears in
            // SubtitleTracks and the popover can reflect it next open.
            try
            {
                if (_watchDetail != null)
                    await LoadWatchDetailAsync(_watchDetail.ContentId);
            }
            catch { }
        };
        await dialog.ShowAsync();
    }

    private void UpdateSubtitlesPopoverSummary(List<VersionSubtitleTrack> subs)
    {
        if (_selectedSubtitleIndex == -1)
        {
            SubtitlesSummary.Text = "Off";
            return;
        }
        if (_selectedSubtitleSignature?.Source.Equals("downloaded", StringComparison.OrdinalIgnoreCase) == true)
        {
            var selected = _downloadedSubtitles.FirstOrDefault(subtitle =>
                string.Equals(subtitle.Language, _selectedSubtitleSignature.Language, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(subtitle.Format, _selectedSubtitleSignature.Codec, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(DownloadedSubtitleLabel(subtitle), _selectedSubtitleSignature.Label, StringComparison.OrdinalIgnoreCase) &&
                subtitle.HearingImpaired == _selectedSubtitleSignature.HearingImpaired);
            SubtitlesSummary.Text = selected != null
                ? FormatDownloadedSubtitleSummary(selected)
                : "Downloaded subtitle";
            return;
        }
        if (_selectedSubtitleIndex == null)
        {
            // Resolve what "Auto" picks using profile prefs + effective audio language
            // so the user can see what will actually turn on. The WebUI uses
            // "Auto: Off" when the policy does not resolve a candidate. A
            // stored series override is presented as the resolved selection,
            // without an "Auto:" prefix.
            var preferredSignature = _watchDetail?.EffectiveSubtitleTrackSignature;
            if (preferredSignature?.Source.Equals("downloaded", StringComparison.OrdinalIgnoreCase) == true)
            {
                var downloadedOverride = FindDownloadedSubtitle(preferredSignature);
                if (downloadedOverride != null)
                {
                    SubtitlesSummary.Text = FormatDownloadedSubtitleSummary(downloadedOverride);
                    return;
                }
            }
            var resolved = ResolveAutoSubtitle(subs);
            SubtitlesSummary.Text = resolved != null
                ? preferredSignature != null
                    ? FormatSubtitleTrackSummary(resolved)
                    : $"Auto: {FormatSubtitleTrackSummary(resolved)}"
                : "Auto: Off";
            return;
        }
        var idx = _selectedSubtitleIndex.Value;
        if (idx < 0 || idx >= subs.Count)
        {
            SubtitlesSummary.Text = "Auto: Off";
            return;
        }
        SubtitlesSummary.Text = FormatSubtitleTrackSummary(subs[idx]);
    }

    /// <summary>
    /// Compute which subtitle track the server-side "auto" selection would pick,
    /// for display in the pre-play popover ("Auto: ENG SRT" vs. bare "Auto").
    /// Uses effective prefs from WatchDetail when available (per-series aware),
    /// falls back to profile-level SettingsViewModel prefs.
    /// </summary>
    private VersionSubtitleTrack? ResolveAutoSubtitle(List<VersionSubtitleTrack> subs)
    {
        if (subs.Count == 0 || _watchDetail == null || _selectedVersion == null) return null;

        // Effective (server-resolved per-series) values win over profile-level settings.
        var mode = _watchDetail.EffectiveSubtitleMode;
        var preferredLang = _watchDetail.EffectiveSubtitleLanguage;
        var showForced = _watchDetail.EffectiveShowForcedSubtitles;
        if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(preferredLang) || showForced == null)
        {
            try
            {
                var settings = App.Services.GetRequiredService<SettingsViewModel>();
                if (string.IsNullOrEmpty(mode)) mode = settings.SubtitleMode;
                if (string.IsNullOrEmpty(preferredLang)) preferredLang = settings.SubtitleLanguage;
                if (showForced == null) showForced = settings.ShowForcedSubtitles;
            }
            catch { /* settings unavailable — use defaults below */ }
        }

        // Audio language for the current version + selected track (drives "same as audio" logic).
        string? audioLang = null;
        var tracks = _selectedVersion.AudioTracks;
        if (tracks != null && tracks.Count > 0)
        {
            var autoIdx = _selectedVersion.EffectiveAudioTrackIndex ?? -1;
            if (autoIdx < 0 || autoIdx >= tracks.Count) autoIdx = tracks.FindIndex(t => t.Default);
            if (autoIdx < 0) autoIdx = 0;
            var activeIdx = _selectedAudioTrackIndex ?? autoIdx;
            if (activeIdx >= 0 && activeIdx < tracks.Count)
                audioLang = tracks[activeIdx].Language;
            if (string.IsNullOrEmpty(audioLang) &&
                _selectedVersion.EffectiveAudioTrackIndex == activeIdx)
                audioLang = _selectedVersion.EffectiveAudioLanguage;
        }

        var candidates = SubtitleAutoSelect.BuildCandidates(subs);
        var idx = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
            Mode: SubtitleAutoSelect.NormalizeSubtitleMode(mode),
            Tracks: candidates,
            PreferredLanguage: preferredLang,
            AudioLanguage: audioLang,
            ProfileLanguage: App.Services.GetRequiredService<Core.Services.AuthService>()
                .SelectedProfile?.Language,
            ShowForcedSubtitles: showForced ?? true,
            PreferredTrackSignature: _watchDetail.EffectiveSubtitleTrackSignature));

        if (idx == null) return null;
        // Resolve back to a VersionSubtitleTrack. OriginalIndex was set to track.Index ?? fallback.
        var track = subs.FirstOrDefault(s => (s.Index ?? -1) == idx.Value);
        if (track != null) return track;
        // Fallback: position-based lookup if tracks lack Index.
        return idx.Value >= 0 && idx.Value < subs.Count ? subs[idx.Value] : null;
    }

    /// <summary>Current WebUI pill label: "English · SRT", "Japanese (Forced) · ASS", etc.</summary>
    private static string FormatSubtitleTrackSummary(VersionSubtitleTrack sub)
    {
        var name = !string.IsNullOrWhiteSpace(sub.Language)
            ? MediaLanguageCatalog.Label(sub.Language)
            : !string.IsNullOrWhiteSpace(sub.Title)
                ? sub.Title!
                : "Unknown";
        if (sub.HearingImpaired == true &&
            !System.Text.RegularExpressions.Regex.IsMatch(name, @"\b(?:sdh|cc|hi)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            name += " (SDH)";
        if (sub.Forced == true &&
            !name.Contains("forced", StringComparison.OrdinalIgnoreCase))
            name += " (Forced)";

        var format = SubtitleFormatLabel(sub.Codec);
        return string.IsNullOrEmpty(format) ? name : $"{name} · {format}";
    }

    private static string FormatSubtitleTrackMenuText(VersionSubtitleTrack sub)
    {
        var summary = FormatSubtitleTrackSummary(sub);
        var description = sub.Title?.Trim()
            ?? sub.EmbeddedTitle?.Trim()
            ?? sub.FileName?.Trim();
        if (string.IsNullOrWhiteSpace(description))
            return summary;

        var normalizedDescription = NormalizeSubtitleDisplayToken(description);
        var normalizedCodec = NormalizeSubtitleDisplayToken(sub.Codec);
        var normalizedFormat = NormalizeSubtitleDisplayToken(SubtitleFormatLabel(sub.Codec));
        var normalizedLanguage = NormalizeSubtitleDisplayToken(MediaLanguageCatalog.Label(sub.Language));
        if (normalizedDescription == normalizedCodec ||
            normalizedDescription == normalizedFormat ||
            normalizedDescription == normalizedLanguage)
            return summary;

        return $"{summary} — {description}";
    }

    private static Button CreateTrackSelectionRow(
        string title,
        string? description,
        IReadOnlyList<string> badges,
        bool active)
    {
        var titleRow = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 4 };
        titleRow.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        });
        foreach (var badge in badges.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            titleRow.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"],
                BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock
                {
                    Text = badge,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    CharacterSpacing = 30,
                },
            });
        }

        var copy = new StackPanel { Spacing = 3 };
        copy.Children.Add(titleRow);
        if (!string.IsNullOrWhiteSpace(description))
        {
            copy.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
            });
        }

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.Children.Add(copy);
        if (active)
        {
            var check = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
            };
            Grid.SetColumn(check, 1);
            grid.Children.Add(check);
        }

        var row = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = new CornerRadius(8),
            Background = active
                ? (Brush)Application.Current.Resources["AccentBackgroundBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(row, title);
        return row;
    }

    private static string? SubtitleTrackDescription(VersionSubtitleTrack sub)
    {
        var description = sub.Title?.Trim() ?? sub.EmbeddedTitle?.Trim() ?? sub.FileName?.Trim();
        if (string.IsNullOrWhiteSpace(description)) return null;
        var normalizedDescription = NormalizeSubtitleDisplayToken(description);
        if (normalizedDescription == NormalizeSubtitleDisplayToken(sub.Codec) ||
            normalizedDescription == NormalizeSubtitleDisplayToken(SubtitleFormatLabel(sub.Codec)) ||
            normalizedDescription == NormalizeSubtitleDisplayToken(MediaLanguageCatalog.Label(sub.Language)))
            return null;
        return description;
    }

    private static bool SubtitleSignaturesEqual(SubtitleTrackSignature? left, SubtitleTrackSignature? right)
        => left is not null && right is not null &&
           string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(MediaLanguageCatalog.Normalize(left.Language), MediaLanguageCatalog.Normalize(right.Language), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(SubtitleFormatLabel(left.Codec), SubtitleFormatLabel(right.Codec), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(left.Label?.Trim(), right.Label?.Trim(), StringComparison.OrdinalIgnoreCase) &&
           left.Forced == right.Forced &&
           left.HearingImpaired == right.HearingImpaired;

    private string? PrePlaySubtitlePreferenceId()
        => !string.IsNullOrWhiteSpace(_watchDetail?.SeriesId)
            ? _watchDetail.SeriesId
            : _watchDetail?.ContentId;

    private async Task PersistPrePlaySubtitlePreferenceAsync(
        SubtitleTrackSignature? signature,
        int trackIndex,
        string mode)
    {
        var preferenceId = PrePlaySubtitlePreferenceId();
        if (string.IsNullOrWhiteSpace(preferenceId)) return;
        try
        {
            await App.Services.GetRequiredService<PlaybackApi>().SaveSubtitlePrefsAsync(
                preferenceId,
                new SubtitlePreferenceRequest
                {
                    SubtitleLanguage = signature?.Language ?? "",
                    SubtitleTrackIndex = trackIndex,
                    SubtitleMode = mode,
                    TrackSignature = signature,
                    ShowForcedSubtitles = _watchDetail?.EffectiveShowForcedSubtitles,
                });
            if (_watchDetail is not null)
            {
                _watchDetail.EffectiveSubtitleLanguage = signature?.Language;
                _watchDetail.EffectiveSubtitleMode = mode;
                _watchDetail.EffectiveSubtitleTrackSignature = signature;
            }
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error(
                string.IsNullOrWhiteSpace(ex.Message) ? "Failed to save subtitle preference" : ex.Message);
        }
    }

    private async Task ResetPrePlaySubtitlePreferenceAsync()
    {
        var preferenceId = PrePlaySubtitlePreferenceId();
        if (string.IsNullOrWhiteSpace(preferenceId)) return;
        try
        {
            await App.Services.GetRequiredService<PlaybackApi>().DeleteSubtitlePrefsAsync(preferenceId);
            if (_watchDetail is not null)
            {
                _watchDetail.EffectiveSubtitleLanguage = null;
                _watchDetail.EffectiveSubtitleMode = "auto";
                _watchDetail.EffectiveSubtitleTrackSignature = null;
            }
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<ToastService>().Error(
                string.IsNullOrWhiteSpace(ex.Message) ? "Failed to reset subtitle preference" : ex.Message);
        }
    }

    private static string FormatAudioChannels(int channels) => channels switch
    {
        1 => "mono",
        2 => "stereo",
        6 => "5.1",
        8 => "7.1",
        _ => $"{channels} ch",
    };

    private static string FormatTrackBitrate(int bitrate)
    {
        var bitsPerSecond = bitrate < 10_000 ? bitrate * 1000d : bitrate;
        return bitsPerSecond >= 1_000_000
            ? $"{bitsPerSecond / 1_000_000:0.#} Mbps"
            : $"{bitsPerSecond / 1000:0} kbps";
    }

    private static string FormatSampleRate(int sampleRate)
        => sampleRate >= 1000 ? $"{sampleRate / 1000d:0.#} kHz" : $"{sampleRate} Hz";

    private static string NormalizeSubtitleDisplayToken(string? value)
        => new((value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private async Task LoadDownloadedSubtitlesAsync(string contentId, int fileId)
    {
        _downloadedSubtitles = [];
        _loadingDownloadedSubtitles = true;
        if (_selectedVersion?.FileId == fileId)
            BuildSubtitlesPopoverFlyout(_selectedVersion, resetSelection: false);

        try
        {
            var response = await App.Services.GetRequiredService<PlaybackApi>()
                .GetSubtitlesAsync(fileId);
            if (!string.Equals(_playableContentId, contentId, StringComparison.Ordinal) ||
                _selectedVersion?.FileId != fileId)
                return;

            _downloadedSubtitles = response.Subtitles ?? [];
            BuildSubtitlesPopoverFlyout(_selectedVersion, resetSelection: false);
        }
        catch
        {
            // Provider subtitles are optional. Embedded and external tracks
            // remain selectable when this companion request is unavailable.
        }
        finally
        {
            if (string.Equals(_playableContentId, contentId, StringComparison.Ordinal) &&
                _selectedVersion?.FileId == fileId)
            {
                _loadingDownloadedSubtitles = false;
                BuildSubtitlesPopoverFlyout(_selectedVersion, resetSelection: false);
            }
        }
    }

    private static SubtitleTrackSignature BuildSubtitleSignature(VersionSubtitleTrack sub)
        => new()
        {
            Source = sub.External == true ? "external" : "embedded",
            Language = sub.Language,
            Codec = sub.Codec,
            Label = !string.IsNullOrWhiteSpace(sub.Title)
                ? sub.Title
                : !string.IsNullOrWhiteSpace(sub.EmbeddedTitle)
                    ? sub.EmbeddedTitle
                    : !string.IsNullOrWhiteSpace(sub.FileName)
                        ? sub.FileName
                        : sub.Language,
            Forced = sub.Forced == true,
            HearingImpaired = sub.HearingImpaired == true,
        };

    private static string DownloadedSubtitleLabel(SubtitleEntry subtitle)
    {
        var release = subtitle.ReleaseName?.Trim();
        var provider = subtitle.Provider?.Trim();
        if (!string.IsNullOrWhiteSpace(release) && !string.IsNullOrWhiteSpace(provider))
            return $"{release} ({provider})";
        if (!string.IsNullOrWhiteSpace(release)) return release;
        if (!string.IsNullOrWhiteSpace(provider)) return provider;
        return Services.PlayerService.LanguageCodeToName(subtitle.Language);
    }

    private static string FormatDownloadedSubtitleSummary(SubtitleEntry subtitle)
    {
        var language = MediaLanguageCatalog.Label(subtitle.Language);
        if (subtitle.HearingImpaired) language += " (SDH)";
        var format = SubtitleFormatLabel(subtitle.Format);
        return string.IsNullOrEmpty(format) ? language : $"{language} · {format}";
    }

    private static string FormatDownloadedSubtitleMenuText(SubtitleEntry subtitle)
    {
        var summary = FormatDownloadedSubtitleSummary(subtitle);
        var release = DownloadedSubtitleLabel(subtitle);
        return string.Equals(release, MediaLanguageCatalog.Label(subtitle.Language), StringComparison.OrdinalIgnoreCase)
            ? summary
            : $"{summary} — {release}";
    }

    private SubtitleEntry? FindDownloadedSubtitle(SubtitleTrackSignature signature)
        => _downloadedSubtitles.FirstOrDefault(subtitle =>
            string.Equals(
                MediaLanguageCatalog.Normalize(subtitle.Language),
                MediaLanguageCatalog.Normalize(signature.Language),
                StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(signature.Codec) ||
             string.Equals(SubtitleFormatLabel(subtitle.Format), SubtitleFormatLabel(signature.Codec),
                 StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(signature.Label) ||
             string.Equals(DownloadedSubtitleLabel(subtitle), signature.Label, StringComparison.OrdinalIgnoreCase)) &&
            subtitle.HearingImpaired == signature.HearingImpaired);

    private static string SubtitleFormatLabel(string? codec)
        => codec?.Trim().ToLowerInvariant() switch
        {
            "ass" or "ssa" => "ASS",
            "srt" or "subrip" => "SRT",
            "vtt" or "webvtt" => "VTT",
            "pgs" or "hdmv_pgs_subtitle" => "PGS",
            "dvd_subtitle" => "DVD",
            "dvb_subtitle" => "DVB",
            null or "" => "",
            _ => codec.Trim().ToUpperInvariant(),
        };

    /// <summary>
    /// Build the WebUI audio summary: "English · AC3 · 5.1".
    /// Mirrors the WebUI <c>formatAudioTrackSummary</c> helper.
    /// </summary>
    private static string FormatAudioTrackSummary(AudioTrackInfo track)
    {
        var parts = new List<string>();
        var lang = !string.IsNullOrWhiteSpace(track.Language)
            ? MediaLanguageCatalog.Label(track.Language)
            : null;
        var title = !string.IsNullOrWhiteSpace(track.Title) ? track.Title : track.EmbeddedTitle;
        if (!string.IsNullOrEmpty(lang)) parts.Add(lang);
        else if (!string.IsNullOrEmpty(title)) parts.Add(title!);
        else parts.Add("Unknown");

        if (!string.IsNullOrWhiteSpace(track.Codec))
            parts.Add(VersionRanking.MapAudioLabel(track.Codec));

        if (track.Channels.HasValue)
        {
            var ch = track.Channels.Value switch
            {
                2 => "stereo",
                6 => "5.1",
                8 => "7.1",
                _ => $"{track.Channels.Value} ch",
            };
            parts.Add(ch);
        }

        return string.Join(" · ", parts);
    }

    private void ConfigureVersionSelectors(
        List<FileVersion> versions,
        List<PlaybackVariant>? variants,
        FileVersion? selected,
        bool isResuming)
    {
        selected ??= versions.FirstOrDefault();
        var orderedVariants = (variants ?? [])
            .Select((variant, index) => new { Variant = variant, Index = index })
            .OrderBy(entry => VersionRanking.PlaybackVariantEditionPreference(entry.Variant))
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Variant)
            .ToList();
        var hasNamedEdition = orderedVariants.Any(variant => !string.IsNullOrWhiteSpace(variant.EditionKey));
        var labels = orderedVariants.Select(variant => BuildEditionLabel(variant, hasNamedEdition)).ToList();
        var showEditions = orderedVariants.Count > 1
                           && hasNamedEdition
                           && labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

        PlaybackVariant? activeVariant = null;
        if (selected != null)
        {
            activeVariant = orderedVariants.FirstOrDefault(variant =>
                variant.Parts.Any(part => part.Versions.Any(version => version.FileId == selected.FileId)));
        }
        activeVariant ??= orderedVariants.FirstOrDefault();

        EditionFlyout.Items.Clear();
        EditionButton.Visibility = showEditions ? Visibility.Visible : Visibility.Collapsed;
        if (showEditions && activeVariant != null)
        {
            EditionSummaryText.Text = BuildEditionLabel(activeVariant, hasNamedEdition);
            foreach (var variant in orderedVariants)
            {
                var option = new MenuFlyoutItem
                {
                    Text = BuildEditionMenuLabel(variant, versions, hasNamedEdition),
                };
                var selectedVariant = variant;
                option.Click += (_, _) =>
                {
                    var defaultVersion = ResolveVariantDefaultVersion(selectedVariant, versions);
                    if (defaultVersion == null) return;
                    _selectedVersion = defaultVersion;
                    _downloadedSubtitles = [];
                    UpdateSelectedVersionUi(defaultVersion);
                    ConfigureVersionSelectors(versions, orderedVariants, defaultVersion, isResuming);
                    if (_playableContentId != null)
                        _ = LoadDownloadedSubtitlesAsync(_playableContentId, defaultVersion.FileId);
                };
                EditionFlyout.Items.Add(option);
            }
        }

        var activeVersions = activeVariant?.Parts
            .OrderBy(part => part.PartIndex)
            .FirstOrDefault()?.Versions;
        BuildVersionFlyout(
            activeVersions is { Count: > 0 } ? activeVersions : versions,
            isResuming,
            versions,
            orderedVariants);
    }

    private static string BuildEditionLabel(PlaybackVariant variant, bool hasNamedEditions)
    {
        if (!string.IsNullOrWhiteSpace(variant.EditionRaw)) return variant.EditionRaw.Trim();
        if (!string.IsNullOrWhiteSpace(variant.EditionKey))
        {
            return string.Join(" ", variant.EditionKey
                .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Equals("imax", StringComparison.OrdinalIgnoreCase)
                    ? "IMAX"
                    : char.ToUpperInvariant(part[0]) + part[1..]));
        }
        return hasNamedEditions ? "Standard" : "Edition";
    }

    private static string BuildEditionMenuLabel(
        PlaybackVariant variant,
        List<FileVersion> versions,
        bool hasNamedEditions)
    {
        var label = BuildEditionLabel(variant, hasNamedEditions);
        var firstPartVersions = variant.Parts.OrderBy(part => part.PartIndex).FirstOrDefault()?.Versions ?? [];
        var selected = ResolveVariantDefaultVersion(variant, versions);
        var detail = new List<string>();
        if (firstPartVersions.Count > 1) detail.Add($"{firstPartVersions.Count} versions");
        if (selected != null)
        {
            var summary = BuildQualitySummary(selected);
            if (!string.IsNullOrEmpty(summary)) detail.Add(summary);
        }
        return detail.Count == 0 ? label : $"{label}  —  {string.Join(" · ", detail)}";
    }

    private static FileVersion? ResolveVariantDefaultVersion(
        PlaybackVariant variant,
        List<FileVersion> allVersions)
    {
        var firstPart = variant.Parts.OrderBy(part => part.PartIndex).FirstOrDefault();
        if (firstPart == null) return null;
        if (firstPart.DefaultFileId is int fileId)
            return allVersions.FirstOrDefault(version => version.FileId == fileId)
                   ?? firstPart.Versions.FirstOrDefault(version => version.FileId == fileId);
        return firstPart.Versions
            .OrderByDescending(version => VersionRanking.ResolutionScore(version.Resolution))
            .ThenByDescending(version => version.Bitrate)
            .FirstOrDefault();
    }

    private void UpdateSelectedVersionUi(FileVersion version)
    {
        BuildAudioTracksFlyout(version);
        BuildSubtitlesPopoverFlyout(version);
        VersionSummaryText.Text = string.Join(" ", new[]
        {
            version.Resolution,
            MediaVideoRange.Label(version),
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        UpdateSelectedVersionHeroSummary(version);
        BuildMoreFlyout();
    }

    private void UpdateSelectedVersionHeroSummary(FileVersion version)
    {
        var item = ViewModel.Item;
        if (item == null)
            return;

        UpdateQualityBadgesFromItemData(item);

        var variants = _watchDetail?.PlaybackVariants ?? item.PlaybackVariants;
        var selectedVariant = variants?.FirstOrDefault(variant =>
            variant.Parts.Any(part => part.Versions.Any(candidate => candidate.FileId == version.FileId)));
        var multipart = selectedVariant != null &&
            (selectedVariant.PartCount > 1 || selectedVariant.Parts.Count > 1);
        var durationSeconds = multipart && selectedVariant?.TotalDuration is > 0
            ? selectedVariant.TotalDuration.Value
            : version.Duration;

        if (durationSeconds > 0)
        {
            var minutes = (int)Math.Round(durationSeconds / 60d, MidpointRounding.AwayFromZero);
            RuntimeText.Text = minutes >= 60
                ? $"{minutes / 60}H {minutes % 60}M"
                : $"{minutes}M";
        }
        else
        {
            RuntimeText.Text = ViewModel.RuntimeDisplay.ToUpperInvariant();
        }

        MetaDot2.Visibility = Visibility.Collapsed;
    }

    private void BuildVersionFlyout(
        List<FileVersion> versions,
        bool isResuming,
        List<FileVersion>? allVersions = null,
        List<PlaybackVariant>? variants = null)
    {
        VersionFlyout.Items.Clear();
        // VersionDropdown.tsx scopes the Version control to the currently
        // selected edition. If that edition has only one file, the Edition
        // control remains visible but the redundant Version pill does not.
        VersionDropdownButton.Visibility = versions.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;

        // "Play from Start" option when resuming
        if (isResuming)
        {
            var playFromStart = new MenuFlyoutItem { Text = "Play from Start" };
            playFromStart.Click += PlayFromStart_Click;
            VersionFlyout.Items.Add(playFromStart);

            if (versions.Count > 1)
            {
                VersionFlyout.Items.Add(new MenuFlyoutSeparator());
            }
        }

        // Version options (only show if multiple). Sort by resolution
        // descending (4K → 1080p → 720p → SD), matching webui
        // sortByResolution. Ties keep server order.
        if (versions.Count > 1)
        {
            var sorted = versions
                .OrderByDescending(v => ResolutionRank(v.Resolution))
                .ToList();

            foreach (var version in sorted)
            {
                // Webui buildQualitySummary: "2160p · HEVC · HDR · TrueHD"
                // — resolution, video codec, HDR tag (if any), normalized
                // audio codec label. Joined with middle dots.
                var quality = BuildQualitySummary(version);

                // Webui subtitle line: "45.0 GB · Remux" — file size in
                // human-friendly units plus an extracted release hint
                // (Remux / WEB-DL / BluRay / etc.) derived from the filename.
                var subtitleParts = new List<string>();
                if (version.FileSize > 0)
                    subtitleParts.Add(FormatFileSize(version.FileSize));
                var hint = ExtractReleaseHint(version.FileName);
                if (!string.IsNullOrEmpty(hint))
                    subtitleParts.Add(hint!);

                string label = quality;
                if (subtitleParts.Count > 0)
                    label += "  \u2014  " + string.Join(" \u00B7 ", subtitleParts);

                var item = new MenuFlyoutItem { Text = label };
                var fileVersion = version;
                item.Click += (_, _) =>
                {
                    _selectedVersion = fileVersion;
                    _downloadedSubtitles = [];
                    UpdateSelectedVersionUi(fileVersion);
                    if (allVersions != null && variants != null)
                        ConfigureVersionSelectors(allVersions, variants, fileVersion, isResuming);
                    if (_playableContentId != null)
                        _ = LoadDownloadedSubtitlesAsync(_playableContentId, fileVersion.FileId);
                };
                VersionFlyout.Items.Add(item);
            }
        }
    }

    // ─── Version formatting helpers (webui lib/quality.ts parity) ──────

    /// <summary>
    /// Build the main quality summary line for a file version. Matches the
    /// webui <c>buildQualitySummary</c> output format
    /// <c>"2160p · HEVC · HDR · TrueHD"</c>. Empty fields are skipped.
    /// </summary>
    private static string BuildQualitySummary(FileVersion version)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(version.Resolution))
            parts.Add(version.Resolution);
        if (!string.IsNullOrEmpty(version.CodecVideo))
            parts.Add(version.CodecVideo.ToUpperInvariant());
        var rangeLabel = MediaVideoRange.Label(version);
        if (!string.IsNullOrEmpty(rangeLabel))
            parts.Add(rangeLabel);
        var audio = string.IsNullOrWhiteSpace(version.CodecAudio)
            ? null
            : VersionRanking.MapAudioLabel(version.CodecAudio);
        if (!string.IsNullOrEmpty(audio))
            parts.Add(audio!);
        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(version.Container))
            parts.Add(version.Container.ToUpperInvariant());
        return string.Join(" \u00B7 ", parts);
    }

    /// <summary>
    /// Normalizes raw audio codec strings to human-friendly labels. Matches
    /// webui <c>mapAudioLabel</c>: TRUEHD → TrueHD, DTSHDMA → DTS-HD MA,
    /// etc. Appends channel count when available ("5.1", "7.1").
    /// </summary>
    private static string? NormalizeAudioCodec(string? codec, int? channels)
    {
        if (string.IsNullOrWhiteSpace(codec)) return null;
        string upper = codec.ToUpperInvariant();
        string label = upper switch
        {
            "TRUEHD" => "TrueHD",
            "DTSHDMA" or "DTS-HD MA" or "DTSHD" => "DTS-HD MA",
            "DTSHDHRA" => "DTS-HD HRA",
            "DTSX" or "DTS:X" => "DTS:X",
            "EAC3" => "E-AC3",
            "AC3" => "AC3",
            "AAC" => "AAC",
            "FLAC" => "FLAC",
            "OPUS" => "Opus",
            "MP3" => "MP3",
            "VORBIS" => "Vorbis",
            _ => codec,
        };
        if (channels.HasValue && channels.Value > 0)
        {
            string chLabel = channels.Value switch
            {
                1 => "Mono",
                2 => "Stereo",
                6 => "5.1",
                8 => "7.1",
                _ => $"{channels.Value}ch",
            };
            label += $" {chLabel}";
        }
        return label;
    }

    /// <summary>
    /// Format a file size in bytes to a human-friendly string like
    /// "45.0 GB" or "720 MB". Uses decimal (1000) not binary (1024) to
    /// match webui <c>formatFileSize</c>.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "";
        double gb = bytes / 1_000_000_000.0;
        if (gb >= 1) return $"{gb:F1} GB";
        double mb = bytes / 1_000_000.0;
        return $"{mb:F0} MB";
    }

    /// <summary>
    /// Extract a release-type hint from a filename — Remux / WEB-DL /
    /// WEBRip / BluRay / BDRip / HDTV / DVDRip. Case-insensitive match.
    /// Mirrors webui <c>extractSourceHint</c>.
    /// </summary>
    private static string? ExtractReleaseHint(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        var upper = fileName.ToUpperInvariant();
        if (upper.Contains("REMUX")) return "Remux";
        if (upper.Contains("WEB-DL") || upper.Contains("WEBDL")) return "WEB-DL";
        if (upper.Contains("WEBRIP")) return "WEBRip";
        if (upper.Contains("BLURAY") || upper.Contains("BLU-RAY") || upper.Contains("BDMUX")) return "BluRay";
        if (upper.Contains("BDRIP")) return "BDRip";
        if (upper.Contains("HDTV")) return "HDTV";
        if (upper.Contains("DVDRIP")) return "DVDRip";
        return null;
    }

    /// <summary>
    /// Rank for resolution-descending sort. Larger numbers sort first.
    /// Unknown values land at 0 (bottom of the list).
    /// </summary>
    private static int ResolutionRank(string resolution)
    {
        if (string.IsNullOrEmpty(resolution)) return 0;
        var r = resolution.ToLowerInvariant();
        return r switch
        {
            "2160p" or "4k" => 2160,
            "1440p" => 1440,
            "1080p" => 1080,
            "720p" => 720,
            "480p" => 480,
            "sd" => 300,
            _ => 0,
        };
    }

    // ===== Similar Items ("More Like This") =====

    private async Task LoadSimilarItemsAsync()
    {
        var requestedContentId = ViewModel.Item?.ContentId;
        await ViewModel.LoadSimilarCommand.ExecuteAsync(null);

        if (!string.Equals(_currentContentId, requestedContentId, StringComparison.Ordinal))
            return;

        SimilarLoadingSkeleton.Visibility = Visibility.Collapsed;
        SimilarLoadError.Visibility = Visibility.Collapsed;
        SimilarPanel.Visibility = ViewModel.SimilarLoadFailed
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (ViewModel.SimilarLoadFailed)
        {
            SimilarSection.Visibility = Visibility.Collapsed;
            return;
        }

        if (ViewModel.SimilarItems.Count == 0)
        {
            SimilarSection.Visibility = Visibility.Collapsed;
            return;
        }

        SimilarSection.Visibility = Visibility.Visible;
        SimilarPanel.Children.Clear();

        foreach (var item in ViewModel.SimilarItems)
        {
            var posterCard = new PosterCard
            {
                MediaItem = item
            };
            SimilarPanel.Children.Add(posterCard);
        }
        UpdateResponsiveLayout(ActualWidth);
    }

    // ===== Hero Crew Line =====

    private static string? AirDateYear(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length >= 4
            ? value[..4]
            : null;

    private static string FormatDetailDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return DateTimeOffset.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed)
            ? DateTimeDisplay.FormatDate(parsed, medium: true)
            : value;
    }

    private void BuildHeroCrewLine(MediaItemDetail item)
    {
        var crew = item.Crew ?? [];
        var directors = crew.Where(c => c.Job.Equals("Director", StringComparison.OrdinalIgnoreCase)).DistinctBy(c => c.Name).Take(2).ToList();
        var writers = crew.Where(c => c.Job.Equals("Writer", StringComparison.OrdinalIgnoreCase) || c.Job.Equals("Screenplay", StringComparison.OrdinalIgnoreCase)).DistinctBy(c => c.Name).Take(2).ToList();
        var authors = crew.Where(c => c.Job.Equals("Author", StringComparison.OrdinalIgnoreCase)).DistinctBy(c => c.Name).Take(3).ToList();
        var genres = item.Type is "movie" or "series" ? item.Genres ?? [] : [];

        HeroCrewLine.Inlines.Clear();
        var hasSegment = false;
        void AddSeparator()
        {
            if (hasSegment)
                HeroCrewLine.Inlines.Add(new Run { Text = "  \u00B7  ", Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
            hasSegment = true;
        }
        void AddPeople(string label, List<CrewMember> people)
        {
            if (people.Count == 0) return;
            AddSeparator();
            HeroCrewLine.Inlines.Add(new Run { Text = label, Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
            for (var i = 0; i < people.Count; i++)
            {
                if (i > 0) HeroCrewLine.Inlines.Add(new Run { Text = ", " });
                var person = people[i];
                if (string.IsNullOrWhiteSpace(person.PersonId))
                {
                    HeroCrewLine.Inlines.Add(new Run { Text = person.Name });
                    continue;
                }
                var personId = person.PersonId;
                var link = new Hyperlink
                {
                    UnderlineStyle = UnderlineStyle.None,
                    Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                };
                link.Inlines.Add(new Run { Text = person.Name });
                link.Click += (_, _) => App.Services.GetRequiredService<NavigationService>()
                    .Navigate<PersonDetailPage>(personId);
                HeroCrewLine.Inlines.Add(link);
            }
        }

        AddPeople("By ", authors);
        AddPeople(item.Type == "series" ? "Created by " : "Directed by ", directors);
        AddPeople("Written by ", writers);
        if (genres.Count > 0)
        {
            AddSeparator();
            for (var i = 0; i < genres.Count; i++)
            {
                if (i > 0)
                    HeroCrewLine.Inlines.Add(new Run { Text = "  \u00B7  ", Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"] });
                HeroCrewLine.Inlines.Add(new Run { Text = genres[i] });
            }
        }

        HeroCrewLine.Visibility = hasSegment ? Visibility.Visible : Visibility.Collapsed;
    }

    // ===== Season Episode Loading =====

    private async Task<bool> ShowSingleSeasonEpisodesAsync(
        Season singleSeason,
        string seriesContentId,
        CancellationToken ct = default)
    {
        SeasonsLoadingSkeleton.Visibility = Visibility.Collapsed;
        SeasonsScrollViewer.Visibility = Visibility.Collapsed;
        SeasonsSection.Visibility = Visibility.Collapsed;
        EpisodesSection.Visibility = Visibility.Visible;
        EpisodesLoadingSkeleton.Visibility = Visibility.Visible;
        EpisodesLoadError.Visibility = Visibility.Collapsed;
        EpisodesEmptyState.Visibility = Visibility.Collapsed;
        EpisodesPanel.Visibility = Visibility.Collapsed;

        // Match the current WebUI's single-season flow: once a series resolves to
        // one season, the visible Episodes row is loaded through the canonical
        // season item endpoint. That preserves specials (season 0) and any server
        // semantics attached to the season content_id instead of relying on the
        // legacy /series/{id}/seasons/{num}/episodes route.
        var loaded = await LoadSeasonEpisodesAsync(
            singleSeason.ContentId,
            singleSeason.SeasonNumber,
            ct);

        if (_currentContentId != seriesContentId || ViewModel.Item?.ContentId != seriesContentId)
            return false;
        if (ct.IsCancellationRequested)
            return false;

        EpisodesLoadingSkeleton.Visibility = Visibility.Collapsed;
        EpisodesLoadError.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
        EpisodesPanel.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        if (loaded)
            BuildEpisodeRows();
        return loaded;
    }

    private async Task<bool> LoadSeasonEpisodesAsync(
        string seasonContentId,
        int seasonNumber,
        CancellationToken ct = default)
    {
        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var response = await catalogApi.GetItemEpisodesAsync(seasonContentId, ct);
            if (response?.Episodes != null && response.Episodes.Count > 0)
            {
                ViewModel.SelectedSeasonNumber = seasonNumber;
                ViewModel.Episodes.Clear();
                foreach (var ep in response.Episodes)
                    ViewModel.Episodes.Add(ep);
            }
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _seasonEpisodesErrorMessage = DetailFailurePolicy.Message(DetailFailurePolicy.Classify(ex));
            System.Diagnostics.Debug.WriteLine($"Season episodes load failed for {seasonContentId}: {ex}");
            return false;
        }
    }

    private void ShowSeasonEpisodesFatalError(MediaItemDetail season)
    {
        ContentScroll.Visibility = Visibility.Collapsed;
        SeasonEpisodesBackButton.Content = $"\u2190 Back to {season.SeriesTitle ?? "Series"}";
        SeasonEpisodesFatalErrorText.Text = _seasonEpisodesErrorMessage;
        SeasonEpisodesFatalError.Visibility = Visibility.Visible;
    }

    private void SeasonEpisodesBackButton_Click(object sender, RoutedEventArgs e)
    {
        var seriesId = ViewModel.Item?.SeriesId;
        var navigation = App.Services.GetRequiredService<NavigationService>();
        if (!string.IsNullOrWhiteSpace(seriesId))
            navigation.Navigate<ItemDetailPage>(seriesId);
        else if (navigation.CanGoBack)
            navigation.GoBack();
        else
            navigation.Navigate<HomePage>();
    }

    // ===== Ambient Glow Color Extraction =====

    /// <summary>
    /// Extracts the dominant saturated color from RGBA pixel data.
    /// Samples center-weighted pixels and picks the most saturated hue.
    /// </summary>
    private static Windows.UI.Color ExtractDominantColor(byte[] rgba, int width, int height)
    {
        long totalR = 0, totalG = 0, totalB = 0;
        int count = 0;

        // Sample a grid of pixels, weighted toward center
        for (int y = height / 4; y < height * 3 / 4; y++)
        {
            for (int x = width / 4; x < width * 3 / 4; x++)
            {
                int idx = (y * width + x) * 4;
                if (idx + 3 >= rgba.Length) continue;
                byte r = rgba[idx], g = rgba[idx + 1], b = rgba[idx + 2], a = rgba[idx + 3];
                if (a < 128) continue;

                // Boost saturated pixels (skip near-gray)
                int max = Math.Max(r, Math.Max(g, b));
                int min = Math.Min(r, Math.Min(g, b));
                int saturation = max - min;
                if (saturation < 20) continue;

                int weight = saturation;
                totalR += r * weight;
                totalG += g * weight;
                totalB += b * weight;
                count += weight;
            }
        }

        if (count == 0)
            return Windows.UI.Color.FromArgb(255, 120, 174, 252); // Default accent blue

        byte avgR = (byte)(totalR / count);
        byte avgG = (byte)(totalG / count);
        byte avgB = (byte)(totalB / count);

        // Boost saturation slightly for more visible glow
        int maxC = Math.Max(avgR, Math.Max(avgG, avgB));
        if (maxC > 0)
        {
            float boost = Math.Min(255f / maxC, 1.4f);
            avgR = (byte)Math.Min(255, avgR * boost);
            avgG = (byte)Math.Min(255, avgG * boost);
            avgB = (byte)Math.Min(255, avgB * boost);
        }

        return Windows.UI.Color.FromArgb(255, avgR, avgG, avgB);
    }

    // ===== Sibling Episodes =====

    private async Task LoadSiblingEpisodesAsync()
    {
        var item = ViewModel.Item;
        if (item?.Type != "episode" || string.IsNullOrEmpty(item.SeriesId)) return;

        int? seasonNum = item.SeasonNumber;
        if (seasonNum == null || seasonNum < 0) return;

        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var response = await catalogApi.GetEpisodesAsync(item.SeriesId, seasonNum.Value);
            if (!ReferenceEquals(ViewModel.Item, item) ||
                !string.Equals(_currentContentId, item.ContentId, StringComparison.Ordinal))
                return;

            if (response?.Episodes == null || response.Episodes.Count <= 1)
            {
                SiblingEpisodesSection.Visibility = Visibility.Collapsed;
                SiblingEpisodesArrowPanel.Visibility = Visibility.Collapsed;
                return;
            }

            SiblingEpisodesSection.Visibility = Visibility.Visible;
            SiblingEpisodesLoadingSkeleton.Visibility = Visibility.Collapsed;
            SiblingEpisodesLoadError.Visibility = Visibility.Collapsed;
            SiblingEpisodesScrollViewer.Visibility = Visibility.Visible;
            SiblingEpisodesTitle.Text = "More Episodes";
            SiblingEpisodesPanel.Children.Clear();

            var currentEpisodeIndex = -1;
            for (var index = 0; index < response.Episodes.Count; index++)
            {
                var ep = response.Episodes[index];
                var isCurrentEpisode = ep.ContentId == item.ContentId;
                if (isCurrentEpisode)
                    currentEpisodeIndex = index;

                // Convert Episode to MediaItem for LandscapeCard
                var mediaItem = new SiloPlayer.Core.Models.Home.MediaItem
                {
                    ContentId = ep.ContentId,
                    Title = ep.Title,
                    Type = "episode",
                    ItemSource = "episode_carousel",
                    BackdropUrl = ep.StillUrl,
                    BackdropThumbhash = ep.StillThumbhash,
                    Overview = ep.Overview ?? "",
                    EpisodeNumber = ep.EpisodeNumber,
                    SeasonNumber = ep.SeasonNumber,
                    Runtime = ep.Runtime,
                    Badges = isCurrentEpisode ? ["now_viewing"] : [],
                    UserState = new UserState { Played = ep.UserData?.Played == true },
                };
                if (ep.UserData != null)
                {
                    mediaItem.PositionSeconds = ep.UserData.PositionSeconds;
                    mediaItem.DurationSeconds = ep.UserData.DurationSeconds;
                }

                var card = new LandscapeCard { MediaItem = mediaItem };
                card.SetCardWidth(240);
                SiblingEpisodesPanel.Children.Add(card);
            }

            if (SiblingEpisodesPanel.Children.Count == 0)
            {
                SiblingEpisodesSection.Visibility = Visibility.Collapsed;
                SiblingEpisodesArrowPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpdateSiblingEpisodeScrollButtons();
                AlignSiblingEpisodeToStart(currentEpisodeIndex);
            }
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(ViewModel.Item, item) ||
                !string.Equals(_currentContentId, item.ContentId, StringComparison.Ordinal))
                return;
            System.Diagnostics.Debug.WriteLine($"Sibling episodes load failed for {item.ContentId}: {ex}");
            SiblingEpisodesSection.Visibility = Visibility.Collapsed;
            SiblingEpisodesLoadingSkeleton.Visibility = Visibility.Collapsed;
            SiblingEpisodesScrollViewer.Visibility = Visibility.Collapsed;
            SiblingEpisodesLoadError.Visibility = Visibility.Collapsed;
            SiblingEpisodesArrowPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void AlignSiblingEpisodeToStart(int episodeIndex)
    {
        if (episodeIndex < 0) return;
        _pendingSiblingEpisodeIndex = episodeIndex;
        DispatcherQueue.TryEnqueue(() =>
        {
            SiblingEpisodesScrollViewer.UpdateLayout();
            TryAlignSiblingEpisodeToStart();
        });
    }

    private void TryAlignSiblingEpisodeToStart()
    {
        if (_pendingSiblingEpisodeIndex < 0 ||
            SiblingEpisodesScrollViewer.ViewportWidth <= 0 ||
            SiblingEpisodesScrollViewer.ExtentWidth <= 0)
            return;

        // The WebUI carousel uses Embla with align:"start", then calls
        // scrollTo(currentEpisodeIndex) on mount. Wait until WinUI has measured
        // the horizontal extent before applying the equivalent snap; clamping
        // against an unmeasured ScrollableWidth silently left the row at item 1.
        const double cardWidth = 240d;
        const double gap = 12d;
        var targetOffset = Math.Clamp(
            _pendingSiblingEpisodeIndex * (cardWidth + gap),
            0,
            Math.Max(0, SiblingEpisodesScrollViewer.ScrollableWidth));
        _pendingSiblingEpisodeIndex = -1;
        SiblingEpisodesScrollViewer.ChangeView(targetOffset, null, null, disableAnimation: true);
        UpdateSiblingEpisodeScrollButtons();
    }

    // ===== Backdrop Image =====

    private async Task LoadBackdropAsync(MediaItemDetail item, CancellationToken ct)
    {
        // Show thumbhash placeholder first
        if (!string.IsNullOrEmpty(item.BackdropThumbhash))
        {
            try
            {
                var decoded = ThumbhashDecoder.Decode(item.BackdropThumbhash);
                var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);

                var bgra = new byte[decoded.Rgba.Length];
                for (int i = 0; i < decoded.Rgba.Length; i += 4)
                {
                    bgra[i] = decoded.Rgba[i + 2];
                    bgra[i + 1] = decoded.Rgba[i + 1];
                    bgra[i + 2] = decoded.Rgba[i];
                    bgra[i + 3] = decoded.Rgba[i + 3];
                }

                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.CopyTo(bgra, bitmap.PixelBuffer);
                bitmap.Invalidate();
                BackdropImage.Source = bitmap;

                // Extract dominant color from thumbhash for ambient glow
                var dominantColor = ExtractDominantColor(decoded.Rgba, decoded.Width, decoded.Height);
                AmbientGlowColor.Color = dominantColor;
            }
            catch
            {
                BackdropImage.ClearValue(Image.SourceProperty);
            }
        }

        if (string.IsNullOrEmpty(item.BackdropUrl)) return;

        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "backdrop", item.BackdropUrl, httpClient, ct);

            if (ct.IsCancellationRequested || bytes == null) return;

            var bitmapImage = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            if (ct.IsCancellationRequested) return;

            BackdropImage.Source = bitmapImage;
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    /// <summary>
    /// Load the 170×255 hero portrait poster via ImageService so it hits the
    /// disk cache (same cache poster cards use). Fires fire-and-forget from
    /// UpdateUI. Failures are silent — poster just stays blank.
    /// </summary>
    private async Task LoadHeroPosterAsync(string posterUrl, CancellationToken ct)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var item = ViewModel.Item;
            if (item == null) return;

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "poster", posterUrl, httpClient, ct);
            if (ct.IsCancellationRequested || bytes == null) return;

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 340, // 2x for crisp on HiDPI
                DecodePixelType = DecodePixelType.Logical,
            };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (ct.IsCancellationRequested || ViewModel.Item?.ContentId != item.ContentId) return;
            HeroPosterImage.Source = bitmap;
            HeroPosterFallback.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch { /* best-effort */ }
    }

    private long _titleArtRevision;
    private void OnTitleArtPreferenceChanged(object? sender, EventArgs e)
    {
        var client = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
        var authority = client.CaptureContext();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || !client.IsCurrentContext(authority) || ViewModel.Item is not { } item || _imageCts == null) return;
            _ = LoadTitleArtPreferenceAsync(item, _imageCts.Token);
        });
    }
    private async Task LoadTitleArtPreferenceAsync(MediaItemDetail item, CancellationToken ct)
    {
        var revision = ++_titleArtRevision;
        if (string.IsNullOrWhiteSpace(item.LogoUrl)) return;
        var client = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
        var authority = client.CaptureContext();
        var show = true;
        try
        {
            var result = await App.Services.GetRequiredService<SiloPlayer.Core.Api.SettingsApi>().GetEffectiveSettingsAsync(["ui.title_art"], ct);
            var value = result.Settings.FirstOrDefault(setting => setting.Key == "ui.title_art")?.EffectiveValue;
            if (bool.TryParse(value, out var choice)) show = choice;
        }
        catch (OperationCanceledException) { return; }
        catch { /* Missing or failed reads resolve to the published contract default. */ }
        if (ct.IsCancellationRequested || revision != _titleArtRevision || !client.IsCurrentContext(authority) || !ReferenceEquals(ViewModel.Item, item)) return;
        HeroLogoImage.Source = null; HeroLogoImage.Visibility = Visibility.Collapsed;
        TitleArtPending.Visibility = Visibility.Collapsed;
        TitleText.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show) await LoadHeroLogoAsync(item.ContentId, item.LogoUrl, ct, revision);
    }

    private async Task LoadHeroLogoAsync(string contentId, string logoUrl, CancellationToken ct, long revision)
    {
        var client = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>();
        var authority = client.CaptureContext();
        try
        {
            var bytes = await App.Services.GetRequiredService<ImageService>().GetImageAsync(
                contentId,
                "logo",
                logoUrl,
                App.Services.GetRequiredService<HttpClient>(),
                ct);
            if (ct.IsCancellationRequested || revision != _titleArtRevision || !client.IsCurrentContext(authority)) return;
            if (bytes == null) { TitleText.Visibility = Visibility.Visible; return; }

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 960,
                DecodePixelType = DecodePixelType.Logical,
            };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (ct.IsCancellationRequested || revision != _titleArtRevision || !client.IsCurrentContext(authority) || ViewModel.Item?.ContentId != contentId) return;

            HeroLogoImage.Source = bitmap;
            HeroLogoImage.Visibility = Visibility.Visible;
            TitleText.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!ct.IsCancellationRequested && revision == _titleArtRevision && client.IsCurrentContext(authority) && ViewModel.Item?.ContentId == contentId)
                TitleText.Visibility = Visibility.Visible;
        }
    }

    // ===== Cast Section =====

    private void BuildCast(List<CastMember> cast)
    {
        CastPanel.Children.Clear();

        if (cast.Count == 0)
        {
            CastSection.Visibility = Visibility.Collapsed;
            CastHeader.Visibility = Visibility.Collapsed;
            CastScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        CastSection.Visibility = Visibility.Visible;
        CastHeader.Visibility = Visibility.Visible;
        CastScrollViewer.Visibility = Visibility.Visible;

        // Sort by the server-provided order field (main cast first) then
        // take top 20. Previously unsorted with .Take(20) which could miss
        // prominent actors who appeared later in the list.
        foreach (var member in cast.OrderBy(c => c.Order).Take(20))
        {
            // B34: Portrait cards 110x165 (aspect 2:3) instead of 64x64 circles —
            // matches WebUI CastCarousel aspect-[2/3] frames.
            var card = new StackPanel
            {
                Width = 110,
                Spacing = 0
            };

            // Photo placeholder (portrait, rounded corners — not a circle).
            // Show initials when no photo URL instead of a generic Contact glyph.
            var photoBorder = new Border
            {
                Width = 110,
                Height = 165,
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            if (!string.IsNullOrEmpty(member.PhotoUrl))
            {
                _ = LoadCastPhotoAsync(photoBorder, member);
            }
            else
            {
                // Initials fallback: first letter(s) of each name part, max 2.
                string initials = string.Join("", member.Name
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Take(2)
                    .Select(p => char.ToUpperInvariant(p[0])));
                if (string.IsNullOrEmpty(initials)) initials = "?";

                photoBorder.Child = new TextBlock
                {
                    Text = initials,
                    FontSize = 28,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            card.Children.Add(photoBorder);

            card.Children.Add(new TextBlock
            {
                Text = member.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
                TextAlignment = TextAlignment.Left,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 2, 0)
            });

            if (!string.IsNullOrEmpty(member.Character))
            {
                // Distinct secondary styling for character name (webui uses
                // separate muted text-xs; previous code reused CaptionTextStyle
                // for both which made them look identical).
                card.Children.Add(new TextBlock
                {
                    Text = member.Character,
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
                    TextAlignment = TextAlignment.Left,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(2, 0, 2, 0)
                });
            }

            // Make cast card clickable if PersonId is available
            if (!string.IsNullOrEmpty(member.PersonId))
            {
                var personId = member.PersonId;
                var button = new Button
                {
                    Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Content = card,
                    CornerRadius = new CornerRadius(8),
                };
                AutomationProperties.SetName(button,
                    string.IsNullOrWhiteSpace(member.Character)
                        ? member.Name
                        : $"{member.Name}, {member.Character}");
                button.Click += (_, _) =>
                {
                    App.Services.GetRequiredService<NavigationService>()
                        .Navigate<PersonDetailPage>(personId);
                };
                CastPanel.Children.Add(button);
            }
            else
            {
                CastPanel.Children.Add(card);
            }
        }

        CastScrollViewer.ChangeView(0, null, null, disableAnimation: true);
        DispatcherQueue.TryEnqueue(() =>
            UpdateDetailCarouselButtons(CastScrollViewer, CastPrevButton, CastNextButton));
    }

    private async Task LoadCastPhotoAsync(Border photoBorder, CastMember member)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                member.Name, "cast_photo", member.PhotoUrl!, httpClient, CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            var image = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Clip to circle
            photoBorder.Child = image;
        }
        catch { }
    }

    // ===== Crew Section =====

    private void BuildCrew(List<CrewMember> crew)
    {
        DirectorNamesPanel.Children.Clear();
        WriterNamesPanel.Children.Clear();
        ProducerNamesPanel.Children.Clear();

        if (crew.Count == 0)
        {
            CrewSection.Visibility = Visibility.Collapsed;
            return;
        }

        // Match WebUI: group by exact job value, deduplicate by name within
        // each group (the server can return the same person twice if they
        // have multiple credits). Webui parity: Directors + Writers + Producers.
        var directors = DeduplicateByName(crew.Where(c =>
            c.Job.Equals("Director", StringComparison.OrdinalIgnoreCase)));
        var writers = DeduplicateByName(crew.Where(c =>
            c.Job.Equals("Writer", StringComparison.OrdinalIgnoreCase)));
        var producers = DeduplicateByName(crew.Where(c =>
            c.Job.Equals("Producer", StringComparison.OrdinalIgnoreCase)));

        bool hasAny = directors.Count > 0 || writers.Count > 0 || producers.Count > 0;
        if (!hasAny)
        {
            CrewSection.Visibility = Visibility.Collapsed;
            return;
        }

        CrewSection.Visibility = Visibility.Visible;

        ToggleCrewGroup(DirectorsPanel, DirectorNamesPanel, directors);
        ToggleCrewGroup(WritersPanel, WriterNamesPanel, writers);
        ToggleCrewGroup(ProducersPanel, ProducerNamesPanel, producers);
    }

    private static List<CrewMember> DeduplicateByName(IEnumerable<CrewMember> source) =>
        source.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
              .Select(g => g.First())
              .ToList();

    private void ToggleCrewGroup(FrameworkElement groupPanel, StackPanel namesPanel, List<CrewMember> members)
    {
        if (members.Count > 0)
        {
            groupPanel.Visibility = Visibility.Visible;
            BuildCrewNameLinks(namesPanel, members);
        }
        else
        {
            groupPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildCrewNameLinks(StackPanel panel, List<CrewMember> members)
    {
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];

            var link = new HyperlinkButton
            {
                Content = member.Name,
                Padding = new Thickness(0),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
                FontSize = 13
            };

            // B33: Pass string person IDs through; supports non-numeric IDs.
            if (!string.IsNullOrEmpty(member.PersonId))
            {
                var id = member.PersonId;
                link.Click += (_, _) =>
                {
                    var nav = App.Services.GetRequiredService<NavigationService>();
                    nav.Navigate<PersonDetailPage>(id);
                };
            }
            else
            {
                link.IsEnabled = false;
            }

            panel.Children.Add(link);

            if (i < members.Count - 1)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = ", ",
                    FontSize = 13,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
        }
    }

    // ===== Series: Season Cards =====

    private void BuildSeasonCards()
    {
        SeasonsPanel.Children.Clear();
        _seasonCardWidth = SeasonWidth(ActualWidth);

        if (ViewModel.Seasons.Count == 0)
        {
            UpdateSeasonScrollButtons();
            return;
        }
        SeasonsTotalText.Text = $"{ViewModel.Seasons.Count} total";

        foreach (var season in ViewModel.Seasons.OrderBy(season => season.SeasonNumber))
        {
            var card = CreateSeasonCard(season);
            SeasonsPanel.Children.Add(card);
        }

        DispatcherQueue.TryEnqueue(UpdateSeasonScrollButtons);
    }

    private Button CreateSeasonCard(Season season)
    {
        var cardWidth = _seasonCardWidth > 0
            ? _seasonCardWidth
            : SeasonWidth(ActualWidth);
        var title = !string.IsNullOrWhiteSpace(season.Title)
            ? season.Title
            : season.SeasonNumber == 0 ? "Specials" : $"Season {season.SeasonNumber}";

        var posterBorder = new Border
        {
            Width = cardWidth,
            Height = cardWidth * 1.5,
            CornerRadius = new CornerRadius(16),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"]
        };

        var posterPlaceholder = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(16),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        posterBorder.Child = posterPlaceholder;

        // Load poster if available
        if (!string.IsNullOrEmpty(season.PosterUrl))
        {
            _ = LoadSeasonPosterAsync(posterBorder, season);
        }

        // Overlay container hosts poster + checkmark badge + progress bar.
        var posterHost = new Grid { Width = cardWidth, Height = cardWidth * 1.5 };
        posterHost.Children.Add(posterBorder);

        // Completed checkmark (top-right green badge) — webui parity: the
        // circular bg-green-500/90 check overlay on fully-watched seasons.
        bool isCompleted = season.UserData?.Played == true;
        bool hasProgress = !isCompleted && season.UserData != null &&
            (season.UserData.WatchedCount > 0 || season.UserData.InProgressCount > 0);
        if (isCompleted)
        {
            posterHost.Children.Add(new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xE6, 0x22, 0xC5, 0x5E)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Child = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 14,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                },
            });
        }

        // Season progress bar (3px at bottom): green when completed, accent
        // otherwise. Fills proportional to watched/total episodes.
        if ((isCompleted || hasProgress) && season.UserData != null && season.EpisodeCount > 0)
        {
            int watched = isCompleted ? season.EpisodeCount : (season.UserData.WatchedCount);
            double ratio = Math.Clamp((double)watched / season.EpisodeCount, 0, 1);
            var progressLayout = MediaCardProgressGeometry.Calculate(cardWidth, ratio, episodeCard: false);
            var barTrack = new Grid
            {
                Width = progressLayout.TrackWidth,
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, progressLayout.BottomInset),
            };
            barTrack.Children.Add(new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x66, 0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(2),
            });
            var fill = new Border
            {
                Width = progressLayout.FillWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = isCompleted
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50))
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(2),
            };
            barTrack.Children.Add(fill);
            posterHost.Children.Add(barTrack);
        }

        // Title
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 10, 2, 0)
        };

        // Episode count / progress
        var progressText = BuildSeasonProgressText(season);

        var content = new StackPanel
        {
            Width = cardWidth,
            Children = { posterHost, titleText, progressText }
        };

        var cardButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = content,
            Tag = season.ContentId,
            CornerRadius = new CornerRadius(12),
        };
        AutomationProperties.SetName(cardButton, $"{title}, {FormatSeasonProgressText(season)}");

        cardButton.PointerEntered += (_, _) =>
        {
            App.Services.GetRequiredService<ItemDetailPrefetchCache>()
                .Prefetch(season.ContentId);
            posterBorder.Opacity = 0.92;
        };
        cardButton.PointerExited += (_, _) =>
        {
            posterBorder.Opacity = 1;
        };
        cardButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(season.ContentId)) return;
            App.Services.GetRequiredService<ItemDetailPrefetchCache>()
                .Prefetch(season.ContentId);
            App.Services.GetRequiredService<NavigationService>()
                .Navigate<ItemDetailPage>(season.ContentId);
        };
        cardButton.GotFocus += (_, _) => App.Services
            .GetRequiredService<ItemDetailPrefetchCache>()
            .Prefetch(season.ContentId);

        return cardButton;
    }

    private TextBlock BuildSeasonProgressText(Season season)
    {
        return new TextBlock
        {
            Text = FormatSeasonProgressText(season),
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 2, 2, 0)
        };
    }

    private static string FormatSeasonProgressText(Season season)
    {
        var userData = season.UserData;
        var hasProgress = userData is { Played: false } &&
            (userData.WatchedCount > 0 || userData.InProgressCount > 0);
        return hasProgress
            ? $"{userData!.WatchedCount} of {season.EpisodeCount} episodes"
            : $"{season.EpisodeCount} episodes";
    }

    private async Task LoadSeasonPosterAsync(Border posterBorder, Season season)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                season.ContentId, "poster", season.PosterUrl!, httpClient, CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = 180,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            var image = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            posterBorder.Child = image;
        }
        catch { }
    }

    // ===== Series: Episode Grid =====

    // Target per-card min width. Used to derive column count from container width
    // so cards reflow responsively (mirrors upstream's grid-cols-1 → grid-cols-5).
    private const int EpisodeStillDecodeWidth = 640;

    private void BuildEpisodeRows()
    {
        EpisodesPanel.Children.Clear();
        EpisodesPanel.RowDefinitions.Clear();
        EpisodesPanel.ColumnDefinitions.Clear();

        if (ViewModel.Episodes.Count == 0)
        {
            if (ViewModel.Item?.Type == "season")
            {
                EpisodesSection.Visibility = Visibility.Visible;
                EpisodesHeader.Text = "Episodes";
                EpisodesTotalText.Text = "0 total";
                EpisodesEmptyState.Visibility = Visibility.Visible;
            }
            else
            {
                EpisodesSection.Visibility = Visibility.Collapsed;
                EpisodesEmptyState.Visibility = Visibility.Collapsed;
            }
            return;
        }

        EpisodesSection.Visibility = Visibility.Visible;
        EpisodesEmptyState.Visibility = Visibility.Collapsed;
        if (ViewModel.Item?.Type == "season" || ViewModel.Item?.Type == "series" && ViewModel.Seasons.Count == 1)
        {
            EpisodesHeader.Text = "Episodes";
            EpisodesTotalText.Text = $"{ViewModel.Item.EpisodeCount ?? ViewModel.Episodes.Count} total";
        }
        else
        {
            EpisodesHeader.Text = ViewModel.SelectedSeasonNumber == 0
                ? "Specials"
                : $"Season {ViewModel.SelectedSeasonNumber} Episodes";
            EpisodesTotalText.Text = $"{ViewModel.Episodes.Count} total";
        }

        LayoutEpisodeGrid();
    }

    private void LayoutEpisodeGrid()
    {
        EpisodesPanel.Children.Clear();
        EpisodesPanel.RowDefinitions.Clear();
        EpisodesPanel.ColumnDefinitions.Clear();

        if (ViewModel.Episodes.Count == 0) return;

        // TV navigation overrides the ordinary episode grid on narrow and
        // short landscape viewports.
        var viewportWidth = XamlRoot?.Content is FrameworkElement root && root.ActualWidth > 0
            ? root.ActualWidth
            : ActualWidth > 0 ? ActualWidth : EpisodesPanel.ActualWidth;
        var horizontal = _tvViewport != null && viewportWidth < 1024;
        var shortLandscape = _tvViewport != null && ActualHeight <= 650 && viewportWidth > ActualHeight;
        int cols = horizontal ? ViewModel.Episodes.Count : shortLandscape ? 2 : GetEpisodeGridColumnCount(viewportWidth);
        EpisodesScroll.HorizontalScrollMode = horizontal ? ScrollMode.Enabled : ScrollMode.Disabled;
        EpisodesScroll.HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        // Column definitions (equal-width fractions).
        for (int c = 0; c < cols; c++)
        {
            EpisodesPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = horizontal ? new GridLength(c == cols - 1 ? 160 : 172) : new GridLength(1, GridUnitType.Star) });
        }

        // Row definitions — one per rowful of cards.
        int rows = (int)Math.Ceiling(ViewModel.Episodes.Count / (double)cols);
        for (int r = 0; r < rows; r++)
        {
            EpisodesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        // Populate.
        int i = 0;
        foreach (var episode in ViewModel.Episodes)
        {
            var card = CreateEpisodeCard(episode);
            int row = i / cols;
            int col = i % cols;
            Grid.SetRow(card, row);
            Grid.SetColumn(card, col);
            card.Width = horizontal ? 160 : double.NaN;
            card.HorizontalAlignment = HorizontalAlignment.Left;
            if (!horizontal) card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.Margin = horizontal ? new Thickness(0, 0, col == cols - 1 ? 0 : 12, 8) : new Thickness(col == 0 ? 0 : 8, row == 0 ? 0 : 8, col == cols - 1 ? 0 : 8, 8);
            EpisodesPanel.Children.Add(card);
            i++;
        }
    }

    private void EpisodesPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Reflow when available width crosses a column boundary. Only re-layout
        // if column count would change; skip minor resize noise.
        if (ViewModel.Episodes.Count == 0) return;
        var width = XamlRoot?.Content is FrameworkElement root && root.ActualWidth > 0
            ? root.ActualWidth
            : ActualWidth > 0 ? ActualWidth : e.NewSize.Width;
        int newCols = _tvViewport != null && width < 1024 ? ViewModel.Episodes.Count
            : _tvViewport != null && ActualHeight <= 650 && width > ActualHeight ? 2 : GetEpisodeGridColumnCount(width);
        int currentCols = EpisodesPanel.ColumnDefinitions.Count;
        if (newCols != currentCols) LayoutEpisodeGrid();
    }

    private static int GetEpisodeGridColumnCount(double viewportWidth) => viewportWidth >= 1024 ? 5
        : viewportWidth >= 768 ? 4
        : viewportWidth >= 640 ? 3
        : viewportWidth >= 460 ? 2
        : 1;

    /// <summary>
    /// Build a compact vertical episode card for the grid layout: 16:9 still on
    /// top (with progress bar overlay for in-progress episodes), ep-number + title
    /// line below, overview (2 lines), then quality badges + watched checkmark.
    /// </summary>
    private FrameworkElement CreateEpisodeCard(Episode episode)
    {
        var presentation = EpisodeCardPresentation.Create(
            episode.UserData?.Played == true,
            episode.UserData?.PositionSeconds ?? 0,
            episode.UserData?.DurationSeconds ?? 0);
        bool isInProgress = presentation.ShowProgress;
        bool isWatched = presentation.ShowWatchedIndicator;

        // ── Still image (16:9, fills card width) ──────────────────────────
        var stillBorder = new Border
        {
            CornerRadius = new CornerRadius(17),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
        };
        var stillPlaceholder = new FontIcon
        {
            Glyph = "\uE714",
            FontSize = 28,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stillBorder.Child = stillPlaceholder;
        if (!string.IsNullOrEmpty(episode.StillUrl))
            _ = LoadEpisodeStillAsync(stillBorder, episode);

        // Wrap still in a Viewbox that enforces 16:9 aspect via a Grid.
        var stillWrapper = new Grid();
        stillWrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stillWrapper.SizeChanged += (s, _) =>
        {
            if (s is Grid g) g.Height = g.ActualWidth * 9.0 / 16.0;
        };
        stillWrapper.Children.Add(stillBorder);
        AddEpisodeCardOverlays(stillWrapper, episode);

        // Progress bar overlay for in-progress episodes.
        Grid? progressTrack = null;
        if (isInProgress && episode.UserData!.DurationSeconds > 0)
        {
            var progressFraction = presentation.ProgressRatio;
            progressTrack = new Grid
            {
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(8, 0, 8, 6),
            };
            progressTrack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0, 0, 0)),
                CornerRadius = new CornerRadius(2),
            });
            var fill = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            progressTrack.Children.Add(fill);
            progressTrack.SizeChanged += (_, args) =>
                fill.Width = args.NewSize.Width * Math.Clamp(progressFraction, 0, 1);
            stillWrapper.Children.Add(progressTrack);
        }

        // ── Title line: "42. Title · 42 min" ───────────────────────────────
        var episodeNumberText = new TextBlock
        {
            Text = $"Episode {episode.EpisodeNumber}",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Margin = new Thickness(0, 8, 0, 0),
        };
        var episodeNumberRow = new Grid { ColumnSpacing = 8 };
        episodeNumberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        episodeNumberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        episodeNumberRow.Children.Add(episodeNumberText);
        var watchedIndicator = new Grid
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isWatched ? Visibility.Visible : Visibility.Collapsed,
        };
        AutomationProperties.SetName(watchedIndicator, "Watched");
        watchedIndicator.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Stroke = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            StrokeThickness = 1.5,
        });
        watchedIndicator.Children.Add(new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 8,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(watchedIndicator, 1);
        episodeNumberRow.Children.Add(watchedIndicator);
        var titleText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(episode.Title)
                ? $"Episode {episode.EpisodeNumber}"
                : episode.Title,
            Style = (Style)Application.Current.Resources["SubtitleTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };

        var metaParts = new List<string>();
        if (episode.Runtime > 0) metaParts.Add($"{episode.Runtime}m");
        var episodeAirDate = FormatDetailDate(episode.AirDate);
        if (!string.IsNullOrWhiteSpace(episodeAirDate))
            metaParts.Add(episodeAirDate);
        var episodeMetaText = new TextBlock
        {
            Text = string.Join("  ", metaParts),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            Visibility = metaParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };

        // ── Overview (2 lines) ────────────────────────────────────────────
        var overviewText = new TextBlock
        {
            Text = episode.Overview ?? "",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 16,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
        };
        if (_tvViewport != null && ActualWidth < 1024)
        {
            episodeMetaText.Visibility = Visibility.Collapsed;
            overviewText.Visibility = Visibility.Collapsed;
        }

        // ── Badges row (resolution, HDR) + watched checkmark ──────────────
        // ── Assemble card ─────────────────────────────────────────────────
        var content = new StackPanel
        {
            Spacing = 0,
            Children = { stillWrapper, episodeNumberRow, titleText, episodeMetaText, overviewText },
        };

        var defaultBg = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var mediaItem = new MediaItem
        {
            ContentId = episode.ContentId,
            Type = "episode",
            Title = episode.Title,
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            BackdropUrl = episode.StillUrl,
            UserState = new UserState { Played = isWatched },
            PositionSeconds = episode.UserData?.PositionSeconds,
            DurationSeconds = episode.UserData?.DurationSeconds,
        };

        var cardBorder = new Border
        {
            Padding = new Thickness(0),
            Child = content,
            Tag = episode.ContentId,
            Background = defaultBg,
            BorderThickness = new Thickness(0),
        };
        cardBorder.PointerEntered += (s, _) =>
        {
            App.Services.GetRequiredService<ItemDetailPrefetchCache>()
                .Prefetch(episode.ContentId);
            if (s is Border b)
                b.Opacity = 0.9;
        };
        cardBorder.PointerExited += (s, _) =>
        {
            if (s is Border b) b.Opacity = 1;
        };
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = cardBorder,
            Tag = episode.ContentId,
        };
        AutomationProperties.SetName(button,
            $"Episode {episode.EpisodeNumber}, {episode.Title}, {episode.Runtime} minutes");
        button.Click += (_, _) =>
        {
            App.Services.GetRequiredService<ItemDetailPrefetchCache>()
                .Prefetch(episode.ContentId);
            App.Services.GetRequiredService<NavigationService>()
                .Navigate<ItemDetailPage>(episode.ContentId);
        };
        button.GotFocus += (_, _) => App.Services
            .GetRequiredService<ItemDetailPrefetchCache>()
            .Prefetch(episode.ContentId);
        void RefreshActionState()
        {
            var current = EpisodeCardPresentation.Create(
                mediaItem.UserState?.Played == true,
                mediaItem.PositionSeconds ?? 0,
                mediaItem.DurationSeconds ?? 0);
            watchedIndicator.Visibility = current.ShowWatchedIndicator
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (progressTrack != null)
                progressTrack.Visibility = current.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        }

        button.ContextFlyout = MediaItemMenu.Build(
            mediaItem,
            MediaItemMenu.Surface.Default,
            showCollectionActions: false,
            stateChanged: RefreshActionState,
            owner: button);

        var actionLayer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        void SetActionLayerVisibility(bool reveal)
        {
            actionLayer.Opacity = reveal ? 1 : 0;
            actionLayer.IsHitTestVisible = reveal;
        }
        var quickWatchedIcon = new FontIcon { FontSize = 18, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        var quickWatchedButton = new Button
        {
            Width = 36,
            Height = 36,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xAA, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Content = quickWatchedIcon,
        };
        var moreButton = new Button
        {
            Width = 36,
            Height = 36,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xAA, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE712", FontSize = 14, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
        };
        AutomationProperties.SetName(moreButton, "More actions");
        ToolTipService.SetToolTip(moreButton, "More actions");

        void UpdateQuickAction()
        {
            var watched = mediaItem.UserState?.Played == true;
            var current = EpisodeCardPresentation.Create(
                watched,
                mediaItem.PositionSeconds ?? 0,
                mediaItem.DurationSeconds ?? 0);
            quickWatchedIcon.Glyph = watched ? "\uE7B3" : "\uED1A";
            quickWatchedIcon.Foreground = watched
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80))
                : new SolidColorBrush(Microsoft.UI.Colors.White);
            var label = current.WatchedActionLabel;
            AutomationProperties.SetName(quickWatchedButton, label);
            ToolTipService.SetToolTip(quickWatchedButton, label);
            RefreshActionState();
        }

        quickWatchedButton.Click += async (_, _) =>
        {
            if (!quickWatchedButton.IsEnabled) return;
            quickWatchedButton.IsEnabled = false;
            try
            {
                var operation = MediaItemCardActions.ToggleWatchedAsync(mediaItem);
                UpdateQuickAction();
                await operation;
                UpdateQuickAction();
                button.ContextFlyout = MediaItemMenu.Build(
                    mediaItem,
                    MediaItemMenu.Surface.Default,
                    showCollectionActions: false,
                    stateChanged: UpdateQuickAction,
                    owner: button);
            }
            catch (Exception ex)
            {
                App.Services.GetRequiredService<Services.ToastService>().Error(ex.Message);
            }
            finally
            {
                quickWatchedButton.IsEnabled = true;
            }
        };
        moreButton.Click += (_, _) => MediaItemMenu.Build(
            mediaItem,
            MediaItemMenu.Surface.Default,
            showCollectionActions: false,
            stateChanged: UpdateQuickAction,
            owner: moreButton).ShowAt(moreButton);
        actionLayer.Children.Add(quickWatchedButton);
        actionLayer.Children.Add(moreButton);

        var root = new Grid();
        root.Children.Add(button);
        root.Children.Add(actionLayer);
        root.SizeChanged += (_, args) => actionLayer.Height = args.NewSize.Width * 9d / 16d;
        root.PointerEntered += (_, _) => SetActionLayerVisibility(true);
        root.PointerExited += (_, _) => SetActionLayerVisibility(false);
        root.GotFocus += (_, _) => SetActionLayerVisibility(true);
        root.LostFocus += (_, _) => SetActionLayerVisibility(false);
        UpdateQuickAction();

        return root;
    }

    private void AddEpisodeCardOverlays(Grid host, Episode episode)
    {
        var topLeft = MakeEpisodeOverlayHost(HorizontalAlignment.Left, VerticalAlignment.Top);
        var topRight = MakeEpisodeOverlayHost(HorizontalAlignment.Right, VerticalAlignment.Top);
        var bottomLeft = MakeEpisodeOverlayHost(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        var bottomRight = MakeEpisodeOverlayHost(HorizontalAlignment.Right, VerticalAlignment.Bottom);
        topLeft.Margin = new Thickness(8);
        topRight.Margin = new Thickness(8);
        bottomLeft.Margin = new Thickness(8, 8, 8, 24);
        bottomRight.Margin = new Thickness(8, 8, 8, 56);
        host.Children.Add(topLeft);
        host.Children.Add(topRight);
        host.Children.Add(bottomLeft);
        host.Children.Add(bottomRight);

        void Render()
        {
            topLeft.Children.Clear();
            topRight.Children.Clear();
            bottomLeft.Children.Clear();
            bottomRight.Children.Clear();

            var service = App.Services.GetRequiredService<global::SiloPlayer.Services.CardOverlayService>();
            var prefs = service.GetPrefs();
            if (prefs == null) return;
            var summary = episode.OverlaySummary;
            var data = new global::SiloPlayer.Services.OverlayData
            {
                Resolution = summary?.Resolution,
                Hdr = summary?.Hdr,
                Audio = summary?.Audio,
                AudioChannels = summary?.AudioChannels,
                VideoCodec = summary?.VideoCodec,
                Container = summary?.Container,
                AspectRatio = summary?.AspectRatio,
                ReleaseType = summary?.ReleaseType,
                Edition = summary?.Edition,
                MultiAudio = summary?.MultiAudio == true,
                MultiSub = summary?.MultiSub == true,
                Runtime = episode.Runtime > 0 ? episode.Runtime : null,
            };
            var cornerCounts = new Dictionary<global::SiloPlayer.Services.OverlayPosition, int>();
            foreach (var definition in service.GetOrderedDefinitions())
            {
                if (!prefs.TryGetValue(definition.Id, out var config) || !config.Enabled) continue;
                if (global::SiloPlayer.Services.OverlayRegistry.SuppressesStandaloneOverlays(definition.Id, prefs)) continue;
                var value = definition.GetValue(data);
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (cornerCounts.GetValueOrDefault(config.Position) >= 3) continue;
                var badge = PosterCard.BuildBadge(value, definition.Id, config, service.Preset);
                var corner = config.Position switch
                {
                    global::SiloPlayer.Services.OverlayPosition.TopLeft => topLeft,
                    global::SiloPlayer.Services.OverlayPosition.TopRight => topRight,
                    global::SiloPlayer.Services.OverlayPosition.BottomLeft => bottomLeft,
                    global::SiloPlayer.Services.OverlayPosition.BottomRight => bottomRight,
                    _ => topLeft,
                };
                corner.Children.Add(badge);
                cornerCounts[config.Position] = cornerCounts.GetValueOrDefault(config.Position) + 1;
            }
        }

        Render();
        if (!App.Services.GetRequiredService<global::SiloPlayer.Services.CardOverlayService>().IsLoaded)
            _ = EnsureEpisodeCardOverlaysLoadedAsync(Render);
    }

    private async Task EnsureEpisodeCardOverlaysLoadedAsync(Action render)
    {
        try
        {
            await App.Services.GetRequiredService<global::SiloPlayer.Services.CardOverlayService>().EnsureLoadedAsync();
            DispatcherQueue.TryEnqueue(() => render());
        }
        catch { }
    }

    private async Task LoadSeriesPrimaryEpisodesAsync(string seasonContentId)
    {
        try
        {
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var response = await catalogApi.GetItemEpisodesAsync(seasonContentId);
            ViewModel.Episodes.Clear();
            foreach (var episode in response.Episodes)
                ViewModel.Episodes.Add(episode);
            if (response.Episodes.Count > 0)
                ViewModel.SelectedSeasonNumber = response.Episodes[0].SeasonNumber;
        }
        catch
        {
            ViewModel.Episodes.Clear();
        }
    }

    private void UpdateSeriesCountsFromLoadedSeasons()
    {
        if (ViewModel.Item?.Type != "series" || ViewModel.SeasonsLoadFailed)
            return;

        var seasonCount = ViewModel.Seasons.Count;
        var episodeCount = ViewModel.Seasons.Sum(season => Math.Max(0, season.EpisodeCount));
        SeasonCountText.Text = $"{seasonCount} {(seasonCount == 1 ? "SEASON" : "SEASONS")}";
        SeasonCountBadge.Visibility = seasonCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        EpisodeCountText.Text = $"{episodeCount} {(episodeCount == 1 ? "EPISODE" : "EPISODES")}";
        EpisodeCountBadge.Visibility = episodeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static StackPanel MakeEpisodeOverlayHost(
        HorizontalAlignment horizontal,
        VerticalAlignment vertical) => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 3,
        Margin = new Thickness(6),
        HorizontalAlignment = horizontal,
        VerticalAlignment = vertical,
    };

    private async Task LoadEpisodeStillAsync(Border stillBorder, Episode episode)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                episode.ContentId, "still", episode.StillUrl!, httpClient, CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = EpisodeStillDecodeWidth,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            var image = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Replace the placeholder. The stillBorder may be nested inside a
            // progress-overlay Grid, but it is still the same object reference
            // so setting its Child updates the visual tree correctly.
            stillBorder.Child = image;
            // Clear the placeholder background so the image shows through
            stillBorder.Background = null;
        }
        catch { }
    }
}
