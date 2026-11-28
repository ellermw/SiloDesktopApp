using System.Runtime.CompilerServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Helpers;

/// <summary>
/// Continuous mouse-wheel scrolling for a page's vertical ScrollViewer.
/// WinUI's ScrollViewer hands each wheel notch to DirectManipulation as its own short ease-out
/// over 15% of the viewport (about 220 ms, a fifth of the distance in the first frame) and
/// restarts it on the next notch. At a 1440-pixel-high window every notch is a 216-pixel lurch
/// that starts at full speed, so a turning wheel shows steps at the notch rate instead of motion.
/// Here the same distance per notch is followed by a critically damped spring, ticked once per
/// frame: the speed carries from one notch into the next and never jumps.
/// Touchpads (DirectManipulation), Ctrl/Shift and horizontal wheels, nested scrollers that take
/// the wheel themselves, and Windows animation effects turned off keep the platform behavior.
/// </summary>
internal static class SmoothWheelScrolling
{
    /// <summary>Distance per notch, as a share of the viewport: the platform's own wheel distance.</summary>
    internal const double NotchFraction = 0.15;

    /// <summary>Spring rate (1/s): half a notch in about 56 ms, 90% in about 130 ms (the platform: 47 and 145 ms).</summary>
    internal const double Omega = 30;

    private static readonly ConditionalWeakTable<ScrollViewer, Follower> Followers = new();

    /// <summary>Follows the mouse wheel over the scroller's current content.</summary>
    internal static void Attach(ScrollViewer scroller)
    {
        if (scroller.Content is not UIElement content || Followers.TryGetValue(scroller, out _)) return;
        var follower = new Follower(scroller);
        Followers.Add(scroller, follower);
        // The content sees the wheel before the ScrollViewer (whose own handler runs first on the
        // ScrollViewer itself) and after any nested control or scroller that takes it.
        content.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(follower.OnWheel), false);
    }

    /// <summary>Scrolls as the wheel would by <paramref name="notches"/> (positive: down). False when the wheel is left to the platform.</summary>
    internal static bool Notch(ScrollViewer scroller, double notches) =>
        Followers.TryGetValue(scroller, out var follower) && follower.Notch(notches);

    private sealed class Follower
    {
        private readonly ScrollViewer _scroller;
        private readonly double[] _applied = new double[4];
        private double _target, _position, _velocity;
        private TimeSpan _lastFrame;
        private bool _running;

        public Follower(ScrollViewer scroller)
        {
            _scroller = scroller;
            scroller.Unloaded += (_, _) => Stop();
            // Touch and touchpad panning take over from the wheel.
            scroller.DirectManipulationStarted += (_, _) => Stop();
        }

        public void OnWheel(object sender, PointerRoutedEventArgs e)
        {
            if (e.Handled || e.Pointer.PointerDeviceType != PointerDeviceType.Mouse
                || e.KeyModifiers != Windows.System.VirtualKeyModifiers.None) return;
            var properties = e.GetCurrentPoint(_scroller).Properties;
            if (properties.IsHorizontalMouseWheel || properties.MouseWheelDelta == 0) return;
            if (Notch(-properties.MouseWheelDelta / 120.0)) e.Handled = true;
        }

        public bool Notch(double notches)
        {
            if (!SystemMotion.AnimationsEnabled || !_scroller.IsLoaded || _scroller.VerticalScrollMode == ScrollMode.Disabled
                || _scroller.ScrollableHeight <= 0 || Math.Abs(_scroller.ZoomFactor - 1) > 0.001) return false;
            var from = _running ? _target : _scroller.VerticalOffset;
            var target = Math.Clamp(from + notches * NotchFraction * _scroller.ViewportHeight, 0, _scroller.ScrollableHeight);
            // Already at that end: the wheel goes on to an outer scroller, as with the ScrollViewer.
            if (!_running && Math.Abs(target - from) < 0.5) return false;
            _target = target;
            if (!_running)
            {
                _position = _scroller.VerticalOffset;
                Array.Fill(_applied, _position);
                _velocity = 0;
                _lastFrame = TimeSpan.Zero;
                _running = true;
                CompositionTarget.Rendering += OnRendering;
            }
            return true;
        }

        private void OnRendering(object? sender, object e)
        {
            if (!_scroller.IsLoaded) { Stop(); return; }
            // Anything else that moved the view (scrollbar, keyboard, a page's own ChangeView) wins.
            var offset = _scroller.VerticalOffset;
            if (!IsOwnOffset(offset)) { Stop(); return; }
            var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
            var dt = _lastFrame == TimeSpan.Zero ? 1 / 120d : (now - _lastFrame).TotalSeconds;
            if (dt <= 0) return;
            _lastFrame = now;
            // After a late frame the motion continues from where it was, at most a 30 Hz step further,
            // instead of jumping ahead to make up the lost time.
            dt = Math.Min(dt, 1 / 30d);
            _target = Math.Min(_target, _scroller.ScrollableHeight);
            // Exact step of a critically damped spring: x(t) = (x0 + (v0 + w*x0) t) e^(-w t).
            var x = _position - _target;
            var slope = _velocity + Omega * x;
            var decay = Math.Exp(-Omega * dt);
            x = (x + slope * dt) * decay;
            _velocity = (_velocity - Omega * slope * dt) * decay;
            var settled = Math.Abs(x) < 0.5 && Math.Abs(_velocity) < 20;
            _position = settled ? _target : _target + x;
            _scroller.ChangeView(null, _position, null, true);
            Array.Copy(_applied, 0, _applied, 1, _applied.Length - 1);
            _applied[0] = _position;
            if (settled) Stop();
        }

        // One of the last offsets this follower applied (a loop: this runs every frame and allocates nothing).
        private bool IsOwnOffset(double offset)
        {
            foreach (var applied in _applied)
                if (Math.Abs(applied - offset) <= 2) return true;
            return false;
        }

        private void Stop()
        {
            if (!_running) return;
            _running = false;
            _velocity = 0;
            CompositionTarget.Rendering -= OnRendering;
        }
    }
}
