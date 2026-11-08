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
    private SubtitleReadOperation? BeginSubtitleRead(Func<bool>? current = null)
    {
        SubtitleReadOperation next;
        SubtitleReadOperation? previous;
        lock (_subtitleTrackStateLock)
        {
            if (current?.Invoke() == false) return null;
            next = new SubtitleReadOperation();
            previous = Interlocked.Exchange(ref _subtitleReadOperation, next);
        }
        // Cancellation can run callbacks synchronously. Neither callbacks nor
        // prefetch I/O may run while owning the subtitle state lock.
        previous?.Cancel();
        return next;
    }
    private void CancelSubtitleSelectionRead() => Volatile.Read(ref _subtitleReadOperation)?.Cancel();

    private enum SubtitlePrefetchOutcome { Ready, Fallback, Busy, Error, DownloadLimit, BitmapDownloadLimit, Timeout, Retired }

    private static bool IsSubtitleReadFailure(SubtitlePrefetchOutcome outcome)
        => outcome is SubtitlePrefetchOutcome.Busy or SubtitlePrefetchOutcome.Error or SubtitlePrefetchOutcome.DownloadLimit
            or SubtitlePrefetchOutcome.BitmapDownloadLimit or SubtitlePrefetchOutcome.Timeout;

    private void ShowSubtitleReadFailure(SubtitlePrefetchOutcome outcome)
    {
        if (outcome == SubtitlePrefetchOutcome.Busy)
            ShowSubtitleReadBusy();
        else if (IsSubtitleReadFailure(outcome))
        {
            var downloadError = outcome switch
            {
                SubtitlePrefetchOutcome.DownloadLimit => "This subtitle exceeds the 16 MiB download limit. Choose another subtitle or turn subtitles off.",
                SubtitlePrefetchOutcome.BitmapDownloadLimit => "This subtitle exceeds the 256 MiB download limit. Choose another subtitle or turn subtitles off.",
                SubtitlePrefetchOutcome.Timeout => "Subtitle download timed out. The server may still be extracting the track. Try again or choose another subtitle.",
                _ => "The subtitle could not be downloaded. Try again or choose another subtitle after checking your connection and available disk space.",
            };
            _mpv?.SendScriptMessage("osc-set-active-subtitle", _activeSubtitleServerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ShowNotice("Subtitle could not be loaded", downloadError, "warning");
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
