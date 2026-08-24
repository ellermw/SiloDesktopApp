using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Core.Models.Playback;
using Windows.UI;

namespace SiloPlayer.Controls;

/// <summary>
/// Multi-layered seek bar matching the webui SeekBar.tsx design:
/// background rail + buffered fill + progress fill, with optional
/// intro/credits tinted regions and chapter markers. Emits <see cref="SeekRequested"/>
/// on click and drag-end (no live seek during drag to avoid mpv buffering
/// storms — matches webui commit-on-mouseup behavior).
/// </summary>
public sealed partial class CustomSeekBar : UserControl
{
    public event Action<double>? SeekRequested;
    public event Action<string, double>? MarkerEdgeChanged;

    // ── Bindable state ──────────────────────────────────────────────────
    //
    // Dependency properties were considered, but this control is always
    // data-pushed from the PlayerOverlay UI tick (250ms) so a simple property
    // + Invalidate pattern is cheaper than dependency-property plumbing.

    public double CurrentTime
    {
        get => _currentTime;
        set
        {
            if (Math.Abs(_currentTime - value) < 0.01) return;
            _currentTime = value;
            Invalidate();
        }
    }

    public double Duration
    {
        get => _duration;
        set
        {
            if (Math.Abs(_duration - value) < 0.01) return;
            _duration = value;
            Invalidate();
        }
    }

    /// <summary>End of the buffered region, in seconds from start.</summary>
    public double BufferedEnd
    {
        get => _bufferedEnd;
        set
        {
            if (Math.Abs(_bufferedEnd - value) < 0.01) return;
            _bufferedEnd = value;
            Invalidate();
        }
    }

    public (double start, double end)? IntroMarker { get; set; }
    public (double start, double end)? RecapMarker { get; set; }
    public (double start, double end)? CreditsMarker { get; set; }
    public (double start, double end)? PreviewMarker { get; set; }
    public (double start, double end)? EditableMarker { get; set; }
    public bool IsMarkerEditing { get; set; }
    public IReadOnlyList<VersionChapter>? Chapters { get; set; }

    private double _currentTime;
    private double _duration;
    private double _bufferedEnd;
    private bool _isDragging;
    private bool _isMarkerDragging;
    private string? _markerDragEdge;
    private double _dragPendingSeconds;

    public CustomSeekBar()
    {
        this.InitializeComponent();
        this.SizeChanged += (_, _) => Invalidate();
    }

