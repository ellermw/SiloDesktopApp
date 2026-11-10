using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>
/// Read-only view that an ItemsRepeater binds to in place of a shared model
/// collection. ItemsRepeater rejects a change that reaches it during its own
/// layout pass, or during another change, by throwing E_FAIL from the
/// collection's CollectionChanged. Bound directly, that throw unwinds into
/// whoever changed the model (the Home reconciler), aborts the rest of the
/// update, and leaves the repeater out of step with its data. This view takes
/// the rejection instead: the model change completes for every other listener,
/// and one Reset on a later dispatcher turn resynchronizes the repeater. While
/// it is stale the view serves the snapshot taken for the rejected change and
/// holds it fixed; its realized cards still show the items it last accepted.
/// Whether WinUI's ItemsSourceView cached the new Count before the rejection
/// is not relied on: an index past the held snapshot is served from the last
/// accepted one, or from the longest snapshot rejected since, so a row that
/// shrank cannot be indexed past its end in either case. Every rejection is logged with both counts. Later model changes in
/// that window are not lost: they are coalesced into the Reset, which publishes
/// the collection as it is then. A rejected Reset is retried a few times with
/// a growing delay; after that the failure is logged to home_error.txt and
/// <see cref="GaveUp"/> lets the owner rebind the repeater. Otherwise the
/// view stays stale until the next model change. Only the repeater's
/// rejection is taken; any other failure propagates as before. The Reset
/// recycles every realized card, so the owner hooks
/// <see cref="Resyncing"/>/<see cref="Resynced"/> to carry keyboard focus
/// across it.
/// </summary>
public sealed class RepeaterItemsSource<T> : IList, INotifyCollectionChanged, IDisposable
{
    private static readonly NotifyCollectionChangedEventArgs ResetArgs =
        new(NotifyCollectionChangedAction.Reset);
    private const int MaxResyncAttempts = 3;
    private const string LogFile = "home_error.txt";

    private readonly DispatcherQueue _dispatcher;
    private DispatcherQueueTimer? _retryTimer;
    // The items as the repeater knows them. Home rows hold at most a few
    // hundred cards, so a copy per change is cheaper than replaying deltas.
    private List<T> _items;
    // The snapshot the repeater last accepted. Until the next accepted change
    // its cached Count may still describe this list instead of _items.
    private List<T> _accepted;
    // The longest snapshot delivered since the last accepted change. A rejected
    // growth may have left the repeater's cached Count at its length, so it
    // backs the indexer past both _items and _accepted.
    private List<T> _widest;
    private bool _loggedFallback;
    private bool _resyncQueued;
    // Rejected recovery Resets since the last accepted delivery.
    private int _failedResyncs;
    // Recovery gave up (or could not be queued); the next model change retries.
    private bool _stale;
    // The owner left the visual tree: no model subscription and no retry runs.
    private bool _detached;
    private bool _disposed;

