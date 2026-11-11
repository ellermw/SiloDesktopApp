using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>The poster status corner is independent of overlay preferences.</summary>
public static class CardWatchStatus
{
    public static int? RemainingEpisodes(UserState? state) => state is { Played: false, WatchedCount: > 0, UnplayedCount: > 0 }
        ? state.UnplayedCount : null;

    public static string? AccessibleName(UserState? state) => state?.Played == true ? "Watched"
        : RemainingEpisodes(state) is { } left ? $"{left} {(left == 1 ? "episode" : "episodes")} left" : null;
}
