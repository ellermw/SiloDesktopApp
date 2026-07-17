namespace SiloPlayer.Core.Models;
public class ServerEntry
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastUsed { get; set; }
}

public class AudiobookRatePreference
{
    public double Rate { get; set; } = 1;
    public DateTime LastUsedUtc { get; set; }
}

public class AppSettings
{
    public List<ServerEntry> Servers { get; set; } = [];
    public string? DeviceId { get; set; }
    public string? LastProfileId { get; set; }
    public string? LastTheme { get; set; }
    public List<int> HiddenLibraryIds { get; set; } = [];
    public string? LastUserRole { get; set; }
    public string? LastUsername { get; set; }

    /// <summary>
    /// Player volume on a 0-100 scale. Persisted so the player doesn't
    /// reset to full loudness on every launch.
    /// </summary>
    public double PlayerVolume { get; set; } = 100;

    /// <summary>Whether the player was last in a muted state.</summary>
    public bool PlayerMuted { get; set; } = false;

    public int AudiobookSkipBackSeconds { get; set; } = 10;
    public int AudiobookSkipForwardSeconds { get; set; } = 30;
    public bool AudiobookSmartRewind { get; set; } = true;
    public Dictionary<string, AudiobookRatePreference> AudiobookPlaybackRates { get; set; } = [];

    /// <summary>Opt-in HDMI/S/PDIF compressed-audio passthrough. Disabled by
    /// default because unsupported speakers and Bluetooth endpoints can be silent.</summary>
    public bool AudioBitstreamPassthrough { get; set; }
    public string CalendarPreset { get; set; } = "following";
    public List<string> TasteSeedDismissedProfileIds { get; set; } = [];
    public List<string> TasteSeedBannerDismissedProfileIds { get; set; } = [];
    public string UiDateFormat { get; set; } = "auto";
    public string UiTimeFormat { get; set; } = "auto";
    public string UiTextScale { get; set; } = "default";
    public string UiTextWeight { get; set; } = "default";
    public bool UiHighContrast { get; set; }
    public Dictionary<string, string> ThemeOverrides { get; set; } = [];
}
