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
    public string? LastProfileId { get; set; }
    public string? LastTheme { get; set; }
    public List<int> HiddenLibraryIds { get; set; } = [];
    public string? LastUserRole { get; set; }
    public string? LastUsername { get; set; }
}
