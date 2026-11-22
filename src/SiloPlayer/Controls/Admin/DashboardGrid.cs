using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Core.Services.Admin;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Controls.Admin;

/// <summary>
/// The dashboard grid. One content-sized column below 640px, two from 640px
/// (span 6+ keeps the full row), twelve columns of fixed 100px rows from
/// 1024px with a 14px gap. Children stay in a stable collection; Order sets
/// placement, so a reorder never re-creates or unloads a widget.
/// </summary>
internal sealed class DashboardGridPanel : Panel
{
    private IReadOnlyList<DashboardGridCell> _cells = [];

    public DashboardGridPanel()
    {
        Background = new SolidColorBrush(Colors.Transparent);
    }

    public DashboardGridMode Mode { get; private set; } = DashboardGridMode.Twelve;

    public void SetMode(DashboardGridMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        InvalidateMeasure();
    }

    private IEnumerable<DashboardWidgetFrame> Frames => Children.OfType<DashboardWidgetFrame>().Where(frame => frame.Visibility == Visibility.Visible);

    private IReadOnlyList<DashboardWidgetFrame> Ordered() => Frames.OrderBy(frame => frame.Order).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        var frames = Ordered();
        _cells = DashboardGridPlacement.Place(Mode, frames.Select(f => new DashboardGridRequest(f.Id, f.EffectiveSpan, f.EffectiveRows)).ToList());
        var columns = DashboardGridPlacement.ColumnCount(Mode);
        var columnWidth = DashboardGridPlacement.ColumnWidth(width, columns);
        double Width(DashboardGridCell cell) => columnWidth * cell.ColumnSpan + DashboardGridPlacement.Gap * (cell.ColumnSpan - 1);
        if (Mode == DashboardGridMode.Twelve)
        {
            var rows = 0;
            for (var i = 0; i < frames.Count; i++)
            {
                var cell = _cells[i];
                frames[i].Measure(new Size(Width(cell), DashboardGridPlacement.FixedHeight(cell.RowSpan)));
                rows = Math.Max(rows, cell.Row + cell.RowSpan);
            }
            foreach (var hidden in Children.Except(frames)) hidden.Measure(new Size(0, 0));
            return new Size(width, rows == 0 ? 0 : DashboardGridPlacement.FixedHeight(rows));
        }
        var heights = new Dictionary<int, double>();
        for (var i = 0; i < frames.Count; i++)
        {
            var cell = _cells[i];
            frames[i].Measure(new Size(Width(cell), double.PositiveInfinity));
            heights[cell.Row] = Math.Max(heights.GetValueOrDefault(cell.Row), frames[i].DesiredSize.Height);
        }
        foreach (var hidden in Children.Except(frames)) hidden.Measure(new Size(0, 0));
        _rowHeights = heights;
        var total = heights.Values.Sum() + Math.Max(0, heights.Count - 1) * DashboardGridPlacement.Gap;
        return new Size(width, total);
    }

    private Dictionary<int, double> _rowHeights = [];

    protected override Size ArrangeOverride(Size finalSize)
    {
        var frames = Ordered();
        var columns = DashboardGridPlacement.ColumnCount(Mode);
        var columnWidth = DashboardGridPlacement.ColumnWidth(finalSize.Width, columns);
        for (var i = 0; i < frames.Count && i < _cells.Count; i++)
        {
            var cell = _cells[i];
            var x = cell.Column * (columnWidth + DashboardGridPlacement.Gap);
            var w = columnWidth * cell.ColumnSpan + DashboardGridPlacement.Gap * (cell.ColumnSpan - 1);
            double y, h;
            if (Mode == DashboardGridMode.Twelve)
            {
                y = cell.Row * (DashboardGridPlacement.RowHeight + DashboardGridPlacement.Gap);
                h = DashboardGridPlacement.FixedHeight(cell.RowSpan);
            }
            else
            {
                y = 0;
                for (var r = 0; r < cell.Row; r++) y += _rowHeights.GetValueOrDefault(r) + DashboardGridPlacement.Gap;
                h = _rowHeights.GetValueOrDefault(cell.Row);
            }
            frames[i].Arrange(new Rect(x, y, w, h));
        }
        foreach (var hidden in Children.Except(frames)) hidden.Arrange(new Rect(0, 0, 0, 0));
        return finalSize;
    }

    /// <summary>The placed widget under a point, and which side ("before" is the start half).</summary>
    public (DashboardWidgetFrame Frame, bool Before)? HitTest(Point point)
    {
        foreach (var frame in Ordered())
        {
            var origin = frame.TransformToVisual(this).TransformPoint(new Point(0, 0));
            var rect = new Rect(origin.X, origin.Y, frame.ActualWidth, frame.ActualHeight);
            if (rect.Contains(point)) return (frame, point.X < rect.X + rect.Width / 2);
        }
        return null;
    }
}

