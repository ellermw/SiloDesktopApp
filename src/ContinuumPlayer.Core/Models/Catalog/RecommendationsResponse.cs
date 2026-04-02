namespace ContinuumPlayer.Core.Models.Catalog;

public class RecommendationsResponse
{
    public List<RecommendationRow> Rows { get; set; } = [];
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
