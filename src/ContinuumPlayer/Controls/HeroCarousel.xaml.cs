using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Views;

namespace ContinuumPlayer.Controls;

public sealed partial class HeroCarousel : UserControl
{
    private IList<MediaItem>? _items;
    private int _currentIndex;
    private DispatcherTimer? _autoAdvanceTimer;
    private CancellationTokenSource? _imageCts;

    // F-series hero polish:
    // - Crossfade between BackdropImageA / BackdropImageB. `_activeIsA`
    //   tracks which one is currently fully visible.
    // - Hover-reveal nav arrows (fade 0 → 1 on PointerEntered).
    private bool _activeIsA = true;

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
            Height = Math.Max(350, root.ActualHeight * 0.72);
    }

    // Height is managed by UpdateHeightFromWindow, no SizeChanged needed

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoAdvance();
        _imageCts?.Cancel();
    }

    private void StartAutoAdvance()
    {
        StopAutoAdvance();
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
        if (_autoAdvanceTimer != null)
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
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        var item = _items[_currentIndex];

        HeroTitle.Text = item.Title;
        HeroTitleShadow.Text = item.Title;
        HeroOverview.Text = item.Overview ?? "";

        // Metadata pills row: year · IMDb badge · first 3 genres as dark-glass
        // pills matching the webui .metadata-badge hero pattern.
        HeroMetaPillsRow.Children.Clear();
        if (item.Year > 0)
            HeroMetaPillsRow.Children.Add(BuildHeroPill(item.Year.ToString()));
        if (item.RatingImdb.HasValue)
        {
            var ratingStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            ratingStack.Children.Add(new FontIcon
            {
                Glyph = "\uE735",
                FontSize = 11,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            ratingStack.Children.Add(new TextBlock
            {
                Text = item.RatingImdb.Value.ToString("0.0"),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
            });
            HeroMetaPillsRow.Children.Add(BuildHeroPill(ratingStack));
        }
        foreach (var genre in item.Genres.Take(3))
            HeroMetaPillsRow.Children.Add(BuildHeroPill(genre));

        UpdateDots();

        // Load backdrop image into the INACTIVE layer, then crossfade.
        _imageCts?.Cancel();
        _imageCts = new CancellationTokenSource();
        _ = LoadBackdropAsync(item, _imageCts.Token);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items == null || _items.Count == 0 || _currentIndex >= _items.Count) return;
        var item = _items[_currentIndex];
        // Navigate to ItemDetailPage first (for series, user needs to pick an episode)
        // For movies, the detail page has the Play button ready
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<ItemDetailPage>(item.ContentId);
    }

    private async Task LoadBackdropAsync(MediaItem item, CancellationToken ct)
    {
        // Load into the INACTIVE layer so when we crossfade the user never
        // sees a blank frame between slides.
        var incoming = _activeIsA ? BackdropImageB : BackdropImageA;

        // Show thumbhash placeholder first so there's SOMETHING to fade to
        // while the real backdrop fetches.
        if (!string.IsNullOrEmpty(item.BackdropThumbhash))
        {
            try
            {
                var decoded = ThumbhashDecoder.Decode(item.BackdropThumbhash);
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
                incoming.Source = bitmap;
            }
            catch
            {
                incoming.Source = null;
            }
        }
        else
        {
            incoming.Source = null;
        }

        // Load the actual backdrop
        if (string.IsNullOrEmpty(item.BackdropUrl))
        {
            Crossfade();
            return;
        }

        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "backdrop", item.BackdropUrl, httpClient, ct);

            if (ct.IsCancellationRequested || bytes == null) return;

            var bitmapImage = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            if (ct.IsCancellationRequested) return;

            incoming.Source = bitmapImage;
            Crossfade();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch
        {
            // Backdrop load failed, placeholder remains — still crossfade so
            // at least the gradient moves.
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

    private static Border BuildHeroPill(string text) => BuildHeroPill(
        new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
        });

    private static Border BuildHeroPill(FrameworkElement content) => new()
    {
        Height = 26,
        Background = (Brush)Application.Current.Resources["CardOverlayBackgroundBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardOverlayBorderBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(13),
        Padding = new Thickness(12, 0, 12, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = content,
    };

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
    }

    private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateArrows(0.0);
    }

    private void AnimateArrows(double to)
    {
        var sb = new Storyboard();
        sb.Children.Add(BuildOpacity(PrevButton, to, 180));
        sb.Children.Add(BuildOpacity(NextButton, to, 180));
        sb.Begin();
    }
}
