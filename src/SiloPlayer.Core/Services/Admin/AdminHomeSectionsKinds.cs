using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminHomeSectionVariant(string PresetKey, string ChipKey, string LabelKey, string? HintKey, IReadOnlyDictionary<string, JsonNode?> Values);
public sealed record AdminHomeSectionVariantFamily(string ControlKey, string FollowsKey, IReadOnlyList<string> Keys, IReadOnlyList<AdminHomeSectionVariant> Options);

/// <summary>Plain names, picker groups and variants of Home row kinds (217ff25 web/src/lib/homeRows/catalog.ts and variants.ts). Stored section_type values never change.</summary>
public static class AdminHomeSectionsKinds
{
    public static readonly IReadOnlyList<string> GroupOrder = ["keep", "new", "popular", "picked", "moods", "collections"];

    private sealed record Kind(string Group, string Name, string Key);

    // Insertion order is the picker's card order within each group.
    private static readonly IReadOnlyDictionary<string, Kind> Kinds = new Dictionary<string, Kind>(StringComparer.Ordinal)
    {
        ["continue_watching"] = new("keep", "Continue watching", "continueWatching"), ["next_up"] = new("keep", "Next up", "nextUp"),
        ["next_in_series"] = new("keep", "Next in series", "nextInSeries"), ["watchlist"] = new("keep", "Watchlist", "watchlist"), ["favorites"] = new("keep", "Favorites", "favorites"),
        ["recently_added"] = new("new", "Recently added", "recentlyAdded"), ["recently_released"] = new("new", "New releases", "recentlyReleased"),
        ["new_to_library"] = new("new", "New this month", "newToLibrary"), ["returning_shows"] = new("new", "Returning shows", "returningShows"),
        ["trending_on_server"] = new("popular", "Trending on this server", "trendingOnServer"), ["most_watched"] = new("popular", "Most watched", "mostWatched"),
        ["trending_discover"] = new("popular", "Trending worldwide", "trendingDiscover"), ["profile_activity_feed"] = new("popular", "What others just watched", "profileActivityFeed"),
        ["recommended_for_you"] = new("picked", "Recommended for you", "recommendedForYou"), ["because_you_watched"] = new("picked", "Because you watched", "becauseYouWatched"),
        ["similar_users_liked"] = new("picked", "Profiles like you enjoyed", "similarUsersLiked"), ["taste_match"] = new("picked", "Top picks today", "tasteMatch"),
        ["mood_collection"] = new("moods", "Mood picks", "moodCollection"), ["seasonal_themed"] = new("moods", "Seasonal picks", "seasonalThemed"),
        ["editorial_spotlight"] = new("moods", "Spotlight", "editorialSpotlight"), ["format_showcase"] = new("moods", "4K & HDR showcase", "formatShowcase"),
        ["hidden_gems"] = new("moods", "Hidden gems", "hiddenGems"), ["critically_acclaimed"] = new("moods", "Critically acclaimed", "criticallyAcclaimed"),
        ["forgotten_favorites"] = new("moods", "Forgotten favorites", "forgottenFavorites"), ["genre_roulette"] = new("moods", "Genre roulette", "genreRoulette"),
        ["random"] = new("moods", "Surprise me", "random"), ["short_watches"] = new("moods", "Short & sweet", "shortWatches"), ["anniversaries"] = new("moods", "Anniversaries", "anniversaries"),
        ["award_winners"] = new("moods", "Award winners (no longer offered)", "awardWinners"),
        ["collection"] = new("collections", "A collection", "collection"), ["collection_posters"] = new("collections", "Collections", "collectionPosters"),
        ["custom_filter"] = new("collections", "Titles matching rules", "customFilter"), ["genre"] = new("collections", "Genre (no longer offered)", "genre"),
        ["admin_curated_list"] = new("collections", "Editor's picks", "adminCuratedList")
    };
    private static readonly List<string> Order = Kinds.Keys.ToList();

    /// <summary>Kinds an existing row may still have but no picker offers for a new row (the server adds the only Next up row itself).</summary>
    public static readonly IReadOnlySet<string> NotOfferedForNewRows = new HashSet<string>(["admin_curated_list", "genre", "award_winners", "next_up"], StringComparer.Ordinal);

