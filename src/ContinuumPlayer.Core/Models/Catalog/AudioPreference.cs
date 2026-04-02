namespace ContinuumPlayer.Core.Models.Catalog;

public class AudioPreference
{
    public int AudioTrackIndex { get; set; }
    public string? AudioLanguage { get; set; }
}

public class AudioPreferenceResponse
{
    public int AudioTrackIndex { get; set; }
    public string? AudioLanguage { get; set; }
}
