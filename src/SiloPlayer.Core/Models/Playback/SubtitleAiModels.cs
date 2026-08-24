namespace SiloPlayer.Core.Models.Playback;

public sealed class SubtitleAiStatus
{
    public bool Enabled { get; set; }
    public bool TranscribeEnabled { get; set; }
}

public sealed class SubtitleProviderStatus
{
    public int SchemaVersion { get; set; }
    public bool Enabled { get; set; }
    public List<string> Providers { get; set; } = [];
}

public sealed class SubtitleAiRequest
{
    public int MediaFileId { get; set; }
    public string? Kind { get; set; }
    public int SourceIndex { get; set; }
    public string SourceLanguage { get; set; } = "";
    public string TargetLanguage { get; set; } = "";
    public string SessionId { get; set; } = "";
    public double StartPosition { get; set; }
}

public sealed class SubtitleAiStartResponse
{
    public SubtitleAiJob Job { get; set; } = new();
}

public sealed class SubtitleAiJob
{
    public long Id { get; set; }
    public int MediaFileId { get; set; }
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public double Progress { get; set; }
    public string ProgressMessage { get; set; } = "";
    public int? ResultSubtitleId { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class SubtitleAiQuota
{
    public bool Limited { get; set; }
    public int Limit { get; set; }
    public int Used { get; set; }
    public int Remaining { get; set; }
    public string Period { get; set; } = "";
}
