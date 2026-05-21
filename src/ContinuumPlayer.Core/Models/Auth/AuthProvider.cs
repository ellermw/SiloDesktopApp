using System.Text.Json.Serialization;

namespace ContinuumPlayer.Core.Models.Auth;

public class AuthProvider
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Mode { get; set; } = "";
    public string? IconUrl { get; set; }
    public int InstallationId { get; set; }
    [JsonPropertyName("default")]
    public bool IsDefault { get; set; }
}

public class AuthProvidersResponse
{
    public List<AuthProvider> Providers { get; set; } = [];
}

public class OAuthCompleteResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public string Next { get; set; } = "";
}
