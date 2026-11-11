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

    private void RetirePendingSubtitleReadyRefresh()
    {
        lock (_subtitleReadyRefreshLock)
        {
            foreach (var pending in _pendingSubtitleReadyRefreshes) ContentLoaded -= pending;
            _pendingSubtitleReadyRefreshes.Clear();
        }
    }

    private void DeferSubtitleReadyRefresh(JsonElement payload, SubtitleOperationOwner owner, string sessionId, int fileId)
    {
        var announcement = payload.Clone();
        Action refresh = null!;
        refresh = () =>
        {
            lock (_subtitleReadyRefreshLock)
            {
                if (!_pendingSubtitleReadyRefreshes.Remove(refresh)) return;
                ContentLoaded -= refresh;
            }
            if (owner.IsCurrentForAi && _playbackManager?.CurrentSession is { } current &&
                current.SessionId == sessionId && current.MediaFileId == fileId)
                _ = ApplySubtitleReadyAsync(announcement, sessionId);
        };
        lock (_subtitleReadyRefreshLock)
        {
            _pendingSubtitleReadyRefreshes.Add(refresh);
            ContentLoaded += refresh;
        }
        if (!_switchingContent && !IsLoading) refresh();
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
        foreach (var entry in _prefetchedSubtitleSids.Where(pair => !cacheIndices.ContainsKey(pair.Key)).ToArray())
            if (entry.Value.FileGeneration == Volatile.Read(ref _fileLoadGeneration)) _mpv?.RemoveSubtitle(entry.Value.Sid);
        Remap(_loadedExternalSubtitleSids);
        Remap(_prefetchedSubtitleFiles);
        Remap(_prefetchedSubtitleSids);
        var stale = _staleExternalSubtitleIndices.Where(cacheIndices.ContainsKey).Select(index => cacheIndices[index]).ToArray();
        _staleExternalSubtitleIndices.Clear();
        foreach (var staleIndex in stale) _staleExternalSubtitleIndices.Add(staleIndex);
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
    }

    private async Task ApplySubtitleReadyAsync(JsonElement payload, string eventSessionId)
    {
        var manager = _playbackManager;
        if (manager == null || !TryGetPayloadInt(payload, "file_id", out var fileId)) return;
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
        try
        {
            // Serialize even in-place merges against inventory publication. A queued
            // announcement belongs to the same file lifetime, not the outgoing plan.
            await _subtitleReadyGuard.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            if (!owner.IsCurrentForAi) return;
            session = manager.CurrentSession;
            if (session == null || session.SessionId != eventSessionId || session.MediaFileId != fileId || !owner.IsCurrent) return;
            var nativeFile = Volatile.Read(ref _fileLoadGeneration);
            var file = Volatile.Read(ref _playbackFileGeneration);
            bool SameFile() => nativeFile == Volatile.Read(ref _fileLoadGeneration) && file == Volatile.Read(ref _playbackFileGeneration);
            bool Current() => owner.IsCurrent && SameFile();
            // Never merge into a plan whose replacement inventory may already be
            // frozen. Retain every payload and replay it after the actual load.
            if (_switchingContent || IsLoading)
            {
                DeferSubtitleReadyRefresh(payload, owner, session.SessionId, fileId);
                return;
            }
            if (payload.TryGetProperty("track", out var value) && value.ValueKind == JsonValueKind.Object)
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
                        if (!Current()) return;
                        if (manager.TryApplySubtitleTrack(session, track))
                        {
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
            var busyRevision = Volatile.Read(ref _playbackBusyRevision);
            var seekRevision = Volatile.Read(ref _seekRestartGeneration);
            bool SameTransport() => SameFile() && busyRevision == Volatile.Read(ref _playbackBusyRevision) &&
                seekRevision == Volatile.Read(ref _seekRestartGeneration);
            var position = CurrentMediaPosition;
            var replacement = await manager.RefreshSubtitleInventoryForOwnerAsync(position,
                candidate => Current() && SameTransport() && ReferenceEquals(candidate, session),
                (previous, next) => next.MediaFileId == fileId && SameTransport() && owner.TryAdoptSession(previous, next), token)
                .ConfigureAwait(false);
            if (replacement == null || !Current() || !SameTransport()) return;
            if (!SubtitleInventoryRequiresTransportReload(session, replacement))
            {
                lock (_subtitleSyncGate)
                lock (_subtitleTrackStateLock)
                lock (manager.SubtitleInventorySyncRoot)
                {
                    if (!Current() || !SameTransport()) return;
                    AdoptSubtitleInventoryInPlace(session, replacement);
                }
                AdoptBufferingQualityPlan();
                SendSubtitleListToOsc();
                EnsureSubtitleSync();
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
            if (!Current() || !SameTransport()) return;
            PlayMethod = replacement.PlayMethod;
            ApplyPreparedTransport(prepared);
            PrepareSubtitleFileForReload(session, replacement, manager.SubtitleSelectionForLoad);
            BeginMpvLoad(prepared, CaptureUserPausedState());
            SendSubtitleListToOsc();
            await ReconcileDeferredSubtitleSelectionAsync(manager, replacement).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogToFile("state_trace.txt", $"Subtitle announcement refresh failed: {ex.Message}"); }
        finally { if (entered) _subtitleReadyGuard.Release(); }
    }
}
