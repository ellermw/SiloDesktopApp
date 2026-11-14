using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views.Dialogs;

/// <summary>
/// Interface language for the metadata editor. Field labels are keyed by the
/// stable field id; edited values, saved titles and stored tags stay raw. The
/// reset confirmation keeps its desktop-specific English wording (it describes
/// atomic/legacy semantics the shared catalog has no exact message for).
/// </summary>
public sealed partial class EditMetadataDialog
{
    private readonly List<Action> _translationLocaleRefreshers = [];
    private TextBlock? _ratingsHeading;
    private string _resetLanguage = AppStrings.Language;

    private static string? SectionKey(string section) => section switch
    {
        "general" => "web.item.editMetadata.sectionGeneral",
        "dates" => "web.item.editMetadata.sectionDates",
        "tags" => "web.item.editMetadata.sectionTags",
        "ids" => "web.item.editMetadata.sectionIds",
        "images" => "web.item.editMetadata.sectionImages",
        _ => null,
    };

    private static string? FieldKey(string field) => field switch
    {
        "title" => "web.item.dialog.fieldTitle",
        "sort_title" => "web.item.editMetadata.sortTitle",
        "original_title" => "web.item.editMetadata.originalTitle",
        "overview" => "web.item.editMetadata.overview",
        "tagline" => "web.item.editMetadata.tagline",
        "content_rating" => "web.item.editMetadata.contentRating",
        "runtime" => "web.item.editMetadata.runtime",
        "season_number" => "web.item.editMetadata.seasonNumber",
        "episode_number" => "web.item.editMetadata.episodeNumber",
        "year" => "web.item.dialog.fieldYear",
        "release_date" => "web.item.editMetadata.releaseDate",
        "first_air_date" => "web.item.editMetadata.firstAirDate",
        "last_air_date" => "web.item.editMetadata.lastAirDate",
        "air_time" => "web.item.editMetadata.airTime",
        "air_timezone" => "web.item.editMetadata.airTimezone",
        "air_date" => "web.item.editMetadata.airDate",
        "rating_imdb" => "web.item.editMetadata.imdbRating",
        "rating_tmdb" => "web.item.editMetadata.tmdbRating",
        "rating_rt_critic" => "web.item.editMetadata.rtCritic",
        "rating_rt_audience" => "web.item.editMetadata.rtAudience",
        "genres" => "web.item.editMetadata.genres",
        "studios" => "web.item.editMetadata.studios",
        "networks" => "web.item.editMetadata.networks",
        "countries" => "web.item.editMetadata.countries",
        "imdb_id" => "web.item.dialog.imdbId",
        "tmdb_id" => "web.item.dialog.tmdbId",
        "tvdb_id" => "web.item.dialog.tvdbId",
        _ => null,
    };

    private static TextBlock TypeBadge(string type)
    {
        var badge = new TextBlock
        {
            Text = type.Length == 0 ? "Item" : char.ToUpperInvariant(type[0]) + type[1..],
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
        };
        var key = type switch
        {
            "movie" => "web.item.editMetadata.typeMovie",
            "series" => "web.item.editMetadata.typeSeries",
            "season" => "web.item.editMetadata.typeSeason",
            "episode" => "web.item.editMetadata.typeEpisode",
            _ => null,
        };
        return key == null ? badge : badge.Keyed(key);
    }

    private void RefreshLocale()
    {
        if (!_pending) PrimaryButtonText = AppStrings.Get("web.item.editMetadata.saveChanges");
        if (_resetLanguage != AppStrings.Language)
        {
            _resetLanguage = AppStrings.Language;
            _reset.Content = AppStrings.Get("web.item.editMetadata.resetToProvider");
        }
        if (_ratingsHeading != null) _ratingsHeading.Text = AppStrings.Uppercase(AppStrings.Get("web.item.editMetadata.ratings"));
        foreach (var refresh in _translationLocaleRefreshers) refresh();
        RefreshLocks();
    }
}
