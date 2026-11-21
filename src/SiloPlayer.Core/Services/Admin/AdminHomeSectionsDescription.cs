using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminHomeSectionPartStyle { Plain, Strong, Warning }
public readonly record struct AdminHomeSectionPart(string Text, AdminHomeSectionPartStyle Style = AdminHomeSectionPartStyle.Plain);

/// <summary>What the page knows about a collection a row shows: Found, Gone (the list loaded and it is not there) or Unknown (not loaded yet).</summary>
public readonly record struct AdminHomeSectionCollectionLookup(string State, string? Title = null, string? Kind = null)
{
    public static readonly AdminHomeSectionCollectionLookup Unknown = new("unknown");
    public static readonly AdminHomeSectionCollectionLookup Gone = new("gone");
    public static AdminHomeSectionCollectionLookup Found(string title, string? kind) => new("found", title, kind);
}

/// <summary>Port of the official row description sentences (217ff25 web/src/lib/homeRows/describe.ts, admin surface).</summary>
public static class AdminHomeSectionsDescription
{
    private const string Strong = "";
    private static string T(string key, object? values = null) => AdminHomeSectionsText.T(key, values);

    private static readonly Dictionary<string, string> ServerWindows = new() { ["24h"] = "hours24", ["7d"] = "days7", ["30d"] = "days30" };
    private static readonly Dictionary<string, string> SortOrders = new()
    {
        ["rating_imdb:desc"] = "rating", ["rating_tmdb:desc"] = "rating", ["rating_rt_critic:desc"] = "rating", ["rating_rt_audience:desc"] = "rating",
        ["release_date:desc"] = "released", ["year:desc"] = "released", ["plays:desc"] = "plays", ["title:asc"] = "title"
    };
    private static readonly Dictionary<string, string> SortSummaries = new()
    {
        ["rating"] = "web.home.rows.describe.sortHighestRated", ["released"] = "web.home.addRow.params.orderReleaseDesc",
        ["plays"] = "web.home.rows.describe.sortMostPlayed", ["title"] = "web.home.addRow.params.orderTitleAsc"
    };
    private static readonly HashSet<string> Scopes = ["movie", "series", "episode", "audiobook", "ebook", "manga", "video"];
    private static readonly Dictionary<string, string> FormatNames = new() { ["4k"] = "4K", ["dolby_vision"] = "Dolby Vision", ["hdr"] = "HDR" };
    private static readonly Dictionary<string, string> Cadences = new() { ["daily"] = "day", ["weekly"] = "week", ["monthly"] = "month" };
    private static readonly Dictionary<string, string> AdminSentences = new()
    {
        ["next_up"] = "nextUp", ["next_in_series"] = "nextInSeries", ["watchlist"] = "watchlist", ["favorites"] = "favorites",
        ["returning_shows"] = "returningShows", ["recommended_for_you"] = "recommended", ["because_you_watched"] = "becauseYouWatched", ["taste_match"] = "tasteMatch"
    };
    private static readonly Dictionary<string, string> FixedSentences = new()
    {
        ["recently_released"] = "web.home.rows.describe.recentlyReleased", ["profile_activity_feed"] = "web.home.rows.describe.activityFeed",
        ["similar_users_liked"] = "web.home.rows.describe.similarUsersLiked", ["mood_collection"] = "web.home.rows.describe.moodPick",
        ["random"] = "web.home.rows.describe.randomMix", ["anniversaries"] = "web.home.rows.describe.anniversaries", ["genre"] = "web.home.rows.describe.genre",
        ["admin_curated_list"] = "web.home.rows.describe.curated", ["award_winners"] = "web.home.rows.kinds.awardWinners"
    };
    private const string MinVotes = "100", AcclaimedMinVotes = "500";

