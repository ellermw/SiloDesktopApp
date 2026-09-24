namespace SiloPlayer.Core.Models.Catalog;

public class SimilarResponse
{
    public List<SimilarItem> Items { get; set; } = [];
}

public class SimilarItem : SiloPlayer.Core.Models.Home.MediaItem
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string MediaItemId { get => ContentId; set => ContentId = value; }
    public double Score { get; set; }
    public string? Reason { get; set; }
    public string? ReasonDetail { get; set; }
}
