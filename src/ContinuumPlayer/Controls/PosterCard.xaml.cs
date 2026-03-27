using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;

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
        // Cancel any previous load
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        TitleText.Text = item.Title;
        PosterImage.Opacity = 0;

        // Decode thumbhash placeholder
        if (!string.IsNullOrEmpty(item.PosterThumbhash))
        {
            try
            {
                var decoded = ThumbhashDecoder.Decode(item.PosterThumbhash);
                var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);

                var bgra = new byte[decoded.Rgba.Length];
                for (int i = 0; i < decoded.Rgba.Length; i += 4)
                {
                    bgra[i] = decoded.Rgba[i + 2];     // B
                    bgra[i + 1] = decoded.Rgba[i + 1]; // G
                    bgra[i + 2] = decoded.Rgba[i];     // R
                    bgra[i + 3] = decoded.Rgba[i + 3]; // A
                }

                bgra.CopyTo(bitmap.PixelBuffer);
                bitmap.Invalidate();
                ThumbhashImage.Source = bitmap;
            }
            catch
            {
                ThumbhashImage.Source = null;
            }
        }
        else
        {
            ThumbhashImage.Source = null;
        }

        // Show overlay badges
        UpdateBadges(item.OverlaySummary);

        // Start async image load
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
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "poster", item.PosterUrl, httpClient, ct);

            if (ct.IsCancellationRequested || bytes == null) return;

            var bitmapImage = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            if (ct.IsCancellationRequested) return;

            PosterImage.Source = bitmapImage;
            PosterImage.Opacity = 1;
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch
        {
            // Image load failed, thumbhash placeholder remains
        }
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