    /// <summary>The catalog keys of the per-kind sentences, for the text coverage test.</summary>
    public static IEnumerable<string> SentenceKeys => AdminSentences.Values.Select(name => "web.home.rows.describe." + name).Concat(FixedSentences.Values);

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) && text != "" ? text : null;
    private static double? Num(JsonNode? node) => node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;
    private static double Positive(JsonNode? node, double fallback) => Num(node) is { } number && number > 0 ? number : fallback;
    private static string Rating(JsonNode? node, double fallback) => (Num(node) is { } number && number != 0 ? number : fallback).ToString("0.0", AppStrings.FormatCulture);
    private static string Cadence(JsonObject config) => Cadences.GetValueOrDefault(Str(config["rotation_cadence"]) ?? "weekly", "week");

    /// <summary>The row's media scope as the rule rows read it, "other" when it names none the messages know.</summary>
    public static string ScopeOf(JsonObject config)
    {
        string? scope = null;
        foreach (var name in new[] { "movie", "series", "episode", "audiobook", "ebook", "manga" })
            if (Str(config["media_scope"]) == name || Str(config["filter_type"]) == name) { scope = name; break; }
        scope ??= Str(config["media_scope"]) == "video" ? "video" : null;
        return scope != null && Scopes.Contains(scope) ? scope : "other";
    }

    private static (string Key, bool NewestAdded) SortKey(JsonObject config)
    {
        string field, order;
        if (config["sort"] is JsonObject sort)
        {
            field = Str(sort["field"]) ?? "added_at"; order = Str(sort["order"]) is "asc" or "desc" ? Str(sort["order"])! : DefaultOrder(field);
        }
        else
        {
            field = Str(config["sort"]) ?? "added_at"; order = Str(config["order"]) is "asc" or "desc" ? Str(config["order"])! : DefaultOrder(field);
        }
        return (field + ":" + order, field == "added_at" && order == "desc");
    }
    private static string DefaultOrder(string field) => field is "title" or "author" or "series" or "narrator" ? "asc" : "desc";

    /// <summary>How a rule row is ordered, as its More options summary names it.</summary>
    public static string RuleSortSummary(JsonObject config)
    {
        var (key, newest) = SortKey(config);
        if (newest) return T("web.home.addRow.params.orderAddedDesc");
        return T(SortOrders.TryGetValue(key, out var order) && SortSummaries.TryGetValue(order, out var summary) ? summary : "web.home.rows.describe.sortCustom");
    }

    private static string DescribeRules(JsonObject config)
    {
        var scope = ScopeOf(config);
        var rules = config["groups"] is JsonArray groups ? groups.OfType<JsonObject>().Sum(group => (group["rules"] as JsonArray)?.Count ?? 0) : 0;
        if (rules > 0) return T("web.home.rows.describe.rulesMatching", new { scope, count = (long)rules });
        var (key, newest) = SortKey(config);
        if (newest) return T("web.home.rows.describe.rulesAll", new { scope });
        return SortOrders.TryGetValue(key, out var order) ? T("web.home.rows.describe.rulesAllSorted", new { scope, order }) : T("web.home.rows.describe.rulesChosenOrder", new { scope });
    }

    private static string DescribeSpotlight(JsonObject config)
    {
        var type = Str(config["subject_type"]); var subject = Str(config["subject"]);
        if (type == "era") return subject != null ? T("web.home.rows.describe.spotlightEra", new { subject }) : T("web.home.rows.describe.spotlightOneEra");
        if (type is "director" or "actor" or "studio")
        {
            if (config["auto_rotate"] is JsonValue { } rotate && rotate.TryGetValue<bool>(out var auto) && !auto && subject != null) return T("web.home.rows.describe.spotlightPinned", new { subject });
            return T("web.home.rows.describe.spotlightRotating", new { subject_type = type, cadence = Cadence(config) });
        }
        return T("web.home.rows.describe.spotlightChanging");
    }

    private static string DescribeFormat(JsonObject config)
    {
        if (!FormatNames.TryGetValue(Str(config["format"]) ?? "", out var format)) return T("web.home.rows.describe.formatAny");
        return Str(config["sort"]) == "recent" ? T("web.home.rows.describe.formatRecent", new { format }) : T("web.home.rows.describe.formatTitles", new { format });
    }

    private static int LibraryCount(JsonObject config) => AdminHomeSectionsRules.LibraryFilterIds(config).Count;

    private static List<AdminHomeSectionPart> WithStrong(string text, string strong)
    {
        var split = text.Split(Strong); var parts = new List<AdminHomeSectionPart>();
        if (split[0] != "") parts.Add(new(split[0]));
        parts.Add(new(strong, AdminHomeSectionPartStyle.Strong));
        if (split.Length > 1 && split[1] != "") parts.Add(new(split[1]));
        return parts;
    }

    private static string? RefId(JsonNode? node) => node is JsonObject entry ? Str(entry["library_collection_id"]) ?? Str(entry["user_collection_id"]) : null;

    /// <summary>The library collection ids a row of collections lists, in order.</summary>
    public static IReadOnlyList<string> CollectionIds(JsonObject config) =>
        config["collections"] is JsonArray list ? list.Select(entry => entry is JsonObject obj ? Str(obj["library_collection_id"]) ?? Str(obj["user_collection_id"]) : null).Where(id => id != null).Select(id => id!).ToArray() : [];

    private static List<AdminHomeSectionPart> DescribeCollection(JsonObject config, Func<string, AdminHomeSectionCollectionLookup>? lookup)
    {
        var id = Str(config["library_collection_id"]) ?? Str(config["user_collection_id"]);
        var found = id == null || lookup == null ? AdminHomeSectionCollectionLookup.Unknown : lookup(id);
        if (found.State == "gone") return [new(T("web.home.rows.describe.collectionGone"), AdminHomeSectionPartStyle.Warning)];
        if (found.State != "found") return [new(T("web.home.rows.describe.collectionUnknown"))];
        return WithStrong(T("web.home.rows.describe.collection", new { owner = "other", title = Strong, has_kind = found.Kind != null ? "yes" : "no", kind = found.Kind ?? "" }), found.Title!);
    }

    private static List<AdminHomeSectionPart> DescribeCollectionList(JsonObject config, Func<string, AdminHomeSectionCollectionLookup>? lookup)
    {
        var found = CollectionIds(config).Select(id => lookup?.Invoke(id) ?? AdminHomeSectionCollectionLookup.Unknown).ToList();
        var gone = found.Count(entry => entry.State == "gone");
        var names = found.Where(entry => entry.State == "found").Select(entry => entry.Title!).ToList();
        var count = found.Count - gone; var parts = new List<AdminHomeSectionPart>();
        if (names.Count == 0)
        {
            if (count > 0 || gone == 0) parts.Add(new(T("web.home.rows.describe.collectionCount", AdminHomeSectionsText.Count(count))));
        }
        else
        {
            var more = names.Count > 3 ? names.Count - 2 : 0;
            var split = T("web.home.rows.describe.collectionList", AdminHomeSectionsText.Count(count, ("names", Strong))).Split(Strong);
            if (split[0] != "") parts.Add(new(split[0]));
            var listed = more > 0 ? names.Take(2).ToList() : names;
            for (var i = 0; i < listed.Count; i++)
            {
                if (i > 0) parts.Add(new(i == listed.Count - 1 && more == 0 ? " and " : ", "));
                parts.Add(new(listed[i], AdminHomeSectionPartStyle.Strong));
            }
            if (more > 0) parts.Add(new(" and " + T("web.home.rows.describe.collectionListMore", AdminHomeSectionsText.Count(more))));
            if (split.Length > 1 && split[1] != "") parts.Add(new(split[1]));
        }
        if (gone > 0)
        {
            if (parts.Count > 0) parts.Add(new(" "));
            parts.Add(new(T("web.home.rows.describe.collectionsGone", AdminHomeSectionsText.Count(gone)), AdminHomeSectionPartStyle.Warning));
        }
        return parts;
    }

    /// <summary>One plain sentence saying what a row shows; personalized rows say "each viewer" on the admin surface.</summary>
    public static IReadOnlyList<AdminHomeSectionPart> Describe(AdminHomeSection row, bool onHome, Func<string, AdminHomeSectionCollectionLookup>? collections = null)
    {
        var config = row.Config.ValueKind == System.Text.Json.JsonValueKind.Object ? JsonNode.Parse(row.Config.GetRawText())!.AsObject() : new JsonObject();
        if (AdminSentences.TryGetValue(row.SectionType, out var admin)) return [new(T("web.home.rows.describe." + admin))];
        if (FixedSentences.TryGetValue(row.SectionType, out var fixedKey)) return [new(T(fixedKey))];
        string Plain(string text) => text;
        switch (row.SectionType)
        {
            case "continue_watching":
                return [new(T(Str(config["continue_type"]) switch { "listening" => "web.home.rows.describe.cwListening", "reading" => "web.home.rows.describe.cwReading", _ => "web.home.rows.describe.cwWatching" }))];
            case "recently_added":
                if (!onHome) return [new(T("web.home.rows.describe.recentlyAddedLibrary", new { scope = ScopeOf(config) }))];
                var libraries = LibraryCount(config);
                return [new(libraries == 0 ? T("web.home.rows.describe.recentlyAddedAll") : T("web.home.rows.describe.recentlyAddedFrom", new { count = (long)libraries }))];
            case "new_to_library":
                var days = (long)(Num(config["lookback_days"]) ?? 30);
                return [new(T("web.home.rows.describe.newToLibrary", new { count = days, days }))];
            case "trending_on_server":
                if (ServerWindows.TryGetValue(Str(config["window"]) ?? "", out var window))
                    return [new(T(onHome ? "web.home.rows.describe.trendingHome" : "web.home.rows.describe.trendingHere", new { window }))];
                return [new(T(onHome ? "web.home.rows.describe.trendingHomeLately" : "web.home.rows.describe.trendingHereLately"))];
            case "most_watched":
                return [new(T("web.home.rows.describe.mostWatched", new { window = Str(config["window"]) == "month" ? "month" : "week" }))];
            case "trending_discover":
                var discoverWindow = Str(config["window"]) == "day" ? "day" : "week";
                return Str(config["filter_type"]) is "movie" or "series"
                    ? [new(T("web.home.rows.describe.trendingDiscoverFiltered", new { media = Str(config["filter_type"]), window = discoverWindow }))]
                    : [new(T("web.home.rows.describe.trendingDiscover", new { window = discoverWindow }))];
            case "seasonal_themed":
                var themes = config["enabled_themes"];
                var family = themes is JsonArray { Count: 1 } list && list[0]?.ToString() == "family_movie_night" || themes == null && Str(config["theme"]) == "family_movie_night";
                return [new(T(family ? "web.home.rows.describe.seasonalFamily" : "web.home.rows.describe.seasonal"))];
            case "editorial_spotlight": return [new(DescribeSpotlight(config))];
            case "format_showcase": return [new(DescribeFormat(config))];
            case "hidden_gems":
                var plays = (long)Math.Max(Num(config["max_play_count"]) ?? 0, 0);
                return [new(T("web.home.rows.describe.hiddenGems", new { rating = Rating(config["min_rating"], 7.5), votes = MinVotes, plays, plays_text = plays.ToString() }))];
            case "critically_acclaimed":
                return [new(T("web.home.rows.describe.acclaimed", new { rating = Rating(config["min_score"], 8), votes = AcclaimedMinVotes }))];
            case "forgotten_favorites":
                var back = (long)Positive(config["lookback_days"], 365);
                return [new(T("web.home.rows.describe.forgotten", new { rating = 7.0.ToString("0.0", AppStrings.FormatCulture), votes = MinVotes, count = back, days = back }))];
            case "genre_roulette":
                return [new(T("web.home.rows.describe.genreRoulette", new { cadence = Cadence(config), rating = Rating(config["min_rating"], 6), votes = MinVotes }))];
            case "short_watches":
                var minutes = (long)Positive(config["max_minutes"], 95);
                return [new(T("web.home.rows.describe.shortWatches", new { count = minutes, minutes, rating = Rating(config["min_rating"], 6), votes = MinVotes }))];
            case "collection": return DescribeCollection(config, collections);
            case "collection_posters": return DescribeCollectionList(config, collections);
            case "custom_filter": return [new(DescribeRules(config))];
            default: return [new(Plain(AdminHomeSectionsKinds.Label(row.SectionType)))];
        }
    }

    /// <summary>"12 titles".</summary>
    public static string TitleCount(long count) => T("web.home.rows.describe.titleCount", AdminHomeSectionsText.Count(count));

    /// <summary>The switch's accessible name says what it does.</summary>
    public static string SwitchLabel(string title, bool shown) => T("web.home.rows.describe.switchAdmin", new { shown = shown ? "yes" : "no", row = title });

    /// <summary>The line after the bold title on a row that is off.</summary>
    public static string CollapsedText() => " " + T("web.home.rows.describe.collapsedAdmin");
}
