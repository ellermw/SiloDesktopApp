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

    public static string GetTitle(PlaybackSessionSummary session)
    {
        if (GetSeries(session) != null &&
            session.SeasonNumber != null &&
            session.EpisodeNumber != null)
        {
            // Current servers put the episode title in media_title; older ones sent it as episode_name.
            var episode = !string.IsNullOrWhiteSpace(session.SeriesTitle) ? session.MediaTitle : session.EpisodeName;
            return !string.IsNullOrWhiteSpace(episode)
                ? episode
                : $"S{session.SeasonNumber}E{session.EpisodeNumber}";
        }
        return !string.IsNullOrWhiteSpace(session.MediaTitle)
            ? session.MediaTitle
            : $"File #{session.MediaFileId}";
    }

    public static string? GetSubtitle(PlaybackSessionSummary session)
    {
        if (GetSeries(session) is { } series &&
            session.SeasonNumber != null &&
            session.EpisodeNumber != null)
            return $"S{session.SeasonNumber}E{session.EpisodeNumber} · {series}";
        return session.MediaType switch
        {
            "movie" => "Movie",
            "series" or "episode" => "Series",
            _ => null,
        };
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
