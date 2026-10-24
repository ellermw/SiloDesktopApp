using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace SiloPlayer.Helpers;

/// <summary>ActionBar's motion-safe 150ms hover/press feedback, without repainting the hero.</summary>
internal static class DetailActionFeedback
{
    internal static void Attach(Button button, FrameworkElement paintedSurface)
        => _ = new Feedback(button, paintedSurface);

    private sealed class Feedback
    {
        private readonly Button _button;
        private readonly FrameworkElement _surface;
        private VisualStateGroup? _states;
        private Visual? _visual;
        private readonly UISettings _settings = new();

        internal Feedback(Button button, FrameworkElement surface)
        {
            _button = button;
            _surface = surface;
            button.Loaded += Loaded;
            button.Unloaded += Unloaded;
            surface.SizeChanged += SizeChanged;
        }

        private void Loaded(object sender, RoutedEventArgs args)
        {
            _button.ApplyTemplate();
            if (_states != null) _states.CurrentStateChanged -= StateChanged;
            _states = FindCommonStates(_button);
            _visual = ElementCompositionPreview.GetElementVisual(_surface);
            UpdateCenter();
            if (_states != null) _states.CurrentStateChanged += StateChanged;
            UpdateScale(_states?.CurrentState?.Name);
        }

        private void Unloaded(object sender, RoutedEventArgs args)
        {
            if (_states != null) _states.CurrentStateChanged -= StateChanged;
            _states = null;
            if (_visual == null) return;
            _visual.ImplicitAnimations = null;
            _visual.StopAnimation(nameof(Visual.Scale));
            _visual.Scale = Vector3.One;
            _visual = null;
        }

        private void SizeChanged(object sender, SizeChangedEventArgs args) => UpdateCenter();
        private void UpdateCenter()
        {
            if (_visual != null)
                _visual.CenterPoint = new((float)_surface.ActualWidth / 2, (float)_surface.ActualHeight / 2, 0);
        }

        private void StateChanged(object? sender, VisualStateChangedEventArgs args) => UpdateScale(args.NewState?.Name);

        private void UpdateScale(string? state)
        {
            if (_visual == null) return;
            if (!_settings.AnimationsEnabled || !_button.IsEnabled)
            {
                _visual.ImplicitAnimations = null;
                _visual.StopAnimation(nameof(Visual.Scale));
                _visual.Scale = Vector3.One;
                return;
            }
            if (_visual.ImplicitAnimations == null)
            {
                var compositor = _visual.Compositor;
                var animation = compositor.CreateVector3KeyFrameAnimation();
                animation.Target = nameof(Visual.Scale);
                animation.Duration = TimeSpan.FromMilliseconds(150);
                animation.InsertExpressionKeyFrame(1, "this.FinalValue",
                    compositor.CreateCubicBezierEasingFunction(new(.4f, 0), new(.2f, 1)));
                var animations = compositor.CreateImplicitAnimationCollection();
                animations[nameof(Visual.Scale)] = animation;
                _visual.ImplicitAnimations = animations;
            }
            // Implicit animation preserves the current presentation value when
            // a fast pointer/keyboard transition interrupts an earlier one.
            var scale = state switch { "PointerOver" => 1.02f, "Pressed" => .98f, _ => 1f };
            _visual.Scale = new(scale, scale, 1);
        }

        private static VisualStateGroup? FindCommonStates(DependencyObject node)
        {
            if (node is FrameworkElement element)
                foreach (var group in VisualStateManager.GetVisualStateGroups(element))
                    if (group.Name == "CommonStates") return group;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (FindCommonStates(VisualTreeHelper.GetChild(node, i)) is { } states) return states;
            return null;
        }
    }
}
