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
    /// Playback resumes from the watch detail's saved position. A card or page
    /// that resets progress or marks the title watched must not let Play start
    /// from, and then re-save, the previous position. Besides clearing the
    /// cache, the returned revision advances so callers can reject snapshots
    /// they already hold. The messenger holds it weakly: keep it referenced.
    /// </summary>
    public static UserStateRevision Attach<T>(WatchDetailPrefetchCache<T> cache, IMessenger? messenger = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(cache);
        messenger ??= WeakReferenceMessenger.Default;
        var revision = new UserStateRevision(cache.Clear);
        messenger.Register<UserStateRevision, MediaSurfaceChanged>(revision, static (owner, _) => owner.Advance());
        messenger.Register<UserStateRevision, PlaybackProgressUpdated>(revision,
            static (owner, message) => ClearNowAndAfter(message.Persisted, owner.Advance));
        return revision;
    }

    // A player-close message arrives before its final write finishes. Clear
    // now, and again once the server has the position, so a read started in
    // between cannot outlive the write.
    internal static void ClearNowAndAfter(Task persisted, Action clear)
    {
        clear();
        if (!persisted.IsCompleted)
            _ = persisted.ContinueWith(static (_, state) => ((Action)state!)(), clear,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

/// <summary>Counts profile-state mutations that make held watch snapshots stale.</summary>
public sealed class UserStateRevision(Action onAdvance)
{
    private long _value;

    public long Current => Interlocked.Read(ref _value);

    public void Advance()
    {
        Interlocked.Increment(ref _value);
        onAdvance();
    }
}
