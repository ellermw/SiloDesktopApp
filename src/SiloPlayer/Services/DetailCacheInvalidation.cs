using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;

namespace SiloPlayer.Services;

/// <summary>
/// Keeps the shared item-detail prefetch cache coherent with profile-state
/// mutations. Detail view models also invalidate, but they are per page and
/// held weakly by the messenger, so a card action taken while no detail page
/// is alive left a hover-prefetched detail with the old watched, progress or
/// list state for up to its two-minute lifetime. The cache is an app-lifetime
/// singleton, so registering it directly keeps this subscription alive.
/// </summary>
public static class DetailCacheInvalidation
{
    public static void Attach(ItemDetailPrefetchCache cache, IMessenger? messenger = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        messenger ??= WeakReferenceMessenger.Default;
        // A watched or progress change on an episode also changes its season
        // and series next-up state, so drop every entry; the cache is small.
        messenger.Register<ItemDetailPrefetchCache, MediaSurfaceChanged>(cache, static (owner, _) => owner.Clear());
        messenger.Register<ItemDetailPrefetchCache, PlaybackProgressUpdated>(cache,
            static (owner, message) => ClearNowAndAfter(message.Persisted, owner.Clear));
    }

    /// <summary>
    /// Advances a content-scoped revision on every progress/watched mutation so
    /// playback can reject /watch snapshots read before it. /watch only serves
    /// playable leaves (movie, episode, audiobook, ebook), whose user data
    /// depends on the item itself or, for a season/series mutation, its series.
    /// The messenger holds the revision weakly: keep it referenced.
    /// </summary>
    public static UserStateRevision AttachUserStateRevision(IMessenger? messenger = null)
    {
        messenger ??= WeakReferenceMessenger.Default;
        var revision = new UserStateRevision();
        messenger.Register<UserStateRevision, MediaSurfaceChanged>(revision,
            static (owner, message) => owner.Advance(ScopeOf(message)));
        messenger.Register<UserStateRevision, PlaybackProgressUpdated>(revision,
            static (owner, message) => ClearNowAndAfter(message.Persisted, () => owner.Advance(message.ContentId)));
        return revision;
    }

    // A season id ("series:show-S01") without its series id cannot be matched
    // to episodes; treat it as affecting everything rather than nothing.
    private static string?[] ScopeOf(MediaSurfaceChanged message)
        => message.ContentId.StartsWith("series:", StringComparison.Ordinal) && string.IsNullOrEmpty(message.SeriesId)
            ? []
            : [message.ContentId, message.SeriesId];

    // A player-close message arrives before its final write finishes. Clear
    // now, and again once the server has the position, so a read started in
    // between cannot outlive the write. Completion is sampled before the first
    // clear: a write finishing during that clear still gets the second one.
    internal static void ClearNowAndAfter(Task persisted, Action clear)
    {
        var completedBefore = persisted.IsCompleted;
        clear();
        if (!completedBefore)
            _ = persisted.ContinueWith(static (_, state) => ((Action)state!)(), clear,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

/// <summary>
/// Content-scoped counter of profile-state mutations. A value stamped with the
/// revision current when its server read began stays current until a later
/// mutation touches its content or series (or one of unknown scope occurs).
/// </summary>
public sealed class UserStateRevision
{
    private readonly object _gate = new();
    private readonly Dictionary<string, long> _changed = new(StringComparer.Ordinal);
    private long _value;
    private long _everything;

    public long Current { get { lock (_gate) return _value; } }

    /// <summary>Records a mutation of the given ids; none means unknown scope.</summary>
    public void Advance(params string?[] ids)
    {
        lock (_gate)
        {
            _value++;
            var scoped = false;
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                _changed[id] = _value;
                scoped = true;
            }
            if (!scoped) _everything = _value;
        }
    }

    public bool IsCurrent(long stamp, string? contentId, string? seriesId)
    {
        lock (_gate)
            return _everything <= stamp && !ChangedAfter(contentId, stamp) && !ChangedAfter(seriesId, stamp);
    }

    private bool ChangedAfter(string? id, long stamp)
        => !string.IsNullOrEmpty(id) && _changed.TryGetValue(id, out var changed) && changed > stamp;
}
