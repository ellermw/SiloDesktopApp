using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

public sealed record EpisodeCaptionPresentation(string Title, string SpokenTitle, string Metadata,
    string Overview, bool ShowTranslation, bool ShowUnreadable);

public readonly record struct EpisodeCardPresentationState(
    bool ShowWatchedIndicator,
    bool ShowProgress,
    double ProgressRatio,
    string WatchedActionLabel);

/// <summary>Derives the current WebUI episode-card state from user progress.</summary>
public static class EpisodeCardPresentation
{
    public static EpisodeCaptionPresentation Caption(Episode episode, string? formattedAirDate, bool hideOverview)
    {
        var overview = hideOverview ? "" : episode.Overview?.Trim() ?? "";
        var title = episode.Title?.Trim();
        var code = $"S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2}";
        var season = episode.SeasonNumber == 0 ? "Specials" : $"Season {episode.SeasonNumber}";
        var spoken = $"{season}, Episode {episode.EpisodeNumber}";
        var runtime = episode.Runtime <= 0 ? "" : episode.Runtime >= 60
            ? $"{episode.Runtime / 60}h {episode.Runtime % 60}m" : $"{episode.Runtime}m";
        return new(string.IsNullOrEmpty(title) ? code : $"{code} · {title}",
            string.IsNullOrEmpty(title) ? spoken : $"{spoken}, {title}",
            string.Join("\u00a0\u00a0·\u00a0\u00a0", new[] { formattedAirDate ?? "", runtime }.Where(value => value.Length > 0)),
            overview, overview.Length > 0 && MachineTranslationPresentation.HasField(episode.MachineTranslatedFields),
            episode.Files.Count > 0 && episode.Files.All(file => file.Unreadable == true));
    }

    public static EpisodeCardPresentationState Create(
        bool watched,
        double positionSeconds,
        double durationSeconds)
    {
        var showProgress = !watched && positionSeconds > 0 && durationSeconds > 0;
        return new EpisodeCardPresentationState(
            ShowWatchedIndicator: watched,
            ShowProgress: showProgress,
            ProgressRatio: showProgress
                ? Math.Clamp(positionSeconds / durationSeconds, 0, 1)
                : 0,
            WatchedActionLabel: watched
                ? "Mark Episode Unwatched"
                : "Mark Episode Watched");
    }
}
