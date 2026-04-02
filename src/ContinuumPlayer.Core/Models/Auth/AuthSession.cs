namespace ContinuumPlayer.Core.Models.Auth;

public class AuthSession
{
    public string Id { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public string? RevokedAt { get; set; }
    public bool IsCurrent { get; set; }
}

public class AuthSessionsResponse
{
    public List<AuthSession> Sessions { get; set; } = [];
}
