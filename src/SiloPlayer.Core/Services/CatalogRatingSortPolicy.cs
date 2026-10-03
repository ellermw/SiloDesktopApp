namespace SiloPlayer.Core.Services;

public static class CatalogRatingSortPolicy
{
    public static bool IsAvailable(string field, IReadOnlySet<string> shownSources, string? savedEditorField = null)
    {
        var source = field switch { "rating_rt_critic" => "rt_critic", "rating_rt_audience" => "rt_audience", _ => null };
        return source == null || shownSources.Contains(source) || field == savedEditorField;
    }
    public static bool AppliesToScope(string field, string? scope)
    {
        if (string.IsNullOrEmpty(scope)) return true;
        var all = scope is "all" or "video" or "mixed";
        var video = all || scope is "movie" or "movies" or "series" or "tv" or "episode";
        return field switch
        {
            "last_air_date" => scope is "series" or "tv" or "episode",
            "latest_episode_added" => scope is "series" or "tv",
            "content_rating" or "rating_imdb" or "rating_tmdb" or "rating_rt_critic" or "rating_rt_audience" or "resolution" => video,
            "runtime" or "bitrate" => scope != "manga",
            "author" => scope is "audiobook" or "audiobooks" or "ebook" or "ebooks" or "manga",
            "narrator" => scope is "audiobook" or "audiobooks",
            "series" => scope is "audiobook" or "audiobooks" or "ebook" or "ebooks",
            _ => true,
        };
    }
}