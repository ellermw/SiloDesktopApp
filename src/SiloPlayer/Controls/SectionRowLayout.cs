using System.Collections.Specialized;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SiloPlayer.Controls;

/// <summary>
/// The card row of a Home or library section. Cards are placed exactly as by a horizontal StackLayout
/// (card i at i x (card width + Spacing), each measured with the row's available size and arranged at least
/// as tall as the row; extent n x pitch - Spacing by the tallest card measured), with one difference.
/// ItemsRepeater gives a row outside the page's viewport an empty realization window, so StackLayout keeps a
/// single card there and creates the rest in the frame the row scrolls in: 17 cards at 3-8 ms each is a
/// 60-120 ms frame in the middle of the scroll. This layout keeps the cards a row shows at its current
/// horizontal position realized while the row is out of view, and builds the missing ones on idle frames:
/// one row at a time (top first), at low dispatcher priority (after input), about one card per frame, and
/// never while any row's viewport is moving. A row is then already built when it scrolls in.
/// </summary>
public sealed class SectionRowLayout : VirtualizingLayout
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(SectionRowLayout),
        new PropertyMetadata(0d, (d, _) => ((SectionRowLayout)d).InvalidateMeasure()));

    /// <summary>Space between cards, as StackLayout.Spacing.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The repeater using this layout: which of its cards are realized.</summary>
    internal ItemsRepeater? Owner { get; set; }

    /// <summary>The row's visible horizontal window in the repeater's coordinates (start, width).</summary>
    internal Func<(double Start, double Width)>? VisibleWindow { get; set; }

    /// <summary>UI-thread time per frame, shared by all rows, spent building cards ahead (at least one card).</summary>
    internal const double FrameBudgetMs = 2;

    /// <summary>Quiet time after any row's viewport moved before cards are built ahead again.</summary>
    internal const double QuietAfterMotionMs = 150;

    private static long s_lastMotion;
    private static double s_budgetSpentMs;
    private static bool s_watchingFrames;
    // Rows with cards still to build, in the order they asked (top to bottom). Only the first builds and is
    // re-measured on a given frame, so waiting rows cost nothing.
    private static readonly List<SectionRowLayout> s_waiting = new();
    private static bool s_kickQueued;
    private static DispatcherQueueTimer? s_quietTimer;

    private readonly List<(int Index, UIElement Element)> _realized = new();
    private double _pitch;
    private double _maxHeight;
    private Point _lastCenter = new(double.NaN, double.NaN);

    /// <summary>Cards built ahead by every row of the process (tests and diagnostics).</summary>
    internal static int BuiltAhead { get; private set; }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        _realized.Clear();
        var count = context.ItemCount;
        if (count == 0)
        {
            _maxHeight = 0;
            _lastCenter = new(double.NaN, double.NaN);
            return new Size(0, 0);
        }

        var spacing = Spacing;
        var window = context.RealizationRect;
        var inView = window.Width > 0 && window.Height > 0 && !double.IsInfinity(window.Width) && !double.IsInfinity(window.Height);
        double start, width;
        if (inView)
        {
            (start, width) = (window.X, window.Width);
            // A moved centre is scrolling (page or row); the cache growing around it is not.
            var center = new Point(window.X + window.Width / 2, window.Y + window.Height / 2);
            if (!double.IsNaN(_lastCenter.X) && (Math.Abs(center.X - _lastCenter.X) > 0.5 || Math.Abs(center.Y - _lastCenter.Y) > 0.5))
                s_lastMotion = Stopwatch.GetTimestamp();
            _lastCenter = center;
        }
        else
        {
            (start, width) = VisibleWindow?.Invoke() ?? (0, 0);
            _lastCenter = new(double.NaN, double.NaN);
        }

        // The anchor (StackLayout's: the card at the window's start) also gives the row's card width.
        var anchor = _pitch > 0 ? Math.Clamp((int)Math.Floor(start / _pitch), 0, count - 1) : 0;
        var anchorElement = Realize(context, anchor, availableSize);
        var cardWidth = anchorElement.DesiredSize.Width;
        _pitch = cardWidth + spacing;
        var low = anchor;
        var high = anchor;
        if (_pitch > 0 && width > 0)
        {
            // Every card whose bounds intersect the window, as StackLayout realizes them.
            low = Math.Clamp((int)Math.Floor((start - cardWidth) / _pitch) + 1, 0, count - 1);
            high = Math.Clamp((int)Math.Ceiling((start + width) / _pitch) - 1, 0, count - 1);
            if (high < low) low = high = anchor;
        }

        // Cards inside the row's visible width are created at once in view. The rest of the window (the
        // repeater's off-screen cache, or everything while the row is out of view) is built ahead instead,
        // so a row entering the window during a scroll does not create cache cards in that frame.
        var (visibleLow, visibleHigh) = (-1, -1);
        if (inView && VisibleWindow?.Invoke() is { } visible && visible.Width > 0 && _pitch > 0)
            (visibleLow, visibleHigh) = (Math.Clamp((int)Math.Floor((visible.Start - cardWidth) / _pitch) + 1, 0, count - 1),
                Math.Clamp((int)Math.Ceiling((visible.Start + visible.Width) / _pitch) - 1, 0, count - 1));
        else if (inView) (visibleLow, visibleHigh) = (low, high);
        HashSet<int>? alreadyRealized = null;
        var pending = false;
        for (var index = low; index <= high; index++)
        {
            if (index == anchor) continue;
            if (index >= visibleLow && index <= visibleHigh || (alreadyRealized ??= RealizedIndexes()).Contains(index))
            {
                Realize(context, index, availableSize);
                continue;
            }
            if (!MayBuildAhead(this)) { pending = true; continue; }
            var started = Stopwatch.GetTimestamp();
            Realize(context, index, availableSize);
            s_budgetSpentMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            BuiltAhead++;
        }
        // Keep a card the repeater was asked to bring into view (focus, BringIntoView).
        var recommended = context.RecommendedAnchorIndex;
        if (recommended >= 0 && recommended < count && (recommended < low || recommended > high) && recommended != anchor)
            Realize(context, recommended, availableSize);

        if (pending) Wait(this);
        else if (s_waiting.Remove(this)) KickNext();
        return new Size(Math.Max(0, count * _pitch - spacing), _maxHeight);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        foreach (var (index, element) in _realized)
            element.Arrange(new Rect(index * _pitch, 0, element.DesiredSize.Width, Math.Max(element.DesiredSize.Height, finalSize.Height)));
        return finalSize;
    }

    protected override void OnItemsChangedCore(VirtualizingLayoutContext context, object source, NotifyCollectionChangedEventArgs args)
    {
        // ItemsRepeater re-indexes and clears cards for inserted and removed items itself. For the other
        // changes the layout gives back the cards whose item changed, as StackLayout does, so the next
        // measure makes them again for their new items (or with a new ItemTemplate, which reaches only the
        // layout, as a Reset).
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                _maxHeight = 0;
                Recycle(context, _ => true);
                _realized.Clear();
                break;
            case NotifyCollectionChangedAction.Replace:
                var start = args.OldStartingIndex;
                var replaced = Math.Min(args.OldItems?.Count ?? 0, args.NewItems?.Count ?? 0);
                Recycle(context, index => index >= start && index < start + replaced);
                break;
            case NotifyCollectionChangedAction.Move:
                var low = Math.Min(args.OldStartingIndex, args.NewStartingIndex);
                var high = Math.Max(args.OldStartingIndex, args.NewStartingIndex) + Math.Max(1, args.OldItems?.Count ?? 1);
                Recycle(context, index => index >= low && index < high);
                break;
        }
        base.OnItemsChangedCore(context, source, args);
    }

    /// <summary>Gives back this row's realized cards whose current index matches (cards already cleared are skipped).</summary>
    private void Recycle(VirtualizingLayoutContext context, Func<int, bool> affected)
    {
        for (var i = _realized.Count - 1; i >= 0; i--)
        {
            var element = _realized[i].Element;
            var index = Owner?.GetElementIndex(element) ?? -1;
            if (index < 0 || !affected(index)) continue;
            context.RecycleElement(element);
            _realized.RemoveAt(i);
        }
    }

    private UIElement Realize(VirtualizingLayoutContext context, int index, Size availableSize)
    {
        var element = context.GetOrCreateElementAt(index);
        element.Measure(availableSize);
        _maxHeight = Math.Max(_maxHeight, element.DesiredSize.Height);
        _realized.Add((index, element));
        return element;
    }

    private HashSet<int> RealizedIndexes()
    {
        var indexes = new HashSet<int>();
        if (Owner is not { } owner) return indexes;
        for (var i = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(owner) - 1; i >= 0; i--)
        {
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(owner, i) is not UIElement child) continue;
            var index = owner.GetElementIndex(child);
            if (index >= 0) indexes.Add(index);
        }
        return indexes;
    }

    private static bool MayBuildAhead(SectionRowLayout row)
    {
        if (Stopwatch.GetElapsedTime(s_lastMotion).TotalMilliseconds < QuietAfterMotionMs) return false;
        if (Turn() is { } turn && !ReferenceEquals(turn, row)) return false;
        // The budget belongs to the frame: it is refilled when the next frame starts (OnFrame).
        WatchFrames();
        return s_budgetSpentMs < FrameBudgetMs;
    }

    // Rendering is raised at the start of each frame, before its layout pass. Watched only while rows wait.
    private static void WatchFrames()
    {
        if (s_watchingFrames) return;
        s_watchingFrames = true;
        s_budgetSpentMs = 0;
        CompositionTarget.Rendering += OnFrame;
    }

    private static void OnFrame(object? sender, object e)
    {
        s_budgetSpentMs = 0;
        if (s_waiting.Count > 0) return;
        CompositionTarget.Rendering -= OnFrame;
        s_watchingFrames = false;
    }

    private static void Wait(SectionRowLayout row)
    {
        if (!s_waiting.Contains(row)) s_waiting.Add(row);
        KickNext();
    }

    /// <summary>Re-measures the first waiting row on a later idle frame (low priority, after input and
    /// ordinary UI work), or once the page has been still long enough.</summary>
    private static void KickNext(double delayMs = 0)
    {
        if (s_kickQueued) return;
        Prune();
        if (s_waiting.Count == 0) return;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        if (dispatcher == null) return;
        s_kickQueued = true;
        delayMs = Math.Max(delayMs, QuietAfterMotionMs - Stopwatch.GetElapsedTime(s_lastMotion).TotalMilliseconds);
        if (delayMs > 0)
        {
            if (s_quietTimer == null)
            {
                s_quietTimer = dispatcher.CreateTimer();
                s_quietTimer.IsRepeating = false;
                s_quietTimer.Tick += (_, _) => Kick();
            }
            s_quietTimer.Interval = TimeSpan.FromMilliseconds(delayMs + 10);
            s_quietTimer.Start();
            return;
        }
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Low, Kick)) s_kickQueued = false;
    }

    private static void Kick()
    {
        s_kickQueued = false;
        Kicks++;
        Prune();
        Turn()?.InvalidateMeasure();
    }

    /// <summary>The waiting row that builds now: the first one whose repeater has loaded.</summary>
    private static SectionRowLayout? Turn() => s_waiting.FirstOrDefault(row => row.Owner is { IsLoaded: true });

    /// <summary>Times the build-ahead queue has run in the process (tests and diagnostics).</summary>
    internal static int Kicks { get; private set; }

    /// <summary>Rows waiting to build cards ahead (tests and diagnostics).</summary>
    internal static int Waiting => s_waiting.Count;

    // A row whose repeater is not in the live tree (still entering it, its page cached away by navigation,
    // or discarded) gives up its place, so nothing waits on it and nothing static keeps it. Its repeater's
    // Loaded measures it again, and a row with cards still to build then asks again.
    private static void Prune()
    {
        for (var i = s_waiting.Count - 1; i >= 0; i--)
        {
            var row = s_waiting[i];
            if (row.Owner is { IsLoaded: true }) continue;
            s_waiting.RemoveAt(i);
            row.AwaitLoad();
        }
    }

    private bool _awaitingLoad;

    private void AwaitLoad()
    {
        if (_awaitingLoad || Owner is not { } owner) return;
        _awaitingLoad = true;
        owner.Loaded += OnOwnerLoaded;
    }

    private void OnOwnerLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)sender).Loaded -= OnOwnerLoaded;
        _awaitingLoad = false;
        InvalidateMeasure();
    }
}