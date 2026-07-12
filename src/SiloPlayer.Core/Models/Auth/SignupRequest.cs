namespace SiloPlayer.Core.Models.Auth;

public class SignupRequest
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string InviteCode { get; set; } = "";
    public bool CreateDefaultProfile { get; set; } = true;
    public string? DefaultProfileName { get; set; }
}

public class SignupStatusResponse
{
    public bool Enabled { get; set; }
}
