namespace SiloPlayer.Core.Models.Admin;

public sealed class MetadataTranslationJob
{
    public int Id { get; set; }
    public string TargetKind { get; set; } = "";
    public string ContentId { get; set; } = "";
    public bool IncludeChildren { get; set; }
    public string SourceLanguage { get; set; } = "";
    public string TargetLanguage { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public string ProgressMessage { get; set; } = "";
    public int FieldsDone { get; set; }
    public int FieldsTotal { get; set; }
    public bool Force { get; set; }
    public string? ErrorMessage { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    public bool IsActive => Status is "pending" or "running";
}

public sealed class MetadataTranslationJobResponse
{
    public MetadataTranslationJob Job { get; set; } = new();
}

public sealed class MetadataTranslationJobsResponse
{
    public List<MetadataTranslationJob> Jobs { get; set; } = [];
}

public sealed class TranslateItemMetadataRequest
{
    public string TargetLanguage { get; set; } = "";
    public bool IncludeChildren { get; set; }
    public bool Force { get; set; }
}
