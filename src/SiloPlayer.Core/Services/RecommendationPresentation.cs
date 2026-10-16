namespace SiloPlayer.Core.Services;

public static class RecommendationPresentation
{
    public static string PageSubtitle(IEnumerable<string> rowTypes)
    {
        var types = rowTypes.ToList();
        if (types.Count == 0) return "";
        return types.Any(type => type is "cluster" or "similar_users_liked")
            ? "Personalized picks based on your viewing history and ratings."
            : "Highly rated and recently added titles from your library. Watch and rate titles to personalize this page.";
    }

    public static string FallbackTitle(string kind, string? key) => kind switch
    {
        "for-you-main" => "For You",
        "cluster" => string.IsNullOrWhiteSpace(key) ? "Personalized cluster" : $"Personalized cluster {key}",
        "similar-users" => "Profiles Like You Enjoyed",
        "popular" => "Popular on This Server",
        "recently-added" => "Recently Added",
        "top-rated" => "Highly Rated in Your Library",
        "genre" => string.IsNullOrWhiteSpace(key) ? "Genre picks" : $"Top {key}",
        _ => "Recommendations"
    };
}
