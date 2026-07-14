namespace SiloPlayer.Core.Models.Catalog;

public sealed class ItemFile
{
    public int Id { get; set; }
    public int LibraryId { get; set; }
    public string FilePath { get; set; } = "";
    public string ObservedRootPath { get; set; } = "";
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
}

public sealed class ItemFilesResponse
{
    public List<ItemFile> Files { get; set; } = [];
}

public sealed class ItemSplitTarget
{
    public Dictionary<string, string>? ProviderIds { get; set; }
    public string? ContentId { get; set; }
    public bool? Unmatched { get; set; }
    public string? Title { get; set; }
    public int? Year { get; set; }
}

public sealed class ItemSplitRequest
{
    public List<int> FileIds { get; set; } = [];
    public ItemSplitTarget Target { get; set; } = new();
    public string HistoryMode { get; set; } = "evidence";
    public bool PersistOverride { get; set; } = true;
    public bool DryRun { get; set; }
}

public sealed class ItemSplitResponse
{
    public bool DryRun { get; set; }
    public string SourceContentId { get; set; } = "";
    public string TargetContentId { get; set; } = "";
    public bool TargetCreated { get; set; }
    public int FilesMoved { get; set; }
    public List<string> RootOverrides { get; set; } = [];
    public List<string> FileOverrides { get; set; } = [];
    public int EpisodePairs { get; set; }
    public ReattributionReport Reattribution { get; set; } = new();
}

public sealed class ReattributionReport
{
    public int PlaybackSessionLog { get; set; }
    public int Downloads { get; set; }
    public int ProgressMoved { get; set; }
    public int ProgressConflicts { get; set; }
    public int HistoryMoved { get; set; }
    public int HistoryStayed { get; set; }
    public int HistoryAmbiguous { get; set; }
    public int IntentMoved { get; set; }
    public int EpisodePairsMoved { get; set; }
}
