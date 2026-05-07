namespace ContinuumPlayer.Core.Models;
public class ServerEntry
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastUsed { get; set; }
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
}
