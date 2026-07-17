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
    private readonly AsyncLoadVersionGate _imageLoadGate = new();
    // Match the WebUI's single carousel pause state. Hover/focus pauses it,
    // leaving the hero resumes it, and the explicit control can resume while
    // the pointer is still over the hero.
    private bool _isPaused;

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

    public HeroCarousel()
    {
        this.InitializeComponent();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeroCarousel carousel)
        {
            carousel.CancelBackdropLoad(clearImages: e.NewValue == null);
            carousel._items = e.NewValue as IList<MediaItem>;
            carousel._currentIndex = 0;
            carousel.BuildDots();
            carousel.ShowCurrentItem();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartAutoAdvance();
        UpdateHeightFromWindow();

        if (XamlRoot?.Content is FrameworkElement root)
            root.SizeChanged += (_, _) => UpdateHeightFromWindow();
    }

    private void UpdateHeightFromWindow()
    {
        // Match web UI: min-h-[72dvh] — 72% of viewport height
        if (XamlRoot?.Content is FrameworkElement root && root.ActualHeight > 0)
        {
            Height = Math.Max(350, root.ActualHeight * 0.72);
            var titleSize = root.ActualWidth >= 1400 ? 68d : root.ActualWidth >= 1024 ? 60d : 48d;
            HeroTitle.FontSize = titleSize;
            HeroTitleShadow.FontSize = titleSize;
        }
    }

    // Height is managed by UpdateHeightFromWindow, no SizeChanged needed

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoAdvance();
        CancelBackdropLoad(clearImages: false);
    }

    private void StartAutoAdvance()
    {
        StopAutoAdvance();
        if (_items == null || _items.Count <= 1 || IsAutoAdvancePaused)
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

        HeroTitle.Text = item.Title;
        HeroTitleShadow.Text = item.Title;
        HeroOverview.Text = item.Overview ?? "";
        UpdatePrimaryAction(item);

        // Eyebrow: "FEATURED — No. 01"
        HeroEyebrow.Text = $"FEATURED \u2014 No. {(_currentIndex + 1):D2}";

        // Slide counter: "01 / 04"
        SlideCounterText.Text = $"{(_currentIndex + 1):D2} / {_items.Count:D2}";

        // Restart progress rail animation
        AnimateProgressRail();

        // Metadata pills row: year · IMDb badge · first 3 genres as dark-glass
        // pills matching the webui .metadata-badge hero pattern.
        HeroMetaPillsRow.Children.Clear();
        if (item.Year > 0)
            AddHeroMeta(item.Year.ToString());
        if (item.RatingImdb.HasValue)
            AddHeroMeta($"IMDb {item.RatingImdb.Value:0.0}");
        foreach (var genre in item.Genres.Take(3))
            AddHeroMeta(genre);
        var runtime = FormatRuntime(item.DurationSeconds ?? (item.Runtime > 0 ? item.Runtime * 60d : null));
        if (runtime != null)
            AddHeroMeta(runtime);

        SlideControlsPanel.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        UpdateDots();

        // Load backdrop image into the INACTIVE layer, then crossfade.
        CancelBackdropLoad(clearImages: false);
        _imageCts = new CancellationTokenSource();
        var version = _imageLoadGate.BeginNextLoad();
        _ = LoadBackdropAsync(item, version, _imageCts.Token);
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
            PlayButtonIcon.Glyph = "\uE736";
            return;
        }

        PlayButtonIcon.Glyph = "\uE768";
        if (!item.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase))
        {
            PlayButtonText.Text = "Play";
            return;
        }

        var player = App.Services.GetRequiredService<PlayerService>();
        if (player.IsAudiobook && string.Equals(player.ContentId, item.ContentId, StringComparison.Ordinal))
        {
            PlayButtonText.Text = player.IsPaused ? "Resume" : "Pause";
            PlayButtonIcon.Glyph = player.IsPaused ? "\uE768" : "\uE769";
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
        if (seconds is null or <= 0) return null;
        var minutes = (int)Math.Round(seconds.Value / 60d);
        if (minutes < 60) return $"{minutes} min";
        var hours = minutes / 60;
        var remainder = minutes % 60;
        return remainder == 0 ? $"{hours}h" : $"{hours}h {remainder}m";
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
                Crossfade();
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

    /// <summary>
    /// Animate opacity between the two backdrop layers: the new one fades
    /// from 0 → 1 and the previous from 1 → 0 over 800 ms. Mirrors the
    /// webui <c>transition-opacity duration-1000</c> hero crossfade.
    /// </summary>
    private void Crossfade()
    {
        var incoming = _activeIsA ? BackdropImageB : BackdropImageA;
        var outgoing = _activeIsA ? BackdropImageA : BackdropImageB;

        var sb = new Storyboard();
        sb.Children.Add(BuildOpacity(incoming, 1, 800));
        sb.Children.Add(BuildOpacity(outgoing, 0, 800));
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

    // ── Progress rail animation ────────────────────────────────────────

    private Storyboard? _progressStoryboard;

    private void AnimateProgressRail()
    {
        _progressStoryboard?.Stop();
        ProgressRailFill.Width = 0;

        if (_items == null || _items.Count <= 1 || IsAutoAdvancePaused)
            return;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 100,
            Duration = new Duration(TimeSpan.FromSeconds(8)),
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
        AnimateArrows(1.0);
        SetCarouselPaused(true);
        StopAutoAdvance();
        _progressStoryboard?.Pause();
    }

    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateArrows(0.0);
        SetCarouselPaused(false);
        ResumeCarouselCycleIfAllowed();
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
        ToolTipService.SetToolTip(PauseCarouselButton, paused ? "Play slideshow" : "Pause slideshow");
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
