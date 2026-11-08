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
    private readonly Grid _glow = new();
    private readonly Image _thumbhash = new() { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
    private readonly RadialGradientBrush[] _brushes;
    private Viewbox? _mark;
    private string? _iconName;
    private bool _dimmedMark;
    private int _generation;

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
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        _brushes = Enumerable.Range(0, 3).Select(_ => new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            GradientStops = { new GradientStop { Offset = 0 }, new GradientStop { Offset = 1 } }
        }).ToArray();
        // CSS paints the first background image above the other two.
        foreach (var brush in _brushes.Reverse()) _glow.Children.Add(new Border { Background = brush });
        Children.Add(_glow); Children.Add(_thumbhash);
        SizeChanged += (_, _) => Resize();
        Unloaded += (_, _) => { if (!IsLoaded) ++_generation; };
        Refresh();
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
        _thumbhash.Source = null; _thumbhash.Visibility = Visibility.Collapsed;
        _glow.Visibility = Visibility.Visible;
        Background = new SolidColorBrush(Tint(255, 22, 23, 28));
        var icon = DefaultArtworkStyle.Icon(MediaType);
        if (_mark == null || icon != _iconName || Dim != _dimmedMark)
        {
            if (_mark != null) _glow.Children.Remove(_mark);
            _mark = WebUiIcon.Create(icon, 24, new SolidColorBrush(Tint(255, 255, 255, 255)));
            _mark.HorizontalAlignment = HorizontalAlignment.Center; _mark.VerticalAlignment = VerticalAlignment.Center; _mark.Opacity = .13;
            _glow.Children.Add(_mark); _iconName = icon; _dimmedMark = Dim;
        }
        Resize();
    }

    private Color Tint(byte alpha, byte r, byte g, byte b) => ThumbhashBitmaps.Tint(Dim, alpha, r, g, b);

    private void Resize()
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (_mark != null) _mark.Width = _mark.Height = DefaultArtworkStyle.MarkSize(width);
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
        Visibility = Visibility.Visible;
        _glow.Visibility = Visibility.Collapsed;
        _ = ShowThumbhashAsync();
        return true;
    }

    public async Task ShowThumbhashAsync()
    {
        Visibility = Visibility.Visible;
        if (_thumbhash.Source != null) return;
        if (string.IsNullOrWhiteSpace(Thumbhash)) { _glow.Visibility = Visibility.Visible; return; }
        var generation = _generation; var hash = Thumbhash;
        // A picture already made (a recycled card) shows in this frame, without awaiting.
        var picture = ThumbhashBitmaps.TryGet(hash, Dim, out var cached) ? cached : await ThumbhashBitmaps.GetAsync(hash, Dim);
        if (generation != _generation) return;
        if (picture is null) { _glow.Visibility = Visibility.Visible; return; } // An invalid hash keeps the type mark.
        _thumbhash.Source = picture; _thumbhash.Visibility = Visibility.Visible; _glow.Visibility = Visibility.Collapsed;
    }
}
