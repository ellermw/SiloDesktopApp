namespace ContinuumPlayer.Core.Models.Plugins;

public class PluginRepository
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public string? LastFetchedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public class CreatePluginRepositoryRequest
{
    public string Url { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool? Enabled { get; set; }
}

public class UpdatePluginRepositoryRequest
{
    public string? Url { get; set; }
    public string? DisplayName { get; set; }
    public bool? Enabled { get; set; }
}
