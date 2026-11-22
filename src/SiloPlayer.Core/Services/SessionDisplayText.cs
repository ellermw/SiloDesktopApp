using SiloPlayer.Core.Models.Sessions;

namespace SiloPlayer.Core.Services;

public static class SessionDisplayText
{
    /// <summary>The series of an episode: series_title on a current server, series_name on one from before c7c155927 (#4561).</summary>
    public static string? GetSeries(PlaybackSessionSummary session)
        => !string.IsNullOrWhiteSpace(session.SeriesTitle) ? session.SeriesTitle : !string.IsNullOrWhiteSpace(session.SeriesName) ? session.SeriesName : null;

    /// <summary>The catalog id to open or play: media_item_id on a current server, content_id on an older one.</summary>
    public static string? GetItemId(PlaybackSessionSummary session)
        => !string.IsNullOrWhiteSpace(session.MediaItemId) ? session.MediaItemId : !string.IsNullOrWhiteSpace(session.ContentId) ? session.ContentId : null;

    /// <summary>A series title and both numbers (official isEpisodeSession); a movie, a track or a session missing any of them is not an episode.</summary>
    public static bool IsEpisodeSession(PlaybackSessionSummary session)
        => GetSeries(session) != null && session.SeasonNumber != null && session.EpisodeNumber != null;

    /// <summary>The episode's own title: media_title on a current server, episode_name on an older one (whose media_title was the series).</summary>
    private static string? GetEpisodeTitle(PlaybackSessionSummary session)
    {
        var episode = !string.IsNullOrWhiteSpace(session.SeriesTitle) ? session.MediaTitle : session.EpisodeName;
        return string.IsNullOrWhiteSpace(episode) ? null : episode;
    }

    /// <summary>
    /// Official getPlaybackSessionTitle (c90e406db, #5106): the show leads an episode, since an episode's own title does not say which show it is;
    /// anything else keeps its own title, else "File #{id}".
    /// </summary>
    public static string GetTitle(PlaybackSessionSummary session)
    {
        if (IsEpisodeSession(session)) return GetSeries(session)!;
        return !string.IsNullOrWhiteSpace(session.MediaTitle)
            ? session.MediaTitle
            : $"File #{session.MediaFileId}";
    }

    /// <summary>
    /// Official getPlaybackSessionSubtitle (c90e406db, #5106, #5105): the episode line under the show ("S1E2 · 8:00 A.M.", or the code alone when
    /// the episode has no title), else "Series" for a series or for an episode missing its show or numbers, else nothing (a movie has no subtitle).
    /// </summary>
    public static string? GetSubtitle(PlaybackSessionSummary session)
    {
        if (IsEpisodeSession(session))
        {
            var code = $"S{session.SeasonNumber}E{session.EpisodeNumber}";
            return GetEpisodeTitle(session) is { } title ? $"{code} · {title}" : code;
        }
        return session.MediaType is "series" or "episode" ? "Series" : null;
    }

    public static string FormatBitrate(int? kbps)
    {
        if (!kbps.HasValue || kbps.Value <= 0) return "";
        return kbps.Value >= 1000 ? $"{kbps.Value / 1000.0:F1} Mbps" : $"{kbps.Value} kbps";
    }

    public static string GetClientLabel(PlaybackSessionSummary session)
    {
        if (!string.IsNullOrWhiteSpace(session.ClientLabel)) return session.ClientLabel.Trim();
        if (!string.IsNullOrWhiteSpace(session.ClientName) && !string.IsNullOrWhiteSpace(session.ClientVersion))
            return $"{session.ClientName.Trim()} {session.ClientVersion.Trim()}";
        return session.ClientName?.Trim() ?? "";
    }
}
