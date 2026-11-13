using SiloPlayer.Core.Localization;
using SiloPlayer.Services;

namespace SiloPlayer;

// Interface text for the ended/Playing Next screen (PlayingNextScreen.tsx keys).
// Series titles, scope labels and episode metadata stay raw data. The shell
// files that own the screen's presentation call these helpers; see the Task 5
// playback report for the remaining call-site wiring in MainWindow.xaml(.cs)
// and MainWindow.Shuffle.cs.
public sealed partial class MainWindow
{
    private bool _playingNextTextKeyed;

    private static Dictionary<string, object?> PlayingNextArgs(params (string Name, object? Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    internal static string PlayingNextLabel(bool hasNext) => AppStrings.Uppercase(hasNext
        ? AppStrings.Get("web.player.upNext.playingNext", fallback: "Playing Next")
        : AppStrings.Get("upNext.headingFinished", fallback: "Finished"));

    internal static string PlayingNextFinishedHeadingText(string? seriesTitle) => !string.IsNullOrWhiteSpace(seriesTitle)
        ? AppStrings.Get("web.player.upNext.finishedSeries", PlayingNextArgs(("series", seriesTitle)), fallback: $"You've finished {seriesTitle}")
        : AppStrings.Get("upNext.endOfPlayback", fallback: "End of playback");

    // The shuffle wording has no catalog equivalent and stays English (native-only).
    internal static string PlayingNextFinishedDescriptionText(bool shuffle) => shuffle
        ? "There are no more playable titles in this shuffle."
        : AppStrings.Get("web.player.upNext.noMoreEpisodes", fallback: "There are no more episodes available. Pick something else from On Deck below.");

    internal static string PlayingNextShuffleScopeText(string? scope)
        => AppStrings.Get("upNext.shuffling", PlayingNextArgs(("scope", scope ?? "")), fallback: "Shuffling " + scope);

    internal static string PlayingNextCountdown(int seconds)
        => AppStrings.Get("web.player.upNext.countdownSeconds", PlayingNextArgs(("count", seconds)), fallback: $"{seconds}s");

    internal static string PlayingNextAutoplayText(bool on) => on
        ? AppStrings.Get("web.player.upNext.autoplayOn", fallback: "Auto-play is on")
        : AppStrings.Get("web.player.upNext.autoplayOff", fallback: "Auto-play is off");

    /// <summary>Keys the screen's fixed labels once; they then follow the interface language.</summary>
    private void EnsurePlayingNextTextKeys()
    {
        if (_playingNextTextKeyed) return;
        _playingNextTextKeyed = true;
        Localized.SetText(PlayingNextPlayNowText, "upNext.playNow");
        Localized.SetContent(PlayingNextFinishedBackButton, "common.back");
        Localized.SetContent(PlayingNextPickAnother, "upNext.pickAnother");
        Localized.SetContent(PlayingNextStopShuffle, "upNext.stopShuffling");
    }
}
