namespace ContinuumPlayer.Core.Models.Catalog;

public class SimilarResponse
{
    public List<SimilarItem> Items { get; set; } = [];
}

public class SimilarItem
{
    public string MediaItemId { get; set; } = "";
    public double Score { get; set; }
    public string? Reason { get; set; }
    public string? ReasonDetail { get; set; }
}