/// <summary>
/// One placed widget and, in customize mode, its chrome: a dashed outline,
/// move (drag or arrow keys) and remove buttons, the corner resize handle
/// (pointer or arrow keys, lg only) and the live size badge.
/// </summary>
internal sealed class DashboardWidgetFrame : Grid
{
    public const string AddDragFormat = "application/x-silo-widget-add";
    public const string MoveDragFormat = "application/x-silo-widget-move";

    private readonly DashboardWidgetDefinition _widget;
    private readonly Grid _chrome = new() { Visibility = Visibility.Collapsed };
    private readonly Rectangle _outline;
    private readonly Border _dropIndicator;
    private readonly Border _sizeBadge;
    private readonly TextBlock _sizeText;
    private readonly Button _resize;
    private (int Span, int Rows)? _preview;
    private Point _resizeStart;
    private (int Span, int Rows) _resizeOrigin;
    private double _resizeScrollOffset;
    private bool _collapsed;

    public DashboardWidgetFrame(DashboardWidgetDefinition widget, DashboardWidgetView view, DashboardGridHost host)
    {
        _widget = widget;
        View = view;
        Host = host;
        Children.Add(view);
        _outline = new Rectangle
        {
            Stroke = B(Primary, 0.4), StrokeThickness = 2, StrokeDashArray = [3, 3], RadiusX = 16, RadiusY = 16,
            Margin = new Thickness(-4), IsHitTestVisible = false,
        };
        _chrome.Children.Add(_outline);
        _dropIndicator = new Border { Width = 4, CornerRadius = new CornerRadius(999), Background = B(Primary), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _chrome.Children.Add(_dropIndicator);
        var move = IconButton("grip-vertical", 24, 14, DashboardText.T("web.admin.dashboard.grid.moveLabel", ("title", widget.Title)),
            MutedForeground, Foreground, Colors.Transparent, Colors.Transparent, (_, _) => { });
        ToolTipService.SetToolTip(move, DashboardText.T("web.admin.dashboard.grid.moveHint"));
        move.KeyDown += (_, e) => Host.OnMoveKey(this, e);
        var remove = IconButton("x", 24, 14, DashboardText.T("web.admin.dashboard.grid.removeLabel", ("title", widget.Title)),
            MutedForeground, Destructive, Colors.Transparent, Colors.Transparent, (_, _) => Host.Remove(Id));
        ToolTipService.SetToolTip(remove, DashboardText.T("web.admin.dashboard.grid.removeHint"));
        MoveButton = move;
        var toolbar = new Border
        {
            Background = B(BackgroundColor, 0.95), BorderBrush = B(BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999),
            Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -12, 12, 0), Child = Row(2, move, remove),
        };
        Canvas.SetZIndex(toolbar, 20);
        _chrome.Children.Add(toolbar);
        _resize = new DashboardResizeHandle
        {
            Width = 14, Height = 14, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -6, -6), Visibility = widget.CanResize ? Visibility.Visible : Visibility.Collapsed,
        };
        _resize.Resources["ButtonBackground"] = B(BackgroundColor, 0.95);
        _resize.Resources["ButtonBackgroundPointerOver"] = B(Primary, 0.3);
        _resize.Resources["ButtonBackgroundPressed"] = B(Primary, 0.3);
        _resize.Resources["ButtonBorderBrush"] = B(BorderColor);
        _resize.Resources["ButtonBorderBrushPointerOver"] = B(Primary);
        _resize.Resources["ButtonBorderBrushPressed"] = B(Primary);
        _resize.Background = B(BackgroundColor, 0.95);
        _resize.BorderBrush = B(BorderColor);
        AutomationProperties.SetName(_resize, DashboardText.T("web.admin.dashboard.grid.resizeLabel", ("title", widget.Title)));
        ToolTipService.SetToolTip(_resize, DashboardText.T("web.admin.dashboard.grid.resizeHint"));
        _resize.PointerPressed += OnResizePressed;
        _resize.PointerMoved += OnResizeMoved;
        _resize.PointerReleased += OnResizeReleased;
        _resize.PointerCanceled += OnResizeReleased;
        _resize.PointerCaptureLost += OnResizeReleased;
        _resize.KeyDown += OnResizeKey;
        Canvas.SetZIndex(_resize, 20);
        _chrome.Children.Add(_resize);
        _sizeText = new TextBlock { FontSize = 10, FontWeight = Bold, Foreground = B(PrimaryForeground) };
        _sizeBadge = new Border
        {
            Background = B(Primary), CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -12, 0, 0), Visibility = Visibility.Collapsed, Child = _sizeText, IsHitTestVisible = false,
        };
        Canvas.SetZIndex(_sizeBadge, 30);
        _chrome.Children.Add(_sizeBadge);
        Children.Add(_chrome);
        AllowDrop = true;
        DragStarting += (_, e) =>
        {
            if (!Host.IsCustomizing || _preview != null) { e.Cancel = true; return; }
            e.Data.SetData(MoveDragFormat, Id);
            e.Data.RequestedOperation = DataPackageOperation.Move;
            Host.BeginMove(this);
        };
        DropCompleted += (_, _) => Host.EndDrag();
    }

    public string Id => _widget.Id;
    public DashboardWidgetDefinition Widget => _widget;
    public DashboardWidgetView View { get; }
    public DashboardGridHost Host { get; }
    public Button MoveButton { get; }
    public int Order { get; set; }
    public int Span { get; set; }
    public int Rows { get; set; }

    /// <summary>Customize mode always renders full size so drag and resize targets hold still.</summary>
    public int EffectiveSpan => _preview?.Span ?? Span;
    public int EffectiveRows => _preview?.Rows ?? (!Host.IsCustomizing && _collapsed && _widget.CollapsedRows is { } collapsed ? collapsed : Rows);

    public void SetCollapsed(bool collapsed)
    {
        if (_collapsed == collapsed) return;
        _collapsed = collapsed;
        (Parent as UIElement)?.InvalidateMeasure();
    }

    public void SetCustomizing(bool customizing)
    {
        _chrome.Visibility = customizing ? Visibility.Visible : Visibility.Collapsed;
        CanDrag = customizing;
        _resize.Visibility = customizing && _widget.CanResize && Host.Mode == DashboardGridMode.Twelve ? Visibility.Visible : Visibility.Collapsed;
        View.SetCustomizing(customizing);
        if (!customizing) CancelInteraction();
    }

    public void ShowDrop(bool? before)
    {
        _dropIndicator.Visibility = before == null ? Visibility.Collapsed : Visibility.Visible;
        if (before is { } b)
        {
            _dropIndicator.HorizontalAlignment = b ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _dropIndicator.Margin = b ? new Thickness(-10, 0, 0, 0) : new Thickness(0, 0, -10, 0);
        }
    }

    public void SetDragging(bool dragging) => View.Opacity = dragging ? 0.4 : 1;

    public void CancelInteraction()
    {
        _preview = null;
        _resize.ReleasePointerCaptures();
        SetDragging(false);
        ShowDrop(null);
        UpdateResizeChrome();
    }

    private void OnResizePressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Host.Panel);
        if (!Host.IsCustomizing || !point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        _resize.CapturePointer(e.Pointer);
        _resizeStart = point.Position;
        _resizeOrigin = (Span, Rows);
        _resizeScrollOffset = 0;
        _preview = (Span, Rows);
        UpdateResizeChrome();
    }

    private void OnResizeMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_preview == null) return;
        var position = e.GetCurrentPoint(Host.Panel).Position;
        ApplyResize(position);
        Host.AutoScroll(e.GetCurrentPoint(Host.ScrollHost).Position.Y, dy =>
        {
            _resizeScrollOffset += dy;
            ApplyResize(position);
        });
    }

    private void ApplyResize(Point position)
    {
        var next = DashboardGridPlacement.ResizeFromDrag(_widget, _resizeOrigin.Span, _resizeOrigin.Rows,
            position.X - _resizeStart.X, position.Y - _resizeStart.Y + _resizeScrollOffset, Host.Panel.ActualWidth,
            FlowDirection == FlowDirection.RightToLeft);
        if (_preview == next) return;
        _preview = next;
        UpdateResizeChrome();
        Host.Panel.InvalidateMeasure();
    }

    private void OnResizeReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_preview is not { } final) return;
        Host.StopAutoScroll();
        _preview = null;
        _resize.ReleasePointerCaptures();
        UpdateResizeChrome();
        Host.Resize(this, final.Span, final.Rows);
    }

    private void OnResizeKey(object sender, KeyRoutedEventArgs e)
    {
        var rtl = FlowDirection == FlowDirection.RightToLeft;
        int column = e.Key switch
        {
            Windows.System.VirtualKey.Left => rtl ? 1 : -1,
            Windows.System.VirtualKey.Right => rtl ? -1 : 1,
            _ => 0,
        };
        int row = e.Key switch { Windows.System.VirtualKey.Up => -1, Windows.System.VirtualKey.Down => 1, _ => 0 };
        if (column == 0 && row == 0) return;
        e.Handled = true;
        var (span, rows) = DashboardGridPlacement.ResizeFromKey(_widget, Span, Rows, column, row);
        if (span == Span && rows == Rows) return;
        Host.Resize(this, span, rows);
    }

    private void UpdateResizeChrome()
    {
        var resizing = _preview != null;
        _outline.StrokeDashArray = resizing ? null : [3, 3];
        _outline.Stroke = B(Primary, resizing ? 0.8 : 0.4);
        _sizeBadge.Visibility = resizing ? Visibility.Visible : Visibility.Collapsed;
        if (_preview is { } p) _sizeText.Text = DashboardText.T("web.admin.dashboard.grid.sizeBadge", ("span", (long)p.Span), ("rows", (long)p.Rows));
    }
}

