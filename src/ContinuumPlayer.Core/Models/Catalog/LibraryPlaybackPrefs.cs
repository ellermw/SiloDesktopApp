namespace ContinuumPlayer.Core.Models.Catalog;

public class LibraryPlaybackPrefsResponse
{
    public List<LibraryPlaybackPreference> Preferences { get; set; } = [];
}

public class LibraryPlaybackPreference
{
    public int LibraryId { get; set; }
    public string? AudioLanguage { get; set; }
    public string? SubtitleLanguage { get; set; }
    public string? SubtitleMode { get; set; }
    public bool? ShowForcedSubtitles { get; set; }
}
