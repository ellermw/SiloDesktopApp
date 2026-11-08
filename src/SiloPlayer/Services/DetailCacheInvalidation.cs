using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Api;
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
    public static void Attach(ItemDetailPrefetchCache cache, IMessenger? messenger = null,
        Func<ApiIdentityContext, bool>? isCurrentIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        messenger ??= WeakReferenceMessenger.Default;
        // A watched or progress change on an episode also changes its season
        // and series next-up state, so drop every entry; the cache is small.
        messenger.Register<ItemDetailPrefetchCache, MediaSurfaceChanged>(cache, static (owner, _) => owner.Clear());
        messenger.Register<ItemDetailPrefetchCache, MediaStateInvalidated>(cache, static (owner, message) =>
        {
            if (!message.RefreshDetails) owner.Clear();
        });
        messenger.Register<ItemDetailPrefetchCache, PlaybackProgressUpdated>(cache,
            (owner, message) => owner.TrackProgressSave(message.Persisted, message.ContentId, message.SeriesId,
                () => message.Identity is not { } identity || isCurrentIdentity?.Invoke(identity) == true));
    }

    /// <summary>
    /// Advances a content-scoped revision on every progress/watched mutation so
    /// playback can reject /watch snapshots read before it. /watch only serves
    /// playable leaves (movie, episode, audiobook, ebook), whose user data
    /// depends on the item itself or, for a season/series mutation, its series.
    /// The messenger holds the revision weakly: keep it referenced.
    /// </summary>
    public static UserStateRevision AttachUserStateRevision(IMessenger? messenger = null,
        Func<ApiIdentityContext, bool>? isCurrentIdentity = null)
    {
        messenger ??= WeakReferenceMessenger.Default;
        var revision = new UserStateRevision();
        messenger.Register<UserStateRevision, MediaStateInvalidated>(revision, static (owner, message) =>
        {
            // The coalesced delivery refreshes mounted details; its raw events
            // already retired /watch reads and must not consume another retry.
            if (message.RefreshDetails) return;
            if (message.CatchUp)
            {
                owner.Advance();
                return;
            }
            // An explicit empty playback scope is a viewer-only mutation, not
            // an unknown-scope playback change. Legacy messages omit the split.
            var playbackIds = message.PlaybackStateContentIds ?? message.ContentIds;
            if (playbackIds.Count > 0)
                owner.Advance(WatchScope(playbackIds, message.SeriesIds));
        });
        messenger.Register<UserStateRevision, MediaSurfaceChanged>(revision,
            static (owner, message) => owner.Advance(ScopeOf(message)));
        messenger.Register<UserStateRevision, PlaybackProgressUpdated>(revision,
            (owner, message) => ClearNowAndAfter(message.Persisted, () =>
            {
                if (message.Identity is not { } identity || isCurrentIdentity?.Invoke(identity) == true)
                    owner.Advance(message.ContentId);
            }));
        return revision;
    }

    // Local messages carry their content and optional series directly,
    // without the viewer/playback split used by remote invalidations.
    private static string?[] ScopeOf(MediaSurfaceChanged message)
        => WatchScope([message.ContentId], string.IsNullOrEmpty(message.SeriesId) ? [] : [message.SeriesId]);

    // A named series relates aggregate detail state, not every episode's
    // playback position. Only an actual series/season container mutation
    // invalidates its descendant /watch snapshots. Season IDs use series:.
    // Without a known parent an episode cannot name its changed season in its
    // /watch scope. Conservatively retire existing reads until a new snapshot.
    private static string?[] WatchScope(IEnumerable<string> contentIds, IEnumerable<string> seriesIds)
    {
        var items = contentIds.ToArray();
        var series = seriesIds.Where(id => !string.IsNullOrEmpty(id)).ToArray();
        var containerChanged = items.Any(id => id.StartsWith("series:", StringComparison.Ordinal));
        if (containerChanged && series.Length == 0) return [];
        return (containerChanged ? items.Concat(series) : items).Cast<string?>().ToArray();
    }

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
