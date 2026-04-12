namespace ContinuumPlayer.Core.Models.Playback;

public class WatchDetailResponse
{
    public string ContentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public List<FileVersion> Versions { get; set; } = [];
    public List<SubtitleInfo> Subtitles { get; set; } = [];
    public WatchUserData? UserData { get; set; }
    public TimeRange? Intro { get; set; }
    public TimeRange? Credits { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? EffectiveSubtitleLanguage { get; set; }
    public string? EffectiveSubtitleMode { get; set; }
    public bool? EffectiveShowForcedSubtitles { get; set; }
}

public class FileVersion
{
    public int FileId { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string Resolution { get; set; } = "";
    public string CodecVideo { get; set; } = "";
    public string CodecAudio { get; set; } = "";
    public bool Hdr { get; set; }
    public string Container { get; set; } = "";
    public long FileSize { get; set; }
    public double Duration { get; set; }
    public int Bitrate { get; set; }
    public int? AudioChannels { get; set; }
    public string? AddedAt { get; set; }
    /// <summary>Server-computed "auto" audio track index (from user prefs + defaults).</summary>
    public int? EffectiveAudioTrackIndex { get; set; }
    /// <summary>Server-resolved effective audio language for the effective track (commit c16467c).</summary>
    public string? EffectiveAudioLanguage { get; set; }
    public List<VersionVideoTrack>? VideoTracks { get; set; }
    public List<AudioTrackInfo>? AudioTracks { get; set; }
    public List<VersionSubtitleTrack>? SubtitleTracks { get; set; }
    public List<VersionChapter>? Chapters { get; set; }
}

public class VersionVideoTrack
{
    public string? Title { get; set; }
    public string? Codec { get; set; }
    public string? DolbyVision { get; set; }
    public string? Profile { get; set; }
    public int? Level { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? AspectRatio { get; set; }
    public bool? Interlaced { get; set; }
    public string? FrameRate { get; set; }
    public int? Bitrate { get; set; }
    public string? VideoRange { get; set; }
    public string? ColorPrimaries { get; set; }
    public string? ColorSpace { get; set; }
    public string? ColorTransfer { get; set; }
    public int? BitDepth { get; set; }
    public string? PixelFormat { get; set; }
    public int? ReferenceFrames { get; set; }
}

public class AudioTrackInfo
{
    public string? Title { get; set; }
    public string? EmbeddedTitle { get; set; }
    public string? Language { get; set; }
    public string? Codec { get; set; }
    public string? Layout { get; set; }
    public int? Channels { get; set; }
    public int? Bitrate { get; set; }
    public int? SampleRate { get; set; }
    public int? BitDepth { get; set; }
    public bool Default { get; set; }
}

/// <summary>Subtitle track attached to a file version (embedded or external .srt/.ass sidecar).
/// Distinct from <see cref="SubtitleInfo"/> which is the top-level WatchDetail list covering
/// downloaded provider subtitles as well.</summary>
public class VersionSubtitleTrack
{
    public int? Index { get; set; }
    public string? Language { get; set; }
    public string? Codec { get; set; }
    public string? Title { get; set; }
    public string? EmbeddedTitle { get; set; }
    public string? Resolution { get; set; }
    public bool? Forced { get; set; }
    public bool? Default { get; set; }
    public bool? HearingImpaired { get; set; }
    public bool? External { get; set; }
    public string? FileName { get; set; }
}

public class VersionChapter
{
    public int Index { get; set; }
    public string Title { get; set; } = "";
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Source { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public string? ThumbnailThumbhash { get; set; }
}

public class SubtitleInfo
{
    public string Language { get; set; } = "";
    public string? Codec { get; set; }
    public string? Title { get; set; }
    public string Source { get; set; } = "";
    public bool Forced { get; set; }
    public bool? HearingImpaired { get; set; }
}

public class WatchUserData
{
    public double? PositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public bool Played { get; set; }
    public int? LastFileId { get; set; }
    public string? LastResolution { get; set; }
    public bool? LastHdr { get; set; }
    public string? LastCodecVideo { get; set; }
}

public class TimeRange
{
    public double Start { get; set; }
    public double End { get; set; }
}

// ===== Watch Together (Watch Party) =====
// DTOs for the /api/v1/watch-together/* surface. Shadow the webui types in
// continuum-server/web/src/lib/watchTogether.ts. Kept in this file so playback-
// related models stay colocated; there's no separate WatchTogether folder yet.

public class WatchTogetherRoomSnapshot
{
    public string RoomId { get; set; } = "";
    public string Phase { get; set; } = ""; // "lobby" | "playing" | "ended"
    public string PlaybackState { get; set; } = ""; // "idle" | "waiting" | "paused" | "playing"
    public string SelectionMode { get; set; } = "host_pick"; // "host_pick" | "vote"
    public int SelectionRevision { get; set; }
    public string? SelectedContentId { get; set; }
    public int? SelectedFileId { get; set; }
    public int? SelectedLibraryId { get; set; }
    public string Code { get; set; } = "";
    public string GuestControlPolicy { get; set; } = "host_only"; // "host_only" | "guest_play_pause"
    public bool IsPaused { get; set; }
    public double AnchorPositionSeconds { get; set; }
    public string? AnchorUpdatedAt { get; set; }
    public int Generation { get; set; }
    public int MemberCount { get; set; }
    public bool HostConnected { get; set; }
    public string SelfRole { get; set; } = ""; // "host" | "guest"
    public bool SelfCanControlTransport { get; set; }
    public bool SelfCanManageRoom { get; set; }
    public bool SelfIgnoreWait { get; set; }
    public string? AttachedSessionId { get; set; }
    public string? InvitePath { get; set; }
}

public class WatchTogetherRoomResponse
{
    public WatchTogetherRoomSnapshot Room { get; set; } = new();
    public string? RoomAccessToken { get; set; }
}

public class WatchTogetherSuggestion
{
    public string Id { get; set; } = "";
    public string RoomId { get; set; } = "";
    public int SuggesterUserId { get; set; }
    public string SuggesterProfileId { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string ContentType { get; set; } = ""; // "movie" | "episode"
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string PosterUrl { get; set; } = "";
    public string Note { get; set; } = "";
    public int VoteCount { get; set; }
    public bool VotedByMe { get; set; }
    public string? CreatedAt { get; set; }
}

public class WatchTogetherSuggestionsResponse
{
    public List<WatchTogetherSuggestion> Suggestions { get; set; } = [];
}

public class WatchTogetherTransportCommand
{
    public string CommandId { get; set; } = "";
    public string? SessionId { get; set; }
    public int SelectionRevision { get; set; }
    public string Action { get; set; } = ""; // "play" | "pause" | "seek"
    public double PositionSeconds { get; set; }
    public string? ExecuteAt { get; set; }
    public string? IssuedAt { get; set; }
    public string PlaybackState { get; set; } = "";
}