    /// <summary>
    /// Rebuild chapter markers. Expensive relative to Invalidate; call only
    /// when the chapter list changes (e.g. on ContentLoaded).
    /// </summary>
    public void RebuildChapterMarkers()
    {
        ChapterMarkersLayer.Children.Clear();
        if (Chapters == null || Chapters.Count == 0 || _duration <= 0) return;

        double width = RootGrid.ActualWidth;
        if (width <= 0) return;

        foreach (var ch in Chapters)
        {
            if (ch.StartSeconds <= 0 || ch.StartSeconds >= _duration) continue;
            double x = (ch.StartSeconds / _duration) * width;
            var tick = new Rectangle
            {
                Width = 1.5, Height = 8,
                Fill = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(tick, x);
            Canvas.SetTop(tick, 8);
            ChapterMarkersLayer.Children.Add(tick);
        }
    }

    // ── Layout / redraw ─────────────────────────────────────────────────

    public void Invalidate()
    {
        double width = RootGrid.ActualWidth;
        if (width <= 0 || _duration <= 0)
        {
            ProgressFill.Width = 0;
            BufferedFill.Width = 0;
            IntroRegion.Visibility = Visibility.Collapsed;
            RecapRegion.Visibility = Visibility.Collapsed;
            CreditsRegion.Visibility = Visibility.Collapsed;
            PreviewRegion.Visibility = Visibility.Collapsed;
            MarkerStartHandle.Visibility = Visibility.Collapsed;
            MarkerEndHandle.Visibility = Visibility.Collapsed;
            return;
        }

        double effectiveTime = _isDragging ? _dragPendingSeconds : _currentTime;
        double progressFraction = Math.Clamp(effectiveTime / _duration, 0.0, 1.0);
        double bufferedFraction = Math.Clamp(Math.Max(_bufferedEnd, effectiveTime) / _duration, 0.0, 1.0);

        ProgressFill.Width = width * progressFraction;
        BufferedFill.Width = width * bufferedFraction;

        LayoutMarkerRegion(IntroRegion, IntroMarker, width);
        LayoutMarkerRegion(RecapRegion, RecapMarker, width);
        LayoutMarkerRegion(CreditsRegion, CreditsMarker, width);
        LayoutMarkerRegion(PreviewRegion, PreviewMarker, width);

        if (IsMarkerEditing && EditableMarker is { } editable && editable.end > editable.start)
        {
            MarkerStartHandle.Margin = new Thickness(width * Math.Clamp(editable.start / _duration, 0, 1) - 12, 0, 0, 0);
            MarkerEndHandle.Margin = new Thickness(width * Math.Clamp(editable.end / _duration, 0, 1) - 12, 0, 0, 0);
            MarkerStartHandle.Visibility = Visibility.Visible;
            MarkerEndHandle.Visibility = Visibility.Visible;
            TrackArea.Height = 6;
        }
        else
        {
            MarkerStartHandle.Visibility = Visibility.Collapsed;
            MarkerEndHandle.Visibility = Visibility.Collapsed;
        }

        // Position the scrub thumb at the progress point.
        ScrubThumb.Margin = new Thickness(width * progressFraction - 6, 0, 0, 0);

        // Chapter markers follow layout changes.
        RebuildChapterMarkers();
    }

    private void LayoutMarkerRegion(Border element, (double start, double end)? marker, double width)
    {
        if (marker is not { } range || range.start < 0 || range.end <= range.start)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        var left = (Math.Max(0, range.start) / _duration) * width;
        var right = (Math.Min(_duration, range.end) / _duration) * width;
        element.Margin = new Thickness(left, 0, 0, 0);
        element.Width = Math.Max(0, right - left);
        element.Height = IsMarkerEditing && EditableMarker == marker ? 8 : 4;
        element.VerticalAlignment = VerticalAlignment.Center;
        element.Visibility = Visibility.Visible;
    }

    // ── Pointer handling ────────────────────────────────────────────────

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Hover reveal: grow track slightly and show thumb.
        TrackArea.Height = 6;
        ScrubThumb.Opacity = 1;
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging) return;
        TrackArea.Height = 4;
        ScrubThumb.Opacity = 0;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isMarkerDragging) return;
        if (_duration <= 0) return;
        _isDragging = true;
        this.CapturePointer(e.Pointer);
        _dragPendingSeconds = PointerToSeconds(e);
        Invalidate();
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _duration <= 0) return;
        _dragPendingSeconds = PointerToSeconds(e);
        Invalidate();
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        CommitDragIfAny(e);
    }

    private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        CommitDragIfAny(e);
    }

    private void CommitDragIfAny(PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        TrackArea.Height = 4;
        ScrubThumb.Opacity = 0;

        SeekRequested?.Invoke(_dragPendingSeconds);
    }

    private double PointerToSeconds(PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(RootGrid).Position;
        double width = RootGrid.ActualWidth;
        if (width <= 0) return 0;
        double fraction = Math.Clamp(pt.X / width, 0.0, 1.0);
        return fraction * _duration;
    }

    private void MarkerHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_duration <= 0 || sender is not FrameworkElement { Tag: string edge } handle) return;
        e.Handled = true;
        _isMarkerDragging = true;
        _markerDragEdge = edge;
        handle.CapturePointer(e.Pointer);
        MarkerEdgeChanged?.Invoke(edge, PointerToSeconds(e));
    }

    private void MarkerHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMarkerDragging || _markerDragEdge == null) return;
        e.Handled = true;
        MarkerEdgeChanged?.Invoke(_markerDragEdge, PointerToSeconds(e));
    }

    private void MarkerHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMarkerDragging) return;
        e.Handled = true;
        if (_markerDragEdge != null)
            MarkerEdgeChanged?.Invoke(_markerDragEdge, PointerToSeconds(e));
        _isMarkerDragging = false;
        _markerDragEdge = null;
        if (sender is UIElement handle) handle.ReleasePointerCapture(e.Pointer);
    }

    private void MarkerHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isMarkerDragging = false;
        _markerDragEdge = null;
    }
}
