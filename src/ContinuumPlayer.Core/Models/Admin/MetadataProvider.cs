namespace ContinuumPlayer.Core.Models.Admin;

public class MetadataProvider
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string ProviderType { get; set; } = "";
    public bool Enabled { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new();
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class MetadataProvidersResponse
{
    public List<MetadataProvider> Providers { get; set; } = [];
}

public class CreateProviderRequest
{
    public string Slug { get; set; } = "";
    public string ProviderType { get; set; } = "";
    public Dictionary<string, string>? Settings { get; set; }
}