/// <summary>Everything a frame asks of the page: the layout owner, live announcements and drag scrolling.</summary>
internal sealed class DashboardGridHost
{
    private readonly Func<DashboardLayoutSync?> _sync;
    private readonly TextBlock _live;
    private readonly DispatcherTimerLike _scrollTimer;
    private DashboardWidgetFrame? _dragging;
    private double _scrollSpeed;
    private DashboardLayoutSync? _dragOwner;
    private long _interactionVersion;
    private string? _addDragId;
    private Action<double>? _scrolled;

    public DashboardGridHost(DashboardGridPanel panel, ScrollViewer scrollHost, TextBlock live, Func<DashboardLayoutSync?> sync, Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        Panel = panel;
        ScrollHost = scrollHost;
        _live = live;
        _sync = sync;
        _scrollTimer = new DispatcherTimerLike(dispatcher, TimeSpan.FromMilliseconds(16), ScrollTick);
        panel.AllowDrop = true;
        panel.DragOver += OnDragOver;
        panel.Drop += OnDrop;
        panel.DragLeave += (_, _) => { ClearDrop(); StopAutoScroll(); };
    }

    public DashboardGridPanel Panel { get; }
    public ScrollViewer ScrollHost { get; }
    public string? AddDragId
    {
        get => _addDragId;
        set
        {
            _addDragId = value;
            if (value != null) { _dragOwner = _sync(); ++_interactionVersion; }
        }
    }
    public bool IsCustomizing => _sync()?.IsCustomizing == true;
    public DashboardGridMode Mode => Panel.Mode;
    public event Action? AddCompleted;

