namespace ContinuumPlayer.Core.Models.Plugins;

public class PluginRoute
{
    public string Id { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Access { get; set; } = "";
    public bool Navigable { get; set; }
    public string NavigationLabel { get; set; } = "";
    public string NavigationKind { get; set; } = "";
    public bool StaticAsset { get; set; }
}
