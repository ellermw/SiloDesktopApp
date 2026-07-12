namespace SiloPlayer.Core.Models.Downloads;

public class Download
{
    public int Id { get; set; }
    public int MediaFileId { get; set; }
    public string Status { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? CompletedAt { get; set; }
}

public class DownloadsResponse
{
    public List<Download> Downloads { get; set; } = [];
}

public class DownloadRequest
{
    public int MediaFileId { get; set; }
}
