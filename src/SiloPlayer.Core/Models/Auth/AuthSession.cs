namespace SiloPlayer.Core.Models.Auth;

public class AuthSession
{
    public string Id { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DevicePlatform { get; set; } = "";
    public string? LastSeenAt { get; set; }
    public string IpAddress { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public string? RevokedAt { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("current")]
    public bool IsCurrent { get; set; }
}

public class AuthSessionsResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<AuthSession> Sessions { get; set; } = [];
    public AuthSessionPage? Page { get; set; }
    public AuthSession? CurrentSession { get; set; }
}

public sealed class AuthSessionPage
{
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
}

public sealed class LoginSessionCapabilities
{
    public bool Available { get; set; }
}
