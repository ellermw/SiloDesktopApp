using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    // Equal indices still create different pending owners (A -> B -> A).
    private sealed class SubtitleSelectionOperation : IDisposable
    {
        private readonly CancellationTokenSource source = new();
        public CancellationToken Token { get; }
        public SiloPlayer.Core.Api.ApiRequestContext? Context { get; set; }
        public SubtitlePreferenceOwner? PreferenceOwner { get; set; }
        public SubtitleTrackInfo? RequestedTrack { get; set; }
        public int? SourceFile { get; set; }
        public long PlayRequest { get; set; }
        public string? Content { get; set; }
        public bool Accepted { get; set; }
        public int PreferenceClaimed;
        public SubtitleSelectionOperation() => Token = source.Token;
        public void Cancel() { try { source.Cancel(); } catch (ObjectDisposedException) { } }
        public void Dispose() => source.Dispose();
    }
    private SubtitleSelectionOperation? _subtitleSelectionOperation;
    private SubtitleSelectionOperation? _subtitleReadOperation;
    private sealed record PreparedSubtitleReload(SubtitleSelectionOperation? Owner, int MediaFileId, int Index,
        string? TrackId, string? SyncKey, long? Id, string Path, long PlayRequest, string? Content,
        SiloPlayer.Core.Api.ApiRequestContext Context);
    private PreparedSubtitleReload? _preparedSubtitleReload;

    private void PrepareSubtitleFileForReload(PlaybackStartResponse previous, PlaybackStartResponse granted, int? index)
    {
        var after = granted.SubtitleUrls.FirstOrDefault(t => !t.Unplayable && t.Index == index);
        var matches = after == null ? Array.Empty<SubtitleTrackInfo>() : previous.SubtitleUrls.Where(t => !t.Unplayable &&
            HasSameSubtitleReloadIdentity(after.TrackId, after.SyncKey, after.Id, t)).Take(2).ToArray();
        PrepareSubtitleReload(previous.MediaFileId, matches.Length == 1 ? matches[0] : null, granted, after);
    }

    private void PrepareAppliedSubtitleFileForReload()
    {
        // Every replacement load, including audio/quality/recovery, can carry
        // already applied bytes once when the granted identity is unchanged.
        if (_preparedSubtitleReload != null || _playbackManager?.CurrentSession is not { } granted ||
            _activeSubtitleServerIndex < 0 || _executedSubtitleContext is not { } context ||
            !_activeAuthority.ApiClient.IsCurrentContext(context)) return;
        var after = granted.SubtitleUrls.FirstOrDefault(t => !t.Unplayable &&
            t.Index == (_pendingInitialServerSubtitleIndex ?? _playbackManager.SubtitleSelectionForLoad));
        PrepareSubtitleReload(_executedSubtitleFileId, _executedSubtitleTrack, granted, after);
    }

    private void PrepareSubtitleReload(int? previousFileId, SubtitleTrackInfo? before,
        PlaybackStartResponse granted, SubtitleTrackInfo? after)
    {
        var owner = Volatile.Read(ref _subtitleSelectionOperation);
        var context = owner?.Context ?? _executedSubtitleContext;
        // Never carry bytes by ordinal across a file or authority boundary.
        // An automatic applied selection has no manual owner, but owns bytes.
        if (context is not { } captured || !_activeAuthority.ApiClient.IsCurrentContext(captured) ||
            before == null || after == null || previousFileId != granted.MediaFileId ||
            before.Unplayable || after.Unplayable ||
            !HasSameSubtitleReloadIdentity(before.TrackId, before.SyncKey, before.Id, after)) return;
        lock (_subtitleTrackStateLock)
        {
            if (!_prefetchedSubtitleFiles.TryGetValue(before.Index, out var path)) return;
            _preparedSubtitleReload = new(owner, granted.MediaFileId, after.Index, after.TrackId, after.SyncKey, after.Id, path,
                Volatile.Read(ref _playRequestGeneration), ContentId, captured);
        }
    }

    private static bool HasSameSubtitleReloadIdentity(string? trackId, string? syncKey, long? id, SubtitleTrackInfo track)
    {
        // Refreshed downloads can acquire a v3 TrackId later. A shared stored
        // identity is sufficient, but conflicting identities must never carry.
        if ((!string.IsNullOrEmpty(trackId) && !string.IsNullOrEmpty(track.TrackId) && trackId != track.TrackId) ||
            syncKey != track.SyncKey || (id is > 0 && track.Id is > 0 && id != track.Id)) return false;
        return (!string.IsNullOrEmpty(trackId) && trackId == track.TrackId) ||
            !string.IsNullOrEmpty(syncKey) || (id is > 0 && id == track.Id);
    }

    // Called under the subtitle file-state lock. This cache handoff is consumed
    // exactly once by the matching loaded-file initializer.
    private PreparedSubtitleReload? TakePreparedSubtitleReload()
    {
        var prepared = _preparedSubtitleReload; _preparedSubtitleReload = null;
        if (prepared == null || !ReferenceEquals(prepared.Owner, Volatile.Read(ref _subtitleSelectionOperation)) ||
            prepared.Owner?.Token.IsCancellationRequested == true || prepared.PlayRequest != Volatile.Read(ref _playRequestGeneration) ||
            prepared.Content != ContentId || !_activeAuthority.ApiClient.IsCurrentContext(prepared.Context) ||
            _playbackManager?.CurrentSession is not { } session || session.MediaFileId != prepared.MediaFileId ||
            !session.SubtitleUrls.Any(t => !t.Unplayable && t.Index == prepared.Index &&
                HasSameSubtitleReloadIdentity(prepared.TrackId, prepared.SyncKey, prepared.Id, t)) ||
            !File.Exists(prepared.Path)) return null;
        return prepared;
    }
    private SubtitleSelectionOperation BeginSubtitleSelection()
    {
        var next = new SubtitleSelectionOperation();
        var previous = Interlocked.Exchange(ref _subtitleSelectionOperation, next);
        previous?.Cancel(); previous?.Dispose();
        CancelSubtitleSelectionRead();
        return next;
    }
    private SubtitleSelectionOperation BeginSubtitleRead()
    {
        var next = new SubtitleSelectionOperation();
        Interlocked.Exchange(ref _subtitleReadOperation, next)?.Cancel();
        return next;
    }
    private void CancelSubtitleSelectionRead() => Volatile.Read(ref _subtitleReadOperation)?.Cancel();

    private bool IsCurrentSubtitleChoice(SubtitleSelectionOperation owner) =>
        ReferenceEquals(owner, Volatile.Read(ref _subtitleSelectionOperation)) && !owner.Token.IsCancellationRequested &&
        !_closing && owner.PlayRequest == Volatile.Read(ref _playRequestGeneration) && owner.Content == ContentId &&
        owner.PreferenceOwner is { } preference && !preference.Token.IsCancellationRequested &&
        ReferenceEquals(preference.Authority, _activeAuthority) && ReferenceEquals(preference.Manager, _playbackManager) &&
        preference.Authority.ApiClient.IsCurrentContext(preference.Context);

    private void RefusePendingSubtitleSelectionWithoutLoad()
    {
        // No FILE_LOADED will consume a pick queued during an aborted/no-op
        // switch. Keep the applied choice, and never replay this pick on an
        // unrelated later load or report an unapplied preference as saved.
        if (_switchingContent || IsLoading || !_pendingInitialServerSubtitleIndex.HasValue ||
            Volatile.Read(ref _subtitleSelectionOperation) is not { Accepted: true } owner ||
            !IsCurrentSubtitleChoice(owner)) return;
        RestoreExecutedSubtitleSelection(reapply: false);
        ShowSubtitleSelectionUnavailable();
    }

    private async Task CompleteExecutedSubtitleSelectionAsync(SubtitleSelectionOperation? owner, SubtitleTrackInfo? applied)
    {
        if (!ReferenceEquals(owner, Volatile.Read(ref _subtitleSelectionOperation))) return;
        _activeSubtitleServerIndex = applied?.Index ?? -1;
        CommitExecutedSubtitleSelection(applied);
        _mpv?.SendScriptMessage("osc-set-active-subtitle", _activeSubtitleServerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        PublishSubtitlePreferenceStatus();
        if (owner == null || !IsCurrentSubtitleChoice(owner) || !owner.Accepted) return;
        var expected = owner.RequestedTrack == null ? null : ResolveReloadSubtitleChoice(owner.RequestedTrack, owner.SourceFile);
        if ((owner.RequestedTrack != null && expected == null) || expected?.Index != applied?.Index) return;
        // Reserve before awaiting so repeated initializers cannot duplicate an
        // in-flight write. Only success consumes the claim; failures can retry.
        if (Interlocked.CompareExchange(ref owner.PreferenceClaimed, 1, 0) != 0) return;
        // A newer pending pick cancels reads, not the preference for captions
        // already on screen. Authority/profile retirement still owns the send.
        if (!await SaveExecutedSubtitlePreferenceAsync(owner.PreferenceOwner, applied))
            Volatile.Write(ref owner.PreferenceClaimed, 0);
    }

    private SubtitleTrackInfo? ResolveReloadSubtitleChoice(SubtitleTrackInfo requested, int? sourceFile)
    {
        var session = _playbackManager?.CurrentSession;
        var tracks = session?.SubtitleUrls.ToList();
        if (tracks == null) return null;
        if (!string.IsNullOrEmpty(requested.TrackId))
        {
            var exact = tracks.Where(t => t.TrackId == requested.TrackId).ToList();
            if (exact.Count > 0) return exact.Count == 1 && !exact[0].Unplayable ? exact[0] : null;
            if (session!.MediaFileId == sourceFile) return null;
        }
        if (session!.MediaFileId == sourceFile)
        {
            // Stored/downloaded identities must not borrow a reused ordinal.
            if (requested.Id is > 0 || !string.IsNullOrEmpty(requested.SyncKey))
            {
                var exact = tracks.Where(t => requested.Id is > 0 ? t.Id == requested.Id : t.SyncKey == requested.SyncKey).ToList();
                // Refreshed downloads have a stored Id, but a new v3 grant
                // carries that identity only in SyncKey. Never use the ordinal
                // or discard duplicate/unavailable identities to make a match.
                if (exact.Count == 0 && !string.IsNullOrEmpty(requested.SyncKey))
                    exact = tracks.Where(t => t.SyncKey == requested.SyncKey).ToList();
                return exact.Count == 1 && !exact[0].Unplayable &&
                    (requested.Id is not > 0 || exact[0].Id is not > 0 || exact[0].Id == requested.Id) ? exact[0] : null;
            }
            return tracks.FirstOrDefault(t => !t.Unplayable && t.Index == requested.Index);
        }
        tracks = tracks.Where(t => !t.Unplayable).ToList();
        var index = SubtitleAutoSelect.Resolve(new("always", SubtitleAutoSelect.BuildCandidates(tracks),
            requested.Language, null, null, true, new SubtitleTrackSignature
            {
                Source = requested.Source ?? "embedded", Language = requested.Language, Codec = requested.Codec,
                Label = requested.Label, Forced = requested.Forced, HearingImpaired = requested.HearingImpaired,
            }));
        return tracks.FirstOrDefault(t => t.Index == index);
    }

    private enum SubtitlePrefetchOutcome { Ready, Fallback, Busy, Error, Retired }

    private void ShowSubtitleReadFailure(SubtitlePrefetchOutcome outcome)
    {
        if (outcome == SubtitlePrefetchOutcome.Busy)
            ShowSubtitleReadBusy();
        else if (outcome == SubtitlePrefetchOutcome.Error)
        {
            _mpv?.SendScriptMessage("osc-set-active-subtitle", _activeSubtitleServerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ShowNotice("Subtitle could not be loaded", "This subtitle could not be loaded. Choose another subtitle or try again after checking your connection and available disk space.", "warning");
        }
    }

    private void ShowSubtitleReadBusy()
    {
        // OSC shortcuts optimistically highlight a pending choice. Reassert
        // the accepted choice without changing native SID or preference.
        _mpv?.SendScriptMessage("osc-set-active-subtitle", _activeSubtitleServerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ShowNotice("Subtitle still preparing", "The server is still preparing this subtitle. Try again shortly.", "warning");
    }
}
