using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class ItemDetailPage : Page
{
    public ItemDetailViewModel ViewModel { get; }
    private CancellationTokenSource? _imageCts;
    private int _highlightedSeasonNumber;

    public ItemDetailPage()
    {
        ViewModel = App.Services.GetRequiredService<ItemDetailViewModel>();
        this.InitializeComponent();
        SmoothScrollHelper.Attach(ContentScroll);

        this.Loaded += (_, _) =>
        {
            UpdateBackdropHeight();
            if (XamlRoot?.Content is FrameworkElement root)
                root.SizeChanged += (_, _) => UpdateBackdropHeight();
        };
    }

    private void UpdateBackdropHeight()
    {
        // Match web UI: min-h-[60dvh] -- 60% of viewport height
        if (XamlRoot?.Content is FrameworkElement root && root.ActualHeight > 0)
            BackdropContainer.Height = Math.Max(300, root.ActualHeight * 0.60);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string contentId && !string.IsNullOrEmpty(contentId))
        {
            await ViewModel.LoadCommand.ExecuteAsync(contentId);
            UpdateUI();

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
            }
        }
    }

    private void UpdateUI()
    {
        var item = ViewModel.Item;
        if (item == null) return;

        TitleText.Text = item.Title;
        TaglineText.Text = item.Tagline ?? "";
        TaglineText.Visibility = string.IsNullOrEmpty(item.Tagline)
            ? Visibility.Collapsed : Visibility.Visible;

        YearText.Text = item.Year > 0 ? item.Year.ToString() : "";
        ContentRatingText.Text = item.ContentRating ?? "";
        RuntimeText.Text = ViewModel.RuntimeDisplay;
        RatingText.Text = ViewModel.RatingDisplay != ""
            ? $"TMDB: {ViewModel.RatingDisplay}" : "";
        GenresText.Text = ViewModel.GenresDisplay;

        OverviewText.Text = item.Overview;

        UpdateFavoriteButton();
        UpdateWatchlistButton();

        // Load backdrop
        _imageCts?.Cancel();
        _imageCts = new CancellationTokenSource();
        _ = LoadBackdropAsync(item, _imageCts.Token);

        // Build cast
        BuildCast(item.Cast);
    }

    private void UpdateFavoriteButton()
    {
        FavoriteIcon.Glyph = ViewModel.IsFavorite ? "\uE735" : "\uE734";
        FavoriteText.Text = ViewModel.IsFavorite ? "Favorited" : "Favorite";
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

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.CanGoBack)
            nav.GoBack();
    }

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

            CastPanel.Children.Add(card);
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
            Child = rowGrid
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
