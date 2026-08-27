namespace SiloPlayer.Core.Services;

public readonly record struct EpisodeCardPresentationState(
    bool ShowWatchedIndicator,
    bool ShowProgress,
    double ProgressRatio,
    string WatchedActionLabel);

/// <summary>Derives the current WebUI episode-card state from user progress.</summary>
public static class EpisodeCardPresentation
{
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
