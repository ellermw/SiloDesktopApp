using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    public async Task RefreshSubtitlesAfterAiAsync(int mediaFileId, long? preferredSubtitleId = null, Func<bool>? isCurrent = null,
        Func<bool>? mayActivate = null)
    {
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        if (manager == null || session == null || session.MediaFileId != mediaFileId) return;
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureContext();
        var mpv = _mpv;
        var token = _authorityLifetimeCts.Token;
        var play = Volatile.Read(ref _playRequestGeneration);
        var file = Volatile.Read(ref _playbackFileGeneration);
        var nativeFile = Volatile.Read(ref _fileLoadGeneration);
        var content = ContentId;
        var liveJob = Volatile.Read(ref _liveSubtitleJobId);
        List<SubtitleTrackInfo> previousInventory;
        lock (manager.SubtitleInventorySyncRoot)
        {
            if (!ReferenceEquals(session, manager.CurrentSession)) return;
            previousInventory = session.SubtitleUrls;
        }
        var acceptedInventory = previousInventory;
        var activation = mayActivate ?? CaptureSubtitleActivationIntent();
        bool Current() => !_closing && !token.IsCancellationRequested && (isCurrent?.Invoke() ?? true) &&
            activation() &&
            ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(context) &&
            ReferenceEquals(manager, _playbackManager) && ReferenceEquals(session, manager.CurrentSession) &&
            ReferenceEquals(acceptedInventory, session.SubtitleUrls) && nativeFile == Volatile.Read(ref _fileLoadGeneration) &&
            ReferenceEquals(mpv, _mpv) && content == ContentId && play == Volatile.Read(ref _playRequestGeneration) &&
            file == Volatile.Read(ref _playbackFileGeneration);
        if (!Current()) return;
        try
        {
            var response = await authority.PlaybackApi.GetSubtitlesAsync(mediaFileId, token, context).ConfigureAwait(false);
            if (!Current()) return;

            var previousDownloads = previousInventory
                .Where(track => string.Equals(track.Source, "downloaded", StringComparison.OrdinalIgnoreCase)).ToArray();
            var existing = previousInventory.Except(previousDownloads).ToList();
            var reserved = previousDownloads.Where(track => !string.IsNullOrWhiteSpace(track.TrackId) &&
                !string.IsNullOrWhiteSpace(track.Delivery)).Concat(existing).ToArray();
            var baseIndex = existing.Count == 0 ? 0 : existing.Max(track => track.Index) + 1;
            // Stored-list ordinals are relative to the non-downloaded inventory.
            // If canonical rows disagree, only a replan can make new rows executable.
            var canonicalOrdinalsAgree = response.Subtitles.Select((entry, offset) =>
                !reserved.Any(track => (track.Id == entry.Id || track.SyncKey == "stored-" + entry.Id) &&
                    track.Index != baseIndex + offset)).All(agrees => agrees);
            var canonicalStoredIds = new Dictionary<SubtitleTrackInfo, long>();
            var downloaded = response.Subtitles.Select((entry, offset) =>
            {
                var syncKey = "stored-" + entry.Id;
                var unique = entry.Id > 0 && response.Subtitles.Count(item => item.Id == entry.Id) == 1;
                var matches = previousDownloads.Where(track => unique &&
                    (track.Id == entry.Id || track.SyncKey == syncKey)).Take(2).ToArray();
                var previous = matches.Length == 1 ? matches[0] : null;
                // A stored-list response cannot downgrade canonical plan/event facts.
                if (previous != null && !string.IsNullOrWhiteSpace(previous.TrackId) &&
                    !string.IsNullOrWhiteSpace(previous.Delivery))
                {
                    canonicalStoredIds[previous] = entry.Id;
                    return previous;
                }
                var ordinal = baseIndex + offset;
                var collision = reserved.Any(track => track.Index == ordinal);
                var executable = unique && canonicalOrdinalsAgree && !collision;
                if (collision) ordinal = Math.Max(reserved.Max(track => track.Index) + 1, baseIndex + response.Subtitles.Count) + offset;
                return new SubtitleTrackInfo
                {
                    TrackId = previous?.TrackId,
                    SyncKey = previous?.SyncKey ?? syncKey,
                    Id = entry.Id,
                    Unplayable = !executable || (previous?.Unplayable ?? false),
                    Delivery = previous?.Delivery,
                    Index = ordinal,
                    MediaFileId = mediaFileId,
                    Language = entry.Language,
                    Codec = previous?.Codec ?? (string.IsNullOrWhiteSpace(entry.Format) ? entry.Codec : entry.Format),
                    Label = string.IsNullOrWhiteSpace(entry.ReleaseName)
                        ? (entry.Title ?? entry.Language)
                        : $"{entry.ReleaseName} ({entry.Provider})",
                    Source = "downloaded",
                    HearingImpaired = entry.HearingImpaired,
                    Forced = entry.Forced,
                    Url = executable ? $"/stream/{session.SessionId}/subtitles/{ordinal}" : "",
                };
            }).ToList();
            lock (_subtitleSyncGate)
            lock (_subtitleTrackStateLock)
            lock (manager.SubtitleInventorySyncRoot)
            {
                if (!Current()) return;
                foreach (var entry in canonicalStoredIds) entry.Key.Id ??= entry.Value;

                // Keep row objects for uniquely identified downloads. Awaiting
                // selections follow that identity (including a changed Index),
                // while removed rows fail the inventory membership guards.
                // Snapshot index-keyed state before updating any retained row.
                var previousRows = previousInventory.Select(track => (Track: track, Index: track.Index)).ToArray();
                var loaded = previousDownloads.Select(track =>
                    (Track: track, Sid: _loadedExternalSubtitleSids.GetValueOrDefault(track.Index))).ToArray();
                var prefetched = previousDownloads.Select(track =>
                    (Track: track, Path: _prefetchedSubtitleFiles.GetValueOrDefault(track.Index))).ToArray();
                downloaded = downloaded.Select(refreshed =>
                {
                    var matches = previousDownloads.Where(track => track.Id == refreshed.Id ||
                        track.SyncKey == refreshed.SyncKey).Take(2).ToArray();
                    if (matches.Length != 1 || downloaded.Count(track => track.Id == refreshed.Id ||
                        track.SyncKey == refreshed.SyncKey) != 1) return refreshed;
                    var retained = matches[0];
                    retained.Index = refreshed.Index;
                    retained.MediaFileId = refreshed.MediaFileId;
                    retained.SyncKey = refreshed.SyncKey;
                    retained.Id = refreshed.Id;
                    retained.Unplayable = refreshed.Unplayable;
                    retained.Language = refreshed.Language;
                    retained.Codec = refreshed.Codec;
                    retained.Label = refreshed.Label;
                    retained.HearingImpaired = refreshed.HearingImpaired;
                    retained.Forced = refreshed.Forced;
                    retained.Url = refreshed.Url;
                    return retained;
                }).ToList();

                SubtitleTrackInfo? Rebind(SubtitleTrackInfo? track)
                {
                    if (track == null) return null;
                    if (existing.Contains(track)) return track.Unplayable ? null : track;
                    var candidates = string.Equals(track.Source, "downloaded", StringComparison.OrdinalIgnoreCase)
                        ? downloaded : existing;
                    var matches = candidates.Where(candidate => !candidate.Unplayable &&
                        string.Equals(candidate.Source, track.Source, StringComparison.OrdinalIgnoreCase) &&
                        (!string.IsNullOrWhiteSpace(track.TrackId) && candidate.TrackId == track.TrackId ||
                         !string.IsNullOrWhiteSpace(track.SyncKey) && candidate.SyncKey == track.SyncKey ||
                         track.Id is > 0 && candidate.Id == track.Id)).Take(2).ToArray();
                    return matches.Length == 1 ? matches[0] : null;
                }
                int? RebindIndex(int? index) => index == null || index < 0 ? index :
                    Rebind(previousRows.FirstOrDefault(row => row.Index == index).Track)?.Index;

                // A refresh is not a file load. Remap caches under the same
                // lock used by prefetch/sub-add completion instead of retiring
                // valid in-flight selections with the file-load generation.
                foreach (var index in previousRows.Where(row => previousDownloads.Contains(row.Track)).Select(row => row.Index)
                    .Concat(downloaded.Select(track => track.Index)).Distinct())
                {
                    _loadedExternalSubtitleSids.Remove(index);
                    _prefetchedSubtitleFiles.Remove(index);
                }
                foreach (var entry in loaded)
                    if (Rebind(entry.Track) is { } rebound && entry.Sid > 0) _loadedExternalSubtitleSids[rebound.Index] = entry.Sid;
                foreach (var entry in prefetched)
                {
                    if (entry.Path == null) continue;
                    if (Rebind(entry.Track) is { } rebound) _prefetchedSubtitleFiles[rebound.Index] = entry.Path;
                }
                foreach (var path in prefetched.Select(entry => entry.Path).OfType<string>().Distinct()
                    .Where(path => !_prefetchedSubtitleFiles.Values.Contains(path)))
                {
                    _subtitleSyncFiles.Remove(path);
                    try { File.Delete(path); } catch { }
                }
                var activeRetired = _activeSubtitleServerIndex >= 0 && !RebindIndex(_activeSubtitleServerIndex).HasValue;
                var pendingRetired = _pendingSubtitlePreferenceOwner?.MediaFileId == mediaFileId &&
                    _pendingSubtitlePreferenceIndex is >= 0 && !RebindIndex(_pendingSubtitlePreferenceIndex).HasValue;
                _activeSubtitleServerIndex = RebindIndex(_activeSubtitleServerIndex) ?? -1;
                if (_executedSubtitleFileId == mediaFileId)
                {
                    var executed = _executedSubtitleTrack;
                    _executedSubtitleTrack = Rebind(executed);
                    if (executed != null && _executedSubtitleTrack == null && _liveSubtitleSid <= 0)
                        _mpv?.SetSubtitleTrack(0);
                }
                if (_pendingInitialSubtitleFileId == mediaFileId)
                {
                    if (_pendingInitialServerSubtitleIndex.HasValue)
                        _pendingInitialServerSubtitleIndex = RebindIndex(_pendingInitialServerSubtitleIndex);
                    _pendingSubtitleSelection = RebindIndex(_pendingSubtitleSelection);
                }
                if (_pendingSubtitlePreferenceOwner?.MediaFileId == mediaFileId)
                {
                    _pendingSubtitlePreferenceIndex = RebindIndex(_pendingSubtitlePreferenceIndex);
                    if (!_pendingSubtitlePreferenceIndex.HasValue) _pendingSubtitlePreferenceOwner = null;
                }
                if (_preparedSubtitleReload is { } prepared && prepared.MediaFileId == mediaFileId)
                    _preparedSubtitleReload = RebindIndex(prepared.Index) is int index
                        ? prepared with { Index = index } : null;
                if (_subtitleReplanAttempt is { } attempt && ReferenceEquals(attempt.Session, session))
                    _subtitleReplanAttempt = attempt with { SelectionIndex = RebindIndex(attempt.SelectionIndex) ?? -1 };
                var granted = previousRows.FirstOrDefault(row => !string.IsNullOrWhiteSpace(session.SelectedSubtitleTrackId)
                    ? row.Track.TrackId == session.SelectedSubtitleTrackId : row.Index == session.SelectedSubtitleTrackIndex).Track;
                if (granted != null)
                {
                    var rebound = Rebind(granted);
                    session.SelectedSubtitleTrackId = rebound?.TrackId;
                    session.SelectedSubtitleTrackIndex = rebound?.Index;
                }
                session.SubtitleUrls = acceptedInventory = [.. existing, .. downloaded];
                if (activeRetired || pendingRetired)
                {
                    // Removal retires an executable or pending row, not the
                    // viewer's retained identity or automatic/explicit provenance.
                    ShowSubtitleSelectionUnavailable();
                }
            }
            if (!Current()) return;
            SendSubtitleListToOsc();

            var preferred = preferredSubtitleId.HasValue
                ? downloaded.SingleOrDefault(track => !track.Unplayable && track.Id == preferredSubtitleId)
                : null;
            if (preferred != null && mpv != null && Current())
            {
                var completion = liveJob > 0 && preferred.Id is long storedId ? new StoredSubtitleCompletion(authority, context, manager, mpv,
                    mediaFileId, play, nativeFile, content, liveJob, storedId) : null;
                if (completion != null) _storedSubtitleCompletion = completion;
                var executedOrQueued = await TrySetSubtitleTrackAndPersistAsync(1, preferred.Language, preferred,
                    claimActivation: false, acceptActivation: activation).ConfigureAwait(false);
                if (executedOrQueued && Current()) mpv.SendScriptMessage("osc-set-active-subtitle", _activeSubtitleServerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!executedOrQueued && ReferenceEquals(_storedSubtitleCompletion, completion)) _storedSubtitleCompletion = null;
                // The executor owns cleanup after actual SID execution. A busy
                // read or merely queued bitmap reload must retain live cues.
                CompleteStoredSubtitleReplacement(preferred);
            }
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"RefreshSubtitlesAfterAiAsync failed: {ex.Message}");
        }
    }

}
