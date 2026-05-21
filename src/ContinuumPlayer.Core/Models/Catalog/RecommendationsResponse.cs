using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Models.Catalog;

public class RecommendationsResponse
{
    public List<RecommendationRow> Rows { get; set; } = [];
}

/// <summary>
/// Response for GET /api/v1/recommendations/watch-tonight. Returns a small
/// mixed list of in-progress, next-up, and discovery items suitable for a
/// "pick something to watch" quick-launcher.
/// </summary>
public class WatchTonightResponse
{
    public List<WatchTonightItem> Items { get; set; } = [];
    public bool IsCold { get; set; }
}

public class DiscoverResponse
{
    public List<DiscoverRow> Rows { get; set; } = [];
}

public class DiscoverRow
{
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public string? SectionKind { get; set; }
    public string? SectionKey { get; set; }
    public List<MediaItem> Items { get; set; } = [];
}

public class RecommendationSectionResponse
{
    public string Kind { get; set; } = "";
    public string? Key { get; set; }
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public List<MediaItem> Items { get; set; } = [];
}

public class WatchTonightItem : MediaItem
{
    /// <summary>
    /// One of "continue_watching", "next_up", or "recommendation". Used to
    /// tag each card with a source badge so the user knows why it was picked.
    /// </summary>
    public string WatchTonightSource { get; set; } = "";
}

/// <summary>
/// A single card in the gamified Watch Tonight swipe deck. Extends the flat
/// WatchTonightItem with extra metadata the card UI shows on tap (runtime,
/// cast list for the detail back-face).
/// </summary>
public class SwipeCard : WatchTonightItem
{
    public new int? Runtime { get; set; }
    public List<SwipeCardCastMember> Cast { get; set; } = [];
}

public class SwipeCardCastMember
{
    public string Name { get; set; } = "";
    public string? Character { get; set; }
    public string? PhotoUrl { get; set; }
}

/// <summary>
/// One page of swipe cards from <c>GET /recommendations/watch-tonight/cards</c>.
/// Client paginates by passing the <c>content_id</c>s of previously seen cards
/// via <c>exclude_ids[]</c> until <see cref="HasMore"/> is false.
/// </summary>
public class SwipeCardsPage
{
    public List<SwipeCard> Cards { get; set; } = [];
    public bool HasMore { get; set; }
    public bool IsCold { get; set; }
}

public class RecommendationRow
{
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public List<RecommendationItem> Items { get; set; } = [];
}

public class RecommendationItem
{
    public string MediaItemId { get; set; } = "";
    public double Score { get; set; }
}

public class TasteProfileResponse
{
    public List<TasteGenre> Genres { get; set; } = [];
    public List<TasteKeyword> Keywords { get; set; } = [];
    public List<TastePerson> FavoriteActors { get; set; } = [];
    public List<TastePerson> FavoriteDirectors { get; set; } = [];
}

public class TasteGenre
{
    public string Name { get; set; } = "";
    public double Weight { get; set; }
}

public class TasteKeyword
{
    public string Name { get; set; } = "";
    public double Weight { get; set; }
}

public class TastePerson
{
    public string Name { get; set; } = "";
    public double Weight { get; set; }
    public int? PersonId { get; set; }
}
