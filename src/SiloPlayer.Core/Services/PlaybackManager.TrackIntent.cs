using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public partial class PlaybackManager
{
    private readonly object _trackIntentGate = new();
    private bool _hasViewerSubtitle;
    private SubtitleTrackInfo? _viewerSubtitle;
    private int _viewerSubtitleFile;
    private long _subtitleRevision, _subtitleSentRevision;
    private AudioTrackInfo? _viewerAudio;
    private bool _hasViewerAudio;
    private int _viewerAudioIndex, _viewerAudioFile;
    private long _audioRevision, _audioSentRevision;

    /// <summary>Records a viewer's local sid change without saving automatic plan fallbacks.</summary>
    public void RecordLocalSubtitleSelection(int? subtitleIndex)
    {
        lock (_trackIntentGate)
        {
            var track = subtitleIndex.HasValue ? CurrentSession?.SubtitleUrls.FirstOrDefault(t => t.Index == subtitleIndex) : null;
            if (subtitleIndex.HasValue && track == null) throw new InvalidOperationException("The selected subtitle is no longer in the active inventory.");
            _hasViewerSubtitle = true; _viewerSubtitle = track;
            _viewerSubtitleFile = CurrentSession?.MediaFileId ?? 0;
            ++_subtitleRevision;
        }
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
                    return MatchCurrentSubtitle()?.Index ?? -1;
                return CurrentSession?.SubtitleUrls.FirstOrDefault(t =>
                    t.TrackId == CurrentSession.SelectedSubtitleTrackId)?.Index ?? -1;
            }
        }
    }

    private void ResetLocalTrackIntent()
    {
        lock (_trackIntentGate)
        {
            _hasViewerSubtitle = _hasViewerAudio = false; _viewerSubtitle = null; _viewerAudio = null;
            ++_subtitleRevision; _subtitleSentRevision = _subtitleRevision;
            ++_audioRevision; _audioSentRevision = _audioRevision;
        }
    }
    private long CaptureSubtitleRevision() { lock (_trackIntentGate) return _subtitleRevision; }
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
    private SubtitleTrackInfo? MatchCurrentSubtitle()
    {
        if (_viewerSubtitle == null) return null;
        var tracks = CurrentSession?.SubtitleUrls ?? [];
        return tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.TrackId) && t.TrackId == _viewerSubtitle.TrackId)
            ?? tracks.Where(t => SameLanguage(t.Language, _viewerSubtitle.Language) &&
                t.Forced == _viewerSubtitle.Forced && t.HearingImpaired == _viewerSubtitle.HearingImpaired)
                .OrderByDescending(t => SubtitleScore(t.Source, t.Codec, t.Label, _viewerSubtitle)).FirstOrDefault();
    }
    private int? ReplacementSubtitleIndex(int fileId)
    {
        lock (_trackIntentGate)
        {
            // A granted automatic selection also survives a transport/session replacement.
            if (!_hasViewerSubtitle)
            {
                _viewerSubtitle = CurrentSession?.SubtitleUrls.FirstOrDefault(t => t.TrackId == CurrentSession.SelectedSubtitleTrackId);
                _viewerSubtitleFile = CurrentSession?.MediaFileId ?? 0;
                _hasViewerSubtitle = true;
            }
            if (_viewerSubtitle == null) return null;
            if (fileId == _viewerSubtitleFile) return _viewerSubtitle.Index;
            var tracks = WatchDetail?.Versions.FirstOrDefault(v => v.FileId == fileId)?.SubtitleTracks ?? [];
            return tracks.Select((track, i) => (Track: track, Index: track.Index ?? i))
                .Where(t => SameLanguage(t.Track.Language, _viewerSubtitle.Language) &&
                    (t.Track.Forced == true) == _viewerSubtitle.Forced &&
                    (t.Track.HearingImpaired == true) == _viewerSubtitle.HearingImpaired)
                .OrderByDescending(t => SubtitleScore(t.Track.External == true ? "external" : "embedded",
                    t.Track.Codec, t.Track.Title ?? t.Track.EmbeddedTitle, _viewerSubtitle))
                .Select(t => (int?)t.Index).FirstOrDefault();
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
    private static string CanonicalLanguage(string? language)
    {
        var parts = (language ?? "").Trim().ToLowerInvariant().Replace('_', '-').Split('-');
        return MediaLanguageCatalog.Normalize(parts[0]) + (parts.Length > 1 ? "-" + string.Join("-", parts.Skip(1)) : "");
    }
    private static int SubtitleScore(string? source, string? codec, string? label, SubtitleTrackInfo desired)
        => (Equal(source, desired.Source) ? 4 : 0) + (Equal(codec, desired.Codec) ? 1 : 0) + (Equal(label, desired.Label) ? 8 : 0);
}
