namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminDownloadedSubtitle
{
    public int Id { get; set; }
    public int MediaFileId { get; set; }
    public string? MediaContentId { get; set; }
    public string Provider { get; set; } = "";
    public string Language { get; set; } = "";
    public string Format { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public double Score { get; set; }
    public bool HearingImpaired { get; set; }
    public string CreatedAt { get; set; } = "";
    public int? DownloadedBy { get; set; }
    public string UploaderUsername { get; set; } = "";
    public string MediaTitle { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string FilePath { get; set; } = "";
}

public sealed class AdminDownloadedSubtitlesResponse
{
    public List<AdminDownloadedSubtitle> Subtitles { get; set; } = [];
    public int Total { get; set; }
    public int Uploads { get; set; }
    public int ProviderDownloads { get; set; }
}

public sealed class AdminDownloadedSubtitlesFilters
{
    public string? Provider { get; set; }
    public string? Language { get; set; }
    public int? UserId { get; set; }
    public int? MediaFileId { get; set; }
    public string? Query { get; set; }
    public int Limit { get; set; } = 25;
    public int Offset { get; set; }
}

public sealed class AdminUpdateDownloadedSubtitleRequest
{
    public string? Language { get; set; }
    public string? ReleaseName { get; set; }
    public bool? HearingImpaired { get; set; }
}

public sealed class AdminDownloadedSubtitleUpdateResponse
{
    public AdminDownloadedSubtitle Subtitle { get; set; } = new();
}
