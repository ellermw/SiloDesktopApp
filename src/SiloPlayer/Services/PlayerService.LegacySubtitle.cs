using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private sealed record LegacySubtitleChoice(int Ordinal, PlaybackAuthorityScope Authority,
        ApiIdentityContext Identity, PlaybackManager Manager, PlaybackStartResponse Session,
        long PlayGeneration, string? Content, long SelectionVersion, CancellationToken Token);
    private LegacySubtitleChoice? _legacySubtitleChoice;
    private long _legacySubtitleChoiceVersion;

    // Empty response inventories alone admit the legacy ordinal bridge. The
    // loaded native inventory supplies the SID; populated inventories use identity.
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
                    choice.Session.SubtitleUrls.Count != 0 || choice.Session.IsSubtitleBurnedIn ||
                    !CanUseNativeEmbeddedSubtitleTrack()) return true;
                var tracks = _mpv.GetEmbeddedSubtitleTracks();
                if (choice.Ordinal < 0 || choice.Ordinal >= tracks.Count)
                {
                    ShowSubtitleSelectionUnavailable();
                    return true;
                }
                _mpv.SetSubtitleTrack(tracks[choice.Ordinal].Id);
                return true;
            }
        }
    }
}
