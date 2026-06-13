namespace ContinuumPlayer.Core.Models.Auth;
public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Provider { get; set; }
}
