using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

public sealed partial class HeroCarousel : UserControl
{
    private IList<MediaItem>? _items;
    private int _currentIndex;
    private DispatcherTimer? _autoAdvanceTimer;
    private CancellationTokenSource? _imageCts;
    private FrameworkElement? _sizeRoot;
    private readonly AsyncLoadVersionGate _imageLoadGate = new();
    // Match the WebUI's single carousel pause state. Hover/focus pauses it,
    // leaving the hero resumes it, and the explicit control can resume while
    // the pointer is still over the hero.
    private bool _isPaused;
    private bool _isPointerOver;
    private bool _isKeyboardFocusWithin;
    private bool _animationsEnabled = true;
    private double _restingArrowOpacity;
    private double _titleAvailableWidth = 768;
    private string? _lastDisplayedContentId;
    private string? _lastDisplayedBackdropUrl;
    private string? _lastDisplayedBackdropThumbhash;

    // F-series hero polish:
    // - Crossfade between BackdropImageA / BackdropImageB. `_activeIsA`
    //   tracks which one is currently fully visible.
    // - Hover-reveal nav arrows (fade 0 → 1 on PointerEntered).
    private bool _activeIsA = true;
    private bool IsAutoAdvancePaused => _isPaused;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IList<MediaItem>),
            typeof(HeroCarousel),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IList<MediaItem>? ItemsSource
    {
        get => (IList<MediaItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty IsTallProperty =
        DependencyProperty.Register(
            nameof(IsTall),
            typeof(bool),
            typeof(HeroCarousel),
            new PropertyMetadata(false, OnIsTallChanged));

    public bool IsTall
    {
        get => (bool)GetValue(IsTallProperty);
        set => SetValue(IsTallProperty, value);
    }

    public HeroCarousel()
    {
        this.InitializeComponent();
        SetPrimaryIcon("play");
        MoreInfoIcon.Children.Add(WebUiIcon.Create("info", 16, new SolidColorBrush(Microsoft.UI.Colors.White)));
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeroCarousel carousel)
        {
            carousel.CancelBackdropLoad(clearImages: e.NewValue == null);
            var previousContentId = carousel._items != null
                && carousel._currentIndex >= 0
                && carousel._currentIndex < carousel._items.Count
                    ? carousel._items[carousel._currentIndex].ContentId
                    : null;
            var nextItems = e.NewValue as IList<MediaItem>;
            carousel._items = nextItems;
            carousel._currentIndex = 0;
            if (!string.IsNullOrWhiteSpace(previousContentId) && nextItems is { Count: > 0 })
            {
                var preservedIndex = -1;
                for (var i = 0; i < nextItems.Count; i++)
                {
                    if (string.Equals(nextItems[i].ContentId, previousContentId, StringComparison.Ordinal))
                    {
                        preservedIndex = i;
                        break;
                    }
                }
                if (preservedIndex >= 0)
                    carousel._currentIndex = preservedIndex;
            }
            carousel.BuildDots();
            carousel.ShowCurrentItem();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _animationsEnabled = AreSystemAnimationsEnabled();
        StartAutoAdvance();
        UpdateThemeGradientColors();
        UpdateHeightFromWindow();

        DetachSizeRoot();
        if (XamlRoot?.Content is FrameworkElement root)
        {
            _sizeRoot = root;
            _sizeRoot.SizeChanged += SizeRoot_SizeChanged;
        }
    }

    private void UpdateHeightFromWindow()
    {
        // Current WebUI contracts:
        // Home:    h-[54vh] min-h-[380px] max-h-[760px] lg:h-[66vh]
        // Library: h-[60vh] min-h-[420px] max-h-[760px] lg:h-[72vh]
        if (XamlRoot?.Content is FrameworkElement root && root.ActualHeight > 0)
        {
            var heightRatio = IsTall
                ? root.ActualWidth >= 1024 ? 0.72 : 0.60
                : root.ActualWidth >= 1024 ? 0.66 : 0.54;
            Height = IsTall
                ? Math.Clamp(root.ActualHeight * heightRatio, 420, 760)
                : Math.Clamp(root.ActualHeight * heightRatio, 380, 760);
            var width = root.ActualWidth;
            var titleSize = width >= 1280 ? 72d
                : width >= 1024 ? 60d
                : width >= 640 ? 48d
                : 36d;
            HeroTitle.FontSize = titleSize;
            HeroTitleShadow.FontSize = titleSize;
            // text-4xl has40px leading; the larger display sizes use1em.
            HeroTitle.LineHeight = HeroTitleShadow.LineHeight = width < 640 ? 40 : titleSize;
            HeroTitle.LineStackingStrategy = HeroTitleShadow.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

            var gutter = width >= 1280 ? 48d
                : width >= 1024 ? 40d
                : width >= 640 ? 24d
                : 16d;
            var bottom = width >= 1024 ? 64d : width >= 640 ? 48d : 40d;
            HeroContent.Margin = new Thickness(gutter, 0, gutter, bottom);
            HeroContent.MaxWidth = Math.Min(768, Math.Max(280, width - (gutter * 2)));
            _titleAvailableWidth = HeroContent.MaxWidth;
            BalanceTitle();
            HeroEyebrow.Visibility = width >= 640 ? Visibility.Visible : Visibility.Collapsed;
            HeroOverview.FontSize = width >= 640 ? 16 : 14;
            HeroOverview.MaxLines = width >= 640 ? 0 : 2;

            var arrowSize = width >= 640 ? 36d : 28d;
            PrevButton.Width = PrevButton.Height = arrowSize;
            NextButton.Width = NextButton.Height = arrowSize;
            PrevButton.Margin = new Thickness(width >= 640 ? 20 : 12, 0, 0, 0);
            NextButton.Margin = new Thickness(0, 0, width >= 640 ? 20 : 12, 0);
            _restingArrowOpacity = width >= 1024 ? 0 : width >= 640 ? 0.8 : 0.6;
            if (!_isPointerOver)
            {
                PrevButton.Opacity = _restingArrowOpacity;
                NextButton.Opacity = _restingArrowOpacity;
            }

            if (width < 640)
            {
                SlideControlsPanel.VerticalAlignment = VerticalAlignment.Top;
                SlideControlsPanel.Margin = new Thickness(0, IsTall ? 96 : 16, 16, 0);
                ProgressRailContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                SlideControlsPanel.VerticalAlignment = VerticalAlignment.Bottom;
                SlideControlsPanel.Margin = new Thickness(0, 0, 24, 20);
                ProgressRailContainer.Visibility = Visibility.Visible;
            }
        }
    }

    private void UpdateThemeGradientColors()
    {
        if (Application.Current.Resources["AppBackgroundColor"] is not Windows.UI.Color background)
            return;

        Windows.UI.Color WithAlpha(byte alpha) => Microsoft.UI.ColorHelper.FromArgb(
            alpha, background.R, background.G, background.B);

        HeroBottomTransparent.Color = WithAlpha(0);
        HeroBottomSoft.Color = WithAlpha(0x33);
        HeroBottomMid.Color = WithAlpha(0x8C);
        HeroBottomStrong.Color = WithAlpha(0xEB);
        HeroBottomSolid.Color = WithAlpha(0xFF);
        HeroLeftSolid.Color = WithAlpha(0xFF);
        HeroLeftStrong.Color = WithAlpha(0xCC);
        HeroLeftSoft.Color = WithAlpha(0x66);
        HeroLeftTransparent.Color = WithAlpha(0);
        HeroVignetteStrong.Color = WithAlpha(0x66);
        HeroVignetteSoft.Color = WithAlpha(0x33);
        HeroVignetteTransparent.Color = WithAlpha(0);
    }

    // Height is managed by UpdateHeightFromWindow, no SizeChanged needed

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoAdvance();
        CancelBackdropLoad(clearImages: false);
        DetachSizeRoot();
    }

    private void SizeRoot_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateHeightFromWindow();

    private void DetachSizeRoot()
    {
        if (_sizeRoot == null) return;
        _sizeRoot.SizeChanged -= SizeRoot_SizeChanged;
        _sizeRoot = null;
    }

    private void StartAutoAdvance()
    {
        StopAutoAdvance();
        if (!_animationsEnabled || _items == null || _items.Count <= 1 || IsAutoAdvancePaused)
            return;
        _autoAdvanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _autoAdvanceTimer.Tick += (_, _) => NavigateNext();
        _autoAdvanceTimer.Start();
    }

    private void StopAutoAdvance()
    {
        _autoAdvanceTimer?.Stop();
        _autoAdvanceTimer = null;
    }

    private void ResetAutoAdvance()
    {
        // Restart the timer when user interacts
        if (!IsAutoAdvancePaused && _autoAdvanceTimer != null)
        {
            _autoAdvanceTimer.Stop();
            _autoAdvanceTimer.Start();
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        NavigatePrev();
        ResetAutoAdvance();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateNext();
        ResetAutoAdvance();
    }

    private void NavigateNext()
    {
        if (_items == null || _items.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _items.Count;
        ShowCurrentItem();
    }

    private void NavigatePrev()
    {
        if (_items == null || _items.Count == 0) return;
        _currentIndex = (_currentIndex - 1 + _items.Count) % _items.Count;
        ShowCurrentItem();
    }

    private void BuildDots()
    {
        DotsPanel.Children.Clear();
        if (_items == null) return;

        for (int i = 0; i < _items.Count; i++)
        {
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = i == _currentIndex
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["SecondaryTextBrush"],
                Opacity = i == _currentIndex ? 1.0 : 0.5
            };

            var index = i;
            dot.Tapped += (_, _) =>
            {
                _currentIndex = index;
                ShowCurrentItem();
                ResetAutoAdvance();
            };

            DotsPanel.Children.Add(dot);
        }
    }

    private void UpdateDots()
    {
        for (int i = 0; i < DotsPanel.Children.Count; i++)
        {
            if (DotsPanel.Children[i] is Ellipse dot)
            {
                dot.Fill = i == _currentIndex
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : (Brush)Application.Current.Resources["SecondaryTextBrush"];
                dot.Opacity = i == _currentIndex ? 1.0 : 0.5;
            }
        }
    }

    private void ShowCurrentItem()
    {
        if (_items == null || _items.Count == 0 || _currentIndex >= _items.Count)
        {
            CancelBackdropLoad(clearImages: true);
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        var item = _items[_currentIndex];
        var isSameVisibleSlide = string.Equals(_lastDisplayedContentId, item.ContentId, StringComparison.Ordinal);
        var isSameBackdrop = isSameVisibleSlide
            && string.Equals(_lastDisplayedBackdropUrl, item.BackdropUrl, StringComparison.Ordinal)
            && string.Equals(_lastDisplayedBackdropThumbhash, item.BackdropThumbhash, StringComparison.Ordinal);
        _lastDisplayedContentId = item.ContentId;

        var ambient = ThumbhashDecoder.GetAmbientColor(item.BackdropThumbhash);
        if (ambient.HasValue)
        {
            AmbientGlowColor.Color = Microsoft.UI.ColorHelper.FromArgb(
                0xFF, ambient.Value.R, ambient.Value.G, ambient.Value.B);
        }

        HeroTitle.Text = item.Title;
        HeroTitleShadow.Text = item.Title;
        BalanceTitle();
        HeroOverview.Text = item.Overview ?? "";
        UpdatePrimaryAction(item);

        // Eyebrow: "FEATURED — No. 01"
        HeroEyebrow.Text = $"FEATURED \u2014 No. {(_currentIndex + 1):D2}";

        // Slide counter: "01 / 04"
        SlideCounterText.Text = $"{(_currentIndex + 1):D2} / {_items.Count:D2}";

        // The current WebUI keeps the active hero slide mounted when home
        // sections refresh with updated item data. Keep the rail/backdrop
        // stable for the same content id so background refreshes don't make
        // the hero look like it jumped or restarted.
        if (!isSameVisibleSlide)
            AnimateProgressRail();

        HeroMetaPillsRow.Children.Clear();
        var runtimeSeconds = item.Runtime > 0 && double.IsFinite(item.Runtime * 60d)
            ? item.Runtime * 60d : item.DurationSeconds;
        var runtime = FormatRuntime(runtimeSeconds);
        var contentRating = item.ContentRating?.Trim().ToUpperInvariant();
        if (item.Type == "episode")
        {
            if (item.SeasonNumber is >= 0 && item.EpisodeNumber is >= 0)
                AddHeroMeta($"S{item.SeasonNumber} · E{item.EpisodeNumber}");
            if (runtime != null) AddHeroMeta(runtime);
            if (!string.IsNullOrEmpty(contentRating)) AddHeroMeta(contentRating);
        }
        else
        {
            if (item.Year > 0)
                AddHeroMeta(item.Year.ToString());
            if (runtime != null)
                AddHeroMeta(runtime);
            var primaryRating = RatingPresentation.PrimaryCardRating(item.RatingImdb, item.RatingTmdb);
            if (primaryRating != null)
            {
                AddHeroMetaSeparator();
                var entry = DisplayRatingEntry.Create(primaryRating, small: true, foreground: new SolidColorBrush(Microsoft.UI.Colors.White));
                entry.Opacity = .85;
                HeroMetaPillsRow.Children.Add(entry);
            }
            foreach (var genre in item.Genres.Select(genre => genre.Trim()).Where(genre => genre.Length > 0).Distinct().Take(2))
                AddHeroMeta(genre);
            if (!string.IsNullOrEmpty(contentRating)) AddHeroMeta(contentRating);
        }

        SlideControlsPanel.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PrevButton.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        UpdateDots();

        if (!isSameBackdrop)
        {
            // Load backdrop image into the INACTIVE layer, then crossfade.
            CancelBackdropLoad(clearImages: false);
            _imageCts = new CancellationTokenSource();
            var version = _imageLoadGate.BeginNextLoad();
            _ = LoadBackdropAsync(item, version, _imageCts.Token);
        }
    }

    private void CancelBackdropLoad(bool clearImages)
    {
        _imageLoadGate.Cancel();
        try { _imageCts?.Cancel(); } catch { }
        _imageCts = null;

        if (!clearImages)
            return;

        BackdropImageA.ClearValue(Image.SourceProperty);
        BackdropImageB.ClearValue(Image.SourceProperty);
        _lastDisplayedContentId = null;
        _lastDisplayedBackdropUrl = null;
        _lastDisplayedBackdropThumbhash = null;
    }

    private bool IsCurrentBackdropLoad(int version, CancellationToken ct) =>
        !ct.IsCancellationRequested && _imageLoadGate.IsCurrent(version);

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items == null || _items.Count == 0 || _currentIndex >= _items.Count) return;
        var item = _items[_currentIndex];
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase))
        {
            nav.Navigate<EbookReaderPage>(new EbookReaderNavigation(item.ContentId));
            return;
        }

        if (item.Type is not ("movie" or "episode" or "audiobook"))
        {
            nav.Navigate<ItemDetailPage>(item.ContentId);
            return;
        }

        try
        {
            var player = App.Services.GetRequiredService<PlayerService>();
            if (item.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase) &&
                player.IsAudiobook &&
                string.Equals(player.ContentId, item.ContentId, StringComparison.Ordinal))
            {
                player.ToggleAudiobookPlayback();
                UpdatePrimaryAction(item);
                return;
            }

            await player.PlayAsync(item.ContentId);
            UpdatePrimaryAction(item);
        }
        catch (Exception ex)
        {
            App.MainWindowInstance?.ShowPlaybackError($"Failed to start playback: {ex.Message}");
        }
    }

    private static void OnIsTallChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeroCarousel carousel)
            carousel.UpdateHeightFromWindow();
    }

    private void MoreInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items == null || _items.Count == 0 || _currentIndex >= _items.Count) return;
        App.Services.GetRequiredService<NavigationService>()
            .Navigate<ItemDetailPage>(_items[_currentIndex].ContentId);
    }

    private void UpdatePrimaryAction(MediaItem item)
    {
        if (item.Type.Equals("ebook", StringComparison.OrdinalIgnoreCase))
        {
            PlayButtonText.Text = "Read";
            SetPrimaryIcon("read");
            return;
        }

        SetPrimaryIcon("play");
        if (!item.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase))
        {
            PlayButtonText.Text = "Play";
            return;
        }

        var player = App.Services.GetRequiredService<PlayerService>();
        if (player.IsAudiobook && string.Equals(player.ContentId, item.ContentId, StringComparison.Ordinal))
        {
            PlayButtonText.Text = player.IsPaused ? "Resume" : "Pause";
            SetPrimaryIcon(player.IsPaused ? "play" : "pause");
        }
        else if ((item.PositionSeconds ?? 0) > 0 &&
                 ((item.DurationSeconds ?? 0) <= 0 || item.PositionSeconds < item.DurationSeconds))
        {
            PlayButtonText.Text = "Resume";
        }
        else if (item.UserState?.Played == true)
        {
            PlayButtonText.Text = "Listen Again";
        }
        else
        {
            PlayButtonText.Text = "Listen";
        }
    }

    private static string? FormatRuntime(double? seconds)
    {
        if (seconds is null or <= 0 || !double.IsFinite(seconds.Value)) return null;
        var minutes = (int)Math.Round(seconds.Value / 60d, MidpointRounding.AwayFromZero);
        if (minutes <= 0) return null;
        if (minutes < 60) return $"{minutes} min";
        var hours = minutes / 60;
        var remainder = minutes % 60;
        return remainder == 0 ? $"{hours}h" : $"{hours}h {remainder}m";
    }

    private void SetPrimaryIcon(string action)
    {
        PlayButtonIcon.Children.Clear();
        PlayButtonIcon.Children.Add(HeroActionIcon.Create(action, (Brush)Application.Current.Resources["AccentForegroundBrush"]));
    }

    private async Task LoadBackdropAsync(MediaItem item, int version, CancellationToken ct)
    {
        // Load into the INACTIVE layer so when we crossfade the user never
        // sees a blank frame between slides.
        var incoming = _activeIsA ? BackdropImageB : BackdropImageA;
        if (!IsCurrentBackdropLoad(version, ct))
            return;

        // Show thumbhash placeholder first so there's SOMETHING to fade to
        // while the real backdrop fetches.
        if (!string.IsNullOrEmpty(item.BackdropThumbhash))
        {
            try
            {
                var decoded = ThumbhashDecoder.Decode(item.BackdropThumbhash);
                if (!IsCurrentBackdropLoad(version, ct))
                    return;

                var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);

                var bgra = new byte[decoded.Rgba.Length];
                for (int i = 0; i < decoded.Rgba.Length; i += 4)
                {
                    bgra[i] = decoded.Rgba[i + 2];
                    bgra[i + 1] = decoded.Rgba[i + 1];
                    bgra[i + 2] = decoded.Rgba[i];
                    bgra[i + 3] = decoded.Rgba[i + 3];
                }

                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.CopyTo(bgra, bitmap.PixelBuffer);
                bitmap.Invalidate();
                if (!IsCurrentBackdropLoad(version, ct))
                    return;

                incoming.Source = bitmap;
            }
            catch
            {
                if (IsCurrentBackdropLoad(version, ct))
                    incoming.ClearValue(Image.SourceProperty);
            }
        }
        else
        {
            if (IsCurrentBackdropLoad(version, ct))
                incoming.ClearValue(Image.SourceProperty);
        }

        // Load the actual backdrop
        if (string.IsNullOrEmpty(item.BackdropUrl))
        {
            if (IsCurrentBackdropLoad(version, ct))
            {
                Crossfade();
                MarkBackdropDisplayed(item);
            }
            return;
        }

        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "backdrop", item.BackdropUrl, httpClient, ct);

            if (!IsCurrentBackdropLoad(version, ct) || bytes == null) return;

            App.SetPerfBreadcrumb($"Hero backdrop decode start item={item.ContentId}");
            var bitmapImage = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            if (!IsCurrentBackdropLoad(version, ct)) return;

            App.SetPerfBreadcrumb($"Hero backdrop source assign item={item.ContentId}");
            incoming.Source = bitmapImage;
            Crossfade();
            MarkBackdropDisplayed(item);
            App.SetPerfBreadcrumb($"Hero backdrop source end item={item.ContentId}");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch
        {
            // Backdrop load failed, placeholder remains — still crossfade so
            // at least the gradient moves.
            if (IsCurrentBackdropLoad(version, ct))
                Crossfade();
        }
    }

    private void MarkBackdropDisplayed(MediaItem item)
    {
        _lastDisplayedBackdropUrl = item.BackdropUrl;
        _lastDisplayedBackdropThumbhash = item.BackdropThumbhash;
    }

    /// <summary>
    /// Animate opacity between the two backdrop layers: the new one fades
    /// from 0 → 1 and the previous from 1 → 0 over 800 ms. Mirrors the
    /// webui <c>transition-opacity duration-1000</c> hero crossfade.
    /// </summary>
    private void Crossfade()
    {
        var incoming = _activeIsA ? BackdropImageB : BackdropImageA;
        var outgoing = _activeIsA ? BackdropImageA : BackdropImageB;

        if (!_animationsEnabled)
        {
            incoming.Opacity = 1;
            outgoing.Opacity = 0;
            _activeIsA = !_activeIsA;
            return;
        }

        var sb = new Storyboard();
        sb.Children.Add(BuildOpacity(incoming, 1, 1000));
        sb.Children.Add(BuildOpacity(outgoing, 0, 1000));
        sb.Begin();

        _activeIsA = !_activeIsA;
    }

    private static DoubleAnimation BuildOpacity(DependencyObject target, double to, int durationMs)
    {
        var anim = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "Opacity");
        return anim;
    }

    // ── Hero metadata pill builder ──────────────────────────────────────

    private void AddHeroMeta(string text)
    {
        AddHeroMetaSeparator();
        HeroMetaPillsRow.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private void AddHeroMetaSeparator()
    {
        if (HeroMetaPillsRow.Children.Count > 0)
        {
            HeroMetaPillsRow.Children.Add(new TextBlock
            {
                Text = "\u00B7",
                FontSize = 13,
                Opacity = 0.55,
                Margin = new Thickness(7, 0, 7, 0),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

    }

    private void BalanceTitle()
    {
        if (HeroTitle.LineHeight <= 0)
        {
            HeroTitle.MaxWidth = HeroTitleShadow.MaxWidth = _titleAvailableWidth;
            return;
        }
        // CSS text-wrap:balance reduces the wrapping width while preserving
        // the line count. Keep the string/accessibility text intact and bound
        // measurement work to12 iterations when the title or viewport changes.
        var probe = new TextBlock
        {
            Text = HeroTitle.Text, FontFamily = HeroTitle.FontFamily,
            FontSize = HeroTitle.FontSize, FontWeight = HeroTitle.FontWeight,
            CharacterSpacing = HeroTitle.CharacterSpacing, TextWrapping = TextWrapping.Wrap,
            LineHeight = HeroTitle.LineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        probe.Measure(new(_titleAvailableWidth, double.PositiveInfinity));
        var fullHeight = probe.DesiredSize.Height;
        var lines = (int)Math.Round(fullHeight / HeroTitle.LineHeight);
        var balanced = _titleAvailableWidth;
        if (lines is > 1 and <= 6)
        {
            var low = _titleAvailableWidth / lines; var high = _titleAvailableWidth;
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var width = (low + high) / 2;
                probe.Measure(new(width, double.PositiveInfinity));
                if (probe.DesiredSize.Height <= fullHeight + .5) high = width;
                else low = width;
            }
            balanced = Math.Ceiling(high);
        }
        HeroTitle.MaxWidth = HeroTitleShadow.MaxWidth = balanced;
    }

    // ── Progress rail animation ────────────────────────────────────────

    private Storyboard? _progressStoryboard;

    private void AnimateProgressRail()
    {
        _progressStoryboard?.Stop();
        ProgressRailFill.Width = 0;

        if (!_animationsEnabled || _items == null || _items.Count <= 1 || IsAutoAdvancePaused)
            return;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 100,
            Duration = new Duration(TimeSpan.FromSeconds(8)),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(anim, ProgressRailFill);
        Storyboard.SetTargetProperty(anim, "Width");

        _progressStoryboard = new Storyboard();
        _progressStoryboard.Children.Add(anim);
        _progressStoryboard.Begin();
    }

    // ── Keyboard navigation ─────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_items == null || _items.Count == 0) return;
        if (e.Key == Windows.System.VirtualKey.Left)
        {
            NavigatePrev();
            ResetAutoAdvance();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Right)
        {
            NavigateNext();
            ResetAutoAdvance();
            e.Handled = true;
        }
    }

    // ── Hover-reveal arrows ─────────────────────────────────────────────

    private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        AnimateArrows(1.0);
        SetCarouselPaused(true);
        StopAutoAdvance();
        _progressStoryboard?.Pause();

        // The user has stopped the carousel on this title. Warm its watch data
        // now so the primary Play action does not begin with an avoidable
        // serial network request.
        if (_items != null && _currentIndex >= 0 && _currentIndex < _items.Count)
        {
            var item = _items[_currentIndex];
            if (item.Type is "movie" or "episode" or "audiobook")
                App.Services.GetRequiredService<PlayerService>()
                    .PrefetchWatchDetail(item.ContentId);
        }
    }

    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        AnimateArrows(_restingArrowOpacity);
        if (!_isKeyboardFocusWithin)
        {
            SetCarouselPaused(false);
            ResumeCarouselCycleIfAllowed();
        }
    }

    private void OnHeroGotFocus(object sender, RoutedEventArgs e)
    {
        _isKeyboardFocusWithin = true;
        AnimateArrows(1.0);
        SetCarouselPaused(true);
        StopAutoAdvance();
        _progressStoryboard?.Pause();
    }

    private void OnHeroLostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = XamlRoot == null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            _isKeyboardFocusWithin = IsDescendantOf(focused, this);
            if (_isKeyboardFocusWithin)
                return;

            AnimateArrows(_isPointerOver ? 1.0 : _restingArrowOpacity);
            if (!_isPointerOver)
            {
                SetCarouselPaused(false);
                ResumeCarouselCycleIfAllowed();
            }
        });
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    private static bool AreSystemAnimationsEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private void PauseCarouselButton_Click(object sender, RoutedEventArgs e)
    {
        SetCarouselPaused(!_isPaused);

        if (IsAutoAdvancePaused)
        {
            StopAutoAdvance();
            _progressStoryboard?.Pause();
        }
        else
        {
            ResumeCarouselCycleIfAllowed();
        }
    }

    private void SetCarouselPaused(bool paused)
    {
        _isPaused = paused;
        PauseCarouselIcon.Glyph = paused ? "\uE768" : "\uE769";
        var label = paused ? "Play slideshow" : "Pause slideshow";
        ToolTipService.SetToolTip(PauseCarouselButton, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PauseCarouselButton, label);
    }

    private void ResumeCarouselCycleIfAllowed()
    {
        if (IsAutoAdvancePaused)
            return;

        StartAutoAdvance();
        AnimateProgressRail();
    }

    private void AnimateArrows(double to)
    {
        var sb = new Storyboard();
        sb.Children.Add(BuildOpacity(PrevButton, to, 180));
        sb.Children.Add(BuildOpacity(NextButton, to, 180));
        sb.Begin();
    }
}
