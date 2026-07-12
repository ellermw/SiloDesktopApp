using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Helpers;

/// <summary>
/// Sparse, read-only item list for large virtualized catalog grids.
/// The list exposes the server-reported total count immediately, while
/// individual pages are filled in as the viewport asks for them.
/// </summary>
public sealed class VirtualCatalogItems : IList<MediaItem?>, IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly Dictionary<int, IReadOnlyList<MediaItem>> _pages = new();
    private int _count;

    public VirtualCatalogItems(int pageSize)
    {
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        PageSize = pageSize;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public int PageSize { get; }

    public int Count => _count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public MediaItem? this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var pageIndex = index / PageSize;
            var itemIndex = index % PageSize;
            return _pages.TryGetValue(pageIndex, out var page) && itemIndex < page.Count
                ? page[itemIndex]
                : null;
        }
        set => throw new NotSupportedException();
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public bool IsPageLoaded(int pageIndex) => _pages.ContainsKey(pageIndex);

    public void Reset(int totalCount)
    {
        _pages.Clear();
        SetCount(totalCount);
    }

    public void SetCount(int totalCount)
    {
        totalCount = Math.Max(0, totalCount);
        if (_count == totalCount) return;

        _count = totalCount;
        foreach (var pageIndex in _pages.Keys.Where(page => page * PageSize >= _count).ToList())
            _pages.Remove(pageIndex);

        OnPropertyChanged(nameof(Count));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void SetPage(int pageIndex, IReadOnlyList<MediaItem> items)
    {
        if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (_count == 0) return;

        var startIndex = pageIndex * PageSize;
        if (startIndex >= _count) return;

        var replaceCount = Math.Min(items.Count, _count - startIndex);
        if (replaceCount <= 0) return;

        var oldItems = new List<MediaItem?>(replaceCount);
        for (int i = 0; i < replaceCount; i++)
            oldItems.Add(this[startIndex + i]);

        _pages[pageIndex] = items.ToList();

        var newItems = new List<MediaItem?>(replaceCount);
        for (int i = 0; i < replaceCount; i++)
            newItems.Add(items[i]);

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Replace,
            newItems,
            oldItems,
            startIndex));
    }

    public int IndexOf(MediaItem? item)
    {
        if (item == null) return -1;
        foreach (var (pageIndex, page) in _pages)
        {
            for (int i = 0; i < page.Count; i++)
            {
                if (Equals(page[i], item))
                    return pageIndex * PageSize + i;
            }
        }
        return -1;
    }

    public bool Contains(MediaItem? item) => IndexOf(item) >= 0;

    public void CopyTo(MediaItem?[] array, int arrayIndex)
    {
        for (int i = 0; i < _count; i++)
            array[arrayIndex + i] = this[i];
    }

    public IEnumerator<MediaItem?> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(MediaItem? item) => throw new NotSupportedException();
    public void Clear() => Reset(0);
    public void Insert(int index, MediaItem? item) => throw new NotSupportedException();
    public bool Remove(MediaItem? item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();

    int IList.Add(object? value) => throw new NotSupportedException();
    bool IList.Contains(object? value) => value is MediaItem item && Contains(item);
    int IList.IndexOf(object? value) => value is MediaItem item ? IndexOf(item) : -1;
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();

    public void CopyTo(Array array, int index)
    {
        for (int i = 0; i < _count; i++)
            array.SetValue(this[i], index + i);
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        => CollectionChanged?.Invoke(this, args);

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
