using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using Windows.Foundation;
using Windows.UI;

namespace SiloPlayer.Controls;

/// <summary>
/// Decorative, vector-only fallback. Its thumbhash blur shows for a failed image and, through
/// <see cref="ShowLoadingThumbhash"/>, while a card's artwork loads; the blur bitmaps are shared
/// and bounded by <see cref="ThumbhashBitmaps"/>.
/// </summary>
public sealed class DefaultArtwork : Grid
{
    // Every card carries one, collapsed once its artwork shows. Its layers are built the first time they
    // can show (the glow and type mark when there is no blur to show, the blur image while artwork loads):
    // each element and brush made here is an object every full garbage collection walks, and a card with
    // artwork never shows the glow.
    private Grid? _glow;
    private Image? _thumbhash;
    private RadialGradientBrush[]? _brushes;
    private bool _glowShown = true;
    private Viewbox? _mark;
    private string? _iconName;
    private bool _dimmedMark;
    private int _generation;
    private readonly SolidColorBrush _background = new();

    public static readonly DependencyProperty MediaTypeProperty = DependencyProperty.Register(
        nameof(MediaType), typeof(string), typeof(DefaultArtwork), new PropertyMetadata(null, Changed));
    public string? MediaType { get => (string?)GetValue(MediaTypeProperty); set => SetValue(MediaTypeProperty, value); }
    public static readonly DependencyProperty ThumbhashProperty = DependencyProperty.Register(
        nameof(Thumbhash), typeof(string), typeof(DefaultArtwork), new PropertyMetadata(null, Changed));
    public string? Thumbhash { get => (string?)GetValue(ThumbhashProperty); set => SetValue(ThumbhashProperty, value); }
    public static readonly DependencyProperty DimProperty = DependencyProperty.Register(
        nameof(Dim), typeof(bool), typeof(DefaultArtwork), new PropertyMetadata(false, Changed));
    public bool Dim { get => (bool)GetValue(DimProperty); set => SetValue(DimProperty, value); }

    public DefaultArtwork()
    {
        FlowDirection = FlowDirection.LeftToRight;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Background = _background;
        SizeChanged += (_, _) => Resize();
        Unloaded += (_, _) => { if (!IsLoaded) ++_generation; };
        Refresh();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Measured only while shown: the glow is built in the layout pass before it first draws.
        if (_glow == null && _glowShown) BuildGlow();
        return base.MeasureOverride(availableSize);
    }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((DefaultArtwork)sender).Refresh();

    public void Reset(string? mediaType, string? thumbhash = null, bool dim = false)
    {
        MediaType = mediaType; Thumbhash = thumbhash; Dim = dim;
        Refresh();
    }

    private void Refresh()
    {
        ++_generation;
        if (_thumbhash != null) { _thumbhash.Source = null; _thumbhash.Visibility = Visibility.Collapsed; }
        SetGlow(true);
        _background.Color = Tint(255, 22, 23, 28);
        UpdateMark();
        Resize();
    }

    private void SetGlow(bool shown)
    {
        _glowShown = shown;
        UpdateGlow();
    }

    private void UpdateGlow()
    {
        if (_glow != null) _glow.Visibility = _glowShown ? Visibility.Visible : Visibility.Collapsed;
        else if (_glowShown) InvalidateMeasure();
    }

    private void BuildGlow()
    {
        _brushes = Enumerable.Range(0, 3).Select(_ => new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            GradientStops = { new GradientStop { Offset = 0 }, new GradientStop { Offset = 1 } }
        }).ToArray();
        _glow = new Grid();
        // CSS paints the first background image above the other two.
        foreach (var brush in _brushes.Reverse()) _glow.Children.Add(new Border { Background = brush });
        // Under the blur image, as it always was.
        Children.Insert(0, _glow);
        UpdateMark();
        Resize();
    }

    private void UpdateMark()
    {
        if (_glow == null) return;
        var icon = DefaultArtworkStyle.Icon(MediaType);
        if (_mark == null || icon != _iconName || Dim != _dimmedMark)
        {
            if (_mark != null) _glow.Children.Remove(_mark);
            _mark = WebUiIcon.Create(icon, 24, new SolidColorBrush(Tint(255, 255, 255, 255)));
            _mark.HorizontalAlignment = HorizontalAlignment.Center; _mark.VerticalAlignment = VerticalAlignment.Center; _mark.Opacity = .13;
            _glow.Children.Add(_mark); _iconName = icon; _dimmedMark = Dim;
        }
    }

    private Image ThumbhashImage()
    {
        if (_thumbhash != null) return _thumbhash;
        _thumbhash = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
        // Above the glow.
        Children.Add(_thumbhash);
        return _thumbhash;
    }

    private Color Tint(byte alpha, byte r, byte g, byte b) => ThumbhashBitmaps.Tint(Dim, alpha, r, g, b);

    private void Resize()
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (_mark != null) _mark.Width = _mark.Height = DefaultArtworkStyle.MarkSize(width);
        if (_brushes == null) return;
        Position(_brushes[0], .7, .3, .5, Tint(29, 245, 11, 79));
        Position(_brushes[1], 1, 1, .6, Tint(37, 253, 116, 3));
        Position(_brushes[2], 0, 0, .75, Tint(45, 0, 52, 251));
        void Position(RadialGradientBrush brush, double x, double y, double radius, Color color)
        {
            brush.Center = new Point(width * x, height * y); brush.GradientOrigin = brush.Center;
            brush.RadiusX = brush.RadiusY = DefaultArtworkStyle.GlowRadius(width, height, radius);
            brush.GradientStops[0].Color = color;
            brush.GradientStops[1].Color = Color.FromArgb(0, color.R, color.G, color.B);
        }
    }

    public async Task ObserveConvertedImageAsync(Image image)
    {
        var source = image.Source;
        var outcome = SiloPlayer.Converters.UrlToImageSourceConverter.GetLoadOutcome(source);
        if (outcome == null) return;
        var generation = _generation;
        var loaded = await outcome;
        if (generation != _generation || !ReferenceEquals(source, image.Source)) return;
        if (loaded) Visibility = Visibility.Collapsed;
        else { image.Source = null; await ShowThumbhashAsync(); }
    }

    /// <summary>
    /// The blurred artwork while the real artwork loads. Without a thumbhash nothing shows, so loading
    /// cards don't flash the type mark; false then, and the caller keeps the fallback hidden.
    /// </summary>
    public bool ShowLoadingThumbhash()
    {
        if (string.IsNullOrWhiteSpace(Thumbhash)) return false;
        // The glow stays hidden (and unbuilt) under the loading blur.
        SetGlow(false);
        Visibility = Visibility.Visible;
        _ = ShowThumbhashAsync();
        return true;
    }

    public async Task ShowThumbhashAsync()
    {
        Visibility = Visibility.Visible;
        if (_thumbhash?.Source != null) return;
        if (string.IsNullOrWhiteSpace(Thumbhash)) { SetGlow(true); return; }
        var generation = _generation; var hash = Thumbhash;
        // A picture already made (a recycled card) shows in this frame, without awaiting.
        var picture = ThumbhashBitmaps.TryGet(hash, Dim, out var cached) ? cached : await ThumbhashBitmaps.GetAsync(hash, Dim);
        if (generation != _generation) return;
        if (picture is null) { SetGlow(true); return; } // An invalid hash keeps the type mark.
        var image = ThumbhashImage();
        image.Source = picture; image.Visibility = Visibility.Visible; SetGlow(false);
    }
}