    private IReadOnlyList<DashboardLayoutEntry> Entries => _sync()?.Entries ?? [];

    public void Announce(string message)
    {
        _live.Text = message;
        FrameworkElementAutomationPeer.FromElement(_live)?.RaiseNotificationEvent(AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent, message, "dashboard-grid");
    }

    public void Remove(string id) { if (IsCustomizing) _sync()?.RemoveWidget(id); }

    public void Resize(DashboardWidgetFrame frame, int span, int rows)
    {
        if (!IsCustomizing || frame.Parent != Panel) return;
        _sync()?.ResizeWidget(frame.Id, span, rows);
        Announce(DashboardText.T("web.admin.dashboard.grid.resized", ("title", frame.Widget.Title), ("span", (long)span),
            ("columns", (long)DashboardGridPlacement.Columns), ("rows", (long)rows)));
    }

    /// <summary>Arrow keys on the move handle: back (left/up) or forward (right/down) one place.</summary>
    public void OnMoveKey(DashboardWidgetFrame frame, KeyRoutedEventArgs e)
    {
        if (!IsCustomizing || frame.Parent != Panel) return;
        var rtl = frame.FlowDirection == FlowDirection.RightToLeft;
        var backward = e.Key is Windows.System.VirtualKey.Up || e.Key == (rtl ? Windows.System.VirtualKey.Right : Windows.System.VirtualKey.Left);
        var forward = e.Key is Windows.System.VirtualKey.Down || e.Key == (rtl ? Windows.System.VirtualKey.Left : Windows.System.VirtualKey.Right);
        if (!backward && !forward) return;
        e.Handled = true;
        var entries = Entries;
        var index = entries.ToList().FindIndex(entry => entry.Id == frame.Id);
        if (index == -1) return;
        var nextIndex = backward ? index - 1 : index + 1;
        if (nextIndex < 0 || nextIndex >= entries.Count) return;
        if (backward) _sync()?.MoveWidget(frame.Id, entries[nextIndex].Id);
        else _sync()?.MoveWidget(frame.Id, index + 2 < entries.Count ? entries[index + 2].Id : null);
        Announce(DashboardText.T("web.admin.dashboard.grid.moved", ("title", frame.Widget.Title), ("position", (long)nextIndex + 1), ("total", (long)entries.Count)));
        frame.MoveButton.Focus(FocusState.Keyboard);
    }

