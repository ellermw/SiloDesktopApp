using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using Windows.Foundation;
using Windows.System;

namespace SiloPlayer.Controls;

/// <summary>
/// The viewer's own rating, five stars with half stars, as the WebUI's StarRating draws and handles it
/// (silo-server #4398). Each star is two radio-like halves, the left for the odd score and the right for the
/// even one, so the scores run from 1 (half a star) to 10. A click or tap on either half fills the whole star,
/// or clears the rating when that star is the one set; pressing and moving sideways picks half stars and sets
/// the half under the pointer on release; the arrow keys step half a star; activating a half from the keyboard
/// sets that half's own score. Hovering a star previews it filled up to its end, and previews removal (dimmed
/// stars) over the star that is set. The control only reports what was picked: the owner applies it and sets
/// <see cref="Value"/>.
/// </summary>
public sealed class HalfStarRating : Grid
{
    private const double IconSize = 18;
    private const double HalfWidth = IconSize / 2;
    private const double HalfInset = 3;
    private const double HalfHeight = IconSize + 4;
    /// <summary>How far a press must move sideways before it is a drag; a finger lifting off a star drifts a pixel or two.</summary>
    private const double DragThreshold = 6;
    /// <summary>The lucide Star the WebUI draws, in a 24-unit box.</summary>
    private const string StarGeometry = "M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z";
    // The WebUI keeps rating stars yellow-400 whatever the server accent.
    private static readonly SolidColorBrush Filled = new(ColorHelper.FromArgb(0xFF, 0xFA, 0xCC, 0x15));

    private readonly Button[] _halves = new Button[RatingScale.MaxScore];
    private readonly Path[] _paths = new Path[RatingScale.MaxScore];
    private int? _value;
    private int? _hoverStar;
    // The star a pointer click just changed: the hover preview stays off until the pointer leaves it,
    // so a cleared rating does not look undone under a pointer that has not moved.
    private int? _suppressStar;
    private int? _dragScore;
    private uint? _dragPointer;
    private double _dragStartX;
    private int _dragStartScore;
    private bool _dragMoved;

    public HalfStarRating()
    {
        // The capsule the WebUI draws around the stars (px-2.5 py-2, rounded full): same surface and border as the other action pills.
        Background = new SolidColorBrush(Colors.Transparent);
        var pill = new Border
        {
            Background = (Brush)Application.Current.Resources["SurfaceBrush"], BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(10, 8, 10, 8),
        };
        var stars = new StackPanel { Orientation = Orientation.Horizontal };
        for (var score = RatingScale.MinScore; score <= RatingScale.MaxScore; score++) stars.Children.Add(BuildHalf(score));
        pill.Child = stars;
        Children.Add(pill);
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCaptureLost;
        PointerExited += OnPointerExited;
        KeyDown += OnKeyDown;
        ActualThemeChanged += (_, _) => Render();
        Render();
    }

    /// <summary>What the owner picked: a score from 1 to 10, or null to clear the rating.</summary>
    public event Action<int?>? ScoreChosen;

    /// <summary>The stored rating, 1 to 10, or null when unrated.</summary>
    public int? Value
    {
        get => _value;
        set
        {
            var next = value is { } score && RatingScale.IsScore(score) ? score : (int?)null;
            if (_value == next) return;
            _value = next;
            Render();
        }
    }

    /// <summary>The half that takes the keyboard Tab stop, for tests and the owner's focus handling.</summary>
    public int TabbableScore => RatingScale.TabbableScore(_value);

    /// <summary>The half star buttons by score minus one.</summary>
    public IReadOnlyList<Button> Halves => _halves;

    /// <summary>Shows the hover preview for a star, 1 to 5, as a mouse over it would; null removes it.</summary>
    public void PreviewStar(int? star)
    {
        _hoverStar = star;
        Render();
    }

