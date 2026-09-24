namespace SiloPlayer.Core.Models.Catalog;

public class LibraryPlaybackPrefsResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<LibraryPlaybackPreference> Preferences { get; set; } = [];
}

public class LibraryPlaybackPreference
{
    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public int LibraryId { get; set; }
    public string? AudioLanguage { get; set; }
    public string? SubtitleLanguage { get; set; }
    public string? SubtitleMode { get; set; }
    public bool? ShowForcedSubtitles { get; set; }
}
