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
    private readonly bool _webUiEdges;
    private readonly Border? _previousFade, _nextFade;
    private bool _hovered, _focused, _dragging;
    private uint? _pointer;
    private double _startX, _startOffset;
    public CarouselRail(ScrollViewer scroll, bool webUiEdges = false)
    {
        _webUiEdges = webUiEdges;
        _scroll = scroll; scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Children.Add(scroll);
        if (webUiEdges)
        {
            scroll.IsTabStop = true;
            AutomationProperties.SetName(scroll, "Media carousel");
            _previousFade = Fade(HorizontalAlignment.Left);
            _nextFade = Fade(HorizontalAlignment.Right);
            Children.Add(_previousFade); Children.Add(_nextFade);
        }
        _previous = Arrow("back", webUiEdges ? "Scroll left" : "Previous titles", HorizontalAlignment.Left, -1);
        _next = Arrow("chevron-right", webUiEdges ? "Scroll right" : "Next titles", HorizontalAlignment.Right, 1);
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
        if (_webUiEdges)
        {
            button.Width = button.Height = 44; button.MinWidth = button.MinHeight = 0;
            button.Padding = new Thickness(0); button.CornerRadius = new CornerRadius(0);
            button.VerticalAlignment = VerticalAlignment.Center; button.BorderThickness = new Thickness(0);
            button.Background = EdgeGradient(alignment); button.Content = WebUiIcon.Create(icon, 24);
        }
        AutomationProperties.SetName(button, name); button.Click += (_, _) => Move(direction); return button;
    }
    private Border Fade(HorizontalAlignment alignment) => new()
    {
        Width = 40, HorizontalAlignment = alignment, IsHitTestVisible = false,
        Background = EdgeGradient(alignment), Visibility = Visibility.Collapsed,
    };
    private static Brush EdgeGradient(HorizontalAlignment alignment)
    {
        var color = ((SolidColorBrush)Application.Current.Resources["AppBackgroundBrush"]).Color;
        return new LinearGradientBrush
        {
            StartPoint = new(alignment == HorizontalAlignment.Left ? 0 : 1, .5),
            EndPoint = new(alignment == HorizontalAlignment.Left ? 1 : 0, .5),
            GradientStops = { new() { Color = Microsoft.UI.ColorHelper.FromArgb(204, color.R, color.G, color.B), Offset = 0 }, new() { Color = Microsoft.UI.ColorHelper.FromArgb(0, color.R, color.G, color.B), Offset = 1 } },
        };
    }
    private void Move(int direction)
    {
        var distance = Math.Max(240, _scroll.ViewportWidth * .8);
        if (_webUiEdges && _scroll.Content is StackPanel panel && panel.Children.FirstOrDefault() is FrameworkElement card)
        {
            // Match slidesToScroll:auto: page by the number of complete cards
            // in the viewport, preserving aligned card starts at each edge.
            var step = card.ActualWidth + panel.Spacing;
            if (step > 0)
                distance = Math.Max(1, Math.Floor((_scroll.ViewportWidth - panel.Margin.Left - panel.Margin.Right + panel.Spacing) / step)) * step;
        }
        _scroll.ChangeView(Math.Clamp(_scroll.HorizontalOffset + direction * distance, 0, _scroll.ScrollableWidth), null, null);
    }
    private void UpdateArrows()
    {
        _previous.IsEnabled = _scroll.HorizontalOffset > 1;
        _next.IsEnabled = _scroll.HorizontalOffset < _scroll.ScrollableWidth - 1;
        if (_webUiEdges)
        {
            foreach (var button in new[] { _previous, _next })
            {
                button.Visibility = button.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            }
            _previousFade!.Visibility = _previous.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            _nextFade!.Visibility = _next.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
        _previous.Opacity = (_hovered || _focused) && _previous.IsEnabled ? 1 : 0;
        _next.Opacity = (_hovered || _focused) && _next.IsEnabled ? 1 : 0;
    }
}
