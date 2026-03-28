using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Views;

namespace ContinuumPlayer.Controls;

public sealed partial class PosterCard : UserControl
{
    private CancellationTokenSource? _loadCts;

    public static readonly DependencyProperty MediaItemProperty =
        DependencyProperty.Register(
            nameof(MediaItem),
            typeof(MediaItem),
            typeof(PosterCard),
            new PropertyMetadata(null, OnMediaItemChanged));

    public MediaItem? MediaItem
    {
        get => (MediaItem?)GetValue(MediaItemProperty);
        set => SetValue(MediaItemProperty, value);
    }

    public PosterCard()
    {
        this.InitializeComponent();
    }

    private static void OnMediaItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PosterCard card && e.NewValue is MediaItem item)
        {
            card.UpdateContent(item);
        }
    }

    private void UpdateContent(MediaItem item)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        TitleText.Text = item.Title;
        PosterImage.Opacity = 0;

        // Skip thumbhash -- go straight to loading the real image.
        // Thumbhash decoding on UI thread for hundreds of cards causes jank.
        ThumbhashImage.Source = null;

        UpdateBadges(item.OverlaySummary);

        // Delay image load slightly so scrolling isn't blocked by hundreds of simultaneous loads
        _ = LoadPosterAsync(item, ct);
    }

    private void UpdateBadges(OverlaySummary? overlay)
    {
        if (overlay == null ||
            (string.IsNullOrEmpty(overlay.Resolution) && string.IsNullOrEmpty(overlay.Audio)))
        {
            BadgesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        BadgesPanel.Visibility = Visibility.Visible;

        if (!string.IsNullOrEmpty(overlay.Resolution))
        {
            ResolutionBadge.Visibility = Visibility.Visible;
            ResolutionText.Text = overlay.Resolution;
        }
        else
        {
            ResolutionBadge.Visibility = Visibility.Collapsed;
        }

        if (!string.IsNullOrEmpty(overlay.Audio))
        {
            AudioBadge.Visibility = Visibility.Visible;
            AudioText.Text = overlay.Audio;
        }
        else
        {
            AudioBadge.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadPosterAsync(MediaItem item, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.PosterUrl)) return;

        try
        {
            // Small delay: if user is scrolling fast, this card will be cancelled
            // before we start the network request
            await Task.Delay(50, ct);
            if (ct.IsCancellationRequested) return;

            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "poster", item.PosterUrl, httpClient, ct);

            if (ct.IsCancellationRequested || bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                // Decode at display size, not full resolution -- huge perf win
                DecodePixelWidth = 180,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            if (ct.IsCancellationRequested) return;

            PosterImage.Source = bitmapImage;
            PosterImage.Opacity = 1;
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void OnCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;

        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<ItemDetailPage>(MediaItem.ContentId);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        PosterBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["SurfaceHoverBrush"];
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        PosterBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["CardBackgroundBrush"];
    }
}
