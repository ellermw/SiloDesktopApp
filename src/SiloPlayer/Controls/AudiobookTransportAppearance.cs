using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Controls;

internal static class AudiobookTransportAppearance
{
    // Keep the existing player's glyph/state and automation owner, but paint
    // filled pause bars like the audiobook transport in the WebUI.
    internal static void Apply(Button button, FontIcon icon, double size)
    {
        button.Content = null;
        var surface = new Grid();
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = size * .18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var index = 0; index < 2; index++) bars.Children.Add(new Rectangle { Width = size * .18, Height = size * .70, Fill = icon.Foreground });
        surface.Children.Add(icon); surface.Children.Add(bars); button.Content = surface;
        void Paint() { var pausedGlyph = icon.Glyph == "\uE769"; icon.Visibility = pausedGlyph ? Visibility.Collapsed : Visibility.Visible; bars.Visibility = pausedGlyph ? Visibility.Visible : Visibility.Collapsed; }
        icon.RegisterPropertyChangedCallback(FontIcon.GlyphProperty, (_, _) => Paint()); Paint();
    }

    /// <summary>
    /// Audiobook transport text shared by Now Listening and the mini player, from
    /// the WebUI's web.audiobooks.player / web.player.sleepTimer catalog keys.
    /// Numbers use the display culture; chapter titles and names stay raw.
    /// </summary>
    internal static class Labels
    {
        private static Dictionary<string, object?> Args(params (string Name, object? Value)[] values)
            => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

        internal static string Rate(double rate)
        {
            var shown = rate.ToString("0.##", AppStrings.FormatCulture);
            return AppStrings.Get("web.audiobooks.player.rate", Args(("rate", shown)), fallback: shown + "×");
        }

        internal static string PlayPause(bool paused, bool title) => paused
            ? AppStrings.Get(title ? "web.audiobooks.player.playTitle" : "web.audiobooks.player.play", fallback: title ? "Play (space)" : "Play")
            : AppStrings.Get(title ? "web.audiobooks.player.pauseTitle" : "web.audiobooks.player.pause", fallback: title ? "Pause (space)" : "Pause");

        internal static string Skip(bool back, int seconds, bool title) => AppStrings.Get(
            back ? (title ? "web.audiobooks.player.skipBackTitle" : "web.audiobooks.player.skipBack")
                 : (title ? "web.audiobooks.player.skipForwardTitle" : "web.audiobooks.player.skipForward"),
            Args(("seconds", seconds)),
            fallback: (back ? "Back " : "Forward ") + seconds + " seconds" + (title ? back ? " (←)" : " (→)" : ""));

        internal static string Mute(bool muted) => muted
            ? AppStrings.Get("web.player.volume.unmute", fallback: "Unmute")
            : AppStrings.Get("web.player.volume.mute", fallback: "Mute");

        internal static string Sleep(TimeSpan? remaining) => remaining is { } value
            ? AppStrings.Get("web.player.sleepTimer.countdown", Args(("time", $"{(int)value.TotalMinutes}:{value.Seconds:00}")),
                fallback: $"Sleep {(int)value.TotalMinutes}:{value.Seconds:00}")
            : AppStrings.Get("web.player.sleepTimer.short", fallback: "Sleep");

        internal static string SleepOff => AppStrings.Get("web.player.sleepTimer.turnOff", fallback: "Turn off");
        internal static string SleepMinutes(int minutes) => AppStrings.Get("web.player.sleepTimer.minutes",
            Args(("minutes", minutes.ToString(AppStrings.FormatCulture))), fallback: $"{minutes} min");
        internal static string SleepEndOfChapter => AppStrings.Get("web.player.sleepTimer.endOfChapter", fallback: "End of chapter");

        internal static string Remaining(string time) => AppStrings.Get("web.audiobooks.player.timeRemaining", Args(("time", time)), fallback: "-" + time);
        internal static string RemainingAtSpeed(string time, double rate) => AppStrings.Get("web.audiobooks.player.timeRemainingAtSpeed",
            Args(("time", time), ("rate", Rate(rate))), fallback: $"-{time} at {Rate(rate)}");

        internal static string ChapterNumber(int number) => AppStrings.Get("web.audiobooks.player.chapterNumber",
            Args(("number", number)), fallback: $"Chapter {number}");

        internal static string NarratedBy(string narrator) => AppStrings.Get("web.audiobooks.hero.narratedBy",
            Args(("narrator", narrator)), fallback: $"Narrated by {narrator}");
    }
}
