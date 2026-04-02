namespace ContinuumPlayer.Core.Models.Settings;

public class PluginUserSettings
{
    public int InstallationId { get; set; }
    public Dictionary<string, object> Config { get; set; } = new();
}
