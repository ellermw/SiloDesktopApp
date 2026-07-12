namespace SiloPlayer.Core.Models.Collections;

public class CollectionPreviewRequest
{
    public QueryDefinition? QueryDefinition { get; set; }
    public int? Limit { get; set; }
}

public class CollectionPreviewItem
{
    public string ContentId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
}

public class CollectionPreviewResponse
{
    public List<CollectionPreviewItem> Items { get; set; } = [];
    public int Total { get; set; }
}