    public void BeginMove(DashboardWidgetFrame frame)
    {
        if (!IsCustomizing || frame.Parent != Panel) return;
        _dragOwner = _sync();
        ++_interactionVersion;
        _dragging = frame;
        frame.SetDragging(true);
    }

    public void EndDrag()
    {
        ++_interactionVersion;
        _dragOwner = null;
        foreach (var frame in Panel.Children.OfType<DashboardWidgetFrame>()) frame.CancelInteraction();
        _dragging?.SetDragging(false);
        _dragging = null;
        AddDragId = null;
        ClearDrop();
        StopAutoScroll();
    }

    private void ClearDrop()
    {
        foreach (var frame in Panel.Children.OfType<DashboardWidgetFrame>()) frame.ShowDrop(null);
    }

    private (DashboardWidgetFrame Frame, bool Before)? Target(DragEventArgs e, bool isAdd)
    {
        var hit = Panel.HitTest(e.GetPosition(Panel));
        if (hit != null) return hit;
        var last = Entries.LastOrDefault();
        var lastFrame = last == null ? null : Panel.Children.OfType<DashboardWidgetFrame>().FirstOrDefault(f => f.Id == last.Id);
        if (lastFrame == null || (!isAdd && lastFrame == _dragging)) return null;
        return (lastFrame, false);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!IsCustomizing || !ReferenceEquals(_dragOwner, _sync())) return;
        var isAdd = AddDragId != null && e.DataView.Contains(DashboardWidgetFrame.AddDragFormat);
        if (!isAdd && _dragging == null) return;
        e.AcceptedOperation = isAdd ? DataPackageOperation.Copy : DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = false;
        AutoScroll(e.GetPosition(ScrollHost).Y);
        var target = Target(e, isAdd);
        ClearDrop();
        if (target is { } t && (isAdd || t.Frame != _dragging)) t.Frame.ShowDrop(t.Before);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        StopAutoScroll();
        var isAdd = AddDragId != null && e.DataView.Contains(DashboardWidgetFrame.AddDragFormat);
        var target = Target(e, isAdd);
        string? beforeId = null;
        if (target is { } t)
        {
            var entries = Entries.ToList();
            var index = entries.FindIndex(entry => entry.Id == t.Frame.Id);
            beforeId = t.Before ? t.Frame.Id : index >= 0 && index + 1 < entries.Count ? entries[index + 1].Id : null;
            // Over the trailing area a drop appends.
            if (Panel.HitTest(e.GetPosition(Panel)) == null) beforeId = null;
        }
        if (isAdd)
        {
            await CompleteAddDropAsync(async () => (await e.DataView.GetDataAsync(DashboardWidgetFrame.AddDragFormat)) as string, beforeId);
            return;
        }
        if (_dragging is { } dragging && dragging.Parent == Panel && dragging.Id != beforeId) _dragOwner?.MoveWidget(dragging.Id, beforeId);
        EndDrag();
    }

    private async Task CompleteAddDropAsync(Func<Task<string?>> read, string? beforeId)
    {
        var owner = _dragOwner;
        var version = _interactionVersion;
        var id = AddDragId;
        try { id = await read() ?? id; } catch (Exception) { }
        if (version != _interactionVersion || owner == null || !ReferenceEquals(owner, _sync()) || !owner.IsCustomizing) return;
        EndDrag();
        if (id == null || DashboardRegistry.Find(id) is not { } widget) return;
        var entries = owner.Entries;
        var position = beforeId == null ? entries.Count + 1 : Math.Max(1, entries.ToList().FindIndex(entry => entry.Id == beforeId) + 1);
        owner.AddWidget(widget.Id, beforeId);
        Announce(DashboardText.T("web.admin.dashboard.grid.added", ("title", widget.Title), ("position", (long)position), ("total", (long)entries.Count + 1)));
        AddCompleted?.Invoke();
    }

    /// <summary>Scrolls the page while a drag sits within 80px of its top or bottom edge.</summary>
    public void AutoScroll(double pointerY, Action<double>? scrolled = null)
    {
        _scrollSpeed = DashboardDragAutoScroll.EdgeScrollSpeed(pointerY, 0, ScrollHost.ActualHeight);
        _scrolled = scrolled;
        if (_scrollSpeed == 0) _scrollTimer.Stop();
        else _scrollTimer.Start();
    }

    public void StopAutoScroll()
    {
        _scrollSpeed = 0;
        _scrolled = null;
        _scrollTimer.Stop();
    }

    private void ScrollTick()
    {
        if (!IsCustomizing || _scrollSpeed == 0) { StopAutoScroll(); return; }
        var before = ScrollHost.VerticalOffset;
        var target = Math.Clamp(before + _scrollSpeed, 0, ScrollHost.ScrollableHeight);
        if (Math.Abs(target - before) < 0.5) return;
        ScrollHost.ChangeView(null, target, null, disableAnimation: true);
        _scrolled?.Invoke(target - before);
    }
}

