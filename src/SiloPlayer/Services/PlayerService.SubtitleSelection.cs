using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private sealed record PreparedSubtitleReload(SubtitlePreferenceOwner Owner, int MediaFileId, int Index,
        string TrackId, string? SyncKey, string Path, long PlayRequest, string? Content,
        SiloPlayer.Core.Api.ApiRequestContext Context);
    private PreparedSubtitleReload? _preparedSubtitleReload;

    private void PrepareSubtitleFileForReload(PlaybackStartResponse previous, PlaybackStartResponse granted, int? index)
    {
        var owner = _pendingSubtitlePreferenceOwner;
        if (owner == null) return;
        var context = owner.Context;
        var after = granted.SubtitleUrls.FirstOrDefault(t => !t.Unplayable && t.Index == index);
        var before = after == null || string.IsNullOrEmpty(after.TrackId) ? null :
            previous.SubtitleUrls.FirstOrDefault(t => !t.Unplayable && t.TrackId == after.TrackId);
        // A track-change may fall back to another file. Never carry bytes by
        // ordinal across that boundary or assume a new grant kept its identity.
        if (!_activeAuthority.ApiClient.IsCurrentContext(context) ||
            before == null || after == null || previous.MediaFileId != granted.MediaFileId ||
            string.IsNullOrEmpty(before.TrackId) || before.TrackId != after.TrackId || before.SyncKey != after.SyncKey) return;
        lock (_subtitleTrackStateLock)
        {
            if (!_prefetchedSubtitleFiles.TryGetValue(before.Index, out var path)) return;
            _preparedSubtitleReload = new(owner, granted.MediaFileId, after.Index, after.TrackId!, after.SyncKey, path,
                Volatile.Read(ref _playRequestGeneration), ContentId, context);
        }
    }

    // Called under the subtitle file-state lock. This cache handoff is consumed
    // exactly once by the matching loaded-file initializer.
    private PreparedSubtitleReload? TakePreparedSubtitleReload()
    {
        var prepared = _preparedSubtitleReload; _preparedSubtitleReload = null;
        if (prepared == null || !ReferenceEquals(prepared.Owner, _pendingSubtitlePreferenceOwner) ||
            prepared.Owner.Token.IsCancellationRequested || prepared.PlayRequest != Volatile.Read(ref _playRequestGeneration) ||
            prepared.Content != ContentId || !_activeAuthority.ApiClient.IsCurrentContext(prepared.Context) ||
            _playbackManager?.CurrentSession is not { } session || session.MediaFileId != prepared.MediaFileId ||
            !session.SubtitleUrls.Any(t => !t.Unplayable && t.Index == prepared.Index && t.TrackId == prepared.TrackId && t.SyncKey == prepared.SyncKey) ||
            !File.Exists(prepared.Path)) return null;
        return prepared;
    }
    private sealed class SubtitleReadOperation : IDisposable
    {
        private readonly CancellationTokenSource source = new();
        public CancellationToken Token { get; }
        public SubtitleReadOperation() => Token = source.Token;
        public void Cancel() { try { source.Cancel(); } catch (ObjectDisposedException) { } }
        public void Dispose() => source.Dispose();
    }
    private SubtitleReadOperation? _subtitleReadOperation;
    private SubtitleReadOperation BeginSubtitleRead()
    {
        var next = new SubtitleReadOperation();
        Interlocked.Exchange(ref _subtitleReadOperation, next)?.Cancel();
        return next;
    }
    private void CancelSubtitleSelectionRead() => Volatile.Read(ref _subtitleReadOperation)?.Cancel();

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