    private static string T(string key, object? values = null) => AdminHomeSectionsText.T(key, values);
    public static string Label(string type) => T("web.home.rows.kinds." + (Kinds.TryGetValue(type, out var kind) ? kind.Key : "unknown"));
    public static string Name(string type) => Kinds.TryGetValue(type, out var kind) ? kind.Name : "Row";
    public static string Group(string type) => Kinds.TryGetValue(type, out var kind) ? kind.Group : "collections";
    public static string Sentence(string type) => T("web.home.rows.kindSentences." + (Kinds.TryGetValue(type, out var kind) ? kind.Key : "unknown"));
    public static bool IsKnown(string type) => Kinds.ContainsKey(type);
    public static int Rank(string type) { var index = Order.IndexOf(type); return index == -1 ? Order.Count : index; }
    public static string GroupLabel(string group) => T("web.home.rows.groups." + group);
    public static string GroupBlurb(string group) => T("web.home.rows.groups." + group + "Blurb");
    public static bool HoldsCollections(string type) => type == "collection_posters";

    /// <summary>The "Shows:" name of a kind the picker no longer offers; award rows keep their award.</summary>
    public static string RetiredLabel(string type, JsonObject config) =>
        type == "award_winners" ? T("web.home.rows.kinds.retiredAward", new { award = config["award_type"]?.ToString() ?? "" }) : Label(type);

    /// <summary>The picker's groups: one card per kind the catalog offers, minus kinds no longer offered; unknown kinds sit under moods.</summary>
    public static IReadOnlyList<(string Group, IReadOnlyList<AdminHomeRecipe> Cards)> PickerGroups(AdminHomeSectionCatalog catalog)
    {
        var defs = catalog.Categories.SelectMany(group => group.Recipes)
            .Where(def => !NotOfferedForNewRows.Contains(def.Type) && (def.Presets.Count > 0 || def.Type == "custom_filter"))
            .OrderBy(def => Rank(def.Type)).ToList();
        return GroupOrder.Select(group => (group, (IReadOnlyList<AdminHomeRecipe>)defs.Where(def => (Kinds.TryGetValue(def.Type, out var kind) ? kind.Group : "moods") == group).ToArray()))
            .Where(entry => entry.Item2.Count > 0).ToArray();
    }

    // ── Variants ─────────────────────────────────────────────

