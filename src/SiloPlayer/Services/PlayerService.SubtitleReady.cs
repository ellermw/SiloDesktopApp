using System.Text.Json;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private static readonly JsonSerializerOptions SubtitleReadyJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly SemaphoreSlim _subtitleReadyGuard = new(1, 1);
    private readonly object _subtitleReadyRefreshLock = new();
    private readonly HashSet<Action> _pendingSubtitleReadyRefreshes = [];
    // Publication can precede native application. Retries on the same loaded
    // file must still compare/remap from the inventory mpv actually uses.
    private sealed record PendingSubtitleInventoryApplication(SubtitleOperationOwner Owner,
        PlaybackStartResponse Session, long NativeFile, long File)
    {
        public int Failures;
    }
    private PendingSubtitleInventoryApplication? _pendingSubtitleInventoryApplication;
    // A replan can also fail before it publishes anything. Nothing is then
    // outstanding except the announcement itself; its retries share one budget
    // per loaded file, renewed by the next published inventory.
    private sealed record UnpublishedSubtitleInventoryRefresh(long NativeFile, long File)
    {
        public int Failures;
    }
    private UnpublishedSubtitleInventoryRefresh? _unpublishedSubtitleInventoryRefresh;
    private const int SubtitleInventoryApplicationRetries = 2;
    private static readonly TimeSpan SubtitleInventoryApplicationRetryDelay = TimeSpan.FromSeconds(2);

    private void RetirePendingSubtitleReadyRefresh()
    {
        lock (_subtitleReadyRefreshLock)
        {
            foreach (var pending in _pendingSubtitleReadyRefreshes) ContentLoaded -= pending;
            _pendingSubtitleReadyRefreshes.Clear();
        }
    }

    private bool SubtitleReadyRefreshIsBusy => _switchingContent || IsLoading ||
        Volatile.Read(ref _pendingPlanInvalidations) > 0 || _playbackTerminalState != null;

    private void ResumePendingSubtitleReadyRefreshes()
    {
        Action[] pending;
        lock (_subtitleReadyRefreshLock) pending = _pendingSubtitleReadyRefreshes.ToArray();
        foreach (var refresh in pending) refresh();
    }

    private void DeferSubtitleReadyRefresh(JsonElement payload, SubtitleOperationOwner owner, string sessionId, int fileId)
    {
        var announcement = payload.Clone();
        DeferUntilSubtitleReadyIdle(() =>
        {
            if (owner.IsCurrentForAi && _playbackManager?.CurrentSession is { } current &&
                current.SessionId == sessionId && current.MediaFileId == fileId)
                _ = ApplySubtitleReadyAsync(announcement, sessionId);
        });
    }

    private void DeferUntilSubtitleReadyIdle(Action resume)
    {
        Action refresh = null!;
        refresh = () =>
        {
            // FILE_LOADED can race the invalidation's finally block. Keep the
            // payload subscribed until both the plan and native load are idle.
            if (SubtitleReadyRefreshIsBusy) return;
            lock (_subtitleReadyRefreshLock)
            {
                if (!_pendingSubtitleReadyRefreshes.Remove(refresh)) return;
                ContentLoaded -= refresh;
            }
            resume();
        };
        lock (_subtitleReadyRefreshLock)
        {
            _pendingSubtitleReadyRefreshes.Add(refresh);
            ContentLoaded += refresh;
        }
        refresh();
    }

    // Called once an announcement's replacement inventory is applied to the
    // loaded file, in place or by its reload. A stored-list refresh still reading
    // under an outgoing response would otherwise end without its selection.
    private void RestartSupersededSubtitleListRefreshes(PlaybackStartResponse applied)
    {
        Action<PlaybackStartResponse>[] reading;
        lock (_subtitleReadyRefreshLock) reading = _storedSubtitleListRefreshes.ToArray();
        foreach (var superseded in reading) superseded(applied);
    }

    // Publication precedes that call. A refresh whose read returns in between
    // ends on the outgoing response before the restart can see it. The guard is
    // held from publication to application; a failed one stays pending for its
    // retry. Only then does an ended refresh keep its registration.
    private bool AwaitsSubtitleInventoryApplication(PlaybackManager manager, PlaybackStartResponse session)
        => manager.CurrentSession is { } current && !ReferenceEquals(current, session) &&
            current.SessionId == session.SessionId && current.MediaFileId == session.MediaFileId &&
            (_pendingSubtitleInventoryApplication != null || _subtitleReadyGuard.CurrentCount == 0);

    // Stored subtitles whose pick was already started over on the response an
    // authoritative replan published. That inventory is the final answer for
    // the pick: a refresh the stored list still cannot place there never asks
    // for another, so an unmatched pick cannot replan in a loop.
    private sealed record ReplannedStoredSubtitlePicks(PlaybackStartResponse Session, HashSet<long> SubtitleIds);
    private ReplannedStoredSubtitlePicks? _replannedStoredSubtitlePicks;

    private void MarkStoredSubtitlePickReplanned(PlaybackStartResponse applied, long subtitleId)
    {
        lock (_subtitleReadyRefreshLock)
        {
            var picks = _replannedStoredSubtitlePicks;
            if (picks == null || !ReferenceEquals(picks.Session, applied))
                _replannedStoredSubtitlePicks = picks = new(applied, []);
            picks.SubtitleIds.Add(subtitleId);
        }
    }

    // Called by both stored-list refresh entry points, outside their gates, when
    // the list's layout withheld the requested subtitle's route. Only v3 has an
    // authoritative inventory to ask for: the no-change replan an announcement
    // without track metadata makes. Returns that response once it is applied in
    // place of the one the list was read under.
    private async Task<PlaybackStartResponse?> ReplanForStoredSubtitlePickAsync(PlaybackManager manager,
        PlaybackStartResponse session, long subtitleId)
    {
        lock (_subtitleReadyRefreshLock)
            if (session.ProtocolVersion < 3 || _replannedStoredSubtitlePicks is { } picks &&
                ReferenceEquals(picks.Session, session) && picks.SubtitleIds.Contains(subtitleId)) return null;
        await ApplySubtitleReadyAsync(
            JsonSerializer.SerializeToElement(new { file_id = session.MediaFileId, subtitle_id = subtitleId }),
            session.SessionId).ConfigureAwait(false);
        return manager.CurrentSession is { } applied && !ReferenceEquals(applied, session) &&
            applied.SessionId == session.SessionId && applied.MediaFileId == session.MediaFileId &&
            _pendingSubtitleInventoryApplication == null ? applied : null;
    }

    private static bool SubtitleInventoryRequiresTransportReload(PlaybackStartResponse previous, PlaybackStartResponse next)
        => previous.StreamUrl != next.StreamUrl || previous.Delivery != next.Delivery ||
            PlaybackTransportPlanner.Plan(previous).TransportKind != PlaybackTransportPlanner.Plan(next).TransportKind ||
            previous.IsSubtitleBurnedIn != next.IsSubtitleBurnedIn ||
            previous.IsSubtitleBurnedIn &&
                ((!string.IsNullOrWhiteSpace(previous.SelectedSubtitleTrackId) || !string.IsNullOrWhiteSpace(next.SelectedSubtitleTrackId)
                    ? previous.SelectedSubtitleTrackId != next.SelectedSubtitleTrackId
                    : previous.SelectedSubtitleTrackIndex != next.SelectedSubtitleTrackIndex) ||
                 previous.SelectedSubtitleArtifactUrl != next.SelectedSubtitleArtifactUrl ||
                 previous.SubtitleTimingOriginSeconds != next.SubtitleTimingOriginSeconds);

    // The video file is unchanged. Move index-keyed native state by unique,
    // stable identity rather than reopening mpv just to replace the menu rows.
    private void AdoptSubtitleInventoryInPlace(PlaybackStartResponse previous, PlaybackStartResponse next)
    {
        SubtitleTrackInfo? Rebind(SubtitleTrackInfo? track)
        {
            if (track == null) return null;
            var matches = next.SubtitleUrls.Where(row => row.Source == track.Source &&
                !(track.Id is > 0 && row.Id is > 0 && track.Id != row.Id) &&
                !(!string.IsNullOrWhiteSpace(track.SyncKey) && !string.IsNullOrWhiteSpace(row.SyncKey) && track.SyncKey != row.SyncKey) &&
                (track.Id is > 0 && row.Id == track.Id ||
                 !string.IsNullOrWhiteSpace(track.SyncKey) && row.SyncKey == track.SyncKey ||
                 !string.IsNullOrWhiteSpace(track.TrackId) && row.TrackId == track.TrackId &&
                    !((track.Id is > 0 || !string.IsNullOrWhiteSpace(track.SyncKey)) &&
                      (row.Id is > 0 || !string.IsNullOrWhiteSpace(row.SyncKey))))).Take(2).ToArray();
            return matches.Length == 1 && !matches[0].Unplayable ? matches[0] : null;
        }
        int? RebindIndex(int? index) => index == null || index < 0 ? index :
            Rebind(previous.SubtitleUrls.SingleOrDefault(row => row.Index == index))?.Index;
        var cacheIndices = previous.SubtitleUrls.Select(row => (Before: row, After: Rebind(row)))
            .Where(pair => pair.After != null && pair.Before.Url == pair.After.Url && pair.Before.Delivery == pair.After.Delivery)
            .GroupBy(pair => pair.After!.Index).Where(group => group.Count() == 1).Select(group => group.Single())
            .GroupBy(pair => pair.Before.Index).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().After!.Index);
        void Remap<T>(Dictionary<int, T> cache)
        {
            var retained = cache.Where(pair => cacheIndices.ContainsKey(pair.Key))
                .Select(pair => (Index: cacheIndices[pair.Key], pair.Value)).ToArray();
            cache.Clear();
            foreach (var entry in retained) cache[entry.Index] = entry.Value;
        }
        // Captions already on screen outlive a changed Url/Delivery for the same
        // identity: keep the applied SID visible, but stale, so a re-selection
        // fetches the canonical cues instead of adding a duplicate track. Retire
        // every other dropped SID and sidecar file along with its cache entry.
        var active = _activeSubtitleServerIndex;
        var activeSid = active >= 0 && !cacheIndices.ContainsKey(active) ? _loadedExternalSubtitleSids.GetValueOrDefault(active) : 0;
        var retainedActive = activeSid > 0 && RebindIndex(active) is int reboundActive && !cacheIndices.ContainsValue(reboundActive)
            ? reboundActive : (int?)null;
        var droppedSids = _loadedExternalSubtitleSids.Where(pair => !cacheIndices.ContainsKey(pair.Key) &&
            !(retainedActive.HasValue && pair.Key == active)).Select(pair => pair.Value).ToArray();
        var droppedFiles = _prefetchedSubtitleFiles.Where(pair => !cacheIndices.ContainsKey(pair.Key))
            .Select(pair => pair.Value).Distinct().ToArray();
        foreach (var entry in _prefetchedSubtitleSids.Where(pair => !cacheIndices.ContainsKey(pair.Key)).ToArray())
            if (entry.Value.FileGeneration == Volatile.Read(ref _fileLoadGeneration)) _mpv?.RemoveSubtitle(entry.Value.Sid);
        Remap(_loadedExternalSubtitleSids);
        Remap(_prefetchedSubtitleFiles);
        Remap(_prefetchedSubtitleSids);
        var stale = _staleExternalSubtitleIndices.Where(cacheIndices.ContainsKey).Select(index => cacheIndices[index]).ToArray();
        _staleExternalSubtitleIndices.Clear();
        foreach (var staleIndex in stale) _staleExternalSubtitleIndices.Add(staleIndex);
        if (retainedActive is int retainedIndex)
        {
            _loadedExternalSubtitleSids[retainedIndex] = activeSid;
            _staleExternalSubtitleIndices.Add(retainedIndex);
        }
        foreach (var path in droppedFiles.Where(path => !_prefetchedSubtitleFiles.ContainsValue(path)))
        {
            _subtitleSyncFiles.Remove(path);
            try { File.Delete(path); } catch { }
        }
        _activeSubtitleServerIndex = RebindIndex(_activeSubtitleServerIndex) ?? -1;
        if (_executedSubtitleFileId == next.MediaFileId)
        {
            var executed = _executedSubtitleTrack;
            _executedSubtitleTrack = Rebind(executed);
            if (executed != null && _executedSubtitleTrack == null && _liveSubtitleSid <= 0) _mpv?.SetSubtitleTrack(0);
        }
        if (_pendingInitialSubtitleFileId == next.MediaFileId)
        {
            _pendingInitialServerSubtitleIndex = RebindIndex(_pendingInitialServerSubtitleIndex);
            _pendingSubtitleSelection = RebindIndex(_pendingSubtitleSelection);
        }
        if (_pendingSubtitlePreferenceOwner?.MediaFileId == next.MediaFileId)
        {
            _pendingSubtitlePreferenceIndex = RebindIndex(_pendingSubtitlePreferenceIndex);
            if (!_pendingSubtitlePreferenceIndex.HasValue) _pendingSubtitlePreferenceOwner = null;
        }
        if (_preparedSubtitleReload is { } prepared && prepared.MediaFileId == next.MediaFileId)
            _preparedSubtitleReload = RebindIndex(prepared.Index) is int preparedIndex ? prepared with { Index = preparedIndex } : null;
        if (_subtitleReplanAttempt is { } attempt && ReferenceEquals(attempt.Session, previous))
            _subtitleReplanAttempt = attempt with { Session = next, SelectionIndex = RebindIndex(attempt.SelectionIndex) ?? -1 };
        // A retired selection is off screen only now. Never remove the SID mpv still shows.
        foreach (var sid in droppedSids.Where(sid => !_loadedExternalSubtitleSids.ContainsValue(sid)))
            if (_mpv != null && (int)Math.Round(_mpv.GetPropertyDouble("sid")) != sid) _mpv.RemoveSubtitle(sid);
    }

    private async Task ApplySubtitleReadyAsync(JsonElement payload, string eventSessionId)
    {
        var manager = _playbackManager;
        if (manager == null || !TryGetPayloadInt(payload, "file_id", out var fileId)) return;
        // The realtime envelope may omit its redundant session ID. Resolve it
        // once so the same session fence also covers queued/deferred refreshes.
        if (string.IsNullOrEmpty(eventSessionId) && payload.TryGetProperty("session_id", out var fallbackSession) &&
            fallbackSession.ValueKind == JsonValueKind.String)
            eventSessionId = fallbackSession.GetString() ?? "";
        PlaybackStartResponse? session;
        SubtitleOperationOwner? owner;
        lock (manager.SubtitleInventorySyncRoot)
        {
            session = manager.CurrentSession;
            if (session == null || session.MediaFileId != fileId || eventSessionId != session.SessionId ||
                payload.TryGetProperty("session_id", out var payloadSession) &&
                    (payloadSession.ValueKind != JsonValueKind.String || payloadSession.GetString() != session.SessionId)) return;
            owner = CaptureSubtitleOperationOwner(session);
            if (owner == null || !owner.IsCurrent) return;
        }
        var token = _authorityLifetimeCts.Token;
        var entered = false;
        var refreshed = false;
        var nativeFile = Volatile.Read(ref _fileLoadGeneration);
        var file = Volatile.Read(ref _playbackFileGeneration);
        var busyRevision = Volatile.Read(ref _playbackBusyRevision);
        var seekRevision = Volatile.Read(ref _seekRestartGeneration);
        bool SameFile() => nativeFile == Volatile.Read(ref _fileLoadGeneration) && file == Volatile.Read(ref _playbackFileGeneration);
        bool SameTransport() => SameFile() && busyRevision == Volatile.Read(ref _playbackBusyRevision) &&
            seekRevision == Volatile.Read(ref _seekRestartGeneration);
        bool RetryIfSuperseded()
        {
            // A guard wait or a later busy claim may replace our exact plan.
            // Keep the announcement for this file lifetime, not that response.
            if (!token.IsCancellationRequested && owner.IsCurrentForAi && manager.CurrentSession is { } current &&
                current.SessionId == eventSessionId && current.MediaFileId == fileId &&
                (SubtitleReadyRefreshIsBusy || !ReferenceEquals(current, session) || !SameTransport()))
            {
                DeferSubtitleReadyRefresh(payload, owner, eventSessionId, fileId);
                return true;
            }
            return false;
        }
        void RetryUnpublishedSubtitleInventory()
        {
            // A replan that failed before publication changed nothing, so only
            // this announcement still records the new subtitle. Re-defer it with
            // the same bounded backoff instead of dropping it. An outstanding
            // application is retried by its own budget below.
            if (!entered || refreshed || token.IsCancellationRequested || _pendingSubtitleInventoryApplication != null ||
                !owner.IsCurrentForAi || manager.CurrentSession is not { } current ||
                current.SessionId != eventSessionId || current.MediaFileId != fileId) return;
            var unpublished = _unpublishedSubtitleInventoryRefresh;
            if (unpublished == null || unpublished.NativeFile != Volatile.Read(ref _fileLoadGeneration) ||
                unpublished.File != Volatile.Read(ref _playbackFileGeneration))
                _unpublishedSubtitleInventoryRefresh = unpublished =
                    new(Volatile.Read(ref _fileLoadGeneration), Volatile.Read(ref _playbackFileGeneration));
            if (++unpublished.Failures > SubtitleInventoryApplicationRetries) return;
            // The event's document may be disposed before the delay elapses.
            var announcement = payload.Clone();
            _ = Task.Delay(SubtitleInventoryApplicationRetryDelay * unpublished.Failures, token).ContinueWith(_ =>
            {
                if (ReferenceEquals(unpublished, _unpublishedSubtitleInventoryRefresh))
                    DeferSubtitleReadyRefresh(announcement, owner, eventSessionId, fileId);
            }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }
        void RecoverUnappliedSubtitleInventory()
        {
            // A failure after publication leaves mpv on a plan the manager no
            // longer holds. Retry the outstanding application a bounded number of
            // times, backing off so one outage is not replayed as back-to-back
            // replans. An announcement is never a playback failure: once the
            // budget is spent, the transport mpv is playing stays up and the
            // application stays outstanding for the next announcement or load.
            if (!entered || token.IsCancellationRequested || _pendingSubtitleInventoryApplication is not { } unapplied ||
                !unapplied.Owner.IsCurrentForAi || unapplied.NativeFile != Volatile.Read(ref _fileLoadGeneration) ||
                unapplied.File != Volatile.Read(ref _playbackFileGeneration) ||
                manager.CurrentSession is not { } published || published.SessionId != eventSessionId ||
                published.MediaFileId != fileId) return;
            if (++unapplied.Failures > SubtitleInventoryApplicationRetries) return;
            // The event's document may be disposed before the delay elapses.
            var announcement = payload.Clone();
            _ = Task.Delay(SubtitleInventoryApplicationRetryDelay * unapplied.Failures, token).ContinueWith(_ =>
            {
                if (ReferenceEquals(unapplied, _pendingSubtitleInventoryApplication))
                    DeferSubtitleReadyRefresh(announcement, owner, eventSessionId, fileId);
            }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }
        try
        {
            // Serialize even in-place merges against inventory publication. A queued
            // announcement belongs to the same file lifetime, not the outgoing plan.
            await _subtitleReadyGuard.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            if (!owner.IsCurrentForAi) return;
            session = manager.CurrentSession;
            if (session == null || session.SessionId != eventSessionId || session.MediaFileId != fileId || !owner.IsCurrent)
            {
                RetryIfSuperseded();
                return;
            }
            nativeFile = Volatile.Read(ref _fileLoadGeneration);
            file = Volatile.Read(ref _playbackFileGeneration);
            busyRevision = Volatile.Read(ref _playbackBusyRevision);
            seekRevision = Volatile.Read(ref _seekRestartGeneration);
            bool Current() => owner.IsCurrent && SameFile();
            // Never merge into a plan whose replacement inventory may already be
            // frozen. Retain every payload and replay it after the actual load.
            if (SubtitleReadyRefreshIsBusy)
            {
                DeferSubtitleReadyRefresh(payload, owner, session.SessionId, fileId);
                return;
            }
            var pendingApplication = _pendingSubtitleInventoryApplication;
            if (pendingApplication != null && (!pendingApplication.Owner.IsCurrentForAi ||
                pendingApplication.NativeFile != nativeFile || pendingApplication.File != file))
                _pendingSubtitleInventoryApplication = pendingApplication = null;
            var appliedSession = pendingApplication?.Session ?? session;
            // Optional metadata cannot complete an outstanding reload/cache remap.
            if (pendingApplication == null && payload.TryGetProperty("track", out var value) && value.ValueKind == JsonValueKind.Object)
            {
                PlaybackSubtitleInventoryV3? track = null;
                try { track = value.Deserialize<PlaybackSubtitleInventoryV3>(SubtitleReadyJson); }
                catch (JsonException) { /* Invalid optional metadata needs an authoritative replan too. */ }
                if (track?.Source != "downloaded" || track.Delivery is not ("sidecar" or "burn_in_only") ||
                    !value.TryGetProperty("combined_index", out var ordinal) || !ordinal.TryGetInt32(out _)) track = null;
                if (track?.Source == "downloaded" && TryGetPayloadInt(payload, "subtitle_id", out var storedId) && storedId > 0)
                {
                    if (track.StoredSubtitleId is > 0 && track.StoredSubtitleId != storedId) track = null;
                    else track.StoredSubtitleId = storedId;
                }
                if (track != null && Current())
                {
                    lock (_subtitleSyncGate)
                    lock (_subtitleTrackStateLock)
                    lock (manager.SubtitleInventorySyncRoot)
                    {
                        if (!Current() || SubtitleReadyRefreshIsBusy)
                        {
                            RetryIfSuperseded();
                            return;
                        }
                        // A successful merge rewrites the row already at this ordinal
                        // in place. Capture the facts its native caches were read under.
                        var rewritten = session.SubtitleUrls.FirstOrDefault(row => row.Index == track.CombinedIndex);
                        var (readUrl, readDelivery) = (rewritten?.Url, rewritten?.Delivery);
                        if (manager.TryApplySubtitleTrack(session, track))
                        {
                            if (rewritten != null && ((readUrl ?? "") != rewritten.Url || readDelivery != rewritten.Delivery))
                                InvalidateRewrittenSubtitleCache(rewritten.Index);
                            SendSubtitleListToOsc();
                            return;
                        }
                    }
                }
            }
            if (session.ProtocolVersion < 3)
            {
                await RefreshSubtitlesAfterAiForOwnerAsync(fileId, null, owner, isCurrent: Current).ConfigureAwait(false);
                return;
            }
            // v3 has no inventory GET. A no-change replan carries the existing
            // grant/retained intent (preserving unresolved grants via quality_change)
            // and returns canonical delivery facts.
            var position = CurrentMediaPosition;
            var replacement = await manager.RefreshSubtitleInventoryForOwnerAsync(position,
                candidate => Current() && SameTransport() && !SubtitleReadyRefreshIsBusy && ReferenceEquals(candidate, session),
                (previous, next) => next.MediaFileId == fileId && SameTransport() && !SubtitleReadyRefreshIsBusy && owner.TryAdoptSession(previous, next), token)
                .ConfigureAwait(false);
            if (replacement != null)
            {
                _pendingSubtitleInventoryApplication ??= new(owner, appliedSession, nativeFile, file);
                // Published inventory answers every announcement that failed before it.
                refreshed = true;
                _unpublishedSubtitleInventoryRefresh = null;
            }
            if (replacement == null || !Current() || !SameTransport())
            {
                RetryIfSuperseded();
                return;
            }
            if (!SubtitleInventoryRequiresTransportReload(appliedSession, replacement))
            {
                lock (_subtitleSyncGate)
                lock (_subtitleTrackStateLock)
                lock (manager.SubtitleInventorySyncRoot)
                {
                    if (!Current() || !SameTransport())
                    {
                        RetryIfSuperseded();
                        return;
                    }
                    AdoptSubtitleInventoryInPlace(appliedSession, replacement);
                    _pendingSubtitleInventoryApplication = null;
                }
                // The native transport did not change. Keep its stall history,
                // prompt/grace deadlines and throughput samples; only rebase the
                // session used by subsequent buffering actions and diagnostics.
                lock (_bufferingGate)
                    if (ReferenceEquals(_bufferingManager, manager) && ReferenceEquals(_bufferingSession, appliedSession) &&
                        ReferenceEquals(manager.CurrentSession, replacement))
                        _bufferingSession = replacement;
                SendSubtitleListToOsc();
                EnsureSubtitleSync();
                RestartSupersededSubtitleListRefreshes(replacement);
                await ReconcileDeferredSubtitleSelectionAsync(manager, replacement).ConfigureAwait(false);
                return;
            }
            // Local direct seeks and pause gestures remain available while the
            // inventory request is in flight. Reload at their latest intent.
            position = CurrentMediaPosition;
            var version = Versions.FirstOrDefault(item => item.FileId == replacement.MediaFileId)
                ?? throw new InvalidOperationException("The subtitle inventory's media version is unavailable.");
            var prepared = await PreparePlaybackTransportAsync(replacement, version,
                manager.StreamUrl ?? throw new InvalidOperationException("The refreshed stream URL is unavailable."), position, token)
                .ConfigureAwait(false);
            if (!Current() || !SameTransport())
            {
                RetryIfSuperseded();
                return;
            }
            PlayMethod = replacement.PlayMethod;
            ApplyPreparedTransport(prepared);
            PrepareSubtitleFileForReload(appliedSession, replacement, manager.SubtitleSelectionForLoad);
            BeginMpvLoad(prepared, CaptureUserPausedState());
            _pendingSubtitleInventoryApplication = null;
            SendSubtitleListToOsc();
            RestartSupersededSubtitleListRefreshes(replacement);
            await ReconcileDeferredSubtitleSelectionAsync(manager, replacement).ConfigureAwait(false);
        }
        // A request timeout also surfaces as a cancellation that superseded nothing.
        catch (OperationCanceledException) { if (!RetryIfSuperseded()) RetryUnpublishedSubtitleInventory(); }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Subtitle announcement refresh failed: {ex.Message}");
            RecoverUnappliedSubtitleInventory();
            RetryUnpublishedSubtitleInventory();
        }
        finally { if (entered) _subtitleReadyGuard.Release(); }
    }

    // Called under the sync, track-state and inventory gates. An in-place merge
    // keeps its row and ordinal, so AdoptSubtitleInventoryInPlace never runs for
    // it: bytes read under the previous Url/Delivery must not satisfy another
    // selection. Keep the applied SID visible, but stale; retire every other entry.
    private void InvalidateRewrittenSubtitleCache(int index)
    {
        if (_preparedSubtitleReload is { } prepared && prepared.Index == index) _preparedSubtitleReload = null;
        if (_prefetchedSubtitleFiles.Remove(index, out var path) && !_prefetchedSubtitleFiles.ContainsValue(path))
        {
            _subtitleSyncFiles.Remove(path);
            try { File.Delete(path); } catch { }
        }
        if (_prefetchedSubtitleSids.Remove(index, out var ready) &&
            ready.FileGeneration == Volatile.Read(ref _fileLoadGeneration)) _mpv?.RemoveSubtitle(ready.Sid);
        if (index == _activeSubtitleServerIndex)
        {
            if (_loadedExternalSubtitleSids.ContainsKey(index)) _staleExternalSubtitleIndices.Add(index);
        }
        else if (_loadedExternalSubtitleSids.Remove(index, out var sid))
        {
            _staleExternalSubtitleIndices.Remove(index);
            // Never remove the SID mpv still shows.
            if (_mpv != null && (int)Math.Round(_mpv.GetPropertyDouble("sid")) != sid) _mpv.RemoveSubtitle(sid);
        }
    }
}
