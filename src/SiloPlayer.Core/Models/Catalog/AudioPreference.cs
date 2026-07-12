namespace SiloPlayer.Core.Models.Catalog;

public class AudioPreference
{
    public int AudioTrackIndex { get; set; }
    public string? AudioLanguage { get; set; }
    public AudioTrackSignature? TrackSignature { get; set; }
}

public class AudioPreferenceResponse
{
    public int AudioTrackIndex { get; set; }
    public string? AudioLanguage { get; set; }
    public AudioTrackSignature? TrackSignature { get; set; }
}

public class AudioTrackSignature
{
    public string? Language { get; set; }
    public string? Title { get; set; }
    public string? EmbeddedTitle { get; set; }
    public string? Codec { get; set; }
    public string? Layout { get; set; }
    public int Channels { get; set; }
    public bool Default { get; set; }
}
