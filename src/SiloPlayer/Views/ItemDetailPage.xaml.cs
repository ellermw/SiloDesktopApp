using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage : Page
{
    public ItemDetailViewModel ViewModel { get; }
    private CancellationTokenSource? _imageCts;
    private CancellationTokenSource? _translationCts;
    private bool _isTranslatingOverview;
    private bool _translateButtonMode;
    private int _highlightedSeasonNumber;
    private WatchDetailResponse? _watchDetail;
    private FileVersion? _selectedVersion;
    private string? _readerTargetContentId;
    private int? _readerTargetFileId;
    private FrameworkElement? _mangaResumeRow;
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
    private Services.PlayerService? _playerService;
    private FrameworkElement? _rootElement;

    public ItemDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<ItemDetailViewModel>();
        this.InitializeComponent();
        ComposeHeroLayout();

        // Listen for async property changes (e.g., rating loaded after initial UI update)
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        this.Loaded += OnPageLoaded;
    }

    private void UpdateBackdropHeight()
    {
        // Current WebUI uses a compact 35vh hero for season pages and the
        // standard 60dvh hero for full item detail pages. MinHeight (rather
        // than a fixed Height) lets wrapped actions and long metadata expand
        // naturally on smaller windows.
        if (XamlRoot?.Content is FrameworkElement root && root.ActualHeight > 0)
        {
            var factor = ViewModel.Item?.Type == "season" ? 0.35 : 0.60;
            BackdropContainer.Height = double.NaN;
            BackdropContainer.MinHeight = Math.Max(300, root.ActualHeight * factor);
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

        MoveIntoHero(HeroMetadataRow);
        MoveIntoHero(ScoresPanel);
        MoveIntoHero(OverviewText);
        MoveIntoHero(TranslateOverviewButton);
        MoveIntoHero(HeroCrewLine);
        MoveIntoHero(GenresBadgesPanel);
        MoveIntoHero(HeroActionsRow);

        HeroMetadataRow.Margin = new Thickness(0, 6, 0, 0);
        ScoresPanel.Margin = new Thickness(0, 2, 0, 0);
        OverviewText.Margin = new Thickness(0, 4, 0, 0);
        TranslateOverviewButton.Margin = new Thickness(0, 2, 0, 0);
        HeroCrewLine.Margin = new Thickness(0, 2, 0, 0);
        GenresBadgesPanel.Margin = new Thickness(0, 4, 0, 0);
        HeroActionsRow.Margin = new Thickness(0, 8, 0, 0);
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
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        UpdateBackdropHeight();
        if (XamlRoot?.Content is FrameworkElement root)
        {
            _rootElement = root;
            _rootElement.SizeChanged += OnRootSizeChanged;
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBackdropHeight();
    }

    private void OnPlayerStateChanged(Services.PlayerState state)
    {
        if (state == Services.PlayerState.Idle && _playableContentId != null)
        {
            DispatcherQueue?.TryEnqueue(() => _ = LoadWatchDetailAsync(_playableContentId));
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_rootElement != null)
        {
            _rootElement.SizeChanged -= OnRootSizeChanged;
            _rootElement = null;
        }
        if (_playerService != null)
        {
            _playerService.StateChanged -= OnPlayerStateChanged;
            _subscribedToStateChanged = false;
        }
        _translationCts?.Cancel();
        _translationCts?.Dispose();
        _translationCts = null;
    }

    private string? _currentContentId;
    private string? _playableContentId; // The actual episode/movie ID used for watch detail
    private bool _subscribedToStateChanged;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Subscribe to player state changes to refresh play button after playback ends
        if (!_subscribedToStateChanged)
        {
            _subscribedToStateChanged = true;
            _playerService = App.Services.GetRequiredService<Services.PlayerService>();
            _playerService.StateChanged += OnPlayerStateChanged;
        }

        if (e.Parameter is string contentId && !string.IsNullOrEmpty(contentId))
        {
            _currentContentId = contentId;
            await ViewModel.LoadCommand.ExecuteAsync(contentId);

            // If this is an episode, enrich with series metadata
            if (ViewModel.Item?.Type == "episode" && !string.IsNullOrEmpty(ViewModel.Item.SeriesId))
            {
                await EnrichEpisodeWithSeriesDataAsync(ViewModel.Item.SeriesId);
            }

            // If this is a season, load its episodes for display
            if (ViewModel.Item?.Type == "season")
            {
                await LoadSeasonEpisodesAsync(ViewModel.Item.ContentId, ViewModel.Item.SeasonNumber ?? 0);
            }

            UpdateUI();
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
                _ = LoadSimilarItemsAsync();

            // Load sibling episodes if this is an episode (non-blocking)
            if (ViewModel.Item?.Type == "episode")
                _ = LoadSiblingEpisodesAsync();

            if (ViewModel.IsSeries)
            {
                SeasonsSection.Visibility = Visibility.Visible;
                SeasonsLoadingRing.IsActive = true;
                SeasonsLoadingRing.Visibility = Visibility.Visible;

                await ViewModel.LoadSeasonsCommand.ExecuteAsync(null);

                SeasonsLoadingRing.IsActive = false;
                SeasonsLoadingRing.Visibility = Visibility.Collapsed;

                var primaryAction = SeriesPrimaryActionResolver.Resolve(ViewModel.Seasons);
                if (!string.IsNullOrEmpty(primaryAction.TargetSeasonId))
                    await LoadSeriesPrimaryEpisodesAsync(primaryAction.TargetSeasonId);

                // Inline episodes for single-season series — skip the season picker
                // entirely and show the flat episode list directly. Matches WebUI
                // SeriesContent's single-season-collapse behavior (commit 7da3620).
                if (ViewModel.Seasons.Count <= 1)
                {
                    SeasonsSection.Visibility = Visibility.Collapsed;
                }
                else
                {
                    BuildSeasonCards();
                }
                BuildEpisodeRows();

                var targetIndex = Math.Clamp(
                    (primaryAction.TargetEpisodeNumber ?? 1) - 1,
                    0,
                    Math.Max(ViewModel.Episodes.Count - 1, 0));
                var targetEpisode = ViewModel.Episodes.Count > 0
                    ? ViewModel.Episodes[targetIndex]
                    : null;
                _playableContentId = targetEpisode?.ContentId;
                PlayButtonText.Text = targetEpisode?.UserData?.PositionSeconds > 0
                    ? "Resume"
                    : primaryAction.Label;
                SplitPlayButton.Visibility = targetEpisode == null
                    ? Visibility.Collapsed
                    : Visibility.Visible;
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
                _playableContentId = firstEpisode?.ContentId;
                PlayButtonText.Text = "Play First Episode";
                SplitPlayButton.Visibility = firstEpisode == null
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
                _playableContentId = contentId;
                _ = LoadWatchDetailAsync(contentId);
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

    private async Task EnrichEpisodeWithSeriesDataAsync(string seriesId)
    {
        try
        {
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var series = await catalogApi.GetItemDetailAsync(seriesId);
            if (series == null) return;

            var episode = ViewModel.Item!;

            // Fill in missing episode data from series
            if (string.IsNullOrEmpty(episode.BackdropUrl) && !string.IsNullOrEmpty(series.BackdropUrl))
                episode.BackdropUrl = series.BackdropUrl;
            if (string.IsNullOrEmpty(episode.PosterUrl) && !string.IsNullOrEmpty(series.PosterUrl))
                episode.PosterUrl = series.PosterUrl;
            if ((episode.Cast == null || episode.Cast.Count == 0) && series.Cast?.Count > 0)
                episode.Cast = series.Cast;
            if ((episode.Crew == null || episode.Crew.Count == 0) && series.Crew?.Count > 0)
                episode.Crew = series.Crew;
            if ((episode.Studios?.Count ?? 0) == 0 && (series.Studios?.Count ?? 0) > 0)
                episode.Studios = series.Studios!;
            if ((episode.Networks?.Count ?? 0) == 0 && (series.Networks?.Count ?? 0) > 0)
                episode.Networks = series.Networks!;
            if ((episode.Countries?.Count ?? 0) == 0 && (series.Countries?.Count ?? 0) > 0)
                episode.Countries = series.Countries!;
            if ((episode.Genres?.Count ?? 0) == 0 && (series.Genres?.Count ?? 0) > 0)
                episode.Genres = series.Genres!;
            if (string.IsNullOrEmpty(episode.ContentRating) && !string.IsNullOrEmpty(series.ContentRating))
                episode.ContentRating = series.ContentRating;
        }
        catch
        {
            // Non-critical — episode still shows with its own data
        }
    }

    private void UpdateUI()
    {
        var item = ViewModel.Item;
        if (item == null) return;

        UpdateBackdropHeight();

        WatchedButton.Visibility = Visibility.Visible;
        FavoriteButton.Visibility = Visibility.Visible;
        StarRatingContainer.Visibility = Visibility.Visible;
        MoreButton.Visibility = Visibility.Visible;
        AddCollectionButton.Visibility = Visibility.Collapsed;
        ListenFromStartButton.Visibility = Visibility.Collapsed;
        BookDownloadButton.Visibility = Visibility.Collapsed;
        HeroPosterContainer.Width = 170;
        HeroPosterContainer.Height = 255;
        MediaLocationsTitle.Text = item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase) ? "Files" : "Media locations";

        // Reset breadcrumb visibility by default; episode case repopulates it
        BreadcrumbPanel.Children.Clear();
        BreadcrumbPanel.Visibility = Visibility.Collapsed;

        // Star rating widget is hidden on season pages (webui parity — the
        // test suite explicitly asserts this; users rate individual movies
        // and series, not seasons).
        StarRatingContainer.Visibility = item.Type == "season"
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (item.Type == "season")
        {
            // SeasonContent exposes watched + curator actions, but not the
            // series/movie favorite action in the current WebUI.
            FavoriteButton.Visibility = Visibility.Collapsed;
        }

        // ─── Hero poster / logo / kicker (webui DetailHero parity) ───

        // Portrait poster column. Only show for non-episode items — episodes
        // inherit the series backdrop and don't have a dedicated portrait.
        if (item.Type != "episode" && !string.IsNullOrEmpty(item.PosterUrl))
        {
            _ = LoadHeroPosterAsync(item.PosterUrl);
            HeroPosterContainer.Visibility = Visibility.Visible;
        }
        else
        {
            HeroPosterContainer.Visibility = Visibility.Collapsed;
            HeroPosterImage.Source = null;
        }

        // Logo image — when present, replaces the plain title with the
        // stylized logo. Title still holds the text for accessibility.
        if (!string.IsNullOrEmpty(item.LogoUrl))
        {
            try
            {
                HeroLogoImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(item.LogoUrl));
                HeroLogoImage.Visibility = Visibility.Visible;
                TitleText.Visibility = Visibility.Collapsed;
            }
            catch
            {
                HeroLogoImage.Visibility = Visibility.Collapsed;
                TitleText.Visibility = Visibility.Visible;
            }
        }
        else
        {
            HeroLogoImage.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;
        }

        // Studio/network kicker — uppercase line above the title. Networks
        // win for series, studios for movies. First one only.
        string? kicker = null;
        if (item.Networks != null && item.Networks.Count > 0)
            kicker = item.Networks[0];
        else if (item.Studios != null && item.Studios.Count > 0)
            kicker = item.Studios[0];
        if (!string.IsNullOrEmpty(kicker))
        {
            StudioKickerText.Text = kicker;
            StudioKickerText.Visibility = Visibility.Visible;
        }
        else
        {
            StudioKickerText.Visibility = Visibility.Collapsed;
        }

        // Episode context: show series title and S##E## above/below episode title
        if (item.Type == "episode")
        {
            // Clickable breadcrumb: Series › Season N › Episode M
            // The season link navigates by season content_id (resolved async below).
            BuildEpisodeBreadcrumb(item);

            // Hide the plain tagline — breadcrumb replaces it
            TaglineText.Visibility = Visibility.Collapsed;

            // Show season/episode info in the year/metadata slot
            var episodeInfo = "";
            if (item.SeasonNumber.HasValue) episodeInfo += $"Season {item.SeasonNumber}";
            if (item.EpisodeNumber.HasValue) episodeInfo += (episodeInfo.Length > 0 ? " \u00B7 " : "") + $"Episode {item.EpisodeNumber}";
            if (!string.IsNullOrEmpty(episodeInfo))
            {
                YearText.Text = episodeInfo;
            }
            else
            {
                YearText.Text = item.Year > 0 ? item.Year.ToString() : "";
            }

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
        else
        {
            TitleText.Text = item.Title;
            TaglineText.Text = item.Tagline ?? "";
            TaglineText.Visibility = string.IsNullOrEmpty(item.Tagline)
                ? Visibility.Collapsed : Visibility.Visible;

            YearText.Text = item.Year > 0 ? item.Year.ToString() : "";
        }

        // F7: set dynamic window title to the item's name
        if (App.MainWindowInstance is MainWindow mw)
            mw.SetDynamicTitle(item.Title);

        // Content rating in pill badge
        if (!string.IsNullOrEmpty(item.ContentRating))
        {
            ContentRatingText.Text = item.ContentRating;
            ContentRatingBadge.Visibility = Visibility.Visible;
            MetaDot1.Visibility = item.Year > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ContentRatingBadge.Visibility = Visibility.Collapsed;
            MetaDot1.Visibility = Visibility.Collapsed;
        }

        RuntimeText.Text = ViewModel.RuntimeDisplay;
        if (item.Type == "season")
            RuntimeText.Text = $"{item.EpisodeCount ?? ViewModel.Episodes.Count} episodes";
        MetaDot2.Visibility = !string.IsNullOrEmpty(ViewModel.RuntimeDisplay) &&
            (item.Year > 0 || !string.IsNullOrEmpty(item.ContentRating))
            ? Visibility.Visible : Visibility.Collapsed;
        if (item.Type == "season")
            MetaDot2.Visibility = !string.IsNullOrEmpty(YearText.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

        OverviewText.Text = item.Overview ?? "";
        if (string.IsNullOrWhiteSpace(item.PendingTranslationLanguage))
            TranslateOverviewButton.Visibility = Visibility.Collapsed;

        // Genres as styled badge pills (webui: clickable genre badges)
        GenresBadgesPanel.Children.Clear();
        if (item.Genres?.Count > 0)
        {
            foreach (var genre in item.Genres)
            {
                var badge = new Border
                {
                    Background = (SolidColorBrush)Application.Current.Resources["SurfaceRaisedBrush"],
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(10, 4, 10, 4),
                };
                badge.Child = new TextBlock
                {
                    Text = genre,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                    Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
                };
                GenresBadgesPanel.Children.Add(badge);
            }
            GenresBadgesPanel.Visibility = Visibility.Visible;
        }
        else
        {
            GenresBadgesPanel.Visibility = Visibility.Collapsed;
        }

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
        MatchButton.Visibility = Visibility.Collapsed;
        RefreshMetadataButton.Visibility = Visibility.Collapsed;
        EditMetadataButton.Visibility = Visibility.Collapsed;
        BuildMediaLocationsSection(canCurateMetadata, item.Versions);

        // Load backdrop
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();
        _ = LoadBackdropAsync(item, _imageCts.Token);

        BuildTrailers(item.Videos ?? []);
        BuildExtras(item.Extras ?? []);

        // Build cast
        BuildCast(item.Cast ?? new());

        // Build crew (directors + writers)
        BuildCrew(item.Crew ?? new());

        // Hero crew line: "Directed by X · Written by Y"
        BuildHeroCrewLine(item.Crew ?? new());

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
        ConfigureBookDetail(item);
    }

    private void ArrangeCurrentWebUiContentOrder(string itemType)
    {
        if (TrailersSection.Parent is not StackPanel parent) return;

        FrameworkElement[] movable =
        [
            MediaLocationsSection,
            SeasonsSection,
            EpisodesSection,
            TrailersSection,
            ExtrasSection,
            SiblingEpisodesSection,
            CastHeader,
            CastScrollViewer,
            CrewSection,
            SimilarSection,
            SubtitlesSection,
            StudiosText,
            NetworksText,
            CountriesText,
            BookRelatedSection,
            AudiobookChaptersSection,
            MangaChaptersSection,
        ];
        foreach (var element in movable)
            parent.Children.Remove(element);

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
                CastHeader, CastScrollViewer, CrewSection, SimilarSection,
            ],
            "series" =>
            [
                SeasonsSection, EpisodesSection, TrailersSection, ExtrasSection,
                CastHeader, CastScrollViewer, CrewSection, SimilarSection,
            ],
            "episode" =>
            [
                MediaLocationsSection, SiblingEpisodesSection,
                CastHeader, CastScrollViewer, CrewSection,
            ],
            "audiobook" =>
            [
                BookRelatedSection, AudiobookChaptersSection,
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
                EpisodesSection, CastHeader, CastScrollViewer, CrewSection,
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
            parent.Children.Add(element);
        foreach (var element in movable.Except(orderedElements))
            parent.Children.Add(element);
    }

    private void ConfigureBookDetail(MediaItemDetail item)
    {
        _readerTargetContentId = null;
        _readerTargetFileId = null;
        BookRelatedSection.Visibility = Visibility.Collapsed;
        AudiobookChaptersSection.Visibility = Visibility.Collapsed;
        MangaChaptersSection.Visibility = Visibility.Collapsed;

        if (item.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase))
        {
            App.Services.GetRequiredService<Services.PlayerService>().SetAudiobookPresentation(item);
            var position = Math.Max(0, item.UserData?.PositionSeconds ?? 0);
            var duration = Math.Max(0, item.Audiobook?.TotalDurationSeconds ?? item.UserData?.DurationSeconds ?? 0);
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
            PlayButtonIcon.Glyph = "\uE768";
            PlayButtonText.Text = position > 0 && (item.UserData?.Played != true) ? "Resume" : "Listen";
            if (duration > 0)
            {
                RuntimeText.Text = FormatBookDuration(duration);
                _playProgressFraction = Math.Clamp(position / duration, 0, 1);
                UpdatePlayProgressWidth();
            }
            var authors = PeopleNames(item.Audiobook?.Authors, item.Crew, "Author");
            var narrators = PeopleNames(item.Audiobook?.Narrators, item.Crew, "Narrator");
            var creditParts = new List<string>();
            if (authors.Count > 0) creditParts.Add($"By {string.Join(", ", authors)}");
            if (narrators.Count > 0) creditParts.Add($"Narrated by {string.Join(", ", narrators)}");
            HeroCrewLine.Text = string.Join(" \u00B7 ", creditParts);
            HeroCrewLine.Visibility = creditParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildBookRelatedGroups(item.Audiobook?.Series, item.Audiobook?.Related, authors.FirstOrDefault());
            BuildAudiobookChapters(item);
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
            HeroCrewLine.Text = authors.Count > 0 ? $"By {string.Join(", ", authors)}" : "";
            HeroCrewLine.Visibility = authors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        AudiobookChaptersPanel.Children.Clear();
        var chapters = item.Versions.SelectMany(version => version.Chapters ?? [])
            .GroupBy(chapter => Math.Round(chapter.StartSeconds, 3))
            .Select(group => group.First()).OrderBy(chapter => chapter.StartSeconds).ToList();
        if (chapters.Count == 0) return;
        foreach (var chapter in chapters)
        {
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 10, 12, 10),
                Tag = chapter.StartSeconds,
            };
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = chapter.Title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
            var time = new TextBlock { Text = FormatClock(chapter.StartSeconds), Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], FontSize = 12 };
            Grid.SetColumn(time, 1);
            grid.Children.Add(time);
            button.Content = grid;
            button.Click += AudiobookChapter_Click;
            AudiobookChaptersPanel.Children.Add(button);
        }
        AudiobookChaptersSection.Visibility = Visibility.Visible;
    }

    private async void AudiobookChapter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: double seconds } || ViewModel.Item == null) return;
        await App.Services.GetRequiredService<Services.PlayerService>().PlayAsync(
            ViewModel.Item.ContentId,
            fileId: _selectedVersion?.FileId,
            startPositionOverride: seconds);
    }

    private void BuildMangaChapters(MediaItemDetail item)
    {
        MangaChaptersPanel.Children.Clear();
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

            MangaChaptersPanel.Children.Add(new Expander
            {
                Header = header,
                Content = rows,
                IsExpanded = !allRead,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            });
        }
        if (chapters.Count == 0)
            MangaChaptersPanel.Children.Add(new TextBlock { Text = "No chapters found. Chapters appear here once the library scan completes.", Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        MangaChaptersSection.Visibility = Visibility.Visible;
    }

    private void MangaJumpButton_Click(object sender, RoutedEventArgs e)
    {
        _mangaResumeRow?.StartBringIntoView(new BringIntoViewOptions
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
        if (sender is not Button { Tag: MangaChapter chapter } || ViewModel.Item == null) return;
        var api = App.Services.GetRequiredService<CatalogApi>();
        if (chapter.Read == true) await api.MarkUnwatchedAsync(chapter.ContentId);
        else await api.MarkWatchedAsync(chapter.ContentId);
        await ViewModel.LoadCommand.ExecuteAsync(ViewModel.Item.ContentId);
        UpdateUI();
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
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m" : $"{Math.Max(1, span.Minutes)}m";
    }

    private static string FormatClock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
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
        if (_isTranslatingOverview || string.IsNullOrWhiteSpace(item.PendingTranslationLanguage)) return;
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
                await ViewModel.LoadCommand.ExecuteAsync(item.ContentId);
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
            OverviewText.Opacity = 1;
            if (_translateButtonMode && !string.IsNullOrWhiteSpace(ViewModel.Item?.PendingTranslationLanguage))
            {
                TranslateOverviewButton.Visibility = Visibility.Visible;
                TranslateOverviewButton.IsEnabled = true;
                TranslateOverviewButton.Content = "Translate description";
            }
        }
    }

    private void UpdateScoresRow(MediaItemDetail item)
    {
        bool hasAnyScore = false;

        // IMDb score
        if (item.RatingImdb != null)
        {
            ImdbScorePanel.Visibility = Visibility.Visible;
            ImdbScoreText.Text = $"{item.RatingImdb:F1}";
            hasAnyScore = true;
        }
        else
        {
            ImdbScorePanel.Visibility = Visibility.Collapsed;
        }

        // TMDB score
        if (item.RatingTmdb != null)
        {
            TmdbScorePanel.Visibility = Visibility.Visible;
            TmdbScoreText.Text = $"{item.RatingTmdb:F1}";
            hasAnyScore = true;
        }
        else
        {
            TmdbScorePanel.Visibility = Visibility.Collapsed;
        }

        // RT Critic
        if (item.RatingRtCritic != null)
        {
            RtCriticPanel.Visibility = Visibility.Visible;
            RtCriticText.Text = $"{item.RatingRtCritic}%";
            hasAnyScore = true;
        }
        else
        {
            RtCriticPanel.Visibility = Visibility.Collapsed;
        }

        // RT Audience
        if (item.RatingRtAudience != null)
        {
            RtAudiencePanel.Visibility = Visibility.Visible;
            RtAudienceText.Text = $"{item.RatingRtAudience}%";
            hasAnyScore = true;
        }
        else
        {
            RtAudiencePanel.Visibility = Visibility.Collapsed;
        }

        ScoresPanel.Visibility = hasAnyScore ? Visibility.Visible : Visibility.Collapsed;
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

        // Audio codec badge (e.g., "EAC3", "TRUEHD", "DTS")
        if (!string.IsNullOrEmpty(best.CodecAudio))
        {
            var audioLabel = best.CodecAudio.ToUpperInvariant();
            QualityBadgesPanel.Children.Add(CreateQualityBadge(
                audioLabel,
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
        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = foreground ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            }
        };
    }

    // ===== Watched Toggle =====

    private void UpdateWatchedButton()
    {
        if (ViewModel.IsWatched)
        {
            WatchedIcon.Glyph = "\uE73E"; // Checkmark
            WatchedText.Text = "Watched";
            WatchedIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            WatchedIcon.Glyph = "\uE73E"; // Checkmark outline
            WatchedText.Text = "Mark as Watched";
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

        // Dim-highlight when showing hover preview so users can tell it's
        // not yet committed.
        var highlightBrush = _starHoverValue.HasValue
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentHoverBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];

        for (int i = 0; i < 5; i++)
        {
            bool filled = effective != null && (i + 1) <= effective;
            stars[i].Glyph = filled ? "\uE735" : "\uE734"; // E735=filled, E734=outline
            stars[i].Foreground = filled
                ? highlightBrush
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"];
        }
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
    private void BuildMoreFlyout()
    {
        MoreFlyout.Items.Clear();
        var item = ViewModel.Item;
        if (item == null) return;

        if (item.Type.Equals("manga", StringComparison.OrdinalIgnoreCase))
        {
            var details = new MenuFlyoutItem
            {
                Text = "View Details",
                Icon = new FontIcon { Glyph = "\uE946" },
            };
            details.Click += async (_, _) => await ShowMangaDetailsDialogAsync();
            MoreFlyout.Items.Add(details);
            if (AuthorizationPolicy.IsActingAdmin(App.Services.GetRequiredService<Core.Services.AuthService>()))
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
        }

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
            }

            var searchSubtitles = new MenuFlyoutItem
            {
                Text = "Search Subtitles",
                Icon = new FontIcon { Glyph = "\uED1E" },
            };
            searchSubtitles.Click += async (_, _) => await OpenSubtitleSearchDialogAsync();
            MoreFlyout.Items.Add(searchSubtitles);
        }

        var authService = App.Services.GetRequiredService<Core.Services.AuthService>();
        var isActingAdmin = AuthorizationPolicy.IsActingAdmin(authService);
        var canCurateMetadata = AuthorizationPolicy.CanCurateMetadata(authService);
        var canEditMarkers = AuthorizationPolicy.CanEditMarkers(authService);
        if (isActingAdmin || canCurateMetadata || canEditMarkers)
        {
            MoreFlyout.Items.Add(new MenuFlyoutSeparator());

            if (canCurateMetadata && _selectedVersion != null)
            {
                var mediaInfo = new MenuFlyoutItem
                {
                    Text = "Media Info",
                    Icon = new FontIcon { Glyph = "\uE946" },
                };
                mediaInfo.Click += async (_, _) => await ShowMediaInfoDialogAsync();
                MoreFlyout.Items.Add(mediaInfo);
            }

            if (isActingAdmin)
            {
                var history = new MenuFlyoutItem { Text = "View Play History" };
                history.Click += (_, _) =>
                {
                    App.MainWindowInstance?.RestoreMainPane();
                    App.Services.GetRequiredService<NavigationService>()
                        .Navigate<Admin.AdminShellPage>(new Admin.AdminShellNavigation(
                            typeof(Admin.AdminPlaybackHistoryPage),
                            new Admin.AdminPlaybackHistoryFilter(MediaItemId: item.ContentId)));
                };
                MoreFlyout.Items.Add(history);
            }

            if (canCurateMetadata)
            {
                var refreshItem = new MenuFlyoutItem
                {
                    Text = "Refresh Metadata",
                    Icon = new FontIcon { Glyph = "\uE72C" },
                };
                refreshItem.Click += async (_, _) => await ShowRefreshMetadataDialogAsync();
                MoreFlyout.Items.Add(refreshItem);
            }

            if (isActingAdmin && item.Type.Equals("episode", StringComparison.OrdinalIgnoreCase))
            {
                var redetect = new MenuFlyoutItem
                {
                    Text = "Re-detect Intro Markers",
                    Icon = new FontIcon { Glyph = "\uE72C" },
                };
                redetect.Click += async (_, _) => await RedetectIntroMarkersAsync();
                MoreFlyout.Items.Add(redetect);
            }

            if (canCurateMetadata)
            {
                var edit = new MenuFlyoutItem
                {
                    Text = "Edit Metadata",
                    Icon = new FontIcon { Glyph = "\uE70F" },
                };
                edit.Click += EditMetadataButton_Click;
                MoreFlyout.Items.Add(edit);
            }

            if (canEditMarkers && item.Type is "movie" or "episode")
            {
                var markers = new MenuFlyoutItem
                {
                    Text = "Edit Markers",
                    Icon = new FontIcon { Glyph = "\uE8EC" },
                };
                markers.Click += async (_, _) => await ShowMarkerEditorAsync();
                MoreFlyout.Items.Add(markers);
            }

            if (canCurateMetadata)
            {
                var match = new MenuFlyoutItem
                {
                    Text = "Match Item",
                    Icon = new FontIcon { Glyph = "\uE721" },
                };
                match.Click += MatchButton_Click;
                MoreFlyout.Items.Add(match);
            }

            if (canCurateMetadata && item.Versions.Count > 1)
            {
                var split = new MenuFlyoutItem
                {
                    Text = "Split Versions",
                    Icon = new FontIcon { Glyph = "\uE8C6" },
                };
                split.Click += async (_, _) => await ShowSplitVersionsDialogAsync();
                MoreFlyout.Items.Add(split);
            }
        }
    }

    private async Task ShowAddToCollectionDialogAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var toast = App.Services.GetRequiredService<Services.ToastService>();
        try
        {
            var api = App.Services.GetRequiredService<CollectionsApi>();
            var response = await api.GetCollectionsAsync();
            var choices = response.Collections
                .Where(collection => collection.CollectionType.Equals("manual", StringComparison.OrdinalIgnoreCase))
                .OrderBy(collection => collection.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (choices.Count == 0)
            {
                var empty = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "Add to Collection",
                    Content = "You don't have any manual collections yet. Create one in Collections first.",
                    CloseButtonText = "Close",
                };
                await empty.ShowAsync();
                return;
            }

            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 320,
                ItemsSource = choices,
                DisplayMemberPath = "Name",
            };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = $"Pick a manual collection to add “{item.Title}” to.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            });
            panel.Children.Add(list);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Add to Collection",
                Content = panel,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            dialog.IsPrimaryButtonEnabled = false;
            list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem != null;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary
                || list.SelectedItem is not Core.Models.Collections.Collection collection)
                return;

            await api.AddCollectionItemAsync(collection.Id, item.ContentId);
            toast.Success("Added to collection");
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
        }
    }

    private async void AddCollectionButton_Click(object sender, RoutedEventArgs e)
        => await ShowAddToCollectionDialogAsync();

    private async Task ShowMangaDetailsDialogAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        try
        {
            var data = await App.Services.GetRequiredService<CatalogApi>().GetMangaSeriesFilesAsync(item.ContentId);
            var panel = new StackPanel { Spacing = 12 };
            if (data.FolderPaths?.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "Folders", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                foreach (var path in data.FolderPaths)
                    panel.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            }
            panel.Children.Add(new TextBlock
            {
                Text = $"{data.Files.Count} files \u00B7 {FormatFileBytes(data.Files.Sum(file => file.FileSize))}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            foreach (var file in data.Files.OrderBy(file => MangaVolumeSort(file.Volume)).ThenBy(file => file.ChapterIndex))
            {
                var summary = string.Join(" \u00B7 ", new[]
                {
                    string.IsNullOrWhiteSpace(file.Volume) ? null : $"Volume {file.Volume}",
                    file.ChapterIndex.HasValue ? $"Chapter {file.ChapterIndex:0.##}" : null,
                    string.IsNullOrWhiteSpace(file.Container) ? null : file.Container.ToUpperInvariant(),
                    FormatFileBytes(file.FileSize),
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                panel.Children.Add(new TextBlock { Text = file.FileName, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
                panel.Children.Add(new TextBlock { Text = summary, FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            }
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"{item.Title} details",
                Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
                CloseButtonText = "Close",
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Title = "Unable to load details", Content = ex.Message, CloseButtonText = "Close" }.ShowAsync();
        }
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

    private async Task ShowMediaInfoDialogAsync()
    {
        var version = _selectedVersion;
        if (version == null) return;
        var lines = new List<string>
        {
            BuildQualitySummary(version),
            $"Container: {version.Container.ToUpperInvariant()}",
            $"Duration: {FormatExtraDuration(version.Duration)}",
            $"Bitrate: {(version.Bitrate > 0 ? $"{version.Bitrate / 1_000_000d:0.##} Mbps" : "Unknown")}",
            $"Size: {(version.FileSize > 0 ? FormatFileSize(version.FileSize) : "Unknown")}",
        };
        if (!string.IsNullOrWhiteSpace(version.EditionRaw)) lines.Add($"Edition: {version.EditionRaw}");
        if (!string.IsNullOrWhiteSpace(version.FileName)) lines.Add($"File: {version.FileName}");
        if (version.VideoTracks is { Count: > 0 })
        {
            var video = version.VideoTracks[0];
            lines.Add("");
            lines.Add("VIDEO");
            lines.Add($"{video.Codec?.ToUpperInvariant()}  {video.Width}×{video.Height}  {video.BitDepth}-bit");
            if (!string.IsNullOrWhiteSpace(video.DolbyVision)) lines.Add($"Dolby Vision: {video.DolbyVision}");
            if (video.Hdr10Plus == true) lines.Add("HDR10+");
        }
        if (version.AudioTracks is { Count: > 0 })
        {
            lines.Add("");
            lines.Add("AUDIO TRACKS");
            lines.AddRange(version.AudioTracks.Select((track, index) =>
                $"{index + 1}. {FormatAudioTrackSummary(track)}"));
        }
        if (version.SubtitleTracks is { Count: > 0 })
        {
            lines.Add("");
            lines.Add("SUBTITLE TRACKS");
            lines.AddRange(version.SubtitleTracks.Select((track, index) =>
                $"{index + 1}. {FormatSubtitleTrackSummary(track)}"));
        }

        var text = new TextBlock
        {
            Text = string.Join(Environment.NewLine, lines),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Media Info",
            Content = new ScrollViewer { Content = text, MaxHeight = 560 },
            CloseButtonText = "Close",
        };
        await dialog.ShowAsync();
    }

    private async Task ShowRefreshMetadataDialogAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "Choose whether to refresh the existing item or rebuild it from the files on disk.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Quick Refresh — Keep the current item and refresh metadata using the existing scan scope.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Complete Refresh — Clear the current match, re-scan, and rebuild the item from disk context. This can recreate the item with a new ID or type.",
            TextWrapping = TextWrapping.Wrap,
        });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Refresh Metadata",
            Content = content,
            PrimaryButtonText = "Quick Refresh",
            SecondaryButtonText = "Complete Refresh",
            CloseButtonText = "Cancel",
        };
        var result = await dialog.ShowAsync();
        var mode = result switch
        {
            ContentDialogResult.Primary => "quick",
            ContentDialogResult.Secondary => "complete",
            _ => null,
        };
        if (mode == null) return;

        var toast = App.Services.GetRequiredService<Services.ToastService>();
        try
        {
            await App.Services.GetRequiredService<AdminApi>()
                .RefreshItemMetadataAsync(item.ContentId, mode);
            toast.Success(mode == "complete" ? "Complete refresh queued" : "Metadata refresh queued");
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
        }
    }

    private async Task RedetectIntroMarkersAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var toast = App.Services.GetRequiredService<Services.ToastService>();
        try
        {
            var response = await App.Services.GetRequiredService<AdminApi>()
                .RedetectEpisodeIntroAsync(item.ContentId);
            toast.Success(response.Status == "already_running"
                ? "Re-detection already running"
                : "Re-detection started");
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
        }
    }

    private async Task ShowMarkerEditorAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
        var toast = App.Services.GetRequiredService<Services.ToastService>();
        FileMarkersResponse current;
        try
        {
            current = await playbackApi.GetItemMarkersAsync(item.ContentId);
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
            return;
        }

        var values = new Dictionary<string, (TextBox Start, TextBox End)>();
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var markerRows = new[]
        {
            (Key: "intro", Label: "Intro", Segment: current.Intro),
            (Key: "recap", Label: "Recap", Segment: current.Recap),
            (Key: "credits", Label: "Credits", Segment: current.Credits),
            (Key: "preview", Label: "Preview", Segment: current.Preview),
        };
        for (var row = 0; row < markerRows.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var marker = markerRows[row];
            var label = new TextBlock
            {
                Text = marker.Label,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            };
            var start = new TextBox { Text = FormatMarkerClock(marker.Segment.Start), PlaceholderText = "start" };
            var end = new TextBox { Text = FormatMarkerClock(marker.Segment.End), PlaceholderText = "end" };
            var clear = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 13 },
                Padding = new Thickness(8),
            };
            clear.Click += (_, _) => { start.Text = ""; end.Text = ""; };
            Grid.SetRow(label, row);
            Grid.SetRow(start, row); Grid.SetColumn(start, 1);
            Grid.SetRow(end, row); Grid.SetColumn(end, 2);
            Grid.SetRow(clear, row); Grid.SetColumn(clear, 3);
            grid.Children.Add(label);
            grid.Children.Add(start);
            grid.Children.Add(end);
            grid.Children.Add(clear);
            values[marker.Key] = (start, end);
        }

        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Set intro, recap, credits, and preview times. Use m:ss or h:mm:ss; leave both fields empty to remove a marker.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        });
        content.Children.Add(grid);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Edit markers",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var changes = new Dictionary<string, object?>();
            foreach (var (kind, fields) in values)
            {
                var startText = fields.Start.Text.Trim();
                var endText = fields.End.Text.Trim();
                if (startText.Length == 0 && endText.Length == 0)
                {
                    changes[kind] = null;
                    continue;
                }
                if (!TryParseMarkerClock(startText, out var startSeconds)
                    || !TryParseMarkerClock(endText, out var endSeconds))
                {
                    error.Text = $"{char.ToUpperInvariant(kind[0]) + kind[1..]}: enter both start and end (for example 1:30).";
                    error.Visibility = Visibility.Visible;
                    return;
                }
                if (endSeconds <= startSeconds)
                {
                    error.Text = $"{char.ToUpperInvariant(kind[0]) + kind[1..]}: end must be after start.";
                    error.Visibility = Visibility.Visible;
                    return;
                }
                changes[kind] = new Dictionary<string, object?>
                {
                    ["start"] = startSeconds,
                    ["end"] = endSeconds,
                };
            }

            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                await playbackApi.SetItemMarkersAsync(item.ContentId, changes);
                toast.Success("Markers saved");
                dialog.Hide();
            }
            catch (Exception ex)
            {
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = true;
            }
        };
        await dialog.ShowAsync();
    }

    private static string FormatMarkerClock(double? seconds)
    {
        if (seconds == null) return "";
        var span = TimeSpan.FromSeconds(Math.Round(Math.Max(0, seconds.Value)));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    private static bool TryParseMarkerClock(string value, out double seconds)
    {
        seconds = 0;
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3 || parts.Any(part => !int.TryParse(part, out _))) return false;
        var numbers = parts.Select(int.Parse).ToArray();
        if (numbers.Any(number => number < 0)) return false;
        seconds = numbers.Length switch
        {
            1 => numbers[0],
            2 when numbers[1] < 60 => numbers[0] * 60d + numbers[1],
            3 when numbers[1] < 60 && numbers[2] < 60 => numbers[0] * 3600d + numbers[1] * 60d + numbers[2],
            _ => -1,
        };
        return seconds >= 0;
    }

    private async Task ShowSplitVersionsDialogAsync()
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var adminApi = App.Services.GetRequiredService<AdminApi>();
        var toast = App.Services.GetRequiredService<Services.ToastService>();
        ItemFilesResponse filesResponse;
        try
        {
            filesResponse = await adminApi.GetItemFilesAsync(item.ContentId);
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
            return;
        }
        if (filesResponse.Files.Count < 2)
        {
            toast.Error("This item has only one file; splitting needs at least two.");
            return;
        }

        var selectedFiles = new List<(ItemFile File, CheckBox Check)>();
        var filesPanel = new StackPanel { Spacing = 5 };
        foreach (var group in filesResponse.Files.GroupBy(file => file.ObservedRootPath))
        {
            filesPanel.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            foreach (var file in group)
            {
                var check = new CheckBox
                {
                    Content = new TextBlock
                    {
                        Text = file.FilePath,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 560,
                    },
                };
                filesPanel.Children.Add(check);
                selectedFiles.Add((file, check));
            }
        }

        var title = new TextBox { Text = item.Title, Header = "Search title" };
        var year = new NumberBox
        {
            Header = "Year",
            Value = item.Year > 0 ? item.Year : double.NaN,
            Minimum = 1850,
            Maximum = 2100,
        };
        var imdb = new TextBox { Header = "IMDb ID", Text = item.ImdbId ?? "" };
        var tmdb = new TextBox { Header = "TMDB ID", Text = item.TmdbId ?? "" };
        var tvdb = new TextBox { Header = "TVDB ID", Text = item.TvdbId ?? "" };
        var identityGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        identityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        identityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identityGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        identityGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(year, 1);
        Grid.SetRow(imdb, 1);
        Grid.SetRow(tmdb, 1); Grid.SetColumn(tmdb, 1);
        Grid.SetRow(tvdb, 2);
        identityGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        identityGrid.Children.Add(title);
        identityGrid.Children.Add(year);
        identityGrid.Children.Add(imdb);
        identityGrid.Children.Add(tmdb);
        identityGrid.Children.Add(tvdb);

        var candidates = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = "Title",
            MaxHeight = 150,
        };
        var search = new Button { Content = "Search matches", HorizontalAlignment = HorizontalAlignment.Left };
        var detach = new CheckBox { Content = "Detach as unmatched" };
        var historyMode = new ComboBox { Header = "Watch history", SelectedIndex = 0 };
        historyMode.Items.Add(new ComboBoxItem { Content = "Follow play evidence (recommended)", Tag = "evidence" });
        historyMode.Items.Add(new ComboBoxItem { Content = "Keep all history on this item", Tag = "keep" });
        historyMode.Items.Add(new ComboBoxItem { Content = "Move everything to the new item", Tag = "move_all" });

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = $"{item.Title} ({item.Year}) · {item.Type}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock { Text = "Files to move", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(filesPanel);
        content.Children.Add(new TextBlock { Text = "Target identity", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(identityGrid);
        content.Children.Add(search);
        content.Children.Add(candidates);
        content.Children.Add(detach);
        content.Children.Add(historyMode);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Split Versions",
            Content = new ScrollViewer { Content = content, MaxHeight = 620 },
            PrimaryButtonText = "Review Split",
            CloseButtonText = "Cancel",
            IsPrimaryButtonEnabled = false,
        };
        void UpdateCanReview()
        {
            var selectedCount = selectedFiles.Count(entry => entry.Check.IsChecked == true);
            dialog.IsPrimaryButtonEnabled = selectedCount > 0
                                            && selectedCount < selectedFiles.Count
                                            && (detach.IsChecked == true || candidates.SelectedItem != null);
        }
        foreach (var entry in selectedFiles)
        {
            entry.Check.Checked += (_, _) => UpdateCanReview();
            entry.Check.Unchecked += (_, _) => UpdateCanReview();
        }
        detach.Checked += (_, _) => { candidates.SelectedItem = null; UpdateCanReview(); };
        detach.Unchecked += (_, _) => UpdateCanReview();
        candidates.SelectionChanged += (_, _) =>
        {
            if (candidates.SelectedItem != null) detach.IsChecked = false;
            UpdateCanReview();
        };
        search.Click += async (_, _) =>
        {
            search.IsEnabled = false;
            try
            {
                var response = await adminApi.MatchSearchAsync(item.ContentId, new Core.Models.Admin.ItemMatchSearchRequest
                {
                    Title = string.IsNullOrWhiteSpace(title.Text) ? null : title.Text.Trim(),
                    Year = double.IsNaN(year.Value) ? null : (int)year.Value,
                    ImdbId = string.IsNullOrWhiteSpace(imdb.Text) ? null : imdb.Text.Trim(),
                    TmdbId = string.IsNullOrWhiteSpace(tmdb.Text) ? null : tmdb.Text.Trim(),
                    TvdbId = string.IsNullOrWhiteSpace(tvdb.Text) ? null : tvdb.Text.Trim(),
                });
                candidates.ItemsSource = response.Candidates;
                if (response.Candidates.Count == 0) toast.Error("No matching titles found.");
            }
            catch (Exception ex)
            {
                toast.Error(ex.Message);
            }
            finally
            {
                search.IsEnabled = true;
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var chosenFiles = selectedFiles
            .Where(entry => entry.Check.IsChecked == true)
            .Select(entry => entry.File.Id)
            .ToList();
        var candidate = candidates.SelectedItem as Core.Models.Admin.MatchCandidate;
        var request = new ItemSplitRequest
        {
            FileIds = chosenFiles,
            Target = detach.IsChecked == true
                ? new ItemSplitTarget { Unmatched = true }
                : new ItemSplitTarget
                {
                    ProviderIds = candidate?.ProviderIds,
                    Title = candidate?.Title,
                    Year = candidate?.Year > 0 ? candidate.Year : null,
                },
            HistoryMode = (historyMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "evidence",
            PersistOverride = true,
            DryRun = true,
        };

        try
        {
            var preview = await adminApi.SplitItemAsync(item.ContentId, request);
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Confirm Split",
                Content = $"Move {chosenFiles.Count} file(s) to “{candidate?.Title ?? "Unmatched"}”?\n\n"
                          + $"History moved: {preview.Reattribution.HistoryMoved}\n"
                          + $"History staying: {preview.Reattribution.HistoryStayed}\n"
                          + $"Ambiguous history: {preview.Reattribution.HistoryAmbiguous}",
                PrimaryButtonText = "Split Versions",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            request.DryRun = false;
            var result = await adminApi.SplitItemAsync(item.ContentId, request);
            toast.Success($"Moved {result.FilesMoved} file{(result.FilesMoved == 1 ? "" : "s")} to a separate item");
            await ViewModel.LoadCommand.ExecuteAsync(item.ContentId);
            UpdateUI();
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
        }
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
            row.Children.Add(new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = (Brush)Application.Current.Resources["SidebarAccentBrush"],
                Child = new FontIcon { Glyph = "\uE896", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var labels = new StackPanel { Spacing = 2 };
            labels.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(quality) ? version.FileName ?? "Media file" : quality, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (version.FileSize > 0)
                labels.Children.Add(new TextBlock { Text = FormatFileSize(version.FileSize), FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);
            versionButton.Content = row;
            var selected = version;
            versionButton.Click += async (_, _) =>
            {
                dialog.Hide();
                await SaveDirectDownloadAsync(selected, title);
            };
            content.Children.Add(versionButton);
        }
        if (versions.Count > 1)
            content.Children.Add(new TextBlock { Text = "Larger files require more storage space.", FontSize = 11, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        dialog.Content = content;
        await dialog.ShowAsync();
    }

    private async Task SaveDirectDownloadAsync(FileVersion version, string title)
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
            if (file == null) return;

            var apiClient = App.Services.GetRequiredService<SiloApiClient>();
            var path = DownloadsApi.GetDirectDownloadPath(version.FileId);
            var token = apiClient.AccessToken;
            var url = apiClient.BaseUrl + path +
                      (string.IsNullOrWhiteSpace(token) ? "" : $"&token={Uri.EscapeDataString(token)}");
            var http = App.Services.GetRequiredService<HttpClient>();
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
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
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
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
        bool isSeries = item.Type == "series";

        var dialog = new MatchItemDialog(item.ContentId, versions, item.FolderPaths, isSeries)
        {
            XamlRoot = this.XamlRoot
        };

        // Pre-fill search with current title
        // Dialog SearchBox is accessible via name
        dialog.Loaded += (_, _) =>
        {
            // Find the search box by traversing the visual tree (it's named SearchBox in the dialog)
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedCandidate != null)
        {
            try
            {
                var adminApi = App.Services.GetRequiredService<AdminApi>();
                await adminApi.MatchApplyAsync(item.ContentId, new Core.Models.Admin.ItemMatchApplyRequest
                {
                    ProviderIds = dialog.SelectedCandidate.ProviderIds
                });

                // Refresh the item detail
                await ViewModel.LoadCommand.ExecuteAsync(item.ContentId);
                UpdateUI();
            }
            catch
            {
                // Match apply failure is non-fatal
            }
        }
    }

    // ===== Refresh metadata (admin) — B12 =====

    private async void RefreshMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.Item;
        if (item == null) return;

        try
        {
            RefreshMetadataButton.IsEnabled = false;
            var adminApi = App.Services.GetRequiredService<AdminApi>();
            await adminApi.RefreshItemMetadataAsync(item.ContentId);

            // Reload the item detail to pick up refreshed metadata
            await ViewModel.LoadCommand.ExecuteAsync(item.ContentId);
            UpdateUI();
        }
        catch
        {
            // Refresh failure is non-fatal
        }
        finally
        {
            RefreshMetadataButton.IsEnabled = true;
        }
    }

    private async void EditMetadataButton_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.Item;
        if (item == null) return;
        var dialog = new Controls.EditMetadataDialog(item) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        if (!dialog.HasAppliedChanges) return;

        // Image changes are immediate and metadata saves can alter any detail
        // surface, so reload the complete item after the dialog closes.
        await ViewModel.LoadCommand.ExecuteAsync(item.ContentId);
        UpdateUI();
    }

    // ===== Media Locations (admin-only) =====
    //
    // Mirrors web/src/components/MediaLocations.tsx. Shows folder/filename
    // for each version with a quality summary header and a copy-full-path
    // button. Admin-only; hidden for regular users even if they somehow land
    // on this page with file_path populated.

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
        var versionLabel = BuildVersionQualitySummary(version) ?? $"Version {index}";
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

        // Copy folder path button.
        if (!string.IsNullOrEmpty(folderPath))
        {
            var copyBtn = new Button
            {
                Width = 32, Height = 32, Padding = new Thickness(0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Top,
                Content = new FontIcon { Glyph = "\uE8C8", FontSize = 13 }, // Copy
            };
            ToolTipService.SetToolTip(copyBtn, "Copy full folder path");
            copyBtn.Click += (_, _) =>
            {
                var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                pkg.SetText(folderPath);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            };
            Grid.SetColumn(copyBtn, 1);
            grid.Children.Add(copyBtn);
        }

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
        var rangeLabel = MediaVideoRange.Label(v);
        if (!string.IsNullOrWhiteSpace(rangeLabel)) parts.Add(rangeLabel);
        if (!string.IsNullOrWhiteSpace(v.CodecVideo)) parts.Add(v.CodecVideo.ToUpperInvariant());
        return parts.Count == 0 ? null : string.Join(" · ", parts);
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
            Source = new BitmapImage(new Uri($"https://i.ytimg.com/vi/{video.SiteKey}/hqdefault.jpg")),
        };

        var imageHost = new Grid { Width = 280, Height = 158 };
        imageHost.Children.Add(thumbnail);
        imageHost.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xA8, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = "\uE768",
                FontSize = 18,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            },
        });

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
        card.Click += TrailerCard_Click;
        return card;
    }

    private async void TrailerCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ItemVideo video }
            || !IsSafeYouTubeKey(video.SiteKey))
            return;

        var title = string.IsNullOrWhiteSpace(video.Name)
            ? ExtraKindLabel(video.Kind)
            : video.Name!;
        var webView = new WebView2
        {
            Width = 960,
            Height = 540,
            Source = new Uri($"https://www.youtube-nocookie.com/embed/{video.SiteKey}?autoplay=1"),
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            CloseButtonText = "Close",
            Content = webView,
        };

        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            webView.Source = new Uri("about:blank");
        }
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
        row.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
            Child = new FontIcon { Glyph = "\uE768", FontSize = 15 },
        });
        row.Children.Add(text);

        var button = new Button
        {
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Content = row,
            Tag = extra.ContentId,
        };
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
        playerService.NextEpisodeContentId = null;
        if (playerService.State != Services.PlayerState.Idle)
        {
            await playerService.CloseAsync();
            await Task.Delay(300);
        }

        await playerService.PlayAsync(contentId);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item == null) return;

        if (IsReaderItem(ViewModel.Item))
        {
            var target = _readerTargetContentId ?? ViewModel.Item.ContentId;
            App.Services.GetRequiredService<NavigationService>()
                .Navigate<EbookReaderPage>(new EbookReaderNavigation(target, _readerTargetFileId));
            return;
        }

        // Series and season pages resolve their collection action to a concrete
        // episode. Movies and episode detail pages already point at themselves.
        if (!string.IsNullOrEmpty(_playableContentId))
        {
            NavigateToPlayer(_playableContentId, fileId: _selectedVersion?.FileId);
        }
        else
        {
            // Leaf fallback for older servers that did not return watch detail.
            NavigateToPlayer(ViewModel.Item.ContentId, fileId: _selectedVersion?.FileId);
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

    private void PlayFromStart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item == null) return;

        if (IsReaderItem(ViewModel.Item))
        {
            var target = _readerTargetContentId ?? ViewModel.Item.ContentId;
            App.Services.GetRequiredService<NavigationService>()
                .Navigate<EbookReaderPage>(new EbookReaderNavigation(target, _readerTargetFileId));
            return;
        }

        // For series, restart the current/next episode from position 0
        // (same episode selection as the Play button)
        if (!string.IsNullOrEmpty(_playableContentId))
        {
            NavigateToPlayer(_playableContentId, fromStart: true, fileId: _selectedVersion?.FileId);
        }
        else
        {
            NavigateToPlayer(ViewModel.Item.ContentId, fromStart: true, fileId: _selectedVersion?.FileId);
        }
    }

    private async void NavigateToPlayer(string contentId, bool fromStart = false, int? fileId = null)
    {
        var playerService = App.Services.GetRequiredService<Services.PlayerService>();

        // Phase 3b: pre-load the "next episode" hint so the Playing Next
        // cinematic overlay can show at the end of this episode. Applies
        // when this is a series episode with a known successor.
        SetNextEpisodeHintIfApplicable(playerService, contentId);

        // Close any existing playback before starting new
        if (playerService.State != Services.PlayerState.Idle)
        {
            await playerService.CloseAsync();
            await Task.Delay(300); // Let server process the session stop
        }
        // Phase 2a + 2b: pass pre-play audio + subtitle selections through.
        _ = playerService.PlayAsync(
            contentId,
            fromStart: fromStart,
            fileId: fileId,
            audioTrackIndex: _selectedAudioTrackIndex,
            subtitleSelection: _selectedSubtitleIndex);
    }

    /// <summary>
    /// Phase 3b — if the content being played is a series episode in the
    /// currently-loaded Episodes list, find the next one after it and record
    /// metadata on the PlayerService so the "Up next" overlay fires at the
    /// end. Clears any stale hint from a previous playback first.
    /// </summary>
    private void SetNextEpisodeHintIfApplicable(Services.PlayerService playerService, string currentContentId)
    {
        // Always clear first so a previous episode's next-hint doesn't leak in.
        playerService.NextEpisodeContentId = null;
        playerService.NextEpisodeTitle = null;
        playerService.NextEpisodeSeriesTitle = null;
        playerService.NextEpisodePosterUrl = null;
        playerService.NextEpisodeOverview = null;

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
        playerService.NextEpisodeContentId = next.ContentId;
        // Match the current WebUI post-roll episode label.
        var label = $"S{next.SeasonNumber}:E{next.EpisodeNumber}";
        playerService.NextEpisodeTitle = string.IsNullOrEmpty(next.Title) ? label : $"{label} \u2014 {next.Title}";
        playerService.NextEpisodeSeriesTitle = ViewModel.Item?.Title;
        playerService.NextEpisodePosterUrl = next.StillUrl;
        playerService.NextEpisodeOverview = next.Overview;
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
            if (qualityParts.Count == 0 && !string.IsNullOrEmpty(best.Resolution))
                qualityParts.Add(best.Resolution);
            var rangeLabel = MediaVideoRange.Label(best);
            if (!string.IsNullOrEmpty(rangeLabel)) qualityParts.Add(rangeLabel);
        }
        // Quality/HDR summary shows beside the version dropdown chevron
        // (webui commit a42f46b — no longer duplicated on the Play button).
        if (qualityParts.Count > 0)
        {
            VersionSummaryText.Text = string.Join(" ", qualityParts);
        }

        // Show version dropdown if multiple versions, OR if we have a quality
        // summary to display (so the single-version case still shows "4K HDR").
        if (item.Versions?.Count > 1 || qualityParts.Count > 0)
        {
            VersionDropdownButton.Visibility = Visibility.Visible;
            VersionSeparator.Visibility = Visibility.Visible;
        }

        // Resume state from user data
        var userData = item.UserData;
        if (userData != null && userData.PositionSeconds > 0 && !userData.Played)
        {
            var ts = TimeSpan.FromSeconds(userData.PositionSeconds);
            var timeStr = ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
            PlayButtonText.Text = $"Resume from {timeStr}";

            // Show progress bar
            if (userData.DurationSeconds > 0)
            {
                var fraction = userData.PositionSeconds / userData.DurationSeconds;
                _playProgressFraction = Math.Min(fraction, 1.0);
                SplitPlayButton.SizeChanged += OnSplitPlayButtonSizeChanged;
            }

            // Show "Play from Start" in dropdown
            VersionDropdownButton.Visibility = Visibility.Visible;
            VersionSeparator.Visibility = Visibility.Visible;
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
            var playbackApi = App.Services.GetRequiredService<PlaybackApi>();
            _watchDetail = await playbackApi.GetWatchDetailAsync(contentId);
            UpdatePlayButton();
            UpdateQualityBadges();

            // Show download button if we have a selected version
            DownloadButton.Visibility = Visibility.Collapsed;
            BuildMoreFlyout();

            // Subtitle selection/search lives in the ActionBar in the current
            // WebUI. Avoid a duplicate detail-page list and its extra request.
        }
        catch (Exception ex)
        {
            // Log the error so we can debug
            LocalLog.AppendLine("watch_detail_error.txt", $"LoadWatchDetailAsync failed for contentId={contentId}: {ex}");
        }
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
        bool isResuming = userData?.PositionSeconds > 0 && userData.Played != true;

        if (isResuming)
        {
            var ts = TimeSpan.FromSeconds(userData!.PositionSeconds!.Value);
            var timeStr = ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
            PlayButtonText.Text = $"Resume from {timeStr}";
        }
        else
        {
            PlayButtonText.Text = "Play";
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

            bool hasQualitySummary = false;
            if (best != null)
            {
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
                    VersionSummaryText.Text = string.Join(" ", qualityParts);
                    hasQualitySummary = true;
                }
            }

            // Show version dropdown if multiple versions OR if resuming (for "Play from Start")
            // OR if we have a quality summary to show beside it.
            if (versions.Count > 1 || isResuming || hasQualitySummary)
            {
                VersionDropdownButton.Visibility = Visibility.Visible;
                VersionSeparator.Visibility = Visibility.Visible;
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
                // We need to measure the split button width; use a reasonable estimate
                // The actual width will be set after layout
                SplitPlayButton.SizeChanged += OnSplitPlayButtonSizeChanged;
                _playProgressFraction = Math.Min(fraction, 1.0);
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
        AudioTracksFlyout.Items.Clear();
        var tracks = version?.AudioTracks;
        if (tracks == null || tracks.Count == 0)
        {
            AudioTracksButton.Visibility = Visibility.Collapsed;
            _selectedAudioTrackIndex = null;
            return;
        }

        // Show only when there's a real choice — one track = no point in showing.
        if (tracks.Count < 2)
        {
            AudioTracksButton.Visibility = Visibility.Collapsed;
            _selectedAudioTrackIndex = null;
            return;
        }

        AudioTracksButton.Visibility = Visibility.Visible;

        // Reset selection when we're on a new version so stale explicit indices
        // from a previous version don't leak in.
        _selectedAudioTrackIndex = null;

        // Auto index: prefer server's effective_audio_track_index, else first default, else 0.
        var autoIndex = version!.EffectiveAudioTrackIndex ?? -1;
        if (autoIndex < 0 || autoIndex >= tracks.Count)
            autoIndex = tracks.FindIndex(t => t.Default);
        if (autoIndex < 0) autoIndex = 0;
        var autoSummary = FormatAudioTrackSummary(tracks[autoIndex]);

        // "Auto" option
        var autoItem = new MenuFlyoutItem { Text = $"Auto: {autoSummary}" };
        autoItem.Click += (_, _) =>
        {
            _selectedAudioTrackIndex = null;
            UpdateAudioTracksSummary(tracks, autoIndex);
        };
        AudioTracksFlyout.Items.Add(autoItem);
        AudioTracksFlyout.Items.Add(new MenuFlyoutSeparator());

        // Explicit track options
        for (int i = 0; i < tracks.Count; i++)
        {
            var idx = i; // capture
            var track = tracks[i];
            var label = FormatAudioTrackSummary(track);
            if (track.Default) label += "  (default)";
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) =>
            {
                _selectedAudioTrackIndex = idx;
                UpdateAudioTracksSummary(tracks, autoIndex);
            };
            AudioTracksFlyout.Items.Add(item);
        }

        UpdateAudioTracksSummary(tracks, autoIndex);
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
    /// Shows "Off" + "Auto" + each embedded subtitle track. Selection applied
    /// client-side by setting mpv's "sid" property before loadfile — subtitles
    /// aren't baked into the stream, mpv reads them live from the MKV/MP4
    /// container and picks based on the sid property.
    /// </summary>
    private void BuildSubtitlesPopoverFlyout(FileVersion? version)
    {
        SubtitlesPopoverFlyout.Items.Clear();
        var subs = version?.SubtitleTracks ?? [];
        if (version == null)
        {
            SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
            _selectedSubtitleIndex = null;
            return;
        }

        SubtitlesPopoverButton.Visibility = Visibility.Visible;
        _selectedSubtitleIndex = null; // Reset on version change

        if (subs.Count > 0)
        {
            // Off
            var offItem = new MenuFlyoutItem { Text = "Off" };
            offItem.Click += (_, _) =>
            {
                _selectedSubtitleIndex = -1;
                UpdateSubtitlesPopoverSummary(subs);
            };
            SubtitlesPopoverFlyout.Items.Add(offItem);

            // Auto
            var autoItem = new MenuFlyoutItem { Text = "Auto" };
            autoItem.Click += (_, _) =>
            {
                _selectedSubtitleIndex = null;
                UpdateSubtitlesPopoverSummary(subs);
            };
            SubtitlesPopoverFlyout.Items.Add(autoItem);

            SubtitlesPopoverFlyout.Items.Add(new MenuFlyoutSeparator());

            // Explicit embedded tracks
            for (int i = 0; i < subs.Count; i++)
            {
                var idx = i;
                var sub = subs[i];
                var item = new MenuFlyoutItem { Text = FormatSubtitleTrackSummary(sub) };
                item.Click += (_, _) =>
                {
                    _selectedSubtitleIndex = idx;
                    UpdateSubtitlesPopoverSummary(subs);
                };
                SubtitlesPopoverFlyout.Items.Add(item);
            }

            SubtitlesPopoverFlyout.Items.Add(new MenuFlyoutSeparator());
        }

        // Opens the full SubtitleSearchDialog for provider search or upload.
        var searchItem = new MenuFlyoutItem { Text = "Add subtitles..." };
        searchItem.Click += async (_, _) => await OpenSubtitleSearchDialogAsync();
        SubtitlesPopoverFlyout.Items.Add(searchItem);

        if (subs.Count > 0)
            UpdateSubtitlesPopoverSummary(subs);
        else
            SubtitlesSummary.Text = "";
    }

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
        var dialog = new Controls.SubtitleSearchDialog(_selectedVersion.FileId, defaultLang)
        {
            XamlRoot = this.XamlRoot,
        };
        dialog.SubtitleDownloaded += async () =>
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
        if (_selectedSubtitleIndex == null)
        {
            // Resolve what "Auto" picks using profile prefs + effective audio language
            // so the user can see what will actually turn on. Falls back to bare "Auto"
            // if nothing resolves (no preferred language / no matching track).
            var resolved = ResolveAutoSubtitle(subs);
            SubtitlesSummary.Text = resolved != null
                ? $"Auto: {FormatSubtitleTrackSummary(resolved)}"
                : "Auto";
            return;
        }
        var idx = _selectedSubtitleIndex.Value;
        if (idx < 0 || idx >= subs.Count)
        {
            SubtitlesSummary.Text = "Auto";
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
            ProfileLanguage: null, // profile-level "language" isn't yet surfaced client-side
            ShowForcedSubtitles: showForced ?? true));

        if (idx == null) return null;
        // Resolve back to a VersionSubtitleTrack. OriginalIndex was set to track.Index ?? fallback.
        var track = subs.FirstOrDefault(s => (s.Index ?? -1) == idx.Value);
        if (track != null) return track;
        // Fallback: position-based lookup if tracks lack Index.
        return idx.Value >= 0 && idx.Value < subs.Count ? subs[idx.Value] : null;
    }

    /// <summary>Short label for a subtitle track: "ENG (Forced)", "JPN SRT", etc.</summary>
    private static string FormatSubtitleTrackSummary(VersionSubtitleTrack sub)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sub.Language))
            parts.Add(sub.Language.ToUpperInvariant());
        else if (!string.IsNullOrWhiteSpace(sub.Title))
            parts.Add(sub.Title!);
        else
            parts.Add("Unknown");

        if (!string.IsNullOrWhiteSpace(sub.Codec))
            parts.Add(sub.Codec.ToUpperInvariant());

        var flags = new List<string>();
        if (sub.Forced == true) flags.Add("Forced");
        if (sub.HearingImpaired == true) flags.Add("HI");
        if (flags.Count > 0) parts.Add($"({string.Join(", ", flags)})");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Build a short descriptive label for an audio track: "English AC3 5.1", "FRE DTS 7.1", etc.
    /// Mirrors the WebUI <c>formatAudioTrackSummary</c> helper.
    /// </summary>
    private static string FormatAudioTrackSummary(AudioTrackInfo track)
    {
        var parts = new List<string>();
        var lang = !string.IsNullOrWhiteSpace(track.Language) ? track.Language.ToUpperInvariant() : null;
        var title = !string.IsNullOrWhiteSpace(track.Title) ? track.Title : track.EmbeddedTitle;
        if (!string.IsNullOrEmpty(lang)) parts.Add(lang);
        else if (!string.IsNullOrEmpty(title)) parts.Add(title!);
        else parts.Add("Unknown");

        if (!string.IsNullOrWhiteSpace(track.Codec))
            parts.Add(track.Codec.ToUpperInvariant());

        if (track.Channels.HasValue)
        {
            var ch = track.Channels.Value switch
            {
                1 => "Mono",
                2 => "Stereo",
                6 => "5.1",
                7 => "6.1",
                8 => "7.1",
                _ => $"{track.Channels.Value}ch",
            };
            parts.Add(ch);
        }

        return string.Join(" ", parts);
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
                    UpdateSelectedVersionUi(defaultVersion);
                    ConfigureVersionSelectors(versions, orderedVariants, defaultVersion, isResuming);
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
                ? $"{minutes / 60}h {minutes % 60}m"
                : $"{minutes}m";
        }
        else
        {
            RuntimeText.Text = ViewModel.RuntimeDisplay;
        }

        MetaDot2.Visibility = !string.IsNullOrEmpty(RuntimeText.Text) &&
            (item.Year > 0 || !string.IsNullOrEmpty(item.ContentRating))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BuildVersionFlyout(
        List<FileVersion> versions,
        bool isResuming,
        List<FileVersion>? allVersions = null,
        List<PlaybackVariant>? variants = null)
    {
        VersionFlyout.Items.Clear();

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
                .ThenByDescending(v => v.Bitrate)
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
                    UpdateSelectedVersionUi(fileVersion);
                    if (allVersions != null && variants != null)
                        ConfigureVersionSelectors(allVersions, variants, fileVersion, isResuming);
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
        var audio = NormalizeAudioCodec(version.CodecAudio, version.AudioChannels);
        if (!string.IsNullOrEmpty(audio))
            parts.Add(audio!);
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
        await ViewModel.LoadSimilarCommand.ExecuteAsync(null);

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
    }

    // ===== Hero Crew Line =====

    private void BuildHeroCrewLine(List<SiloPlayer.Core.Models.Catalog.CrewMember> crew)
    {
        if (crew.Count == 0) { HeroCrewLine.Visibility = Visibility.Collapsed; return; }

        var parts = new List<string>();
        var directors = crew.Where(c => c.Job.Equals("Director", StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).Distinct().Take(2);
        var writers = crew.Where(c => c.Job.Equals("Writer", StringComparison.OrdinalIgnoreCase) || c.Job.Equals("Screenplay", StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).Distinct().Take(2);

        var dirList = directors.ToList();
        var writerList = writers.ToList();

        if (dirList.Count > 0) parts.Add($"Directed by {string.Join(", ", dirList)}");
        if (writerList.Count > 0) parts.Add($"Written by {string.Join(", ", writerList)}");

        if (parts.Count > 0)
        {
            HeroCrewLine.Text = string.Join(" \u00B7 ", parts);
            HeroCrewLine.Visibility = Visibility.Visible;
        }
        else
        {
            HeroCrewLine.Visibility = Visibility.Collapsed;
        }
    }

    // ===== Season Episode Loading =====

    private async Task LoadSeasonEpisodesAsync(string seasonContentId, int seasonNumber)
    {
        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var response = await catalogApi.GetItemEpisodesAsync(seasonContentId);
            if (response?.Episodes != null && response.Episodes.Count > 0)
            {
                ViewModel.SelectedSeasonNumber = seasonNumber;
                ViewModel.Episodes.Clear();
                foreach (var ep in response.Episodes)
                    ViewModel.Episodes.Add(ep);
            }
        }
        catch { }
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
        if (seasonNum == null || seasonNum <= 0) return;

        try
        {
            var catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
            var response = await catalogApi.GetEpisodesAsync(item.SeriesId, seasonNum.Value);

            if (response?.Episodes == null || response.Episodes.Count <= 1)
            {
                SiblingEpisodesSection.Visibility = Visibility.Collapsed;
                return;
            }

            SiblingEpisodesSection.Visibility = Visibility.Visible;
            SiblingEpisodesTitle.Text = $"Season {seasonNum} Episodes";
            SiblingEpisodesPanel.Children.Clear();

            foreach (var ep in response.Episodes)
            {
                if (ep.ContentId == item.ContentId) continue;

                // Convert Episode to MediaItem for LandscapeCard
                var mediaItem = new SiloPlayer.Core.Models.Home.MediaItem
                {
                    ContentId = ep.ContentId,
                    Title = $"E{ep.EpisodeNumber} · {ep.Title}",
                    Type = "episode",
                    BackdropUrl = ep.StillUrl,
                    BackdropThumbhash = ep.StillThumbhash,
                    Overview = ep.Overview ?? "",
                };
                if (ep.UserData != null)
                {
                    mediaItem.PositionSeconds = ep.UserData.PositionSeconds;
                    mediaItem.DurationSeconds = ep.UserData.DurationSeconds;
                }

                var card = new LandscapeCard { MediaItem = mediaItem };
                SiblingEpisodesPanel.Children.Add(card);
            }

            if (SiblingEpisodesPanel.Children.Count == 0)
                SiblingEpisodesSection.Visibility = Visibility.Collapsed;
        }
        catch
        {
            SiblingEpisodesSection.Visibility = Visibility.Collapsed;
        }
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
    private async Task LoadHeroPosterAsync(string posterUrl)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var item = ViewModel.Item;
            if (item == null) return;

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "poster", posterUrl, httpClient, CancellationToken.None);
            if (bytes == null) return;

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 340, // 2x for crisp on HiDPI
                DecodePixelType = DecodePixelType.Logical,
            };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            HeroPosterImage.Source = bitmap;
        }
        catch { /* best-effort */ }
    }

    // ===== Cast Section =====

    private void BuildCast(List<CastMember> cast)
    {
        CastPanel.Children.Clear();

        if (cast.Count == 0)
        {
            CastHeader.Visibility = Visibility.Collapsed;
            CastScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

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
                Spacing = 6
            };

            // Photo placeholder (portrait, rounded corners — not a circle).
            // Show initials when no photo URL instead of a generic Contact glyph.
            var photoBorder = new Border
            {
                Width = 110,
                Height = 165,
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
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
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                HorizontalAlignment = HorizontalAlignment.Center
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
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            // Make cast card clickable if PersonId is available
            if (!string.IsNullOrEmpty(member.PersonId))
            {
                card.Tag = member.PersonId;
                card.Tapped += CastCard_Tapped;
                card.PointerEntered += (s, _) =>
                {
                    if (s is FrameworkElement fe)
                        fe.Opacity = 0.7;
                };
                card.PointerExited += (s, _) =>
                {
                    if (s is FrameworkElement fe)
                        fe.Opacity = 1.0;
                };
            }

            CastPanel.Children.Add(card);
        }
    }

    private void CastCard_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // B33: Pass the string ID through. Non-numeric IDs from third-party
        // providers used to be filtered out by int.TryParse and silently
        // become non-clickable.
        if (sender is FrameworkElement fe && fe.Tag is string personId
            && !string.IsNullOrEmpty(personId))
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<PersonDetailPage>(personId);
        }
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
            c.Job.Equals("Producer", StringComparison.OrdinalIgnoreCase)
            || c.Job.Equals("Executive Producer", StringComparison.OrdinalIgnoreCase)));

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

    private void ToggleCrewGroup(StackPanel groupPanel, StackPanel namesPanel, List<CrewMember> members)
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

        if (ViewModel.Seasons.Count == 0) return;

        _highlightedSeasonNumber = ViewModel.SelectedSeasonNumber;

        foreach (var season in ViewModel.Seasons)
        {
            var card = CreateSeasonCard(season);
            SeasonsPanel.Children.Add(card);
        }
    }

    private Border CreateSeasonCard(Season season)
    {
        bool isSelected = season.SeasonNumber == _highlightedSeasonNumber;

        // Poster image area
        var posterBorder = new Border
        {
            Width = 150,
            Height = 225,
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"]
        };

        var posterPlaceholder = new FontIcon
        {
            Glyph = "\uE8B9", // Photo icon
            FontSize = 32,
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
        var posterHost = new Grid { Width = 150, Height = 225 };
        posterHost.Children.Add(posterBorder);

        // Completed checkmark (top-right green badge) — webui parity: the
        // circular bg-green-500/90 check overlay on fully-watched seasons.
        bool isCompleted = season.UserData?.Played == true;
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
        if (season.UserData != null && season.EpisodeCount > 0)
        {
            int watched = isCompleted ? season.EpisodeCount : (season.UserData.WatchedCount);
            double pct = Math.Clamp((double)watched / season.EpisodeCount * 100, 0, 100);
            if (pct > 0)
            {
                var barTrack = new Grid
                {
                    Height = 3,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                };
                barTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
                barTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
                var fill = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Fill = isCompleted
                        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E))
                        : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                };
                Grid.SetColumn(fill, 0);
                barTrack.Children.Add(fill);
                posterHost.Children.Add(barTrack);
            }
        }

        // Title
        var titleText = new TextBlock
        {
            Text = season.Title,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 6, 2, 0)
        };

        // Episode count / progress
        var progressText = BuildSeasonProgressText(season);

        var content = new StackPanel
        {
            Width = 150,
            Children = { posterHost, titleText, progressText }
        };

        var cardBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(0),
            BorderBrush = isSelected
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
                : null,
            Child = content,
            Tag = season.SeasonNumber
        };

        cardBorder.PointerEntered += (s, _) =>
        {
            if (s is Border b && (int)b.Tag != _highlightedSeasonNumber)
                b.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceHoverBrush"];
        };

        cardBorder.PointerExited += (s, _) =>
        {
            if (s is Border b && (int)b.Tag != _highlightedSeasonNumber)
                b.Background = null;
        };

        cardBorder.Tapped += async (s, _) =>
        {
            if (s is Border b)
            {
                int seasonNum = (int)b.Tag;
                if (seasonNum == _highlightedSeasonNumber) return;

                _highlightedSeasonNumber = seasonNum;

                // Update visual selection
                foreach (var child in SeasonsPanel.Children)
                {
                    if (child is Border border)
                    {
                        bool sel = (int)border.Tag == seasonNum;
                        border.BorderThickness = sel ? new Thickness(2) : new Thickness(0);
                        border.BorderBrush = sel
                            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
                            : null;
                        border.Background = null;
                    }
                }

                // Load episodes
                EpisodesLoadingRing.IsActive = true;
                EpisodesLoadingRing.Visibility = Visibility.Visible;
                EpisodesPanel.Children.Clear();

                await ViewModel.SelectSeasonCommand.ExecuteAsync(seasonNum);

                EpisodesLoadingRing.IsActive = false;
                EpisodesLoadingRing.Visibility = Visibility.Collapsed;

                BuildEpisodeRows();
            }
        };

        return cardBorder;
    }

    private TextBlock BuildSeasonProgressText(Season season)
    {
        string text;
        if (season.UserData != null)
        {
            var ud = season.UserData;
            if (ud.Played)
                text = $"{season.EpisodeCount} episodes \u2022 Watched";
            else if (ud.WatchedCount > 0)
                text = $"{ud.WatchedCount}/{season.EpisodeCount} watched";
            else
                text = $"{season.EpisodeCount} episodes";
        }
        else
        {
            text = $"{season.EpisodeCount} episodes";
        }

        return new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 2, 2, 0)
        };
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
    private const double EpisodeCardMinWidth = 260;
    private const int EpisodeStillDecodeWidth = 640;

    private void BuildEpisodeRows()
    {
        EpisodesPanel.Children.Clear();
        EpisodesPanel.RowDefinitions.Clear();
        EpisodesPanel.ColumnDefinitions.Clear();

        if (ViewModel.Episodes.Count == 0)
        {
            EpisodesSection.Visibility = Visibility.Collapsed;
            return;
        }

        EpisodesSection.Visibility = Visibility.Visible;
        if (ViewModel.Item?.Type == "season")
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

        // Derive column count from current width. Fallback to 4 before first layout.
        double availableWidth = EpisodesPanel.ActualWidth > 0 ? EpisodesPanel.ActualWidth : 1200;
        int cols = Math.Max(1, (int)Math.Floor(availableWidth / EpisodeCardMinWidth));
        cols = Math.Min(cols, 5); // Cap at 5 to match upstream's lg:grid-cols-5.

        // Column definitions (equal-width fractions).
        for (int c = 0; c < cols; c++)
        {
            EpisodesPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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
            card.Margin = new Thickness(col == 0 ? 0 : 8, row == 0 ? 0 : 8, col == cols - 1 ? 0 : 8, 8);
            EpisodesPanel.Children.Add(card);
            i++;
        }
    }

    private void EpisodesPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Reflow when available width crosses a column boundary. Only re-layout
        // if column count would change; skip minor resize noise.
        if (ViewModel.Episodes.Count == 0) return;
        double width = e.NewSize.Width;
        int newCols = Math.Max(1, Math.Min(5, (int)Math.Floor(width / EpisodeCardMinWidth)));
        int currentCols = EpisodesPanel.ColumnDefinitions.Count;
        if (newCols != currentCols) LayoutEpisodeGrid();
    }

    /// <summary>
    /// Build a compact vertical episode card for the grid layout: 16:9 still on
    /// top (with progress bar overlay for in-progress episodes), ep-number + title
    /// line below, overview (2 lines), then quality badges + watched checkmark.
    /// </summary>
    private Border CreateEpisodeCard(Episode episode)
    {
        bool isInProgress = episode.UserData != null
            && episode.UserData.PositionSeconds > 0
            && !episode.UserData.Played;
        bool isWatched = episode.UserData?.Played == true;

        // ── Still image (16:9, fills card width) ──────────────────────────
        var stillBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
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
        if (isInProgress && episode.UserData!.DurationSeconds > 0)
        {
            var progressFraction = Math.Min(1.0, episode.UserData.PositionSeconds / episode.UserData.DurationSeconds);
            var progressTrack = new Grid
            {
                Height = 3,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            progressTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(progressFraction, GridUnitType.Star) });
            progressTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0 - progressFraction, GridUnitType.Star) });
            var fill = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                CornerRadius = new CornerRadius(0, 2, 2, 0),
            };
            Grid.SetColumn(fill, 0);
            progressTrack.Children.Add(fill);
            stillWrapper.Children.Add(progressTrack);
        }

        // ── Title line: "42. Title · 42 min" ───────────────────────────────
        var runtimeStr = episode.Runtime > 0 ? $" \u00B7 {episode.Runtime} min" : "";
        var titleText = new TextBlock
        {
            Text = $"{episode.EpisodeNumber}. {episode.Title}{runtimeStr}",
            Style = (Style)Application.Current.Resources["SubtitleTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(0, 8, 0, 0),
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

        // ── Badges row (resolution, HDR) + watched checkmark ──────────────
        var badgesPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 6, 0, 0),
        };
        foreach (var file in episode.Files)
        {
            var label = file.Resolution;
            var rangeLabel = episode.OverlaySummary?.Hdr;
            if (string.IsNullOrWhiteSpace(rangeLabel) && file.Hdr)
                rangeLabel = "HDR";
            if (!string.IsNullOrEmpty(rangeLabel)) label += $" {rangeLabel}";
            var badge = new Border
            {
                Background = !string.IsNullOrEmpty(rangeLabel)
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeHdrBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeResolutionBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                },
            };
            badgesPanel.Children.Add(badge);
        }
        if (isWatched)
        {
            badgesPanel.Children.Add(new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 13,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E)),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        // ── Assemble card ─────────────────────────────────────────────────
        var content = new StackPanel
        {
            Spacing = 0,
            Children = { stillWrapper, titleText, overviewText, badgesPanel },
        };

        var defaultBg = isInProgress
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x0D, 0x78, 0xAE, 0xFC))
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"];

        var cardBorder = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(10),
            Child = content,
            Tag = episode.ContentId,
            Background = defaultBg,
        };
        cardBorder.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        };
        cardBorder.PointerExited += (s, _) =>
        {
            if (s is Border b) b.Background = defaultBg;
        };
        cardBorder.Tapped += EpisodeRow_Tapped;

        return cardBorder;
    }

    private void AddEpisodeCardOverlays(Grid host, Episode episode)
    {
        var topLeft = MakeEpisodeOverlayHost(HorizontalAlignment.Left, VerticalAlignment.Top);
        var topRight = MakeEpisodeOverlayHost(HorizontalAlignment.Right, VerticalAlignment.Top);
        var bottomLeft = MakeEpisodeOverlayHost(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        var bottomRight = MakeEpisodeOverlayHost(HorizontalAlignment.Right, VerticalAlignment.Bottom);
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
            foreach (var definition in global::SiloPlayer.Services.OverlayRegistry.All)
            {
                if (!prefs.TryGetValue(definition.Id, out var config) || !config.Enabled) continue;
                if (global::SiloPlayer.Services.OverlayRegistry.SuppressesStandaloneOverlays(definition.Id, prefs)) continue;
                var value = definition.GetValue(data);
                if (string.IsNullOrWhiteSpace(value)) continue;
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
            }
        }

        Render();
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
