using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Dispatching;

namespace SiloPlayer.Helpers;

/// <summary>
/// Read-only view that an ItemsRepeater binds to in place of a shared model
/// collection. ItemsRepeater rejects a change that reaches it during its own
/// layout pass, or during another change, by throwing E_FAIL from the
/// collection's CollectionChanged. Bound directly, that throw unwinds into
/// whoever changed the model (the Home reconciler), aborts the rest of the
/// update, and leaves the repeater out of step with its data. This view takes
/// the rejection instead: the model change completes for every other listener,
/// the repeater reads a snapshot that stays fixed while it is stale, and one
/// Reset on a later dispatcher turn resynchronizes it.
/// </summary>
public sealed class RepeaterItemsSource<T> : IList, INotifyCollectionChanged, IDisposable
{
    private static readonly NotifyCollectionChangedEventArgs ResetArgs =
        new(NotifyCollectionChangedAction.Reset);

    private readonly DispatcherQueue _dispatcher;
    // The items as the repeater knows them. Home rows hold at most a few
    // hundred cards, so a copy per change is cheaper than replaying deltas.
    private List<T> _items;
    private bool _resyncQueued;
    private bool _disposed;

    public RepeaterItemsSource(ObservableCollection<T> source, DispatcherQueue dispatcher)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _items = [.. source];
        Source.CollectionChanged += OnSourceChanged;
    }

    public ObservableCollection<T> Source { get; }

    /// <summary>Changes the bound repeater rejected; each one queued a resync.</summary>
    public int RejectedChanges { get; private set; }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Source.CollectionChanged -= OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // While a resync is pending the repeater's indices are stale; further
        // deltas would be applied to the wrong elements. The snapshot stays
        // fixed, and the queued Reset publishes the collection as it is then.
        if (_resyncQueued) return;
        Publish(e);
    }

    private void Publish(NotifyCollectionChangedEventArgs e)
    {
        _items = [.. Source];
        try
        {
            CollectionChanged?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            // WinUI's ItemsSourceView refreshes its cached Count before the
            // repeater's handler rejects the change, so keep the snapshot that
            // matches that Count and hold it until the Reset. The observed
            // rejection is a COMException (E_FAIL), but any failure to deliver
            // leaves the repeater equally stale and must not reach the model.
            RejectedChanges++;
            App.RecordUiDiagnostic($"RepeaterItemsSource rejected action={e.Action} type={ex.GetType().Name} hr=0x{ex.HResult:X8} count={Source.Count}");
            QueueResync();
        }
    }

    private void QueueResync()
    {
        if (_resyncQueued || _disposed) return;
        _resyncQueued = true;
        // Normal priority so the Reset lands ahead of SectionRow's deferred
        // scroll restore, which UpdateSection enqueues after this.
        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, Resync))
            _resyncQueued = false;
    }

    private void Resync()
    {
        _resyncQueued = false;
        if (_disposed) return;
        // A Reset raised inside another layout pass is rejected the same way
        // and queues the next attempt.
        Publish(ResetArgs);
    }

    public object? this[int index]
    {
        get => _items[index];
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
