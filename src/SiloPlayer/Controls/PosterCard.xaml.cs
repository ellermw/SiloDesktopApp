using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public sealed partial class PosterCard : UserControl
{
    private readonly UICustomizationService _uiCustomizationService;
    private bool _observingUICustomization;
    private CardOverlayGeometry _overlayGeometry = CardOverlayGeometry.ForPoster(178);

    public void SetCatalogGridLayout(double width)
    {
        var safeWidth = Math.Max(96, width);
        var posterHeight = safeWidth * 1.5;
        // Repeater rows and cards must reserve the same caption space;
        // a shorter row clips the year/type line even when the card is taller.
        var captionHeight = _uiCustomizationService.CardCaptionHeight;
        var totalHeight = posterHeight + captionHeight;
        Width = safeWidth;
        Height = totalHeight;
        RootGrid.Width = safeWidth;
        RootGrid.Height = totalHeight;
        PosterRow.Height = new GridLength(posterHeight);
        FallbackTitle.MaxWidth = Math.Max(72, safeWidth - 32);

        var geometry = CardOverlayGeometry.ForPoster(safeWidth);
        if (Math.Abs(geometry.Scale - _overlayGeometry.Scale) >= 0.001)
        {
            _overlayGeometry = geometry;
            ApplyOverlayGeometry();
            if (MediaItem is { } item)
                UpdateBadges(item);
        }
    }

    private void ApplyOverlayGeometry()
    {
        foreach (var host in new[] { OverlayTopLeft, OverlayTopRight, OverlayBottomLeft, OverlayBottomRight })
            host.Spacing = _overlayGeometry.StackGap;

        var edge = _overlayGeometry.EdgeInset;
        OverlayTopLeft.Margin = new Thickness(edge);
        OverlayTopRight.Margin = new Thickness(edge);
        OverlayBottomLeft.Margin = new Thickness(edge, edge, edge, 40 + edge);
        OverlayBottomRight.Margin = new Thickness(edge, edge, edge, 40 + edge);
    }

    private void ApplyActionGeometry(MediaItem? item = null)
    {
        var viewportWidth = XamlRoot?.Size.Width ?? 1280;
        var geometry = PosterActionGeometryFactory.Create(
            (item ?? MediaItem)?.ItemSource,
            _uiCustomizationService.CardPresentation.PosterSize,
            viewportWidth);

        QuickActions.Spacing = geometry.Gap;
        QuickActions.Margin = new Thickness(geometry.EdgeInset);
        MoreButton.Margin = new Thickness(0, 0, geometry.EdgeInset, geometry.EdgeInset);
        foreach (var button in new[] { QuickWatchedButton, QuickFavoriteButton, MoreButton })
        {
            button.Width = geometry.TriggerSize;
            button.Height = geometry.TriggerSize;
        }
        QuickWatchedIcon.FontSize = geometry.IconSize;
        QuickFavoriteIcon.FontSize = geometry.IconSize;
        MoreIcon.FontSize = geometry.IconSize;
    }

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _playbackPrefetchCts;
    private MediaItem? _deferredPosterItem;
    private MediaItem? _deferredOverlayItem;
    private bool _isKeyboardFocusWithin;
    private bool _quickActionPending;

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
        {
            card.SelectionBadge.Visibility = card.SelectionMode && card.IsSelected
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (card.MediaItem is { } item)
                card.UpdateQuickActionState(item);
        }
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
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        ApplyOverlayGeometry();
        ApplyActionGeometry();
        SizeChanged += (_, _) => ApplyActionGeometry();
        this.Loaded += (_, _) =>
        {
            if (!_observingUICustomization)
            {
                _observingUICustomization = true;
                _uiCustomizationService.Changed += UICustomization_Changed;
            }
            ApplyCardPresentation();
            // Cached pages retain their card controls across navigation. Their
            // Unloaded handler deliberately drops the decoded bitmap, so reload
            // the unchanged item when the card becomes live again.
            if (_loadCts == null &&
                PosterImage.Source == null &&
                !SuppressImageLoading &&
                !DeferImageLoading &&
                MediaItem is { } item)
            {
                _loadCts = new CancellationTokenSource();
                _ = LoadPosterAsync(item, _loadCts.Token);
            }
        };
        // When ItemsRepeater recycles the card out of the viewport, Unloaded
        // fires. Cancel any in-flight image download and release the decoded
        // BitmapImage so its pixel data can be garbage-collected instead of
        // piling up across 100k-scale libraries.
        this.Unloaded += (_, _) =>
        {
            if (_observingUICustomization)
            {
                _observingUICustomization = false;
                _uiCustomizationService.Changed -= UICustomization_Changed;
            }
            try { _loadCts?.Cancel(); } catch { }
            _loadCts?.Dispose();
            _loadCts = null;
            CancelPlaybackPrefetch();
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

    private void UICustomization_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(ApplyCardPresentation);

    private void ApplyCardPresentation()
    {
        var caption = _uiCustomizationService.CardPresentation.Caption;
        var showCaption = caption != "artwork";
        var showMetadata = caption == "title_metadata";
        TitleText.Visibility = showCaption ? Visibility.Visible : Visibility.Collapsed;
        EpisodeTitleText.Visibility = showMetadata && !string.IsNullOrWhiteSpace(EpisodeTitleText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SubtitleText.Visibility = showMetadata && !string.IsNullOrWhiteSpace(SubtitleText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (Width > 0)
            SetCatalogGridLayout(Width);
        ApplyActionGeometry();
    }

    private void PosterCard_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (MediaItem is not { } item) return;
        var flyout = MediaItemMenu.Build(
            item,
            MediaItemMenu.Surface.Default,
            stateChanged: () => UpdateQuickActionState(item));
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
        EpisodeTitleText.Text = "";
        EpisodeTitleText.Visibility = Visibility.Collapsed;
        SubtitleText.Text = "";
        FallbackTitle.Text = "";
        FallbackTitle.Visibility = Visibility.Collapsed;
        PosterImage.Source = null;
        PosterImage.Opacity = 0;
        ThumbhashImage.Source = null;
        RevealCardActions(false);
        QuickWatchedButton.Visibility = Visibility.Collapsed;
        QuickFavoriteButton.Visibility = Visibility.Collapsed;
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

        TitleText.Text = MediaItemDisplayText.BuildTitle(item);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, TitleText.Text);
        var secondaryTitle = item.UpcomingEvent is { } upcoming
            ? MediaItemDisplayText.FormatUpcomingSubtitle(upcoming)
            : MediaItemDisplayText.BuildEpisodeTitle(item);
        EpisodeTitleText.Text = secondaryTitle ?? "";
        EpisodeTitleText.Visibility = string.IsNullOrWhiteSpace(secondaryTitle)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // Build subtitle line — sort-driven. When the hosting surface (Library,
        // Search, etc.) has sorted by a specific key, surface that key's value
        // so the meta line matches what the user is looking at.
        SubtitleText.Text = item.UpcomingEvent is { } upcomingSchedule
            ? MediaItemDisplayText.FormatUpcomingSchedule(upcomingSchedule)
            : MediaItemDisplayText.BuildSubtitle(item, CurrentSortKey);
        ApplyCardPresentation();
        ApplyActionGeometry(item);
        UpdateQuickActionState(item);

        PosterImage.Opacity = 0;

        // Skip thumbhash -- go straight to loading the real image.
        // Thumbhash decoding on UI thread for hundreds of cards causes jank.
        ThumbhashImage.Source = null;

        // Fallback title: when no poster URL is available, show the item's
        // title centered on the card background (webui: line-clamp-3).
        string? imageUrl = !string.IsNullOrEmpty(item.PosterUrl) ? item.PosterUrl : item.BackdropUrl;
        if (SuppressImageLoading || string.IsNullOrEmpty(imageUrl))
        {
            FallbackTitle.Text = MediaItemDisplayText.BuildTitle(item);
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
            var overlayService = App.Services.GetRequiredService<Services.CardOverlayService>();
            if (!overlayService.IsLoaded)
                _ = EnsureBadgesLoadedAsync(item, ct);
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
        var overlayService = App.Services.GetRequiredService<Services.CardOverlayService>();
        if (!overlayService.IsLoaded && _loadCts is { } owner)
            _ = EnsureBadgesLoadedAsync(item, owner.Token);
    }

    private async Task EnsureBadgesLoadedAsync(MediaItem item, CancellationToken ct)
    {
        try
        {
            var service = App.Services.GetRequiredService<Services.CardOverlayService>();
            await service.EnsureLoadedAsync(ct);
            if (!ct.IsCancellationRequested && ReferenceEquals(MediaItem, item))
                UpdateBadges(item);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { }
    }

    /// <summary>Refreshes mutable profile state without reloading the poster.</summary>
    public void RefreshState()
    {
        if (MediaItem is not { } item) return;
        SubtitleText.Text = item.UpcomingEvent is { } upcoming
            ? MediaItemDisplayText.FormatUpcomingSchedule(upcoming)
            : MediaItemDisplayText.BuildSubtitle(item, CurrentSortKey);
        if (!DeferOverlayLoading)
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
        if (item.Status is "pending" or "unmatched" or "ambiguous")
        {
            var label = item.Status switch
            {
                "pending" => "SCANNING",
                "unmatched" => "UNMATCHED",
                _ => "AMBIGUOUS",
            };
            OverlayTopLeft.Children.Add(BuildBadge(
                label,
                "status",
                new Services.OverlayItemConfig(true, Services.OverlayPosition.TopLeft),
                "classic",
                _overlayGeometry.Scale));
            return;
        }

        if (item.Type == "manga")
        {
            if (!string.IsNullOrWhiteSpace(item.ShowStatus))
            {
                OverlayTopLeft.Children.Add(BuildBadge(
                    item.ShowStatus,
                    "show_status",
                    new Services.OverlayItemConfig(true, Services.OverlayPosition.TopLeft),
                    service.Preset,
                    _overlayGeometry.Scale));
            }

            var counts = new List<string>();
            if (item.MangaVolumeCount > 0) counts.Add($"{item.MangaVolumeCount} Vol");
            if (item.MangaChapterCount > 0) counts.Add($"{item.MangaChapterCount} Ch");
            if (counts.Count > 0)
            {
                OverlayTopRight.Children.Add(BuildBadge(
                    string.Join(" \u00b7 ", counts),
                    "manga_counts",
                    new Services.OverlayItemConfig(true, Services.OverlayPosition.TopRight),
                    service.Preset,
                    _overlayGeometry.Scale));
            }
            return;
        }

        // Upcoming rows carry server-computed premiere/finale badges that are
        // independent of the user's generic overlay preferences.
        if (item.UpcomingEvent is { Badges.Count: > 0 } upcoming)
        {
            foreach (var badge in upcoming.Badges)
            {
                var label = badge switch
                {
                    "series_premiere" => "Series Premiere",
                    "season_premiere" => "Season Premiere",
                    "finale" => "Finale",
                    _ => badge,
                };
                OverlayTopLeft.Children.Add(BuildBadge(
                    label,
                    "upcoming_event",
                    new Services.OverlayItemConfig(true, Services.OverlayPosition.TopLeft),
                    "classic",
                    _overlayGeometry.Scale));
            }
        }

        var prefs = service.GetPrefs();
        if (prefs == null) return; // Admin kill switch engaged → no badges.

        var data = Services.OverlayData.FromMediaItem(item);
        var cornerCounts = new Dictionary<Services.OverlayPosition, int>();
        foreach (var def in service.GetOrderedDefinitions())
        {
            if (!prefs.TryGetValue(def.Id, out var config) || !config.Enabled) continue;
            if (Services.OverlayRegistry.SuppressesStandaloneOverlays(def.Id, prefs)) continue;
            var value = def.GetValue(data);
            if (string.IsNullOrEmpty(value)) continue;
            if (cornerCounts.GetValueOrDefault(config.Position) >= 3) continue;

            var badge = BuildBadge(value, def.Id, config, service.Preset, _overlayGeometry.Scale);
            var host = config.Position switch
            {
                Services.OverlayPosition.TopLeft => OverlayTopLeft,
                Services.OverlayPosition.TopRight => OverlayTopRight,
                Services.OverlayPosition.BottomLeft => OverlayBottomLeft,
                Services.OverlayPosition.BottomRight => OverlayBottomRight,
                _ => OverlayTopLeft,
            };
            host.Children.Add(badge);
            cornerCounts[config.Position] = cornerCounts.GetValueOrDefault(config.Position) + 1;
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
        string preset,
        double scale = 1)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
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
            Spacing = 4 * scale,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var preferIcon = preset is "vibrant" or "pill";
        if ((config.ShowIcon ?? preferIcon) && OverlayGlyph(overlayId) is { } glyph)
        {
            content.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = (fontSize + 1) * scale,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        content.Children.Add(new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = fontSize * scale,
            FontWeight = preset == "vibrant" ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold,
            CharacterSpacing = preset is "minimal" or "square" ? 100 : 60,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Border
        {
            Height = height * scale,
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(
                borderThickness.Left * scale,
                borderThickness.Top * scale,
                borderThickness.Right * scale,
                borderThickness.Bottom * scale),
            CornerRadius = new CornerRadius(radius * scale),
            Padding = new Thickness(
                padding.Left * scale,
                padding.Top * scale,
                padding.Right * scale,
                padding.Bottom * scale),
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
        "rating_imdb" => "#f5c518",
        "rating_tmdb" => "#01b4e4",
        "rating_rt" => "#fa320a",
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
        "show_status" => "\uE735",
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
        if (ActivateCard())
            e.Handled = true;
    }

    private void OnCardKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this))
            return;
        if (e.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space))
            return;
        e.Handled = ActivateCard();
    }

    private bool ActivateCard()
    {
        if (MediaItem == null) return false;

        if (SelectionMode)
        {
            IsSelected = !IsSelected;
            SelectionToggled?.Invoke(this, EventArgs.Empty);
            return true;
        }

        App.Services.GetRequiredService<ItemDetailPrefetchCache>()
            .Prefetch(MediaItem.ContentId);

        App.Services.GetRequiredService<NavigationService>()
            .Navigate<ItemDetailPage>(MediaItem.ContentId);
        return true;
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
        QueuePlaybackPrefetch();
        if (!CardPointerInteractionPolicy.ShouldRevealHoverActions(
                e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch))
            return;

        _hoverEnterTimer?.Stop();
        _hoverEnterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _hoverEnterTimer.Tick += (_, _) =>
        {
            _hoverEnterTimer?.Stop();
            _hoverEnterTimer = null;
            _hoverActive = true;
            PosterBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["SurfaceHoverBrush"];
            RevealCardActions(true);
            AnimateHover(scale: 1.06, translateY: -4.0, brightenOpacity: 1.0);
        };
        _hoverEnterTimer.Start();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        CancelPlaybackPrefetch();
        // If the pointer never stayed long enough to commit a hover, just
        // cancel the pending timer — nothing to animate back.
        _hoverEnterTimer?.Stop();
        _hoverEnterTimer = null;
        if (!_hoverActive) return;
        _hoverActive = false;
        PosterBackground.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["CardBackgroundBrush"];
        if (!_isKeyboardFocusWithin)
            RevealCardActions(false);
        AnimateHover(scale: 1.0, translateY: 0.0, brightenOpacity: 0.0);
    }

    private async void QueuePlaybackPrefetch()
    {
        var item = MediaItem;
        if (item == null || SelectionMode)
            return;

        CancelPlaybackPrefetch();
        _playbackPrefetchCts = new CancellationTokenSource();
        var ct = _playbackPrefetchCts.Token;
        try
        {
            // Avoid turning fast pointer travel across a dense catalog into
            // playback API traffic. A deliberate hover is early enough to hide
            // the watch-detail request behind the user's decision time.
            await Task.Delay(140, ct);
            if (!ct.IsCancellationRequested && ReferenceEquals(MediaItem, item))
            {
                App.Services.GetRequiredService<ItemDetailPrefetchCache>()
                    .Prefetch(item.ContentId);
                if (item.Type is not ("movie" or "episode" or "audiobook"))
                    return;
                App.Services.GetRequiredService<PlayerService>()
                    .PrefetchWatchDetail(item.ContentId);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void CancelPlaybackPrefetch()
    {
        try { _playbackPrefetchCts?.Cancel(); } catch { }
        _playbackPrefetchCts?.Dispose();
        _playbackPrefetchCts = null;
    }

    private void MoreButton_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void MoreButton_GotFocus(object sender, RoutedEventArgs e)
    {
        RevealCardActions(true);
    }

    private void MoreButton_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_hoverActive && !_isKeyboardFocusWithin)
            RevealCardActions(false);
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MediaItem is not { } item)
            return;

        var flyout = MediaItemMenu.Build(
            item,
            MediaItemMenu.Surface.Default,
            stateChanged: () => UpdateQuickActionState(item));
        flyout.ShowAt(MoreButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight,
        });
    }

    private async void QuickWatchedButton_Click(object sender, RoutedEventArgs e)
    {
        var item = MediaItem;
        if (item == null || _quickActionPending) return;

        _quickActionPending = true;
        SetQuickActionsEnabled(false);
        try
        {
            var operation = MediaItemCardActions.ToggleWatchedAsync(item);
            UpdateQuickActionState(item);
            await operation;
            if (ReferenceEquals(MediaItem, item))
                UpdateQuickActionState(item);
        }
        finally
        {
            _quickActionPending = false;
            SetQuickActionsEnabled(true);
        }
    }

    private async void QuickFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        var item = MediaItem;
        if (item == null || _quickActionPending) return;

        _quickActionPending = true;
        SetQuickActionsEnabled(false);
        try
        {
            var operation = MediaItemCardActions.ToggleFavoriteAsync(item);
            UpdateQuickActionState(item);
            await operation;
            if (ReferenceEquals(MediaItem, item))
                UpdateQuickActionState(item);
        }
        finally
        {
            _quickActionPending = false;
            SetQuickActionsEnabled(true);
        }
    }

    private void QuickAction_Tapped(object sender, TappedRoutedEventArgs e)
        => e.Handled = true;

    private void UpdateQuickActionState(MediaItem item)
    {
        var hasState = item.UserState != null && !SelectionMode;
        var showWatched = hasState && item.Type is "movie" or "series";
        var isWatched = item.UserState?.Played == true;
        var isFavorite = item.UserState?.IsFavorite == true;

        QuickWatchedButton.Visibility = showWatched ? Visibility.Visible : Visibility.Collapsed;
        QuickFavoriteButton.Visibility = hasState ? Visibility.Visible : Visibility.Collapsed;
        QuickWatchedIcon.Glyph = isWatched ? "\uE7B3" : "\uED1A";
        QuickWatchedIcon.Foreground = isWatched
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        QuickFavoriteIcon.Glyph = isFavorite ? "\uEB52" : "\uEB51";
        QuickFavoriteIcon.Foreground = isFavorite
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF8, 0x71, 0x71))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

        var watchedLabel = MediaItemCardActions.GetWatchedActionLabel(item.Type, isWatched);
        var favoriteLabel = isFavorite ? "Remove from favorites" : "Add to favorites";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(QuickWatchedButton, watchedLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(QuickFavoriteButton, favoriteLabel);
        ToolTipService.SetToolTip(QuickWatchedButton, watchedLabel);
        ToolTipService.SetToolTip(QuickFavoriteButton, favoriteLabel);
        QuickActions.Opacity = _hoverActive || _isKeyboardFocusWithin ? 1 : 0;
    }

    private void SetQuickActionsEnabled(bool enabled)
    {
        QuickWatchedButton.IsEnabled = enabled;
        QuickFavoriteButton.IsEnabled = enabled;
    }

    private void RevealCardActions(bool reveal)
    {
        MoreButton.Opacity = reveal ? 1 : 0;
        MoreButton.IsHitTestVisible = reveal;
        QuickActions.Opacity = reveal ? 1 : 0;
        QuickActions.IsHitTestVisible = reveal;
    }

    private void OnCardGotFocus(object sender, RoutedEventArgs e)
    {
        _isKeyboardFocusWithin = true;
        QueuePlaybackPrefetch();
        RevealCardActions(true);
    }

    private void OnCardLostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = XamlRoot == null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            _isKeyboardFocusWithin = IsDescendantOf(focused, this);
            if (!_isKeyboardFocusWithin && !_hoverActive)
                RevealCardActions(false);
        });
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current != null;
             current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mirrors the current WebUI ItemCard hover: lift the complete card four
    /// pixels, brighten it, and scale only the poster image inside its clip.
    /// </summary>
    private void AnimateHover(double scale, double translateY, double brightenOpacity)
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
        Add(CardHoverTransform, "TranslateY", translateY);
        Add(HoverBrighten, "Opacity", brightenOpacity);

        storyboard.Begin();
    }
}
