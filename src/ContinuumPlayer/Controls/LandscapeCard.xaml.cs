using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Views;

namespace ContinuumPlayer.Controls;

public sealed partial class LandscapeCard : UserControl
{
    private CancellationTokenSource? _loadCts;

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

    public LandscapeCard()
    {
        this.InitializeComponent();
    }

    private static void OnMediaItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LandscapeCard card && e.NewValue is MediaItem item)
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

        // Build subtitle: "Series Title · S1 E3"
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(item.SeriesTitle))
            parts.Add(item.SeriesTitle);
        if (item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
            parts.Add($"S{item.SeasonNumber} E{item.EpisodeNumber}");
        else if (item.EpisodeNumber.HasValue)
            parts.Add($"E{item.EpisodeNumber}");

        if (parts.Count > 0)
        {
            SubtitleText.Text = string.Join(" \u00B7 ", parts);
            SubtitleText.Visibility = Visibility.Visible;
        }
        else
        {
            SubtitleText.Visibility = Visibility.Collapsed;
        }

        // Progress bar
        if (item.PositionSeconds.HasValue && item.DurationSeconds.HasValue && item.DurationSeconds.Value > 0)
        {
            double progress = item.PositionSeconds.Value / item.DurationSeconds.Value;
            progress = Math.Clamp(progress, 0, 1);

            ProgressContainer.Visibility = Visibility.Visible;
            ProgressFill.Width = 280 * progress;

            // Remaining time
            double remainingSeconds = item.DurationSeconds.Value - item.PositionSeconds.Value;
            if (remainingSeconds > 0)
            {
                var remaining = TimeSpan.FromSeconds(remainingSeconds);
                RemainingText.Text = remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m left"
                    : $"{remaining.Minutes}m left";
                RemainingBadge.Visibility = Visibility.Visible;
            }
            else
            {
                RemainingBadge.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            ProgressContainer.Visibility = Visibility.Collapsed;
            RemainingBadge.Visibility = Visibility.Collapsed;
        }

        BackdropImage.Opacity = 0;

        _ = LoadImageAsync(item, ct);
    }

    private async Task LoadImageAsync(MediaItem item, CancellationToken ct)
    {
        // Prefer backdrop, fall back to poster
        var imageUrl = !string.IsNullOrEmpty(item.BackdropUrl) ? item.BackdropUrl : item.PosterUrl;
        if (string.IsNullOrEmpty(imageUrl)) return;

        try
        {
            await Task.Delay(50, ct);
            if (ct.IsCancellationRequested) return;

            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var imageType = !string.IsNullOrEmpty(item.BackdropUrl) ? "backdrop" : "poster";
            var bytes = await imageService.GetImageAsync(
                item.ContentId, imageType, imageUrl, httpClient, ct);

            if (ct.IsCancellationRequested || bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = 280,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            if (ct.IsCancellationRequested) return;

            BackdropImage.Source = bitmapImage;
            BackdropImage.Opacity = 1;
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void OnCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;

        var nav = App.Services.GetRequiredService<NavigationService>();
        // For TV episodes, navigate to the series detail page (which has full cast/crew/poster)
        // rather than the sparse episode detail
        var targetId = !string.IsNullOrEmpty(MediaItem.SeriesId)
            ? MediaItem.SeriesId
            : MediaItem.ContentId;
        nav.Navigate<ItemDetailPage>(targetId);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CardBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["SurfaceHoverBrush"];
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        CardBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["CardBackgroundBrush"];
    }
}
