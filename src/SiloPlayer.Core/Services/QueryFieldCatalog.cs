namespace SiloPlayer.Core.Services;

public sealed record QueryFieldDefinition(string Name, string Label, string Group, string Kind,
    string[] Operators, bool Extended = false, bool Hidden = false, bool ShowsOnly = false,
    bool NoEpisode = false, string? RatingSource = null, string? Unit = null);

/// <summary>Current public collectionBuilderFields contract, shared by all native query editors.</summary>
public static class QueryFieldCatalog
{
    private static readonly string[] Is = ["is", "is_not"];
    private static readonly string[] Number = ["gte", "lte", "gt", "lt", "between"];
    private static readonly string[] Date = ["in_last", "not_in_last", "lt", "gt", "between"];
    public static readonly IReadOnlyList<QueryFieldDefinition> All = [
        new("title", "Title", "Title", "text", ["contains", "not_contains", "is", "is_not", "begins_with", "ends_with"], Extended: true),
        new("genre", "Genre", "Title", "facet", ["is", "is_not", "contains"]),
        new("studio", "Studio", "Title", "facet", Is), new("network", "Network", "Title", "facet", Is),
        new("country", "Country", "Title", "facet", Is), new("content_rating", "Content rating", "Title", "facet", Is),
        new("original_language", "Original language", "Title", "language", Is), new("type", "Type", "Title", "select", Is),
        new("year", "Year", "Title", "number", ["is", "is_not", ..Number]),
        new("decade", "Decade", "Title", "select", Is, Extended: true),
        new("release_date", "Release date", "Title", "date", Date),
        new("last_air_date", "Latest episode aired", "Title", "date", Date, Extended: true, ShowsOnly: true),
        new("runtime", "Duration", "Title", "number", Number, Extended: true, Unit: "min"),
        new("rating_imdb", "IMDb rating", "Title", "number", Number),
        new("rating_tmdb", "TMDB rating", "Title", "number", Number, Extended: true),
        new("rating_rt_critic", "RT critic score", "Title", "number", Number, Extended: true, NoEpisode: true, RatingSource: "rt_critic"),
        new("rating_rt_audience", "RT audience score", "Title", "number", Number, Extended: true, NoEpisode: true, RatingSource: "rt_audience"),
        new("status", "Status", "Title", "select", Is, Hidden: true),
        new("actor", "Actor", "People", "person", Is), new("director", "Director", "People", "person", Is),
        new("writer", "Writer", "People", "person", Is), new("producer", "Producer", "People", "person", Is),
        new("resolution", "Resolution", "File", "select", Is), new("hdr", "HDR", "File", "boolean", ["is"]),
        new("dolby_vision", "Dolby Vision", "File", "boolean", ["is"]),
        new("audio_language", "Audio language", "File", "language", Is), new("subtitle_language", "Subtitle language", "File", "language", Is),
        new("bitrate", "Bitrate", "File", "number", Number, Unit: "kbps"),
        new("watched", "Watched", "You", "boolean", ["is"]), new("favorited", "Favorited", "You", "boolean", ["is"]),
        new("in_watchlist", "In watchlist", "You", "boolean", ["is"]), new("in_progress", "In progress", "You", "boolean", ["is"]),
        new("last_watched", "Last watched", "You", "date", Date), new("added_at", "Added", "Library", "date", Date),
        new("latest_episode_added", "Latest episode added", "Library", "date", Date, Extended: true, ShowsOnly: true),
    ];
    public static QueryFieldDefinition? Get(string name) => All.FirstOrDefault(field => field.Name == (name == "rating" ? "rating_imdb" : name));
    public static IEnumerable<QueryFieldDefinition> Offered(string? scope, bool extended, IReadOnlySet<string>? shownRatings, bool personalized = true)
        => All.Where(field => !field.Hidden && (!field.Extended || extended)
            && (personalized || field.Group != "You") && (!field.ShowsOnly || scope is null or "all" or "video" or "series" or "tv")
            && (!field.NoEpisode || scope != "episode") && (field.RatingSource == null || shownRatings == null || shownRatings.Contains(field.RatingSource)));
    public static string[] OfferedOperators(string field, string current, bool extended)
        => (Get(field)?.Operators ?? []).Where(op => (field != "genre" || op != "contains" || current == op)
            && (op != "not_in_last" || extended || current == op)).ToArray();
}
