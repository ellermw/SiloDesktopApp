using SiloPlayer.Core.Models.Sessions;

namespace SiloPlayer.Core.Services;

public static class SessionDisplayText
{
    public static string GetTitle(PlaybackSessionSummary session)
    {
        if (!string.IsNullOrWhiteSpace(session.SeriesName) &&
            session.SeasonNumber != null &&
            session.EpisodeNumber != null)
            return !string.IsNullOrWhiteSpace(session.EpisodeName)
                ? session.EpisodeName
                : $"S{session.SeasonNumber}E{session.EpisodeNumber}";
        return !string.IsNullOrWhiteSpace(session.MediaTitle)
            ? session.MediaTitle
            : $"File #{session.MediaFileId}";
    }

    public static string? GetSubtitle(PlaybackSessionSummary session)
    {
        if (!string.IsNullOrWhiteSpace(session.SeriesName) &&
            session.SeasonNumber != null &&
            session.EpisodeNumber != null)
            return $"S{session.SeasonNumber}E{session.EpisodeNumber} · {session.SeriesName}";
        return session.MediaType switch
        {
            "movie" => "Movie",
            "series" => "Series",
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