/// <summary>A small repeating dispatcher timer that never runs once stopped.</summary>
internal sealed class DispatcherTimerLike(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, TimeSpan interval, Action tick)
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;
    public void Start()
    {
        if (_timer != null) return;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = interval;
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => tick();
        _timer.Start();
    }
    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }
}

/// <summary>The non-modal Add widget sheet at the right edge: click appends, drag places.</summary>
internal sealed class DashboardAddSheet : Grid
{
    private readonly Border _panel = new();
    private readonly StackPanel _items = new() { Spacing = 8 };
    private readonly DashboardGridHost _host;
    private readonly Action<string> _add;

    public DashboardAddSheet(DashboardGridHost host, Action<string> add, Action close)
    {
        _host = host;
        _add = add;
        Width = 320;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Stretch;
        _panel.Background = B(BackgroundColor);
        _panel.BorderBrush = B(BorderColor);
        _panel.BorderThickness = new Thickness(1, 0, 0, 0);
        Children.Add(_panel);
        Visibility = Visibility.Collapsed;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new StackPanel { Spacing = 6, Padding = new Thickness(16, 16, 40, 0) };
        header.Children.Add(Text(DashboardText.T("web.admin.dashboard.addSheet.title"), 16, Semibold));
        header.Children.Add(Text(DashboardText.T("web.admin.dashboard.addSheet.description"), 14, color: MutedForeground, wrap: true));
        root.Children.Add(header);
        var closeButton = IconButton("x", 24, 16, DashboardText.T("common.done"), MutedForeground, Foreground, Colors.Transparent, Colors.Transparent, (_, _) => close(), 6);
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        closeButton.VerticalAlignment = VerticalAlignment.Top;
        closeButton.Margin = new Thickness(0, 14, 14, 0);
        closeButton.Opacity = 0.7;
        root.Children.Add(closeButton);
        var scroll = new ScrollViewer { Content = _items, Padding = new Thickness(16, 8, 16, 16), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        _panel.Child = root;
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; close(); } };
        AutomationProperties.SetName(this, DashboardText.T("web.admin.dashboard.addSheet.title"));
    }

    public void Populate(IReadOnlyList<DashboardWidgetDefinition> hidden)
    {
        _items.Children.Clear();
        if (hidden.Count == 0)
        {
            _items.Children.Add(new Border { Padding = new Thickness(0, 16, 0, 16), Child = Text(DashboardText.T("web.admin.dashboard.addSheet.empty"), 14, color: MutedForeground, wrap: true) });
            return;
        }
        foreach (var widget in hidden)
        {
            var plus = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(10), Background = B(Primary, 0.1), VerticalAlignment = VerticalAlignment.Top,
                Child = new Grid { Children = { CenterIcon("plus", 14, Primary) } },
            };
            var text = Column(0, Text(widget.Title, 14, Semibold, wrap: true), Text(widget.Description, 12, color: MutedForeground, wrap: true));
            var content = new Grid { ColumnSpacing = 12 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(plus);
            Grid.SetColumn(text, 1);
            content.Children.Add(text);
            var button = new Button
            {
                Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), CanDrag = true,
            };
            button.Resources["ButtonBackground"] = B(Colors.Transparent);
            button.Resources["ButtonBackgroundPointerOver"] = B(Accent, 0.4);
            button.Resources["ButtonBackgroundPressed"] = B(Accent, 0.4);
            button.Resources["ButtonBorderBrush"] = B(BorderColor);
            button.Resources["ButtonBorderBrushPointerOver"] = B(Primary, 0.5);
            button.Resources["ButtonBorderBrushPressed"] = B(Primary, 0.5);
            button.Background = B(Colors.Transparent);
            button.BorderBrush = B(BorderColor);
            AutomationProperties.SetName(button, widget.Title);
            var id = widget.Id;
            button.Click += (_, _) => _add(id);
            button.DragStarting += (_, e) =>
            {
                e.Data.SetData(DashboardWidgetFrame.AddDragFormat, id);
                e.Data.RequestedOperation = DataPackageOperation.Copy;
                _host.AddDragId = id;
                // Transparent to the pointer and faded, but kept mounted: removing the source cancels the drag.
                IsHitTestVisible = false;
                Opacity = 0.3;
            };
            button.DropCompleted += (_, _) =>
            {
                IsHitTestVisible = true;
                Opacity = 1;
                _host.EndDrag();
            };
            _items.Children.Add(button);
        }
    }

    private static FrameworkElement CenterIcon(string name, double size, Windows.UI.Color color)
    {
        var icon = Icon(name, size, color);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }
}

