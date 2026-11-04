using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public partial class PlaybackManager
{
    private readonly object _trackIntentGate = new();
    private bool _hasViewerSubtitle;
    private bool _subtitleSelectionIsAutomatic;
    private SubtitleTrackInfo? _viewerSubtitle;
    private int _viewerSubtitleFile;
    private long _subtitleRevision, _subtitleSentRevision;
    private AudioTrackInfo? _viewerAudio;
    private bool _hasViewerAudio;
    private int _viewerAudioIndex, _viewerAudioFile;
    private long _audioRevision, _audioSentRevision;

    public sealed record SubtitleSelectionIntent(bool HasSelection, SubtitleTrackInfo? Track,
        int MediaFileId, bool IsAutomatic);

    public SubtitleSelectionIntent CaptureLocalSubtitleIntent()
    {
        lock (_trackIntentGate)
            return new(_hasViewerSubtitle, _viewerSubtitle, _viewerSubtitleFile, _subtitleSelectionIsAutomatic);
    }

    /// <summary>Rolls back an unexecuted pick without inventing an explicit Off choice.</summary>
    public void RestoreLocalSubtitleIntent(SubtitleSelectionIntent intent)
    {
        lock (_trackIntentGate)
        {
            _hasViewerSubtitle = intent.HasSelection;
            _viewerSubtitle = intent.Track;
            _viewerSubtitleFile = intent.MediaFileId;
            _subtitleSelectionIsAutomatic = intent.IsAutomatic;
            ++_subtitleRevision;
        }
    }

    public bool IsSubtitleSelectionAutomatic
    {
        get { lock (_trackIntentGate) return !_hasViewerSubtitle || _subtitleSelectionIsAutomatic; }
    }

    /// <summary>Records a local choice, retaining whether startup policy or the viewer selected it.</summary>
    public void RecordLocalSubtitleSelection(int? subtitleIndex, bool isAutomatic = false)
    {
        lock (_trackIntentGate)
        {
            var track = subtitleIndex.HasValue ? CurrentSession?.SubtitleUrls.FirstOrDefault(t => t.Index == subtitleIndex) : null;
            if (subtitleIndex.HasValue && track == null) throw new InvalidOperationException("The selected subtitle is no longer in the active inventory.");
            if (track?.Unplayable == true) throw new InvalidOperationException("The selected subtitle is unavailable.");
            RecordLocalSubtitleSelectionCore(track, isAutomatic);
        }
    }

    /// <summary>Admits a resolved choice only while its exact session and row remain current.</summary>
    public bool TryRecordLocalSubtitleSelection(PlaybackStartResponse session, SubtitleTrackInfo? track, bool isAutomatic = false)
    {
        lock (_trackIntentGate)
        {
            if (!ReferenceEquals(session, CurrentSession) || track != null &&
                (track.Unplayable || !session.SubtitleUrls.Contains(track))) return false;
            RecordLocalSubtitleSelectionCore(track, isAutomatic);
            return true;
        }
    }

    private void RecordLocalSubtitleSelectionCore(SubtitleTrackInfo? track, bool isAutomatic)
    {
        _hasViewerSubtitle = true; _viewerSubtitle = track;
        _subtitleSelectionIsAutomatic = isAutomatic;
        _viewerSubtitleFile = CurrentSession?.MediaFileId ?? 0;
        ++_subtitleRevision;
    }

    public void RecordLocalAudioSelection(int audioIndex)
    {
        lock (_trackIntentGate)
        {
            _viewerAudio = WatchDetail?.Versions.FirstOrDefault(v => v.FileId == CurrentSession?.MediaFileId)
                ?.AudioTracks?.ElementAtOrDefault(audioIndex);
            _hasViewerAudio = true; _viewerAudioIndex = audioIndex; _viewerAudioFile = CurrentSession?.MediaFileId ?? 0;
            ++_audioRevision;
        }
    }

    public int AudioSelectionForLoad
    {
        get { lock (_trackIntentGate) return _audioRevision > _audioSentRevision
            ? ReplacementAudioIndex(CurrentSession?.MediaFileId ?? 0) ?? CurrentSession?.AudioTrackIndex ?? 0
            : CurrentSession?.AudioTrackIndex ?? 0; }
    }

    /// <summary>A newer viewer pick wins over a plan reply still in flight.</summary>
    public int SubtitleSelectionForLoad
    {
        get
        {
            lock (_trackIntentGate)
            {
                if (_hasViewerSubtitle && _subtitleRevision > _subtitleSentRevision)
                {
                    var desired = MatchCurrentSubtitle();
                    if (desired != null || !_subtitleSelectionIsAutomatic || _viewerSubtitle == null)
                        return desired?.Index ?? -1;
                    // Seek replies cannot acknowledge an unsent revision, but
                    // an unmatched Auto row still must not become explicit Off.
                }
                return GrantedSubtitleSelection()?.Index ?? -1;
            }
        }
    }

    private void ResetLocalTrackIntent()
    {
        lock (_trackIntentGate)
        {
            _hasViewerSubtitle = _hasViewerAudio = false; _viewerSubtitle = null; _viewerAudio = null;
            _subtitleSelectionIsAutomatic = false;
            ++_subtitleRevision; _subtitleSentRevision = _subtitleRevision;
            ++_audioRevision; _audioSentRevision = _audioRevision;
        }
    }
    private long CaptureSubtitleRevision() { lock (_trackIntentGate) return _subtitleRevision; }
    private void AcknowledgeGrantedSubtitleIntent(long revision)
    {
        lock (_trackIntentGate)
        {
            if (!_hasViewerSubtitle)
            {
                AcknowledgeSubtitleIntent(revision);
                return;
            }
            var desired = MatchCurrentSubtitle();
            // An automatic row that cannot uniquely rebind is not a viewer
            // request for Off, even on a same-file replan. Accept the Auto grant,
            // retaining the original row so a later inventory can rebind it.
            // A same-file automatic Off still needs its own acknowledgement.
            if (_subtitleSelectionIsAutomatic && desired == null &&
                (_viewerSubtitle != null || CurrentSession?.MediaFileId != _viewerSubtitleFile))
            {
                AcknowledgeSubtitleIntent(revision);
                return;
            }
            var granted = GrantedSubtitleSelection();
            if (ReferenceEquals(desired, granted) &&
                (desired != null || string.Equals(CurrentSession?.SubtitleMode, "off", StringComparison.OrdinalIgnoreCase)))
                AcknowledgeSubtitleIntent(revision);
        }
    }
    private bool HasPendingSubtitleSelection()
    {
        lock (_trackIntentGate)
            return _hasViewerSubtitle && _subtitleRevision > _subtitleSentRevision;
    }
    private long CaptureAudioRevision() { lock (_trackIntentGate) return _audioRevision; }
    private void AcknowledgeAudioIntent(long revision) { lock (_trackIntentGate) _audioSentRevision = Math.Max(_audioSentRevision, revision); }
    private void AcknowledgeSubtitleIntent(long revision) { lock (_trackIntentGate) _subtitleSentRevision = Math.Max(_subtitleSentRevision, revision); }
    private bool NeedsExplicitSubtitleOff()
    {
        lock (_trackIntentGate) return _hasViewerSubtitle && _viewerSubtitle == null &&
            _subtitleRevision > _subtitleSentRevision && _currentPlanV3?.SelectedTracks.Subtitle != null;
    }
    private long ApplyLocalSubtitleIntent(PlaybackSelectedTracksV3 selected, string operation)
    {
        lock (_trackIntentGate)
        {
            if (operation is not ("track_change" or "quality_change" or "output_change")) return _subtitleSentRevision;
            if (_hasViewerSubtitle && _subtitleRevision > _subtitleSentRevision)
            {
                var track = MatchCurrentSubtitle();
                if (track != null || !_subtitleSelectionIsAutomatic || _viewerSubtitle == null)
                    selected.Subtitle = track == null ? null : new() { Id = track.TrackId ?? "", Index = track.Index };
            }
            return _subtitleRevision;
        }
    }
    private long ApplyLocalAudioIntent(PlaybackSelectedTracksV3 selected, string operation)
    {
        lock (_trackIntentGate)
        {
            if (operation is not ("track_change" or "quality_change" or "output_change")) return _audioSentRevision;
            if (_hasViewerAudio && _audioRevision > _audioSentRevision &&
                ReplacementAudioIndex(CurrentSession?.MediaFileId ?? 0) is int index)
                selected.Audio = new() { Id = $"file:{CurrentSession?.MediaFileId ?? 0}:audio:{index}", Index = index };
            return _audioRevision;
        }
    }
    private SubtitleTrackInfo? GrantedSubtitleSelection()
    {
        var session = CurrentSession;
        if (session == null) return null;
        // Empty legacy IDs are not identities, and an omitted selection is Off.
        var matches = session.SubtitleUrls.Where(t => !t.Unplayable &&
            (!string.IsNullOrWhiteSpace(session.SelectedSubtitleTrackId)
                ? t.TrackId == session.SelectedSubtitleTrackId
                : session.SelectedSubtitleTrackIndex is int index && t.Index == index)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private SubtitleTrackInfo? MatchCurrentSubtitle()
    {
        if (_viewerSubtitle == null) return null;
        var inventory = CurrentSession?.SubtitleUrls ?? [];
        var sameFile = CurrentSession?.MediaFileId == _viewerSubtitleFile;
        var hasIdentity = !string.IsNullOrWhiteSpace(_viewerSubtitle.TrackId);
        if (hasIdentity)
        {
            // Unavailable rows still attest identity. Filtering them first can
            // turn a duplicate ID into a false unique match or pick a lookalike.
            var identities = inventory.Where(t => t.TrackId == _viewerSubtitle.TrackId).Take(2).ToArray();
            if (identities.Length > 0)
                return identities.Length == 1 && !identities[0].Unplayable ? identities[0] : null;
        }
        if (sameFile && Equal(_viewerSubtitle.Source, "downloaded"))
        {
            // A stored subtitle can cross the ID-less refresh/plan boundary.
            // Its stored key, never its current list position, links the rows.
            var key = SubtitleStoredKey(_viewerSubtitle);
            if (key != null)
            {
                var stored = inventory.Where(t => Equal(t.Source, _viewerSubtitle.Source) &&
                    SubtitleStoredKey(t) == key).Take(2).ToArray();
                if (stored.Length > 0)
                    return stored.Length == 1 && !stored[0].Unplayable ? stored[0] : null;
                return null;
            }
        }
        var candidates = inventory.Where(t => !t.Unplayable && (!sameFile || !hasIdentity) &&
            Equal(t.Source, _viewerSubtitle.Source) && SubtitleCodec(t.Codec) == SubtitleCodec(_viewerSubtitle.Codec) &&
            MatchesSubtitleLanguage(t.Language, t.Source, t.Codec, SubtitleIdentityLabel(t), _viewerSubtitle) &&
            t.Forced == _viewerSubtitle.Forced && t.HearingImpaired == _viewerSubtitle.HearingImpaired).ToArray();
        // Only two identityless rows in the same file share a legacy index.
        // Across versions equally good metadata remains ambiguous.
        if (sameFile && !hasIdentity && candidates.FirstOrDefault(t =>
            string.IsNullOrWhiteSpace(t.TrackId) && t.Index == _viewerSubtitle.Index) is { } legacy) return legacy;
        var ranked = candidates.OrderByDescending(t => SubtitleScore(t.Language, t.Source, t.Codec, t.Label, _viewerSubtitle)).Take(2).ToArray();
        return ranked.Length == 1 || ranked.Length == 2 &&
            SubtitleScore(ranked[0].Language, ranked[0].Source, ranked[0].Codec, ranked[0].Label, _viewerSubtitle) >
            SubtitleScore(ranked[1].Language, ranked[1].Source, ranked[1].Codec, ranked[1].Label, _viewerSubtitle)
                ? ranked[0] : null;
    }

    private static string? SubtitleStoredKey(SubtitleTrackInfo track)
        => !string.IsNullOrWhiteSpace(track.SyncKey) ? track.SyncKey : track.Id is > 0 ? $"stored-{track.Id}" : null;

    private static string SubtitleCodec(string? codec) => codec?.Trim().ToLowerInvariant() switch
    {
        "srt" => "subrip",
        "ssa" => "ass",
        "vtt" => "webvtt",
        var value => value ?? "",
    };

    private void CaptureReplacementSubtitleIntent()
    {
        lock (_trackIntentGate)
        {
            // A local Auto result of no subtitle is not an explicit viewer Off
            // either. Startup policy must be reconsidered for the new file.
            if (_hasViewerSubtitle && _viewerSubtitle == null && _subtitleSelectionIsAutomatic)
            {
                _hasViewerSubtitle = false;
                return;
            }
            // A granted automatic selection also survives a transport/session replacement.
            if (!_hasViewerSubtitle)
            {
                _viewerSubtitle = GrantedSubtitleSelection();
                // No automatic grant is not a viewer's explicit Off. Let the
                // replacement start resolve Auto against its own inventory.
                if (_viewerSubtitle == null) return;
                _viewerSubtitleFile = CurrentSession?.MediaFileId ?? 0;
                _hasViewerSubtitle = true;
                _subtitleSelectionIsAutomatic = true;
            }
            // A replacement start has no accepted inventory yet. Retain the
            // identity (including Off), then resolve it against the returned
            // plan rather than sending a watch-detail stream/list ordinal.
            ++_subtitleRevision;
        }
    }
    private int? ReplacementAudioIndex(int fileId)
    {
        lock (_trackIntentGate)
        {
            if (_hasViewerAudio && fileId == _viewerAudioFile) return _viewerAudioIndex;
            if (!_hasViewerAudio)
            {
                _viewerAudio = WatchDetail?.Versions.FirstOrDefault(v => v.FileId == CurrentSession?.MediaFileId)
                    ?.AudioTracks?.ElementAtOrDefault(CurrentSession?.AudioTrackIndex ?? -1);
                if (_viewerAudio != null)
                {
                    _hasViewerAudio = true; _viewerAudioFile = CurrentSession?.MediaFileId ?? 0;
                    _viewerAudioIndex = CurrentSession?.AudioTrackIndex ?? 0;
                }
            }
            if (_viewerAudio == null) return null;
            var desired = _viewerAudio;
            return (WatchDetail?.Versions.FirstOrDefault(v => v.FileId == fileId)?.AudioTracks ?? [])
                .Select((track, index) => (Track: track, Index: index))
                .Where(t => SameLanguage(t.Track.Language, desired.Language) &&
                    IsCommentary(t.Track) == IsCommentary(desired))
                .OrderByDescending(t => (Equal(t.Track.Title, desired.Title) ? 8 : 0) +
                    (Equal(t.Track.EmbeddedTitle, desired.EmbeddedTitle) ? 4 : 0) +
                    (CanonicalLanguage(t.Track.Language) == CanonicalLanguage(desired.Language) ? 64 : 0) +
                    (t.Track.Channels == desired.Channels ? 2 : 0) + (Equal(t.Track.Codec, desired.Codec) ? 1 : 0))
                .Select(t => (int?)t.Index).FirstOrDefault();
        }
    }
    private static bool IsCommentary(AudioTrackInfo track)
        => $"{track.Title} {track.EmbeddedTitle}".Contains("commentary", StringComparison.OrdinalIgnoreCase);
    private static bool Equal(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool SameLanguage(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
           !string.Equals(left.Trim(), "und", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(right.Trim(), "und", StringComparison.OrdinalIgnoreCase) &&
           MediaLanguageCatalog.Normalize(left) == MediaLanguageCatalog.Normalize(right);
    private static bool MatchesSubtitleLanguage(string? language, string? source, string? codec, string? label, SubtitleTrackInfo desired)
        => !UnknownSubtitleLanguage(language) && !UnknownSubtitleLanguage(desired.Language) &&
           CanonicalLanguage(language) == CanonicalLanguage(desired.Language) ||
           UnknownSubtitleLanguage(language) && UnknownSubtitleLanguage(desired.Language) &&
           Equal(source, desired.Source) && SubtitleCodec(codec) == SubtitleCodec(desired.Codec) &&
           Equal(label ?? "", SubtitleIdentityLabel(desired) ?? "");
    // Adapted legacy rows can use the language itself as a synthesized label.
    private static string? SubtitleIdentityLabel(SubtitleTrackInfo track)
        => track.NativeTitle ?? (Equal(track.Label, track.Language) ? null : track.Label);
    private static bool UnknownSubtitleLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) || Equal(language, "und");
    private static string CanonicalLanguage(string? language)
    {
        var parts = (language ?? "").Trim().ToLowerInvariant().Replace('_', '-').Split('-');
        return MediaLanguageCatalog.Normalize(parts[0]) + (parts.Length > 1 ? "-" + string.Join("-", parts.Skip(1)) : "");
    }
    private static int SubtitleScore(string? language, string? source, string? codec, string? label, SubtitleTrackInfo desired)
        => (CanonicalLanguage(language) == CanonicalLanguage(desired.Language) ? 64 : 0) +
           (Equal(source, desired.Source) ? 4 : 0) + (SubtitleCodec(codec) == SubtitleCodec(desired.Codec) ? 1 : 0) + (Equal(label, desired.Label) ? 8 : 0);
}
