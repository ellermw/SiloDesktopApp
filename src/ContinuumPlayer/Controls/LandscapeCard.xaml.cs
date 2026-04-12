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

    /// <summary>
    /// Fired when the user clicks the X dismiss button on a Continue
    /// Watching / Next Up card. The consumer (HomePage) handles the
    /// actual dismissal via HomeViewModel.DismissItemCommand.
    /// </summary>
    public event EventHandler<MediaItem>? DismissRequested;

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

        // F10: attach right-click media menu. LandscapeCard is used for
        // Continue Watching / Next Up sections, so surface-specific Dismiss
        // entries show up automatically based on ItemSource.
        var surface = item.ItemSource switch
        {
            "continue_watching" => MediaItemMenu.Surface.ContinueWatching,
            "next_up" => MediaItemMenu.Surface.NextUp,
            _ => MediaItemMenu.Surface.Default,
        };
        this.ContextFlyout = MediaItemMenu.Build(item, surface);

        // Show dismiss X button for CW/NU cards — enables quick-dismiss
        // from the home screen without opening a context menu.
        bool canDismiss = item.ItemSource is "continue_watching" or "next_up";
        DismissButton.Visibility = canDismiss ? Visibility.Visible : Visibility.Collapsed;
        DismissButton.Opacity = 0; // starts invisible, fades in on hover

        // B31: Match the webui ContinueWatchingCard hierarchy. For episodes,
        // the series title is the primary heading and the episode context
        // ("Season 1 Episode 1 · Pilot") is the secondary line. Movies fall
        // through to title-as-heading with no subtitle.
        bool hasEpisodeMeta = item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue;
        if (hasEpisodeMeta && !string.IsNullOrEmpty(item.SeriesTitle))
        {
            TitleText.Text = item.SeriesTitle!;
            string epLine = $"Season {item.SeasonNumber} Episode {item.EpisodeNumber}";
            if (!string.IsNullOrEmpty(item.Title) && item.Title != item.SeriesTitle)
                epLine += $" \u00B7 {item.Title}";
            SubtitleText.Text = epLine;
            SubtitleText.Visibility = Visibility.Visible;
        }
        else
        {
            TitleText.Text = item.Title;
            SubtitleText.Visibility = Visibility.Collapsed;
        }

        // Badge pill (e.g. "SEASON PREMIERE") — mirrors WebUI ContinueWatchingCard.
        // Server attaches badge strings to section items; we render the first known one.
        var badgeLabel = GetBadgeLabel(item);
        if (badgeLabel != null)
        {
            BadgeText.Text = badgeLabel;
            BadgePill.Visibility = Visibility.Visible;
        }
        else
        {
            BadgePill.Visibility = Visibility.Collapsed;
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

    // B32: image click → play, text click → details. Mirrors the webui
    // ContinueWatchingCard dual-link pattern (image <Link to="/watch/{id}">,
    // text <Link to="/item/{id}">).

    private void OnImageTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;
        e.Handled = true;
        var playerService = App.Services.GetRequiredService<Services.PlayerService>();
        // Always play the actual content_id on the card — the server resolves
        // episode → file; we don't rewrite to series_id here (that would break
        // resume for the specific episode the card represents).
        _ = playerService.PlayAsync(MediaItem.ContentId);
    }

    private void OnTextTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;
        e.Handled = true;
        var nav = App.Services.GetRequiredService<NavigationService>();
        // For TV episodes, navigate to the series detail page (which has full
        // cast/crew/poster) rather than the sparse episode detail.
        var targetId = !string.IsNullOrEmpty(MediaItem.SeriesId)
            ? MediaItem.SeriesId
            : MediaItem.ContentId;
        nav.Navigate<ItemDetailPage>(targetId);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CardBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["SurfaceHoverBrush"];
        AnimateHover(scale: 1.04, borderOpacity: 1.0, dimOpacity: 1.0, playOpacity: 1.0, playScale: 1.0, dismissOpacity: 1.0);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        CardBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["CardBackgroundBrush"];
        AnimateHover(scale: 1.0, borderOpacity: 0.0, dimOpacity: 0.0, playOpacity: 0.0, playScale: 0.7, dismissOpacity: 0.0);
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        if (MediaItem != null) DismissRequested?.Invoke(this, MediaItem);
    }

    /// <summary>
    /// Mirrors the webui ContinueWatchingCard hover: subtle card scale, accent
    /// border glow, dark tint overlay, and a centered Play circle that fades
    /// and scales in. Short ease-out curve matching the webui transition timing.
    /// </summary>
    private void AnimateHover(double scale, double borderOpacity, double dimOpacity, double playOpacity, double playScale, double dismissOpacity = 0)
    {
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };

        void Add(DependencyObject target, string prop, double to)
        {
            var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, target);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, prop);
            storyboard.Children.Add(anim);
        }

        Add(HoverTransform, "ScaleX", scale);
        Add(HoverTransform, "ScaleY", scale);
        Add(HoverBorder, "Opacity", borderOpacity);
        Add(HoverDim, "Opacity", dimOpacity);
        Add(HoverPlayButton, "Opacity", playOpacity);
        Add(HoverPlayButtonTransform, "ScaleX", playScale);
        Add(HoverPlayButtonTransform, "ScaleY", playScale);
        if (DismissButton.Visibility == Visibility.Visible)
            Add(DismissButton, "Opacity", dismissOpacity);

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
