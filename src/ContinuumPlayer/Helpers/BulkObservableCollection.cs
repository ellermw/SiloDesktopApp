using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ContinuumPlayer.Helpers;

/// <summary>
/// ObservableCollection that supports bulk additions with a single Add notification
/// instead of one notification per item. Prevents UI freeze when adding many items at once.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Adds all items, then fires a single CollectionChanged(Add) with the full range.
    /// Existing realized UI elements are preserved — only new elements are created.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        var newItems = items as IList ?? items.ToList();
        if (newItems.Count == 0) return;

        int startIndex = Items.Count;
        foreach (T item in newItems)
            Items.Add(item); // Protected Items list — no per-item notification

        // Single ranged Add: ItemsRepeater adds new elements without touching existing ones
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, newItems, startIndex));
    }
}
