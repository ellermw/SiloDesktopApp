namespace SiloPlayer.Core.Services;

/// <summary>The WebUI editor's visible fields; audiobook runtime and artwork come from files/Match.</summary>
public static class MetadataEditorFormPolicy
{
    public static IReadOnlyList<string> FieldsFor(string type, string section) => (type, section) switch
    {
        ("movie", "general") => ["title", "sort_title", "original_title", "overview", "tagline", "content_rating", "runtime"],
        ("series" or "audiobook", "general") => ["title", "sort_title", "original_title", "overview", "tagline", "content_rating"],
        ("season", "general") => ["title", "sort_title", "original_title", "overview", "tagline", "content_rating", "season_number"],
        ("episode", "general") => ["title", "sort_title", "original_title", "overview", "tagline", "content_rating", "runtime", "season_number", "episode_number"],
        ("movie", "dates") => ["year", "release_date", "rating_imdb", "rating_tmdb", "rating_rt_critic", "rating_rt_audience"],
        ("series", "dates") => ["year", "first_air_date", "last_air_date", "air_time", "air_timezone", "rating_imdb", "rating_tmdb", "rating_rt_critic", "rating_rt_audience"],
        ("season" or "episode", "dates") => ["air_date"],
        ("audiobook", "dates") => ["year", "release_date"],
        ("movie" or "audiobook", "tags") => ["genres", "studios", "countries"],
        ("series", "tags") => ["genres", "studios", "networks", "countries"],
        ("movie" or "series" or "season" or "episode", "ids") => ["imdb_id", "tmdb_id", "tvdb_id"],
        _ => [],
    };

    // A reset empties file-tag-derived audiobook values that providers do not supply.
    public static bool CanResetToProvider(string type) => type is "movie" or "series" or "season" or "episode";
    public static bool CanEditImages(string type, bool isAdmin) => isAdmin && (type is "movie" or "series" or "season");
}
