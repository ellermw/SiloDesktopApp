namespace SiloPlayer.Services;

public partial class PlayerService
{
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
