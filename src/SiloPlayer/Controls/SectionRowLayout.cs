using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// <remarks>
/// Every row uses <see cref="Shared"/>, which lives as long as the process; each row's state lives in its
/// repeater's layout context. A layout is a managed subclass of a WinRT type: one instance per row could be
/// collected together with its row before the native repeater lets go of it, leaving the repeater's
/// teardown calling into a released wrapper.
/// </remarks>
public sealed class SectionRowLayout : VirtualizingLayout
{
    /// <summary>The layout every card row uses.</summary>
    public static SectionRowLayout Shared { get; } = new();

    private SectionRowLayout() { }

    /// <summary>Space between a row's cards, as StackLayout.Spacing; set on the row's ItemsRepeater.</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.RegisterAttached(
        "Spacing", typeof(double), typeof(SectionRowLayout),
        new PropertyMetadata(0d, (d, e) =>
        {
            Spacings.AddOrUpdate(d, new StrongBox<double>((double)e.NewValue));
            (d as UIElement)?.InvalidateMeasure();
        }));

    // Measure reads the spacing from here: reading the property itself boxes the value, every frame a row is in view.
    private static readonly ConditionalWeakTable<DependencyObject, StrongBox<double>> Spacings = new();

    public static double GetSpacing(DependencyObject repeater) => (double)repeater.GetValue(SpacingProperty);

    // The table is keyed by the repeater's wrapper: a repeater whose wrapper was made again (none of its
    // owners kept it) reads the property once more and is remembered under the new one.
    private static double SpacingOf(DependencyObject repeater)
    {
        if (Spacings.TryGetValue(repeater, out var set)) return set.Value;
        var spacing = GetSpacing(repeater);
        Spacings.AddOrUpdate(repeater, new StrongBox<double>(spacing));
        return spacing;
    }

    public static void SetSpacing(DependencyObject repeater, double value) => repeater.SetValue(SpacingProperty, value);

    /// <summary>UI-thread time per frame, shared by all rows, spent building cards ahead (at least one card).</summary>
    internal const double FrameBudgetMs = 2;

    /// <summary>Quiet time after any row's viewport moved before cards are built ahead again.</summary>
    internal const double QuietAfterMotionMs = 150;

    private static long s_lastMotion;
    private static double s_budgetSpentMs;
    private static bool s_watchingFrames;
    // Rows with cards still to build, in the order they asked (top to bottom). Only the first builds and is
    // re-measured on a given frame, so waiting rows cost nothing.
    private static readonly List<Row> s_waiting = new();
    private static bool s_kickQueued;
    private static DispatcherQueueTimer? s_quietTimer;

    /// <summary>Cards built ahead by every row of the process (tests and diagnostics).</summary>
    internal static int BuiltAhead { get; private set; }

    /// <summary>Times the build-ahead queue has run in the process (tests and diagnostics).</summary>
    internal static int Kicks { get; private set; }

    /// <summary>Rows waiting to build cards ahead (tests and diagnostics).</summary>
    internal static int Waiting => s_waiting.Count;

    /// <summary>One row's state, kept in its repeater's layout context.</summary>
    private sealed class Row
    {
        public readonly List<(int Index, UIElement Element)> Realized = new();
        // The indexes realized by the previous measure (kept, so measuring allocates nothing).
        public readonly HashSet<int> Previous = new();
        public double Pitch;
        public double MaxHeight;
        public Point LastCenter = new(double.NaN, double.NaN);
        public bool AwaitingLoad;
        // Held weakly: the repeater holds this state.
        private WeakReference<ItemsRepeater>? _owner;
        private WeakReference<ScrollViewer>? _scroller;

        public ItemsRepeater? Owner => _owner != null && _owner.TryGetTarget(out var owner) ? owner : null;

        /// <summary>Learns the row's repeater (a realized card's parent) and its horizontal scroller.</summary>
        public void Attach(UIElement card)
        {
            if (Owner != null || VisualTreeHelper.GetParent(card) is not ItemsRepeater owner) return;
            _owner = new(owner);
            for (var parent = VisualTreeHelper.GetParent(owner); parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroller) { _scroller = new(scroller); break; }
        }

        /// <summary>The row's visible horizontal window in the repeater's coordinates (start, width).</summary>
        public (double Start, double Width)? VisibleWindow() =>
            _scroller != null && _scroller.TryGetTarget(out var scroller) && Owner is { } owner
                ? (scroller.HorizontalOffset - owner.Margin.Left, scroller.ViewportWidth) : null;

        public void Remeasure() => Owner?.InvalidateMeasure();

        public void AwaitLoad()
        {
            if (AwaitingLoad || Owner is not { } owner) return;
            AwaitingLoad = true;
            owner.Loaded += OnOwnerLoaded;
        }

        private void OnOwnerLoaded(object sender, RoutedEventArgs e)
        {
            ((FrameworkElement)sender).Loaded -= OnOwnerLoaded;
            AwaitingLoad = false;
            Remeasure();
        }
    }

    protected override void InitializeForContextCore(VirtualizingLayoutContext context) => context.LayoutState = new Row();

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        if (context.LayoutState is Row row) s_waiting.Remove(row);
        context.LayoutState = null;
    }

    private static Row StateOf(VirtualizingLayoutContext context) =>
        context.LayoutState as Row ?? (Row)(context.LayoutState = new Row());

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var row = StateOf(context);
        row.Previous.Clear();
        foreach (var (index, _) in row.Realized) row.Previous.Add(index);
        row.Realized.Clear();
        var count = context.ItemCount;
        if (count == 0)
        {
            row.MaxHeight = 0;
            row.LastCenter = new(double.NaN, double.NaN);
            return new Size(0, 0);
        }

        var window = context.RealizationRect;
        var inView = window.Width > 0 && window.Height > 0 && !double.IsInfinity(window.Width) && !double.IsInfinity(window.Height);

        // The anchor (StackLayout's: the card at the window's start) also gives the row's card width.
        var anchorStart = inView ? window.X : row.VisibleWindow()?.Start ?? 0;
        var anchor = row.Pitch > 0 ? Math.Clamp((int)Math.Floor(anchorStart / row.Pitch), 0, count - 1) : 0;
        var anchorElement = Realize(context, row, anchor, availableSize);
        row.Attach(anchorElement);
        var spacing = row.Owner is { } repeater ? SpacingOf(repeater) : 0;
        var cardWidth = anchorElement.DesiredSize.Width;
        row.Pitch = cardWidth + spacing;

        double start, width;
        if (inView)
        {
            (start, width) = (window.X, window.Width);
            // A moved centre is scrolling (page or row); the cache growing around it is not.
            var center = new Point(window.X + window.Width / 2, window.Y + window.Height / 2);
            if (!double.IsNaN(row.LastCenter.X) && (Math.Abs(center.X - row.LastCenter.X) > 0.5 || Math.Abs(center.Y - row.LastCenter.Y) > 0.5))
                s_lastMotion = Stopwatch.GetTimestamp();
            row.LastCenter = center;
        }
        else
        {
            (start, width) = row.VisibleWindow() ?? (0, 0);
            row.LastCenter = new(double.NaN, double.NaN);
        }

        var low = anchor;
        var high = anchor;
        if (row.Pitch > 0 && width > 0)
        {
            // Every card whose bounds intersect the window, as StackLayout realizes them.
            low = Math.Clamp((int)Math.Floor((start - cardWidth) / row.Pitch) + 1, 0, count - 1);
            high = Math.Clamp((int)Math.Ceiling((start + width) / row.Pitch) - 1, 0, count - 1);
            if (high < low) low = high = anchor;
        }

        // Cards inside the row's visible width are created at once in view. The rest of the window (the
        // repeater's off-screen cache, or everything while the row is out of view) is built ahead instead,
        // so a row entering the window during a scroll does not create cache cards in that frame.
        var (visibleLow, visibleHigh) = (-1, -1);
        if (inView && row.VisibleWindow() is { } visible && visible.Width > 0 && row.Pitch > 0)
            (visibleLow, visibleHigh) = (Math.Clamp((int)Math.Floor((visible.Start - cardWidth) / row.Pitch) + 1, 0, count - 1),
                Math.Clamp((int)Math.Ceiling((visible.Start + visible.Width) / row.Pitch) - 1, 0, count - 1));
        else if (inView) (visibleLow, visibleHigh) = (low, high);
        var pending = false;
        for (var index = low; index <= high; index++)
        {
            if (index == anchor) continue;
            if (index >= visibleLow && index <= visibleHigh || row.Previous.Contains(index))
            {
                Realize(context, row, index, availableSize);
                continue;
            }
            if (!MayBuildAhead(row)) { pending = true; continue; }
            var started = Stopwatch.GetTimestamp();
            Realize(context, row, index, availableSize);
            s_budgetSpentMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            BuiltAhead++;
        }
        // Keep a card the repeater was asked to bring into view (focus, BringIntoView).
        var recommended = context.RecommendedAnchorIndex;
        if (recommended >= 0 && recommended < count && (recommended < low || recommended > high) && recommended != anchor)
            Realize(context, row, recommended, availableSize);

        if (pending) Wait(row);
        else if (s_waiting.Remove(row)) KickNext();
        return new Size(Math.Max(0, count * row.Pitch - spacing), row.MaxHeight);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        var row = StateOf(context);
        foreach (var (index, element) in row.Realized)
            element.Arrange(new Rect(index * row.Pitch, 0, element.DesiredSize.Width, Math.Max(element.DesiredSize.Height, finalSize.Height)));
        return finalSize;
    }

    protected override void OnItemsChangedCore(VirtualizingLayoutContext context, object source, NotifyCollectionChangedEventArgs args)
    {
        // ItemsRepeater re-indexes and clears cards for inserted and removed items itself. For the other
        // changes the layout gives back the cards whose item changed, as StackLayout does, so the next
        // measure makes them again for their new items (or with a new ItemTemplate, which reaches only the
        // layout, as a Reset).
        var row = StateOf(context);
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Reset:
                row.MaxHeight = 0;
                Recycle(context, row, _ => true);
                row.Realized.Clear();
                break;
            case NotifyCollectionChangedAction.Replace:
                var start = args.OldStartingIndex;
                var replaced = Math.Min(args.OldItems?.Count ?? 0, args.NewItems?.Count ?? 0);
                Recycle(context, row, index => index >= start && index < start + replaced);
                break;
            case NotifyCollectionChangedAction.Add:
                // The repeater moved the cards after the insertion up; the remembered indexes follow them.
                Shift(row, args.NewStartingIndex, args.NewItems?.Count ?? 0);
                break;
            case NotifyCollectionChangedAction.Remove:
                Shift(row, args.OldStartingIndex, -(args.OldItems?.Count ?? 0));
                break;
            case NotifyCollectionChangedAction.Move:
                var low = Math.Min(args.OldStartingIndex, args.NewStartingIndex);
                var high = Math.Max(args.OldStartingIndex, args.NewStartingIndex) + Math.Max(1, args.OldItems?.Count ?? 1);
                Recycle(context, row, index => index >= low && index < high);
                break;
        }
        // The row's own repeater measures again. A row that has not measured a card yet does not know its
        // repeater: then every row using the layout does, as with a shared StackLayout.
        if (row.Owner != null) row.Remeasure();
        else base.OnItemsChangedCore(context, source, args);
    }

    /// <summary>Gives back the row's realized cards whose current index matches (cards already cleared are skipped).</summary>
    private static void Recycle(VirtualizingLayoutContext context, Row row, Func<int, bool> affected)
    {
        for (var i = row.Realized.Count - 1; i >= 0; i--)
        {
            var element = row.Realized[i].Element;
            var index = row.Owner?.GetElementIndex(element) ?? -1;
            if (index < 0 || !affected(index)) continue;
            context.RecycleElement(element);
            row.Realized.RemoveAt(i);
        }
    }

    private static UIElement Realize(VirtualizingLayoutContext context, Row row, int index, Size availableSize)
    {
        var element = context.GetOrCreateElementAt(index);
        element.Measure(availableSize);
        row.MaxHeight = Math.Max(row.MaxHeight, element.DesiredSize.Height);
        row.Realized.Add((index, element));
        return element;
    }

    /// <summary>Follows an insertion (<paramref name="delta"/> &gt; 0) or removal (&lt; 0) at <paramref name="at"/> in the realized cards' indexes.</summary>
    private static void Shift(Row row, int at, int delta)
    {
        if (at < 0 || delta == 0) return;
        for (var i = row.Realized.Count - 1; i >= 0; i--)
        {
            var (index, element) = row.Realized[i];
            if (index < at) continue;
            if (delta < 0 && index < at - delta) row.Realized.RemoveAt(i);
            else row.Realized[i] = (index + delta, element);
        }
    }

    private static bool MayBuildAhead(Row row)
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

    private static void Wait(Row row)
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
        Turn()?.Remeasure();
    }

    /// <summary>The waiting row that builds now: the first one whose repeater has loaded.</summary>
    private static Row? Turn() => s_waiting.FirstOrDefault(row => row.Owner is { IsLoaded: true });

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
}
