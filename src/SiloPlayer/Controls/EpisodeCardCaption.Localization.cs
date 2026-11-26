using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Controls;

public sealed partial class EpisodeCardCaption
{
    private static EpisodeCaptionPresentation Present(Episode episode, string? formattedAirDate,
        bool hideOverview, string? episodeLabels)
        => EpisodeCardPresentation.Caption(episode, formattedAirDate, hideOverview, episodeLabels,
            DateTimeDisplay.EpisodeAirDate(episodeLabels, episode.AirDate));

    private void RefreshDisplay(Episode episode, string? formattedAirDate, bool hideOverview, string? episodeLabels)
    {
        Presentation = Present(episode, formattedAirDate, hideOverview, episodeLabels);
        TitleText.Text = Presentation.Title;
        var title = episode.Title?.Trim();
        var key = episode.SeasonNumber == 0
            ? string.IsNullOrEmpty(title) ? "web.detail.episodes.spokenSpecial" : "web.detail.episodes.spokenSpecialTitled"
            : string.IsNullOrEmpty(title) ? "web.detail.episodes.spoken" : "web.detail.episodes.spokenTitled";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TitleText, EpisodeLabels.UsesAirDate(episodeLabels)
            ? Presentation.SpokenTitle : AppStrings.Get(key,
                new Dictionary<string, object?> { ["season"] = episode.SeasonNumber, ["episode"] = episode.EpisodeNumber, ["title"] = title ?? "" }));
        var date = CalendarDateValue.TryParse(episode.AirDate, TimeZoneInfo.Local, out var day)
            ? DateTimeDisplay.FormatDate(day, medium: true) : formattedAirDate ?? "";
        MetadataText.Text = string.Join("  ·  ", new[] { date, episode.Runtime > 0 ? MediaItemCardActions.Runtime(episode.Runtime) : "" }
            .Where(value => value.Length > 0));
    }
}
