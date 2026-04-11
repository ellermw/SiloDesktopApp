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
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        TitleText.Text = item.Title;

        // Build subtitle line: "2024 Series" or "2024" (web: year + type in uppercase)
        var parts = new List<string>();
        if (item.Year > 0) parts.Add(item.Year.ToString());
        if (item.Type == "series") parts.Add("SERIES");
        SubtitleText.Text = string.Join("  ", parts);

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

    // Limit concurrent image decodes on the UI thread to prevent bursts of
    // SetSourceAsync calls from freezing the app during fast scrolling.
    private static readonly SemaphoreSlim s_decodeLock = new(4);

    private async Task LoadPosterAsync(MediaItem item, CancellationToken ct)
    {
        var imageUrl = !string.IsNullOrEmpty(item.PosterUrl) ? item.PosterUrl : item.BackdropUrl;
        if (string.IsNullOrEmpty(imageUrl)) return;

        try
        {
            // Small delay: if user is scrolling fast, this card will be cancelled
            // before we start the network request
            await Task.Delay(50, ct);
            if (ct.IsCancellationRequested) return;

            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var imageType = !string.IsNullOrEmpty(item.PosterUrl) ? "poster" : "backdrop";

            // Run all I/O on a background thread — File.Exists, disk reads,
            // and HTTP downloads were previously running on the UI thread
            var bytes = await Task.Run(async () =>
                await imageService.GetImageAsync(
                    item.ContentId, imageType, imageUrl, httpClient, ct), ct);

            if (ct.IsCancellationRequested || bytes == null) return;

            // Throttle concurrent image decodes on the UI thread
            await s_decodeLock.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested) return;

                var bitmapImage = new BitmapImage
                {
                    // Decode at display size, not full resolution -- huge perf win
                    DecodePixelWidth = 200,
                    DecodePixelType = DecodePixelType.Logical
                };
                using var stream = new MemoryStream(bytes);
                await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

                if (ct.IsCancellationRequested) return;

                PosterImage.Source = bitmapImage;
                PosterImage.Opacity = 1;
            }
            finally
            {
                s_decodeLock.Release();
            }
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
