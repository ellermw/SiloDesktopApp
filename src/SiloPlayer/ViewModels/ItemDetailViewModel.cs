using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;

namespace SiloPlayer.ViewModels;

public partial class ItemDetailViewModel : ObservableObject,
    IRecipient<MediaSurfaceChanged>,
    IRecipient<MediaStateInvalidated>,
    IRecipient<NotInterestedChange>,
    IRecipient<PlaybackProgressUpdated>
{
    private readonly CatalogApi _catalogApi;
    private readonly ItemDetailPrefetchCache _detailPrefetchCache;
    private CancellationTokenSource? _loadCts;
    private long _similarLoadGeneration;
    private long _seasonsLoadGeneration;
    private long _episodesLoadGeneration;
    private long _watchedStateGeneration;
    // Write authority is independent of progress/read invalidation revisions.
    private long _watchedWriteGeneration;
    private bool _stateRefreshActive = true;
    private readonly SynchronizationContext? _stateContext = SynchronizationContext.Current;
    private readonly object _remoteStateGate = new();
    private readonly RealtimeMediaStateRefresh _localProgressRefresh;
    private Task? _remoteStateRefresh;
    private MediaStateInvalidated? _pendingRemoteState;
    private MediaStateInvalidated? _activeRemoteState;
    private MediaStateInvalidated? _initialRemoteState;
    private long _initialStateRevision;
    private long _remoteStateGeneration;
    private long _localStateGeneration;
    private Task _pendingLocalProgressSave = Task.CompletedTask;
    private readonly List<(PlaybackProgressUpdated Message, Task Settled)> _initialProgressSaves = [];
    private CancellationTokenSource? _progressSettlementCts;
    private CancellationTokenSource? _remoteStateReadCts;
    private ApiRequestContext? _remoteStateReadContext;
    public long WatchedStateRevision => Volatile.Read(ref _watchedStateGeneration);
    // Companion loads retry coalesced remote changes without retiring local write feedback.
    public long RemoteStateRevision => Volatile.Read(ref _remoteStateGeneration);
    public int? LibraryId { get; private set; }
    public Task LoadInLibraryAsync(string contentId, int? libraryId)
    {
        LibraryId = libraryId;
        return LoadAsync(contentId);
    }
    private Task<MediaItemDetail> ReadDetailAsync(string contentId, CancellationToken ct) => LibraryId.HasValue
        ? _catalogApi.GetItemDetailAsync(contentId, LibraryId, ct) : _detailPrefetchCache.GetAsync(contentId, ct);

    public ItemDetailViewModel(
        CatalogApi catalogApi,
        ItemDetailPrefetchCache detailPrefetchCache)
    {
        _catalogApi = catalogApi;
        _detailPrefetchCache = detailPrefetchCache;
        _localProgressRefresh = new(catalogApi, EnqueueStateRefresh, Receive);
        // F4: subscribe to media-surface changes so if the same item is
        // favorited / watchlisted / watched from another surface (context
        // menu on a poster card, etc.), this VM reflects it immediately.
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this);
        WeakReferenceMessenger.Default.Register<MediaStateInvalidated>(this);
        WeakReferenceMessenger.Default.Register<NotInterestedChange>(this);
        WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this);
    }

    // F4: Helper to publish media-surface changes via the shared messenger.
    // Subscribers (HomeViewModel, FavoritesViewModel, WatchlistViewModel,
    // HistoryViewModel) react by invalidating / adjusting their own state.
    private static void Publish(MediaSurfaceChangeKind kind, string contentId, string? seriesId = null, int? rating = null)
    {
        WeakReferenceMessenger.Default.Send(
            new MediaSurfaceChanged(kind, contentId, seriesId, rating));
    }

    public void Receive(NotInterestedChange message)
    {
        if (!_catalogApi.IsCurrentContext(message.Context)) return;
        _detailPrefetchCache.Invalidate(message.ItemId);
        if (Item == null) return;
        if (Item.ContentId == message.ItemId && message.Marked.HasValue)
        {
            Interlocked.Increment(ref _localStateGeneration);
            (Item.UserState ??= new()).NotInterested = message.Marked.Value;
            OnPropertyChanged(nameof(Item));
        }
        if (message.Marked == true)
            for (var index = SimilarItems.Count - 1; index >= 0; index--)
                if (SimilarItems[index].ContentId == message.ItemId) SimilarItems.RemoveAt(index);
        if (_similarLoaded) _ = LoadSimilarAsync();
    }

    public void Receive(MediaSurfaceChanged message)
    {
        if (message.Context is { } context && !_catalogApi.IsCurrentContext(context)) return;
        _detailPrefetchCache.Invalidate(message.ContentId);
        if (message.Kind == MediaSurfaceChangeKind.PlaybackProgress)
        {
            if (_stateRefreshActive) _localProgressRefresh.Invalidate(StateInvalidation(message));
            return;
        }
        if (message.Kind is MediaSurfaceChangeKind.WatchedMarked or MediaSurfaceChangeKind.WatchedCleared)
        {
            ApplyWatchedChange(message);
            return;
        }
        // Ignore our own publishes (already reflected in state) and anything
        // that doesn't match the currently displayed item.
        if (Item == null || message.ContentId != Item.ContentId) return;

        switch (message.Kind)
        {
            case MediaSurfaceChangeKind.FavoriteAdded:
            case MediaSurfaceChangeKind.FavoriteRemoved:
                Interlocked.Increment(ref _localStateGeneration);
                (Item.UserState ??= new()).IsFavorite = IsFavorite = message.Kind == MediaSurfaceChangeKind.FavoriteAdded;
                break;
            case MediaSurfaceChangeKind.WatchlistAdded:
            case MediaSurfaceChangeKind.WatchlistRemoved:
                Interlocked.Increment(ref _localStateGeneration);
                (Item.UserState ??= new()).InWatchlist = InWatchlist = message.Kind == MediaSurfaceChangeKind.WatchlistAdded;
                break;
            case MediaSurfaceChangeKind.RatingChanged:
                Interlocked.Increment(ref _localStateGeneration);
                Item.UserRating = UserRating = message.Rating;
                break;
        }
    }

    public void Receive(MediaStateInvalidated message) => _ = RefreshMediaStateAsync(message);

    private MediaStateInvalidated StateInvalidation(MediaSurfaceChanged message) =>
        new(message.Context ?? _catalogApi.CaptureContext(), new HashSet<string>([message.ContentId], StringComparer.Ordinal),
            string.IsNullOrEmpty(message.SeriesId) ? new HashSet<string>() : new HashSet<string>([message.SeriesId], StringComparer.Ordinal));

    // Only containers have a complete retained-state refresh. A targeted
    // catch-up also lets an initial-load correction/retry reconcile leaf detail
    // state; reconnect still remounts leaves for their page-owned /watch reads.
    private bool IsRelatedState(MediaStateInvalidated message, MediaItemDetail item) =>
        ((item.Type is "series" or "season") || (message.CatchUp && message.ContentIds.Contains(item.ContentId))) && (
            message.CatchUp || message.ContentIds.Contains(item.ContentId)
            || (!string.IsNullOrEmpty(item.SeriesId) && message.ContentIds.Contains(item.SeriesId))
            || message.SeriesIds.Contains(item.ContentId)
            || (!string.IsNullOrEmpty(item.SeriesId) && message.SeriesIds.Contains(item.SeriesId))
            || Seasons.Any(s => message.ContentIds.Contains(s.ContentId))
            || Episodes.Any(e => message.ContentIds.Contains(e.ContentId)));

    /// <summary>Refresh retained state without replacing the detail or changing playback/room intent.</summary>
    public Task RefreshMediaStateAsync(MediaStateInvalidated message)
    {
        if (_stateContext != null && !ReferenceEquals(SynchronizationContext.Current, _stateContext))
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stateContext.Post(async _ =>
            {
                try { await RefreshMediaStateCoreAsync(message); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }, null);
            return completion.Task;
        }
        return RefreshMediaStateCoreAsync(message);
    }

    private Task RefreshMediaStateCoreAsync(MediaStateInvalidated message)
    {
        lock (_remoteStateGate)
        {
            if (!_stateRefreshActive || !_catalogApi.IsCurrentContext(message.Context)) return Task.CompletedTask;
            if (Item is not { } item)
            {
                if (_loadCts is { IsCancellationRequested: false })
                {
                    _initialRemoteState = MergeState(_initialRemoteState, message);
                    ++_initialStateRevision;
                }
                return Task.CompletedTask;
            }
            if (!IsRelatedState(message, item)) return Task.CompletedTask;
            if (_remoteStateReadContext is { } reading && reading != message.Context) _remoteStateReadCts?.Cancel();
            if (!message.RefreshDetails) return Task.CompletedTask;
            // A later event may recover an exhausted sweep, but must reconcile
            // its full catch-up state before dismissing the inline retry UI.
            if (RemoteStateErrorMessage != null) message = message with { CatchUp = true };
            Interlocked.Increment(ref _remoteStateGeneration);
            // A still-owned snapshot finishes even when another sweep is due.
            // Retain its scope in the trailing work until all of its reads settle.
            _pendingRemoteState = MergeState(_pendingRemoteState, MergeState(_activeRemoteState, message));
            return _remoteStateRefresh ??= RefreshRemoteStateLoopAsync();
        }
    }

    [RelayCommand]
    private Task RetryRemoteStateAsync() => RefreshMediaStateAsync(new(
        _catalogApi.CaptureContext(), new HashSet<string>(Item == null ? [] : [Item.ContentId]),
        new HashSet<string>(), CatchUp: true));

    private void EnqueueStateRefresh(Action action)
    {
        if (_stateContext == null || ReferenceEquals(SynchronizationContext.Current, _stateContext)) action();
        else _stateContext.Post(_ => action(), null);
    }

    private static MediaStateInvalidated MergeState(MediaStateInvalidated? earlier, MediaStateInvalidated later) =>
        earlier == null || earlier.Context != later.Context ? later : later with
        {
            ContentIds = earlier.ContentIds.Concat(later.ContentIds).ToHashSet(StringComparer.Ordinal),
            SeriesIds = earlier.SeriesIds.Concat(later.SeriesIds).ToHashSet(StringComparer.Ordinal),
            CatchUp = earlier.CatchUp || later.CatchUp,
            RefreshDetails = earlier.RefreshDetails || later.RefreshDetails
        };

    private async Task RefreshRemoteStateLoopAsync()
    {
        // Yield so the owning task is assigned before a synchronous fixture/cache response completes.
        await Task.Yield();
        while (true)
        {
            MediaStateInvalidated message;
            lock (_remoteStateGate)
            {
                if (_pendingRemoteState is not { } pending)
                {
                    // Retire ownership atomically with the empty-queue check.
                    // A concurrent sender now starts a loop instead of losing a wakeup.
                    _activeRemoteState = null;
                    _remoteStateRefresh = null;
                    return;
                }
                message = pending;
                _pendingRemoteState = null;
                _activeRemoteState = message;
            }
            var item = Item;
            var libraryId = LibraryId;
            var selectedSeason = SelectedSeasonNumber;
            var episodesSeasonContentId = EpisodesSeasonContentId;
            var revision = Volatile.Read(ref _localStateGeneration);
            using var readOwner = new CancellationTokenSource();
            var ct = readOwner.Token;
            bool Current() => _stateRefreshActive && ReferenceEquals(Item, item) && LibraryId == libraryId
                && !ct.IsCancellationRequested && revision == Volatile.Read(ref _localStateGeneration)
                && _catalogApi.IsCurrentContext(message.Context);
            var itemChanged = false;
            var seasonsChanged = false;
            var episodesChanged = false;
            Exception? refreshFailure = null;
            var refreshCompleted = false;
            try
            {
                lock (_remoteStateGate)
                {
                    // Even an early local-revision rejection must retain the
                    // active scope through the finally block below.
                    if (item == null || !Current() || !IsRelatedState(message, item)) continue;
                    _remoteStateReadCts = readOwner;
                    _remoteStateReadContext = message.Context;
                }
                // An echoed/other-device event must not retire the accepted
                // feedback or rollback authority of an in-flight local mark.
                do
                {
                    if (ToggleWatchedCommand is { IsRunning: true, ExecutionTask: { } mutation })
                        await mutation.WaitAsync(ct);
                    foreach (var command in new IAsyncRelayCommand[] { ToggleFavoriteCommand, ToggleWatchlistCommand, SetRatingCommand })
                        if (command is { IsRunning: true, ExecutionTask: { } viewerMutation })
                            await viewerMutation.WaitAsync(ct);
                    await _detailPrefetchCache.WaitForProgressSettlementAsync(ct, item.ContentId, item.SeriesId);
                    await WaitForLocalProgressSettlementAsync(ct);
                    // A mutation can start while another write/save is pending.
                } while (ToggleWatchedCommand.IsRunning || ToggleFavoriteCommand.IsRunning
                    || ToggleWatchlistCommand.IsRunning || SetRatingCommand.IsRunning);
                if (!_stateRefreshActive || ct.IsCancellationRequested || !ReferenceEquals(Item, item)
                    || LibraryId != libraryId || !_catalogApi.IsCurrentContext(message.Context)) continue;
                revision = Volatile.Read(ref _localStateGeneration);
                _detailPrefetchCache.Invalidate(item.ContentId);
                // Mounted reads own their transport and snapshot. Raw ticks
                // still clear speculative cache entries, without retrying or
                // discarding this request each time another device advances.
                var refreshed = await ReadRemoteStateAsync(
                    () => _catalogApi.GetItemDetailAsync(item.ContentId, libraryId, ct), message.CatchUp, Current, ct);
                if (!Current()) continue;
                // Keep aggregate feedback and later local watched deltas on the
                // same server total. Missing legacy counts leave known totals intact.
                if (refreshed.EpisodeCount.HasValue && item.EpisodeCount != refreshed.EpisodeCount)
                { item.EpisodeCount = refreshed.EpisodeCount; itemChanged = true; }
                if (refreshed.SeasonCount.HasValue && item.SeasonCount != refreshed.SeasonCount)
                { item.SeasonCount = refreshed.SeasonCount; itemChanged = true; }
                if (refreshed.UserData is { } userData && !SameUserData(item.UserData, userData))
                {
                    item.UserData = userData;
                    IsWatched = userData.Played;
                    itemChanged = true;
                }
                if (refreshed.HasAuthoritativePlayTarget && (!item.HasAuthoritativePlayTarget
                    || item.PlayContentId != refreshed.PlayContentId || item.PlaySeasonNumber != refreshed.PlaySeasonNumber))
                {
                    item.PlayContentId = refreshed.PlayContentId;
                    item.PlaySeasonNumber = refreshed.PlaySeasonNumber;
                    itemChanged = true;
                }
                // Direct item invalidations also include cross-device viewer-state
                // changes. Reconcile from REST, never from an event's scalar payload.
                if (message.CatchUp || message.ContentIds.Contains(item.ContentId))
                {
                    if (refreshed.UserState != null && (item.UserState == null
                        || (item.UserState.Played, item.UserState.IsFavorite, item.UserState.InWatchlist, item.UserState.NotInterested)
                            != (refreshed.UserState.Played, refreshed.UserState.IsFavorite, refreshed.UserState.InWatchlist, refreshed.UserState.NotInterested)))
                    {
                        item.UserState = refreshed.UserState;
                        IsFavorite = item.UserState.IsFavorite;
                        InWatchlist = item.UserState.InWatchlist;
                        itemChanged = true;
                    }
                    if (refreshed.HasAuthoritativeUserRating && (!item.HasAuthoritativeUserRating || item.UserRating != refreshed.UserRating))
                    {
                        item.UserRating = refreshed.UserRating;
                        UserRating = item.UserRating;
                        itemChanged = true;
                    }
                }
                // Notify the retained snapshot once all companion reads settle,
                // including successful detail changes when a companion fails.
                if (!Current()) continue;
                // LoadSeasonsAsync stays active through auto-selected episodes;
                // its already-committed seasons still need remote count refreshes.
                if (item.Type == "series" && (_seasonsLoaded || !IsSeasonsLoading && Seasons.Count > 0))
                {
                    try
                    {
                        var seasons = await ReadRemoteStateAsync(
                            () => _catalogApi.GetSeasonsAsync(item.ContentId, libraryId, ct), message.CatchUp, Current, ct);
                        if (!Current()) continue;
                        var retained = seasons.Seasons.Select(fresh =>
                        {
                            var loaded = Seasons.FirstOrDefault(s => s.ContentId == fresh.ContentId);
                            if (loaded == null) return fresh;
                            if (loaded.EpisodeCount != fresh.EpisodeCount)
                            { loaded.EpisodeCount = fresh.EpisodeCount; seasonsChanged = true; }
                            if (fresh.UserData != null && !SameUserData(loaded.UserData, fresh.UserData))
                            { loaded.UserData = fresh.UserData; seasonsChanged = true; }
                            return loaded;
                        }).ToArray();
                        if (!Seasons.SequenceEqual(retained))
                        {
                            Seasons.Clear();
                            foreach (var season in retained) Seasons.Add(season);
                            seasonsChanged = true;
                        }
                        _seasonsLoaded = true;
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        refreshFailure = ex;
                        LocalLog.AppendException("state_trace.txt", ex, "realtime_seasons_state_refresh");
                    }
                }
                if (!Current()) continue;
                // A named series relates its aggregate seasons, not its other episode leaves.
                // The episode companion refreshes user data on retained rows only;
                // navigation/season selection still owns episode list membership.
                var refreshEpisodes = Episodes.Count > 0 && (message.CatchUp || message.ContentIds.Contains(item.ContentId)
                    || (!string.IsNullOrEmpty(item.SeriesId) && message.ContentIds.Contains(item.SeriesId))
                    || Seasons.Any(s => message.ContentIds.Contains(s.ContentId))
                    || Episodes.Any(e => message.ContentIds.Contains(e.ContentId)));
                if (refreshEpisodes)
                {
                    try
                    {
                        EpisodesResponse? episodes = null;
                        if (item.Type == "season")
                            episodes = await ReadRemoteStateAsync(
                                () => _catalogApi.GetItemEpisodesAsync(item.ContentId, libraryId, ct), message.CatchUp, Current, ct);
                        else if (item.Type == "series" && episodesSeasonContentId != null)
                            episodes = await ReadRemoteStateAsync(
                                () => _catalogApi.GetItemEpisodesAsync(episodesSeasonContentId, libraryId, ct), message.CatchUp, Current, ct);
                        else if (item.Type == "series" && selectedSeason >= 0)
                            episodes = await ReadRemoteStateAsync(
                                () => _catalogApi.GetEpisodesAsync(item.ContentId, selectedSeason, libraryId, ct), message.CatchUp, Current, ct);
                        if (!Current()) continue;
                        if (episodes != null && SelectedSeasonNumber == selectedSeason && EpisodesSeasonContentId == episodesSeasonContentId)
                            foreach (var loaded in Episodes)
                                if (episodes.Episodes.FirstOrDefault(e => e.ContentId == loaded.ContentId) is { UserData: { } fresh }
                                    && !SameUserData(loaded.UserData, fresh))
                                { loaded.UserData = fresh; episodesChanged = true; }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        refreshFailure ??= ex;
                        LocalLog.AppendException("state_trace.txt", ex, "realtime_episodes_state_refresh");
                    }
                }
                if (message.CatchUp && _similarLoaded && Current()) await LoadSimilarAsync();
                refreshCompleted = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                refreshFailure = ex;
                LocalLog.AppendException("state_trace.txt", ex, "realtime_detail_state_refresh");
            }
            finally
            {
                // Values committed before a later local mutation still need
                // painting; navigation/profile retirement must never paint them.
                if (_stateRefreshActive && !ct.IsCancellationRequested && ReferenceEquals(Item, item)
                    && LibraryId == libraryId && _catalogApi.IsCurrentContext(message.Context))
                {
                    if (itemChanged) OnPropertyChanged(nameof(Item));
                    if (seasonsChanged) OnPropertyChanged(nameof(Seasons));
                    if (episodesChanged) OnPropertyChanged(nameof(Episodes));
                    // A partial sweep is not a completed catch-up. Offer an
                    // inline retry without covering mounted content or changing
                    // the initial-load failure state used by reconnect handling.
                    if (Current() && (refreshFailure != null || refreshCompleted))
                        RemoteStateErrorMessage = refreshFailure == null ? null
                            : "Silo couldn't refresh this item. Displayed information may be out of date.";
                }
                lock (_remoteStateGate)
                {
                    if (ReferenceEquals(_remoteStateReadCts, readOwner))
                    {
                        _remoteStateReadCts = null;
                        _remoteStateReadContext = null;
                    }
                    // A local toggle can retire this snapshot before its write
                    // publishes feedback. Keep the scope alive so the next pass
                    // waits for that write instead of losing the remote change.
                    if (_stateRefreshActive && !ct.IsCancellationRequested
                        && ReferenceEquals(Item, item) && LibraryId == libraryId
                        && _catalogApi.IsCurrentContext(message.Context)
                        && revision != Volatile.Read(ref _localStateGeneration))
                        _pendingRemoteState = MergeState(_pendingRemoteState, message);
                    if (ReferenceEquals(_activeRemoteState, message)) _activeRemoteState = null;
                }
            }
        }
    }

    private static async Task<T> ReadRemoteStateAsync<T>(
        Func<Task<T>> read, bool catchUp, Func<bool> current, CancellationToken ct)
    {
        // Keep the active merged scope and its read owner throughout recovery.
        // Reconnect can precede REST readiness; normal events get one trailing
        // retry, while catch-up gets three, with bounded exponential backoff.
        var attempts = catchUp ? 4 : 2;
        for (var attempt = 1; ; attempt++)
        {
            try { return await read(); }
            catch (Exception) when (!ct.IsCancellationRequested && current() && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * (1 << (attempt - 1))), ct);
                // Profile departure or newer local intent must not authorize
                // another read under a different owner during the delay.
                if (!current()) throw;
            }
        }
    }

    private static bool SameUserData(ItemDetailUserData? left, ItemDetailUserData right) => left != null
        && (left.Played, left.IsInProgress, left.PositionSeconds, left.DurationSeconds, left.WatchedCount,
            left.UnplayedCount, left.InProgressCount, left.LastFileId, left.LastResolution, left.LastHdr, left.LastCodecVideo, left.LastEditionKey)
        == (right.Played, right.IsInProgress, right.PositionSeconds, right.DurationSeconds, right.WatchedCount,
            right.UnplayedCount, right.InProgressCount, right.LastFileId, right.LastResolution, right.LastHdr, right.LastCodecVideo, right.LastEditionKey);

    private static bool SameUserData(SeasonUserData? left, SeasonUserData right) => left != null
        && (left.Played, left.WatchedCount, left.UnplayedCount, left.InProgressCount)
        == (right.Played, right.WatchedCount, right.UnplayedCount, right.InProgressCount);

    private static bool SameUserData(EpisodeUserData? left, EpisodeUserData right) => left != null
        && (left.Played, left.IsInProgress, left.PositionSeconds, left.DurationSeconds)
        == (right.Played, right.IsInProgress, right.PositionSeconds, right.DurationSeconds);

    private async Task WaitForLocalProgressSettlementAsync(CancellationToken ct)
    {
        while (true)
        {
            Task pending;
            lock (_remoteStateGate) pending = _pendingLocalProgressSave;
            await pending.WaitAsync(ct);
            // A newer final save may have joined while this barrier waited.
            lock (_remoteStateGate)
                if (ReferenceEquals(pending, _pendingLocalProgressSave)) return;
        }
    }

    private static bool ProgressAffectsDetail(PlaybackProgressUpdated message, string contentId, string? seriesId) =>
        message.ContentId == contentId || message.SeriesId == contentId
        || (!string.IsNullOrEmpty(seriesId) && (message.ContentId == seriesId || message.SeriesId == seriesId));

    private async Task<bool> WaitForInitialProgressSettlementAsync(
        string contentId, string? seriesId, CancellationToken barrierToken, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                barrierToken.ThrowIfCancellationRequested();
                Task[] pending;
                lock (_remoteStateGate)
                    pending = _initialProgressSaves.Where(save => ProgressAffectsDetail(save.Message, contentId, seriesId))
                        .Select(save => save.Settled).ToArray();
                await _detailPrefetchCache.WaitForProgressSettlementAsync(barrierToken, contentId, seriesId);
                await Task.WhenAll(pending).WaitAsync(barrierToken);
                lock (_remoteStateGate)
                    if (pending.Length == _initialProgressSaves.Count(save => ProgressAffectsDetail(save.Message, contentId, seriesId)))
                        return true;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Navigation owns ct; the separate five-second budget only bounds
            // the loading skeleton, never the save itself or the trailing read.
            return false;
        }
    }

    private async Task RefreshAfterInitialProgressSettlementAsync(
        MediaItemDetail item, int? libraryId, ApiRequestContext context, CancellationToken ct)
    {
        try
        {
            await _detailPrefetchCache.WaitForProgressSettlementAsync(ct, item.ContentId, item.SeriesId);
            await WaitForLocalProgressSettlementAsync(ct);
            EnqueueStateRefresh(() =>
            {
                if (!ct.IsCancellationRequested && _stateRefreshActive && ReferenceEquals(Item, item)
                    && LibraryId == libraryId && _catalogApi.IsCurrentContext(context))
                    _ = RefreshMediaStateAsync(new(context, new HashSet<string>([item.ContentId]),
                        new HashSet<string>(), CatchUp: true));
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static async Task ProgressSaveSettledAsync(Task persisted)
    {
        // The write owner reports failure. A read waits for settlement rather
        // than preserving a failed task as a permanent refresh prohibition.
        try { await persisted.ConfigureAwait(false); }
        catch (Exception) { }
    }

    private void ApplyWatchedChange(MediaSurfaceChanged message)
    {
        var item = Item;
        if (item == null) return;
        var played = message.Kind == MediaSurfaceChangeKind.WatchedMarked;
        var own = item.ContentId == message.ContentId;
        var ancestor = item.SeriesId == message.ContentId;
        var cascade = (own && item.Type is "series" or "season") || ancestor;
        var season = Seasons.FirstOrDefault(s => s.ContentId == message.ContentId);
        var changedEpisodes = Episodes.Where(e => cascade
            || e.ContentId == message.ContentId || season?.SeasonNumber == e.SeasonNumber).ToArray();
        if (!own && !ancestor && season == null && changedEpisodes.Length == 0)
        {
            if ((item.Type is "series" or "season")
                && (item.ContentId == message.SeriesId || (item.SeriesId == message.SeriesId && message.SeriesId != null)))
                _ = RefreshMediaStateAsync(StateInvalidation(message));
            return;
        }

        lock (_remoteStateGate)
        {
            Interlocked.Increment(ref _watchedStateGeneration);
            Interlocked.Increment(ref _watchedWriteGeneration);
            Interlocked.Increment(ref _localStateGeneration);
            if (_activeRemoteState is { } active) _pendingRemoteState = MergeState(_pendingRemoteState, active);
        }
        _detailPrefetchCache.Invalidate(item.ContentId);
        if (!string.IsNullOrEmpty(item.SeriesId)) _detailPrefetchCache.Invalidate(item.SeriesId);
        var watchedDelta = 0;
        var progressDelta = 0;
        foreach (var episode in changedEpisodes)
        {
            episode.UserData ??= new();
            var data = episode.UserData;
            var delta = (played ? 1 : 0) - (data.Played ? 1 : 0);
            var progress = data.IsInProgress ? -1 : 0;
            watchedDelta += delta;
            progressDelta += progress;
            data.Played = played;
            data.IsInProgress = false;
            data.PositionSeconds = 0;
            _detailPrefetchCache.Invalidate(episode.ContentId);
            if (!own && !ancestor && season == null)
            {
                var parentSeason = Seasons.FirstOrDefault(s => s.SeasonNumber == episode.SeasonNumber);
                if (parentSeason != null)
                {
                    parentSeason.UserData ??= new() { UnplayedCount = parentSeason.EpisodeCount };
                    var parent = parentSeason.UserData;
                    parent.WatchedCount = Math.Clamp(parent.WatchedCount + delta, 0, parentSeason.EpisodeCount);
                    parent.UnplayedCount = Math.Max(0, parentSeason.EpisodeCount - parent.WatchedCount);
                    parent.InProgressCount = Math.Max(0, parent.InProgressCount + progress);
                    parent.Played = parentSeason.EpisodeCount > 0 && parent.UnplayedCount == 0;
                    _detailPrefetchCache.Invalidate(parentSeason.ContentId);
                }
            }
        }
        foreach (var affected in Seasons.Where(s => cascade || s == season))
        {
            var old = affected.UserData;
            if (!own && !ancestor)
            {
                watchedDelta = (played ? affected.EpisodeCount : 0) - (old?.WatchedCount ?? 0);
                progressDelta = -(old?.InProgressCount ?? 0);
            }
            affected.UserData = new() { Played = played, WatchedCount = played ? affected.EpisodeCount : 0,
                UnplayedCount = played ? 0 : affected.EpisodeCount };
            _detailPrefetchCache.Invalidate(affected.ContentId);
        }
        item.UserData ??= new();
        if (own || ancestor)
        {
            item.UserData.Played = played;
            item.UserData.IsInProgress = false;
            item.UserData.PositionSeconds = 0;
            if (item.Type is "series" or "season")
            {
                var total = item.EpisodeCount ?? (item.Type == "series" ? Seasons.Sum(s => s.EpisodeCount) : Episodes.Count);
                item.UserData.WatchedCount = played ? total : 0;
                item.UserData.UnplayedCount = played ? 0 : total;
                item.UserData.InProgressCount = 0;
            }
        }
        else if (item.Type is "series" or "season")
        {
            var total = item.EpisodeCount ?? (item.Type == "series" ? Seasons.Sum(s => s.EpisodeCount) : Episodes.Count);
            item.UserData.WatchedCount = Math.Clamp(item.UserData.WatchedCount + watchedDelta, 0, total);
            item.UserData.UnplayedCount = Math.Max(0, total - item.UserData.WatchedCount);
            item.UserData.InProgressCount = Math.Max(0, item.UserData.InProgressCount + progressDelta);
            item.UserData.Played = total > 0 && item.UserData.UnplayedCount == 0;
        }
        IsWatched = item.UserData.Played;
        OnPropertyChanged(nameof(Item));
        if (changedEpisodes.Length > 0) OnPropertyChanged(nameof(Episodes));
        if (Seasons.Count > 0) OnPropertyChanged(nameof(Seasons));
    }

    public void Receive(PlaybackProgressUpdated message)
    {
        _detailPrefetchCache.Invalidate(message.ContentId);
        // Retain scope until the initial snapshot supplies its series ID. An
        // unrelated save must not hold this page's loading state or retry.
        if (Item == null && _loadCts is { IsCancellationRequested: false })
        {
            lock (_remoteStateGate) _initialProgressSaves.Add((message, ProgressSaveSettledAsync(message.Persisted)));
        }
        if (Item is { } item && (item.ContentId == message.ContentId
            || item.PlayContentId == message.ContentId
            || ((item.Type is "series" or "season") && !string.IsNullOrEmpty(message.SeriesId)
                && (item.ContentId == message.SeriesId || item.SeriesId == message.SeriesId))
            || Episodes.Any(e => e.ContentId == message.ContentId)))
        {
            var settled = ProgressSaveSettledAsync(message.Persisted);
            lock (_remoteStateGate)
            {
                Interlocked.Increment(ref _watchedStateGeneration);
                Interlocked.Increment(ref _localStateGeneration);
                _pendingLocalProgressSave = _pendingLocalProgressSave.IsCompleted
                    ? settled : Task.WhenAll(_pendingLocalProgressSave, settled);
                if (_activeRemoteState is { } active) _pendingRemoteState = MergeState(_pendingRemoteState, active);
            }
        }
        var episodeStateChanged = false;
        foreach (var episode in Episodes.Where(episode => episode.ContentId == message.ContentId))
        {
            episode.UserData ??= new EpisodeUserData();
            var data = episode.UserData;
            var previous = (data.Played, data.IsInProgress, data.PositionSeconds, data.DurationSeconds);
            data.PositionSeconds = message.PositionSeconds;
            if (message.DurationSeconds > 0) data.DurationSeconds = message.DurationSeconds;
            data.IsInProgress = message.PositionSeconds > 0 && !message.Completed;
            if (message.Completed) data.Played = true;
            episodeStateChanged |= previous != (data.Played, data.IsInProgress, data.PositionSeconds, data.DurationSeconds);
        }
        if (episodeStateChanged) OnPropertyChanged(nameof(Episodes));
        // Progress update for the item we're currently displaying → refresh
        // the resume position so the "Resume at X:XX" label reflects the
        // latest watch state. We don't refetch the whole detail — just patch
        // the user_data on Item in place.
        if (Item == null || message.ContentId != Item.ContentId) return;
        Item.UserData ??= new ItemDetailUserData();
        var detailWasUnwatched = EpisodeSpoilerPolicy.IsUnwatched(Item.UserData.Played, Item.UserData.IsInProgress, Item.UserData.PositionSeconds);
        Item.UserData.PositionSeconds = message.PositionSeconds;
        if (message.DurationSeconds > 0) Item.UserData.DurationSeconds = message.DurationSeconds;
        Item.UserData.IsInProgress = message.PositionSeconds > 0 && !message.Completed;
        if (message.Completed)
        {
            Item.UserData.Played = true;
            IsWatched = true;
        }
        if (detailWasUnwatched != EpisodeSpoilerPolicy.IsUnwatched(Item.UserData.Played, Item.UserData.IsInProgress, Item.UserData.PositionSeconds))
            OnPropertyChanged(nameof(Item));
    }

    /// <summary>
    /// Read the displayed item's state after final progress saves, including
    /// episodes retired by autoplay and aggregate season/series completion.
    /// Keep the existing page mounted and reject results after navigation.
    /// </summary>
    public async Task RefreshWatchedStateAsync(
        Task pendingProgressSave, CancellationToken cancellationToken)
    {
        var item = Item;
        if (item?.Type is not ("movie" or "episode" or "series" or "season")) return;
        var generation = Volatile.Read(ref _watchedWriteGeneration);
        var context = _catalogApi.CaptureContext();
        try
        {
            await pendingProgressSave.WaitAsync(cancellationToken);
            await WaitForLocalProgressSettlementAsync(cancellationToken);
            if (!ReferenceEquals(Item, item) || !_catalogApi.IsCurrentContext(context)
                || ToggleWatchedCommand.IsRunning || generation != Volatile.Read(ref _watchedWriteGeneration)) return;
            var localRevision = Volatile.Read(ref _localStateGeneration);
            _detailPrefetchCache.Invalidate(item.ContentId);
            var refreshed = await ReadDetailAsync(item.ContentId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Item, item) || !_catalogApi.IsCurrentContext(context)
                || generation != Volatile.Read(ref _watchedWriteGeneration) || localRevision != Volatile.Read(ref _localStateGeneration)) return;
            item.UserData = refreshed.UserData;
            IsWatched = item.UserData?.Played ?? false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Watched-state refresh failed: {ex.Message}");
        }
    }

    [ObservableProperty]
    private MediaItemDetail? _item;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;
    [ObservableProperty]
    private DetailFailureKind _failureKind;

    [ObservableProperty]
    private string? _remoteStateErrorMessage;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _inWatchlist;

    [ObservableProperty]
    private bool _isWatched;

    partial void OnIsWatchedChanged(bool value) => Interlocked.Increment(ref _watchedStateGeneration);

    [ObservableProperty]
    private int? _userRating;

    // Series support
    [ObservableProperty]
    private bool _isSeries;

    [ObservableProperty]
    private bool _isSeasonsLoading;

    [ObservableProperty]
    private bool _isEpisodesLoading;

    [ObservableProperty]
    private bool _seasonsLoadFailed;

    [ObservableProperty]
    private bool _episodesLoadFailed;

    [ObservableProperty]
    private bool _similarLoadFailed;

    [ObservableProperty]
    private int _selectedSeasonNumber = -1;

    // Non-null when the visible rows came from the canonical season item route.
    public string? EpisodesSeasonContentId { get; set; }

    public ObservableCollection<Season> Seasons { get; } = [];
    public ObservableCollection<Episode> Episodes { get; } = [];
    public ObservableCollection<MediaItem> SimilarItems { get; } = [];
    private bool _similarLoaded;
    private bool _seasonsLoaded;

    public string RuntimeDisplay => Item?.Runtime > 0
        ? Item.Runtime >= 60
            ? Item.Runtime % 60 == 0
                ? $"{Item.Runtime / 60}h"
                : $"{Item.Runtime / 60}h {Item.Runtime % 60}m"
            : $"{Item.Runtime}m"
        : "";

    public string GenresDisplay =>
        Item?.Genres.Count > 0 ? string.Join(", ", Item.Genres) : "";

    public string RatingDisplay =>
        Item?.RatingTmdb != null ? $"{Item.RatingTmdb:F1}" : "";

    public void CancelPendingLoads()
    {
        _localProgressRefresh.Reset();
        lock (_remoteStateGate)
        {
            _stateRefreshActive = false;
            _pendingRemoteState = null;
            _activeRemoteState = null;
            _initialRemoteState = null;
            _pendingLocalProgressSave = Task.CompletedTask;
            _initialProgressSaves.Clear();
            _progressSettlementCts?.Cancel();
            _progressSettlementCts?.Dispose();
            _progressSettlementCts = null;
            _remoteStateReadCts?.Cancel();
        }
        Interlocked.Increment(ref _watchedStateGeneration);
        Interlocked.Increment(ref _watchedWriteGeneration);
        Interlocked.Increment(ref _localStateGeneration);
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync(string contentId)
    {
        CancelPendingLoads();
        _stateRefreshActive = true;
        var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        var ct = loadCts.Token;
        var loadContext = _catalogApi.CaptureContext();
        var initialRevision = _initialStateRevision;
        var localRevision = Volatile.Read(ref _localStateGeneration);
        var progressRevision = _detailPrefetchCache.GetProgressSaveRevision(contentId);
        var initialProgressRevision = 0;
        var libraryId = LibraryId;
        bool Current() => ReferenceEquals(_loadCts, loadCts) && !ct.IsCancellationRequested
            && LibraryId == libraryId && _catalogApi.IsCurrentContext(loadContext);

        IsLoading = true;
        ErrorMessage = null;
        FailureKind = DetailFailureKind.None;
        RemoteStateErrorMessage = null;
        Item = null;
        Interlocked.Increment(ref _similarLoadGeneration);
        Interlocked.Increment(ref _seasonsLoadGeneration);
        Interlocked.Increment(ref _episodesLoadGeneration);
        IsSeries = false;
        IsSeasonsLoading = false;
        IsEpisodesLoading = false;
        Seasons.Clear();
        Episodes.Clear();
        SimilarItems.Clear();
        _similarLoaded = false;
        _seasonsLoaded = false;
        SeasonsLoadFailed = false;
        EpisodesLoadFailed = false;
        SimilarLoadFailed = false;
        SelectedSeasonNumber = -1;
        EpisodesSeasonContentId = null;
        UserRating = null;

        try
        {
            using var progressWait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            progressWait.CancelAfter(TimeSpan.FromSeconds(5));
            var progressSettled = await WaitForInitialProgressSettlementAsync(contentId, null, progressWait.Token, ct);
            if (!Current()) return;
            MediaItemDetail item;
            try { item = await ReadDetailAsync(contentId, ct); }
            catch (UserStateChangedException) when (!ct.IsCancellationRequested)
            {
                item = await _catalogApi.GetItemDetailAsync(contentId, LibraryId, ct);
            }
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, loadCts) || !_catalogApi.IsCurrentContext(loadContext)) return;
            if (initialRevision != _initialStateRevision && _initialRemoteState is { } invalidation && IsRelatedState(invalidation, item))
            {
                initialRevision = _initialStateRevision;
                try
                {
                    item = await ReadRemoteStateAsync(
                        () => _catalogApi.GetItemDetailAsync(contentId, libraryId, ct), invalidation.CatchUp, Current, ct);
                }
                catch (Exception ex) when (Current())
                {
                    // A corrective read must not turn a successful initial
                    // snapshot into a full-page failure. Keep it and offer retry.
                    LocalLog.AppendException("state_trace.txt", ex, "initial_detail_state_refresh");
                    RemoteStateErrorMessage = "Silo couldn't refresh this item. Displayed information may be out of date.";
                }
                ct.ThrowIfCancellationRequested();
                if (!Current()) return;
            }
            while (progressSettled)
            {
                int nextInitialProgressRevision;
                lock (_remoteStateGate)
                    nextInitialProgressRevision = _initialProgressSaves.Count(save => ProgressAffectsDetail(save.Message, contentId, item.SeriesId));
                var nextProgressRevision = _detailPrefetchCache.GetProgressSaveRevision(contentId, item.SeriesId);
                if (localRevision == Volatile.Read(ref _localStateGeneration)
                    && progressRevision == nextProgressRevision && initialProgressRevision == nextInitialProgressRevision) break;
                progressSettled = await WaitForInitialProgressSettlementAsync(contentId, item.SeriesId, progressWait.Token, ct);
                if (!Current()) return;
                if (!progressSettled) break;
                localRevision = Volatile.Read(ref _localStateGeneration);
                progressRevision = _detailPrefetchCache.GetProgressSaveRevision(contentId, item.SeriesId);
                lock (_remoteStateGate)
                    initialProgressRevision = _initialProgressSaves.Count(save => ProgressAffectsDetail(save.Message, contentId, item.SeriesId));
                // Own a fresh read after settlement, including for leaves and
                // library-scoped pages. A new save during it requires another.
                try
                {
                    item = await ReadRemoteStateAsync(
                        () => _catalogApi.GetItemDetailAsync(contentId, libraryId, ct), false, Current, ct);
                }
                catch (Exception ex) when (Current())
                {
                    LocalLog.AppendException("state_trace.txt", ex, "initial_final_progress_state_refresh");
                    RemoteStateErrorMessage = "Silo couldn't refresh this item. Displayed information may be out of date.";
                    break;
                }
                ct.ThrowIfCancellationRequested();
                if (!Current()) return;
            }
            lock (_remoteStateGate)
            {
                _pendingLocalProgressSave = Task.WhenAll(_initialProgressSaves
                    .Where(save => ProgressAffectsDetail(save.Message, contentId, item.SeriesId)).Select(save => save.Settled));
                _initialProgressSaves.Clear();
            }
            Item = item;

            // Server commit 4172a16 inlines favorite/watchlist/rating on the item
            // detail response via `user_state` + `user_rating`. Prefer those over
            // the legacy three-way parallel fetch. Fall back to dedicated endpoints
            // only if the server didn't populate `user_state` (older servers).
            if (Item?.UserState != null)
            {
                IsFavorite = Item.UserState.IsFavorite;
                InWatchlist = Item.UserState.InWatchlist;
            }
            else
            {
                IsFavorite = false;
                InWatchlist = false;
                await CheckFavoriteWatchlistAsync(contentId, loadCts);
            }

            UserRating = Item?.UserRating;

            IsWatched = Item?.UserData?.Played ?? false;
            IsSeries = Item?.Type == "series";
            OnPropertyChanged(nameof(RuntimeDisplay));
            OnPropertyChanged(nameof(GenresDisplay));
            OnPropertyChanged(nameof(RatingDisplay));
            var trailing = _initialRemoteState;
            _initialRemoteState = null;
            // Let navigation paint the committed detail without waiting for
            // the shared, ownership-guarded realtime loop to drain.
            if (initialRevision != _initialStateRevision && trailing != null)
                _ = RefreshMediaStateAsync(trailing with { RefreshDetails = true });
            if (!progressSettled)
            {
                _progressSettlementCts = new CancellationTokenSource();
                _ = RefreshAfterInitialProgressSettlementAsync(item, libraryId, loadContext, _progressSettlementCts.Token);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_loadCts, loadCts) || ct.IsCancellationRequested) return;
            System.Diagnostics.Debug.WriteLine($"Item detail load failed for {contentId}: {ex}");
            FailureKind = DetailFailurePolicy.Classify(ex);
            if (FailureKind == DetailFailureKind.NotFound) _detailPrefetchCache.Invalidate(contentId);
            ErrorMessage = DetailFailurePolicy.Message(FailureKind);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                IsLoading = false;
                _loadCts.Dispose();
                _loadCts = null;
            }
        }
    }

    /// <summary>
    /// Invalidates prefetched detail after a server-side metadata or state
    /// mutation, then performs a definitive reload.
    /// </summary>
    public Task ReloadAsync(string contentId)
    {
        _detailPrefetchCache.Invalidate(contentId);
        return LoadAsync(contentId);
    }

    private async Task CheckFavoriteWatchlistAsync(string contentId, CancellationTokenSource owner)
    {
        try
        {
            var ct = owner.Token;
            var favTask = _catalogApi.GetFavoriteItemAsync(contentId, ct);
            var wlTask = _catalogApi.GetWatchlistItemAsync(contentId, ct);
            await Task.WhenAll(favTask, wlTask);
            if (!ReferenceEquals(_loadCts, owner) || Item?.ContentId != contentId) return;
            IsFavorite = await favTask;
            InWatchlist = await wlTask;
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
        }
        catch
        {
            // Non-fatal -- fall back to values from item detail
        }
    }

    [RelayCommand]
    private async Task LoadRatingAsync()
    {
        var contentId = Item?.ContentId;
        if (string.IsNullOrWhiteSpace(contentId)) return;

        try
        {
            var rating = await _catalogApi.GetRatingAsync(contentId);
            if (Item?.ContentId != contentId) return;
            UserRating = rating;
        }
        catch
        {
            // Rating load failure is non-fatal
        }
    }

    [RelayCommand]
    private async Task SetRatingAsync(int rating)
    {
        var contentId = Item?.ContentId;
        var seriesId = Item?.SeriesId;
        if (string.IsNullOrWhiteSpace(contentId)) return;

        // B13 + F4: optimistic rating swap. Clicking an already-selected star
        // clears the rating (matches webui toggle-off). Revert on failure.
        int? priorRating = UserRating;
        int? nextRating = (UserRating == rating) ? null : rating;
        Interlocked.Increment(ref _localStateGeneration);
        UserRating = nextRating;
        try
        {
            if (nextRating == null)
                await _catalogApi.DeleteRatingAsync(contentId);
            else
                await _catalogApi.SetRatingAsync(contentId, nextRating.Value);

            if (Item?.ContentId != contentId) return;
            Publish(MediaSurfaceChangeKind.RatingChanged, contentId, seriesId, nextRating);
        }
        catch
        {
            if (Item?.ContentId == contentId)
                UserRating = priorRating;
        }
        finally { Interlocked.Increment(ref _localStateGeneration); }
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync()
    {
        var item = Item;
        var contentId = item?.ContentId;
        var seriesId = item?.SeriesId;
        if (string.IsNullOrWhiteSpace(contentId)) return;
        var context = _catalogApi.CaptureContext();

        // B13 + F4: optimistic update — flip the flag immediately so the UI
        // responds instantly, then call the API. If it fails, revert. Publish
        // a MediaSurfaceChanged message on success so HomeViewModel's Continue
        // Watching row and HistoryViewModel refresh without a page reload.
        bool wasWatched = IsWatched;
        Interlocked.Increment(ref _localStateGeneration);
        IsWatched = !wasWatched;
        var generation = Interlocked.Increment(ref _watchedWriteGeneration);
        bool Current() => ReferenceEquals(Item, item) && _catalogApi.IsCurrentContext(context)
            && _stateRefreshActive && generation == Volatile.Read(ref _watchedWriteGeneration);
        try
        {
            if (wasWatched)
                await _catalogApi.MarkUnwatchedAsync(contentId);
            else
                await _catalogApi.MarkWatchedAsync(contentId);

            if (!Current()) return;
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                wasWatched ? MediaSurfaceChangeKind.WatchedCleared : MediaSurfaceChangeKind.WatchedMarked,
                contentId,
                seriesId, context: context));
        }
        catch
        {
            if (Current())
                IsWatched = wasWatched;
        }
    }

    [RelayCommand]
    private async Task LoadSimilarAsync()
    {
        if (Item == null) return;
        var contentId = Item.ContentId;
        var generation = Interlocked.Increment(ref _similarLoadGeneration);
        var context = _catalogApi.CaptureContext();
        SimilarLoadFailed = false;
        _similarLoaded = true;
        try
        {
            var response = await _catalogApi.GetSimilarAsync(contentId);
            if (generation != Volatile.Read(ref _similarLoadGeneration)
                || Item?.ContentId != contentId || !_catalogApi.IsCurrentContext(context)) return;

            _similarLoaded = true;
            MediaItemCollectionReconciler.Apply(SimilarItems,
                response.Items.Where(item => item.UserState?.NotInterested != true).Take(12).ToArray());
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _similarLoadGeneration)
                || Item?.ContentId != contentId || !_catalogApi.IsCurrentContext(context)) return;
            SimilarLoadFailed = true;
            System.Diagnostics.Debug.WriteLine($"Similar-items load failed for {contentId}: {ex}");
        }
    }

    [RelayCommand]
    private async Task LoadSeasonsAsync()
    {
        if (Item == null || !IsSeries || IsSeasonsLoading) return;

        var contentId = Item.ContentId;
        var item = Item;
        var context = _catalogApi.CaptureContext();
        var generation = Interlocked.Increment(ref _seasonsLoadGeneration);

        IsSeasonsLoading = true;
        SeasonsLoadFailed = false;
        try
        {
            SeasonsResponse response;
            var attempt = 0;
            while (true)
            {
                var watchedRevision = WatchedStateRevision;
                var remoteRevision = RemoteStateRevision;
                response = await _catalogApi.GetSeasonsAsync(contentId, LibraryId);
                if (generation != Volatile.Read(ref _seasonsLoadGeneration)
                    || !ReferenceEquals(Item, item) || !_catalogApi.IsCurrentContext(context)) return;
                if (++attempt == 2 || watchedRevision == WatchedStateRevision && remoteRevision == RemoteStateRevision) break;
            }
            Seasons.Clear();
            foreach (var season in response.Seasons)
                Seasons.Add(season);
            _seasonsLoaded = true;

            // Auto-select the first season
            if (Seasons.Count > 0)
                await SelectSeasonAsync(Seasons[0].SeasonNumber);
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _seasonsLoadGeneration)
                || Item?.ContentId != contentId) return;
            SeasonsLoadFailed = true;
            System.Diagnostics.Debug.WriteLine($"Seasons load failed for {contentId}: {ex}");
        }
        finally
        {
            if (generation == Volatile.Read(ref _seasonsLoadGeneration)
                && Item?.ContentId == contentId)
                IsSeasonsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectSeasonAsync(int seasonNumber)
    {
        if (Item == null || seasonNumber < 0) return;

        var contentId = Item.ContentId;
        var item = Item;
        var context = _catalogApi.CaptureContext();
        var generation = Interlocked.Increment(ref _episodesLoadGeneration);

        SelectedSeasonNumber = seasonNumber;
        EpisodesSeasonContentId = null;
        IsEpisodesLoading = true;
        EpisodesLoadFailed = false;
        Episodes.Clear();

        try
        {
            EpisodesResponse response;
            var attempt = 0;
            while (true)
            {
                var watchedRevision = WatchedStateRevision;
                var remoteRevision = RemoteStateRevision;
                response = await _catalogApi.GetEpisodesAsync(contentId, seasonNumber, LibraryId);
                if (generation != Volatile.Read(ref _episodesLoadGeneration)
                    || !ReferenceEquals(Item, item) || !_catalogApi.IsCurrentContext(context)) return;
                if (++attempt == 2 || watchedRevision == WatchedStateRevision && remoteRevision == RemoteStateRevision) break;
            }
            foreach (var episode in response.Episodes)
                Episodes.Add(episode);
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _episodesLoadGeneration)
                || Item?.ContentId != contentId) return;
            EpisodesLoadFailed = true;
            System.Diagnostics.Debug.WriteLine($"Episode load failed for season {seasonNumber}: {ex}");
        }
        finally
        {
            if (generation == Volatile.Read(ref _episodesLoadGeneration)
                && Item?.ContentId == contentId)
                IsEpisodesLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (Item == null) return;

        // B13 + F4: optimistic flip, revert on failure, publish on success.
        bool wasFavorite = IsFavorite;
        Interlocked.Increment(ref _localStateGeneration);
        IsFavorite = !wasFavorite;
        try
        {
            if (wasFavorite)
                await _catalogApi.RemoveFavoriteAsync(Item.ContentId);
            else
                await _catalogApi.AddFavoriteAsync(Item.ContentId);

            Publish(
                wasFavorite ? MediaSurfaceChangeKind.FavoriteRemoved : MediaSurfaceChangeKind.FavoriteAdded,
                Item.ContentId,
                Item.SeriesId);
        }
        catch
        {
            IsFavorite = wasFavorite;
        }
        finally { Interlocked.Increment(ref _localStateGeneration); }
    }

    [RelayCommand]
    private async Task ToggleWatchlistAsync()
    {
        if (Item == null) return;

        // B13 + F4: optimistic flip, revert on failure, publish on success.
        bool wasInList = InWatchlist;
        Interlocked.Increment(ref _localStateGeneration);
        InWatchlist = !wasInList;
        try
        {
            if (wasInList)
                await _catalogApi.RemoveFromWatchlistAsync(Item.ContentId);
            else
                await _catalogApi.AddToWatchlistAsync(Item.ContentId);

            Publish(
                wasInList ? MediaSurfaceChangeKind.WatchlistRemoved : MediaSurfaceChangeKind.WatchlistAdded,
                Item.ContentId,
                Item.SeriesId);
        }
        catch
        {
            InWatchlist = wasInList;
        }
        finally { Interlocked.Increment(ref _localStateGeneration); }
    }
}
