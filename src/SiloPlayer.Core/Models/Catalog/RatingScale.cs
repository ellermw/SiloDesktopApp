using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Models.Catalog;

/// <summary>
/// The viewer's own rating as the server stores and serves it (/api/v2/ratings and
/// <c>user_rating</c>): a score from 1 to 10, shown as five stars with half stars,
/// so 7 is three and a half stars. The rules follow the WebUI's StarRating control
/// (silo-server #4398): every half star is a score, a click or tap on either half
/// of a star picks the whole star, and the star already set clears the rating.
/// </summary>
public static class RatingScale
{
    public const int StarCount = 5;
    public const int MinScore = 1;
    public const int MaxScore = StarCount * 2;

    public static bool IsScore(int score) => score is >= MinScore and <= MaxScore;

    /// <summary>The score of the whole star a half belongs to: 7 and 8 are both 8.</summary>
    public static int WholeStar(int score) => (score + 1) / 2 * 2;

    /// <summary>The star, 1 to 5, a score's half belongs to.</summary>
    public static int StarOf(int score) => (score + 1) / 2;

    /// <summary>The score as stars: 7 is 3.5.</summary>
    public static decimal Stars(int score) => score / 2m;

    /// <summary>True when the half belongs to the star that is already set, so picking it clears the rating.</summary>
    public static bool Clears(int score, int? rating) => rating.HasValue && WholeStar(score) == rating.Value;

    /// <summary>What a click or tap on a half sets: its whole star, or null when that star is the one set.</summary>
    public static int? ToggleStar(int score, int? rating)
    {
        var whole = WholeStar(score);
        return whole == rating ? null : whole;
    }

    /// <summary>What activating a half's own radio sets: its own score, or null when that is the rating.</summary>
    public static int? ToggleScore(int score, int? rating) => score == rating ? null : score;

    /// <summary>
    /// The arrow keys step half a star. Up, down, left and right all stop at the ends, and
    /// stepping down from no rating lands on half a star, as the WebUI does.
    /// </summary>
    public static int Step(int? rating, int direction)
        => direction >= 0
            ? Math.Min((rating ?? 0) + 1, MaxScore)
            : Math.Max((rating ?? 2) - 1, MinScore);

    /// <summary>True when the half is drawn filled for a shown score.</summary>
    public static bool IsFilled(int score, int? shown) => shown.HasValue && score <= shown.Value;

    /// <summary>The half that takes the keyboard Tab stop: the rating's own, or the first half when unrated.</summary>
    public static int TabbableScore(int? rating) => rating is { } score && IsScore(score) ? score : MinScore;

    /// <summary>
    /// What to hand the rating command, which toggles when given the current score.
    /// Null means nothing changes: the pick is the rating already set, or clearing an unrated title.
    /// </summary>
    public static int? CommandArgument(int? rating, int? picked)
    {
        if (picked == rating) return null;
        if (picked is { } score) return IsScore(score) ? score : null;
        return rating;
    }

    /// <summary>"3.5 stars", from the WebUI's web.item.rating.stars.</summary>
    public static string Label(int score)
        => AppStrings.Get("web.item.rating.stars", new Dictionary<string, object?> { ["count"] = Stars(score) },
            fallback: score == 2 ? "1 star" : $"{Stars(score).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} stars");
}
