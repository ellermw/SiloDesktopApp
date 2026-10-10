using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>Profile Home row copy and preset inference from the current WebUI contract.</summary>
public static class HomeRecipePresentation
{
    public static GalleryPreset? FindMatchingPreset(RecipeDefinition definition, Dictionary<string, object>? config)
        => definition.Presets.Where(preset => preset.DefaultParams.All(pair => config != null && config.TryGetValue(pair.Key, out var value)
            && HomeSectionWritePolicy.EqualConfig(new() { [pair.Key] = value }, new() { [pair.Key] = pair.Value })))
            .OrderByDescending(preset => preset.DefaultParams.Count).FirstOrDefault();

    public static string TitleCount(int count) => $"{count} title{(count == 1 ? "" : "s")}";

    public static string Describe(SettingsSectionEntry row, bool onLibrary = false, string? fallback = null)
    {
        if (!string.IsNullOrEmpty(row.DefaultTitle) && row.Title != row.DefaultTitle) return $"Renamed from {row.DefaultTitle}";
        var config = row.Config ?? [];
        string Text(string key) => config.TryGetValue(key, out var value) && JsonSerializer.SerializeToElement(value) is { ValueKind: JsonValueKind.String } text ? text.GetString() ?? "" : "";
        double Number(string key, double otherwise) => config.TryGetValue(key, out var value) && JsonSerializer.SerializeToElement(value) is { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out var n) && double.IsFinite(n) ? n : otherwise;
        double Positive(string key, double otherwise) => Number(key, otherwise) is > 0 and var n ? n : otherwise;
        string Rating(string key, double otherwise) => (Number(key, otherwise) is 0 ? otherwise : Number(key, otherwise)).ToString("F1", CultureInfo.InvariantCulture);
        string Cadence() => Text("rotation_cadence") switch { "daily" => "day", "monthly" => "month", _ => "week" };
        string Noun() => Text("media_scope") switch { "movie" => "movies", "series" => "shows", "episode" => "episodes", "audiobook" => "audiobooks", "ebook" => "books", "manga" => "manga", "video" => "movies and shows", _ => "titles" };
        string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
        switch (row.SectionType)
        {
            case "continue_watching": return Text("continue_type") switch { "listening" => "Audiobooks you're partway through", "reading" => "Books you're partway through", _ => "What you're partway through" };
            case "next_up": return "The next episode of every show you follow";
            case "next_in_series": return "The next audiobook in each series you finished";
            case "watchlist": return "What you saved to watch";
            case "favorites": return "Your favorites";
            case "returning_shows": return "Shows you watched that have a new season";
            case "recommended_for_you": return "Picked from your watch history";
            case "because_you_watched": return "More like what you watched last";
            case "taste_match": return "The best matches for your taste today";
            case "similar_users_liked": return "What profiles with similar taste enjoyed";
            case "recently_added":
                if (onLibrary) return Text("media_scope") == "" ? "Newest additions to this library" : $"Newest {Noun()} in this library";
                var libraries = config.TryGetValue("library_ids", out var ids) && JsonSerializer.SerializeToElement(ids) is { ValueKind: JsonValueKind.Array } list ? list.GetArrayLength() : 0;
                return libraries == 0 ? "Newest movies and episodes from all libraries" : $"Newest additions from {libraries} librar{(libraries == 1 ? "y" : "ies")}";
            case "recently_released": return "Newest by release date";
            case "new_to_library": return $"Added in the last {Number("lookback_days", 30)} days";
            case "trending_on_server":
                var window = Text("window") switch { "24h" => "24 hours", "7d" => "7 days", "30d" => "30 days", _ => "" };
                return $"Most played {(onLibrary ? "here" : "on this server")} {(window == "" ? "lately" : "in the last " + window)}";
            case "most_watched": return $"Watched most often here this {(Text("window") == "month" ? "month" : "week")}";
            case "trending_discover": return $"Trending on TMDB {(Text("window") == "day" ? "today" : "this week")}, from what you have";
            case "profile_activity_feed": return "What other profiles just watched";
            case "mood_collection": return "Mood pick";
            case "seasonal_themed":
                var family = config.TryGetValue("enabled_themes", out var themes) ? JsonSerializer.SerializeToElement(themes) is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() == 1 && array[0].ValueKind == JsonValueKind.String && array[0].GetString() == "family_movie_night" : Text("theme") == "family_movie_night";
                return family ? "Family picks on Friday and Saturday evenings" : "Changes with the season";
            case "editorial_spotlight":
                var subjectType = Text("subject_type"); var subject = Text("subject");
                if (subjectType == "era") return subject == "" ? "Titles from one era" : $"Titles from the {subject}";
                if (subjectType is "director" or "actor" or "studio")
                {
                    var pinned = config.TryGetValue("auto_rotate", out var rotate) && JsonSerializer.SerializeToElement(rotate).ValueKind == JsonValueKind.False;
                    return pinned && subject != "" ? $"Titles from {subject}" : $"A different {subjectType} every {Cadence()}";
                }
                return "A changing spotlight";
            case "format_showcase":
                var format = Text("format") switch { "4k" => "4K", "dolby_vision" => "Dolby Vision", "hdr" => "HDR", "lossless_audio" => "lossless audio", _ => "" };
                return format == "" ? "Your best-looking titles" : Text("sort") == "recent" ? $"The latest {format} additions" : $"Titles in {format}";
            case "hidden_gems":
                var plays = Number("max_play_count", 0); var times = plays switch { 1 => "once", 2 => "twice", _ => $"{plays} times" };
                return $"Rated {Rating("min_rating", 7.5)}+ on TMDB with 100+ votes, and {(plays > 0 ? "watched " + times + " or less" : "never watched")}";
            case "critically_acclaimed": return $"Rated {Rating("min_score", 8)}+ on TMDB with 500+ votes";
            case "forgotten_favorites":
                var days = Positive("lookback_days", 365);
                return $"Rated 7.0+ on TMDB with 100+ votes, and not watched in the past {(days == 365 ? "year" : days + " day" + (days == 1 ? "" : "s"))}";
            case "genre_roulette": return $"A different genre every {Cadence()}, rated {Rating("min_rating", 6)}+ on TMDB with 100+ votes";
            case "short_watches": return $"Movies of {Positive("max_minutes", 95)} minutes or less, rated {Rating("min_rating", 6)}+ on TMDB with 100+ votes";
            case "random": return "A random mix";
            case "anniversaries": return "Titles marking a release anniversary this month";
            case "collection": return "A collection";
            case "custom_filter":
                var query = JsonSerializer.Deserialize<QueryDefinition>(JsonSerializer.Serialize(config), V2Json.Options) ?? new();
                var count = query.Groups.Sum(group => group.Rules.Count);
                if (count > 0) return $"{Capitalize(Noun())} matching {count} rule{(count == 1 ? "" : "s")}";
                var phrase = $"{query.Sort?.Field ?? "added_at"}:{query.Sort?.Order ?? "desc"}" switch { "rating_imdb:desc" or "rating_tmdb:desc" or "rating_rt_critic:desc" or "rating_rt_audience:desc" => "highest rated first", "release_date:desc" or "year:desc" => "newest released first", "plays:desc" => "most played first", "title:asc" => "A to Z", "added_at:desc" => "", _ => null };
                return phrase == null ? $"{Capitalize(Noun())} in a chosen order" : $"All {Noun()}{(phrase == "" ? "" : ", " + phrase)}";
            case "genre": return "Titles from chosen genres";
            case "admin_curated_list": return "Hand-picked titles";
            case "award_winners": return "Award winners (no longer offered)";
            default: return fallback ?? row.SectionType;
        }
    }
}
