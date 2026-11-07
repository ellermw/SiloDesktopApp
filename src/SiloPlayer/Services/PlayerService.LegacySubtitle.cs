using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private sealed record LegacySubtitleChoice(SubtitleTrackInfo Track, PlaybackAuthorityScope Authority,
        ApiIdentityContext Identity, PlaybackManager Manager, PlaybackStartResponse Session,
        long PlayGeneration, string? Content, long SelectionVersion, CancellationToken Token);
    private LegacySubtitleChoice? _legacySubtitleChoice;
    private long _legacySubtitleChoiceVersion;

    // Empty response inventories admit only a unique native metadata match.
    // Watch-detail ordinals never address the embedded-only native inventory.
    private bool TryApplyLegacyInitialSubtitleChoice(long generation, CancellationTokenSource ownerCts)
    {
        lock (_subtitleTrackStateLock)
        {
            // An initializer may have waited for this gate while a newer load
            // published its choice. Retire the caller before consuming that field.
            if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return true;
            var choice = _legacySubtitleChoice;
            if (choice == null) return false;
            _legacySubtitleChoice = null;
            _pendingSubtitleSelection = null;
            lock (choice.Manager.SubtitleInventorySyncRoot)
            {
                if (choice.Token.IsCancellationRequested || _closing || _mpv == null ||
                    !ReferenceEquals(choice.Authority, _activeAuthority) ||
                    !choice.Authority.ApiClient.IsCurrentIdentityContext(choice.Identity) ||
                    !ReferenceEquals(choice.Manager, _playbackManager) ||
                    !ReferenceEquals(choice.Session, choice.Manager.CurrentSession) ||
                    choice.PlayGeneration != Volatile.Read(ref _playRequestGeneration) || choice.Content != ContentId ||
                    choice.SelectionVersion != Volatile.Read(ref _legacySubtitleChoiceVersion) ||
                    choice.Session.SubtitleUrls.Count != 0 || choice.Session.IsSubtitleBurnedIn) return true;
                var track = choice.Track;
                var sid = CanUseNativeEmbeddedSubtitleTrack()
                    ? NativeSubtitleSelection.Resolve(new(0, null, null, track.Codec, track.Language,
                        track.NativeTitle, track.Forced, track.HearingImpaired), _mpv.GetEmbeddedSubtitleTracks(), false)
                    : 0;
                if (sid <= 0)
                {
                    ShowSubtitleSelectionUnavailable();
                    return true;
                }
                var previousSid = (int)Math.Round(_mpv.GetPropertyDouble("sid"));
                _mpv.SetSubtitleTrack(sid);
                if ((int)Math.Round(_mpv.GetPropertyDouble("sid")) != sid)
                {
                    _mpv.SetSubtitleTrack(previousSid);
                    ShowSubtitleSelectionUnavailable();
                    return true;
                }
                // Publish the demonstrated legacy row so subsequent loads,
                // presentation and local intent all refer to the executed choice.
                choice.Session.SubtitleUrls.Add(track);
                choice.Manager.RecordLocalSubtitleSelection(track.Index);
                _activeSubtitleServerIndex = track.Index;
                _pendingInitialServerSubtitleIndex = null;
                CommitExecutedSubtitleSelection(track);
            }
        }
        // Publication can take the sync gate; do not invert its lock order.
        SendSubtitleListToOsc();
        return true;
    }
}
