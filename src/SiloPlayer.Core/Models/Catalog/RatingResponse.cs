namespace SiloPlayer.Core.Models.Catalog;

public class RatingResponse
{
    public int Rating { get; set; }
    public string? RatedAt { get; set; }
}

public class RatingListItem
{
    [System.Text.Json.Serialization.JsonPropertyName("item_id")]
    public string MediaItemId { get; set; } = "";
    public int Rating { get; set; }
    public string RatedAt { get; set; } = "";
}

public class RatingListResponse
{
    public List<RatingListItem> Ratings { get; set; } = [];
}
