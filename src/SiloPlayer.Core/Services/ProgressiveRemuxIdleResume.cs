using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public sealed class ProgressiveRemuxIdleResume
{
    private readonly object _gate = new();
    private DateTimeOffset? _pausedSince;
    private bool _isReopening;
    private bool _desiredPaused;
    public bool IsReopening { get { lock (_gate) return _isReopening; } }
    public bool DesiredPaused { get { lock (_gate) return _desiredPaused; } }
    public void ObservePause(bool paused, bool internalTransition, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (internalTransition || _isReopening) return;
            if (paused) _pausedSince ??= now;
            else _pausedSince = null;
        }
    }
    public bool TryBeginResume(PlaybackTransportKind kind, bool canSeekAnywhere, bool roomPlayback, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_isReopening || kind != PlaybackTransportKind.RemuxProgressive || canSeekAnywhere || roomPlayback
                || _pausedSince is not { } since || now - since < TimeSpan.FromSeconds(60)) return false;
            _pausedSince = null;
            _isReopening = true;
            _desiredPaused = false;
            return true;
        }
    }
    public void SetPauseIntent(bool paused) { lock (_gate) _desiredPaused = paused; }
    public void Reset() { lock (_gate) { _isReopening = false; _pausedSince = null; _desiredPaused = false; } }
    public void ResetPauseClock() { lock (_gate) _pausedSince = null; }
    public bool? Complete()
    {
        lock (_gate)
        {
            if (!_isReopening) return null;
            bool? paused = _desiredPaused;
            _isReopening = false;
            // A restored paused load may leave mpv paused throughout, with no
            // new pause event to establish the next idle window.
            _pausedSince = _desiredPaused ? DateTimeOffset.UtcNow : null;
            _desiredPaused = false;
            return paused;
        }
    }
}
