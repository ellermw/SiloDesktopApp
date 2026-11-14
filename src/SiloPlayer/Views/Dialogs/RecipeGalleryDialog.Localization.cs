using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views.Dialogs;

/// <summary>
/// Display labels for the Add row gallery and form, in the interface language (the WebUI's Home rows messages).
/// Everything a row saves keeps its English source: the preset's DisplayName, <see cref="SectionTypeLabel"/> for a blank
/// name and a variant's English name. These helpers only change what is shown.
/// </summary>
public static partial class RecipeGalleryDialog
{
    private static string CategoryLabel(string key, string english) => key switch
    {
        "keep" or "new" or "popular" or "picked" or "moods" or "collections" => CollectionText.Get("web.home.rows.groups." + key, english),
        _ => english,
    };

    /// <summary>A row kind's name as shown. <see cref="SectionTypeLabel"/> keeps the English one that is saved.</summary>
    private static string DisplaySectionTypeLabel(string type)
    {
        var english = SectionTypeLabel(type);
        var key = type switch
        {
            "recently_added" => "web.home.rows.kinds.recentlyAdded",
            "recently_released" => "home.section.recentlyReleased",
            "genre" => "web.collections.builder.fieldGenre",
            "custom_filter" => "web.home.rows.kinds.customFilter",
            "continue_watching" => "web.home.rows.kinds.continueWatching",
            "recommended_for_you" => "web.home.rows.kinds.recommendedForYou",
            "because_you_watched" => "web.home.rows.kinds.becauseYouWatched",
            "similar_users_liked" => "web.home.rows.kinds.similarUsersLiked",
            "taste_match" => "web.home.rows.kinds.tasteMatch",
            "next_up" => "web.home.rows.kinds.nextUp",
            "next_in_series" => "web.home.rows.kinds.nextInSeries",
            "watchlist" => "web.home.rows.kinds.watchlist",
            "favorites" => "web.home.rows.kinds.favorites",
            "collection" => "web.home.addRow.collection.label",
            _ => null,
        };
        return key == null ? english : CollectionText.Get(key, english);
    }

    private static string VariantFamilyLabel(string type, string english) => type switch
    {
        "continue_watching" => CollectionText.Get("web.home.rows.variants.controls.kind", english),
        "trending_on_server" => CollectionText.Get("web.home.rows.variants.controls.timeWindow", english),
        "most_watched" or "trending_discover" => CollectionText.Get("web.home.rows.variants.controls.period", english),
        "mood_collection" => CollectionText.Get("web.home.rows.variants.controls.mood", english),
        "seasonal_themed" => CollectionText.Get("web.home.rows.variants.controls.theme", english),
        "editorial_spotlight" => CollectionText.Get("web.home.rows.variants.controls.spotlightOn", english),
        "format_showcase" => CollectionText.Get("web.home.rows.variants.controls.format", english),
        _ => english,
    };

    /// <summary>Variant label and hint keys by the variant's own key (its saved discriminator values are unaffected).</summary>
    private static readonly IReadOnlyDictionary<string, (string? Label, string? Hint)> VariantKeys = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal)
    {
        ["continue_watching_default"] = ("web.home.rows.sections.watching", "watchingHint"),
        ["continue_listening_default"] = ("web.home.rows.sections.listening", "listeningHint"),
        ["tr_24h"] = ("last24Hours", "hotToday"), ["tr_7d"] = ("last7Days", "weeklyMix"), ["tr_30d"] = ("last30Days", "changesSlowly"),
        ["mw_week"] = ("thisWeek", "last7DaysHint"), ["mw_month"] = ("thisMonth", "last30DaysHint"),
        ["tdisc_tmdb_day"] = ("today", "changesDaily"), ["tdisc_tmdb_week"] = ("thisWeek", "steadierWeekly"),
        ["mood_feel_good"] = ("feelGood", null), ["mood_mind_bending"] = ("mindBending", null), ["mood_comfort"] = ("comfort", null),
        ["mood_edge_of_seat"] = ("edgeOfSeat", null), ["mood_tearjerker"] = ("tearjerker", null), ["mood_quiet_sunday"] = ("quietSunday", null),
        ["mood_date_night"] = ("dateNight", null), ["mood_after_midnight"] = ("afterMidnight", null),
        ["se_auto"] = ("holidays", "holidaysHint"), ["se_family_movie_night"] = ("web.home.rows.variants.themes.familyMovieNight", "familyHint"),
        ["es_director_auto"] = ("director", "directorHint"), ["es_actor"] = ("actor", "actorHint"), ["es_studio"] = ("studio", "studioHint"),
        ["es_era_80s"] = ("eighties", "eightiesHint"),
        ["fs_4k"] = ("fourK", "fourKHint"), ["fs_4k_recent"] = ("newIn4k", "newIn4kHint"), ["fs_dv"] = ("dolbyVision", "dolbyVisionHint"), ["fs_hdr"] = ("hdr", "hdrHint"),
    };

    private static string VariantMessage(string key) => key.Contains('.') ? key : "web.home.rows.variants.options." + key;

    private static string VariantLabel(HomeRowVariant option)
        => VariantKeys.TryGetValue(option.Key, out var keys) && keys.Label != null ? CollectionText.Get(VariantMessage(keys.Label), option.Label) : option.Label;

    private static string? VariantHint(HomeRowVariant option)
        => option.Hint == null ? null
            : VariantKeys.TryGetValue(option.Key, out var keys) && keys.Hint != null ? CollectionText.Get(VariantMessage(keys.Hint), option.Hint) : option.Hint;

    /// <summary>The seasonal theme's catalog key (the earlier built-in list's keys are snake_case).</summary>
    private static string ThemeKey(string key) => key switch
    {
        "st_patricks" => "stPatricks",
        "saturday_morning" => "saturdayMorning",
        "family_movie_night" => "familyMovieNight",
        "summer_blockbuster" => "summerBlockbuster",
        "new_year" => "newYear",
        _ => key,
    };
}
