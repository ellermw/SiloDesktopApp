using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Controls;

public sealed partial class HeroCarousel : UserControl
{
    private IList<MediaItem>? _items;
    private int _currentIndex;
    private DispatcherTimer? _autoAdvanceTimer;
    private CancellationTokenSource? _imageCts;

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
    }

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
        HeroYear.Text = item.Year > 0 ? item.Year.ToString() : "";
        HeroGenres.Text = item.Genres.Count > 0 ? string.Join(", ", item.Genres) : "";
        HeroOverview.Text = item.Overview ?? "";

        UpdateDots();

        // Load backdrop image
        _imageCts?.Cancel();
        _imageCts = new CancellationTokenSource();
        _ = LoadBackdropAsync(item, _imageCts.Token);
    }

    private async Task LoadBackdropAsync(MediaItem item, CancellationToken ct)
    {
        // Show thumbhash placeholder first
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
                BackdropImage.Source = bitmap;
            }
            catch
            {
                BackdropImage.Source = null;
            }
        }
        else
        {
            BackdropImage.Source = null;
        }

        // Load the actual backdrop
        if (string.IsNullOrEmpty(item.BackdropUrl)) return;

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

            BackdropImage.Source = bitmapImage;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch
        {
            // Backdrop load failed, placeholder remains
        }
    }
}
