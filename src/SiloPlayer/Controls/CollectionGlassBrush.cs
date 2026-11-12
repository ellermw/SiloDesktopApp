using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace SiloPlayer.Controls;

public sealed class CollectionGlassBrush : XamlCompositionBrushBase
{
    public static readonly DependencyProperty TintOpacityProperty = DependencyProperty.Register(nameof(TintOpacity), typeof(double), typeof(CollectionGlassBrush),
        new PropertyMetadata(.12, (sender, _) => { var brush = (CollectionGlassBrush)sender; if (brush._settings != null) brush.UpdateBrush(); }));
    public double TintOpacity { get => (double)GetValue(TintOpacityProperty); set => SetValue(TintOpacityProperty, value); }
    private UISettings? _settings;
    private CompositionEffectFactory? _factory;
    private CompositionBackdropBrush? _backdrop;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    public CollectionGlassBrush()
    {
        FallbackColor = Color.FromArgb(255, 48, 48, 48);
    }

    protected override void OnConnected()
    {
        if (_settings != null) return;
        _settings = new UISettings();
        _settings.AdvancedEffectsEnabledChanged += EffectsChanged;
        UpdateBrush();
    }

    protected override void OnDisconnected()
    {
        if (_settings != null) _settings.AdvancedEffectsEnabledChanged -= EffectsChanged;
        _settings = null;
        ClearBrush();
    }

    private void EffectsChanged(UISettings sender, object args)
    {
        _dispatcher.TryEnqueue(() => { if (ReferenceEquals(_settings, sender)) UpdateBrush(); });
    }

    private void ClearBrush()
    {
        CompositionBrush?.Dispose(); CompositionBrush = null;
        _backdrop?.Dispose(); _backdrop = null;
        _factory?.Dispose(); _factory = null;
    }

    private void UpdateBrush()
    {
        ClearBrush();
        if (_settings?.AdvancedEffectsEnabled != true) return;
        var foreground = ((SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]).Color;
        var compositor = Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread();
        using var blur = new GaussianBlurEffect { BlurAmount = 8, BorderMode = EffectBorderMode.Hard,
            Source = new CompositionEffectSourceParameter("backdrop") };
        using var tint = new ColorSourceEffect { Color = Color.FromArgb((byte)Math.Round(Math.Clamp(TintOpacity, 0, 1) * 255), foreground.R, foreground.G, foreground.B) };
        using var effect = new CompositeEffect { Mode = Microsoft.Graphics.Canvas.CanvasComposite.SourceOver, Sources = { blur, tint } };
        try
        {
            _factory = compositor.CreateEffectFactory(effect);
            var brush = _factory.CreateBrush();
            CompositionBrush = brush;
            _backdrop = compositor.CreateBackdropBrush();
            brush.SetSourceParameter("backdrop", _backdrop);
        }
        catch
        {
            ClearBrush();
        }
    }
}
