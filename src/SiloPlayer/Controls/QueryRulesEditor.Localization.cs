using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

/// <summary>
/// Display labels for rule fields, operators and values in the interface language (the WebUI's rule builder messages).
/// Rules keep their wire field, operator and value strings; only what the combo boxes show is translated. A language
/// change redraws the editor from its query.
/// </summary>
public sealed partial class QueryRulesEditor
{
    private IDisposable? _localization;
    // Rule controls use the catalog's Field, Condition, Value, Amount and Unit
    // labels for accessibility as well as the translated visible choices.

    private static readonly IReadOnlyDictionary<string, string> FieldKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["title"] = "fieldTitle", ["show_title"] = "fieldShowTitle", ["genre"] = "fieldGenre", ["studio"] = "fieldStudio",
        ["network"] = "fieldNetwork", ["country"] = "fieldCountry", ["content_rating"] = "fieldContentRating",
        ["original_language"] = "fieldOriginalLanguage", ["type"] = "fieldType", ["year"] = "fieldYear", ["decade"] = "fieldDecade",
        ["release_date"] = "fieldReleaseDate", ["last_air_date"] = "fieldLastAirDate", ["runtime"] = "fieldRuntime",
        ["rating_imdb"] = "fieldRatingImdb", ["rating_tmdb"] = "fieldRatingTmdb", ["rating_rt_critic"] = "fieldRatingRtCritic",
        ["rating_rt_audience"] = "fieldRatingRtAudience", ["status"] = "fieldStatus", ["actor"] = "fieldActor",
        ["director"] = "fieldDirector", ["writer"] = "fieldWriter", ["producer"] = "fieldProducer", ["resolution"] = "fieldResolution",
        ["hdr"] = "fieldHdr", ["dolby_vision"] = "fieldDolbyVision", ["audio_language"] = "fieldAudioLanguage",
        ["subtitle_language"] = "fieldSubtitleLanguage", ["bitrate"] = "fieldBitrate", ["watched"] = "fieldWatched",
        ["favorited"] = "fieldFavorited", ["in_watchlist"] = "fieldInWatchlist", ["in_progress"] = "fieldInProgress",
        ["last_watched"] = "fieldLastWatched", ["added_at"] = "fieldAdded", ["latest_episode_added"] = "fieldLatestEpisodeAdded",
    };

    /// <summary>Operators, booleans, relative-date units, match modes and sort directions by wire value.</summary>
    private static readonly IReadOnlyDictionary<string, string> ValueKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["is"] = "web.collections.builder.opIs", ["is_not"] = "web.collections.builder.opIsNot",
        ["gte"] = "web.collections.builder.opAtLeast", ["lte"] = "web.collections.builder.opAtMost",
        ["gt"] = "web.collections.builder.opAbove", ["lt"] = "web.collections.builder.opBelow",
        ["before"] = "web.collections.builder.opBefore", ["after"] = "web.collections.builder.opAfter",
        ["between"] = "web.collections.builder.opBetween", ["contains"] = "web.collections.builder.opContains",
        ["not_contains"] = "web.collections.builder.opNotContains", ["begins_with"] = "web.collections.builder.opBeginsWith",
        ["ends_with"] = "web.collections.builder.opEndsWith", ["in_last"] = "web.collections.builder.opInLast",
        ["not_in_last"] = "web.collections.builder.opNotInLast",
        ["true"] = "web.catalog.rules.yes", ["false"] = "web.catalog.rules.no",
        ["d"] = "web.catalog.rules.unitDays", ["w"] = "web.catalog.rules.unitWeeks", ["m"] = "web.catalog.rules.unitMonths",
        ["y"] = "web.catalog.rules.unitYears", ["h"] = "web.catalog.rules.unitHours",
        ["all"] = "web.catalog.rules.matchAll", ["any"] = "web.catalog.rules.matchAny",
        ["asc"] = "web.catalog.sort.ascending", ["desc"] = "web.catalog.sort.descending",
        ["pending"] = "web.collections.guidedRules.pending", ["matched"] = "web.collections.guidedRules.matched",
        ["unmatched"] = "web.collections.guidedRules.unmatched",
    };

    /// <summary>A rule field, operator or value as the interface shows it; the English label is the fallback.</summary>
    internal static string Label(string value)
    {
        var english = EnglishLabel(value);
        if (FieldKeys.TryGetValue(value, out var field)) return CollectionText.Get("web.collections.builder." + field, english);
        return ValueKeys.TryGetValue(value, out var key) ? CollectionText.Get(key, english) : english;
    }

    /// <summary>A field's label in the field picker, with the WebUI's reading and episode wording.</summary>
    private string FieldLabel(QueryFieldDefinition field)
    {
        if (_mediaScope is "ebook" or "manga" && field.Name == "watched") return CollectionText.Get("web.catalog.rules.fieldRead", "Read");
        if (_mediaScope is "ebook" or "manga" && field.Name == "last_watched") return CollectionText.Get("web.catalog.rules.fieldLastRead", "Last read");
        if (_mediaScope == "episode" && field.Name == "title") return CollectionText.Get("web.catalog.rules.fieldEpisodeTitle", "Episode title");
        return FieldKeys.TryGetValue(field.Name, out var key) ? CollectionText.Get("web.collections.builder." + key, field.Label) : field.Label;
    }

    private static readonly IReadOnlyDictionary<string, string> SortKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["title"] = "catalog.sort.title", ["added_at"] = "catalog.sort.added", ["release_date"] = "catalog.sort.released",
        ["last_air_date"] = "web.common.sort.latestEpisodeAirDate", ["latest_episode_added"] = "web.common.sort.latestEpisodeAdded",
        ["year"] = "catalog.sort.year", ["content_rating"] = "web.common.sort.contentRating", ["runtime"] = "web.common.sort.duration",
        ["rating_imdb"] = "catalog.sort.imdbRating", ["rating_tmdb"] = "web.common.sort.tmdbRating", ["rating_rt_critic"] = "web.common.sort.rtCriticRating",
        ["rating_rt_audience"] = "web.common.sort.rtAudienceRating", ["resolution"] = "catalog.sort.resolution", ["bitrate"] = "web.common.sort.bitrate",
        ["progress"] = "web.common.sort.progress", ["date_viewed"] = "web.common.sort.dateViewed", ["plays"] = "web.common.sort.plays",
        ["author"] = "catalog.sort.author", ["narrator"] = "catalog.sort.narrator", ["series"] = "catalog.sort.series",
    };

    /// <summary>A sort field's label; the field itself is what is saved.</summary>
    internal static string SortLabel(string field, string english) => SortKeys.TryGetValue(field, out var key) ? CollectionText.Get(key, english) : english;

    /// <summary>The search box for a facet value: the WebUI's per-facet wording, else a generic one.</summary>
    private static string FacetSearchPlaceholder(string field) => field switch
    {
        "genre" or "studio" or "network" or "country" => CollectionText.Get("web.collections.builder.facetSearchLabel", "Search " + EnglishLabel(field).ToLowerInvariant(), ("facet", field)),
        "content_rating" => CollectionText.Get("web.collections.builder.facetSearchLabel", "Search content rating", ("facet", "other")),
        _ => CollectionText.Get("web.catalog.rules.searchValues", "Search " + EnglishLabel(field).ToLowerInvariant()),
    };

    /// <summary>The collection rules sentence's all/any choice, worded as the WebUI's lower-case sentence words.</summary>
    private static ComboBox CollectionMatchChoice(string selected, string name)
    {
        var control = Choice(["all", "any"], selected, name);
        var options = new[] { "all", "any" }.Concat(string.IsNullOrWhiteSpace(selected) || selected is "all" or "any" ? [] : new[] { selected })
            .Select(value => new Option(value switch
            {
                "all" => CollectionText.Get("web.home.addRow.rules.matchAll", "all"),
                "any" => CollectionText.Get("web.home.addRow.rules.matchAny", "any"),
                _ => Label(value),
            }, value)).ToArray();
        control.ItemsSource = options; control.SelectedItem = options.FirstOrDefault(option => option.Value == selected);
        return control;
    }

    private static string GroupLabel(string group) => group switch
    {
        "Title" => CollectionText.Get("web.collections.builder.groupTitle", group),
        "People" => CollectionText.Get("web.collections.builder.groupPeople", group),
        "File" => CollectionText.Get("web.collections.builder.groupFile", group),
        "You" => CollectionText.Get("web.collections.builder.groupYou", group),
        "Library" => CollectionText.Get("web.collections.builder.groupLibrary", group),
        _ => group,
    };

    /// <summary>A language code's name: the catalog's language name when it has one, otherwise the system's English name.</summary>
    private static string LanguageName(string code)
    {
        string english;
        try { english = System.Globalization.CultureInfo.GetCultureInfo(code).EnglishName; }
        catch (System.Globalization.CultureNotFoundException) { english = code; }
        return CollectionText.Get("language." + code.ToLowerInvariant(), english);
    }

    private void AttachLocalization()
        => _localization ??= AppLocalization.Register(this, () =>
        {
            // The first load draws the editor from Load(); a later language redraws it from the same query.
            if (_drawnLocale != null && !ReferenceEquals(_drawnLocale, SiloPlayer.Core.Localization.AppStrings.Current) && Query != null) Rebuild();
        });

    private SiloPlayer.Core.Localization.AppLocale? _drawnLocale;
}
