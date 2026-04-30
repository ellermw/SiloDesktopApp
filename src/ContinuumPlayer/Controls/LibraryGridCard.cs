using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Views;

namespace ContinuumPlayer.Controls;

public sealed class LibraryGridCard : Canvas
{
    private readonly Border _posterBackground;
    private readonly Image _posterImage;
    private readonly TextBlock _fallbackTitle;
    private readonly TextBlock _titleText;
    private readonly TextBlock _subtitleText;
    private CancellationTokenSource? _posterLoadCts;
    private int _posterLoadVersion;

    private static readonly AsyncWorkThrottle s_imageLoadThrottle = new(maxConcurrency: 8);
    private static readonly SemaphoreSlim s_bitmapCreateLock = new(1);

    public MediaItem? MediaItem { get; private set; }
    public string? SortKey { get; private set; }

    public LibraryGridCard()
    {
        var cardWidth = (double)Application.Current.Resources["PosterCardWidth"];
        var posterHeight = (double)Application.Current.Resources["PosterCardHeight"];
        var cardHeight = (double)Application.Current.Resources["PosterCardTotalHeight"];

        Width = cardWidth;
        Height = cardHeight;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

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
        };

        var posterHost = new Grid();
        posterHost.Children.Add(_posterImage);
        posterHost.Children.Add(_fallbackTitle);

        _posterBackground = new Border
        {
            Width = cardWidth,
            Height = posterHeight,
            Background = Brush("CardBackgroundBrush"),
            CornerRadius = (CornerRadius)Application.Current.Resources["PosterCornerRadius"],
            Child = posterHost,
        };
        SetLeft(_posterBackground, 0);
        SetTop(_posterBackground, 0);
        Children.Add(_posterBackground);

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
        SetLeft(_titleText, 4);
        SetTop(_titleText, posterHeight + 12);
        Children.Add(_titleText);

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

        MediaItem = item;
        SortKey = sortKey;
        IsHitTestVisible = true;
        Opacity = 1;

        var title = string.IsNullOrWhiteSpace(item.Title) ? "Untitled" : item.Title;
        _fallbackTitle.Text = title;
        _fallbackTitle.Visibility = Visibility.Visible;
        _titleText.Text = title;
        _subtitleText.Text = MediaItemDisplayText.BuildSubtitle(item, sortKey);

        var imageUrl = !string.IsNullOrWhiteSpace(item.PosterUrl) ? item.PosterUrl : item.BackdropUrl;
        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            _posterLoadCts = new CancellationTokenSource();
            var version = ++_posterLoadVersion;
            _ = LoadPosterAsync(item, imageUrl, version, _posterLoadCts.Token);
        }
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
        _subtitleText.Text = "";
        _posterBackground.Background = Brush("CardBackgroundBrush");
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
        _subtitleText.Text = "";
        _posterBackground.Background = Brush("CardBackgroundBrush");
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
                    DecodePixelWidth = 200,
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
        _posterBackground.Background = Brush("SurfaceHoverBrush");
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _posterBackground.Background = Brush("CardBackgroundBrush");
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
