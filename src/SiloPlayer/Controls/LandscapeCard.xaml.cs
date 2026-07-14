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
        this.Unloaded += (_, _) =>
        {
            try { _loadCts?.Cancel(); } catch { }
            _loadCts?.Dispose();
            _loadCts = null;
            BackdropImage.Source = null;
        };
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
        UpdateDismissVisibility();
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
                epLine += $" \u2022 {item.Title}";
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

        var isNextUp = item.ItemSource == "next_up";
        if (isNextUp)
        {
            TimeLeftText.Text = badgeLabel == null ? "Next Episode" : "";
            TimeLeftText.Visibility = badgeLabel == null ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (item.PositionSeconds.HasValue && item.DurationSeconds.HasValue && item.DurationSeconds.Value > 0)
        {
            var remainingMinutes = Math.Max(0, (int)Math.Round(
                (item.DurationSeconds.Value - item.PositionSeconds.Value) / 60));
            TimeLeftText.Text = item.Type == "ebook"
                ? $"{Math.Round(Math.Clamp(item.PositionSeconds.Value / item.DurationSeconds.Value, 0, 1) * 100)}% read"
                : $"{remainingMinutes} min left";
            TimeLeftText.Visibility = Visibility.Visible;
        }
        else
        {
            TimeLeftText.Visibility = Visibility.Collapsed;
        }

        // Progress bar (Next Up never renders resume progress).
        if (!isNextUp && item.PositionSeconds.HasValue && item.DurationSeconds.HasValue && item.DurationSeconds.Value > 0)
        {
            double progress = item.PositionSeconds.Value / item.DurationSeconds.Value;
            progress = Math.Clamp(progress, 0, 1);

            ProgressContainer.Visibility = Visibility.Visible;
            ProgressFill.Width = 315 * progress;
        }
        else
        {
            ProgressContainer.Visibility = Visibility.Collapsed;
        }
        RemainingBadge.Visibility = Visibility.Collapsed;

        UpdateBadges(item);
        _ = EnsureBadgesLoadedAsync(item, ct);

        BackdropImage.Opacity = 0;

        _ = LoadImageAsync(item, ct);
    }

    private async Task EnsureBadgesLoadedAsync(MediaItem item, CancellationToken ct)
    {
        try
        {
            var service = App.Services.GetRequiredService<Services.CardOverlayService>();
            await service.EnsureLoadedAsync();
            if (!ct.IsCancellationRequested && ReferenceEquals(MediaItem, item))
                UpdateBadges(item);
        }
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
        foreach (var definition in Services.OverlayRegistry.All)
        {
            if (!prefs.TryGetValue(definition.Id, out var config) || !config.Enabled) continue;
            if (Services.OverlayRegistry.SuppressesStandaloneOverlays(definition.Id, prefs)) continue;
            var value = definition.GetValue(data);
            if (string.IsNullOrWhiteSpace(value)) continue;
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
        }
    }

    private async Task LoadImageAsync(MediaItem item, CancellationToken ct)
    {
        // Current WebUI wide cards use backdrop_url first. Section episode
        // payloads now reserve poster_url for vertical series/season artwork
        // and backdrop_url for the episode still.
        var usesBackdrop = !string.IsNullOrEmpty(item.BackdropUrl);
        var imageUrl = usesBackdrop ? item.BackdropUrl : item.PosterUrl;
        if (string.IsNullOrEmpty(imageUrl)) return;

        try
        {
            await Task.Delay(300, ct);
            if (ct.IsCancellationRequested) return;

            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var imageType = usesBackdrop ? "backdrop" : "poster";

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
                DecodePixelWidth = 630,
                DecodePixelType = DecodePixelType.Logical,
                UriSource = new Uri(diskPath),
            };
            BackdropImage.Source = bitmapImage;
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
        ContextFlyout?.ShowAt(DismissButton);
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
