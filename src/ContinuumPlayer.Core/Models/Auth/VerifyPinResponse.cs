namespace ContinuumPlayer.Core.Models.Auth;
public class VerifyPinResponse
{
    public bool Valid { get; set; }
    public string ProfileToken { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}
