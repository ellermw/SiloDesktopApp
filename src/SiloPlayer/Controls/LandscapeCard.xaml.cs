using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public sealed partial class LandscapeCard : UserControl
{
    private double _cardWidth = 315;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _playbackPrefetchCts;
    private bool _isPointerOver;
    private bool _isKeyboardFocusWithin;
    private bool _quickActionPending;
    private MediaItem? _artworkItem;
    public static readonly DependencyProperty MediaItemProperty =
        DependencyProperty.Register(
            nameof(MediaItem),
            typeof(MediaItem),
            typeof(LandscapeCard),
            new PropertyMetadata(null, OnMediaItemChanged));

    public MediaItem? MediaItem
    {
        get => (MediaItem?)GetValue(MediaItemProperty);
        set => SetValue(MediaItemProperty, value);
    }

    public static readonly DependencyProperty UsePosterAspectProperty =
        DependencyProperty.Register(
            nameof(UsePosterAspect),
            typeof(bool),
            typeof(LandscapeCard),
            new PropertyMetadata(false, OnUsePosterAspectChanged));

    public bool UsePosterAspect
    {
        get => (bool)GetValue(UsePosterAspectProperty);
        set => SetValue(UsePosterAspectProperty, value);
    }

    public LandscapeCard()
    {
        this.InitializeComponent();
        this.Loaded += (_, _) =>
        {
            ObserveArtwork(MediaItem);
            // HomePage is navigation-cached. Unloaded releases decoded artwork,
            // but the existing card and its MediaItem are reused when Home is
            // revisited, so the dependency property does not change again.
            if (_loadCts == null && BackdropImage.Source == null && MediaItem is { } item)
            {
                _loadCts = new CancellationTokenSource();
                _ = LoadImageAsync(item, _loadCts.Token);
            }
        };
        this.Unloaded += (_, _) =>
        {
            // WinUI can deliver a queued Unloaded after this card has already
            // been reattached and received Loaded (detail-section reordering).
            // That stale event must not cancel the current artwork load.
            if (IsLoaded) return;
            ObserveArtwork(null);

            try { _loadCts?.Cancel(); } catch { }
            _loadCts?.Dispose();
            _loadCts = null;
            try { _playbackPrefetchCts?.Cancel(); } catch { }
            _playbackPrefetchCts?.Dispose();
            _playbackPrefetchCts = null;
            BackdropImage.Source = null;
        };
    }

    public void SetCardWidth(double width)
    {
        _cardWidth = Math.Max(UsePosterAspect ? 96 : 180, width);
        Width = _cardWidth;
        RootGrid.Width = _cardWidth;
        var imageHeight = UsePosterAspect
            ? MediaItem?.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase) == true
                ? _cardWidth
                : _cardWidth * 1.5d
            : _cardWidth * 9d / 16d;
        RootGrid.RowDefinitions[0].Height = new GridLength(imageHeight);
        DismissButton.Width = DismissButton.Height = UsePosterAspect ? 32 : 36;
        DismissButton.Margin = UsePosterAspect
            ? new Thickness(0, 0, 10, 10)
            : new Thickness(0, 0, 12, 12);

        if (MediaItem is { } item)
            UpdateProgressState(item);
    }

    private static void OnMediaItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not LandscapeCard card) return;
        card.ObserveArtwork(card.IsLoaded ? e.NewValue as MediaItem : null);
        if (e.NewValue is MediaItem item) card.UpdateContent(item);
    }

    private void ObserveArtwork(MediaItem? item)
    {
        if (ReferenceEquals(_artworkItem, item)) return;
        if (_artworkItem != null) _artworkItem.ArtworkUrlsChanged -= OnArtworkUrlsChanged;
        _artworkItem = item;
        if (_artworkItem != null) _artworkItem.ArtworkUrlsChanged += OnArtworkUrlsChanged;
    }

    private void OnArtworkUrlsChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || !ReferenceEquals(sender, MediaItem) || BackdropImage.Source != null) return;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = LoadImageAsync(MediaItem!, _loadCts.Token);
    }

    private static void OnUsePosterAspectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LandscapeCard card && card.RootGrid != null)
        {
            card.SetCardWidth(card._cardWidth);
            if (card.MediaItem != null)
                card.UpdateContent(card.MediaItem);
        }
    }

    private void UpdateContent(MediaItem item)
    {
        SetCardWidth(_cardWidth);
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        // F10: attach right-click media menu. LandscapeCard is used for
        // Continue Watching / Next Up sections, so surface-specific Dismiss
        // entries show up automatically based on ItemSource.
        var surface = item.ItemSource switch
        {
            "continue_watching" => MediaItemMenu.Surface.ContinueWatching,
            "next_up" => MediaItemMenu.Surface.NextUp,
            _ => MediaItemMenu.Surface.Default,
        };
        this.ContextFlyout = MediaItemMenu.Build(
            item,
            surface,
            showCollectionActions: item.ItemSource != "episode_carousel",
            stateChanged: () => RefreshMenuState(item));
        UpdateQuickWatchedState(item);

        // Show dismiss X button for CW/NU cards — enables quick-dismiss
        // from the home screen without opening a context menu.
        UpdateDismissVisibility();
        DismissButton.Opacity = 0; // starts invisible, fades in on hover
        HoverPlayIcon.Glyph = item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase)
            ? "\uE736"
            : "\uE768";
        var displayTitle = MediaItemDisplayText.BuildTitle(item);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, displayTitle);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            HoverPlayButton,
            item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase)
                ? $"Read {displayTitle}"
                : $"Play {displayTitle}");
        var isEpisodeCarousel = item.ItemSource == "episode_carousel";
        HoverPlayButton.Visibility = isEpisodeCarousel ? Visibility.Collapsed : Visibility.Visible;
        CurrentItemBadge.Visibility = isEpisodeCarousel &&
            item.Badges?.Contains("now_viewing", StringComparer.OrdinalIgnoreCase) == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        CurrentItemBorder.Visibility = CurrentItemBadge.Visibility;
        EpisodeWatchedInline.Visibility = isEpisodeCarousel && item.UserState?.Played == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        // B31: Match the webui ContinueWatchingCard hierarchy. For episodes,
        // the series title is the primary heading and the episode context
        // ("Season 1 Episode 1 · Pilot") is the secondary line. Movies fall
        // through to title-as-heading with no subtitle.
        bool hasEpisodeMeta = item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue;
        if (item.ItemSource == "episode_carousel")
        {
            TitleText.Text = item.Title;
            var metadata = item.EpisodeNumber.HasValue ? $"Episode {item.EpisodeNumber}" : "Episode";
            if (item.Runtime > 0) metadata += $" · {item.Runtime}m";
            SubtitleText.Text = metadata;
            SubtitleText.Visibility = Visibility.Visible;
            ConfigureEpisodeCarouselText(item);
        }
        else if (hasEpisodeMeta && !string.IsNullOrEmpty(item.SeriesTitle))
        {
            ResetTextPresentation();
            TitleText.Text = item.SeriesTitle!;
            string epLine = $"Season {item.SeasonNumber} Episode {item.EpisodeNumber}";
            if (!string.IsNullOrEmpty(item.Title) && item.Title != item.SeriesTitle)
                epLine += $" \u2022 {item.Title}";
            SubtitleText.Text = epLine;
            SubtitleText.Visibility = Visibility.Visible;
        }
        else
        {
            ResetTextPresentation();
            TitleText.Text = item.Title;
            SubtitleText.Visibility = Visibility.Collapsed;
        }

        // Badge pill (e.g. "SEASON PREMIERE") — mirrors WebUI ContinueWatchingCard.
        // Server attaches badge strings to section items; we render the first known one.
        var badgeLabel = isEpisodeCarousel ? null : GetBadgeLabel(item);
        if (badgeLabel != null)
        {
            BadgeText.Text = badgeLabel;
            BadgePill.Visibility = Visibility.Visible;
        }
        else
        {
            BadgePill.Visibility = Visibility.Collapsed;
        }

        var isNextUp = item.ItemSource == "next_up";
        if (isNextUp)
        {
            TimeLeftText.Text = badgeLabel == null ? "Next Episode" : "";
            TimeLeftText.Visibility = badgeLabel == null ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (!isEpisodeCarousel && item.PositionSeconds.HasValue && item.DurationSeconds.HasValue && item.DurationSeconds.Value > 0)
        {
            var remainingMinutes = Math.Max(0, (int)Math.Round(
                (item.DurationSeconds.Value - item.PositionSeconds.Value) / 60));
            TimeLeftText.Text = item.Type == "ebook"
                ? $"{Math.Round(Math.Clamp(item.PositionSeconds.Value / item.DurationSeconds.Value, 0, 1) * 100)}% read"
                : $"{remainingMinutes} min left";
            TimeLeftText.Visibility = Visibility.Visible;
        }
        else if (!isEpisodeCarousel)
        {
            TimeLeftText.Visibility = Visibility.Collapsed;
        }

        UpdateProgressState(item);
        RemainingBadge.Visibility = Visibility.Collapsed;

        SubtitleButton.Visibility = SubtitleText.Visibility;
        TimeLeftButton.Visibility = TimeLeftText.Visibility;
        SubtitleText.Visibility = Visibility.Visible;
        TimeLeftText.Visibility = Visibility.Visible;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HeadingButton, $"Open {TitleText.Text}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SubtitleButton, $"Open {SubtitleText.Text}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TimeLeftButton, $"Open {TimeLeftText.Text}");

        UpdateBadges(item);
        var overlayService = App.Services.GetRequiredService<Services.CardOverlayService>();
        if (!overlayService.IsLoaded)
            _ = EnsureBadgesLoadedAsync(item, ct);

        NoImageText.Visibility = Visibility.Visible;
        BackdropImage.Opacity = 0;

        _ = LoadImageAsync(item, ct);
    }

    private void ConfigureEpisodeCarouselText(MediaItem item)
    {
        var isCurrent = item.Badges?.Contains("now_viewing", StringComparer.OrdinalIgnoreCase) == true;
        TitleText.Text = item.EpisodeNumber.HasValue ? $"Episode {item.EpisodeNumber}" : "Episode";
        TitleText.FontSize = 12;
        TitleText.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        TitleText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
        SubtitleText.Text = string.IsNullOrWhiteSpace(item.Title) ? TitleText.Text : item.Title;
        SubtitleText.FontSize = 14;
        SubtitleText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        SubtitleText.Foreground = isCurrent
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
        SubtitleText.Visibility = Visibility.Visible;
        TimeLeftText.Text = item.Runtime > 0 ? $"{item.Runtime}m" : "";
        TimeLeftText.Visibility = item.Runtime > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetTextPresentation()
    {
        TitleText.FontSize = 13;
        TitleText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        TitleText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
        SubtitleText.FontSize = 12;
        SubtitleText.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        SubtitleText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
        TimeLeftText.FontSize = 12;
        TimeLeftText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
    }

    private void RefreshMenuState(MediaItem item)
    {
        if (!ReferenceEquals(MediaItem, item)) return;
        var surface = item.ItemSource switch
        {
            "continue_watching" => MediaItemMenu.Surface.ContinueWatching,
            "next_up" => MediaItemMenu.Surface.NextUp,
            _ => MediaItemMenu.Surface.Default,
        };
        ContextFlyout = MediaItemMenu.Build(
            item,
            surface,
            showCollectionActions: item.ItemSource != "episode_carousel",
            stateChanged: () => RefreshMenuState(item));
        UpdateQuickWatchedState(item);
        EpisodeWatchedInline.Visibility = item.ItemSource == "episode_carousel" && item.UserState?.Played == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateProgressState(item);
    }

    private void UpdateProgressState(MediaItem item)
    {
        var hasPartialProgress = item.ItemSource != "next_up"
            && item.UserState?.Played != true
            && item.PositionSeconds is > 0
            && item.DurationSeconds is > 0;
        if (!hasPartialProgress)
        {
            ProgressContainer.Visibility = Visibility.Collapsed;
            return;
        }

        var progress = item.PositionSeconds!.Value / item.DurationSeconds!.Value;
        var layout = MediaCardProgressGeometry.Calculate(
            _cardWidth,
            progress,
            episodeCard: item.ItemSource == "episode_carousel");
        ProgressContainer.Margin = new Thickness(
            layout.HorizontalInset,
            0,
            layout.HorizontalInset,
            layout.BottomInset);
        ProgressFill.Width = layout.FillWidth;
        ProgressContainer.Visibility = Visibility.Visible;
    }

    private async Task EnsureBadgesLoadedAsync(MediaItem item, CancellationToken ct)
    {
        try
        {
            var service = App.Services.GetRequiredService<Services.CardOverlayService>();
            await service.EnsureLoadedAsync(ct);
            if (!ct.IsCancellationRequested && ReferenceEquals(MediaItem, item))
                UpdateBadges(item);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { }
    }

    private void UpdateBadges(MediaItem item)
    {
        OverlayTopLeft.Children.Clear();
        OverlayTopRight.Children.Clear();
        OverlayBottomLeft.Children.Clear();
        OverlayBottomRight.Children.Clear();

        var service = App.Services.GetRequiredService<Services.CardOverlayService>();
        var prefs = service.GetPrefs();
        if (prefs == null) return;

        var data = Services.OverlayData.FromMediaItem(item);
        var cornerCounts = new Dictionary<Services.OverlayPosition, int>();
        foreach (var definition in service.GetOrderedDefinitions())
        {
            if (!prefs.TryGetValue(definition.Id, out var config) || !config.Enabled) continue;
            if (Services.OverlayRegistry.SuppressesStandaloneOverlays(definition.Id, prefs)) continue;
            var value = definition.GetValue(data);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (cornerCounts.GetValueOrDefault(config.Position) >= 3) continue;
            var badge = PosterCard.BuildBadge(value, definition.Id, config, service.Preset);
            var host = config.Position switch
            {
                Services.OverlayPosition.TopLeft => OverlayTopLeft,
                Services.OverlayPosition.TopRight => OverlayTopRight,
                Services.OverlayPosition.BottomLeft => OverlayBottomLeft,
                Services.OverlayPosition.BottomRight => OverlayBottomRight,
                _ => OverlayTopLeft,
            };
            host.Children.Add(badge);
            cornerCounts[config.Position] = cornerCounts.GetValueOrDefault(config.Position) + 1;
        }
    }

    private async Task LoadImageAsync(MediaItem item, CancellationToken ct)
    {
        // Current WebUI wide cards use backdrop_url first. Section episode
        // payloads now reserve poster_url for vertical series/season artwork
        // and backdrop_url for the episode still.
        var usesBackdrop = !UsePosterAspect && !string.IsNullOrEmpty(item.BackdropUrl);
        var imageUrl = usesBackdrop ? item.BackdropUrl : item.PosterUrl;
        if (string.IsNullOrEmpty(imageUrl) && !string.IsNullOrEmpty(item.BackdropUrl))
        {
            imageUrl = item.BackdropUrl;
            usesBackdrop = true;
        }
        if (string.IsNullOrEmpty(imageUrl)) return;

        try
        {
            await Task.Delay(300, ct);
            if (ct.IsCancellationRequested) return;

            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            // Episode-carousel backdrops are episode stills. Use the same cache
            // identity as season episode cards so the detail carousel reuses
            // artwork already fetched by the season page instead of forcing a
            // second presigned-URL download under a generic backdrop key.
            var imageType = item.ItemSource == "episode_carousel"
                ? "still"
                : usesBackdrop ? "backdrop" : "poster";

            var diskPath = await imageService.GetImageDiskPathAsync(
                item.ContentId, imageType, imageUrl, httpClient, ct);

            if (ct.IsCancellationRequested || string.IsNullOrEmpty(diskPath)) return;

            // Decode at 2x logical width so hi-DPI displays (and the 1.04× hover
            // scale) keep the source crisp. Logical means WinUI also multiplies
            // by RasterizationScale, so 560 logical → 1120 physical at 2× DPI.
            // Any smaller (e.g. 280) and source detail is thrown away before
            // the rendering pipeline ever scales it up.
            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = (int)Math.Ceiling(_cardWidth * 2),
                DecodePixelType = DecodePixelType.Logical,
                UriSource = new Uri(diskPath),
            };
            BackdropImage.Source = bitmapImage;
            NoImageText.Visibility = Visibility.Collapsed;
            // Smooth fade-in matching webui transition-opacity duration-300
            var fadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                {
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                },
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, BackdropImage);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            sb.Children.Add(fadeIn);
            sb.Begin();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LocalLog.AppendLine(
                "item_detail_images.txt",
                $"landscape_image_failed content={item.ContentId} source={item.ItemSource} " +
                $"error={ex.GetType().Name}: {ex.Message}");
        }
    }

    // B32: image click → play, text click → details. Mirrors the webui
    // ContinueWatchingCard dual-link pattern (image <Link to="/watch/{id}">,
    // text <Link to="/item/{id}">).

    private void OnImageTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;
        e.Handled = true;
        App.Services.GetRequiredService<ItemDetailPrefetchCache>()
            .Prefetch(MediaItem.ContentId);
        var navigationService = App.Services.GetRequiredService<NavigationService>();
        // Always play the actual content_id on the card — the server resolves
        // episode → file; we don't rewrite to series_id here (that would break
        // resume for the specific episode the card represents).
        navigationService.Navigate<ItemDetailPage>(MediaItem.ContentId);
    }

    private void OnCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this))
            return;
        if (e.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
            || MediaItem == null)
            return;

        App.Services.GetRequiredService<ItemDetailPrefetchCache>()
            .Prefetch(MediaItem.ContentId);
        App.Services.GetRequiredService<NavigationService>()
            .Navigate<ItemDetailPage>(MediaItem.ContentId);
        e.Handled = true;
    }

    private void OnPlayTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (MediaItem == null) return;

        var nav = App.Services.GetRequiredService<NavigationService>();
        if (MediaItem.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase))
        {
            nav.Navigate<EbookReaderPage>(new EbookReaderNavigation(MediaItem.ContentId));
            return;
        }

        if (MediaItem.Type is "movie" or "episode" or "audiobook")
        {
            var player = App.Services.GetRequiredService<Services.PlayerService>();
            if (MediaItem.Type == "audiobook" && player.IsAudiobook &&
                string.Equals(player.ContentId, MediaItem.ContentId, StringComparison.Ordinal))
                player.ToggleAudiobookPlayback();
            else
                _ = player.PlayAsync(MediaItem.ContentId);
            return;
        }

        nav.Navigate<ItemDetailPage>(MediaItem.ContentId);
    }

    private void OnHeadingClick(object sender, RoutedEventArgs e)
    {
        if (MediaItem == null) return;
        var nav = App.Services.GetRequiredService<NavigationService>();
        var headingIsSeries = MediaItem.ItemSource != "episode_carousel"
            && !string.IsNullOrWhiteSpace(MediaItem.SeriesId)
            && !string.IsNullOrWhiteSpace(MediaItem.SeriesTitle)
            && (MediaItem.SeasonNumber.HasValue && MediaItem.EpisodeNumber.HasValue
                || MediaItem.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase));
        var contentId = headingIsSeries ? MediaItem.SeriesId! : MediaItem.ContentId;
        App.Services.GetRequiredService<ItemDetailPrefetchCache>().Prefetch(contentId);
        nav.Navigate<ItemDetailPage>(contentId);
    }

    private void OnMetadataClick(object sender, RoutedEventArgs e)
    {
        if (MediaItem == null) return;
        var nav = App.Services.GetRequiredService<NavigationService>();
        var isMangaChapter = MediaItem.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(MediaItem.SeriesId);
        if (isMangaChapter)
            nav.Navigate<EbookReaderPage>(new EbookReaderNavigation(MediaItem.ContentId));
        else
        {
            App.Services.GetRequiredService<ItemDetailPrefetchCache>()
                .Prefetch(MediaItem.ContentId);
            nav.Navigate<ItemDetailPage>(MediaItem.ContentId);
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        QueuePlaybackPrefetch();
        if (!CardPointerInteractionPolicy.ShouldRevealHoverActions(
                e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch))
            return;

        _isPointerOver = true;
        AnimateHover(scale: 1.05, dimOpacity: 1.0, playOpacity: 1.0, playScale: 1.0, dismissOpacity: 1.0);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        try { _playbackPrefetchCts?.Cancel(); } catch { }
        _playbackPrefetchCts?.Dispose();
        _playbackPrefetchCts = null;
        if (!_isKeyboardFocusWithin)
            AnimateHover(scale: 1.0, dimOpacity: 0.0, playOpacity: 0.0, playScale: 0.7, dismissOpacity: 0.0);
    }

    private void OnCardGotFocus(object sender, RoutedEventArgs e)
    {
        _isKeyboardFocusWithin = true;
        QueuePlaybackPrefetch();
        AnimateHover(scale: 1.05, dimOpacity: 1.0, playOpacity: 1.0, playScale: 1.0, dismissOpacity: 1.0);
    }

    private void OnCardLostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = XamlRoot == null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            _isKeyboardFocusWithin = IsDescendantOf(focused, this);
            if (!_isKeyboardFocusWithin && !_isPointerOver)
                AnimateHover(scale: 1.0, dimOpacity: 0.0, playOpacity: 0.0, playScale: 0.7, dismissOpacity: 0.0);
        });
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    private async void QueuePlaybackPrefetch()
    {
        var item = MediaItem;
        if (item == null)
            return;

        try { _playbackPrefetchCts?.Cancel(); } catch { }
        _playbackPrefetchCts?.Dispose();
        _playbackPrefetchCts = new CancellationTokenSource();
        var ct = _playbackPrefetchCts.Token;

        try
        {
            // Avoid issuing requests while the pointer is merely crossing a
            // carousel. A deliberate hover still begins before the 180 ms play
            // affordance animation has finished.
            await Task.Delay(140, ct);
            if (!ct.IsCancellationRequested && ReferenceEquals(MediaItem, item))
            {
                App.Services.GetRequiredService<ItemDetailPrefetchCache>()
                    .Prefetch(item.ContentId);
                if (item.Type is not ("movie" or "episode" or "audiobook"))
                    return;
                App.Services.GetRequiredService<Services.PlayerService>()
                    .PrefetchWatchDetail(item.ContentId);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        ContextFlyout?.ShowAt(DismissButton);
    }

    private void DismissButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Keep the overlay action from bubbling into the backdrop's play tap
        // or the card's detail-navigation tap. PosterCard uses the same guard.
        e.Handled = true;
    }

    private async void QuickWatchedButton_Click(object sender, RoutedEventArgs e)
    {
        var item = MediaItem;
        if (item == null || _quickActionPending) return;

        _quickActionPending = true;
        QuickWatchedButton.IsEnabled = false;
        var operation = MediaItemCardActions.ToggleWatchedAsync(item);
        UpdateQuickWatchedState(item);
        await operation;
        if (ReferenceEquals(MediaItem, item))
            RefreshMenuState(item);
        _quickActionPending = false;
        QuickWatchedButton.IsEnabled = true;
    }

    private void QuickWatchedButton_Tapped(object sender, TappedRoutedEventArgs e)
        => e.Handled = true;

    private void UpdateQuickWatchedState(MediaItem item)
    {
        var show = item.UserState != null;
        var isWatched = item.UserState?.Played == true;
        QuickWatchedButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        QuickWatchedIcon.Glyph = isWatched ? "\uE7B3" : "\uED1A";
        QuickWatchedIcon.Foreground = isWatched
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        var label = MediaItemCardActions.GetWatchedActionLabel(item.Type, isWatched);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(QuickWatchedButton, label);
        ToolTipService.SetToolTip(QuickWatchedButton, label);
        QuickWatchedButton.Opacity = _isPointerOver || _isKeyboardFocusWithin ? 1 : 0;
    }

    private void UpdateDismissVisibility()
    {
        if (DismissButton == null) return;
        DismissButton.Visibility = MediaItem != null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Mirrors the webui ContinueWatchingCard hover: subtle card scale, accent
    /// border glow, dark tint overlay, and a centered Play circle that fades
    /// and scales in. Short ease-out curve matching the webui transition timing.
    /// </summary>
    private void AnimateHover(double scale, double dimOpacity, double playOpacity, double playScale, double dismissOpacity = 0)
    {
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

        void Add(DependencyObject target, string prop, double to, int durationMs)
        {
            var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = ease,
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, target);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, prop);
            storyboard.Children.Add(anim);
        }

        Add(HoverTransform, "ScaleX", scale, 300);
        Add(HoverTransform, "ScaleY", scale, 300);
        Add(HoverDim, "Opacity", dimOpacity, 150);
        Add(HoverPlayButton, "Opacity", playOpacity, 200);
        Add(HoverPlayButtonTransform, "ScaleX", playScale, 200);
        Add(HoverPlayButtonTransform, "ScaleY", playScale, 200);
        if (DismissButton.Visibility == Visibility.Visible)
            Add(DismissButton, "Opacity", dismissOpacity, 200);
        if (QuickWatchedButton.Visibility == Visibility.Visible)
            Add(QuickWatchedButton, "Opacity", dismissOpacity, 200);

        storyboard.Begin();
    }

    /// <summary>
    /// Translate a server-attached badge string into the uppercase label we
    /// display on the card. Returns null if no known badge applies. Mirrors the
    /// WebUI <c>upcomingBadgeLabel()</c> helper.
    /// </summary>
    private static string? GetBadgeLabel(MediaItem item)
    {
        if (item.Badges == null || item.Badges.Count == 0) return null;

        foreach (var badge in item.Badges)
        {
            if (string.IsNullOrEmpty(badge)) continue;
            switch (badge)
            {
                case "season_premiere": return "SEASON PREMIERE";
                case "series_premiere": return "SERIES PREMIERE";
                case "new_season":      return "NEW SEASON";
                case "new_episode":     return "NEW EPISODE";
                // Unknown badge: render the server string in uppercase as a best-effort fallback.
                default: return badge.Replace('_', ' ').ToUpperInvariant();
            }
        }
        return null;
    }
}
