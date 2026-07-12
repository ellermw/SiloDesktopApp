namespace SiloPlayer.Core.Models.Collections;

public class CollectionItem
{
    public string CollectionId { get; set; } = "";
    public string MediaItemId { get; set; } = "";
    public int Position { get; set; }
    public string AddedAt { get; set; } = "";
    public string? ContentId { get; set; }
    public string? Type { get; set; }
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
}

public class CollectionItemsResponse
{
    public List<CollectionItem> Items { get; set; } = [];
    public int Total { get; set; }
}
