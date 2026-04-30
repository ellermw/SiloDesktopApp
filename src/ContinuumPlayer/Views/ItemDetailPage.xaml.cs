using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class ItemDetailPage : Page
{
    public ItemDetailViewModel ViewModel { get; }
    private CancellationTokenSource? _imageCts;
    private int _highlightedSeasonNumber;
    private WatchDetailResponse? _watchDetail;
    private FileVersion? _selectedVersion;
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
        SmoothScrollHelper.Attach(ContentScroll);

        // Listen for async property changes (e.g., rating loaded after initial UI update)
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        this.Loaded += OnPageLoaded;
    }

    private void UpdateBackdropHeight()
    {
        // Match web UI: min-h-[60dvh] -- 60% of viewport height
        if (XamlRoot?.Content is FrameworkElement root && root.ActualHeight > 0)
            BackdropContainer.Height = Math.Max(300, root.ActualHeight * 0.60);
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
            if (ViewModel.Item?.Type == "season" && !string.IsNullOrEmpty(ViewModel.Item.SeriesId))
            {
                await LoadSeasonEpisodesAsync(ViewModel.Item.SeriesId, ViewModel.Item.SeasonNumber ?? 0);
            }

            UpdateUI();

            // Rating is inlined on the item detail response via `user_rating`
            // (server commit 4172a16). Only fire the dedicated /ratings/{id}
            // endpoint if the server didn't populate it — keeps older servers
            // working while avoiding an extra round trip on current ones.
            if (ViewModel.Item?.UserRating == null && ViewModel.UserRating == null)
            {
                _ = ViewModel.LoadRatingCommand.ExecuteAsync(null);
            }

            // Load similar items (non-blocking)
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

                // For series, find the next episode to play and load its watch detail
                // so the play button shows "Resume" with quality info
                var nextEpisode = ViewModel.Episodes.FirstOrDefault(ep => ep.UserData?.Played != true)
                                  ?? ViewModel.Episodes.FirstOrDefault();
                if (nextEpisode != null)
                {
                    _playableContentId = nextEpisode.ContentId;
                    _ = LoadWatchDetailAsync(nextEpisode.ContentId);
                }
            }
            else
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

        // Reset breadcrumb visibility by default; episode case repopulates it
        BreadcrumbPanel.Children.Clear();
        BreadcrumbPanel.Visibility = Visibility.Collapsed;

        // Star rating widget is hidden on season pages (webui parity — the
        // test suite explicitly asserts this; users rate individual movies
        // and series, not seasons).
        StarRatingContainer.Visibility = item.Type == "season"
            ? Visibility.Collapsed
            : Visibility.Visible;

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
        MetaDot2.Visibility = !string.IsNullOrEmpty(ViewModel.RuntimeDisplay) &&
            (item.Year > 0 || !string.IsNullOrEmpty(item.ContentRating))
            ? Visibility.Visible : Visibility.Collapsed;

        OverviewText.Text = item.Overview ?? "";

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
        var isAdmin = authService.CurrentUser?.Role == "admin";
        MatchButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        RefreshMetadataButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        EditMetadataButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        BuildMediaLocationsSection(isAdmin, item.Versions);

        // Load backdrop
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();
        _ = LoadBackdropAsync(item, _imageCts.Token);

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
    }

    // ===== Scores Row =====

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
        var best = manager.SelectBestVersion(_watchDetail.Versions, userData: _watchDetail.UserData);
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

        // HDR badge
        if (best.Hdr)
        {
            QualityBadgesPanel.Children.Add(CreateQualityBadge(
                "HDR",
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

        // Watchlist toggle
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

        // Watched toggle
        var watchItem = new MenuFlyoutItem
        {
            Text = ViewModel.IsWatched ? "Mark Unwatched" : "Mark Watched",
            Icon = new FontIcon { Glyph = "\uE73E" },
        };
        watchItem.Click += async (_, _) =>
        {
            await ViewModel.ToggleWatchedCommand.ExecuteAsync(null);
            UpdateWatchedButton();
            BuildMoreFlyout();
        };
        MoreFlyout.Items.Add(watchItem);

        // Admin-only: Refresh Metadata
        var authService = App.Services.GetRequiredService<Core.Services.AuthService>();
        if (authService.CurrentUser?.Role == "admin" && ViewModel.Item != null)
        {
            MoreFlyout.Items.Add(new MenuFlyoutSeparator());
            var refreshItem = new MenuFlyoutItem
            {
                Text = "Refresh Metadata",
                Icon = new FontIcon { Glyph = "\uE72C" },
            };
            refreshItem.Click += async (_, _) =>
            {
                try
                {
                    var adminApi = App.Services.GetRequiredService<Core.Api.AdminApi>();
                    await adminApi.RefreshItemMetadataAsync(ViewModel.Item.ContentId);
                    var toast = App.Services.GetRequiredService<Services.ToastService>();
                    toast.Success("Metadata refresh queued");
                }
                catch (Exception ex)
                {
                    var toast = App.Services.GetRequiredService<Services.ToastService>();
                    toast.Error(ex.Message);
                }
            };
            MoreFlyout.Items.Add(refreshItem);
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

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedVersion == null) return;

        try
        {
            var downloadsApi = App.Services.GetRequiredService<DownloadsApi>();
            await downloadsApi.CreateDownloadAsync(new Core.Models.Downloads.DownloadRequest
            {
                MediaFileId = _selectedVersion.FileId
            });

            // Visual feedback: change button text briefly
            DownloadButton.IsEnabled = false;
            if (DownloadButton.Content is StackPanel sp && sp.Children.Count > 1
                && sp.Children[1] is TextBlock tb)
            {
                tb.Text = "Requested!";
                await Task.Delay(2000);
                tb.Text = "Download";
            }
            DownloadButton.IsEnabled = true;
        }
        catch
        {
            // Download request failure is non-fatal
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

        var titleBox = new TextBox { Text = item.Title, PlaceholderText = "Title", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var sortTitleBox = new TextBox { Text = item.SortTitle ?? "", PlaceholderText = "Sort title (optional)", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var originalTitleBox = new TextBox { Text = item.OriginalTitle ?? "", PlaceholderText = "Original title (optional)", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var taglineBox = new TextBox { Text = item.Tagline ?? "", PlaceholderText = "Tagline (optional)", CornerRadius = new CornerRadius(6), FontSize = 13 };
        var yearBox = new NumberBox
        {
            Value = item.Year > 0 ? item.Year : double.NaN,
            Minimum = 1850, Maximum = 2100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            CornerRadius = new CornerRadius(6), FontSize = 13,
        };
        var overviewBox = new TextBox
        {
            Text = item.Overview, PlaceholderText = "Overview",
            AcceptsReturn = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MinHeight = 120, MaxHeight = 220,
            CornerRadius = new CornerRadius(6), FontSize = 13,
        };
        var contentRatingBox = new TextBox { Text = item.ContentRating ?? "", PlaceholderText = "e.g. PG-13, TV-MA", CornerRadius = new CornerRadius(6), FontSize = 13 };

        var form = new StackPanel { Width = 520, Spacing = 14 };
        void AddField(string label, FrameworkElement control)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock
            {
                Text = label, FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"],
            });
            group.Children.Add(control);
            form.Children.Add(group);
        }
        AddField("Title", titleBox);
        AddField("Sort Title", sortTitleBox);
        AddField("Original Title", originalTitleBox);
        AddField("Tagline", taglineBox);
        AddField("Year", yearBox);
        AddField("Overview", overviewBox);
        AddField("Content Rating", contentRatingBox);

        var dialog = new ContentDialog
        {
            Title = item.Type == "series" ? "Edit Series Metadata" : "Edit Movie Metadata",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            Content = new ScrollViewer { Content = form, MaxHeight = 540 },
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try
            {
                EditMetadataButton.IsEnabled = false;
                var adminApi = App.Services.GetRequiredService<AdminApi>();
                var payload = new Dictionary<string, object?>
                {
                    ["title"] = titleBox.Text.Trim(),
                    ["sort_title"] = string.IsNullOrWhiteSpace(sortTitleBox.Text) ? null : sortTitleBox.Text.Trim(),
                    ["original_title"] = string.IsNullOrWhiteSpace(originalTitleBox.Text) ? null : originalTitleBox.Text.Trim(),
                    ["tagline"] = string.IsNullOrWhiteSpace(taglineBox.Text) ? null : taglineBox.Text.Trim(),
                    ["year"] = double.IsNaN(yearBox.Value) ? null : (int?)yearBox.Value,
                    ["overview"] = string.IsNullOrWhiteSpace(overviewBox.Text) ? null : overviewBox.Text.Trim(),
                    ["content_rating"] = string.IsNullOrWhiteSpace(contentRatingBox.Text) ? null : contentRatingBox.Text.Trim(),
                };
                await adminApi.UpdateItemMetadataAsync(item.ContentId, payload);

                // Reload to pick up the saved metadata.
                await ViewModel.LoadCommand.ExecuteAsync(item.ContentId);
                UpdateUI();
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = $"Failed to update metadata: {ex.Message}";
            }
            finally
            {
                EditMetadataButton.IsEnabled = true;
            }
        }
    }

    // ===== Media Locations (admin-only) =====
    //
    // Mirrors web/src/components/MediaLocations.tsx. Shows folder/filename
    // for each version with a quality summary header and a copy-full-path
    // button. Admin-only; hidden for regular users even if they somehow land
    // on this page with file_path populated.

    private void BuildMediaLocationsSection(bool isAdmin, List<ContinuumPlayer.Core.Models.Playback.FileVersion>? versions)
    {
        MediaLocationsPanel.Children.Clear();
        if (!isAdmin || versions == null || versions.Count == 0)
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
            MediaLocationsSection.Visibility = Visibility.Collapsed;
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

    private Border BuildMediaLocationRow(ContinuumPlayer.Core.Models.Playback.FileVersion version, int index)
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

    private static string? BuildVersionQualitySummary(ContinuumPlayer.Core.Models.Playback.FileVersion v)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(v.Resolution)) parts.Add(v.Resolution);
        if (v.Hdr) parts.Add("HDR");
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

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item == null) return;

        // For series, play the first unwatched or in-progress episode if available
        if (ViewModel.IsSeries && ViewModel.Episodes.Count > 0)
        {
            var episode = ViewModel.Episodes.FirstOrDefault(ep => ep.UserData?.Played != true)
                          ?? ViewModel.Episodes[0];
            NavigateToPlayer(episode.ContentId);
        }
        else
        {
            // For movies, play the item directly
            NavigateToPlayer(ViewModel.Item.ContentId);
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

        // For series, restart the current/next episode from position 0
        // (same episode selection as the Play button)
        if (ViewModel.IsSeries && ViewModel.Episodes.Count > 0)
        {
            var episode = ViewModel.Episodes.FirstOrDefault(ep => ep.UserData?.Played != true)
                          ?? ViewModel.Episodes[0];
            NavigateToPlayer(episode.ContentId, fromStart: true);
        }
        else
        {
            NavigateToPlayer(ViewModel.Item.ContentId, fromStart: true);
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
        // Format the title the way the overlay shows it: "S2 E5 · Episode Name"
        var label = $"S{next.SeasonNumber} E{next.EpisodeNumber}";
        playerService.NextEpisodeTitle = string.IsNullOrEmpty(next.Title) ? label : $"{label} \u00B7 {next.Title}";
        playerService.NextEpisodeSeriesTitle = ViewModel.Item?.Title;
        playerService.NextEpisodePosterUrl = next.StillUrl;
        playerService.NextEpisodeOverview = next.Overview;
    }

    // ===== Initial Play Button & Quality Badges from Catalog Item Data =====

    /// <summary>
    /// Sets the play button text using catalog item data (before watch detail loads).
    /// Shows "Resume from X:XX" if in-progress, and quality like "· 2160p HDR".
    /// </summary>
    private void UpdatePlayButtonFromItemData(MediaItemDetail item)
    {
        // Show quality on play button from OverlaySummary or Versions
        var qualityParts = new List<string>();
        if (item.OverlaySummary != null && !string.IsNullOrEmpty(item.OverlaySummary.Resolution))
            qualityParts.Add(item.OverlaySummary.Resolution);
        if (item.Versions?.Count > 0)
        {
            var best = item.Versions.OrderByDescending(v => v.Resolution switch {
                "2160p" => 4, "1080p" => 3, "720p" => 2, _ => 1
            }).First();
            if (qualityParts.Count == 0 && !string.IsNullOrEmpty(best.Resolution))
                qualityParts.Add(best.Resolution);
            if (best.Hdr) qualityParts.Add("HDR");
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
            BuildVersionFlyout(item.Versions ?? [], isResuming: true);
        }
        else if (item.Versions?.Count > 1)
        {
            // Multiple versions available — show version picker
            BuildVersionFlyout(item.Versions, isResuming: false);
        }
    }

    /// <summary>
    /// Shows quality badges from the catalog item's OverlaySummary, Versions, or UserData
    /// before the watch detail response is available.
    /// </summary>
    private void UpdateQualityBadgesFromItemData(MediaItemDetail item)
    {
        QualityBadgesPanel.Children.Clear();

        // Prefer OverlaySummary (available immediately from item detail)
        if (item.OverlaySummary != null)
        {
            // Resolution badge (e.g., "2160p", "1080p")
            if (!string.IsNullOrEmpty(item.OverlaySummary.Resolution))
            {
                QualityBadgesPanel.Children.Add(CreateQualityBadge(
                    item.OverlaySummary.Resolution,
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeResolutionBrush"]));
            }

            // Audio badge (e.g., "Atmos", "DTS-HD MA")
            if (!string.IsNullOrEmpty(item.OverlaySummary.Audio))
            {
                QualityBadgesPanel.Children.Add(CreateQualityBadge(
                    item.OverlaySummary.Audio,
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeBackgroundBrush"],
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeTextBrush"]));
            }
        }

        // Check Versions for HDR info
        if (item.Versions?.Count > 0)
        {
            var bestVersion = item.Versions.OrderByDescending(v => v.Resolution switch {
                "2160p" => 4, "1080p" => 3, "720p" => 2, _ => 1
            }).First();

            if (bestVersion.Hdr)
            {
                QualityBadgesPanel.Children.Add(CreateQualityBadge(
                    "HDR",
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeHdrBrush"]));
            }

            // If no OverlaySummary, fall back to version resolution
            if (item.OverlaySummary == null && !string.IsNullOrEmpty(bestVersion.Resolution))
            {
                QualityBadgesPanel.Children.Insert(0, CreateQualityBadge(
                    bestVersion.Resolution,
                    (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeResolutionBrush"]));
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
            if (_selectedVersion != null)
                DownloadButton.Visibility = Visibility.Visible;

            // Load subtitles for the best version
            if (_selectedVersion != null)
                _ = LoadSubtitlesSectionAsync(_selectedVersion.FileId);
        }
        catch (Exception ex)
        {
            // Log the error so we can debug
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ContinuumPlayer", "watch_detail_error.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now}] LoadWatchDetailAsync failed for contentId={contentId}: {ex}\n\n");
            }
            catch { }
        }
    }

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
            var best = manager.SelectBestVersion(versions, userData: userData);
            _selectedVersion = best;
            BuildAudioTracksFlyout(best);
            BuildSubtitlesPopoverFlyout(best);

            bool hasQualitySummary = false;
            if (best != null)
            {
                var qualityParts = new List<string>();
                if (!string.IsNullOrEmpty(best.Resolution))
                    qualityParts.Add(best.Resolution);
                if (best.Hdr)
                    qualityParts.Add("HDR");

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
                BuildVersionFlyout(versions, isResuming);
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
        var subs = version?.SubtitleTracks;
        // Show the button as long as there's at least one embedded sub — the
        // "Off" option is still useful even with a single track.
        if (subs == null || subs.Count == 0)
        {
            SubtitlesPopoverButton.Visibility = Visibility.Collapsed;
            _selectedSubtitleIndex = null;
            return;
        }

        SubtitlesPopoverButton.Visibility = Visibility.Visible;
        _selectedSubtitleIndex = null; // Reset on version change

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

        // Search online… opens the full SubtitleSearchDialog so the user can
        // pick provider/language/release manually (complements the on-the-fly
        // auto-search triggered from the player OSC).
        SubtitlesPopoverFlyout.Items.Add(new MenuFlyoutSeparator());
        var searchItem = new MenuFlyoutItem { Text = "Search online\u2026" };
        searchItem.Click += async (_, _) => await OpenSubtitleSearchDialogAsync();
        SubtitlesPopoverFlyout.Items.Add(searchItem);

        UpdateSubtitlesPopoverSummary(subs);
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

    private void BuildVersionFlyout(List<FileVersion> versions, bool isResuming)
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
                    BuildAudioTracksFlyout(fileVersion);
                    BuildSubtitlesPopoverFlyout(fileVersion);

                    // If already playing, switch version mid-playback
                    var playerService = App.Services.GetRequiredService<Services.PlayerService>();
                    if (playerService.State != Services.PlayerState.Idle)
                    {
                        _ = playerService.SwitchVersionAsync(fileVersion);
                    }
                    else
                    {
                        // Start playback with this specific version
                        var playContentId = ViewModel.Item?.ContentId;
                        if (ViewModel.IsSeries && ViewModel.Episodes.Count > 0)
                        {
                            var episode = ViewModel.Episodes.FirstOrDefault(ep => ep.UserData?.Played != true)
                                          ?? ViewModel.Episodes[0];
                            playContentId = episode.ContentId;
                        }
                        if (playContentId != null)
                            NavigateToPlayer(playContentId, fileId: fileVersion.FileId);
                    }
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
        if (version.Hdr)
            parts.Add("HDR");
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

    private void BuildHeroCrewLine(List<ContinuumPlayer.Core.Models.Catalog.CrewMember> crew)
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

    private async Task LoadSeasonEpisodesAsync(string seriesId, int seasonNumber)
    {
        if (seasonNumber <= 0) return;
        try
        {
            var catalogApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.CatalogApi>();
            var response = await catalogApi.GetEpisodesAsync(seriesId, seasonNumber);
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
            var catalogApi = App.Services.GetRequiredService<ContinuumPlayer.Core.Api.CatalogApi>();
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
                var mediaItem = new ContinuumPlayer.Core.Models.Home.MediaItem
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
        EpisodesHeader.Text = $"Season {ViewModel.SelectedSeasonNumber} Episodes";

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
            if (file.Hdr) label += " HDR";
            var badge = new Border
            {
                Background = file.Hdr
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
                DecodePixelWidth = 200,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            var image = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                Width = 160,
                Height = 90,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
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
