using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private sealed record LegacySubtitleChoice(int Ordinal, PlaybackAuthorityScope Authority,
        ApiIdentityContext Identity, PlaybackManager Manager, PlaybackStartResponse Session,
        long PlayGeneration, string? Content, SubtitleSelectionOperation? Selection, CancellationToken Token);
    private LegacySubtitleChoice? _legacySubtitleChoice;

    // Only a metadata-free legacy response can retain a UI ordinal. Resolve
    // actual native IDs after FILE_LOADED; populated inventories use identity.
    private bool TryApplyLegacyInitialSubtitleChoice(long generation, CancellationTokenSource ownerCts)
    {
        lock (_subtitleTrackStateLock)
        {
            // A newer FILE_LOADED may install its choice after the caller's
            // check. An old initializer must not even consume that new record.
            if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return true;
            var choice = _legacySubtitleChoice;
            if (choice is null) return false;
            _legacySubtitleChoice = null;
            _pendingSubtitleSelection = null;
            if (choice.Token.IsCancellationRequested || _closing || _mpv is null ||
                !ReferenceEquals(choice.Authority, _activeAuthority) ||
                !choice.Authority.ApiClient.IsCurrentIdentityContext(choice.Identity) ||
                !ReferenceEquals(choice.Manager, _playbackManager) ||
                !ReferenceEquals(choice.Session, choice.Manager.CurrentSession) ||
                choice.PlayGeneration != Volatile.Read(ref _playRequestGeneration) || choice.Content != ContentId ||
                !ReferenceEquals(choice.Selection, Volatile.Read(ref _subtitleSelectionOperation)) ||
                choice.Session.SubtitleUrls.Count != 0 || choice.Session.IsSubtitleBurnedIn)
                return true;
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
