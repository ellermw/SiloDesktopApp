using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public sealed class LibraryGridCard : Canvas
{
    private readonly Border _posterBackground;
    private readonly Image _posterImage;
    private readonly Border _hoverBrighten;
    private readonly CompositeTransform _cardHoverTransform;
    private readonly CompositeTransform _posterHoverTransform;
    private readonly UICustomizationService _uiCustomizationService;
    private readonly DefaultArtwork _artworkFallback;
    private readonly TextBlock _titleText;
    private readonly TextBlock _episodeTitleText;
    private readonly TextBlock _subtitleText;
    private readonly Button _moreButton;
    private readonly Button _quickWatchedButton;
    private readonly Button _quickFavoriteButton;
    private readonly FontIcon _quickWatchedIcon;
    private readonly FontIcon _quickFavoriteIcon;
    private readonly StackPanel _overlayTopLeft;
    private readonly StackPanel _overlayTopRight;
    private readonly StackPanel _overlayBottomLeft;
    private readonly StackPanel _overlayBottomRight;
    private CancellationTokenSource? _posterLoadCts;
    private CancellationTokenSource? _playbackPrefetchCts;
    private int _posterLoadVersion;
    private double _posterHeight;
    private bool _quickActionPending;
    private bool _isPointerOver;
    private CardOverlayGeometry _overlayGeometry;

    private static readonly AsyncWorkThrottle s_imageLoadThrottle = new(maxConcurrency: 8);
    private static readonly SemaphoreSlim s_bitmapCreateLock = new(1);

    public MediaItem? MediaItem { get; private set; }
    public string? SortKey { get; private set; }

    public LibraryGridCard()
    {
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        var cardWidth = (double)Application.Current.Resources["PosterCardWidth"];
        var posterHeight = (double)Application.Current.Resources["PosterCardHeight"];
        var cardHeight = (double)Application.Current.Resources["PosterCardTotalHeight"];
        _posterHeight = posterHeight;
        _overlayGeometry = CardOverlayGeometry.ForPoster(cardWidth - 2, App.Services.GetRequiredService<CardOverlayService>().Preset);

        Width = cardWidth;
        Height = cardHeight;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _cardHoverTransform = new CompositeTransform();
        RenderTransform = _cardHoverTransform;

        _artworkFallback = new DefaultArtwork();

        _posterImage = new Image
        {
            Width = cardWidth,
            Height = posterHeight,
            Stretch = Stretch.UniformToFill,
            Opacity = 0,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
        };
        _posterHoverTransform = new CompositeTransform();
        _posterImage.RenderTransform = _posterHoverTransform;

        var posterHost = new Grid();
        posterHost.Children.Add(_artworkFallback);
        posterHost.Children.Add(_posterImage);
        _posterImage.ImageOpened += (_, _) => _artworkFallback.Visibility = Visibility.Collapsed;
        _posterImage.ImageFailed += (_, _) =>
        {
            _posterImage.Source = null; _posterImage.Opacity = 0;
            _artworkFallback.Visibility = Visibility.Visible;
            _ = _artworkFallback.ShowThumbhashAsync();
        };

        _overlayTopLeft = CreateOverlayHost(HorizontalAlignment.Left, VerticalAlignment.Top, _overlayGeometry);
        _overlayTopRight = CreateOverlayHost(HorizontalAlignment.Right, VerticalAlignment.Top, _overlayGeometry);
        _overlayBottomLeft = CreateOverlayHost(HorizontalAlignment.Left, VerticalAlignment.Bottom, _overlayGeometry);
        _overlayBottomRight = CreateOverlayHost(HorizontalAlignment.Right, VerticalAlignment.Bottom, _overlayGeometry);
        ApplyOverlayHostGeometry();
        posterHost.Children.Add(_overlayTopLeft);
        posterHost.Children.Add(_overlayTopRight);
        posterHost.Children.Add(_overlayBottomLeft);
        posterHost.Children.Add(_overlayBottomRight);
        _hoverBrighten = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(24, 255, 255, 255)),
            CornerRadius = (CornerRadius)Application.Current.Resources["PosterCornerRadius"],
            Opacity = 0,
            IsHitTestVisible = false,
        };
        posterHost.Children.Add(_hoverBrighten);

        _posterBackground = new Border
        {
            Width = cardWidth,
            Height = posterHeight,
            Background = Brush("CardBackgroundBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["PosterCornerRadius"],
            Child = posterHost,
        };
        SetLeft(_posterBackground, 0);
        SetTop(_posterBackground, 0);
        Children.Add(_posterBackground);

        _quickWatchedIcon = new FontIcon
        {
            Glyph = "\uED1A",
            FontSize = 15,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        _quickWatchedButton = CreateQuickActionButton(_quickWatchedIcon);
        SetLeft(_quickWatchedButton, 10);
        SetTop(_quickWatchedButton, posterHeight - 42);
        _quickWatchedButton.Tapped += (_, args) => args.Handled = true;
        _quickWatchedButton.Click += QuickWatchedButton_Click;
        Children.Add(_quickWatchedButton);

        _quickFavoriteIcon = new FontIcon
        {
            Glyph = "\uEB51",
            FontSize = 15,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
        };
        _quickFavoriteButton = CreateQuickActionButton(_quickFavoriteIcon);
        SetLeft(_quickFavoriteButton, 48);
        SetTop(_quickFavoriteButton, posterHeight - 42);
        _quickFavoriteButton.Tapped += (_, args) => args.Handled = true;
        _quickFavoriteButton.Click += QuickFavoriteButton_Click;
        Children.Add(_quickFavoriteButton);

        _moreButton = new Button
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(170, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(48, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            IsHitTestVisible = false,
            Content = new FontIcon
            {
                Glyph = "\uE712",
                FontSize = 15,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            },
        };
        SetLeft(_moreButton, cardWidth - 42);
        SetTop(_moreButton, posterHeight - 42);
        _moreButton.Tapped += (_, args) => args.Handled = true;
        _moreButton.Click += MoreButton_Click;
        Children.Add(_moreButton);

        _titleText = new TextBlock
        {
            Width = Math.Max(0, cardWidth - 8),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("PrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        _subtitleText = new TextBlock
        {
            Width = Math.Max(0, cardWidth - 8),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = Brush("SecondaryTextBrush"),
            CharacterSpacing = 140,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        _episodeTitleText = new TextBlock
        {
            Width = Math.Max(0, cardWidth - 8),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = Brush("SecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Visibility = Visibility.Collapsed,
        };
        SetLeft(_titleText, 4);
        SetTop(_titleText, posterHeight + 12);
        Children.Add(_titleText);

        SetLeft(_episodeTitleText, 4);
        SetTop(_episodeTitleText, posterHeight + 34);
        Children.Add(_episodeTitleText);

        SetLeft(_subtitleText, 4);
        SetTop(_subtitleText, posterHeight + 34);
        Children.Add(_subtitleText);

        Tapped += OnTapped;
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        GotFocus += OnGotFocus;
        LostFocus += OnLostFocus;
        ContextRequested += OnContextRequested;
        Loaded += (_, _) => { App.Services.GetRequiredService<CardOverlayService>().Changed -= OverlayPreferences_Changed; App.Services.GetRequiredService<CardOverlayService>().Changed += OverlayPreferences_Changed; };
        Unloaded += (_, _) => App.Services.GetRequiredService<CardOverlayService>().Changed -= OverlayPreferences_Changed;
    }

    private void OverlayPreferences_Changed() => DispatcherQueue.TryEnqueue(() => { if (MediaItem is {} item) { UpdateQuickActionState(item); UpdateOverlays(item); } });

    public void Bind(MediaItem item, string? sortKey)
    {
        var preservePoster = !LibraryPosterBinding.RequiresReload(MediaItem, item) &&
            (_posterImage.Source != null || (_posterLoadCts != null &&
                MediaItem?.PosterUrl == item.PosterUrl && MediaItem?.BackdropUrl == item.BackdropUrl));
        if (!preservePoster) CancelPosterLoad(clearImage: true);
        CancelPlaybackPrefetch();

        // ItemGrid uses square artwork for audiobook items even inside a mixed
        // library. Keep the virtual row height stable, but size the individual
        // artwork from the item type just as the WebUI does.
        var itemPosterHeight = string.Equals(item.Type, "audiobook", StringComparison.OrdinalIgnoreCase)
            ? Width
            : Width * 1.5;
        SetLayout(Width, itemPosterHeight, Height);

        MediaItem = item;
        SortKey = sortKey;
        IsHitTestVisible = true;
        Opacity = 1;

        _artworkFallback.Reset(item.Type, !string.IsNullOrWhiteSpace(item.PosterUrl) ? item.PosterThumbhash : item.BackdropThumbhash);
        _artworkFallback.Visibility = string.IsNullOrWhiteSpace(item.PosterUrl) && string.IsNullOrWhiteSpace(item.BackdropUrl) ? Visibility.Visible : Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, MediaItemDisplayText.BuildTitle(item));
        _titleText.Text = MediaItemDisplayText.BuildTitle(item);
        var episodeTitle = MediaItemDisplayText.BuildEpisodeTitle(item);
        _episodeTitleText.Text = episodeTitle ?? "";
        _episodeTitleText.Visibility = episodeTitle is null ? Visibility.Collapsed : Visibility.Visible;
        _subtitleText.Text = MediaItemDisplayText.BuildSubtitle(item, sortKey);
        SetTop(_subtitleText, _posterHeight + (episodeTitle is null ? 34 : 56));
        ApplyCardPresentation();
        UpdateOverlays(item);
        UpdateQuickActionState(item);

        var imageUrl = !string.IsNullOrWhiteSpace(item.PosterUrl) ? item.PosterUrl : item.BackdropUrl;
        if (!preservePoster && !string.IsNullOrWhiteSpace(imageUrl))
        {
            _posterLoadCts = new CancellationTokenSource();
            var version = ++_posterLoadVersion;
            _ = LoadPosterAsync(item, imageUrl, version, _posterLoadCts.Token);
        }
        else if (string.IsNullOrWhiteSpace(imageUrl)) _ = _artworkFallback.ShowThumbhashAsync();
    }

    /// <summary>Refreshes mutable profile state without restarting artwork loading.</summary>
    public void RefreshState()
    {
        if (MediaItem is not { } item) return;
        _subtitleText.Text = MediaItemDisplayText.BuildSubtitle(item, SortKey);
        ApplyCardPresentation();
        UpdateOverlays(item);
        UpdateQuickActionState(item);
    }

    public void ApplyCardPresentation()
    {
        var caption = _uiCustomizationService.CardPresentation.Caption;
        var showCaption = caption != "artwork";
        var showMetadata = caption == "title_metadata";
        _titleText.Visibility = showCaption ? Visibility.Visible : Visibility.Collapsed;
        _episodeTitleText.Visibility = showMetadata && !string.IsNullOrWhiteSpace(_episodeTitleText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _subtitleText.Visibility = showMetadata && !string.IsNullOrWhiteSpace(_subtitleText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void SetLayout(double cardWidth, double posterHeight, double cardHeight)
    {
        if (Math.Abs(Width - cardWidth) < 0.5 &&
            Math.Abs(_posterHeight - posterHeight) < 0.5 &&
            Math.Abs(Height - cardHeight) < 0.5)
            return;

        Width = cardWidth;
        Height = cardHeight;
        _posterHeight = posterHeight;
        _posterBackground.Width = cardWidth;
        _posterBackground.Height = posterHeight;
        _posterImage.Width = cardWidth;
        _posterImage.Height = posterHeight;
        _titleText.Width = Math.Max(0, cardWidth - 8);
        _episodeTitleText.Width = Math.Max(0, cardWidth - 8);
        _subtitleText.Width = Math.Max(0, cardWidth - 8);
        SetTop(_titleText, posterHeight + 12);
        SetTop(_episodeTitleText, posterHeight + 34);
        SetTop(_subtitleText, posterHeight +
            (_episodeTitleText.Visibility == Visibility.Visible ? 56 : 34));
        SetLeft(_moreButton, cardWidth - 42);
        SetTop(_moreButton, posterHeight - 42);
        SetTop(_quickWatchedButton, posterHeight - 42);
        SetTop(_quickFavoriteButton, posterHeight - 42);

        var geometry = CardOverlayGeometry.ForPoster(cardWidth - 2, App.Services.GetRequiredService<CardOverlayService>().Preset);
        if (geometry != _overlayGeometry)
        {
            _overlayGeometry = geometry;
            ApplyOverlayHostGeometry();
            if (MediaItem is { } item)
                UpdateOverlays(item);
        }
    }

    public void BindPlaceholder()
    {
        CancelPosterLoad(clearImage: true);
        CancelPlaybackPrefetch();
        MediaItem = null;
        SortKey = null;
        IsHitTestVisible = false;
        Opacity = 0.55;
        _artworkFallback.Reset(null);
        _artworkFallback.Visibility = Visibility.Collapsed;
        _titleText.Text = "";
        _episodeTitleText.Text = "";
        _episodeTitleText.Visibility = Visibility.Collapsed;
        _subtitleText.Text = "";
        _posterBackground.Background = Brush("CardBackgroundBrush");
        _moreButton.Opacity = 0;
        _moreButton.IsHitTestVisible = false;
        HideQuickActions();
        ResetHoverVisuals();
        ClearOverlays();
    }

    public void Reset()
    {
        CancelPosterLoad(clearImage: true);
        CancelPlaybackPrefetch();
        MediaItem = null;
        SortKey = null;
        IsHitTestVisible = false;
        Opacity = 1;
        _artworkFallback.Reset(null);
        _artworkFallback.Visibility = Visibility.Collapsed;
        _titleText.Text = "";
        _episodeTitleText.Text = "";
        _episodeTitleText.Visibility = Visibility.Collapsed;
        _subtitleText.Text = "";
        _posterBackground.Background = Brush("CardBackgroundBrush");
        _moreButton.Opacity = 0;
        _moreButton.IsHitTestVisible = false;
        HideQuickActions();
        ResetHoverVisuals();
        ClearOverlays();
    }

    private async Task LoadPosterAsync(MediaItem item, string imageUrl, int version, CancellationToken ct)
    {
        try
        {
            await Task.Delay(25, ct);
            if (!IsCurrentPosterLoad(item, version, ct))
                return;

            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var imageType = !string.IsNullOrWhiteSpace(item.PosterUrl) ? "poster" : "backdrop";

            var diskPath = await s_imageLoadThrottle.RunAsync(
                token => imageService.GetImageDiskPathAsync(item.ContentId, imageType, imageUrl, httpClient, token),
                ct);

            if (!IsCurrentPosterLoad(item, version, ct))
                return;
            if (string.IsNullOrWhiteSpace(diskPath)) { await _artworkFallback.ShowThumbhashAsync(); return; }

            await s_bitmapCreateLock.WaitAsync(ct);
            try
            {
                if (!IsCurrentPosterLoad(item, version, ct))
                    return;

                var bitmapImage = new BitmapImage
                {
                    DecodePixelWidth = Math.Clamp((int)Math.Ceiling(Width * 1.25), 200, 640),
                    DecodePixelType = DecodePixelType.Logical,
                    UriSource = new Uri(diskPath),
                };

                if (!IsCurrentPosterLoad(item, version, ct))
                    return;

                _posterImage.Source = bitmapImage;
                _posterImage.Opacity = 1;
            }
            finally
            {
                s_bitmapCreateLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch { if (IsCurrentPosterLoad(item, version, ct)) await _artworkFallback.ShowThumbhashAsync(); }
        finally
        {
            if (version == _posterLoadVersion)
            {
                _posterLoadCts?.Dispose();
                _posterLoadCts = null;
            }
        }
    }

    private bool IsCurrentPosterLoad(MediaItem item, int version, CancellationToken ct)
    {
        return !ct.IsCancellationRequested &&
            version == _posterLoadVersion &&
            MediaItem?.ContentId == item.ContentId;
    }

    private void CancelPosterLoad(bool clearImage)
    {
        try { _posterLoadCts?.Cancel(); } catch { }
        _posterLoadCts?.Dispose();
        _posterLoadCts = null;
        _posterLoadVersion++;

        if (!clearImage)
            return;

        _posterImage.Source = null;
        _posterImage.Opacity = 0;
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null)
            return;

        var libraryId = MediaNavigationContext.LibraryId(this);
        if (!libraryId.HasValue) App.Services.GetRequiredService<ItemDetailPrefetchCache>().Prefetch(MediaItem.ContentId);
        App.Services.GetRequiredService<NavigationService>()
            .Navigate<ItemDetailPage>(MediaNavigationContext.Detail(MediaItem.ContentId, libraryId));
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        QueuePlaybackPrefetch();
        if (!CardPointerInteractionPolicy.ShouldRevealHoverActions(
                e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch))
            return;

        _isPointerOver = true;
        _posterHoverTransform.ScaleX = 1.06;
        _posterHoverTransform.ScaleY = 1.06;
        _cardHoverTransform.TranslateY = -4;
        _hoverBrighten.Opacity = 1;
        _moreButton.Opacity = 1;
        _moreButton.IsHitTestVisible = true;
        RevealQuickActions();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        CancelPlaybackPrefetch();
        ResetHoverVisuals();
        _moreButton.Opacity = 0;
        _moreButton.IsHitTestVisible = false;
        HideQuickActions(preserveVisibility: true);
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        QueuePlaybackPrefetch();
        _moreButton.Opacity = 1;
        _moreButton.IsHitTestVisible = true;
        RevealQuickActions();
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        CancelPlaybackPrefetch();
        if (!_isPointerOver)
        {
            _moreButton.Opacity = 0;
            _moreButton.IsHitTestVisible = false;
            HideQuickActions(preserveVisibility: true);
        }
    }

    private async void QueuePlaybackPrefetch()
    {
        var item = MediaItem;
        if (item == null)
            return;

        CancelPlaybackPrefetch();
        _playbackPrefetchCts = new CancellationTokenSource();
        var ct = _playbackPrefetchCts.Token;
        try
        {
            // Keep fast library scrolling free of speculative network work.
            // Only a pointer that settles on a playable card primes playback.
            await Task.Delay(140, ct);
            if (!ct.IsCancellationRequested && ReferenceEquals(MediaItem, item))
            {
                var libraryId = MediaNavigationContext.LibraryId(this);
                if (!libraryId.HasValue) App.Services.GetRequiredService<ItemDetailPrefetchCache>().Prefetch(item.ContentId);
                if (item.Type is not ("movie" or "episode" or "audiobook"))
                    return;
                App.Services.GetRequiredService<PlayerService>()
                    .PrefetchWatchDetail(item.ContentId, libraryId);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void CancelPlaybackPrefetch()
    {
        try { _playbackPrefetchCts?.Cancel(); } catch { }
        _playbackPrefetchCts?.Dispose();
        _playbackPrefetchCts = null;
    }

    private void ResetHoverVisuals()
    {
        _posterHoverTransform.ScaleX = 1;
        _posterHoverTransform.ScaleY = 1;
        _cardHoverTransform.TranslateY = 0;
        _hoverBrighten.Opacity = 0;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MediaItem == null)
            return;

        var item = MediaItem;
        var flyout = MediaItemMenu.Build(
            item,
            MediaItemMenu.Surface.Default,
            stateChanged: RefreshState, owner: this);
        flyout.ShowAt(_moreButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight,
        });
    }

    private async void QuickWatchedButton_Click(object sender, RoutedEventArgs e)
    {
        var item = MediaItem;
        if (item == null || _quickActionPending) return;

        _quickActionPending = true;
        SetQuickActionsEnabled(false);
        try
        {
            var operation = MediaItemCardActions.ToggleWatchedAsync(item);
            UpdateQuickActionState(item);
            await operation;
            if (ReferenceEquals(MediaItem, item))
                RefreshState();
        }
        finally
        {
            _quickActionPending = false;
            SetQuickActionsEnabled(true);
        }
    }

    private async void QuickFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        var item = MediaItem;
        if (item == null || _quickActionPending) return;

        _quickActionPending = true;
        SetQuickActionsEnabled(false);
        try
        {
            var operation = MediaItemCardActions.ToggleFavoriteAsync(item);
            UpdateQuickActionState(item);
            await operation;
            if (ReferenceEquals(MediaItem, item))
                RefreshState();
        }
        finally
        {
            _quickActionPending = false;
            SetQuickActionsEnabled(true);
        }
    }

    private void UpdateQuickActionState(MediaItem item)
    {
        var preferences = App.Services.GetRequiredService<CardOverlayService>();
        var hasState = item.UserState != null && preferences.QuickActionsEnabled && preferences.QuickActionMode != "none";
        var showWatched = hasState && (preferences.QuickActionMode is "both" or "watched") && (item.Type is "movie" or "series");
        var showFavorite = hasState && (preferences.QuickActionMode is "both" or "favorites");
        var isWatched = item.UserState?.Played == true;
        var isFavorite = item.UserState?.IsFavorite == true;

        _quickWatchedButton.Visibility = showWatched ? Visibility.Visible : Visibility.Collapsed;
        _quickFavoriteButton.Visibility = showFavorite ? Visibility.Visible : Visibility.Collapsed;
        _quickWatchedIcon.Glyph = isWatched ? "\uE7B3" : "\uED1A";
        _quickWatchedIcon.Foreground = isWatched
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80))
            : new SolidColorBrush(Microsoft.UI.Colors.White);
        _quickFavoriteIcon.Glyph = isFavorite ? "\uEB52" : "\uEB51";
        _quickFavoriteIcon.Foreground = isFavorite
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF8, 0x71, 0x71))
            : new SolidColorBrush(Microsoft.UI.Colors.White);

        var watchedLabel = MediaItemCardActions.GetWatchedActionLabel(item.Type, isWatched);
        var favoriteLabel = isFavorite ? "Remove from favorites" : "Add to favorites";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_quickWatchedButton, watchedLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_quickFavoriteButton, favoriteLabel);
        ToolTipService.SetToolTip(_quickWatchedButton, watchedLabel);
        ToolTipService.SetToolTip(_quickFavoriteButton, favoriteLabel);
    }

    private void RevealQuickActions()
    {
        if (_quickWatchedButton.Visibility == Visibility.Visible)
        {
            _quickWatchedButton.Opacity = 1;
            _quickWatchedButton.IsHitTestVisible = true;
        }
        if (_quickFavoriteButton.Visibility == Visibility.Visible)
        {
            _quickFavoriteButton.Opacity = 1;
            _quickFavoriteButton.IsHitTestVisible = true;
        }
    }

    private void HideQuickActions(bool preserveVisibility = false)
    {
        _quickWatchedButton.Opacity = 0;
        _quickWatchedButton.IsHitTestVisible = false;
        _quickFavoriteButton.Opacity = 0;
        _quickFavoriteButton.IsHitTestVisible = false;
        if (!preserveVisibility)
        {
            _quickWatchedButton.Visibility = Visibility.Collapsed;
            _quickFavoriteButton.Visibility = Visibility.Collapsed;
        }
    }

    private void SetQuickActionsEnabled(bool enabled)
    {
        _quickWatchedButton.IsEnabled = enabled;
        _quickFavoriteButton.IsEnabled = enabled;
    }

    private static StackPanel CreateOverlayHost(
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        CardOverlayGeometry geometry) => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = geometry.StackGap,
        HorizontalAlignment = horizontal,
        VerticalAlignment = vertical,
        Margin = new Thickness(geometry.EdgeInset),
        IsHitTestVisible = false,
    };

    private void ApplyOverlayHostGeometry()
    {
        foreach (var host in new[] { _overlayTopLeft, _overlayTopRight, _overlayBottomLeft, _overlayBottomRight })
            host.Spacing = _overlayGeometry.StackGap;

        var edge = _overlayGeometry.EdgeInset;
        _overlayTopLeft.Margin = new Thickness(edge);
        _overlayTopRight.Margin = new Thickness(edge);
        _overlayBottomLeft.Margin = new Thickness(edge);
        _overlayBottomRight.Margin = new Thickness(edge);
    }

    private void ClearOverlays()
    {
        _overlayTopLeft.Children.Clear();
        _overlayTopRight.Children.Clear();
        _overlayBottomLeft.Children.Clear();
        _overlayBottomRight.Children.Clear();
    }

    private void UpdateOverlays(MediaItem item)
    {
        ClearOverlays();
        var service = App.Services.GetRequiredService<CardOverlayService>();
        _overlayGeometry = CardOverlayGeometry.ForPoster(Width - 2, service.Preset);
        ApplyOverlayHostGeometry();
        if (!service.IsLoaded)
            _ = EnsureOverlaysLoadedAsync(item, service);

        if (item.Status is "pending" or "unmatched" or "ambiguous")
        {
            var label = item.Status switch
            {
                "pending" => "SCANNING",
                "unmatched" => "UNMATCHED",
                _ => "AMBIGUOUS",
            };
            _overlayTopLeft.Children.Add(PosterCard.BuildBadge(
                label,
                "status",
                new OverlayItemConfig(true, OverlayPosition.TopLeft),
                "classic",
                _overlayGeometry.Scale));
            return;
        }

        if (item.Type == "manga")
        {
            if (!string.IsNullOrWhiteSpace(item.ShowStatus))
            {
                _overlayTopLeft.Children.Add(PosterCard.BuildBadge(
                    item.ShowStatus,
                    "show_status",
                    new OverlayItemConfig(true, OverlayPosition.TopLeft),
                    service.Preset,
                    _overlayGeometry.Scale));
            }

            var counts = new List<string>();
            if (item.MangaVolumeCount > 0) counts.Add($"{item.MangaVolumeCount} Vol");
            if (item.MangaChapterCount > 0) counts.Add($"{item.MangaChapterCount} Ch");
            if (counts.Count > 0)
            {
                _overlayTopRight.Children.Add(PosterCard.BuildBadge(
                    string.Join(" · ", counts),
                    "manga_counts",
                    new OverlayItemConfig(true, OverlayPosition.TopRight),
                    service.Preset,
                    _overlayGeometry.Scale));
            }
            return;
        }

        var prefs = service.GetPrefs();
        if (prefs == null)
            return;

        var data = OverlayData.FromMediaItem(item);
        var cornerCounts = new Dictionary<OverlayPosition, int>();
        foreach (var def in service.GetOrderedDefinitions())
        {
            if (!prefs.TryGetValue(def.Id, out var config) || !config.Enabled)
                continue;
            if (OverlayRegistry.SuppressesStandaloneOverlays(def.Id, prefs))
                continue;

            var value = def.GetValue(data);
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (cornerCounts.GetValueOrDefault(config.Position) >= 3)
                continue;

            var host = config.Position switch
            {
                OverlayPosition.TopLeft => _overlayTopLeft,
                OverlayPosition.TopRight => _overlayTopRight,
                OverlayPosition.BottomLeft => _overlayBottomLeft,
                OverlayPosition.BottomRight => _overlayBottomRight,
                _ => _overlayTopLeft,
            };
            host.Children.Add(PosterCard.BuildBadge(
                value,
                def.Id,
                config,
                service.Preset,
                _overlayGeometry.Scale));
            cornerCounts[config.Position] = cornerCounts.GetValueOrDefault(config.Position) + 1;
        }
    }

    private async Task EnsureOverlaysLoadedAsync(MediaItem item, CardOverlayService service)
    {
        try
        {
            await service.EnsureLoadedAsync();
            if (ReferenceEquals(MediaItem, item))
                UpdateOverlays(item);
        }
        catch { }
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (MediaItem == null)
            return;

        var flyout = MediaItemMenu.Build(
            MediaItem,
            MediaItemMenu.Surface.Default,
            stateChanged: RefreshState, owner: this);
        if (args.TryGetPosition(this, out var pos))
        {
            flyout.ShowAt(this, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = pos });
        }
        else
        {
            flyout.ShowAt(this);
        }

        args.Handled = true;
    }

    private static Brush Brush(string key) =>
        (Brush)Application.Current.Resources[key];

    private static Button CreateQuickActionButton(FontIcon icon) => new()
    {
        Width = 32,
        Height = 32,
        MinWidth = 0,
        MinHeight = 0,
        Padding = new Thickness(0),
        CornerRadius = new CornerRadius(6),
        Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(170, 0, 0, 0)),
        BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(48, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Opacity = 0,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        Content = icon,
    };
}
