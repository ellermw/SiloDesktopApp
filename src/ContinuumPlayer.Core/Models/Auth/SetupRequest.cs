namespace ContinuumPlayer.Core.Models.Auth;

public class SetupRequest
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class SetupStatusResponse
{
    public bool NeedsSetup { get; set; }
}
