namespace SiloPlayer.Core.Models.Downloads;

public class Download
{
    public string Id { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string? EpisodeId { get; set; }
    public string? BatchId { get; set; }
    public string? DeviceId { get; set; }
    public int MediaFileId { get; set; }
    public long FileSize { get; set; }
    public long BytesSent { get; set; }
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Quality { get; set; } = "original";
    public string EffectiveQuality { get; set; } = "original";
    public string DeliveryFormat { get; set; } = "original";
    public int TargetBitrateKbps { get; set; }
    public int Revision { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? CompletedAt { get; set; }
}

public class DownloadsResponse
{
    public List<Download> Downloads { get; set; } = [];
    public List<SkippedDownload> Skipped { get; set; } = [];
}

public class SkippedDownload
{
    public string? ContentId { get; set; }
    public string? EpisodeId { get; set; }
    public string? Reason { get; set; }
}

public class DownloadRequest
{
    public string ContentId { get; set; } = "";
    public string? EpisodeId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("media_file_id")]
    public int? FileId { get; set; }
    public int ExpectedRevision { get; set; }
    public string? ExpectedDownloadId { get; set; }
    public string? BatchId { get; set; }
    public string Quality { get; set; } = "original";
    public bool Series { get; set; }
    public int? SeasonNumber { get; set; }
}

public class DownloadCapability
{
    public bool Enabled { get; set; }
    public bool DownloadAllowed { get; set; }
    public List<string> QualityPresets { get; set; } = [];
    public bool TranscodeEnabled { get; set; }
    public bool TranscodeUserAllowed { get; set; }
    public bool SeasonDownload { get; set; }
    public bool SeriesMonitoring { get; set; }
    public List<string> MonitoringModes { get; set; } = [];
}
