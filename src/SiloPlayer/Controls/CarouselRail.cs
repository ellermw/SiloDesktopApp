using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace SiloPlayer.Controls;

/// <summary>Pointer dragging, keyboard paging and edge actions for poster/brand rails.</summary>
public sealed class CarouselRail : Grid
{
    private readonly ScrollViewer _scroll;
    private readonly Button _previous, _next;
    private bool _hovered, _focused, _dragging;
    private uint? _pointer;
    private double _startX, _startOffset;
    public CarouselRail(ScrollViewer scroll)
    {
        _scroll = scroll; scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Children.Add(scroll);
        _previous = Arrow("back", "Previous titles", HorizontalAlignment.Left, -1);
        _next = Arrow("chevron-right", "Next titles", HorizontalAlignment.Right, 1);
        Children.Add(_previous); Children.Add(_next);
        PointerEntered += (_, _) => { _hovered = true; UpdateArrows(); };
        PointerExited += (_, _) => { _hovered = false; UpdateArrows(); };
        GotFocus += (_, _) => { _focused = true; UpdateArrows(); };
        LostFocus += (_, _) => { _focused = false; UpdateArrows(); };
        scroll.ViewChanged += (_, _) => UpdateArrows(); SizeChanged += (_, _) => UpdateArrows(); Loaded += (_, _) => UpdateArrows();
        KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Left) { Move(-1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Right) { Move(1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Home) { scroll.ChangeView(0, null, null); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.End) { scroll.ChangeView(scroll.ScrollableWidth, null, null); e.Handled = true; }
        };
        scroll.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(scroll); if (!point.Properties.IsLeftButtonPressed) return;
            _pointer = e.Pointer.PointerId; _startX = point.Position.X; _startOffset = scroll.HorizontalOffset; _dragging = false;
        }), true);
        scroll.AddHandler(PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (_pointer != e.Pointer.PointerId) return;
            var delta = e.GetCurrentPoint(scroll).Position.X - _startX;
            if (!_dragging && Math.Abs(delta) > 7) _dragging = scroll.CapturePointer(e.Pointer);
            if (!_dragging) return;
            scroll.ChangeView(Math.Clamp(_startOffset - delta, 0, scroll.ScrollableWidth), null, null, true); e.Handled = true;
        }), true);
        scroll.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (_pointer != e.Pointer.PointerId) return;
            e.Handled = _dragging; scroll.ReleasePointerCapture(e.Pointer); _pointer = null; _dragging = false;
        }), true);
        scroll.PointerCanceled += (_, _) => { _pointer = null; _dragging = false; };
        scroll.PointerCaptureLost += (_, _) => { _pointer = null; _dragging = false; };
    }
    private Button Arrow(string icon, string name, HorizontalAlignment alignment, int direction)
    {
        var button = new Button { Content = WebUiIcon.Create(icon, 18), Width = 32, Height = 32, MinHeight = 0, Padding = new Thickness(6), CornerRadius = new CornerRadius(16),
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center, Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], BorderBrush = (Brush)Application.Current.Resources["BorderBrush"], BorderThickness = new Thickness(1), Opacity = 0 };
        AutomationProperties.SetName(button, name); button.Click += (_, _) => Move(direction); return button;
    }
    private void Move(int direction) => _scroll.ChangeView(Math.Clamp(_scroll.HorizontalOffset + direction * Math.Max(240, _scroll.ViewportWidth * .8), 0, _scroll.ScrollableWidth), null, null);
    private void UpdateArrows()
    {
        _previous.IsEnabled = _scroll.HorizontalOffset > 1;
        _next.IsEnabled = _scroll.HorizontalOffset < _scroll.ScrollableWidth - 1;
        _previous.Opacity = (_hovered || _focused) && _previous.IsEnabled ? 1 : 0;
        _next.Opacity = (_hovered || _focused) && _next.IsEnabled ? 1 : 0;
    }
}