    private Button BuildHalf(int score)
    {
        var right = score % 2 == 0;
        var path = new Path { Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), StarGeometry), StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        // Each half shows its side of one star icon: the right half slides the icon left by half its width.
        var icon = new Viewbox { Width = IconSize, Height = IconSize, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(right ? -HalfWidth : 0, 0, 0, 0), Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
        var face = new Grid { Width = HalfWidth, Height = IconSize, IsHitTestVisible = false,
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, HalfWidth, IconSize) }, Children = { icon } };
        var half = new Button
        {
            Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"],
            Content = face,
            MinWidth = 0, MinHeight = 0,
            // The hit-target template ignores Padding, so each half hugs its inner edge: the two halves of a star meet with no seam.
            HorizontalContentAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            Width = HalfWidth + HalfInset, Height = HalfHeight,
            Padding = right ? new Thickness(0, 2, HalfInset, 2) : new Thickness(HalfInset, 2, 0, 2),
            CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(0),
            Tag = score,
            UseSystemFocusVisuals = true,
        };
        AutomationProperties.SetPositionInSet(half, score);
        AutomationProperties.SetSizeOfSet(half, RatingScale.MaxScore);
        half.Click += (_, _) => OnHalfClick(score);
        _halves[score - 1] = half; _paths[score - 1] = path;
        return half;
    }

    private void Render()
    {
        var muted = (Brush)Application.Current.Resources["TertiaryTextBrush"];
        var preview = _dragScore == null && _suppressStar == null && _hoverStar != null;
        var shown = _dragScore ?? (preview ? _hoverStar!.Value * 2 : _value);
        // Hovering the star that is already set previews removal, so its stars dim instead of showing the same fill.
        var removing = preview && RatingScale.Clears(_hoverStar!.Value * 2, _value);
        var tabbable = RatingScale.TabbableScore(_value);
        var removeText = AppStrings.Get("web.item.rating.remove", fallback: "Remove rating");
        for (var score = RatingScale.MinScore; score <= RatingScale.MaxScore; score++)
        {
            var half = _halves[score - 1]; var path = _paths[score - 1];
            var filled = RatingScale.IsFilled(score, shown);
            path.Stroke = filled ? Filled : muted;
            path.Fill = filled ? Filled : null;
            half.Opacity = removing ? 0.45 : 1;
            half.IsTabStop = score == tabbable;
            AutomationProperties.SetName(half, RatingScale.Label(score));
            AutomationProperties.SetItemStatus(half, _value == score ? "Selected" : "Not selected");
            var clears = RatingScale.Clears(score, _value);
            ToolTipService.SetToolTip(half, clears ? removeText : null);
        }
    }

    private void Commit(int? score) => ScoreChosen?.Invoke(score);

    /// <summary>A keyboard or assistive activation: the pointer path answers its own clicks.</summary>
    private void OnHalfClick(int score)
    {
        if (_dragPointer != null) return;
        Commit(RatingScale.ToggleScore(score, _value));
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (FlowDirection == FlowDirection.RightToLeft)
            key = key switch { VirtualKey.Left => VirtualKey.Right, VirtualKey.Right => VirtualKey.Left, _ => key };
        int direction;
        if (key is VirtualKey.Right or VirtualKey.Up) direction = 1;
        else if (key is VirtualKey.Left or VirtualKey.Down) direction = -1;
        else return;
        e.Handled = true;
        StepFromKeyboard(direction);
    }

    /// <summary>An arrow key's step of half a star, up (1) or down (-1): reports the stepped score and moves keyboard focus to its half.</summary>
    private void StepFromKeyboard(int direction)
    {
        var next = RatingScale.Step(_value, direction);
        Commit(next);
        // The owner sets Value after this returns, and a half that is off the Tab stop cannot take focus:
        // the Tab stop moves to the stepped half first, so focus is not left on the old half.
        for (var score = RatingScale.MinScore; score <= RatingScale.MaxScore; score++) _halves[score - 1].IsTabStop = score == next;
        _halves[next - 1].Focus(FocusState.Keyboard);
    }

    /// <summary>The half star horizontally nearest the pointer, so a drag that wanders above or below the stars, or past either end, still picks one.</summary>
    private int? ScoreAt(double x)
    {
        int? best = null; var bestDistance = double.PositiveInfinity;
        foreach (var half in _halves)
        {
            if (half.ActualWidth <= 0) continue;
            var left = half.TransformToVisual(this).TransformPoint(new Point(0, 0)).X;
            var right = left + half.ActualWidth;
            var distance = x < left ? left - x : Math.Max(0, x - right);
            if (distance < bestDistance) { bestDistance = distance; best = (int)half.Tag; }
        }
        return best;
    }

    /// <summary>The star, 1 to 5, directly under a point, or null over the pill's padding and the gaps.</summary>
    private int? StarAt(Point point)
    {
        foreach (var half in _halves)
        {
            var origin = half.TransformToVisual(this).TransformPoint(new Point(0, 0));
            if (point.X >= origin.X && point.X < origin.X + half.ActualWidth && point.Y >= origin.Y && point.Y < origin.Y + half.ActualHeight)
                return RatingScale.StarOf((int)half.Tag);
        }
        return null;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed) return;
        if (ScoreAt(point.Position.X) is not { } score) return;
        if (!CapturePointer(e.Pointer)) return;
        _dragPointer = e.Pointer.PointerId; _dragStartX = point.Position.X; _dragStartScore = score; _dragMoved = false;
        _dragScore = RatingScale.WholeStar(score);
        Render();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        if (_dragPointer is { } active)
        {
            if (active != e.Pointer.PointerId || ScoreAt(point.X) is not { } score) return;
            if (Math.Abs(point.X - _dragStartX) >= DragThreshold) _dragMoved = true;
            if (_dragMoved && _dragScore != score) { _dragScore = score; Render(); }
            return;
        }
        // A touch press has no hover; its emulated one must not hold a preview on.
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;
        var star = StarAt(point);
        var changed = false;
        if (_suppressStar != null && star != _suppressStar) { _suppressStar = null; changed = true; }
        if (star != _hoverStar) { _hoverStar = star; changed = true; }
        if (changed) Render();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer != null) return;
        // PointerExited bubbles from the halves: only leaving the pill itself ends the preview.
        var at = e.GetCurrentPoint(this).Position;
        if (at.X >= 0 && at.Y >= 0 && at.X < ActualWidth && at.Y < ActualHeight) return;
        _suppressStar = null; _hoverStar = null; Render();
    }

    /// <summary>
    /// Sets the dragged-to value on release. A press that never left its half is a tap and behaves as a click:
    /// it fills the whole star, or clears the rating when that star is the one set.
    /// </summary>
    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer != e.Pointer.PointerId) return;
        var moved = _dragMoved; var start = _dragStartScore;
        var releasedAt = ScoreAt(e.GetCurrentPoint(this).Position.X);
        EndDrag(e.Pointer);
        int? picked = moved ? releasedAt ?? start : RatingScale.ToggleStar(start, _value);
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
        {
            _suppressStar = RatingScale.StarOf(picked ?? RatingScale.WholeStar(start));
            _hoverStar = StarAt(e.GetCurrentPoint(this).Position);
        }
        Render();
        Commit(picked);
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer == e.Pointer.PointerId) { EndDrag(e.Pointer); Render(); }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointer == e.Pointer.PointerId) { _dragPointer = null; _dragScore = null; Render(); }
    }

    private void EndDrag(Pointer pointer)
    {
        _dragPointer = null; _dragScore = null; _dragMoved = false;
        ReleasePointerCapture(pointer);
    }
}
