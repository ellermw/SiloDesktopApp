namespace SiloPlayer.Core.Services;

/// <summary>
/// Stores a small set of profile-scoped, short-lived watch-detail requests so
/// hover, detail navigation, and playback can share the same network request.
/// </summary>
public sealed class WatchDetailPrefetchCache<T> where T : class
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly TimeSpan _lifetime;

    private sealed record Entry(string ProfileId, DateTimeOffset CreatedAt, Task<T> Task);

    public WatchDetailPrefetchCache(int capacity, TimeSpan lifetime)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        _capacity = capacity;
        _lifetime = lifetime;
    }

    public void Store(string contentId, string profileId, Task<T> task, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
        {
            PruneLocked(now, profileId);
            MakeRoomLocked(contentId);
            _entries[contentId] = new Entry(profileId, now, task);
        }
    }

    public Task<T> GetOrAdd(
        string contentId,
        string profileId,
        Func<Task<T>> taskFactory,
        DateTimeOffset now,
        out bool added)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(taskFactory);

        lock (_gate)
        {
            PruneLocked(now, profileId);
            if (_entries.TryGetValue(contentId, out var current))
            {
                added = false;
                return current.Task;
            }

            MakeRoomLocked(contentId);
            var task = taskFactory();
            _entries[contentId] = new Entry(profileId, now, task);
            added = true;
            return task;
        }
    }

    public Task<T?> TryGetAsync(
        string contentId,
        string profileId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => TryGetCoreAsync(contentId, profileId, now, removeAfterSuccess: false, cancellationToken);

    public Task<T?> TryTakeAsync(
        string contentId,
        string profileId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => TryGetCoreAsync(contentId, profileId, now, removeAfterSuccess: true, cancellationToken);

    public void Invalidate(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
            return;

        lock (_gate)
            _entries.Remove(contentId);
    }

    public void Remove(string contentId, Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (_gate)
        {
            if (_entries.TryGetValue(contentId, out var current) &&
                ReferenceEquals(current.Task, task))
                _entries.Remove(contentId);
        }
    }

    private async Task<T?> TryGetCoreAsync(
        string contentId,
        string profileId,
        DateTimeOffset now,
        bool removeAfterSuccess,
        CancellationToken cancellationToken)
    {
        Entry? entry;
        lock (_gate)
        {
            PruneLocked(now, profileId);
            _entries.TryGetValue(contentId, out entry);
        }

        if (entry == null)
            return null;

        var result = await entry.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (removeAfterSuccess)
            Remove(contentId, entry.Task);
        return result;
    }

    private void PruneLocked(DateTimeOffset now, string profileId)
    {
        foreach (var key in _entries
                     .Where(pair =>
                         !string.Equals(pair.Value.ProfileId, profileId, StringComparison.Ordinal) ||
                         now - pair.Value.CreatedAt >= _lifetime)
                     .Select(pair => pair.Key)
                     .ToList())
            _entries.Remove(key);
    }

    private void MakeRoomLocked(string incomingContentId)
    {
        if (_entries.ContainsKey(incomingContentId))
            return;

        while (_entries.Count >= _capacity)
        {
            var oldest = _entries.MinBy(pair => pair.Value.CreatedAt);
            if (oldest.Key == null)
                return;
            _entries.Remove(oldest.Key);
        }
    }
}
