using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Auth;

public class AuthProvider
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Mode { get; set; } = "";
    public string? IconUrl { get; set; }
    public string? NativeStartPath { get; set; }
    public AuthProviderNetworkIdentity? NetworkIdentity { get; set; }
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int InstallationId { get; set; }
    [JsonPropertyName("default")]
    public bool IsDefault { get; set; }
}

public sealed class AuthProviderNetworkIdentity
{
    public string DisplayName { get; set; } = "";
    public string Username { get; set; } = "";
    [JsonIgnore]
    public string Name => !string.IsNullOrEmpty(DisplayName) ? DisplayName : Username;
}

public class AuthProvidersResponse
{
    [JsonPropertyName("items")]
    public List<AuthProvider> Providers { get; set; } = [];
    public bool PasswordLogin { get; set; } = true;
}

public class OAuthCompleteResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public string Next { get; set; } = "";
}

public sealed class ServerIdentity
{
    public string ServerId { get; set; } = "";
}