/// <summary>Setup the server still owes. Only not-yet-set-up items can be hidden; links to settings are out of scope.</summary>
internal static class DashboardAttentionNotice
{
    public static FrameworkElement? Create(IReadOnlyList<DashboardHealthItem> items, bool saving, Action<DashboardHealthItem> dismiss, DashboardGridMode mode, double viewportWidth)
    {
        if (items.Count == 0) return null;
        var title = Row(8, Icon("triangle-alert", 16, DashboardUi.Warning), Text(DashboardText.T("web.admin.settings.attention.title"), 14, Semibold, DashboardUi.Warning));
        var columns = viewportWidth >= 1280 ? 3 : mode == DashboardGridMode.Single ? 1 : 2;
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < (items.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var dismissible = DashboardPresentation.IsAttentionDismissible(item);
            var body = Column(0,
                Text(DashboardText.T("web.admin.settings.attention.itemHeading", ("label", item.Label), ("state", item.StateText)), 14, Medium, wrap: true),
                Text(item.Detail, 12, color: MutedForeground, wrap: true));
            var cell = new Grid();
            cell.Children.Add(new Border
            {
                BorderBrush = B(DashboardUi.Warning, 0.2), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 10, dismissible ? 40 : 12, 10), Child = body,
            });
            if (dismissible)
            {
                var hide = IconButton("x", 24, 14, DashboardText.T("web.admin.settings.attention.hideLabel", ("label", item.Label)),
                    MutedForeground, Foreground, Colors.Transparent, Alpha(DashboardUi.Warning, 0.15), (_, _) => dismiss(item), 10);
                ToolTipService.SetToolTip(hide, DashboardText.T("web.admin.settings.attention.hide"));
                hide.HorizontalAlignment = HorizontalAlignment.Right;
                hide.VerticalAlignment = VerticalAlignment.Top;
                hide.Margin = new Thickness(0, 8, 8, 0);
                hide.IsEnabled = !saving;
                cell.Children.Add(hide);
            }
            Grid.SetColumn(cell, i % columns);
            Grid.SetRow(cell, i / columns);
            grid.Children.Add(cell);
        }
        var section = new Border
        {
            BorderBrush = B(DashboardUi.Warning, 0.3), BorderThickness = new Thickness(1), Background = B(DashboardUi.Warning, 0.08),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(16), Child = Column(0, title, grid),
        };
        AutomationProperties.SetName(section, DashboardText.T("web.admin.settings.attention.title"));
        return section;
    }
}

/// <summary>The corner resize handle; its diagonal cursor can only be set from inside the control.</summary>
internal sealed class DashboardResizeHandle : Button
{
    public DashboardResizeHandle()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast);
    }
}
