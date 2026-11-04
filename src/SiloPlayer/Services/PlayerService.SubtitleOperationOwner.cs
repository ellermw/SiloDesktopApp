using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    internal SubtitleOperationOwner? CaptureSubtitleOperationOwner(PlaybackStartResponse session)
    {
        var manager = _playbackManager;
        if (manager == null) return null;
        lock (manager.SubtitleInventorySyncRoot)
        {
            if (!ReferenceEquals(manager, _playbackManager) || !ReferenceEquals(session, manager.CurrentSession)) return null;
            return new(this, _activeAuthority, manager, session);
        }
    }

    internal sealed class SubtitleOperationOwner
    {
        private readonly PlayerService _player;
        private readonly PlaybackAuthorityScope _authority;
        private readonly ApiIdentityContext _identity;
        private readonly PlaybackManager _manager;
        private readonly long _playGeneration;
        private readonly string? _content;
        private readonly int _mediaFileId;
        private readonly long _mediaFileLifetimeGeneration;
        private readonly CancellationToken _token;
        private PlaybackStartResponse _session;

        internal SubtitleOperationOwner(PlayerService player, PlaybackAuthorityScope authority,
            PlaybackManager manager, PlaybackStartResponse session)
        {
            _player = player; _authority = authority; _manager = manager; _session = session;
            _identity = authority.ApiClient.CaptureIdentityContext();
            _playGeneration = Volatile.Read(ref player._playRequestGeneration);
            _content = player.ContentId; _mediaFileId = session.MediaFileId;
            _mediaFileLifetimeGeneration = manager.MediaFileLifetimeGeneration;
            _token = player._authorityLifetimeCts.Token;
        }

        private bool Stable => !_player._closing && !_token.IsCancellationRequested &&
            ReferenceEquals(_authority, _player._activeAuthority) && _authority.ApiClient.IsCurrentIdentityContext(_identity) &&
            ReferenceEquals(_manager, _player._playbackManager) && _playGeneration == Volatile.Read(ref _player._playRequestGeneration) &&
            _content == _player.ContentId;

        internal bool IsCurrent => Stable && ReferenceEquals(Volatile.Read(ref _session), _manager.CurrentSession);

        // AI jobs belong to the media file, not one seek/quality/audio plan.
        // Ordinary selections still require the exact accepted response below.
        internal bool IsCurrentForAi
        {
            get
            {
                lock (_manager.SubtitleInventorySyncRoot)
                {
                    if (!Stable || _mediaFileLifetimeGeneration != _manager.MediaFileLifetimeGeneration) return false;
                    var previous = Volatile.Read(ref _session);
                    var current = _manager.CurrentSession;
                    if (current == null || current.MediaFileId != _mediaFileId) return false;
                    Interlocked.CompareExchange(ref _session, current, previous);
                    return Stable && _mediaFileLifetimeGeneration == _manager.MediaFileLifetimeGeneration &&
                        ReferenceEquals(current, _manager.CurrentSession) &&
                        ReferenceEquals(current, Volatile.Read(ref _session));
                }
            }
        }

        internal bool CanUseSession(PlaybackStartResponse session) => Stable &&
            ReferenceEquals(session, Volatile.Read(ref _session)) && ReferenceEquals(session, _manager.CurrentSession);

        // Core invokes this only for this request's guarded, accepted response.
        // No UI/state gate is taken while the manager's publication gate is held.
        internal bool TryAdoptSession(PlaybackStartResponse previous, PlaybackStartResponse replacement) => Stable &&
            ReferenceEquals(replacement, _manager.CurrentSession) &&
            (ReferenceEquals(Interlocked.CompareExchange(ref _session, replacement, previous), previous) ||
             ReferenceEquals(Volatile.Read(ref _session), replacement)) && Stable;
    }
}