    public RepeaterItemsSource(ObservableCollection<T> source, DispatcherQueue dispatcher)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _items = [.. source];
        _accepted = _items;
        _widest = _items;
        Source.CollectionChanged += OnSourceChanged;
    }

    public ObservableCollection<T> Source { get; }

    /// <summary>
    /// Moves this view's model subscription behind every listener added since,
    /// so an owner that re-subscribes on reload is still notified first. A
    /// detached view is attached again, and when the model moved on (or a
    /// recovery was interrupted) while it was detached, one Reset publishes
    /// the model as it is now. A stale view stays stale for the owner to rebind.
    /// </summary>
    public void Resubscribe()
    {
        if (_disposed) return;
        Source.CollectionChanged -= OnSourceChanged;
        Source.CollectionChanged += OnSourceChanged;
        if (!_detached) return;
        _detached = false;
        if (_stale || _resyncQueued) return;
        if (ReferenceEquals(_accepted, _items) && _items.SequenceEqual(Source)) return;
        _failedResyncs = 0;
        QueueResync();
    }

    /// <summary>
    /// Drops the model subscription and any pending retry while the owner is
    /// out of the visual tree, so the model does not keep the owner and its
    /// repeater reachable or keep driving them. <see cref="Resubscribe"/>
    /// attaches the view again.
    /// </summary>
    public void Detach()
    {
        if (_disposed || _detached) return;
        _detached = true;
        Source.CollectionChanged -= OnSourceChanged;
        if (_retryTimer is { IsRunning: true })
        {
            // Abandon the retry; reattaching queues a fresh one if still needed.
            _retryTimer.Stop();
            _resyncQueued = false;
        }
    }

    /// <summary>Changes, including recovery Resets, the bound repeater rejected.</summary>
    public int RejectedChanges { get; private set; }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Raised just before the recovery Reset reaches the repeater.</summary>
    public event Action? Resyncing;

    /// <summary>Raised after the repeater accepted the recovery Reset.</summary>
    public event Action? Resynced;

    /// <summary>
    /// Raised when the repeater rejected every recovery Reset. The view is
    /// stale until the owner rebinds the repeater or the model changes again.
    /// </summary>
    public event Action? GaveUp;

    /// <summary>The repeater is out of step with the model and no recovery is queued.</summary>
    public bool IsStale => _stale && !_disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _retryTimer?.Stop();
        Source.CollectionChanged -= OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // While a resync is pending the repeater's indices are stale; further
        // deltas would be applied to the wrong elements. The snapshot stays
        // fixed, and the queued Reset publishes the collection as it is then.
        if (_resyncQueued) return;
        if (_stale)
        {
            // A new change is a fresh chance for the abandoned recovery.
            _stale = false;
            _failedResyncs = 0;
            QueueResync();
            return;
        }
        if (!Publish(e)) QueueResync();
    }

    private bool Publish(NotifyCollectionChangedEventArgs e)
    {
        _items = [.. Source];
        try
        {
            CollectionChanged?.Invoke(this, e);
            _accepted = _items;
            _widest = _items;
            _loggedFallback = false;
            return true;
        }
        catch (Exception ex) when (IsRepeaterRejection(ex))
        {
            // Hold the new snapshot until the Reset. The indexer falls back to
            // the accepted one past its end, so the repeater stays in range
            // whichever Count its ItemsSourceView cached before rejecting.
            RejectedChanges++;
            if (_items.Count > _widest.Count) _widest = _items;
            var detail = $"RepeaterItemsSource rejected action={e.Action} type={ex.GetType().Name} hr=0x{ex.HResult:X8} count={Source.Count} snapshot={_items.Count} accepted={_accepted.Count}";
            App.RecordUiDiagnostic(detail);
            LocalLog.AppendException(LogFile, ex, detail);
            return false;
        }
        catch (Exception ex)
        {
            // Not the repeater's rejection: a real failure in a listener.
            // Report it and let it reach whoever changed the model, as before.
            LocalLog.AppendException(LogFile, ex, $"RepeaterItemsSource listener failed action={e.Action} type={ex.GetType().Name} hr=0x{ex.HResult:X8}");
            throw;
        }
    }

    private const int E_FAIL = unchecked((int)0x80004005);
    private const int E_BOUNDS = unchecked((int)0x8000000B);

    /// <summary>
    /// ItemsRepeater rejects a change with E_FAIL, matched by HRESULT rather
    /// than CLR type so a COMException or InvalidOperationException from a
    /// card handler is not mistaken for it. A WinRT E_BOUNDS from the
    /// repeater means its indices disagreed with the snapshot, which the
    /// recovery Reset resolves, so it is taken too instead of unwinding into
    /// the model change. It is matched by HRESULT whatever CLR type it
    /// projects to; a managed ArgumentOutOfRangeException from a card handler
    /// (COR_E_ARGUMENTOUTOFRANGE) is a real failure and propagates.
    /// </summary>
    public static bool IsRepeaterRejection(Exception ex) =>
        ex.HResult is E_FAIL or E_BOUNDS;

    /// <summary>
    /// The owner could not bind the repeater to this view cleanly, so the
    /// repeater may be out of step with it. Like a recovery that gave up, the
    /// next model change publishes a Reset instead of a delta.
    /// </summary>
    public void MarkStale()
    {
        if (_disposed || _resyncQueued) return;
        _stale = true;
    }

    private void QueueResync()
    {
        if (_resyncQueued || _disposed) return;
        _resyncQueued = true;
        if (_failedResyncs == 0)
        {
            // Normal priority so the Reset lands ahead of SectionRow's deferred
            // scroll restore, which UpdateSection enqueues after this.
            if (_dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, Resync)) return;
            _resyncQueued = false;
            _stale = true;
            App.RecordUiDiagnostic("RepeaterItemsSource resync could not be queued");
            LocalLog.AppendLine(LogFile, $"RepeaterItemsSource resync could not be queued count={Source.Count}; row stays stale until its next change");
            return;
        }
        // A rejected Reset is retried after a growing delay so the layout
        // that rejected it can finish instead of being re-entered every turn.
        if (_retryTimer == null)
        {
            _retryTimer = _dispatcher.CreateTimer();
            _retryTimer.IsRepeating = false;
            _retryTimer.Tick += (_, _) => Resync();
        }
        _retryTimer.Interval = TimeSpan.FromMilliseconds(50 << _failedResyncs);
        _retryTimer.Start();
    }

    private void Resync()
    {
        _resyncQueued = false;
        // A detached view resyncs when it is attached again.
        if (_disposed || _detached) return;
        // This runs on the dispatcher, where nothing above can handle a
        // failure: an owner callback or listener that throws is logged and
        // the recovery bookkeeping carries on.
        RaiseOwner(Resyncing, nameof(Resyncing));
        // A Reset raised inside another layout pass is rejected the same way.
        bool accepted;
        try
        {
            accepted = Publish(ResetArgs);
        }
        catch (Exception)
        {
            // Publish logged the listener failure. Count it as a rejected
            // Reset so it is retried and, failing that, given up on.
            if (_items.Count > _widest.Count) _widest = _items;
            accepted = false;
        }
        if (accepted)
        {
            _failedResyncs = 0;
            RaiseOwner(Resynced, nameof(Resynced));
            return;
        }
        if (++_failedResyncs < MaxResyncAttempts)
        {
            QueueResync();
            return;
        }
        // Stop recycling every card: leave the row stale but reported, and
        // let the next model change try again.
        _failedResyncs = 0;
        _stale = true;
        App.RecordUiDiagnostic($"RepeaterItemsSource gave up after {MaxResyncAttempts} rejected resyncs");
        LocalLog.AppendLine(LogFile, $"RepeaterItemsSource gave up after {MaxResyncAttempts} rejected resyncs count={Source.Count}; row stays stale until it is rebound or changes");
        RaiseOwner(GaveUp, nameof(GaveUp));
    }

    private void RaiseOwner(Action? handler, string name)
    {
        if (handler == null) return;
        try
        {
            handler();
        }
        catch (Exception ex)
        {
            var detail = $"RepeaterItemsSource {name} handler failed type={ex.GetType().Name} hr=0x{ex.HResult:X8}";
            App.RecordUiDiagnostic(detail);
            LocalLog.AppendException(LogFile, ex, detail);
        }
    }

    public object? this[int index]
    {
        get
        {
            if (index >= 0 && index < _items.Count) return _items[index];
            if (!_loggedFallback)
            {
                _loggedFallback = true;
                var detail = $"RepeaterItemsSource read index={index} past snapshot={_items.Count}; serving accepted={_accepted.Count} widest={_widest.Count} until the Reset";
                App.RecordUiDiagnostic(detail);
                LocalLog.AppendLine(LogFile, detail);
            }
            if (index >= 0 && index < _accepted.Count) return _accepted[index];
            if (index >= 0 && index < _widest.Count) return _widest[index];
            // No delivered snapshot is this long. Never throw into the
            // repeater's layout pass; serve a real item until the Reset.
            return _widest.Count > 0 ? _widest[^1] : null;
        }
        set => throw new NotSupportedException();
    }

    public int Count => _items.Count;
    public bool IsFixedSize => false;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public bool Contains(object? value) => value is T item && _items.Contains(item);
    public int IndexOf(object? value) => value is T item ? _items.IndexOf(item) : -1;
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
