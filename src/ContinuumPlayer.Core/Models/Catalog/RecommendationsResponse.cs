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
