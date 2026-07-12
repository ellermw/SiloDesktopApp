namespace SiloPlayer.Core.Models.Admin;

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

// NOTE: GET /admin/providers returns a bare JSON array, not a wrapper object.
// Deserialized directly as List<MetadataProvider> in AdminApi.

public class CreateProviderRequest
{
    public string Slug { get; set; } = "";
    public string ProviderType { get; set; } = "";
    public Dictionary<string, string>? Settings { get; set; }
}