    private static readonly string[] HolidayThemes = ["halloween", "christmas", "new_year", "valentines", "st_patricks", "easter", "thanksgiving", "summer_blockbuster", "saturday_morning"];
    private static IReadOnlyDictionary<string, JsonNode?> V(params (string Key, object Value)[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value is string[] list ? new JsonArray(list.Select(item => (JsonNode)JsonValue.Create(item)!).ToArray()) : (JsonNode?)JsonValue.Create(pair.Value));
    private static AdminHomeSectionVariant O(string key, string chip, string label, string? hint, IReadOnlyDictionary<string, JsonNode?> values) =>
        new(key, chip.Contains('.') && chip.StartsWith("web.") ? chip : "web.home.rows.variants.options." + chip, label.StartsWith("web.") ? label : "web.home.rows.variants.options." + label, hint == null ? null : "web.home.rows.variants.options." + hint, values);
    private static AdminHomeSectionVariantFamily F(string control, string follows, string[] keys, params AdminHomeSectionVariant[] options) =>
        new("web.home.rows.variants.controls." + control, "web.home.rows.variants.follows." + follows, keys, options);
    private static AdminHomeSectionVariant Mood(string key, string label, string mood) => O(key, label, label, null, V(("mood", mood)));
    private const string Themes = "web.home.rows.variants.themes.";

    private static readonly IReadOnlyDictionary<string, AdminHomeSectionVariantFamily> Families = new Dictionary<string, AdminHomeSectionVariantFamily>(StringComparer.Ordinal)
    {
        ["continue_watching"] = F("kind", "kind", ["continue_type"],
            O("continue_watching_default", "web.home.rows.sections.watching", "web.home.rows.sections.watching", "watchingHint", V(("continue_type", "watching"))),
            O("continue_listening_default", "web.home.rows.sections.listening", "web.home.rows.sections.listening", "listeningHint", V(("continue_type", "listening")))),
        ["trending_on_server"] = F("timeWindow", "timeWindow", ["window"],
            O("tr_24h", "hours24", "last24Hours", "hotToday", V(("window", "24h"))), O("tr_7d", "days7", "last7Days", "weeklyMix", V(("window", "7d"))),
            O("tr_30d", "days30", "last30Days", "changesSlowly", V(("window", "30d")))),
        ["most_watched"] = F("period", "period", ["window"],
            O("mw_week", "thisWeek", "thisWeek", "last7DaysHint", V(("window", "week"))), O("mw_month", "thisMonth", "thisMonth", "last30DaysHint", V(("window", "month")))),
        ["trending_discover"] = F("period", "period", ["source", "window"],
            O("tdisc_tmdb_day", "today", "today", "changesDaily", V(("source", "tmdb"), ("window", "day"))),
            O("tdisc_tmdb_week", "thisWeek", "thisWeek", "steadierWeekly", V(("source", "tmdb"), ("window", "week")))),
        ["mood_collection"] = F("mood", "mood", ["mood"],
            Mood("mood_feel_good", "feelGood", "feel_good"), Mood("mood_mind_bending", "mindBending", "mind_bending"), Mood("mood_comfort", "comfort", "comfort"),
            Mood("mood_edge_of_seat", "edgeOfSeat", "edge_of_seat"), Mood("mood_tearjerker", "tearjerker", "tearjerker"), Mood("mood_quiet_sunday", "quietSunday", "quiet_sunday"),
            Mood("mood_date_night", "dateNight", "date_night"), Mood("mood_after_midnight", "afterMidnight", "after_midnight")),
        ["seasonal_themed"] = F("theme", "theme", ["enabled_themes", "theme"],
            O("se_auto", "holidays", "holidays", "holidaysHint", V(("enabled_themes", HolidayThemes))),
            O("se_family_movie_night", Themes + "familyMovieNight", Themes + "familyMovieNight", "familyHint", V(("enabled_themes", new[] { "family_movie_night" })))),
        ["editorial_spotlight"] = F("spotlightOn", "spotlightOn", ["subject_type", "subject"],
            O("es_director_auto", "director", "director", "directorHint", V(("subject_type", "director"))), O("es_actor", "actor", "actor", "actorHint", V(("subject_type", "actor"))),
            O("es_studio", "studio", "studio", "studioHint", V(("subject_type", "studio"))), O("es_era_80s", "eighties", "eighties", "eightiesHint", V(("subject_type", "era"), ("subject", "1980s")))),
        ["format_showcase"] = F("format", "format", ["format", "sort"],
            O("fs_4k", "fourK", "fourK", "fourKHint", V(("format", "4k"))), O("fs_4k_recent", "newIn4k", "newIn4k", "newIn4kHint", V(("format", "4k"), ("sort", "recent"))),
            O("fs_dv", "dolbyVision", "dolbyVision", "dolbyVisionHint", V(("format", "dolby_vision"))), O("fs_hdr", "hdr", "hdr", "hdrHint", V(("format", "hdr"))))
    };

    /// <summary>Every catalog key the kind, group, variant and theme names are built from, so a missing one is caught by a test.</summary>
    public static IEnumerable<string> TextKeys()
    {
        foreach (var kind in Kinds.Values) { yield return "web.home.rows.kinds." + kind.Key; yield return "web.home.rows.kindSentences." + kind.Key; }
        yield return "web.home.rows.kinds.unknown"; yield return "web.home.rows.kindSentences.unknown";
        foreach (var group in GroupOrder) { yield return "web.home.rows.groups." + group; yield return "web.home.rows.groups." + group + "Blurb"; }
        foreach (var family in Families.Values)
        {
            yield return family.ControlKey; yield return family.FollowsKey;
            foreach (var option in family.Options) { yield return option.ChipKey; yield return option.LabelKey; if (option.HintKey != null) yield return option.HintKey; }
        }
        foreach (var theme in new[] { "valentines", "st_patricks", "easter", "thanksgiving", "new_year", "christmas", "halloween", "saturday_morning", "family_movie_night", "summer_blockbuster" }) yield return ThemeLabelKey(theme)!;
    }

    public static AdminHomeSectionVariantFamily? Family(string sectionType) => Families.GetValueOrDefault(sectionType);
    public static IReadOnlyList<string> VariantKeys(string sectionType) => Families.TryGetValue(sectionType, out var family) ? family.Keys : [];

    /// <summary>Continue Reading rows have no variant control.</summary>
    public static bool VariantLocked(string sectionType, JsonObject config) => sectionType == "continue_watching" && config["continue_type"]?.ToString() == "reading";

    /// <summary>The name on Edit row's Shows line: the kind, plus its variant when the config matches one.</summary>
    public static string ShowsLabel(string sectionType, JsonObject config)
    {
        if (NotOfferedForNewRows.Contains(sectionType)) return RetiredLabel(sectionType, config);
        if (sectionType == "continue_watching" && config["continue_type"]?.ToString() == "reading") return T("web.home.rows.variants.continueReading");
        var kind = Label(sectionType);
        var key = AdminHomeSectionsRules.VariantOf(sectionType, config);
        var chip = key == null ? null : Families[sectionType].Options.FirstOrDefault(option => option.PresetKey == key)?.ChipKey;
        if (chip != null) return T("web.home.rows.variants.kindAndVariant", new { kind, variant = T(chip) });
        if (sectionType == "seasonal_themed" && config["theme"] is JsonValue theme && theme.TryGetValue<string>(out var holidayKey))
        {
            var label = ThemeLabelKey(holidayKey);
            if (label != null) return T("web.home.rows.variants.kindOneHoliday", new { kind, holiday = T(label) });
        }
        return kind;
    }

    /// <summary>The label key of a holiday theme (valentines, st_patricks, ...), or null for an unknown one.</summary>
    public static string? ThemeLabelKey(string theme) => theme switch
    {
        "valentines" => Themes + "valentines", "st_patricks" => Themes + "stPatricks", "easter" => Themes + "easter", "thanksgiving" => Themes + "thanksgiving",
        "new_year" => Themes + "newYear", "christmas" => Themes + "christmas", "halloween" => Themes + "halloween", "saturday_morning" => Themes + "saturdayMorning",
        "family_movie_night" => Themes + "familyMovieNight", "summer_blockbuster" => Themes + "summerBlockbuster", _ => null
    };

    /// <summary>Switches a row's config to another preset of its kind: only the family's discriminator keys change; every other key stays.</summary>
    public static JsonObject ApplyVariant(string sectionType, JsonObject config, string presetKey)
    {
        var next = (JsonObject)config.DeepClone();
        var option = Families.GetValueOrDefault(sectionType)?.Options.FirstOrDefault(entry => entry.PresetKey == presetKey);
        if (option == null) return next;
        if (sectionType == "seasonal_themed")
        {
            next.Remove("theme");
            // Custom holidays outrank every theme and only Holidays shows them or the holidays' own dates.
            if (presetKey == "se_family_movie_night") { next.Remove("custom_holidays"); next.Remove("theme_dates"); }
            next["enabled_themes"] = option.Values["enabled_themes"]!.DeepClone();
            return next;
        }
        if (sectionType == "editorial_spotlight")
        {
            next.Remove("subject");
            if (presetKey == "es_era_80s") { next.Remove("auto_rotate"); next.Remove("rotation_cadence"); }
            else
            {
                var rotatingPerson = config["subject_type"]?.ToString() is "director" or "actor" or "studio" && config["auto_rotate"] is JsonValue rotate && rotate.TryGetValue<bool>(out var auto) && auto;
                // A pinned row (era, person or studio) loses its subject here; the server needs one unless the row rotates, so it takes the preset's rotation.
                if (!rotatingPerson) { next["auto_rotate"] = true; next["rotation_cadence"] = "weekly"; }
            }
            foreach (var pair in option.Values) next[pair.Key] = pair.Value?.DeepClone();
            return next;
        }
        foreach (var key in Families[sectionType].Keys) next.Remove(key);
        foreach (var pair in option.Values) next[pair.Key] = pair.Value?.DeepClone();
        return next;
    }

    /// <summary>The preset name a row's kind and variant carry, for the row-name placeholder and "follows the variant" naming.</summary>
    public static string? PresetName(AdminHomeSectionCatalog? catalog, string sectionType, string? presetKey) =>
        presetKey == null ? null : catalog?.Categories.SelectMany(group => group.Recipes).FirstOrDefault(recipe => recipe.Type == sectionType)?.Presets.FirstOrDefault(preset => preset.Key == presetKey)?.DisplayName;
}
