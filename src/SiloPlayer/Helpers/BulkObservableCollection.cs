using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SiloPlayer.Helpers;

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

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        // Single ranged Add: ItemsRepeater adds new elements without touching existing ones
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, newItems, startIndex));
    }

    /// <summary>
    /// Removes the first <paramref name="count"/> items with a single ranged
    /// Remove notification. Used to implement a sliding-window cap on
    /// ItemsRepeater-backed collections so WinUI never has to manage more
    /// than a bounded number of realized elements at once.
    /// </summary>
    public void RemoveFromFront(int count)
    {
        if (count <= 0 || Items.Count == 0) return;
        count = Math.Min(count, Items.Count);

        var removed = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            removed.Add(Items[0]);
            Items.RemoveAt(0);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, removed, 0));
    }
}
