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

        // F10: attach right-click media menu (mark watched / favorite /
        // watchlist / admin refresh / view details). Rebuilt on each item
        // change so "Remove from Favorites" vs "Add" reflects fresh state.
        this.ContextFlyout = MediaItemMenu.Build(item, MediaItemMenu.Surface.Default);

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

        // Fallback title: when no poster URL is available, show the item's
        // title centered on the card background (webui: line-clamp-3).
        string? imageUrl = !string.IsNullOrEmpty(item.PosterUrl) ? item.PosterUrl : item.BackdropUrl;
        if (string.IsNullOrEmpty(imageUrl))
        {
            FallbackTitle.Text = item.Title;
            FallbackTitle.Visibility = Visibility.Visible;
        }
        else
        {
            FallbackTitle.Visibility = Visibility.Collapsed;
        }

        UpdateBadges(item);

        // Delay image load slightly so scrolling isn't blocked by hundreds of simultaneous loads
        _ = LoadPosterAsync(item, ct);
    }

    /// <summary>
    /// Rebuilds the 4 corner overlay panels for the given item, using prefs
    /// from <see cref="Services.CardOverlayService"/>. All 9 overlay types
    /// from the webui are supported; each one is shown only if:
    ///   1. Admin kill switch (<c>overlays.enabled</c>) is on
    ///   2. That overlay id is enabled in the resolved prefs
    ///   3. The value extractor returns a non-null, non-empty string
    /// Also kicks off a one-shot prefs fetch on the first card bind.
    /// </summary>
    private void UpdateBadges(MediaItem item)
    {
        OverlayTopLeft.Children.Clear();
        OverlayTopRight.Children.Clear();
        OverlayBottomLeft.Children.Clear();
        OverlayBottomRight.Children.Clear();

        var service = App.Services.GetRequiredService<Services.CardOverlayService>();
        // Lazy one-shot load. Subsequent cards hit the cached result.
        _ = service.EnsureLoadedAsync();

        var prefs = service.GetPrefs();
        if (prefs == null) return; // Admin kill switch engaged → no badges.

        var data = Services.OverlayData.FromMediaItem(item);
        foreach (var def in Services.OverlayRegistry.All)
        {
            if (!prefs.TryGetValue(def.Id, out var config) || !config.Enabled) continue;
            var value = def.GetValue(data);
            if (string.IsNullOrEmpty(value)) continue;

            var badge = BuildBadge(value, def.Id);
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

    /// <summary>
    /// One pill-shaped overlay badge. All overlays share the same dark glass
    /// style (rgba(0,0,0,0.6) + white/15 border + uppercase white text) —
    /// matches webui BADGE_CLASS + BADGE_STYLE from lib/cardOverlays.ts.
    /// Uniform styling is critical for legibility over bright posters;
    /// earlier resolution-only accent style meant HDR/Audio/Release/etc.
    /// were invisible on light backdrops.
    /// </summary>
    private static Border BuildBadge(string text, string overlayId)
    {
        // Fixed height + CornerRadius=height/2 for a clean pill. WinUI's
        // CornerRadius doesn't behave like CSS rounded-full — a huge radius
        // value produces a warped oval, so we use the explicit-height
        // pattern instead (see feedback_no_appearance_changes / memory).
        return new Border
        {
            Height = 18,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardOverlayBackgroundBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardOverlayBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                CharacterSpacing = 60,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
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
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, PosterImage);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
                var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                sb.Children.Add(fadeIn);
                sb.Begin();
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
        AnimateHover(scale: 1.04, borderOpacity: 1.0, dimOpacity: 1.0, playOpacity: 1.0, playScale: 1.0);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        PosterBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["CardBackgroundBrush"];
        AnimateHover(scale: 1.0, borderOpacity: 0.0, dimOpacity: 0.0, playOpacity: 0.0, playScale: 0.7);
    }

    /// <summary>
    /// Mirrors the webui ContinueWatchingCard hover: subtle card scale, accent
    /// border glow, dark tint overlay, and a centered Play circle that fades
    /// and scales in. Short ease-out curve matching the webui transition timing.
    /// </summary>
    private void AnimateHover(double scale, double borderOpacity, double dimOpacity, double playOpacity, double playScale)
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

        storyboard.Begin();
    }
}
