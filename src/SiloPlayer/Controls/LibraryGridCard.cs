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
    private readonly TextBlock _fallbackTitle;
    private readonly TextBlock _titleText;
    private readonly TextBlock _episodeTitleText;
    private readonly TextBlock _subtitleText;
    private readonly Button _moreButton;
    private readonly StackPanel _overlayTopLeft;
    private readonly StackPanel _overlayTopRight;
    private readonly StackPanel _overlayBottomLeft;
    private readonly StackPanel _overlayBottomRight;
    private CancellationTokenSource? _posterLoadCts;
    private int _posterLoadVersion;
    private double _posterHeight;

    private static readonly AsyncWorkThrottle s_imageLoadThrottle = new(maxConcurrency: 8);
    private static readonly SemaphoreSlim s_bitmapCreateLock = new(1);

    public MediaItem? MediaItem { get; private set; }
    public string? SortKey { get; private set; }

    public LibraryGridCard()
    {
        var cardWidth = (double)Application.Current.Resources["PosterCardWidth"];
        var posterHeight = (double)Application.Current.Resources["PosterCardHeight"];
        var cardHeight = (double)Application.Current.Resources["PosterCardTotalHeight"];
        _posterHeight = posterHeight;

        Width = cardWidth;
        Height = cardHeight;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _cardHoverTransform = new CompositeTransform();
        RenderTransform = _cardHoverTransform;

        _fallbackTitle = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3,
            MaxWidth = Math.Max(80, cardWidth - 28),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("SecondaryTextBrush"),
            Padding = new Thickness(8),
        };

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
        posterHost.Children.Add(_posterImage);
        posterHost.Children.Add(_fallbackTitle);

        _overlayTopLeft = CreateOverlayHost(HorizontalAlignment.Left, VerticalAlignment.Top);
        _overlayTopRight = CreateOverlayHost(HorizontalAlignment.Right, VerticalAlignment.Top);
        _overlayBottomLeft = CreateOverlayHost(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        _overlayBottomRight = CreateOverlayHost(HorizontalAlignment.Right, VerticalAlignment.Bottom);
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
        ContextRequested += OnContextRequested;
    }

    public void Bind(MediaItem item, string? sortKey)
    {
        CancelPosterLoad(clearImage: true);

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

        var title = string.IsNullOrWhiteSpace(item.Title)
            ? "Untitled"
            : MediaItemDisplayText.BuildTitle(item);
        _fallbackTitle.Text = title;
        _fallbackTitle.Visibility = Visibility.Visible;
        _titleText.Text = MediaItemDisplayText.BuildTitle(item);
        var episodeTitle = MediaItemDisplayText.BuildEpisodeTitle(item);
        _episodeTitleText.Text = episodeTitle ?? "";
        _episodeTitleText.Visibility = episodeTitle is null ? Visibility.Collapsed : Visibility.Visible;
        _subtitleText.Text = MediaItemDisplayText.BuildSubtitle(item, sortKey);
        SetTop(_subtitleText, _posterHeight + (episodeTitle is null ? 34 : 56));
        UpdateOverlays(item);

        var imageUrl = !string.IsNullOrWhiteSpace(item.PosterUrl) ? item.PosterUrl : item.BackdropUrl;
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            _posterLoadCts = new CancellationTokenSource();
            var version = ++_posterLoadVersion;
            _ = LoadPosterAsync(item, imageUrl, version, _posterLoadCts.Token);
        }
    }

    public void SetLayout(double cardWidth, double posterHeight, double cardHeight)
    {
        if (Math.Abs(Width - cardWidth) < 0.5 && Math.Abs(_posterHeight - posterHeight) < 0.5)
            return;

        Width = cardWidth;
        Height = cardHeight;
        _posterHeight = posterHeight;
        _posterBackground.Width = cardWidth;
        _posterBackground.Height = posterHeight;
        _posterImage.Width = cardWidth;
        _posterImage.Height = posterHeight;
        _fallbackTitle.MaxWidth = Math.Max(80, cardWidth - 28);
        _titleText.Width = Math.Max(0, cardWidth - 8);
        _episodeTitleText.Width = Math.Max(0, cardWidth - 8);
        _subtitleText.Width = Math.Max(0, cardWidth - 8);
        SetTop(_titleText, posterHeight + 12);
        SetTop(_episodeTitleText, posterHeight + 34);
        SetTop(_subtitleText, posterHeight +
            (_episodeTitleText.Visibility == Visibility.Visible ? 56 : 34));
        SetLeft(_moreButton, cardWidth - 42);
        SetTop(_moreButton, posterHeight - 42);
    }

    public void BindPlaceholder()
    {
        CancelPosterLoad(clearImage: true);
        MediaItem = null;
        SortKey = null;
        IsHitTestVisible = false;
        Opacity = 0.55;
        _fallbackTitle.Text = "";
        _fallbackTitle.Visibility = Visibility.Visible;
        _titleText.Text = "";
        _episodeTitleText.Text = "";
        _episodeTitleText.Visibility = Visibility.Collapsed;
        _subtitleText.Text = "";
        _posterBackground.Background = Brush("CardBackgroundBrush");
        _moreButton.Opacity = 0;
        ResetHoverVisuals();
        ClearOverlays();
    }

    public void Reset()
    {
        CancelPosterLoad(clearImage: true);
        MediaItem = null;
        SortKey = null;
        IsHitTestVisible = false;
        Opacity = 1;
        _fallbackTitle.Text = "";
        _fallbackTitle.Visibility = Visibility.Visible;
        _titleText.Text = "";
        _episodeTitleText.Text = "";
        _episodeTitleText.Visibility = Visibility.Collapsed;
        _subtitleText.Text = "";
        _posterBackground.Background = Brush("CardBackgroundBrush");
        _moreButton.Opacity = 0;
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

            if (string.IsNullOrWhiteSpace(diskPath) || !IsCurrentPosterLoad(item, version, ct))
                return;

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
                _fallbackTitle.Visibility = Visibility.Collapsed;
            }
            finally
            {
                s_bitmapCreateLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private bool IsCurrentPosterLoad(MediaItem item, int version, CancellationToken ct)
    {
        return !ct.IsCancellationRequested &&
            version == _posterLoadVersion &&
            ReferenceEquals(MediaItem, item);
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

        App.Services.GetRequiredService<NavigationService>()
            .Navigate<ItemDetailPage>(MediaItem.ContentId);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _posterHoverTransform.ScaleX = 1.06;
        _posterHoverTransform.ScaleY = 1.06;
        _cardHoverTransform.TranslateY = -4;
        _hoverBrighten.Opacity = 1;
        _moreButton.Opacity = 1;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ResetHoverVisuals();
        _moreButton.Opacity = 0;
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

        var flyout = MediaItemMenu.Build(MediaItem, MediaItemMenu.Surface.Default);
        flyout.ShowAt(_moreButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight,
        });
    }

    private static StackPanel CreateOverlayHost(HorizontalAlignment horizontal, VerticalAlignment vertical) => new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 4,
        HorizontalAlignment = horizontal,
        VerticalAlignment = vertical,
        Margin = new Thickness(6),
        IsHitTestVisible = false,
    };

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
        _ = service.EnsureLoadedAsync();

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
                "classic"));
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
                    service.Preset));
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
                    service.Preset));
            }
            return;
        }

        var prefs = service.GetPrefs();
        if (prefs == null)
            return;

        var data = OverlayData.FromMediaItem(item);
        foreach (var def in OverlayRegistry.All)
        {
            if (!prefs.TryGetValue(def.Id, out var config) || !config.Enabled)
                continue;
            if (OverlayRegistry.SuppressesStandaloneOverlays(def.Id, prefs))
                continue;

            var value = def.GetValue(data);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var host = config.Position switch
            {
                OverlayPosition.TopLeft => _overlayTopLeft,
                OverlayPosition.TopRight => _overlayTopRight,
                OverlayPosition.BottomLeft => _overlayBottomLeft,
                OverlayPosition.BottomRight => _overlayBottomRight,
                _ => _overlayTopLeft,
            };
            host.Children.Add(PosterCard.BuildBadge(value, def.Id, config, service.Preset));
        }
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (MediaItem == null)
            return;

        var flyout = MediaItemMenu.Build(MediaItem, MediaItemMenu.Surface.Default);
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
}
