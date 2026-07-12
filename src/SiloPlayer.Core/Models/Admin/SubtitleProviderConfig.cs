namespace SiloPlayer.Core.Models.Admin;

public class SubtitleProviderConfig
{
    public string ProviderName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool HasApiKey { get; set; }
    public bool HasCredentials { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public class SubtitleProvidersResponse
{
    public List<SubtitleProviderConfig> Providers { get; set; } = [];
}

public class SubtitleProviderUpdateRequest
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public class SubtitleProviderTestResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
