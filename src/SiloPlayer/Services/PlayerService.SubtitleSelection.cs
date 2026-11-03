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
        public SubtitleSelectionOperation() => Token = source.Token;
        public void Cancel() { try { source.Cancel(); } catch (ObjectDisposedException) { } }
        public void Dispose() => source.Dispose();
    }
    private SubtitleSelectionOperation? _subtitleSelectionOperation;
    private SubtitleSelectionOperation? _subtitleReadOperation;
    private sealed record PreparedSubtitleReload(SubtitleSelectionOperation Owner, int MediaFileId, int Index,
        string TrackId, string? SyncKey, string Path, long PlayRequest, string? Content,
        SiloPlayer.Core.Api.ApiRequestContext Context);
    private PreparedSubtitleReload? _preparedSubtitleReload;

    private void PrepareSubtitleFileForReload(PlaybackStartResponse previous, PlaybackStartResponse granted, int? index)
    {
        var owner = Volatile.Read(ref _subtitleSelectionOperation);
        var after = granted.SubtitleUrls.FirstOrDefault(t => t.Index == index);
        var before = after == null || string.IsNullOrEmpty(after.TrackId) ? null :
            previous.SubtitleUrls.FirstOrDefault(t => t.TrackId == after.TrackId);
        // A track-change may fall back to another file. Never carry bytes by
        // ordinal across that boundary or assume a new grant kept its identity.
        if (owner == null || owner.Context is not { } context || !_activeAuthority.ApiClient.IsCurrentContext(context) ||
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
        if (prepared == null || !ReferenceEquals(prepared.Owner, Volatile.Read(ref _subtitleSelectionOperation)) ||
            prepared.Owner.Token.IsCancellationRequested || prepared.PlayRequest != Volatile.Read(ref _playRequestGeneration) ||
            prepared.Content != ContentId || !_activeAuthority.ApiClient.IsCurrentContext(prepared.Context) ||
            _playbackManager?.CurrentSession is not { } session || session.MediaFileId != prepared.MediaFileId ||
            !session.SubtitleUrls.Any(t => t.Index == prepared.Index && t.TrackId == prepared.TrackId && t.SyncKey == prepared.SyncKey) ||
            !File.Exists(prepared.Path)) return null;
        return prepared;
    }
    private SubtitleSelectionOperation BeginSubtitleSelection()
    {
        var next = new SubtitleSelectionOperation();
        Interlocked.Exchange(ref _subtitleSelectionOperation, next)?.Cancel();
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

    private SubtitleTrackInfo? ResolveReloadSubtitleChoice(SubtitleTrackInfo requested, int? sourceFile)
    {
        var session = _playbackManager?.CurrentSession;
        var tracks = session?.SubtitleUrls;
        if (tracks == null) return null;
        var exact = tracks.FirstOrDefault(t => !string.IsNullOrEmpty(requested.TrackId) && t.TrackId == requested.TrackId);
        if (exact != null) return exact;
        if (session!.MediaFileId == sourceFile)
            return tracks.FirstOrDefault(t => t.Index == requested.Index);
        var index = SubtitleAutoSelect.Resolve(new("always", SubtitleAutoSelect.BuildCandidates(tracks),
            requested.Language, null, null, true, new SubtitleTrackSignature
            {
                Source = requested.Source ?? "embedded", Language = requested.Language, Codec = requested.Codec,
                Label = requested.Label, Forced = requested.Forced, HearingImpaired = requested.HearingImpaired,
            }));
        return tracks.FirstOrDefault(t => t.Index == index);
    }

    private void ShowSubtitleReadBusy()
    {
        // OSC shortcuts optimistically highlight a pending choice. Reassert
        // the accepted choice without changing native SID or preference.
        _mpv?.SendScriptMessage("osc-set-active-subtitle", _activeSubtitleServerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ShowNotice("Subtitle still preparing", "The server is still preparing this subtitle. Try again shortly.", "warning");
    }
}
