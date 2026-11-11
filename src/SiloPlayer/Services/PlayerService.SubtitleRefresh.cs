using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private static bool IsDownloadWithoutStoredIdentity(SubtitleTrackInfo track) =>
        string.Equals(track.Source, "downloaded", StringComparison.OrdinalIgnoreCase) &&
        track.Id is not > 0 && string.IsNullOrWhiteSpace(track.SyncKey);

    private static bool IsCanonicalDownloadWithoutStoredIdentity(SubtitleTrackInfo track) =>
        IsDownloadWithoutStoredIdentity(track) &&
        !string.IsNullOrWhiteSpace(track.TrackId) && !string.IsNullOrWhiteSpace(track.Delivery);

    // A plan-only row is known to the stored list by its ordinal alone. An
    // entry there that names another language is proof of a different subtitle.
    private static bool StoredEntryMayBeCanonicalDownload(SubtitleTrackInfo track, string? language) =>
        string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(track.Language) ||
        string.Equals(language, track.Language, StringComparison.OrdinalIgnoreCase);

    // Stored-list refreshes still in flight with a subtitle to select. Each is
    // offered the inventory an announcement's replan applies in place of the
    // session it read under.
    private readonly HashSet<Action<PlaybackStartResponse>> _storedSubtitleListRefreshes = [];

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
        List<SubtitleTrackInfo> readInventory;
        lock (manager.SubtitleInventorySyncRoot)
        {
            if (!ReferenceEquals(session, manager.CurrentSession)) return;
            readInventory = session.SubtitleUrls;
        }
        var acceptedInventory = readInventory;
        var activation = mayActivate ?? CaptureSubtitleActivationIntent();
        // A subtitle_ready merge appends canonical rows or rewrites one in place;
        // it retires nothing this refresh accepted. Any other replacement list does.
        bool InventoryCurrent()
        {
            lock (manager.SubtitleInventorySyncRoot)
            {
                var live = session.SubtitleUrls;
                return ReferenceEquals(acceptedInventory, live) ||
                    acceptedInventory.All(track => live.Contains(track)) &&
                    live.Except(acceptedInventory).All(track => !string.IsNullOrWhiteSpace(track.TrackId) &&
                        !string.IsNullOrWhiteSpace(track.Delivery));
            }
        }
        bool Current() => !_closing && !token.IsCancellationRequested && (isCurrent?.Invoke() ?? true) &&
            activation() &&
            ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(context) &&
            ReferenceEquals(manager, _playbackManager) && ReferenceEquals(session, manager.CurrentSession) &&
            InventoryCurrent() && nativeFile == Volatile.Read(ref _fileLoadGeneration) &&
            ReferenceEquals(mpv, _mpv) && content == ContentId && play == Volatile.Read(ref _playRequestGeneration) &&
            file == Volatile.Read(ref _playbackFileGeneration);
        if (!Current()) return;
        // An announcement's replan retires this response, not the selection this
        // refresh was asked to make. Once that inventory is applied, start over on
        // it under the activation intent captured here instead of dropping the pick.
        Action<PlaybackStartResponse> superseded = null!;
        superseded = applied =>
        {
            if (ReferenceEquals(applied, session) || applied.SessionId != session.SessionId ||
                applied.MediaFileId != mediaFileId) return;
            lock (_subtitleReadyRefreshLock)
                if (!_storedSubtitleListRefreshes.Remove(superseded)) return;
            DeferUntilSubtitleReadyIdle(() =>
            {
                // Every fence above except the response and the loaded file,
                // which the applied inventory was allowed to replace.
                if (!_closing && !token.IsCancellationRequested && (isCurrent?.Invoke() ?? true) && activation() &&
                    ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(context) &&
                    ReferenceEquals(manager, _playbackManager) && ReferenceEquals(mpv, _mpv) && content == ContentId &&
                    play == Volatile.Read(ref _playRequestGeneration) && liveJob == Volatile.Read(ref _liveSubtitleJobId) &&
                    manager.CurrentSession is { } current && current.SessionId == applied.SessionId &&
                    current.MediaFileId == mediaFileId)
                    _ = RefreshSubtitlesAfterAiAsync(mediaFileId, preferredSubtitleId, isCurrent, activation);
            });
        };
        if (preferredSubtitleId.HasValue)
            lock (_subtitleReadyRefreshLock) _storedSubtitleListRefreshes.Add(superseded);
        try
        {
            var response = await authority.PlaybackApi.GetSubtitlesAsync(mediaFileId, token, context).ConfigureAwait(false);
            if (!Current()) return;

            List<SubtitleTrackInfo> downloaded;
            lock (_subtitleSyncGate)
            lock (_subtitleTrackStateLock)
            lock (manager.SubtitleInventorySyncRoot)
            {
                if (!Current()) return;
                // An announcement may have merged while the list was read. Build
                // from the rows as they are now, under the gates that serialize
                // that merge, so its canonical facts are never judged stale.
                var previousInventory = session.SubtitleUrls;
                acceptedInventory = previousInventory;
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
                var displacedCanonical = new HashSet<SubtitleTrackInfo>();
                downloaded = response.Subtitles.Select((entry, offset) =>
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
                    // Older plans omit stored keys. Position alone cannot prove that a
                    // canonical row at this slot changed: retain it, without guessing
                    // a stored identity or creating an unplayable positional duplicate.
                    var unknownCanonical = previousDownloads.Where(track => track.Index == ordinal &&
                        IsCanonicalDownloadWithoutStoredIdentity(track)).Take(2).ToArray();
                    if (unknownCanonical.Length == 1)
                    {
                        // An entry that owns another row, or names another language,
                        // is not this row: it now holds the row's stored ordinal.
                        if (previous == null && StoredEntryMayBeCanonicalDownload(unknownCanonical[0], entry.Language))
                            return unknownCanonical[0];
                        displacedCanonical.Add(unknownCanonical[0]);
                    }
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
                // That evidence leaves every new positional URL from this list
                // uncertain too. Only a replan can make those rows executable.
                if (displacedCanonical.Count > 0)
                    foreach (var row in downloaded.Where(row => !previousDownloads.Contains(row)))
                    {
                        row.Unplayable = true;
                        row.Url = "";
                    }
                foreach (var entry in canonicalStoredIds) entry.Key.Id ??= entry.Value;
                // A displaced plan-only row keeps its identity and any bytes it
                // already loaded, never a URL that may now address another subtitle.
                foreach (var displaced in displacedCanonical.Where(IsCanonicalDownloadWithoutStoredIdentity))
                {
                    displaced.Unplayable |= !(_executedSubtitleFileId == mediaFileId && ReferenceEquals(_executedSubtitleTrack, displaced) ||
                        _loadedExternalSubtitleSids.GetValueOrDefault(displaced.Index) > 0 ||
                        _prefetchedSubtitleFiles.ContainsKey(displaced.Index));
                    displaced.Url = "";
                }

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
                    if (previousDownloads.Contains(refreshed)) return refreshed;
                    var matches = previousDownloads.Where(track => track.Id == refreshed.Id ||
                        track.SyncKey == refreshed.SyncKey).Take(2).ToArray();
                    if (matches.Length != 1 || downloaded.Count(track => track.Id == refreshed.Id ||
                        track.SyncKey == refreshed.SyncKey) != 1) return refreshed;
                    var retained = matches[0];
                    // An uncertain positional URL cannot retire bytes already
                    // loaded for this identity. Inspect caches before re-indexing.
                    var keepPlayable = !retained.Unplayable &&
                        (_executedSubtitleFileId == mediaFileId && ReferenceEquals(_executedSubtitleTrack, retained) ||
                         _loadedExternalSubtitleSids.GetValueOrDefault(retained.Index) > 0 ||
                         _prefetchedSubtitleFiles.ContainsKey(retained.Index));
                    var unplayable = refreshed.Unplayable && !keepPlayable;
                    retained.Index = refreshed.Index;
                    retained.MediaFileId = refreshed.MediaFileId;
                    retained.SyncKey = refreshed.SyncKey;
                    retained.Id = refreshed.Id;
                    retained.Unplayable = unplayable;
                    retained.Language = refreshed.Language;
                    retained.Codec = refreshed.Codec;
                    retained.Label = refreshed.Label;
                    retained.HearingImpaired = refreshed.HearingImpaired;
                    retained.Forced = refreshed.Forced;
                    retained.Url = refreshed.Url;
                    return retained;
                }).ToList();
                // A list read before an announcement landed cannot prove its row gone.
                RetainDownloadsAnnouncedDuringRead(readInventory, previousDownloads, downloaded);
                // Absence from a stored-ID list cannot retire a row without stored
                // keys. Both refresh entry points keep those rows by the same rule.
                RetainDownloadsWithoutStoredIdentity(mediaFileId, previousDownloads, reserved, downloaded, baseIndex);

                SubtitleTrackInfo? Rebind(SubtitleTrackInfo? track)
                {
                    if (track == null) return null;
                    if (existing.Contains(track)) return track.Unplayable ? null : track;
                    // A retained row without stored keys can only rebind to itself.
                    if (IsDownloadWithoutStoredIdentity(track) && downloaded.Contains(track)) return track.Unplayable ? null : track;
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
        finally
        {
            lock (_subtitleReadyRefreshLock) _storedSubtitleListRefreshes.Remove(superseded);
        }
    }

    // Shared by both stored-list refresh entry points, under their inventory
    // locks. A canonical row the read inventory did not hold was announced after
    // the list was requested, so the list cannot prove it gone. A plan accepted
    // during the read replaces every row object: compare identities, not rows.
    private static void RetainDownloadsAnnouncedDuringRead(List<SubtitleTrackInfo> readInventory,
        SubtitleTrackInfo[] previousDownloads, List<SubtitleTrackInfo> downloaded)
    {
        bool Known(SubtitleTrackInfo track) => readInventory.Any(read => ReferenceEquals(read, track) ||
            string.Equals(read.Source, "downloaded", StringComparison.OrdinalIgnoreCase) &&
            (track.Id is > 0 && read.Id == track.Id ||
             !string.IsNullOrWhiteSpace(track.SyncKey) && read.SyncKey == track.SyncKey ||
             !string.IsNullOrWhiteSpace(track.TrackId) && read.TrackId == track.TrackId));
        downloaded.AddRange(previousDownloads.Where(track => !string.IsNullOrWhiteSpace(track.TrackId) &&
            !string.IsNullOrWhiteSpace(track.Delivery) && !Known(track) && !downloaded.Contains(track)).ToArray());
    }

    // Shared by both stored-list refresh entry points, under their inventory
    // locks and before index-keyed caches are remapped. A stored-ID list cannot
    // prove a download without stored keys gone: keep its exact row. A row with
    // no canonical ordinal moves to a free index so no stored row shares it; its
    // positional URL addressed the old ordinal, so only loaded bytes stay usable.
    private void RetainDownloadsWithoutStoredIdentity(int mediaFileId, SubtitleTrackInfo[] previousDownloads,
        SubtitleTrackInfo[] reserved, List<SubtitleTrackInfo> downloaded, int baseIndex)
    {
        foreach (var legacy in previousDownloads.Where(track => IsDownloadWithoutStoredIdentity(track) && !downloaded.Contains(track)))
        {
            if (string.IsNullOrWhiteSpace(legacy.TrackId) || string.IsNullOrWhiteSpace(legacy.Delivery))
            {
                var free = downloaded.Concat(reserved).Select(track => track.Index).DefaultIfEmpty(baseIndex - 1).Max() + 1;
                if (free != legacy.Index)
                {
                    legacy.Unplayable |= !(_executedSubtitleFileId == mediaFileId && ReferenceEquals(_executedSubtitleTrack, legacy) ||
                        _loadedExternalSubtitleSids.GetValueOrDefault(legacy.Index) > 0 ||
                        _prefetchedSubtitleFiles.ContainsKey(legacy.Index));
                    legacy.Url = "";
                    legacy.Index = free;
                }
            }
            downloaded.Add(legacy);
        }
    }
}
