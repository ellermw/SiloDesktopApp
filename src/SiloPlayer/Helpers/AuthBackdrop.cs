using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace SiloPlayer.Helpers;

/// <summary>The shared WebUI auth-shell pseudo-element, behind artwork and form content.</summary>
public static class AuthBackdrop
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(AuthBackdrop), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty ReadabilityScrimProperty = DependencyProperty.RegisterAttached(
        "ReadabilityScrim", typeof(bool), typeof(AuthBackdrop), new PropertyMetadata(false, Changed));
    private static readonly ConditionalWeakTable<FrameworkElement, SurfaceState> States = new();
    private static readonly List<WeakReference<FrameworkElement>> Mounted = [];

    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetReadabilityScrim(DependencyObject target) => (bool)target.GetValue(ReadabilityScrimProperty);
    public static void SetReadabilityScrim(DependencyObject target, bool value) => target.SetValue(ReadabilityScrimProperty, value);

    public static void RefreshAll()
    {
        for (var index = Mounted.Count - 1; index >= 0; index--)
        {
            if (Mounted[index].TryGetTarget(out var surface) && States.TryGetValue(surface, out var state)) state.Refresh();
            else Mounted.RemoveAt(index);
        }
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Grid && target is not Border) return;
        var surface = (FrameworkElement)target;
        if ((bool)args.NewValue)
        {
            if (!States.TryGetValue(surface, out _)) States.Add(surface, new SurfaceState(surface));
        }
        else if (States.TryGetValue(surface, out var state))
        {
            state.Detach(); States.Remove(surface);
        }
    }

    private sealed class SurfaceState
    {
        private readonly FrameworkElement _surface;
        private readonly Brush? _original;
        private readonly SolidColorBrush _background = new();
        private readonly SolidColorBrush _scrim = new();
        private readonly LinearGradientBrush _base = new()
        {
            MappingMode = BrushMappingMode.Absolute,
            GradientStops = { new GradientStop { Offset = 0 }, new GradientStop { Offset = 1 } }
        };
        private readonly RadialGradientBrush _primary = Glow(.28);
        private readonly RadialGradientBrush _ambient = Glow(.26);
        private readonly Border[] _layers;
        private bool _mounted;

        internal SurfaceState(FrameworkElement surface)
        {
            _surface = surface;
            if (surface is Grid grid)
            {
                _original = grid.Background;
                _layers = [Layer(_base), Layer(_ambient), Layer(_primary)];
                for (var index = 0; index < _layers.Length; index++) grid.Children.Insert(index, _layers[index]);
                grid.Background = _background;
            }
            else
            {
                _original = ((Border)surface).Background;
                _layers = [];
                ((Border)surface).Background = _scrim;
            }
            surface.Loaded += Loaded; surface.Unloaded += Unloaded; surface.SizeChanged += Resized;
            if (surface.IsLoaded) Mount();
        }

        private static Border Layer(Brush brush)
        {
            var layer = new Border { Background = brush, IsHitTestVisible = false };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(layer, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            return layer;
        }

        private static RadialGradientBrush Glow(double fade) => new()
        {
            MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Pad,
            GradientStops = { new GradientStop { Offset = 0 }, new GradientStop { Offset = fade } }
        };

        private void Loaded(object sender, RoutedEventArgs args) => Mount();
        private void Unloaded(object sender, RoutedEventArgs args) => Unmount();
        private void Resized(object sender, SizeChangedEventArgs args) { if (_mounted) Refresh(); }
        private void Mount()
        {
            if (_mounted) return;
            _mounted = true; Mounted.Add(new WeakReference<FrameworkElement>(_surface)); Refresh();
        }
        private void Unmount()
        {
            if (!_mounted) return;
            _mounted = false;
            for (var index = Mounted.Count - 1; index >= 0; index--)
                if (!Mounted[index].TryGetTarget(out var surface) || ReferenceEquals(surface, _surface)) Mounted.RemoveAt(index);
        }

        internal void Refresh()
        {
            var resources = Application.Current.Resources;
            if (resources["AppBackgroundBrush"] is not SolidColorBrush background) return;
            var color = background.Color;
            _background.Color = color;
            _scrim.Color = Color.FromArgb((byte)Math.Round(color.A * .7, MidpointRounding.AwayFromZero), color.R, color.G, color.B);
            if (_surface is not Grid) return;
            var width = _surface.ActualWidth; var height = _surface.ActualHeight;
            if (width <= 0 || height <= 0) return;
            var primary = ((SolidColorBrush)resources["AccentBrush"]).Color;
            var ambient = resources.ContainsKey("AmbientBrush") && resources["AmbientBrush"] is SolidColorBrush ambientBrush ? ambientBrush.Color : primary;
            Position(_primary, width, height, .18, .18, primary, .18);
            Position(_ambient, width, height, .82, .12, ambient, .15);
            //135deg CSS gradient uses a45deg line even when the shell is not square.
            var halfExtent = (width + height) / 4;
            _base.StartPoint = new Point(width / 2 - halfExtent, height / 2 - halfExtent);
            _base.EndPoint = new Point(width / 2 + halfExtent, height / 2 + halfExtent);
            _base.GradientStops[0].Color = Color.FromArgb((byte)Math.Round(color.A * .96), color.R, color.G, color.B);
            _base.GradientStops[1].Color = color;
            // Connect the composition-backed brushes after their absolute geometry and colors exist.
            _layers[1].Background = null; _layers[1].Background = _ambient;
            _layers[2].Background = null; _layers[2].Background = _primary;
        }

        private static void Position(RadialGradientBrush brush, double width, double height, double xFraction, double yFraction, Color color, double opacity)
        {
            var x = width * xFraction; var y = height * yFraction;
            var farX = Math.Max(x, width - x); var farY = Math.Max(y, height - y);
            var radius = Math.Sqrt(farX * farX + farY * farY);
            brush.Center = new Point(x, y); brush.GradientOrigin = brush.Center;
            brush.RadiusX = radius; brush.RadiusY = radius;
            brush.GradientStops[0].Color = Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
            brush.GradientStops[1].Color = Color.FromArgb(0, color.R, color.G, color.B);
        }

        internal void Detach()
        {
            Unmount(); _surface.Loaded -= Loaded; _surface.Unloaded -= Unloaded; _surface.SizeChanged -= Resized;
            if (_surface is Grid grid)
            {
                foreach (var layer in _layers) grid.Children.Remove(layer);
                if (ReferenceEquals(grid.Background, _background)) grid.Background = _original;
            }
            else if (_surface is Border border && ReferenceEquals(border.Background, _scrim)) border.Background = _original;
        }
    }
}
