using System.Text.Json.Serialization;

namespace ContinuumPlayer.Core.Models.Catalog;

public class Library
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    [JsonPropertyName("poster_url")]
    public string? PosterUrl { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string> Paths { get; set; } = [];
    [JsonPropertyName("last_scanned_at")]
    public string? LastScannedAt { get; set; }
    [JsonPropertyName("scan_warning_code")]
    public string? ScanWarningCode { get; set; }
    [JsonPropertyName("scan_warning_message")]
    public string? ScanWarningMessage { get; set; }
    [JsonPropertyName("scan_warning_at")]
    public string? ScanWarningAt { get; set; }
}

public class LibraryMountCheckRoot
{
    public string Path { get; set; } = "";
    public bool Reachable { get; set; }
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }
}

public class LibraryMountCheckResponse
{
    public string Status { get; set; } = "";
    [JsonPropertyName("library_id")]
    public int LibraryId { get; set; }
    [JsonPropertyName("library_name")]
    public string LibraryName { get; set; } = "";
    public bool Healthy { get; set; }
    [JsonPropertyName("checked_at")]
    public string CheckedAt { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<LibraryMountCheckRoot> Roots { get; set; } = [];
}

public class LibrarySkippedRoot
{
    [JsonPropertyName("library_id")]
    public int LibraryId { get; set; }
    [JsonPropertyName("library_name")]
    public string LibraryName { get; set; } = "";
    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = "";
    public string Reason { get; set; } = "";
    [JsonPropertyName("sample_file_path")]
    public string SampleFilePath { get; set; } = "";
    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }
    [JsonPropertyName("first_seen_at")]
    public string FirstSeenAt { get; set; } = "";
    [JsonPropertyName("last_seen_at")]
    public string LastSeenAt { get; set; } = "";
}
