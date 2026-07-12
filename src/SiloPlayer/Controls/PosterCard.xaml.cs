using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public sealed partial class PosterCard : UserControl
{
    private CancellationTokenSource? _loadCts;
    private MediaItem? _deferredPosterItem;
    private MediaItem? _deferredOverlayItem;

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

    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(bool), typeof(PosterCard),
            new PropertyMetadata(false, OnSelectionStateChanged));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(PosterCard),
            new PropertyMetadata(false, OnSelectionStateChanged));

    public bool SelectionMode
    {
        get => (bool)GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public event EventHandler? SelectionToggled;

    private static void OnSelectionStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PosterCard card)
            card.SelectionBadge.Visibility = card.SelectionMode && card.IsSelected
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>
    /// App-wide sort context consulted by PosterCard to choose its meta line.
    /// LibraryPage sets this before reloading cards so each card can render a
    /// sort-appropriate secondary line (e.g. IMDb rating when sorted by
    /// rating_imdb instead of "Year · SERIES"). Null / empty = default.
    /// </summary>
    public static string? CurrentSortKey { get; set; }

    public bool DeferImageLoading { get; set; }
    public bool DeferOverlayLoading { get; set; }
    public bool SuppressImageLoading { get; set; }

    public PosterCard()
    {
        this.InitializeComponent();
        // When ItemsRepeater recycles the card out of the viewport, Unloaded
        // fires. Cancel any in-flight image download and release the decoded
        // BitmapImage so its pixel data can be garbage-collected instead of
        // piling up across 100k-scale libraries.
        this.Unloaded += (_, _) =>
        {
            try { _loadCts?.Cancel(); } catch { }
            _loadCts?.Dispose();
            _loadCts = null;
            PosterImage.Source = null;
            ThumbhashImage.Source = null;
        };
        // Lazy context menu build — eager MediaItemMenu.Build() on every
        // UpdateContent was the single biggest per-recycle cost. Now we only
        // build the flyout when the user actually right-clicks (or long-
        // presses), which during fast library scroll saves ~8 MenuFlyoutItem
        // constructions + closures per recycled card.
        this.ContextRequested += PosterCard_ContextRequested;
    }

    private void PosterCard_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (MediaItem == null) return;
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

    private static void OnMediaItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PosterCard card) return;

        if (e.NewValue is MediaItem item)
        {
            card.UpdateContent(item);
        }
        else
        {
            card.ShowPlaceholder();
        }
    }

    private void ShowPlaceholder()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _deferredPosterItem = null;

        TitleText.Text = "";
        SubtitleText.Text = "";
        FallbackTitle.Text = "";
        FallbackTitle.Visibility = Visibility.Collapsed;
        PosterImage.Source = null;
        PosterImage.Opacity = 0;
        ThumbhashImage.Source = null;
        ClearOverlayPanels();
    }

    private void ClearOverlayPanels()
    {
        OverlayTopLeft.Children.Clear();
        OverlayTopRight.Children.Clear();
        OverlayBottomLeft.Children.Clear();
        OverlayBottomRight.Children.Clear();
    }

    private void UpdateContent(MediaItem item)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        // Right-click flyout is built lazily by PosterCard_ContextRequested —
        // building ~8 MenuFlyoutItems per card-recycle was the biggest scroll
        // stall in large libraries.

        TitleText.Text = item.Title;

        // Build subtitle line — sort-driven. When the hosting surface (Library,
        // Search, etc.) has sorted by a specific key, surface that key's value
        // so the meta line matches what the user is looking at.
        var parts = new List<string>();
        if (item.Year > 0) parts.Add(item.Year.ToString());
        switch (CurrentSortKey)
        {
            case "rating_imdb":
                if (item.RatingImdb.HasValue && item.RatingImdb.Value > 0)
                    parts.Add($"\u2605 {item.RatingImdb.Value:0.0}");
                else if (item.Type == "series")
                    parts.Add("SERIES");
                break;
            default:
                if (item.Type == "series") parts.Add("SERIES");
                break;
        }
        SubtitleText.Text = string.Join("  ", parts);

        PosterImage.Opacity = 0;

        // Skip thumbhash -- go straight to loading the real image.
        // Thumbhash decoding on UI thread for hundreds of cards causes jank.
        ThumbhashImage.Source = null;

        // Fallback title: when no poster URL is available, show the item's
        // title centered on the card background (webui: line-clamp-3).
        string? imageUrl = !string.IsNullOrEmpty(item.PosterUrl) ? item.PosterUrl : item.BackdropUrl;
        if (SuppressImageLoading || string.IsNullOrEmpty(imageUrl))
        {
            FallbackTitle.Text = item.Title;
            FallbackTitle.Visibility = Visibility.Visible;
            if (SuppressImageLoading)
                PosterImage.Source = null;
        }
        else
        {
            FallbackTitle.Visibility = Visibility.Collapsed;
        }

        if (DeferOverlayLoading)
        {
            ClearOverlayPanels();
            _deferredOverlayItem = item;
        }
        else
        {
            _deferredOverlayItem = null;
            UpdateBadges(item);
        }

        if (SuppressImageLoading)
        {
            _deferredPosterItem = null;
            return;
        }

        if (DeferImageLoading)
        {
            _deferredPosterItem = item;
            return;
        }

        _deferredPosterItem = null;
        _ = LoadPosterAsync(item, ct);
    }

    public void LoadDeferredPoster()
    {
        if (SuppressImageLoading)
        {
            _deferredPosterItem = null;
            return;
        }

        if (_deferredPosterItem == null || !ReferenceEquals(_deferredPosterItem, MediaItem))
            return;

        var item = _deferredPosterItem;
        _deferredPosterItem = null;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        _ = LoadPosterAsync(item, _loadCts.Token);
    }

    public void LoadDeferredOverlays()
    {
        if (_deferredOverlayItem == null || !ReferenceEquals(_deferredOverlayItem, MediaItem))
            return;

        var item = _deferredOverlayItem;
        _deferredOverlayItem = null;
        UpdateBadges(item);
    }

    public void DeferCurrentPosterLoad()
    {
        if (SuppressImageLoading || MediaItem == null || PosterImage.Source != null)
            return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        _deferredPosterItem = MediaItem;
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
            if (Services.OverlayRegistry.SuppressesStandaloneOverlays(def.Id, prefs)) continue;
            var value = def.GetValue(data);
            if (string.IsNullOrEmpty(value)) continue;

            var badge = BuildBadge(value, def.Id, config, service.Preset);
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
    internal static Border BuildBadge(
        string text,
        string overlayId,
        Services.OverlayItemConfig config,
        string preset)
    {
        var accent = ParseOverlayColor(config.AccentColor ?? DefaultOverlayAccent(overlayId));
        var background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardOverlayBackgroundBrush"];
        var borderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardOverlayBorderBrush"];
        var foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        var height = 18d;
        var radius = 9d;
        var padding = new Thickness(8, 0, 8, 0);
        var fontSize = 10d;
        var borderThickness = new Thickness(1);

        switch (preset)
        {
            case "minimal":
                background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                borderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                foreground = accent ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(217, 255, 255, 255));
                height = 14;
                radius = 2;
                padding = new Thickness(2, 0, 2, 0);
                fontSize = 9;
                borderThickness = new Thickness(0);
                break;
            case "vibrant":
                background = accent ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(242, 220, 220, 220));
                borderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                foreground = accent is null
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                radius = 4;
                borderThickness = new Thickness(0);
                break;
            case "pill":
                background = accent is null
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(179, 20, 20, 30))
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(115, accent.Color.R, accent.Color.G, accent.Color.B));
                height = 22;
                radius = 11;
                padding = new Thickness(10, 0, 10, 0);
                break;
            case "square":
                background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 0, 0, 0));
                foreground = accent ?? foreground;
                radius = 2;
                padding = new Thickness(6, 0, 6, 0);
                fontSize = 9;
                borderBrush = accent ?? borderBrush;
                borderThickness = accent is null ? new Thickness(0) : new Thickness(2, 0, 0, 0);
                break;
            default:
                if (accent is not null)
                    background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.ColorHelper.FromArgb(110, accent.Color.R, accent.Color.G, accent.Color.B));
                break;
        }

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var preferIcon = preset is "vibrant" or "pill";
        if ((config.ShowIcon ?? preferIcon) && OverlayGlyph(overlayId) is { } glyph)
        {
            content.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = Math.Max(8, fontSize + 1),
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        content.Children.Add(new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = fontSize,
            FontWeight = preset == "vibrant" ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = preset is "minimal" or "square" ? 100 : 60,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Border
        {
            Height = height,
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = borderThickness,
            CornerRadius = new CornerRadius(radius),
            Padding = padding,
            VerticalAlignment = VerticalAlignment.Center,
            Child = content,
        };
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush? ParseOverlayColor(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit))
            return null;
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
            255,
            Convert.ToByte(value.Substring(1, 2), 16),
            Convert.ToByte(value.Substring(3, 2), 16),
            Convert.ToByte(value.Substring(5, 2), 16)));
    }

    private static string? DefaultOverlayAccent(string overlayId) => overlayId switch
    {
        "rating_imdb" or "imdb_top_250" => "#f5c518",
        "rating_tmdb" => "#01b4e4",
        "rating_rt" or "rt_certified_fresh" => "#fa320a",
        "rating_rt_audience" => "#fa6400",
        _ => null,
    };

    private static string? OverlayGlyph(string overlayId) => overlayId switch
    {
        "resolution" or "resolution_hdr" or "hdr" or "video_codec" => "\uE7F4",
        "audio" or "audio_channels" => "\uE767",
        "multi_audio" or "original_language" => "\uE8C1",
        "multi_sub" => "\uED1E",
        "rating_imdb" or "rating_tmdb" or "rating_rt" or "rating_rt_audience" => "\uE734",
        "runtime" => "\uE823",
        "year" => "\uE787",
        "studio" or "network" => "\uE80F",
        "content_rating" => "\uE72E",
        "show_status" or "imdb_top_250" or "rt_certified_fresh" => "\uE735",
        "container" or "release_type" or "edition" => "\uE8B7",
        "aspect_ratio" => "\uE740",
        _ => null,
    };

    // Global BitmapImage instantiation lock. The NATIVE decoder invoked by
    // BitmapImage(UriSource) appears to serialize on a shared mutex inside
    // WinUI composition. When 30+ cards try to create BitmapImages at once,
    // the contention spikes UI-thread time to multi-second bursts. Serialize
    // to one at a time across all cards. Creation itself is cheap; the
    // actual decode happens async in native code regardless.
    private static readonly SemaphoreSlim s_bitmapCreateLock = new(1);
    private static readonly SemaphoreSlim s_imageLoadLock = new(10);

    private async Task LoadPosterAsync(MediaItem item, CancellationToken ct)
    {
        var imageUrl = !string.IsNullOrEmpty(item.PosterUrl) ? item.PosterUrl : item.BackdropUrl;
        if (string.IsNullOrEmpty(imageUrl)) return;

        try
        {
            // Short grace window: skip transient recycle churn without making
            // the visible page wait noticeably before poster fetches begin.
            await Task.Delay(60, ct);
            if (ct.IsCancellationRequested) return;

            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var imageType = !string.IsNullOrEmpty(item.PosterUrl) ? "poster" : "backdrop";

            await s_imageLoadLock.WaitAsync(ct);
            string? diskPath;
            try
            {
                diskPath = await imageService.GetImageDiskPathAsync(
                    item.ContentId, imageType, imageUrl, httpClient, ct);
            }
            finally
            {
                s_imageLoadLock.Release();
            }

            if (ct.IsCancellationRequested || string.IsNullOrEmpty(diskPath)) return;

            await s_bitmapCreateLock.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested) return;
                var bitmapImage = new BitmapImage
                {
                    DecodePixelWidth = 200,
                    DecodePixelType = DecodePixelType.Logical,
                    UriSource = new Uri(diskPath),
                };
                PosterImage.Source = bitmapImage;
                PosterImage.Opacity = 1;
            }
            finally { s_bitmapCreateLock.Release(); }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void OnCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (MediaItem == null) return;

        if (SelectionMode)
        {
            IsSelected = !IsSelected;
            SelectionToggled?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<ItemDetailPage>(MediaItem.ContentId);
    }

    // Hover animations are deferred: only fire if the pointer stays over the
    // card for longer than the debounce. Fast scroll-through (where the
    // pointer sweeps across many cards in <80ms each) cancels before any
    // animation is created, avoiding a storm of ~7 DoubleAnimations per card
    // that was freezing the UI thread for seconds at a time.
    private DispatcherTimer? _hoverEnterTimer;
    private bool _hoverActive;

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hoverEnterTimer?.Stop();
        _hoverEnterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _hoverEnterTimer.Tick += (_, _) =>
        {
            _hoverEnterTimer?.Stop();
            _hoverEnterTimer = null;
            _hoverActive = true;
            PosterBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["SurfaceHoverBrush"];
            AnimateHover(scale: 1.04, borderOpacity: 1.0, dimOpacity: 1.0, playOpacity: 1.0, playScale: 1.0);
        };
        _hoverEnterTimer.Start();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // If the pointer never stayed long enough to commit a hover, just
        // cancel the pending timer — nothing to animate back.
        _hoverEnterTimer?.Stop();
        _hoverEnterTimer = null;
        if (!_hoverActive) return;
        _hoverActive = false;
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
