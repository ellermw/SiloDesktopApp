using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services.PlaybackMenus;

internal sealed record PlaybackSpeedMenuOption(string Id, string Title, string Metadata, double Rate,
    bool IsCurrent, bool IsPending, bool IsEnabled, string DisabledReason);

/// <summary>Desired viewer speed only; native room correction rates are never menu choices.</summary>
internal static class PlaybackSpeedMenuModel
{
    internal static IReadOnlyList<PlaybackSpeedMenuOption> Build(double desired, bool canChange, bool isRoom, bool pending)
    {
        var current = VideoPlaybackRatePreferences.IsValidRate(desired) ? Math.Round(desired * 20) / 20 : 1;
        var rates = isRoom ? [current] : VideoPlaybackRatePreferences.Presets.Contains(current)
            ? VideoPlaybackRatePreferences.Presets : VideoPlaybackRatePreferences.Presets.Append(current).OrderBy(rate => rate).ToArray();
        return rates.Select(rate =>
        {
            var label = rate.ToString("0.##", CultureInfo.InvariantCulture);
            var custom = !VideoPlaybackRatePreferences.Presets.Contains(rate);
            var enabled = canChange && !isRoom;
            // The id keeps the invariant wire rate; the title is display text.
            var shown = rate.ToString("0.##", AppStrings.FormatCulture);
            return new PlaybackSpeedMenuOption("rate:" + label, AppStrings.Get("web.player.speed.rate",
                    new Dictionary<string, object?> { ["rate"] = shown }, fallback: shown + "×"),
                custom ? "Saved custom speed" : rate == 1 ? "Normal speed" : "", rate,
                rate == current, pending && rate == current, enabled,
                enabled ? "" : isRoom ? "Watch Party controls playback speed." : "Playback speed is unavailable for this playback.");
        }).ToArray();
    }
}
