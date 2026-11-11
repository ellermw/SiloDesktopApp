using System.Text.Json;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private static readonly JsonSerializerOptions SubtitleReadyJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private Action? _pendingSubtitleReadyRefresh;

    private void RetirePendingSubtitleReadyRefresh()
    {
        var pending = Interlocked.Exchange(ref _pendingSubtitleReadyRefresh, null);
        if (pending != null) ContentLoaded -= pending;
    }

    private void DeferSubtitleReadyRefresh(JsonElement payload, SubtitleOperationOwner owner, string sessionId, int fileId)
    {
        RetirePendingSubtitleReadyRefresh();
        var announcement = payload.Clone();
        Action refresh = null!;
        refresh = () =>
        {
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _pendingSubtitleReadyRefresh, null, refresh), refresh)) return;
            ContentLoaded -= refresh;
            if (owner.IsCurrentForAi && _playbackManager?.CurrentSession is { } current &&
                current.SessionId == sessionId && current.MediaFileId == fileId)
                _ = ApplySubtitleReadyAsync(announcement);
        };
        _pendingSubtitleReadyRefresh = refresh;
        ContentLoaded += refresh;
        if (!_switchingContent && !IsLoading) refresh();
    }

    private async Task ApplySubtitleReadyAsync(JsonElement payload)
    {
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        if (manager == null || session == null || !TryGetPayloadInt(payload, "file_id", out var fileId) ||
            session.MediaFileId != fileId || !payload.TryGetProperty("session_id", out var eventSession) ||
            eventSession.ValueKind != JsonValueKind.String || eventSession.GetString() != session.SessionId) return;
        var owner = CaptureSubtitleOperationOwner(session);
        if (owner == null || !owner.IsCurrent) return;
        var nativeFile = Volatile.Read(ref _fileLoadGeneration);
        var file = Volatile.Read(ref _playbackFileGeneration);
        bool SameFile() => nativeFile == Volatile.Read(ref _fileLoadGeneration) && file == Volatile.Read(ref _playbackFileGeneration);
        bool Current() => owner.IsCurrent && SameFile();
        try
        {
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
            // v3 has no inventory GET. A no-change track_change carries the
            // existing grant/retained intent and returns canonical delivery facts.
            if (_switchingContent || IsLoading)
            {
                // A plan may have frozen its inventory before the subtitle was
                // added. Re-read after its actual load instead of losing the event.
                DeferSubtitleReadyRefresh(payload, owner, session.SessionId, fileId);
                return;
            }
            var busyRevision = Volatile.Read(ref _playbackBusyRevision);
            var seekRevision = Volatile.Read(ref _seekRestartGeneration);
            bool SameTransport() => SameFile() && busyRevision == Volatile.Read(ref _playbackBusyRevision) &&
                seekRevision == Volatile.Read(ref _seekRestartGeneration);
            var position = CurrentMediaPosition;
            var token = _authorityLifetimeCts.Token;
            var replacement = await manager.RefreshSubtitleInventoryForOwnerAsync(position,
                candidate => Current() && SameTransport() && ReferenceEquals(candidate, session),
                (previous, next) => next.MediaFileId == fileId && SameTransport() && owner.TryAdoptSession(previous, next), token)
                .ConfigureAwait(false);
            if (replacement == null || !Current() || !SameTransport()) return;
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
    }
}
