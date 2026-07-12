namespace SiloPlayer.Core.Models.Admin;

public class RecommendationsStatus
{
    public JobStatus Embeddings { get; set; } = new();
    public JobStatus TasteProfiles { get; set; } = new();
    public JobStatus Cowatch { get; set; } = new();
    public JobStatus Recommendations { get; set; } = new();
}

public class JobStatus
{
    public bool Running { get; set; }
    public int Count { get; set; }
    public int? Total { get; set; }
}
