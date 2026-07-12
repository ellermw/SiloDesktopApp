namespace SiloPlayer.Core.Models.Catalog;

public class RatingResponse
{
    public int Rating { get; set; }
    public string? RatedAt { get; set; }
}

public class RatingListItem
{
    public string MediaItemId { get; set; } = "";
    public int Rating { get; set; }
    public string RatedAt { get; set; } = "";
}

public class RatingListResponse
{
    public List<RatingListItem> Ratings { get; set; } = [];
}
