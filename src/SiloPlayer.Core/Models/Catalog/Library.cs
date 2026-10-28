using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Catalog;

public class Library
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }
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
    [JsonPropertyName("metadata_language")]
    public string? MetadataLanguage { get; set; }
    [JsonPropertyName("auto_translate_metadata")]
    public bool AutoTranslateMetadata { get; set; }
    [JsonPropertyName("chapter_thumbnails_enabled")]
    public bool? ChapterThumbnailsEnabled { get; set; }
    [JsonPropertyName("chapter_thumbnails_supported")]
    public bool? ChapterThumbnailsSupported { get; set; }
    [JsonPropertyName("intro_detection_enabled")]
    public bool? IntroDetectionEnabled { get; set; }
    [JsonPropertyName("trailer_kinds")]
    public List<string> TrailerKinds { get; set; } = [];
}
