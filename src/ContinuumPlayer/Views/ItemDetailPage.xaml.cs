using Microsoft.Extensions.DependencyInjection;
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

            UpdateUI();

            // Load rating (non-blocking)
            _ = ViewModel.LoadRatingCommand.ExecuteAsync(null);

            // Load similar items (non-blocking)
            _ = LoadSimilarItemsAsync();

            if (ViewModel.IsSeries)
            {
                SeasonsSection.Visibility = Visibility.Visible;
                SeasonsLoadingRing.IsActive = true;
                SeasonsLoadingRing.Visibility = Visibility.Visible;

                await ViewModel.LoadSeasonsCommand.ExecuteAsync(null);

                SeasonsLoadingRing.IsActive = false;
                SeasonsLoadingRing.Visibility = Visibility.Collapsed;

                BuildSeasonCards();
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
                episode.Studios = series.Studios;
            if ((episode.Networks?.Count ?? 0) == 0 && (series.Networks?.Count ?? 0) > 0)
                episode.Networks = series.Networks;
            if ((episode.Countries?.Count ?? 0) == 0 && (series.Countries?.Count ?? 0) > 0)
                episode.Countries = series.Countries;
            if ((episode.Genres?.Count ?? 0) == 0 && (series.Genres?.Count ?? 0) > 0)
                episode.Genres = series.Genres;
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

        // Episode context: show series title and S##E## above/below episode title
        if (item.Type == "episode")
        {
            // Show series title as a subtitle/breadcrumb
            if (!string.IsNullOrEmpty(item.SeriesTitle))
            {
                TaglineText.Text = item.SeriesTitle;
                TaglineText.Visibility = Visibility.Visible;
            }

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

        // Genres line below overview: "Crime · Drama · History"
        if (item.Genres?.Count > 0)
        {
            GenresText.Text = string.Join(" \u00B7 ", item.Genres);
            GenresText.Visibility = Visibility.Visible;
        }
        else
        {
            GenresText.Visibility = Visibility.Collapsed;
        }

        UpdateScoresRow(item);
        UpdateWatchedButton();
        UpdateFavoriteButton();
        UpdateWatchlistButton();
        UpdateStarRating();

        // Set initial play button text from catalog item user data (before watch detail loads)
        UpdatePlayButtonFromItemData(item);

        // Set initial quality badges from catalog item user data (before watch detail loads)
        UpdateQualityBadgesFromItemData(item);

        // Show Match button for admin users
        var authService = App.Services.GetRequiredService<AuthService>();
        MatchButton.Visibility = authService.CurrentUser?.Role == "admin"
            ? Visibility.Visible : Visibility.Collapsed;

        // Load backdrop
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();
        _ = LoadBackdropAsync(item, _imageCts.Token);

        // Build cast
        BuildCast(item.Cast ?? new());

        // Build crew (directors + writers)
        BuildCrew(item.Crew ?? new());

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
        var best = manager.SelectBestVersion(_watchDetail.Versions);
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

    private void UpdateStarRating()
    {
        var rating = ViewModel.UserRating;
        FontIcon[] stars = [Star1Icon, Star2Icon, Star3Icon, Star4Icon, Star5Icon];

        for (int i = 0; i < 5; i++)
        {
            bool filled = rating != null && (i + 1) <= rating;
            stars[i].Glyph = filled ? "\uE735" : "\uE734"; // FavoriteStar vs FavoriteStarFill -- actually E735=filled, E734=outline
            stars[i].Foreground = filled
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"];
        }
    }

    private async void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int rating))
        {
            await ViewModel.SetRatingCommand.ExecuteAsync(rating);
            UpdateStarRating();
        }
    }

    // ===== Favorite & Watchlist =====

    private void UpdateFavoriteButton()
    {
        FavoriteIcon.Glyph = ViewModel.IsFavorite ? "\uE735" : "\uE734";
        FavoriteIcon.Foreground = ViewModel.IsFavorite
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed)
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
        ToolTipService.SetToolTip(FavoriteButton, ViewModel.IsFavorite ? "Unfavorite" : "Favorite");
    }

    private void UpdateWatchlistButton()
    {
        WatchlistIcon.Glyph = ViewModel.InWatchlist ? "\uE73E" : "\uE8B7";
        WatchlistText.Text = ViewModel.InWatchlist ? "In Watchlist" : "Watchlist";
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

        var dialog = new MatchItemDialog(item.ContentId)
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
        // Close any existing playback before starting new
        if (playerService.State != Services.PlayerState.Idle)
        {
            await playerService.CloseAsync();
            await Task.Delay(300); // Let server process the session stop
        }
        _ = playerService.PlayAsync(contentId, fromStart: fromStart, fileId: fileId);
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
        if (qualityParts.Count > 0)
        {
            PlayQualityText.Text = $"\u00B7 {string.Join(" ", qualityParts)}";
            PlayQualityText.Visibility = Visibility.Visible;
        }

        // Show version dropdown if multiple versions
        if (item.Versions?.Count > 1)
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
            var best = manager.SelectBestVersion(versions);
            _selectedVersion = best;

            if (best != null)
            {
                var qualityParts = new List<string>();
                if (!string.IsNullOrEmpty(best.Resolution))
                    qualityParts.Add(best.Resolution);
                if (best.Hdr)
                    qualityParts.Add("HDR");

                if (qualityParts.Count > 0)
                {
                    PlayQualityText.Text = $"\u00B7 {string.Join(" ", qualityParts)}";
                    PlayQualityText.Visibility = Visibility.Visible;
                }
            }

            // Show version dropdown if multiple versions OR if resuming (for "Play from Start")
            if (versions.Count > 1 || isResuming)
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

        // Version options (only show if multiple)
        if (versions.Count > 1)
        {
            foreach (var version in versions)
            {
                var label = version.Resolution;
                if (!string.IsNullOrEmpty(version.CodecVideo))
                    label += $" {version.CodecVideo.ToUpperInvariant()}";
                if (version.Hdr)
                    label += " HDR";
                if (version.Bitrate > 0)
                    label += $" ({version.Bitrate / 1000}Mbps)";

                var item = new MenuFlyoutItem { Text = label };
                var fileVersion = version;
                item.Click += (_, _) =>
                {
                    _selectedVersion = fileVersion;

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
            }
            catch
            {
                BackdropImage.Source = null;
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

        foreach (var member in cast.Take(20))
        {
            var card = new StackPanel
            {
                Width = 100,
                Spacing = 4
            };

            // Photo placeholder (circle)
            var photoBorder = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(32),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var photoIcon = new FontIcon
            {
                Glyph = "\uE77B",
                FontSize = 24,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            photoBorder.Child = photoIcon;

            // Load photo if available
            if (!string.IsNullOrEmpty(member.PhotoUrl))
            {
                _ = LoadCastPhotoAsync(photoBorder, member);
            }

            card.Children.Add(photoBorder);

            card.Children.Add(new TextBlock
            {
                Text = member.Name,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            if (!string.IsNullOrEmpty(member.Character))
            {
                card.Children.Add(new TextBlock
                {
                    Text = member.Character,
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"],
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
        if (sender is FrameworkElement fe && fe.Tag is string personIdStr
            && int.TryParse(personIdStr, out int personId) && personId > 0)
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

        if (crew.Count == 0)
        {
            CrewSection.Visibility = Visibility.Collapsed;
            return;
        }

        var directors = crew.Where(c =>
            c.Job.Equals("Director", StringComparison.OrdinalIgnoreCase)).ToList();
        var writers = crew.Where(c =>
            c.Job.Equals("Writer", StringComparison.OrdinalIgnoreCase)
            || c.Job.Equals("Screenplay", StringComparison.OrdinalIgnoreCase)
            || c.Job.Equals("Story", StringComparison.OrdinalIgnoreCase)).ToList();

        bool hasDirectors = directors.Count > 0;
        bool hasWriters = writers.Count > 0;

        if (!hasDirectors && !hasWriters)
        {
            CrewSection.Visibility = Visibility.Collapsed;
            return;
        }

        CrewSection.Visibility = Visibility.Visible;

        if (hasDirectors)
        {
            DirectorsPanel.Visibility = Visibility.Visible;
            BuildCrewNameLinks(DirectorNamesPanel, directors);
        }
        else
        {
            DirectorsPanel.Visibility = Visibility.Collapsed;
        }

        if (hasWriters)
        {
            WritersPanel.Visibility = Visibility.Visible;
            BuildCrewNameLinks(WriterNamesPanel, writers);
        }
        else
        {
            WritersPanel.Visibility = Visibility.Collapsed;
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

            if (!string.IsNullOrEmpty(member.PersonId) && int.TryParse(member.PersonId, out int personId) && personId > 0)
            {
                var id = personId;
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
                    Text = ",  ",
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
            Children = { posterBorder, titleText, progressText }
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

    // ===== Series: Episode Rows =====

    private void BuildEpisodeRows()
    {
        EpisodesPanel.Children.Clear();

        if (ViewModel.Episodes.Count == 0)
        {
            EpisodesSection.Visibility = Visibility.Collapsed;
            return;
        }

        EpisodesSection.Visibility = Visibility.Visible;
        EpisodesHeader.Text = $"Season {ViewModel.SelectedSeasonNumber} Episodes";

        foreach (var episode in ViewModel.Episodes)
        {
            var row = CreateEpisodeRow(episode);
            EpisodesPanel.Children.Add(row);
        }
    }

    private Border CreateEpisodeRow(Episode episode)
    {
        // Still image (160x90, 16:9)
        var stillBorder = new Border
        {
            Width = 160,
            Height = 90,
            CornerRadius = new CornerRadius(4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"]
        };

        var stillPlaceholder = new FontIcon
        {
            Glyph = "\uE714", // Video icon
            FontSize = 24,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stillBorder.Child = stillPlaceholder;

        if (!string.IsNullOrEmpty(episode.StillUrl))
        {
            _ = LoadEpisodeStillAsync(stillBorder, episode);
        }

        // Progress overlay on still image
        if (episode.UserData != null && episode.UserData.DurationSeconds > 0
            && episode.UserData.PositionSeconds > 0 && !episode.UserData.Played)
        {
            var progressGrid = new Grid();
            progressGrid.Children.Add(stillBorder);

            var progressFraction = episode.UserData.PositionSeconds / episode.UserData.DurationSeconds;
            var progressBar = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Width = 160 * Math.Min(progressFraction, 1.0),
                Margin = new Thickness(0, 0, 0, 0)
            };
            progressGrid.Children.Add(progressBar);

            // Wrap in the same dimensions
            var progressContainer = new Border
            {
                Width = 160,
                Height = 90,
                CornerRadius = new CornerRadius(4),
                Child = progressGrid
            };

            return BuildEpisodeRowContent(progressContainer, episode);
        }

        return BuildEpisodeRowContent(stillBorder, episode);
    }

    private Border BuildEpisodeRowContent(FrameworkElement stillElement, Episode episode)
    {
        // Title line: E1 . "Title" . 42 min
        var runtimeStr = episode.Runtime > 0 ? $"{episode.Runtime} min" : "";
        var titleLine = $"E{episode.EpisodeNumber} \u00B7 {episode.Title}";
        if (!string.IsNullOrEmpty(runtimeStr))
            titleLine += $" \u00B7 {runtimeStr}";

        var titleText = new TextBlock
        {
            Text = titleLine,
            Style = (Style)Application.Current.Resources["SubtitleTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };

        // Overview (truncated to 2 lines)
        var overviewText = new TextBlock
        {
            Text = episode.Overview ?? "",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 18
        };

        // File quality badges
        var badgesPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 4, 0, 0)
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
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                }
            };
            badgesPanel.Children.Add(badge);
        }

        // Watched indicator
        if (episode.UserData?.Played == true)
        {
            var watchedBadge = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeBackgroundBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock
                {
                    Text = "Watched",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BadgeTextBrush"]
                }
            };
            badgesPanel.Children.Add(watchedBadge);
        }

        // Text content stack
        var textContent = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleText, overviewText, badgesPanel }
        };

        // Main row grid: [Still 160px] [Text content fills rest]
        var rowGrid = new Grid
        {
            ColumnSpacing = 16
        };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(stillElement, 0);
        Grid.SetColumn(textContent, 1);

        rowGrid.Children.Add(stillElement);
        rowGrid.Children.Add(textContent);

        var rowBorder = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(12),
            Child = rowGrid,
            Tag = episode.ContentId
        };

        rowBorder.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceHoverBrush"];
        };

        rowBorder.PointerExited += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"];
        };

        rowBorder.Tapped += EpisodeRow_Tapped;

        return rowBorder;
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
