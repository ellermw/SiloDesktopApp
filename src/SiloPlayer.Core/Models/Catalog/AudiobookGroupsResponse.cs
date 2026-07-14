namespace SiloPlayer.Core.Models.Catalog;

public sealed class AudiobookGroupsResponse
{
    public int Total { get; set; }
    public bool TotalExact { get; set; }
    public bool HasMore { get; set; }
    public List<AudiobookGroup> Groups { get; set; } = [];
}

public sealed class AudiobookGroup
{
    public string Name { get; set; } = "";
    public int ItemCount { get; set; }
    public int TotalDurationSeconds { get; set; }
    public int InProgressCount { get; set; }
    public int FinishedCount { get; set; }
    public List<string> PosterUrls { get; set; } = [];
}
