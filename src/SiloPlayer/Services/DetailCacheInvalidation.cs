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
        messenger.Register<ItemDetailPrefetchCache, PlaybackProgressUpdated>(cache, static (owner, _) => owner.Clear());
    }
}
